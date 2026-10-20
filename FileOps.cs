using Microsoft.VisualBasic.FileIO;

namespace FilipCommander;

/// <summary>
/// Operacje na plikach przez powłokę Windows: systemowe okno postępu,
/// pytania o nadpisanie i kosz — tak jak w Eksploratorze.
/// </summary>
public static class FileOps
{
    public enum Kind { Copy, Move }

    /// <summary>Uruchamia akcję w osobnym wątku STA (wymagane przez okna powłoki), nie blokując UI.</summary>
    private static Task RunSta(Action action)
    {
        var tcs = new TaskCompletionSource();
        var thread = new Thread(() =>
        {
            try { action(); tcs.SetResult(); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        return tcs.Task;
    }

    /// <summary>
    /// Kopiuje/przenosi elementy. <paramref name="target"/> może być katalogiem
    /// albo (dla pojedynczego elementu) nową nazwą/ścieżką.
    /// </summary>
    public static Task TransferAsync(Kind kind, IReadOnlyList<FileSystemInfo> items, string target)
    {
        var plan = BuildPlan(items, target);
        return RunSta(() =>
        {
            foreach (var (src, dst) in plan)
            {
                if (src is DirectoryInfo)
                {
                    if (kind == Kind.Copy)
                        FileSystem.CopyDirectory(src.FullName, dst, UIOption.AllDialogs, UICancelOption.DoNothing);
                    else
                        FileSystem.MoveDirectory(src.FullName, dst, UIOption.AllDialogs, UICancelOption.DoNothing);
                }
                else
                {
                    if (kind == Kind.Copy)
                        FileSystem.CopyFile(src.FullName, dst, UIOption.AllDialogs, UICancelOption.DoNothing);
                    else
                        FileSystem.MoveFile(src.FullName, dst, UIOption.AllDialogs, UICancelOption.DoNothing);
                }
            }
        });
    }

    private static List<(FileSystemInfo src, string dst)> BuildPlan(IReadOnlyList<FileSystemInfo> items, string target)
    {
        target = Environment.ExpandEnvironmentVariables(target.Trim().Trim('"'));
        if (target.Length == 0) throw new InvalidOperationException("Nie podano miejsca docelowego.");

        bool endsWithSep = target.EndsWith('\\') || target.EndsWith('/');
        string full = Path.GetFullPath(target);
        bool intoDirectory = endsWithSep || Directory.Exists(full) || items.Count > 1;

        if (intoDirectory) Directory.CreateDirectory(full);

        var plan = new List<(FileSystemInfo, string)>();
        foreach (var item in items)
        {
            string dst = intoDirectory ? Path.Combine(full, item.Name) : full;
            string srcFull = Path.GetFullPath(item.FullName).TrimEnd('\\');
            string dstFull = Path.GetFullPath(dst).TrimEnd('\\');

            if (string.Equals(srcFull, dstFull, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Nie można skopiować/przenieść „{item.Name}” na samego siebie.");
            if (item is DirectoryInfo && dstFull.StartsWith(srcFull + "\\", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Nie można skopiować/przenieść katalogu „{item.Name}” do jego własnego podkatalogu.");

            plan.Add((item, dst));
        }
        return plan;
    }

    public static Task DeleteAsync(IReadOnlyList<FileSystemInfo> items, bool permanently)
    {
        var recycle = permanently ? RecycleOption.DeletePermanently : RecycleOption.SendToRecycleBin;
        return RunSta(() =>
        {
            foreach (var item in items)
            {
                if (item is DirectoryInfo)
                    FileSystem.DeleteDirectory(item.FullName, UIOption.OnlyErrorDialogs, recycle, UICancelOption.DoNothing);
                else
                    FileSystem.DeleteFile(item.FullName, UIOption.OnlyErrorDialogs, recycle, UICancelOption.DoNothing);
            }
        });
    }

    public static void Rename(FileSystemInfo item, string newName)
    {
        if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidOperationException("Nazwa zawiera niedozwolone znaki.");

        string dir = Path.GetDirectoryName(item.FullName.TrimEnd('\\'))!;
        string dst = Path.Combine(dir, newName);

        // Zmiana tylko wielkości liter wymaga kroku pośredniego.
        if (string.Equals(item.Name, newName, StringComparison.OrdinalIgnoreCase))
        {
            string tmp = Path.Combine(dir, Guid.NewGuid().ToString("N"));
            Move(item, tmp);
            Move(new FileInfo(tmp).Exists ? new FileInfo(tmp) : new DirectoryInfo(tmp), dst);
        }
        else
        {
            Move(item, dst);
        }
    }

    private static void Move(FileSystemInfo item, string dst)
    {
        if (item is DirectoryInfo) Directory.Move(item.FullName, dst);
        else File.Move(item.FullName, dst);
    }
}
