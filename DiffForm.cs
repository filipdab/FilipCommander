namespace FilipCommander;

/// <summary>Porównanie dwóch plików tekstowych linia po linii (pliki binarne — bajt po bajcie).</summary>
public sealed class DiffForm : Form
{
    private const int MaxBytes = 64 * 1024 * 1024;
    private const int MaxDifferences = 4000;

    private enum Kind { Same, Changed, Removed, Added }
    private readonly record struct Row(Kind Kind, int Left, int Right);

    private readonly string[] _a, _b;
    private readonly ListView _view;
    private readonly Label _status;
    private readonly CheckBox _onlyDiffs;
    private readonly CheckBox _ignoreSpace;
    private readonly CheckBox _ignoreCase;
    private List<Row> _rows = new();
    private List<Row> _shown = new();

    /// <summary>Porównuje pliki: tekstowe w oknie, binarne komunikatem.</summary>
    public static void Compare(IWin32Window owner, string left, string right)
    {
        try
        {
            if (new FileInfo(left).Length > MaxBytes || new FileInfo(right).Length > MaxBytes ||
                IsBinary(left) || IsBinary(right))
            {
                MessageBox.Show(owner, CompareBinary(left, right), "Porównanie plików", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            new DiffForm(left, right).Show(owner);
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, ex.Message, "Porównanie plików", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static bool IsBinary(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var buf = new byte[8192];
        int n = fs.Read(buf, 0, buf.Length);
        return ViewerControl.LooksBinary(buf, n);
    }

    private static string CompareBinary(string left, string right)
    {
        using var a = new FileStream(left, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var b = new FileStream(right, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var ba = new byte[1 << 16];
        var bb = new byte[1 << 16];
        long offset = 0;
        while (true)
        {
            int na = a.ReadAtLeast(ba, ba.Length, throwOnEndOfStream: false);
            int nb = b.ReadAtLeast(bb, bb.Length, throwOnEndOfStream: false);
            int n = Math.Min(na, nb);
            for (int i = 0; i < n; i++)
                if (ba[i] != bb[i])
                    return $"Pliki się różnią.\n\nPierwsza różnica na pozycji {offset + i:N0} (0x{offset + i:X}).\nRozmiary: {a.Length:N0} i {b.Length:N0} bajtów.";
            if (na != nb)
                return $"Pliki są identyczne do pozycji {offset + n:N0}, ale mają różne długości: {a.Length:N0} i {b.Length:N0} bajtów.";
            if (na == 0) return $"Pliki są identyczne ({a.Length:N0} bajtów).";
            offset += n;
        }
    }

    private static string[] ReadLines(string path)
    {
        var data = File.ReadAllBytes(path);
        var text = ViewerControl.DecodeText(data, data.Length);
        var lines = text.Split("\r\n");
        return lines.Length > 0 && lines[^1].Length == 0 ? lines[..^1] : lines;
    }

    private DiffForm(string left, string right)
    {
        _a = ReadLines(left);
        _b = ReadLines(right);
        Text = $"Porównanie — {Path.GetFileName(left)} ↔ {Path.GetFileName(right)}";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1200, 750);
        ShowIcon = false;
        KeyPreview = true;

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4), WrapContents = false };
        var prev = new Button { Text = "◀ Poprzednia (Shift+F8)", AutoSize = true };
        var next = new Button { Text = "Następna (F8) ▶", AutoSize = true };
        prev.Click += (_, _) => JumpDiff(-1);
        next.Click += (_, _) => JumpDiff(+1);
        _onlyDiffs = new CheckBox { Text = "Tylko różnice", AutoSize = true, Margin = new Padding(12, 6, 3, 0) };
        _ignoreSpace = new CheckBox { Text = "Ignoruj białe znaki", AutoSize = true, Margin = new Padding(12, 6, 3, 0) };
        _ignoreCase = new CheckBox { Text = "Ignoruj wielkość liter", AutoSize = true, Margin = new Padding(12, 6, 3, 0) };
        _onlyDiffs.CheckedChanged += (_, _) => ShowRows();
        _ignoreSpace.CheckedChanged += (_, _) => Recompute();
        _ignoreCase.CheckedChanged += (_, _) => Recompute();
        bar.Controls.AddRange(new Control[] { prev, next, _onlyDiffs, _ignoreSpace, _ignoreCase });

        _view = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            VirtualMode = true,
            Font = new Font("Consolas", 9.5f)
        };
        _view.Columns.Add("#", 55, HorizontalAlignment.Right);
        _view.Columns.Add(left, 500);
        _view.Columns.Add("#", 55, HorizontalAlignment.Right);
        _view.Columns.Add(right, 500);
        _view.RetrieveVirtualItem += (_, e) => e.Item = CreateItem(_shown[e.ItemIndex]);
        _view.Resize += (_, _) => FitColumns();

        _status = new Label { Dock = DockStyle.Bottom, Height = 22, Padding = new Padding(6, 0, 0, 0), TextAlign = ContentAlignment.MiddleLeft };

        Controls.Add(_view);
        Controls.Add(bar);
        Controls.Add(_status);
        Theme.Apply(this);
        Recompute();
        Shown += (_, _) => { FitColumns(); _view.Focus(); JumpDiff(+1); };
    }

    private void FitColumns()
    {
        int w = Math.Max(200, (_view.ClientSize.Width - 110 - SystemInformation.VerticalScrollBarWidth) / 2);
        _view.Columns[1].Width = w;
        _view.Columns[3].Width = w;
    }

    private ListViewItem CreateItem(Row r)
    {
        var item = new ListViewItem(new[]
        {
            r.Left >= 0 ? (r.Left + 1).ToString() : "",
            r.Left >= 0 ? _a[r.Left].Replace("\t", "    ") : "",
            r.Right >= 0 ? (r.Right + 1).ToString() : "",
            r.Right >= 0 ? _b[r.Right].Replace("\t", "    ") : ""
        });
        item.BackColor = r.Kind switch
        {
            Kind.Changed => Theme.DiffChanged,
            Kind.Removed => Theme.DiffRemoved,
            Kind.Added => Theme.DiffAdded,
            _ => Theme.Window
        };
        item.ForeColor = Theme.WindowText;
        return item;
    }

    private void Recompute()
    {
        // Linie zamieniamy na liczby — porównywanie liczb jest dużo szybsze niż tekstów.
        var ids = new Dictionary<string, int>();
        int[] Map(string[] lines) => lines.Select(l =>
        {
            string key = _ignoreSpace.Checked ? string.Join(' ', l.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)) : l;
            if (_ignoreCase.Checked) key = key.ToLowerInvariant();
            if (!ids.TryGetValue(key, out int id)) ids[key] = id = ids.Count;
            return id;
        }).ToArray();

        int[] a = Map(_a), b = Map(_b);
        try
        {
            _rows = BuildRows(a, b);
        }
        catch (InvalidOperationException ex)
        {
            _rows = new List<Row>();
            _status.Text = ex.Message;
            ShowRows();
            return;
        }
        int blocks = 0;
        for (int i = 0; i < _rows.Count; i++)
            if (_rows[i].Kind != Kind.Same && (i == 0 || _rows[i - 1].Kind == Kind.Same)) blocks++;
        _status.Text = blocks == 0
            ? $"Pliki są identyczne ({_a.Length} wierszy)."
            : $"Bloków różnic: {blocks}    wierszy: {_a.Length} ↔ {_b.Length}    (żółte — zmienione, czerwone — usunięte, zielone — dodane)";
        ShowRows();
    }

    private void ShowRows()
    {
        _shown = _onlyDiffs.Checked ? _rows.Where(r => r.Kind != Kind.Same).ToList() : _rows;
        _view.VirtualListSize = _shown.Count;
        _view.Invalidate();
    }

    private void JumpDiff(int step)
    {
        if (_shown.Count == 0) return;
        int i = _view.SelectedIndices.Count > 0 ? _view.SelectedIndices[0] : -1;
        // Przeskakujemy bieżący blok, potem szukamy początku następnego.
        if (step > 0)
        {
            while (i >= 0 && i < _shown.Count && _shown[i].Kind != Kind.Same) i++;
            while (++i < _shown.Count && _shown[i].Kind == Kind.Same) { }
            if (i >= _shown.Count) return;
        }
        else
        {
            while (i > 0 && _shown[i].Kind != Kind.Same) i--;
            while (--i >= 0 && _shown[i].Kind == Kind.Same) { }
            if (i < 0) return;
            while (i > 0 && _shown[i - 1].Kind != Kind.Same) i--;
        }
        _view.SelectedIndices.Clear();
        _view.SelectedIndices.Add(i);
        _view.FocusedItem = _view.Items[i];
        _view.EnsureVisible(Math.Min(_shown.Count - 1, i + 10));
        _view.EnsureVisible(i);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Escape: Close(); return true;
            case Keys.F8: JumpDiff(+1); return true;
            case Keys.Shift | Keys.F8: JumpDiff(-1); return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ───────────────────────── algorytm (Myers, O(ND)) ─────────────────────────

    private static List<Row> BuildRows(int[] a, int[] b)
    {
        // Wspólny początek i koniec pomijamy w algorytmie.
        int pre = 0;
        while (pre < a.Length && pre < b.Length && a[pre] == b[pre]) pre++;
        int suf = 0;
        while (suf < a.Length - pre && suf < b.Length - pre && a[^(suf + 1)] == b[^(suf + 1)]) suf++;

        var rows = new List<Row>(Math.Max(a.Length, b.Length) + 16);
        for (int i = 0; i < pre; i++) rows.Add(new Row(Kind.Same, i, i));

        var ops = Myers(a[pre..(a.Length - suf)], b[pre..(b.Length - suf)]);
        var dels = new List<int>();
        var ins = new List<int>();
        void Flush()
        {
            int n = Math.Min(dels.Count, ins.Count);
            for (int i = 0; i < n; i++) rows.Add(new Row(Kind.Changed, dels[i], ins[i]));
            for (int i = n; i < dels.Count; i++) rows.Add(new Row(Kind.Removed, dels[i], -1));
            for (int i = n; i < ins.Count; i++) rows.Add(new Row(Kind.Added, -1, ins[i]));
            dels.Clear();
            ins.Clear();
        }
        foreach (var (op, i, j) in ops)
        {
            if (op == '=') { Flush(); rows.Add(new Row(Kind.Same, i + pre, j + pre)); }
            else if (op == '-') dels.Add(i + pre);
            else ins.Add(j + pre);
        }
        Flush();

        for (int k = suf; k > 0; k--) rows.Add(new Row(Kind.Same, a.Length - k, b.Length - k));
        return rows;
    }

    /// <summary>Lista operacji: '=' (wiersz w obu), '-' (tylko lewy), '+' (tylko prawy).</summary>
    private static List<(char Op, int I, int J)> Myers(int[] a, int[] b)
    {
        int n = a.Length, m = b.Length, max = n + m, off = max + 1;
        var v = new int[2 * max + 3];
        var trace = new List<int[]>();
        int found = -1;
        for (int d = 0; d <= max && found < 0; d++)
        {
            if (d > MaxDifferences)
                throw new InvalidOperationException($"Pliki różnią się zbyt mocno (ponad {MaxDifferences} zmienionych wierszy), żeby je zestawić.");
            // Stan przed rundą d dla k ∈ [-(d+1), d+1] — potrzebny do odtworzenia ścieżki.
            var snap = new int[2 * d + 3];
            Array.Copy(v, off - d - 1, snap, 0, 2 * d + 3);
            trace.Add(snap);
            for (int k = -d; k <= d; k += 2)
            {
                int x = k == -d || (k != d && v[off + k - 1] < v[off + k + 1]) ? v[off + k + 1] : v[off + k - 1] + 1;
                int y = x - k;
                while (x < n && y < m && a[x] == b[y]) { x++; y++; }
                v[off + k] = x;
                if (x >= n && y >= m) { found = d; break; }
            }
        }

        var ops = new List<(char, int, int)>();
        int cx = n, cy = m;
        for (int d = found; d >= 0; d--)
        {
            var s = trace[d];
            int k = cx - cy;
            int prevK = k == -d || (k != d && s[k - 1 + d + 1] < s[k + 1 + d + 1]) ? k + 1 : k - 1;
            int prevX = s[prevK + d + 1];
            int prevY = prevX - prevK;
            while (cx > prevX && cy > prevY) { ops.Add(('=', cx - 1, cy - 1)); cx--; cy--; }
            if (d > 0)
            {
                if (cx == prevX) ops.Add(('+', -1, cy - 1));
                else ops.Add(('-', cx - 1, -1));
            }
            cx = prevX;
            cy = prevY;
        }
        ops.Reverse();
        return ops;
    }
}
