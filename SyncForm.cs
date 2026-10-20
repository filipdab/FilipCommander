using System.IO.Enumeration;

namespace FilipCommander;

/// <summary>
/// Synchronizacja katalogów: porównuje dwa drzewa katalogów (data, rozmiar, opcjonalnie zawartość)
/// i kopiuje zaznaczone pliki w wybranym kierunku. Niczego nie usuwa.
/// </summary>
public sealed class SyncForm : Form
{
    private enum Dir { Right, Left, Equal, Unknown } // → ← = ≠

    private sealed class Item
    {
        public required string Rel;
        public FileInfo? L, R;
        public Dir Dir;
    }

    private readonly TextBox _left, _right, _mask;
    private readonly CheckBox _subdirs, _byContent, _ignoreDate, _showEqual;
    private readonly Button _compare, _sync;
    private readonly ListView _list;
    private readonly Label _status;
    private List<Item> _items = new();
    private CancellationTokenSource? _cts;

    /// <summary>Czy coś skopiowano (panele trzeba odświeżyć).</summary>
    public bool Changed { get; private set; }

    public SyncForm(string left, string right)
    {
        Text = "Synchronizacja katalogów";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1100, 700);
        MinimumSize = new Size(800, 450);
        ShowIcon = false;
        KeyPreview = true;

        var grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, Padding = new Padding(8, 8, 8, 0) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _left = new TextBox { Dock = DockStyle.Fill, Text = left };
        _right = new TextBox { Dock = DockStyle.Fill, Text = right };
        _mask = new TextBox { Width = 150, Text = "*" };
        grid.Controls.Add(Lbl("Lewy:"), 0, 0);
        grid.Controls.Add(_left, 1, 0);
        grid.Controls.Add(Lbl("Prawy:"), 2, 0);
        grid.Controls.Add(_right, 3, 0);

        _subdirs = new CheckBox { Text = "Podkatalogi", Checked = true, AutoSize = true };
        _byContent = new CheckBox { Text = "Porównuj zawartość plików o tym samym rozmiarze", AutoSize = true };
        _ignoreDate = new CheckBox { Text = "Ignoruj daty", AutoSize = true };
        _showEqual = new CheckBox { Text = "Pokaż identyczne", AutoSize = true };
        _showEqual.CheckedChanged += (_, _) => Fill();
        _compare = new Button { Text = "Porównaj", Width = 100, Height = 28 };
        _sync = new Button { Text = "Synchronizuj", Width = 110, Height = 28, Enabled = false };
        _compare.Click += (_, _) => { if (_cts == null) CompareAsync(); else _cts.Cancel(); };
        _sync.Click += (_, _) => SyncAsync();
        var options = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        options.Controls.AddRange(new Control[] { Lbl("Pliki:"), _mask, _subdirs, _byContent, _ignoreDate, _showEqual, _compare, _sync });
        grid.Controls.Add(options, 0, 1);
        grid.SetColumnSpan(options, 4);

