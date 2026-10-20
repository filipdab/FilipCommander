using System.Diagnostics;
using System.Globalization;
using System.IO.Enumeration;

namespace FilipCommander;

/// <summary>Jeden panel plików: lista dysków, ścieżka, lista plików i pasek stanu.</summary>
public sealed class FilePanel : UserControl
{
    private readonly ComboBox _drives;
    private readonly Label _header;
    private readonly ListView _list;
    private readonly Label _status;
    private readonly ImageList _images;
    private readonly FileSystemWatcher _watcher = new();
    private readonly System.Windows.Forms.Timer _reloadTimer = new() { Interval = 300 };

    private readonly HashSet<string> _marked = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _dirSizes = new(StringComparer.OrdinalIgnoreCase);
    private List<FileSystemInfo> _entries = new();
    private int _sortColumn;
    private bool _sortDescending;
    private bool _active;

    public string CurrentPath { get; private set; } = "";
    public bool ShowHidden { get; set; }
    public ListView List => _list;

    /// <summary>Panel dostał fokus (kliknięcie, Tab).</summary>
    public event EventHandler? PanelActivated;
    /// <summary>Zmiana katalogu.</summary>
    public event EventHandler? PathChanged;
    /// <summary>Ctrl+Enter — wstaw nazwę do wiersza poleceń.</summary>
    public event Action<string>? InsertNameRequested;

    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    public FilePanel()
    {
        _images = new ImageList { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new Size(16, 16) };

        _drives = new ComboBox
        {
            Dock = DockStyle.Left,
            Width = 110,
            DropDownStyle = ComboBoxStyle.DropDownList,
            TabStop = false
        };
        _drives.DropDown += (_, _) => FillDrives();
        _drives.SelectionChangeCommitted += (_, _) => ChangeDrive();

        var topBar = new Panel { Dock = DockStyle.Top, Height = _drives.PreferredHeight + 2 };
        var freeLabel = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 0, 0, 0) };
        topBar.Controls.Add(freeLabel);
        topBar.Controls.Add(_drives);
        FreeSpaceLabel = freeLabel;

        _header = new Label
        {
            Dock = DockStyle.Top,
            Height = 22,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(4, 0, 0, 0),
            AutoEllipsis = true,
            Font = new Font(Font, FontStyle.Bold)
        };

        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            SmallImageList = _images,
            HeaderStyle = ColumnHeaderStyle.Clickable,
            Font = new Font("Segoe UI", 9f)
        };
        _list.Columns.Add("Nazwa", 240);
        _list.Columns.Add("Rozsz.", 60);
        _list.Columns.Add("Rozmiar", 100, HorizontalAlignment.Right);
        _list.Columns.Add("Data", 125);
        _list.Columns.Add("Atr.", 50);
        _header.Click += (_, _) => _list.Focus();
        _list.ColumnClick += (_, e) => SortBy(e.Column);
        _list.DoubleClick += (_, _) => OpenFocused();
        _list.KeyDown += OnListKeyDown;
        _list.GotFocus += (_, _) => PanelActivated?.Invoke(this, EventArgs.Empty);
        _list.MouseDown += (_, e) =>
        {
            // Prawy przycisk zaznacza/odznacza, jak w TC.
            if (e.Button == MouseButtons.Right && _list.GetItemAt(e.X, e.Y) is { } item)
            {
                item.Focused = true;
                item.Selected = true;
                ToggleMark(item);
            }
        };

        _status = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 20,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(4, 0, 0, 0),
            AutoEllipsis = true
        };

        Controls.Add(_list);
        Controls.Add(_header);
        Controls.Add(topBar);
        Controls.Add(_status);

        _reloadTimer.Tick += (_, _) => { _reloadTimer.Stop(); Reload(); };
        _watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                                NotifyFilters.Size | NotifyFilters.LastWrite | NotifyFilters.Attributes;
        _watcher.Changed += OnFsEvent;
        _watcher.Created += OnFsEvent;
        _watcher.Deleted += OnFsEvent;
        _watcher.Renamed += OnFsEvent;
        _watcher.Error += OnFsEvent;

        SetActive(false);
    }

    private Label FreeSpaceLabel { get; }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _watcher.Dispose();
            _reloadTimer.Dispose();
            _images.Dispose();
        }
        base.Dispose(disposing);
    }

    // ───────────────────────── nawigacja ─────────────────────────

    public bool Navigate(string path, string? selectName = null)
    {
        try
        {
            var dir = new DirectoryInfo(path);
            if (!dir.Exists) throw new DirectoryNotFoundException($"Katalog nie istnieje:\n{path}");
            var entries = ReadEntries(dir);

            bool samePath = string.Equals(dir.FullName, CurrentPath, StringComparison.OrdinalIgnoreCase);
            if (!samePath)
            {
                _marked.Clear();
                _dirSizes.Clear();
            }
            CurrentPath = dir.FullName;
            _entries = entries;
            Populate(selectName);
            StartWatcher();
            PathChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(FindForm(), ex.Message, "FilipCommander", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
    }

    /// <summary>Odświeża bieżący katalog, zachowując kursor i zaznaczenia.</summary>
    public void Reload()
    {
        if (string.IsNullOrEmpty(CurrentPath)) return;
        string? focused = FocusedEntry?.Name;
        int focusedIndex = _list.FocusedItem?.Index ?? 0;

        string path = CurrentPath;
        // Jeśli katalog zniknął, cofamy się do najbliższego istniejącego.
        while (!Directory.Exists(path))
        {
            var parent = Path.GetDirectoryName(path);
            if (parent == null) { path = Path.GetPathRoot(Environment.SystemDirectory)!; break; }
            path = parent;
        }

        if (!string.Equals(path, CurrentPath, StringComparison.OrdinalIgnoreCase))
        {
            Navigate(path);
            return;
        }

        try
        {
            _entries = ReadEntries(new DirectoryInfo(path));
        }
        catch
        {
            return;
        }
        _marked.RemoveWhere(n => !_entries.Any(e => e.Name.Equals(n, StringComparison.OrdinalIgnoreCase)));
        Populate(focused, focusedIndex);
    }

    public void GoUp()
    {
        var dir = new DirectoryInfo(CurrentPath);
        if (dir.Parent != null) Navigate(dir.Parent.FullName, dir.Name);
    }

    public void GoRoot()
    {
        var root = Path.GetPathRoot(CurrentPath);
        if (root != null) Navigate(root);
    }

    public void OpenFocused()
    {
        var item = _list.FocusedItem;
        if (item == null) return;
        if (item.Tag == null) { GoUp(); return; }

        switch (item.Tag)
        {
            case DirectoryInfo d:
                Navigate(d.FullName);
                break;
            case FileInfo f:
                try
                {
                    Process.Start(new ProcessStartInfo(f.FullName) { UseShellExecute = true, WorkingDirectory = CurrentPath });
                }
                catch (Exception ex)
                {
                    MessageBox.Show(FindForm(), ex.Message, "Nie można otworzyć", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                break;
        }
    }

    private List<FileSystemInfo> ReadEntries(DirectoryInfo dir)
    {
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            AttributesToSkip = ShowHidden ? 0 : FileAttributes.Hidden | FileAttributes.System,
            ReturnSpecialDirectories = false
        };
        // Wymuszamy wyjątek przy braku dostępu do samego katalogu.
        var list = dir.EnumerateFileSystemInfos("*", options).ToList();
        if (list.Count == 0) _ = Directory.GetFileSystemEntries(dir.FullName).Length;
        return list;
    }

    // ───────────────────────── wyświetlanie ─────────────────────────

    private void Populate(string? selectName, int fallbackIndex = 0)
    {
        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            var items = new List<ListViewItem>(_entries.Count + 1);

            if (new DirectoryInfo(CurrentPath).Parent != null)
            {
                var up = new ListViewItem("..", ShellIcons.GetKey(_images, null)) { Tag = null };
                up.SubItems.AddRange(new[] { "", "<UP>", "", "" });
                items.Add(up);
            }

            foreach (var e in Sorted(_entries))
                items.Add(CreateItem(e));

            _list.Items.AddRange(items.ToArray());

            ListViewItem? target = null;
            if (selectName != null)
                target = items.FirstOrDefault(i => i.Tag is FileSystemInfo fi &&
                                                   fi.Name.Equals(selectName, StringComparison.OrdinalIgnoreCase));
            if (target == null && _list.Items.Count > 0)
                target = _list.Items[Math.Clamp(fallbackIndex, 0, _list.Items.Count - 1)];

            if (target != null)
            {
                target.Focused = true;
                target.Selected = true;
                target.EnsureVisible();
            }
        }
        finally
        {
            _list.EndUpdate();
        }
        _header.Text = CurrentPath;
        UpdateDriveCombo();
        UpdateStatus();
    }

    private ListViewItem CreateItem(FileSystemInfo e)
    {
        string name, ext, size;
        if (e is DirectoryInfo)
        {
            name = e.Name;
            ext = "";
            size = _dirSizes.TryGetValue(e.Name, out var ds) ? FormatSize(ds) : "<DIR>";
        }
        else
        {
            var f = (FileInfo)e;
            // Jak w TC: nazwa bez rozszerzenia, rozszerzenie w osobnej kolumnie.
            bool hasExt = f.Extension.Length > 0 && f.Extension.Length < f.Name.Length;
            name = hasExt ? Path.GetFileNameWithoutExtension(f.Name) : f.Name;
            ext = hasExt ? f.Extension.TrimStart('.') : "";
            size = SafeLength(f).ToString("N0", Pl);
        }

        var item = new ListViewItem(name, ShellIcons.GetKey(_images, e)) { Tag = e };
        item.SubItems.AddRange(new[]
        {
            ext,
            size,
            SafeDate(e).ToString("yyyy-MM-dd HH:mm", Pl),
            FormatAttributes(e.Attributes)
        });
        if (e.Attributes.HasFlag(FileAttributes.Hidden)) item.ForeColor = SystemColors.GrayText;
        ApplyMarkStyle(item);
        return item;
    }

    private IEnumerable<FileSystemInfo> Sorted(IEnumerable<FileSystemInfo> entries)
    {
        IComparer<FileSystemInfo> cmp = Comparer<FileSystemInfo>.Create((a, b) =>
        {
            int r = _sortColumn switch
            {
                1 => string.Compare(a is FileInfo ? a.Extension : "", b is FileInfo ? b.Extension : "", StringComparison.OrdinalIgnoreCase),
                2 => SizeOf(a).CompareTo(SizeOf(b)),
                3 => SafeDate(a).CompareTo(SafeDate(b)),
                4 => ((int)a.Attributes).CompareTo((int)b.Attributes),
                _ => 0
            };
            if (r == 0) r = NaturalCompare(a.Name, b.Name);
            return _sortDescending ? -r : r;
        });

        // Katalogi zawsze na górze.
        var dirs = entries.Where(e => e is DirectoryInfo).OrderBy(e => e, cmp);
        var files = entries.Where(e => e is FileInfo).OrderBy(e => e, cmp);
        return dirs.Concat(files);
    }

    private long SizeOf(FileSystemInfo e) => e switch
    {
        FileInfo f => SafeLength(f),
        DirectoryInfo d => _dirSizes.TryGetValue(d.Name, out var s) ? s : -1,
        _ => 0
    };

    private static int NaturalCompare(string a, string b)
    {
        // "plik2" przed "plik10"
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
            {
                int si = i, sj = j;
                while (i < a.Length && char.IsDigit(a[i])) i++;
                while (j < b.Length && char.IsDigit(b[j])) j++;
                var na = a[si..i].TrimStart('0');
                var nb = b[sj..j].TrimStart('0');
                int c = na.Length != nb.Length ? na.Length.CompareTo(nb.Length) : string.CompareOrdinal(na, nb);
                if (c != 0) return c;
            }
            else
            {
                int c = string.Compare(a[i].ToString(), b[j].ToString(), Pl, CompareOptions.IgnoreCase);
                if (c != 0) return c;
                i++; j++;
            }
        }
        return (a.Length - i).CompareTo(b.Length - j);
    }

    private void SortBy(int column)
    {
        if (_sortColumn == column) _sortDescending = !_sortDescending;
        else { _sortColumn = column; _sortDescending = false; }

        for (int c = 0; c < _list.Columns.Count; c++)
        {
            string t = _list.Columns[c].Text.TrimEnd(' ', '▲', '▼');
            _list.Columns[c].Text = c == _sortColumn ? t + (_sortDescending ? " ▼" : " ▲") : t;
        }
        Populate(FocusedEntry?.Name);
    }

    private void UpdateStatus()
    {
        int files = 0, dirs = 0, mFiles = 0, mDirs = 0;
        long total = 0, mTotal = 0;
        foreach (var e in _entries)
        {
            bool m = _marked.Contains(e.Name);
            if (e is FileInfo f)
            {
                long len = SafeLength(f);
                files++; total += len;
                if (m) { mFiles++; mTotal += len; }
            }
            else
            {
                dirs++;
                if (m) { mDirs++; if (_dirSizes.TryGetValue(e.Name, out var s)) mTotal += s; }
            }
        }
        _status.Text = $"Zaznaczono {FormatSize(mTotal)} z {FormatSize(total)},  plików: {mFiles} z {files},  katalogów: {mDirs} z {dirs}";

        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(CurrentPath)!);
            FreeSpaceLabel.Text = drive.IsReady
                ? $"[{SafeLabel(drive)}]  wolne {FormatSize(drive.AvailableFreeSpace)} z {FormatSize(drive.TotalSize)}"
                : "";
        }
        catch
        {
            FreeSpaceLabel.Text = "";
        }
    }

    public void SetActive(bool active)
    {
        _active = active;
        _header.BackColor = active ? SystemColors.ActiveCaption : SystemColors.InactiveCaption;
        _header.ForeColor = active ? SystemColors.ActiveCaptionText : SystemColors.InactiveCaptionText;
    }

    public bool IsActive => _active;

    // ───────────────────────── dyski ─────────────────────────

    private void FillDrives()
    {
        string? current = _drives.SelectedItem as string;
        _drives.Items.Clear();
        foreach (var d in DriveInfo.GetDrives())
            _drives.Items.Add(d.Name.TrimEnd('\\'));
        if (current != null) _drives.SelectedItem = current;
    }

    private void UpdateDriveCombo()
    {
        if (_drives.Items.Count == 0) FillDrives();
        string root = (Path.GetPathRoot(CurrentPath) ?? "").TrimEnd('\\');
        if (!_drives.Items.Contains(root)) _drives.Items.Add(root);
        _drives.SelectedItem = root;
    }

    private void ChangeDrive()
    {
        if (_drives.SelectedItem is not string name) return;
        var drive = new DriveInfo(name + "\\");
        if (!drive.IsReady)
        {
            MessageBox.Show(FindForm(), $"Dysk {name} nie jest gotowy.", "FilipCommander", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            UpdateDriveCombo();
        }
        else
        {
            Navigate(drive.RootDirectory.FullName);
        }
        _list.Focus();
    }

    public void DropDownDrives()
    {
        _drives.Focus();
        _drives.DroppedDown = true;
    }

    // ───────────────────────── zaznaczanie ─────────────────────────

    public FileSystemInfo? FocusedEntry => _list.FocusedItem?.Tag as FileSystemInfo;

    /// <summary>Zaznaczone elementy, a gdy nic nie zaznaczono — element pod kursorem.</summary>
    public List<FileSystemInfo> GetSelection()
    {
        if (_marked.Count > 0)
            return _entries.Where(e => _marked.Contains(e.Name)).ToList();
        return FocusedEntry is { } f ? new List<FileSystemInfo> { f } : new List<FileSystemInfo>();
    }

    private void ToggleMark(ListViewItem item)
    {
        if (item.Tag is not FileSystemInfo e) return;
        if (!_marked.Remove(e.Name)) _marked.Add(e.Name);
        ApplyMarkStyle(item);
        UpdateStatus();
    }

    private void ApplyMarkStyle(ListViewItem item)
    {
        bool marked = item.Tag is FileSystemInfo e && _marked.Contains(e.Name);
        item.ForeColor = marked ? Color.Red
            : item.Tag is FileSystemInfo h && h.Attributes.HasFlag(FileAttributes.Hidden) ? SystemColors.GrayText
            : SystemColors.WindowText;
        item.Font = marked ? new Font(_list.Font, FontStyle.Bold) : _list.Font;
    }

    public void ClearMarks()
    {
        _marked.Clear();
        foreach (ListViewItem i in _list.Items) ApplyMarkStyle(i);
        UpdateStatus();
    }

    public void MarkByPattern(string pattern, bool mark)
    {
        var patterns = pattern.Split(new[] { ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (ListViewItem i in _list.Items)
        {
            if (i.Tag is not FileSystemInfo e) continue;
            if (!patterns.Any(p => FileSystemName.MatchesSimpleExpression(p, e.Name))) continue;
            if (mark) _marked.Add(e.Name); else _marked.Remove(e.Name);
            ApplyMarkStyle(i);
        }
        UpdateStatus();
    }

    public void InvertMarks()
    {
        foreach (ListViewItem i in _list.Items)
        {
            if (i.Tag is not FileInfo e) continue; // jak w TC: odwraca tylko pliki
            if (!_marked.Remove(e.Name)) _marked.Add(e.Name);
            ApplyMarkStyle(i);
        }
        UpdateStatus();
    }

    private void MoveCursorDown()
    {
        var item = _list.FocusedItem;
        if (item == null || item.Index >= _list.Items.Count - 1) return;
        var next = _list.Items[item.Index + 1];
        next.Focused = true;
        next.Selected = true;
        next.EnsureVisible();
    }

    private async void CalculateDirSize(ListViewItem item, DirectoryInfo dir)
    {
        item.SubItems[2].Text = "…";
        string path = CurrentPath;
        long size = await Task.Run(() =>
        {
            long total = 0;
            var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 };
            try
            {
                foreach (var f in dir.EnumerateFiles("*", opts))
                    total += SafeLength(f);
            }
            catch { /* częściowy wynik jest OK */ }
            return total;
        });
        if (path != CurrentPath || item.ListView == null) return;
        _dirSizes[dir.Name] = size;
        item.SubItems[2].Text = FormatSize(size);
        UpdateStatus();
    }

    // ───────────────────────── klawiatura ─────────────────────────

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        bool handled = true;
        switch (e.KeyCode)
        {
            case Keys.Enter when e.Control:
                if (FocusedEntry is { } fe) InsertNameRequested?.Invoke(fe.Name);
                break;
            case Keys.Enter:
                OpenFocused();
                break;
            case Keys.Back:
            case Keys.PageUp when e.Control:
                GoUp();
                break;
            case Keys.PageDown when e.Control:
                OpenFocused();
                break;
            case Keys.Oem5 when e.Control: // Ctrl+\
                GoRoot();
                break;
            case Keys.Insert:
                if (_list.FocusedItem is { } ins) { ToggleMark(ins); MoveCursorDown(); }
                break;
            case Keys.Space:
                if (_list.FocusedItem is { } sp)
                {
                    ToggleMark(sp);
                    if (sp.Tag is DirectoryInfo d && _marked.Contains(d.Name)) CalculateDirSize(sp, d);
                }
                break;
            case Keys.A when e.Control:
                MarkByPattern("*", true);
                break;
            case Keys.Add:
                AskPattern(true);
                break;
            case Keys.Subtract:
                AskPattern(false);
                break;
            case Keys.Multiply:
                InvertMarks();
                break;
            default:
                handled = false;
                break;
        }
        if (handled)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    private void AskPattern(bool mark)
    {
        var p = PromptDialog.Show(FindForm(), mark ? "Zaznacz" : "Odznacz",
            mark ? "Zaznacz pliki pasujące do maski (np. *.txt;*.log):" : "Odznacz pliki pasujące do maski:", "*.*");
        if (p != null) MarkByPattern(p.Replace("*.*", "*"), mark);
    }

    // ───────────────────────── obserwator systemu plików ─────────────────────────

    private void StartWatcher()
    {
        try
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Path = CurrentPath;
            _watcher.EnableRaisingEvents = true;
        }
        catch
        {
            // np. dysk sieciowy bez obsługi powiadomień — wtedy tylko Ctrl+R
        }
    }

    private void OnFsEvent(object? sender, EventArgs e)
    {
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke(() => { _reloadTimer.Stop(); _reloadTimer.Start(); });
    }

    // ───────────────────────── pomocnicze ─────────────────────────

    private static long SafeLength(FileInfo f)
    {
        try { return f.Length; } catch { return 0; }
    }

    private static DateTime SafeDate(FileSystemInfo f)
    {
        try { return f.LastWriteTime; } catch { return DateTime.MinValue; }
    }

    private static string SafeLabel(DriveInfo d)
    {
        try { return string.IsNullOrEmpty(d.VolumeLabel) ? d.Name.TrimEnd('\\') : d.VolumeLabel; }
        catch { return d.Name; }
    }

    public static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes;
        int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return u == 0 ? $"{bytes:N0} B".Replace(",", " ") : v.ToString(v < 10 ? "0.0#" : v < 100 ? "0.0" : "0", Pl) + " " + units[u];
    }

    private static string FormatAttributes(FileAttributes a)
    {
        Span<char> s = stackalloc char[4];
        s[0] = a.HasFlag(FileAttributes.ReadOnly) ? 'r' : '-';
        s[1] = a.HasFlag(FileAttributes.Archive) ? 'a' : '-';
        s[2] = a.HasFlag(FileAttributes.Hidden) ? 'h' : '-';
        s[3] = a.HasFlag(FileAttributes.System) ? 's' : '-';
        return new string(s);
    }
}
