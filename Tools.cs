using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace FilipCommander;

/// <summary>Sumy kontrolne MD5/SHA: obliczanie (format sha256sum: „hash *nazwa”) i sprawdzanie plików sum.</summary>
public static class Checksums
{
    public static readonly string[] Algorithms = { "MD5", "SHA-1", "SHA-256", "SHA-512" };
    private static readonly string[] Extensions = { ".md5", ".sha1", ".sha256", ".sha512" };

    public static bool IsChecksumFile(FileSystemInfo f) =>
        f is FileInfo && Extensions.Contains(f.Extension, StringComparer.OrdinalIgnoreCase);

    private static HashAlgorithm Create(string name) => name switch
    {
        "MD5" => MD5.Create(),
        "SHA-1" => SHA1.Create(),
        "SHA-512" => SHA512.Create(),
        _ => SHA256.Create()
    };

    private static string Hash(string algorithm, string path, Action<long> onBytes, CancellationToken ct)
    {
        using var h = Create(algorithm);
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var buffer = new byte[1 << 20];
        int n;
        while ((n = fs.Read(buffer, 0, buffer.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            h.TransformBlock(buffer, 0, n, null, 0);
            onBytes(n);
        }
        h.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(h.Hash!).ToLowerInvariant();
    }

    /// <summary>Oblicza sumy zaznaczonych plików (katalogi — rekurencyjnie) i pokazuje je z możliwością zapisu.</summary>
    public static async void CreateAsync(IWin32Window owner, IReadOnlyList<FileSystemInfo> items, string baseDir)
    {
        string algorithm = AskAlgorithm(owner);
        if (algorithm.Length == 0) return;

        var files = new List<string>();
        var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 };
        foreach (var item in items)
            if (item is FileInfo f) files.Add(f.FullName);
            else files.AddRange(((DirectoryInfo)item).EnumerateFiles("*", opts).Select(x => x.FullName));

        var sb = new StringBuilder();
        try
        {
            bool done = await ProgressDialog.RunAsync(owner, "Sumy kontrolne", (progress, ct) =>
            {
                long total = files.Sum(f => new FileInfo(f).Length), processed = 0;
                foreach (var f in files)
                {
                    string rel = Path.GetRelativePath(baseDir, f);
                    string hash = Hash(algorithm, f, n => { processed += n; progress.Report(new OpProgress(processed, total, rel)); }, ct);
                    sb.Append(hash).Append(" *").Append(rel).Append('\n');
                }
            });
            if (!done) return;
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, "Nie udało się obliczyć sum:\n" + ex.Message, "Sumy kontrolne", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string ext = Extensions[Array.IndexOf(Algorithms, algorithm)];
        string defaultName = (items.Count == 1 ? items[0].Name : new DirectoryInfo(baseDir).Name) + ext;
        TextDialog.ShowReport(owner, $"Sumy kontrolne ({algorithm})", sb.ToString(), (form, text) =>
        {
            using var dlg = new SaveFileDialog { InitialDirectory = baseDir, FileName = defaultName, Filter = $"Plik sum (*{ext})|*{ext}|Wszystkie pliki|*.*" };
            if (dlg.ShowDialog(form) != DialogResult.OK) return;
            try { File.WriteAllText(dlg.FileName, text, new UTF8Encoding(false)); }
            catch (Exception ex) { MessageBox.Show(form, ex.Message, "Zapis", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        });
    }

    private static string AskAlgorithm(IWin32Window owner)
    {
        using var form = new Form
        {
            Text = "Sumy kontrolne",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(300, 100)
        };
        var label = new Label { Text = "Algorytm:", Left = 12, Top = 16, AutoSize = true };
        var combo = new ComboBox { Left = 90, Top = 12, Width = 196, DropDownStyle = ComboBoxStyle.DropDownList };
        combo.Items.AddRange(Algorithms);
        combo.SelectedItem = "SHA-256";
        var ok = new Button { Text = "Oblicz", DialogResult = DialogResult.OK, Left = 120, Top = 60, Width = 80 };
        var cancel = new Button { Text = "Anuluj", DialogResult = DialogResult.Cancel, Left = 206, Top = 60, Width = 80 };
        form.Controls.AddRange(new Control[] { label, combo, ok, cancel });
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        Theme.Apply(form);
        return form.ShowDialog(owner) == DialogResult.OK ? (string)combo.SelectedItem! : "";
    }

    /// <summary>Sprawdza pliki wymienione w pliku sum (.md5/.sha1/.sha256/.sha512).</summary>
    public static async void VerifyAsync(IWin32Window owner, string sumFile)
    {
        string baseDir = Path.GetDirectoryName(sumFile)!;
        var entries = new List<(string Hash, string Name)>();
        foreach (var raw in File.ReadAllLines(sumFile))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';')) continue;
            int space = line.IndexOf(' ');
            if (space <= 0) continue;
            string name = line[(space + 1)..].TrimStart(' ', '*');
            entries.Add((line[..space].ToLowerInvariant(), name));
        }
        if (entries.Count == 0)
        {
            MessageBox.Show(owner, "Plik nie zawiera sum kontrolnych.", "Sumy kontrolne", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var report = new StringBuilder();
        int ok = 0, bad = 0, missing = 0;
        try
        {
            bool done = await ProgressDialog.RunAsync(owner, "Sprawdzanie sum", (progress, ct) =>
            {
                long total = entries.Sum(e => File.Exists(Path.Combine(baseDir, e.Name)) ? new FileInfo(Path.Combine(baseDir, e.Name)).Length : 0);
                long processed = 0;
                foreach (var (hash, name) in entries)
                {
                    string path = Path.Combine(baseDir, name);
                    if (!File.Exists(path)) { missing++; report.Append("BRAK   ").Append(name).Append('\n'); continue; }
                    // Algorytm poznajemy po długości sumy.
                    string algorithm = hash.Length switch { 32 => "MD5", 40 => "SHA-1", 128 => "SHA-512", _ => "SHA-256" };
                    string actual = Hash(algorithm, path, n => { processed += n; progress.Report(new OpProgress(processed, total, name)); }, ct);
                    if (actual == hash) { ok++; report.Append("OK     "); }
                    else { bad++; report.Append("BŁĄD   "); }
                    report.Append(name).Append('\n');
                }
            });
            if (!done) return;
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, "Błąd sprawdzania:\n" + ex.Message, "Sumy kontrolne", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        string summary = $"Zgodne: {ok}    Błędne: {bad}    Brakujące: {missing}\n\n";
        TextDialog.ShowReport(owner, $"Sprawdzanie sum — {Path.GetFileName(sumFile)}", summary + report);
    }
}

/// <summary>Dzielenie dużego pliku na części (.001, .002…) i łączenie ich z powrotem.</summary>
public static class SplitJoin
{
    public static bool IsFirstPart(FileSystemInfo f) => f is FileInfo && f.Name.EndsWith(".001", StringComparison.Ordinal);

    /// <summary>„100M”, „1,44M”, „4G”, „700 MB”, „512K” → bajty.</summary>
    public static long ParseSize(string text)
    {
        string t = text.Trim().ToUpperInvariant().Replace(" ", "").TrimEnd('B').Replace(',', '.');
        long mult = 1;
        if (t.EndsWith('K')) { mult = 1L << 10; t = t[..^1]; }
        else if (t.EndsWith('M')) { mult = 1L << 20; t = t[..^1]; }
        else if (t.EndsWith('G')) { mult = 1L << 30; t = t[..^1]; }
        if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || v <= 0)
            throw new FormatException($"Nieprawidłowy rozmiar: „{text}”.");
        return Math.Max(1, (long)(v * mult));
    }

    public static async void SplitAsync(IWin32Window owner, FileInfo file, string targetDir)
    {
        var answer = PromptDialog.Show(owner, "Podziel plik",
            $"Rozmiar części dla „{file.Name}” ({FilePanel.FormatSize(file.Length)}), np. 100M, 700M, 4G, 1,44M:", "100M");
        if (string.IsNullOrEmpty(answer)) return;
        long part;
        try { part = ParseSize(answer); }
        catch (FormatException ex) { MessageBox.Show(owner, ex.Message, "Podziel plik", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        long count = (file.Length + part - 1) / part;
        if (count > 999)
        {
            MessageBox.Show(owner, $"Wyszłoby {count} części — maksimum to 999. Podaj większy rozmiar.", "Podziel plik", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (count < 2)
        {
            MessageBox.Show(owner, "Plik jest mniejszy niż jedna część — nie ma czego dzielić.", "Podziel plik", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        string target = PromptDialog.Show(owner, "Podziel plik", $"Zapisz {count} części do katalogu:", targetDir.TrimEnd('\\') + "\\") ?? "";
        if (target.Length == 0) return;
        target = Environment.ExpandEnvironmentVariables(target.Trim('"'));

        try
        {
            Directory.CreateDirectory(target);
            await ProgressDialog.RunAsync(owner, "Dzielenie pliku", (progress, ct) =>
            {
                var buffer = new byte[1 << 20];
                long done = 0;
                using var src = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
                for (int i = 1; i <= count; i++)
                {
                    string name = Path.Combine(target, $"{file.Name}.{i:000}");
                    using var dst = new FileStream(name, FileMode.Create, FileAccess.Write);
                    long left = Math.Min(part, file.Length - done);
                    while (left > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        int n = src.Read(buffer, 0, (int)Math.Min(buffer.Length, left));
                        if (n == 0) break;
                        dst.Write(buffer, 0, n);
                        left -= n;
                        done += n;
                        progress.Report(new OpProgress(done, file.Length, Path.GetFileName(name)));
                    }
                }
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, "Nie udało się podzielić pliku:\n" + ex.Message, "Podziel plik", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    public static async void JoinAsync(IWin32Window owner, FileInfo first, string targetDir)
    {
        string baseName = first.FullName[..^4];
        var parts = new List<string>();
        for (int i = 1; File.Exists($"{baseName}.{i:000}"); i++) parts.Add($"{baseName}.{i:000}");
        long total = parts.Sum(p => new FileInfo(p).Length);

        var target = PromptDialog.Show(owner, "Połącz pliki", $"Połącz {parts.Count} części ({FilePanel.FormatSize(total)}) w plik:",
            Path.Combine(targetDir, Path.GetFileName(baseName)), selectNameOnly: false);
        if (string.IsNullOrEmpty(target)) return;
        target = Environment.ExpandEnvironmentVariables(target.Trim('"'));
        if (File.Exists(target) &&
            MessageBox.Show(owner, $"Plik już istnieje:\n{target}\n\nZastąpić go?", "Połącz pliki", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        string tmp = target + ".fctmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
            bool done = await ProgressDialog.RunAsync(owner, "Łączenie plików", (progress, ct) =>
            {
                var buffer = new byte[1 << 20];
                long processed = 0;
                using (var dst = new FileStream(tmp, FileMode.Create, FileAccess.Write))
                    foreach (var p in parts)
                    {
                        using var src = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.Read);
                        int n;
                        while ((n = src.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            ct.ThrowIfCancellationRequested();
                            dst.Write(buffer, 0, n);
                            processed += n;
                            progress.Report(new OpProgress(processed, total, Path.GetFileName(p)));
                        }
                    }
                File.Move(tmp, target, overwrite: true);
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, "Nie udało się połączyć plików:\n" + ex.Message, "Połącz pliki", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }
}

/// <summary>Zmiana atrybutów i dat zaznaczonych plików/katalogów (opcjonalnie z zawartością podkatalogów).</summary>
public sealed class AttributesForm : Form
{
    private static readonly (FileAttributes Flag, string Label)[] Flags =
    {
        (FileAttributes.ReadOnly, "Tylko do odczytu (r)"),
        (FileAttributes.Archive, "Archiwalny (a)"),
        (FileAttributes.Hidden, "Ukryty (h)"),
        (FileAttributes.System, "Systemowy (s)")
    };

    private readonly List<FileSystemInfo> _items;
    private readonly CheckBox[] _flags;
    private readonly CheckBox _setModified, _setCreated, _recursive;
    private readonly DateTimePicker _modified, _created;

    public AttributesForm(List<FileSystemInfo> items)
    {
        _items = items;
        Text = items.Count == 1 ? $"Atrybuty — {items[0].Name}" : $"Atrybuty — {items.Count} elementy(ów)";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ShowIcon = false;
        ClientSize = new Size(420, 330);

        var flagsBox = new GroupBox { Text = "Atrybuty (szare pole — bez zmian)", Left = 12, Top = 10, Width = 396, Height = 130 };
        _flags = new CheckBox[Flags.Length];
        for (int i = 0; i < Flags.Length; i++)
        {
            int count = items.Count(it => it.Attributes.HasFlag(Flags[i].Flag));
            _flags[i] = new CheckBox
            {
                Text = Flags[i].Label,
                Left = 14,
                Top = 24 + i * 25,
                AutoSize = true,
                ThreeState = true,
                CheckState = count == 0 ? CheckState.Unchecked : count == items.Count ? CheckState.Checked : CheckState.Indeterminate
            };
            flagsBox.Controls.Add(_flags[i]);
        }

        var first = items[0];
        _setModified = new CheckBox { Text = "Data modyfikacji:", Left = 14, Top = 154, AutoSize = true };
        _modified = new DateTimePicker { Left = 170, Top = 150, Width = 180, Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd HH:mm:ss", Value = first.LastWriteTime };
        _setCreated = new CheckBox { Text = "Data utworzenia:", Left = 14, Top = 186, AutoSize = true };
        _created = new DateTimePicker { Left = 170, Top = 182, Width = 180, Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd HH:mm:ss", Value = first.CreationTime };
        _modified.ValueChanged += (_, _) => _setModified.Checked = true;
        _created.ValueChanged += (_, _) => _setCreated.Checked = true;
        var now = new Button { Text = "Teraz", Left = 356, Top = 150, Width = 52, Height = _modified.Height };
        now.Click += (_, _) => { _modified.Value = DateTime.Now; _created.Value = DateTime.Now; };

        _recursive = new CheckBox
        {
            Text = "Także zawartość podkatalogów",
            Left = 14,
            Top = 222,
            AutoSize = true,
            Enabled = items.Any(i => i is DirectoryInfo)
        };

        var ok = new Button { Text = "OK", Left = 242, Top = 286, Width = 80 };
        var cancel = new Button { Text = "Anuluj", DialogResult = DialogResult.Cancel, Left = 328, Top = 286, Width = 80 };
        ok.Click += (_, _) => { if (Apply()) { DialogResult = DialogResult.OK; Close(); } };
        Controls.AddRange(new Control[] { flagsBox, _setModified, _modified, _setCreated, _created, now, _recursive, ok, cancel });
        AcceptButton = ok;
        CancelButton = cancel;
        Theme.Apply(this);
    }

    private bool Apply()
    {
        FileAttributes set = 0, clear = 0;
        for (int i = 0; i < Flags.Length; i++)
        {
            if (_flags[i].CheckState == CheckState.Checked) set |= Flags[i].Flag;
            else if (_flags[i].CheckState == CheckState.Unchecked) clear |= Flags[i].Flag;
        }

        var targets = new List<FileSystemInfo>(_items);
        if (_recursive.Checked)
        {
            var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 };
            foreach (var d in _items.OfType<DirectoryInfo>())
                targets.AddRange(d.EnumerateFileSystemInfos("*", opts));
        }

        var errors = new List<string>();
        foreach (var t in targets)
        {
            try
            {
                var attrs = (t.Attributes & ~clear) | set;
                // Daty zmieniamy przy zdjętym „tylko do odczytu”, inaczej system odmówi.
                if (_setModified.Checked || _setCreated.Checked)
                {
                    t.Attributes = t.Attributes & ~FileAttributes.ReadOnly;
                    if (_setModified.Checked) t.LastWriteTime = _modified.Value;
                    if (_setCreated.Checked) t.CreationTime = _created.Value;
                }
                t.Attributes = attrs;
            }
            catch (Exception ex)
            {
                errors.Add($"{t.FullName}: {ex.Message}");
            }
        }
        if (errors.Count > 0)
        {
            TextDialog.ShowReport(this, "Nie wszystko się udało", string.Join("\n", errors));
            return false;
        }
        return true;
    }
}
