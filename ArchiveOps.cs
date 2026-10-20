using System.Text;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using SharpCompress.Writers;

namespace FilipCommander;

/// <summary>
/// Archiwa inne niż ZIP (7z, RAR, TAR, TAR.GZ/BZ2/XZ, GZ) przez bibliotekę SharpCompress.
/// ZIP dalej obsługuje <see cref="ZipOps"/> (poprawne polskie nazwy w starych archiwach).
/// </summary>
public static class ArchiveOps
{
    private static readonly string[] Extensions =
        { ".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".bz2", ".tbz2", ".xz", ".txz" };

    public static bool IsArchive(string path) =>
        Extensions.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    public static bool IsArchive(FileSystemInfo f) => f is FileInfo && IsArchive(f.FullName);

    private static bool IsZip(string path) => path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

    private static bool Is7z(string path) => path.EndsWith(".7z", StringComparison.OrdinalIgnoreCase);

    /// <summary>Czy rozszerzenie oznacza archiwum TAR (zwykłe albo skompresowane), które umiemy utworzyć.</summary>
    public static bool IsTarTarget(string path, out CompressionType compression)
    {
        string p = path.ToLowerInvariant();
        compression = p.EndsWith(".tar.gz") || p.EndsWith(".tgz") ? CompressionType.GZip
            : p.EndsWith(".tar.bz2") || p.EndsWith(".tbz2") ? CompressionType.BZip2
            : CompressionType.None;
        return compression != CompressionType.None || p.EndsWith(".tar");
    }

    // ───────────────────────── rozpakowywanie ─────────────────────────

    /// <summary>
    /// Przechodzi po wszystkich pozycjach archiwum. <paramref name="onEntry"/> dostaje nazwę,
    /// czy to katalog, datę i czytnik (do zapisania treści). Postęp liczony jest po bajtach archiwum.
    /// </summary>
    private static void ForEachEntry(string archivePath, Action<string, bool, DateTime?, IReader> onEntry,
        Action<long, long>? progress, CancellationToken ct)
    {
        using var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var counted = new ProgressStream(fs, pos => progress?.Invoke(pos, fs.Length), ct);
        // 7z nie da się czytać strumieniowo — wymaga otwarcia jako archiwum.
        IArchive? archive = Is7z(archivePath) ? ArchiveFactory.OpenArchive(counted) : null;
        try
        {
            using var reader = archive != null ? archive.ExtractAllEntries() : ReaderFactory.OpenReader(counted);
            while (reader.MoveToNextEntry())
            {
                ct.ThrowIfCancellationRequested();
                var e = reader.Entry;
                // Pojedynczy .gz nie ma nazwy w środku — bierzemy nazwę archiwum bez rozszerzenia.
                string name = string.IsNullOrEmpty(e.Key) ? Path.GetFileNameWithoutExtension(archivePath) : e.Key;
                onEntry(name, e.IsDirectory, e.LastModifiedTime, reader);
            }
        }
        finally
        {
            archive?.Dispose();
        }
    }

    public static void Unpack(IReadOnlyList<string> archives, string targetDir, bool overwrite,
        IProgress<OpProgress> progress, CancellationToken ct)
    {
        var zips = archives.Where(IsZip).ToList();
        if (zips.Count > 0) ZipOps.Unpack(zips, targetDir, overwrite, progress, ct);

        targetDir = Path.GetFullPath(targetDir);
        Directory.CreateDirectory(targetDir);
        var skipped = new List<string>();
        foreach (var archive in archives.Where(a => !IsZip(a)))
        {
            string shortName = Path.GetFileName(archive);
            string current = shortName;
            ForEachEntry(archive, (name, isDir, date, reader) =>
            {
                current = name;
                if (!ZipOps.TryTarget(targetDir, name, out var dest))
                {
                    skipped.Add(name);
                    return;
                }
                if (isDir)
                {
                    Directory.CreateDirectory(dest);
                    return;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                if (File.Exists(dest) && !overwrite) return;

                string tmp = dest + ".fctmp";
                try
                {
                    using (var dst = new FileStream(tmp, FileMode.Create, FileAccess.Write))
                        reader.WriteEntryTo(dst);
                    if (File.Exists(dest)) File.SetAttributes(dest, FileAttributes.Normal);
                    File.Move(tmp, dest, overwrite: true);
                    if (date is { } d) try { File.SetLastWriteTime(dest, d); } catch { }
                }
                finally
                {
                    try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                }
            }, (done, total) => progress.Report(new OpProgress(done, total, $"{shortName}: {current}")), ct);
        }

        if (skipped.Count > 0)
            throw new InvalidDataException(
                $"Pominięto {skipped.Count} pozycji z niebezpieczną ścieżką (wychodzącą poza katalog docelowy), np.:\n{skipped[0]}");
    }

    /// <summary>Ile plików już istnieje w celu. Dla ZIP liczymy dokładnie; inne archiwa trzeba by rozpakować, więc tylko sprawdzamy, czy katalog nie jest pusty.</summary>
    public static int CountConflicts(IReadOnlyList<string> archives, string targetDir, out bool unknown)
    {
        unknown = false;
        if (!Directory.Exists(targetDir)) return 0;
        int count = ZipOps.CountConflicts(archives.Where(IsZip), targetDir);
        if (archives.Any(a => !IsZip(a)) && Directory.EnumerateFileSystemEntries(targetDir).Any()) unknown = true;
        return count;
    }

    // ───────────────────────── podgląd ─────────────────────────

    public static string Listing(string path)
    {
        if (IsZip(path)) return ZipOps.Listing(path);

        var rows = new List<(string name, bool dir, long size, DateTime? date)>();
        if (Is7z(path) || path.EndsWith(".rar", StringComparison.OrdinalIgnoreCase))
        {
            // Spis z nagłówków — bez rozpakowywania.
            using var archive = ArchiveFactory.OpenArchive(path);
            foreach (var e in archive.Entries)
                rows.Add((e.Key ?? "", e.IsDirectory, e.Size, e.LastModifiedTime));
        }
        else
        {
            ForEachEntry(path, (name, dir, date, reader) => rows.Add((name, dir, reader.Entry.Size, date)), null, CancellationToken.None);
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Archiwum: {path}");
        sb.AppendLine($"Pozycji: {rows.Count}");
        sb.AppendLine();
        sb.AppendLine($"{"Rozmiar",14}  {"Data",-16}  Nazwa");
        sb.AppendLine(new string('-', 80));
        long total = 0;
        foreach (var (name, dir, size, date) in rows.OrderBy(r => r.name, StringComparer.OrdinalIgnoreCase))
        {
            total += dir ? 0 : size;
            string sz = dir ? "<DIR>" : size.ToString("N0");
            string d = date?.ToString("yyyy-MM-dd HH:mm") ?? "";
            sb.AppendLine($"{sz,14}  {d,-16}  {name}");
        }
        sb.AppendLine(new string('-', 80));
        sb.AppendLine($"{total,14:N0}  razem (rozmiar archiwum: {new FileInfo(path).Length:N0})");
        return sb.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n");
    }

    // ───────────────────────── pakowanie TAR ─────────────────────────

    public static void PackTar(IReadOnlyList<FileSystemInfo> items, string baseDir, string tarPath, CompressionType compression,
        IProgress<OpProgress> progress, CancellationToken ct)
    {
        string full = Path.GetFullPath(tarPath);
        var files = new List<(string full, string entry)>();
        var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 };
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            if (item is FileInfo f) files.Add((f.FullName, Relative(baseDir, f.FullName)));
            else
                foreach (var sub in ((DirectoryInfo)item).EnumerateFiles("*", opts))
                    files.Add((sub.FullName, Relative(baseDir, sub.FullName)));
        }
        files.RemoveAll(x => string.Equals(Path.GetFullPath(x.full), full, StringComparison.OrdinalIgnoreCase));

        long total = files.Sum(x => { try { return new FileInfo(x.full).Length; } catch { return 0L; } });
        long done = 0;
        string tmp = full + ".tmp";
        try
        {
            using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write))
            using (var writer = WriterFactory.OpenWriter(stream, ArchiveType.Tar, new WriterOptions(compression) { LeaveStreamOpen = false }))
            {
                foreach (var (path, entryName) in files)
                {
                    ct.ThrowIfCancellationRequested();
                    progress.Report(new OpProgress(done, total, entryName));
                    long start = done;
                    using var src = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var counted = new ProgressStream(src, pos =>
                    {
                        done = start + pos;
                        progress.Report(new OpProgress(done, total, entryName));
                    }, ct);
                    writer.Write(entryName, counted, File.GetLastWriteTime(path));
                    done = start + src.Length;
                }
            }
            File.Move(tmp, full, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }

    private static string Relative(string baseDir, string full) =>
        Path.GetRelativePath(baseDir, full).Replace(Path.DirectorySeparatorChar, '/');
}

/// <summary>Strumień tylko do odczytu, który zgłasza pozycję po każdym odczycie i pozwala przerwać operację.</summary>
internal sealed class ProgressStream : Stream
{
    private readonly Stream _inner;
    private readonly Action<long> _onPosition;
    private readonly CancellationToken _ct;

    public ProgressStream(Stream inner, Action<long> onPosition, CancellationToken ct)
    {
        _inner = inner;
        _onPosition = onPosition;
        _ct = ct;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        _ct.ThrowIfCancellationRequested();
        int n = _inner.Read(buffer, offset, count);
        _onPosition(_inner.Position);
        return n;
    }

    public override bool CanRead => true;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => _inner.Length;
    public override long Position { get => _inner.Position; set => _inner.Position = value; }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
