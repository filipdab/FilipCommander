using System.Globalization;
using System.Text.RegularExpressions;

namespace FilipCommander;

/// <summary>Zmiana nazw wielu plików naraz (Ctrl+M): maski z [N], [E], [C]…, szukaj i zamień, wielkość liter, podgląd.</summary>
public sealed class MultiRenameForm : Form
{
    private readonly List<FileSystemInfo> _items;
    private readonly TextBox _nameMask;
    private readonly TextBox _extMask;
    private readonly TextBox _search;
    private readonly TextBox _replace;
    private readonly CheckBox _regex;
    private readonly ComboBox _case;
    private readonly NumericUpDown _start;
    private readonly NumericUpDown _step;
    private readonly NumericUpDown _digits;
    private readonly ListView _preview;
    private readonly Label _status;
    private readonly Button _run;
    private readonly Button _undo;
    private TextBox _lastMask;

    private readonly List<(FileSystemInfo Item, string NewName, string? Problem)> _plan = new();
    /// <summary>Ostatnia operacja do cofnięcia: (obecna ścieżka, poprzednia ścieżka).</summary>
    private List<(string Now, string Before)>? _undoList;

    /// <summary>Czy cokolwiek zmieniono (panel trzeba odświeżyć).</summary>
    public bool Changed { get; private set; }

