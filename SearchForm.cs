using System.Diagnostics;
using System.IO.Enumeration;
using System.Text;

namespace FilipCommander;

/// <summary>Wyszukiwanie plików (Alt+F7): po masce nazwy, tekście w środku, rozmiarze i dacie.</summary>
public sealed class SearchForm : Form
{
    private readonly TextBox _mask;
    private readonly TextBox _startDir;
    private readonly TextBox _text;
    private readonly CheckBox _subdirs;
    private readonly CheckBox _caseSensitive;
    private readonly CheckBox _includeDirs;
    private readonly CheckBox _includeHidden;
    private readonly NumericUpDown _minSize;
    private readonly NumericUpDown _maxSize;
    private readonly DateTimePicker _newerThan;
    private readonly Button _start;
    private readonly ListView _results;
    private readonly Label _status;

    private CancellationTokenSource? _cts;
    private readonly Action<string> _goTo;

    /// <param name="startDir">Katalog startowy (bieżący katalog panelu).</param>
    /// <param name="goTo">Akcja „przejdź do pliku” w aktywnym panelu.</param>
    public SearchForm(string startDir, Action<string> goTo)
    {
        _goTo = goTo;
        Text = "Szukaj plików";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(820, 620);
        MinimumSize = new Size(640, 450);
        ShowIcon = false;
        KeyPreview = true;

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 3,
            Padding = new Padding(8, 8, 8, 0)
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _mask = new TextBox { Dock = DockStyle.Fill, Text = "*" };
        _startDir = new TextBox { Dock = DockStyle.Fill, Text = startDir };
        _text = new TextBox { Dock = DockStyle.Fill, PlaceholderText = "(puste — szukaj tylko po nazwie)" };
        var browse = new Button { Text = "…", Width = 32, Height = _startDir.PreferredHeight };
        browse.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { SelectedPath = _startDir.Text };
            if (dlg.ShowDialog(this) == DialogResult.OK) _startDir.Text = dlg.SelectedPath;
        };

        AddRow(grid, "Szukaj plików:", _mask, new Label { Text = "np. *.txt;*.log", AutoSize = true, Anchor = AnchorStyles.Left });
        AddRow(grid, "Szukaj w:", _startDir, browse);
        AddRow(grid, "Zawierające tekst:", _text, null);

        _subdirs = new CheckBox { Text = "Podkatalogi", Checked = true, AutoSize = true };
        _caseSensitive = new CheckBox { Text = "Rozróżniaj wielkość liter", AutoSize = true };
        _includeDirs = new CheckBox { Text = "Także katalogi", AutoSize = true };
        _includeHidden = new CheckBox { Text = "Ukryte i systemowe", AutoSize = true };
        var checks = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        checks.Controls.AddRange(new Control[] { _subdirs, _caseSensitive, _includeDirs, _includeHidden });
        grid.Controls.Add(checks, 1, grid.RowCount++);

        _minSize = new NumericUpDown { Maximum = 1_000_000_000, Width = 90, ThousandsSeparator = true };
        _maxSize = new NumericUpDown { Maximum = 1_000_000_000, Width = 90, ThousandsSeparator = true };
        _newerThan = new DateTimePicker { Format = DateTimePickerFormat.Short, ShowCheckBox = true, Checked = false, Width = 130 };
        var filters = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        filters.Controls.AddRange(new Control[]
        {
            Lbl("Rozmiar od"), _minSize, Lbl("do"), _maxSize, Lbl("KB (0 = bez limitu)    Zmienione po"), _newerThan
        });
        grid.Controls.Add(filters, 1, grid.RowCount++);

        _start = new Button { Text = "Szukaj", Width = 100, Height = 28 };
        _start.Click += (_, _) => { if (_cts == null) StartSearch(); else _cts.Cancel(); };
        var goButton = new Button { Text = "Przejdź do pliku", Width = 130, Height = 28 };
        goButton.Click += (_, _) => GoToSelected();
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        buttons.Controls.AddRange(new Control[] { _start, goButton });
        grid.Controls.Add(buttons, 1, grid.RowCount++);