        _list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, CheckBoxes = true, HideSelection = false };
        _list.Columns.Add("Plik", 380);
        _list.Columns.Add("Rozmiar (L)", 100, HorizontalAlignment.Right);
        _list.Columns.Add("Data (L)", 125);
        _list.Columns.Add("", 40, HorizontalAlignment.Center);
        _list.Columns.Add("Data (P)", 125);
        _list.Columns.Add("Rozmiar (P)", 100, HorizontalAlignment.Right);
        _list.DoubleClick += (_, _) => CompareSelected();
        var menu = new ContextMenuStrip();
        menu.Items.Add("Kopiuj w prawo  →", null, (_, _) => SetDirection(Dir.Right));
        menu.Items.Add("Kopiuj w lewo  ←", null, (_, _) => SetDirection(Dir.Left));
        menu.Items.Add("Nie kopiuj", null, (_, _) => SetDirection(null));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Porównaj zawartość (podwójne kliknięcie)", null, (_, _) => CompareSelected());
        _list.ContextMenuStrip = menu;
        var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 6, 8, 0) };
        host.Controls.Add(_list);

        _status = new Label { Dock = DockStyle.Bottom, Height = 24, Padding = new Padding(8, 0, 0, 0), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
        _status.Text = "→ kopiuj w prawo   ← kopiuj w lewo   = identyczne   ≠ różne, kierunek do wyboru (prawy przycisk)";

        Controls.Add(host);
        Controls.Add(grid);
        Controls.Add(_status);
        AcceptButton = _compare;
        FormClosing += (_, _) => _cts?.Cancel();
        Theme.Apply(this);
    }

    private static Label Lbl(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(3, 6, 3, 0) };

    private static string Arrow(Dir d) => d switch { Dir.Right => "→", Dir.Left => "←", Dir.Equal => "=", _ => "≠" };

    // ───────────────────────── porównanie ─────────────────────────

    private async void CompareAsync()
    {
        string left = _left.Text.Trim().Trim('"'), right = _right.Text.Trim().Trim('"');
        if (!Directory.Exists(left) || !Directory.Exists(right))
        {
            MessageBox.Show(this, "Oba katalogi muszą istnieć.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var masks = _mask.Text.Split(new[] { ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        bool subdirs = _subdirs.Checked, byContent = _byContent.Checked, ignoreDate = _ignoreDate.Checked;

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _compare.Text = "Przerwij";
        _sync.Enabled = false;
        _status.Text = "Porównywanie…";
        var progress = new Progress<string>(s => _status.Text = s);
        try
        {
            _items = await Task.Run(() => Compare(left, right, masks, subdirs, byContent, ignoreDate, progress, ct), ct);
            int diff = _items.Count(i => i.Dir != Dir.Equal);
            _status.Text = $"Plików: {_items.Count}, różnych: {diff}.   → kopiuj w prawo   ← kopiuj w lewo   ≠ kierunek do wyboru (prawy przycisk)";
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Przerwano.";
        }
        catch (Exception ex)
        {
            _status.Text = "Błąd: " + ex.Message;
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            _compare.Text = "Porównaj";
        }
        Fill();
    }

    private static List<Item> Compare(string left, string right, string[] masks, bool subdirs, bool byContent, bool ignoreDate,
        IProgress<string> progress, CancellationToken ct)
    {
        Dictionary<string, FileInfo> Scan(string root)
        {
            var opts = new EnumerationOptions { RecurseSubdirectories = subdirs, IgnoreInaccessible = true, AttributesToSkip = 0 };
            var result = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in new DirectoryInfo(root).EnumerateFiles("*", opts))
            {
                ct.ThrowIfCancellationRequested();
                if (masks.Length > 0 && !masks.Any(m => FileSystemName.MatchesSimpleExpression(m, f.Name))) continue;
                result[Path.GetRelativePath(root, f.FullName)] = f;
            }
            return result;
        }

        progress.Report("Czytanie lewego katalogu…");
        var l = Scan(left);
        progress.Report("Czytanie prawego katalogu…");
        var r = Scan(right);

        var items = new List<Item>();
        foreach (var rel in l.Keys.Union(r.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            l.TryGetValue(rel, out var lf);
            r.TryGetValue(rel, out var rf);
            var item = new Item { Rel = rel, L = lf, R = rf };
            if (rf == null) item.Dir = Dir.Right;
            else if (lf == null) item.Dir = Dir.Left;
            else
            {
                // FAT zapisuje czas z dokładnością do 2 s.
                double seconds = (lf.LastWriteTimeUtc - rf.LastWriteTimeUtc).TotalSeconds;
                bool sameDate = ignoreDate || Math.Abs(seconds) <= 2;
                bool sameSize = lf.Length == rf.Length;
                if (sameSize && byContent)
                {
                    progress.Report("Porównywanie zawartości: " + rel);
                    item.Dir = SameContent(lf.FullName, rf.FullName, ct) ? Dir.Equal
                        : sameDate ? Dir.Unknown : seconds > 0 ? Dir.Right : Dir.Left;
                }
                else if (sameSize && sameDate) item.Dir = Dir.Equal;
                else if (!sameDate) item.Dir = seconds > 0 ? Dir.Right : Dir.Left;
                else item.Dir = Dir.Unknown;
            }
            items.Add(item);
        }
        return items;
    }

    private static bool SameContent(string a, string b, CancellationToken ct)
    {
        using var fa = new FileStream(a, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var fb = new FileStream(b, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var ba = new byte[1 << 16];
        var bb = new byte[1 << 16];
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            int na = fa.ReadAtLeast(ba, ba.Length, throwOnEndOfStream: false);
            int nb = fb.ReadAtLeast(bb, bb.Length, throwOnEndOfStream: false);
            if (na != nb || !ba.AsSpan(0, na).SequenceEqual(bb.AsSpan(0, nb))) return false;
            if (na == 0) return true;
        }
    }

    private void Fill()
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var it in _items)
        {
            if (it.Dir == Dir.Equal && !_showEqual.Checked) continue;
            var row = new ListViewItem(new[]
            {
                it.Rel,
                it.L?.Length.ToString("N0") ?? "",
                it.L?.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                Arrow(it.Dir),
                it.R?.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                it.R?.Length.ToString("N0") ?? ""
            })
            {
                Tag = it,
                Checked = it.Dir is Dir.Right or Dir.Left,
                ForeColor = Theme.WindowText,
                BackColor = it.Dir switch
                {
                    Dir.Unknown => Theme.DiffChanged,
                    _ when it.L == null || it.R == null => Theme.DiffAdded,
                    _ => Theme.Window
                }
            };
            _list.Items.Add(row);
        }
        _list.EndUpdate();
        _sync.Enabled = _list.Items.Count > 0;
    }

    private void SetDirection(Dir? dir)
    {
        foreach (ListViewItem row in _list.SelectedItems)
        {
            var it = (Item)row.Tag!;
            if (dir == null) { row.Checked = false; continue; }
            // Nie da się kopiować pliku, którego po danej stronie nie ma.
            if ((dir == Dir.Right && it.L == null) || (dir == Dir.Left && it.R == null)) continue;
            it.Dir = dir.Value;
            row.SubItems[3].Text = Arrow(it.Dir);
            row.Checked = true;
        }
    }

    private void CompareSelected()
    {
        if (_list.SelectedItems.Count == 0) return;
        var it = (Item)_list.SelectedItems[0].Tag!;
        if (it.L != null && it.R != null) DiffForm.Compare(this, it.L.FullName, it.R.FullName);
    }

    // ───────────────────────── kopiowanie ─────────────────────────

    private async void SyncAsync()
    {
        string left = _left.Text.Trim().Trim('"'), right = _right.Text.Trim().Trim('"');
        var jobs = _list.Items.Cast<ListViewItem>()
            .Where(r => r.Checked)
            .Select(r => (Item)r.Tag!)
            .Where(it => it.Dir is Dir.Right or Dir.Left)
            .Select(it => it.Dir == Dir.Right
                ? (Src: it.L!.FullName, Dst: Path.Combine(right, it.Rel), Size: it.L.Length)
                : (Src: it.R!.FullName, Dst: Path.Combine(left, it.Rel), Size: it.R.Length))
            .ToList();
        int skippedUnknown = _list.Items.Cast<ListViewItem>().Count(r => r.Checked && ((Item)r.Tag!).Dir == Dir.Unknown);
        if (jobs.Count == 0)
        {
            MessageBox.Show(this, "Nic do skopiowania. Pozycjom ≠ wybierz kierunek prawym przyciskiem.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        string question = $"Skopiować {jobs.Count} plik(ów) ({FilePanel.FormatSize(jobs.Sum(j => j.Size))})?\nIstniejące pliki docelowe zostaną nadpisane.";
        if (skippedUnknown > 0) question += $"\n\nPominięte: {skippedUnknown} pozycji ≠ bez wybranego kierunku.";
        if (MessageBox.Show(this, question, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        try
        {
            await ProgressDialog.RunAsync(this, "Synchronizacja", (progress, ct) =>
            {
                long total = jobs.Sum(j => j.Size), done = 0;
                var buffer = new byte[1 << 20];
                foreach (var (src, dst, size) in jobs)
                {
                    ct.ThrowIfCancellationRequested();
                    progress.Report(new OpProgress(done, total, dst));
                    Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                    // Przerwane kopiowanie nie może zostawić uciętego pliku docelowego.
                    string tmp = dst + ".fctmp";
                    try
                    {
                        using (var s = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        using (var d = new FileStream(tmp, FileMode.Create, FileAccess.Write))
                        {
                            int n;
                            while ((n = s.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                ct.ThrowIfCancellationRequested();
                                d.Write(buffer, 0, n);
                                done += n;
                                progress.Report(new OpProgress(done, total, dst));
                            }
                        }
                        if (File.Exists(dst)) File.SetAttributes(dst, FileAttributes.Normal);
                        File.Move(tmp, dst, overwrite: true);
                    }
                    finally
                    {
                        try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                    }
                    File.SetLastWriteTimeUtc(dst, File.GetLastWriteTimeUtc(src));
                    File.SetAttributes(dst, File.GetAttributes(src));
                }
            });
            Changed = true;
        }
        catch (Exception ex)
        {
            Changed = true;
            MessageBox.Show(this, "Błąd kopiowania:\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        CompareAsync();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { Close(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