    public MultiRenameForm(List<FileSystemInfo> items)
    {
        _items = items;
        Text = $"Zmiana nazw wielu plików — {items.Count} element(y/ów)";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(900, 620);
        MinimumSize = new Size(700, 450);
        ShowIcon = false;
        ShowInTaskbar = false;
        KeyPreview = true;

        var grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, Padding = new Padding(8, 8, 8, 0) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        _nameMask = new TextBox { Dock = DockStyle.Fill, Text = "[N]" };
        _extMask = new TextBox { Dock = DockStyle.Fill, Text = "[E]" };
        _search = new TextBox { Dock = DockStyle.Fill };
        _replace = new TextBox { Dock = DockStyle.Fill };
        _regex = new CheckBox { Text = "Wyrażenie regularne", AutoSize = true };
        _case = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
        _case.Items.AddRange(new object[] { "bez zmian", "małe litery", "WIELKIE LITERY", "Pierwsza litera wielka", "Każde Słowo Wielkie" });
        _case.SelectedIndex = 0;
        _start = new NumericUpDown { Minimum = 0, Maximum = 1_000_000, Value = 1, Width = 80 };
        _step = new NumericUpDown { Minimum = 1, Maximum = 1000, Value = 1, Width = 60 };
        _digits = new NumericUpDown { Minimum = 1, Maximum = 10, Value = 1, Width = 50 };
        _lastMask = _nameMask;
        _nameMask.Enter += (_, _) => _lastMask = _nameMask;
        _extMask.Enter += (_, _) => _lastMask = _extMask;

        var insert = new Button { Text = "Wstaw ▾", AutoSize = true };
        var tokens = new ContextMenuStrip();
        foreach (var (token, label) in new[]
                 {
                     ("[N]", "[N] nazwa bez rozszerzenia"), ("[N1-3]", "[N1-3] znaki 1–3 nazwy"), ("[N5-]", "[N5-] nazwa od 5. znaku"),
                     ("[E]", "[E] rozszerzenie"), ("[C]", "[C] licznik"), ("[P]", "[P] nazwa katalogu"),
                     ("[YMD]", "[YMD] data pliku (rrrrmmdd)"), ("[hms]", "[hms] godzina pliku (ggmmss)")
                 })
        {
            string t = token;
            tokens.Items.Add(label, null, (_, _) =>
            {
                _lastMask.SelectedText = t;
                _lastMask.Focus();
            });
        }
        insert.Click += (_, _) => tokens.Show(insert, new Point(0, insert.Height));

        var maskRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        grid.Controls.Add(Lbl("Maska nazwy:"), 0, 0);
        grid.Controls.Add(_nameMask, 1, 0);
        grid.Controls.Add(Lbl("Rozszerzenie:"), 2, 0);
        grid.Controls.Add(_extMask, 3, 0);
        grid.Controls.Add(Lbl("Szukaj:"), 0, 1);
        grid.Controls.Add(_search, 1, 1);
        grid.Controls.Add(Lbl("Zamień na:"), 2, 1);
        grid.Controls.Add(_replace, 3, 1);
        maskRow.Controls.AddRange(new Control[] { insert, _regex, Lbl("  Wielkość liter:"), _case });
        grid.Controls.Add(maskRow, 1, 2);
        grid.SetColumnSpan(maskRow, 3);
        var counterRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        counterRow.Controls.AddRange(new Control[] { Lbl("Licznik [C]: od"), _start, Lbl("krok"), _step, Lbl("cyfr"), _digits });
        grid.Controls.Add(counterRow, 1, 3);
        grid.SetColumnSpan(counterRow, 3);

        _preview = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false };
        _preview.Columns.Add("Stara nazwa", 330);
        _preview.Columns.Add("Nowa nazwa", 330);
        _preview.Columns.Add("Uwagi", 160);
        var previewHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 6, 8, 0) };
        previewHost.Controls.Add(_preview);

        _status = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
        _run = new Button { Text = "Zmień nazwy", Width = 120, Height = 28 };
        _undo = new Button { Text = "Cofnij", Width = 90, Height = 28, Enabled = false };
        var close = new Button { Text = "Zamknij", Width = 90, Height = 28, DialogResult = DialogResult.Cancel };
        _run.Click += (_, _) => Execute();
        _undo.Click += (_, _) => Undo();
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        buttons.Controls.AddRange(new Control[] { close, _undo, _run });
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(8, 6, 8, 6) };
        bottom.Controls.Add(_status);
        bottom.Controls.Add(buttons);

        Controls.Add(previewHost);
        Controls.Add(grid);
        Controls.Add(bottom);
        CancelButton = close;

        foreach (var c in new Control[] { _nameMask, _extMask, _search, _replace })
            c.TextChanged += (_, _) => UpdatePreview();
        _regex.CheckedChanged += (_, _) => UpdatePreview();
        _case.SelectedIndexChanged += (_, _) => UpdatePreview();
        foreach (var n in new[] { _start, _step, _digits })
            n.ValueChanged += (_, _) => UpdatePreview();

        Shown += (_, _) => { _nameMask.Focus(); _nameMask.SelectAll(); };
        Theme.Apply(this);
        UpdatePreview();
    }

    private static Label Lbl(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(3, 6, 3, 0) };

    // ───────────────────────── budowanie nazw ─────────────────────────

    private static readonly Regex Token = new(@"\[([^\[\]]+)\]", RegexOptions.Compiled);
    private static readonly Regex Range = new(@"^([NE])(\d+)(-(\d*))?$", RegexOptions.Compiled);

    private string BuildName(FileSystemInfo item, int index)
    {
        string name = item is FileInfo ? Path.GetFileNameWithoutExtension(item.Name) : item.Name;
        string ext = item is FileInfo ? Path.GetExtension(item.Name).TrimStart('.') : "";

        string Expand(string mask) => Token.Replace(mask, m =>
        {
            string t = m.Groups[1].Value;
            switch (t)
            {
                case "N": return name;
                case "E": return ext;
                case "C": return ((long)_start.Value + index * (long)_step.Value).ToString().PadLeft((int)_digits.Value, '0');
                case "P": return Path.GetFileName(Path.GetDirectoryName(item.FullName.TrimEnd('\\'))?.TrimEnd('\\') ?? "");
                case "YMD": return item.LastWriteTime.ToString("yyyyMMdd");
                case "hms": return item.LastWriteTime.ToString("HHmmss");
            }
            var r = Range.Match(t);
            if (!r.Success) return m.Value;
            string src = r.Groups[1].Value == "N" ? name : ext;
            int from = Math.Max(1, int.Parse(r.Groups[2].Value)) - 1;
            if (from >= src.Length) return "";
            if (!r.Groups[3].Success) return src[from].ToString();
            int to = r.Groups[4].Value.Length == 0 ? src.Length : Math.Min(src.Length, int.Parse(r.Groups[4].Value));
            return to > from ? src[from..to] : "";
        });

        string n = Expand(_nameMask.Text);
        string e = Expand(_extMask.Text);
        string result = e.Length > 0 ? n + "." + e : n;

        if (_search.Text.Length > 0)
            result = _regex.Checked
                ? Regex.Replace(result, _search.Text, _replace.Text)
                : result.Replace(_search.Text, _replace.Text, StringComparison.CurrentCultureIgnoreCase);

        var ti = CultureInfo.CurrentCulture.TextInfo;
        result = _case.SelectedIndex switch
        {
            1 => result.ToLower(),
            2 => result.ToUpper(),
            3 => result.Length > 0 ? char.ToUpper(result[0]) + result[1..].ToLower() : result,
            4 => ti.ToTitleCase(result.ToLower()),
            _ => result
        };
        return result.Trim();
    }

    private void UpdatePreview()
    {
        _plan.Clear();
        string? error = null;
        for (int i = 0; i < _items.Count; i++)
        {
            string newName;
            try
            {
                newName = BuildName(_items[i], i);
            }
            catch (ArgumentException ex)
            {
                error = "Błędne wyrażenie regularne: " + ex.Message;
                newName = _items[i].Name;
            }
            _plan.Add((_items[i], newName, null));
        }

        // Konflikty: dwie pozycje o tej samej nazwie albo nazwa zajęta przez plik spoza listy.
        var batch = new HashSet<string>(_items.Select(x => x.FullName), StringComparer.OrdinalIgnoreCase);
        var counts = _plan.GroupBy(p => Path.Combine(Dir(p.Item), p.NewName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        int problems = 0, changes = 0;
        for (int i = 0; i < _plan.Count; i++)
        {
            var (item, newName, _) = _plan[i];
            string target = Path.Combine(Dir(item), newName);
            string? problem = null;
            if (newName.Length == 0) problem = "pusta nazwa";
            else if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) problem = "niedozwolone znaki";
            else if (counts[target] > 1) problem = "powtórzona nazwa";
            else if (!batch.Contains(target) && (File.Exists(target) || Directory.Exists(target)) &&
                     !string.Equals(target, item.FullName, StringComparison.OrdinalIgnoreCase)) problem = "nazwa zajęta";
            if (problem != null) problems++;
            else if (newName != item.Name) changes++;
            _plan[i] = (item, newName, problem);
        }

        _preview.BeginUpdate();
        _preview.Items.Clear();
        foreach (var (item, newName, problem) in _plan)
        {
            var row = new ListViewItem(new[] { item.Name, newName, problem ?? (newName == item.Name ? "bez zmian" : "") });
            if (problem != null) row.ForeColor = Theme.Marked;
            else if (newName == item.Name) row.ForeColor = Theme.Gray;
            _preview.Items.Add(row);
        }
        _preview.EndUpdate();

        _status.Text = error ?? (problems > 0 ? $"Problemy: {problems} — popraw maskę." : $"Do zmiany: {changes} z {_items.Count}.");
        _status.ForeColor = error != null || problems > 0 ? Theme.Marked : Theme.Fore;
        _run.Enabled = error == null && problems == 0 && changes > 0;
    }

    private static string Dir(FileSystemInfo item) => Path.GetDirectoryName(item.FullName.TrimEnd('\\'))!;

    // ───────────────────────── wykonanie ─────────────────────────

    private void Execute()
    {
        var moves = _plan.Where(p => p.NewName != p.Item.Name)
            .Select(p => (From: p.Item.FullName, To: Path.Combine(Dir(p.Item), p.NewName)))
            .ToList();
        if (RunMoves(moves))
        {
            _undoList = moves.Select(m => (m.To, m.From)).ToList();
            _undo.Enabled = true;
            Changed = true;
            // Lista elementów wskazuje teraz nowe nazwy — można dalej zmieniać.
            for (int i = 0; i < _items.Count; i++)
            {
                string p = Path.Combine(Dir(_items[i]), _plan[i].NewName);
                _items[i] = Directory.Exists(p) ? new DirectoryInfo(p) : new FileInfo(p);
            }
            UpdatePreview();
            _status.Text = $"Zmieniono nazwy: {moves.Count}.";
        }
    }

    private void Undo()
    {
        if (_undoList == null) return;
        var moves = _undoList.Select(u => (From: u.Now, To: u.Before)).ToList();
        if (!RunMoves(moves)) return;
        var back = moves.ToDictionary(m => m.From, m => m.To, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < _items.Count; i++)
            if (back.TryGetValue(_items[i].FullName, out var before))
                _items[i] = Directory.Exists(before) ? new DirectoryInfo(before) : new FileInfo(before);
        _undoList = null;
        _undo.Enabled = false;
        UpdatePreview();
        _status.Text = $"Cofnięto zmiany: {moves.Count}.";
    }

    /// <summary>
    /// Zmienia nazwy w dwóch krokach (najpierw na tymczasowe), żeby zamiana a↔b albo
    /// zmiana samej wielkości liter się udawała. Przy błędzie przywraca stan sprzed operacji.
    /// </summary>
    private bool RunMoves(List<(string From, string To)> moves)
    {
        var done = new List<(string Now, string Original)>();
        try
        {
            var temps = new List<(string Temp, string To, string From)>();
            foreach (var (from, to) in moves)
            {
                string temp = Path.Combine(Path.GetDirectoryName(from)!, "~fcren" + Guid.NewGuid().ToString("N")[..8]);
                MovePath(from, temp);
                done.Add((temp, from));
                temps.Add((temp, to, from));
            }
            foreach (var (temp, to, from) in temps)
            {
                MovePath(temp, to);
                done[done.FindIndex(d => d.Now == temp)] = (to, from);
            }
            return true;
        }
        catch (Exception ex)
        {
            foreach (var (now, original) in Enumerable.Reverse(done))
                try { MovePath(now, original); } catch { }
            MessageBox.Show(this, "Nie udało się zmienić nazw (przywrócono poprzednie):\n" + ex.Message, "Zmiana nazw",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
    }

    private static void MovePath(string from, string to)
    {
        if (Directory.Exists(from)) Directory.Move(from, to);
        else File.Move(from, to);
    }
}