        _results = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false
        };
        _results.Columns.Add("Nazwa", 220);
        _results.Columns.Add("Katalog", 340);
        _results.Columns.Add("Rozmiar", 90, HorizontalAlignment.Right);
        _results.Columns.Add("Data", 120);
        _results.DoubleClick += (_, _) => GoToSelected();
        _results.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { GoToSelected(); e.SuppressKeyPress = true; }
            if (e.KeyCode == Keys.F3 && SelectedPath is { } p && File.Exists(p)) { new ViewerForm(p).Show(this); e.SuppressKeyPress = true; }
        };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Przejdź do pliku", null, (_, _) => GoToSelected());
        menu.Items.Add("Otwórz", null, (_, _) => OpenSelected());
        menu.Items.Add("Podgląd (F3)", null, (_, _) => { if (SelectedPath is { } p && File.Exists(p)) new ViewerForm(p).Show(this); });
        menu.Items.Add("Kopiuj ścieżkę", null, (_, _) => { if (SelectedPath is { } p) Clipboard.SetText(p); });
        _results.ContextMenuStrip = menu;

        _status = new Label { Dock = DockStyle.Bottom, Height = 22, AutoEllipsis = true, Padding = new Padding(8, 0, 0, 0), TextAlign = ContentAlignment.MiddleLeft };

        var resultsHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 4, 8, 0) };
        resultsHost.Controls.Add(_results);

        Controls.Add(resultsHost);
        Controls.Add(grid);
        Controls.Add(_status);
        AcceptButton = _start;
        Shown += (_, _) => { _mask.Focus(); _mask.SelectAll(); };
        FormClosing += (_, _) => _cts?.Cancel();
        Theme.Apply(this);
    }

    private static Label Lbl(string text) =>
        new() { Text = text, AutoSize = true, Margin = new Padding(3, 6, 3, 0) };

    private static void AddRow(TableLayoutPanel grid, string label, Control main, Control? extra)
    {
        int row = grid.RowCount++;
        grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 6, 3) }, 0, row);
        grid.Controls.Add(main, 1, row);
        if (extra != null) grid.Controls.Add(extra, 2, row);
    }

    private string? SelectedPath => _results.SelectedItems.Count > 0 ? _results.SelectedItems[0].Tag as string : null;

    private void GoToSelected()
    {
        if (SelectedPath is { } p) _goTo(p);
    }

    private void OpenSelected()
    {
        if (SelectedPath is not { } p) return;
        try { Process.Start(new ProcessStartInfo(p) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Szukaj", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            if (_cts != null) _cts.Cancel();
            else Close();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ───────────────────────── szukanie ─────────────────────────

    private sealed record Criteria(
        string Root, string[] Masks, string Text, bool Recurse, bool CaseSensitive,
        bool IncludeDirs, bool IncludeHidden, long MinBytes, long MaxBytes, DateTime? NewerThan);

    private async void StartSearch()
    {
        string root = Environment.ExpandEnvironmentVariables(_startDir.Text.Trim().Trim('"'));
        if (!Directory.Exists(root))
        {
            MessageBox.Show(this, "Katalog nie istnieje:\n" + root, "Szukaj", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var masks = _mask.Text.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                               .Select(m => m == "*.*" ? "*" : m)
                               .Select(m => m.Contains('*') || m.Contains('?') ? m : "*" + m + "*") // samo słowo = fragment nazwy
                               .ToArray();
        if (masks.Length == 0) masks = new[] { "*" };

        var c = new Criteria(root, masks, _text.Text, _subdirs.Checked, _caseSensitive.Checked,
            _includeDirs.Checked && _text.Text.Length == 0, _includeHidden.Checked,
            (long)_minSize.Value * 1024, (long)_maxSize.Value * 1024,
            _newerThan.Checked ? _newerThan.Value.Date : null);

        _results.Items.Clear();
        _cts = new CancellationTokenSource();
        _start.Text = "Zatrzymaj";
        var sw = Stopwatch.StartNew();
        int found = 0;

        // Wyniki spływają paczkami, żeby nie zalać UI przy tysiącach trafień.
        var pending = new List<FileSystemInfo>();
        string currentDir = "";
        var uiTimer = new System.Windows.Forms.Timer { Interval = 150 };
        uiTimer.Tick += (_, _) => Flush();
        uiTimer.Start();

        void Flush()
        {
            List<FileSystemInfo> batch;
            lock (pending) { batch = new List<FileSystemInfo>(pending); pending.Clear(); }
            if (batch.Count > 0)
            {
                _results.BeginUpdate();
                foreach (var f in batch) _results.Items.Add(CreateItem(f));
                _results.EndUpdate();
                found += batch.Count;
            }
            _status.Text = $"Znaleziono: {found}    Przeszukuję: {Volatile.Read(ref currentDir)}";
        }

        bool cancelled = false;
        try
        {
            var token = _cts.Token;
            await Task.Run(() => Search(c, f => { lock (pending) pending.Add(f); }, d => Volatile.Write(ref currentDir, d), token), token);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        finally
        {
            uiTimer.Stop();
            uiTimer.Dispose();
            if (!IsDisposed)
            {
                Flush();
                _status.Text = $"{(cancelled ? "Przerwano. " : "Gotowe. ")}Znaleziono: {found}  ({sw.Elapsed.TotalSeconds:0.0} s)";
                _start.Text = "Szukaj";
                if (_results.Items.Count > 0 && _results.SelectedItems.Count == 0)
                {
                    _results.Items[0].Selected = true;
                    _results.Items[0].Focused = true;
                }
            }
            _cts.Dispose();
            _cts = null;
        }
    }

    private static void Search(Criteria c, Action<FileSystemInfo> onFound, Action<string> onDir, CancellationToken ct)
    {
        var skip = c.IncludeHidden ? 0 : FileAttributes.Hidden | FileAttributes.System;
        var opts = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = skip, RecurseSubdirectories = false };

        var stack = new Stack<string>();
        stack.Push(c.Root);
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            string dir = stack.Pop();
            onDir(dir);

            IEnumerable<FileSystemInfo> entries;
            try { entries = new DirectoryInfo(dir).EnumerateFileSystemInfos("*", opts).ToList(); }
            catch { continue; }

            var subdirs = new List<string>();
            foreach (var e in entries)
            {
                ct.ThrowIfCancellationRequested();
                bool nameMatch = c.Masks.Any(m => FileSystemName.MatchesSimpleExpression(m, e.Name, ignoreCase: true));

                if (e is DirectoryInfo d)
                {
                    // Nie wchodzimy w dowiązania/junction — unikamy pętli.
                    if (c.Recurse && !d.Attributes.HasFlag(FileAttributes.ReparsePoint)) subdirs.Add(d.FullName);
                    if (c.IncludeDirs && nameMatch && (c.NewerThan is null || d.LastWriteTime >= c.NewerThan))
                        onFound(d);
                    continue;
                }

                var f = (FileInfo)e;
                if (!nameMatch) continue;
                long len;
                try { len = f.Length; } catch { continue; }
                if (c.MinBytes > 0 && len < c.MinBytes) continue;
                if (c.MaxBytes > 0 && len > c.MaxBytes) continue;
                if (c.NewerThan is { } since && f.LastWriteTime < since) continue;
                if (c.Text.Length > 0 && !FileContains(f.FullName, c.Text, c.CaseSensitive, ct)) continue;
                onFound(f);
            }
            // Odwrotnie, żeby katalogi szły alfabetycznie.
            for (int i = subdirs.Count - 1; i >= 0; i--) stack.Push(subdirs[i]);
        }
    }

    /// <summary>Szuka tekstu w pliku (UTF-8, UTF-16 i Windows-1250), strumieniowo.</summary>
    private static bool FileContains(string path, string text, bool caseSensitive, CancellationToken ct)
    {
        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var encodings = new[] { new UTF8Encoding(false), Encoding.GetEncoding(1250), Encoding.Unicode };

        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16);
            const int chunk = 1 << 20;
            int overlap = text.Length * 4 + 4; // zapas na znaki wielobajtowe na granicy bloków
            var buffer = new byte[chunk + overlap];
            int carried = 0;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                int read = fs.Read(buffer, carried, chunk);
                if (read == 0) break;
                int len = carried + read;
                foreach (var enc in encodings)
                {
                    string s = enc.GetString(buffer, 0, len);
                    if (s.Contains(text, comparison)) return true;
                    if (enc == Encoding.Unicode && len > 1)
                    {
                        // UTF-16 przesunięte o jeden bajt
                        string s2 = enc.GetString(buffer, 1, len - 1);
                        if (s2.Contains(text, comparison)) return true;
                    }
                }
                carried = Math.Min(overlap, len);
                Buffer.BlockCopy(buffer, len - carried, buffer, 0, carried);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { /* plik zablokowany lub brak dostępu */ }
        return false;
    }

    private static ListViewItem CreateItem(FileSystemInfo f)
    {
        var item = new ListViewItem(f.Name) { Tag = f.FullName };
        item.SubItems.Add(Path.GetDirectoryName(f.FullName) ?? "");
        item.SubItems.Add(f is FileInfo fi ? SafeSize(fi) : "<DIR>");
        DateTime t;
        try { t = f.LastWriteTime; } catch { t = default; }
        item.SubItems.Add(t.ToString("yyyy-MM-dd HH:mm"));
        return item;
    }

    private static string SafeSize(FileInfo f)
    {
        try { return f.Length.ToString("N0"); } catch { return ""; }
    }
}
