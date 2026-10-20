using System.IO.Compression;
using System.Text;

namespace FilipCommander;

/// <summary>Postęp operacji: ile zrobiono, ile w sumie (np. bajtów) i bieżący plik.</summary>
public readonly record struct OpProgress(long Done, long Total, string Current);

/// <summary>Okno postępu z przyciskiem Anuluj dla długich operacji w tle.</summary>
public sealed class ProgressDialog : Form
{
    private readonly Label _label;
    private readonly ProgressBar _bar;
    private readonly CancellationTokenSource _cts = new();

    private ProgressDialog(string title)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ControlBox = false;
        ClientSize = new Size(480, 100);

        _label = new Label { Left = 12, Top = 12, Width = 456, Height = 20, AutoEllipsis = true };
        _bar = new ProgressBar { Left = 12, Top = 36, Width = 456, Height = 20, Maximum = 1000 };
        var cancel = new Button { Text = "Anuluj", Left = 388, Top = 66, Width = 80 };
        cancel.Click += (_, _) => { _cts.Cancel(); cancel.Enabled = false; _label.Text = "Przerywanie…"; };
        Controls.AddRange(new Control[] { _label, _bar, cancel });
        CancelButton = cancel;
    }

    /// <summary>
    /// Uruchamia <paramref name="work"/> w tle i pokazuje okno postępu.
    /// Zwraca false, jeśli użytkownik anulował.
    /// </summary>
    public static async Task<bool> RunAsync(IWin32Window owner, string title,
        Action<IProgress<OpProgress>, CancellationToken> work)
    {
        using var dlg = new ProgressDialog(title);
        var progress = new Progress<OpProgress>(p =>
        {
            if (dlg.IsDisposed || dlg._cts.IsCancellationRequested) return;
            dlg._label.Text = p.Current;
            dlg._bar.Value = p.Total <= 0 ? 0 : (int)Math.Clamp(p.Done * 1000 / p.Total, 0, 1000);
        });

        var task = Task.Run(() => work(progress, dlg._cts.Token));
        // Okno pokazujemy tylko, gdy operacja trwa dłużej niż chwilę.
        var finishedQuickly = await Task.WhenAny(task, Task.Delay(300)) == task;
        if (!finishedQuickly)
        {
            dlg.Shown += async (_, _) =>
            {
                try { await task; } catch { /* błąd obsłużymy niżej */ }
                dlg.Close();
            };
            dlg.ShowDialog(owner);
        }

        try
        {
            await task;
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _cts.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>Pakowanie i rozpakowywanie archiwów ZIP.</summary>
public static class ZipOps
{
    public static bool IsZip(FileSystemInfo f) =>
        f is FileInfo && f.Extension.Equals(".zip", StringComparison.OrdinalIgnoreCase);

    /// <summary>Pakuje elementy do archiwum. Ścieżki w archiwum są względne do <paramref name="baseDir"/>.</summary>
    public static void Pack(IReadOnlyList<FileSystemInfo> items, string baseDir, string zipPath,
        CompressionLevel level, IProgress<OpProgress> progress, CancellationToken ct)
    {
        string zipFull = Path.GetFullPath(zipPath);

        // Lista plików i pustych katalogów do spakowania.
        var files = new List<(string full, string entry)>();
        var emptyDirs = new List<string>();
        var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 };
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            if (item is FileInfo f)
            {
                files.Add((f.FullName, Relative(baseDir, f.FullName)));
                continue;
            }
            var dir = (DirectoryInfo)item;
            bool anything = false;
            foreach (var sub in dir.EnumerateFileSystemInfos("*", opts))
            {
                anything = true;
                if (sub is FileInfo sf) files.Add((sf.FullName, Relative(baseDir, sf.FullName)));
                else if (!((DirectoryInfo)sub).EnumerateFileSystemInfos().Any()) emptyDirs.Add(Relative(baseDir, sub.FullName) + "/");
            }
            if (!anything) emptyDirs.Add(Relative(baseDir, dir.FullName) + "/");
        }
        // Nie pakujemy archiwum do samego siebie.
        files.RemoveAll(x => string.Equals(Path.GetFullPath(x.full), zipFull, StringComparison.OrdinalIgnoreCase));

        long total = files.Sum(x => SafeLength(x.full));
        long done = 0;
        string tmp = zipFull + ".tmp";

        try
        {
            using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                foreach (var d in emptyDirs)
                    zip.CreateEntry(d);

                var buffer = new byte[256 * 1024];
                foreach (var (full, entryName) in files)
                {
                    ct.ThrowIfCancellationRequested();
                    progress.Report(new OpProgress(done, total, entryName));

                    var entry = zip.CreateEntry(entryName, level);
                    try { entry.LastWriteTime = File.GetLastWriteTime(full); } catch { /* daty spoza zakresu ZIP */ }

                    using var src = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var dst = entry.Open();
                    int n;
                    while ((n = src.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        dst.Write(buffer, 0, n);
                        done += n;
                        progress.Report(new OpProgress(done, total, entryName));
                    }
                }
            }
            File.Move(tmp, zipFull, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }

    /// <summary>
    /// Otwiera archiwum do odczytu z poprawnymi nazwami pozycji.
    /// .NET po podaniu kodowania stosuje je też do nazw oznaczonych jako UTF-8,
    /// więc archiwum czytamy dwa razy: nazwę bierzemy z UTF-8, a gdy nie jest
    /// poprawnym UTF-8 (stare archiwa, np. z Eksploratora) — z CP852.
    /// </summary>
    private sealed class ZipReader : IDisposable
    {
        private readonly ZipArchive _utf8;
        private readonly ZipArchive _legacy;
        public readonly List<(ZipArchiveEntry Entry, string Name)> Entries = new();

        public ZipReader(string path)
        {
            _utf8 = ZipFile.Open(path, ZipArchiveMode.Read, new UTF8Encoding(false));
            try
            {
                _legacy = ZipFile.Open(path, ZipArchiveMode.Read, Encoding.GetEncoding(852));
            }
            catch
            {
                _utf8.Dispose();
                throw;
            }
            var a = _utf8.Entries;
            var b = _legacy.Entries;
            for (int i = 0; i < a.Count; i++)
            {
                string name = a[i].FullName.Contains('\uFFFD') && i < b.Count ? b[i].FullName : a[i].FullName;
                Entries.Add((a[i], name));
            }
        }

        public static bool IsDirectory(string name) => name.EndsWith('/') || name.EndsWith('\\');

        public void Dispose()
        {
            _utf8.Dispose();
            _legacy.Dispose();
        }
    }

    /// <summary>Ile plików z archiwów już istnieje w katalogu docelowym.</summary>
    public static int CountConflicts(IEnumerable<string> zips, string targetDir)
    {
        int count = 0;
        foreach (var z in zips)
        {
            using var zip = new ZipReader(z);
            foreach (var (_, name) in zip.Entries)
                if (!ZipReader.IsDirectory(name) && TryTarget(targetDir, name, out var t) && File.Exists(t)) count++;
        }
        return count;
    }

    public static void Unpack(IReadOnlyList<string> zips, string targetDir, bool overwrite,
        IProgress<OpProgress> progress, CancellationToken ct)
    {
        targetDir = Path.GetFullPath(targetDir);
        Directory.CreateDirectory(targetDir);

        long total = 0, done = 0;
        foreach (var z in zips)
        {
            using var zip = new ZipReader(z);
            total += zip.Entries.Sum(e => e.Entry.Length);
        }

        var skipped = new List<string>();
        var buffer = new byte[256 * 1024];
        foreach (var z in zips)
        {
            using var zip = new ZipReader(z);
            foreach (var (entry, name) in zip.Entries)
            {
                ct.ThrowIfCancellationRequested();
                if (!TryTarget(targetDir, name, out var dest))
                {
                    skipped.Add(name); // ścieżka wychodząca poza katalog docelowy (zip slip)
                    continue;
                }

                if (ZipReader.IsDirectory(name))
                {
                    Directory.CreateDirectory(dest);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                if (File.Exists(dest) && !overwrite)
                {
                    done += entry.Length;
                    continue;
                }

                progress.Report(new OpProgress(done, total, name));
                string tmp = dest + ".fctmp";
                try
                {
                    using (var src = entry.Open())
                    using (var dst = new FileStream(tmp, FileMode.Create, FileAccess.Write))
                    {
                        int n;
                        while ((n = src.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            ct.ThrowIfCancellationRequested();
                            dst.Write(buffer, 0, n);
                            done += n;
                            progress.Report(new OpProgress(done, total, name));
                        }
                    }
                    if (File.Exists(dest)) File.SetAttributes(dest, FileAttributes.Normal);
                    File.Move(tmp, dest, overwrite: true);
                    try { File.SetLastWriteTime(dest, entry.LastWriteTime.DateTime); } catch { }
                }
                finally
                {
                    try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                }
            }
        }

        if (skipped.Count > 0)
            throw new InvalidDataException(
                $"Pominięto {skipped.Count} pozycji z niebezpieczną ścieżką (wychodzącą poza katalog docelowy), np.:\n{skipped[0]}");
    }

    /// <summary>Lista zawartości archiwum do podglądu F3.</summary>
    public static string Listing(string zipPath)
    {
        using var zip = new ZipReader(zipPath);
        var sb = new StringBuilder();
        sb.AppendLine($"Archiwum: {zipPath}");
        sb.AppendLine($"Pozycji: {zip.Entries.Count}");
        sb.AppendLine();
        sb.AppendLine($"{"Rozmiar",14} {"Spakowany",14}  {"Data",-16}  Nazwa");
        sb.AppendLine(new string('-', 80));
        long size = 0, packed = 0;
        foreach (var (e, name) in zip.Entries.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            size += e.Length;
            packed += e.CompressedLength;
            bool dir = ZipReader.IsDirectory(name);
            string sz = dir ? "<DIR>" : e.Length.ToString("N0");
            string c = dir ? "" : e.CompressedLength.ToString("N0");
            sb.AppendLine($"{sz,14} {c,14}  {e.LastWriteTime:yyyy-MM-dd HH:mm}  {name}");
        }
        sb.AppendLine(new string('-', 80));
        string ratio = size > 0 ? $"{100.0 * packed / size:0.0}%" : "-";
        sb.AppendLine($"{size,14:N0} {packed,14:N0}  stopień kompresji: {ratio}");
        return sb.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n");
    }

    private static bool TryTarget(string targetDir, string entryName, out string dest)
    {
        char sep = Path.DirectorySeparatorChar;
        string root = Path.GetFullPath(targetDir).TrimEnd(sep) + sep;
        dest = Path.GetFullPath(Path.Combine(root, entryName.Replace('/', sep).Replace('\\', sep)));
        return dest.StartsWith(root, StringComparison.OrdinalIgnoreCase) || dest.Equals(root.TrimEnd(sep), StringComparison.OrdinalIgnoreCase);
    }

    private static string Relative(string baseDir, string full) =>
        Path.GetRelativePath(baseDir, full).Replace(Path.DirectorySeparatorChar, '/');

    private static long SafeLength(string path)
    {
        try { return new FileInfo(path).Length; } catch { return 0; }
    }
}
