using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO.Enumeration;

namespace FilipCommander;

public enum PanelView { Details, Brief, Thumbnails }

/// <summary>Zakładka panelu: katalog i historia odwiedzonych katalogów.</summary>
public sealed class PanelTab
{
    public string Path = "";
    public readonly List<string> Back = new();
    public readonly List<string> Forward = new();
}

/// <summary>Jeden panel plików: lista dysków, zakładki, ścieżka, lista plików i pasek stanu.</summary>
public sealed class FilePanel : UserControl
{
    private const int HistoryLimit = 50;
    private const int ThumbSize = 96;

    private readonly ComboBox _drives;
    private readonly TabBar _tabBar;
    private readonly Label _header;
    private readonly ListView _list;
    private readonly Label _status;
    private readonly Panel _quickBar;
    private readonly Label _quickLabel;
    private readonly TextBox _quickBox;
    private readonly ImageList _images;
    private readonly ImageList _thumbs;
    private readonly FileSystemWatcher _watcher = new();
    private readonly System.Windows.Forms.Timer _reloadTimer = new() { Interval = 300 };
    private readonly System.Windows.Forms.Timer _previewTimer = new() { Interval = 120 };

    private readonly HashSet<string> _marked = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _dirSizes = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<PanelTab> _tabs = new() { new PanelTab() };
    private List<FileSystemInfo> _entries = new();
    private int _tabIndex;
    private int _sortColumn;
    private bool _sortDescending;
    private bool _active;
    private PanelView _view = PanelView.Details;
    private Font _boldFont;

    // szybkie wyszukiwanie (Ctrl+S) i filtr (Ctrl+F)
    private bool _filterMode;
    private string _filter = "";

    // szybki podgląd (Ctrl+Q)
    private ViewerControl? _viewer;
    private FileSystemInfo? _pendingPreview;

    // miniatury ładowane w tle; zmiana numeru przerywa poprzednie ładowanie
    private volatile int _thumbGeneration;

    // ścieżki przeciągane z któregoś panelu — żeby nie upuszczać plików na ich własny katalog
    private static string[]? _draggedPaths;

    public string CurrentPath { get; private set; } = "";
    public bool ShowHidden { get; set; }
    /// <summary>Prawy przycisk otwiera menu kontekstowe zamiast zaznaczać.</summary>
    public bool RightClickMenu { get; set; }
    public ListView List => _list;
    public bool IsQuickView => _viewer is { Visible: true };
    public IReadOnlyList<FileSystemInfo> Entries => _entries;

    /// <summary>Panel dostał fokus (kliknięcie, Tab).</summary>
    public event EventHandler? PanelActivated;
    /// <summary>Zmiana katalogu.</summary>
    public event EventHandler? PathChanged;
    /// <summary>Kursor przeszedł na inny element (do szybkiego podglądu).</summary>
    public event EventHandler? FocusedEntryChanged;
    /// <summary>Ctrl+Enter — wstaw nazwę do wiersza poleceń.</summary>
    public event Action<string>? InsertNameRequested;
    /// <summary>Upuszczono pliki: ścieżki, katalog docelowy, czy przenieść.</summary>
    public event Action<string[], string, bool>? FilesDropped;
    /// <summary>Prośba o menu kontekstowe dla elementów w punkcie ekranu.</summary>
    public event Action<List<FileSystemInfo>, Point>? ContextMenuRequested;

    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    public FilePanel()
    {
        _images = new ImageList { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new Size(16, 16) };
        _thumbs = new ImageList { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new Size(ThumbSize, ThumbSize) };

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

        _tabBar = new TabBar { Dock = DockStyle.Top, Visible = false };

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
            LargeImageList = _thumbs,
            HeaderStyle = ColumnHeaderStyle.Clickable,
            Font = new Font("Segoe UI", 9f),
            AllowDrop = true
        };
        _boldFont = new Font(_list.Font, FontStyle.Bold);
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
        _list.ItemSelectionChanged += (_, e) => { if (e.IsSelected) FocusedEntryChanged?.Invoke(this, EventArgs.Empty); };
        _list.MouseDown += OnListMouseDown;
        _list.ItemDrag += OnItemDrag;
        _list.DragEnter += OnDragOver;
        _list.DragOver += OnDragOver;
        _list.DragDrop += OnDragDrop;
        _tabBar.TabClicked += i => { SelectTab(i); _list.Focus(); };
        _tabBar.TabMiddleClicked += i => { CloseTab(i); _list.Focus(); };
        _tabBar.NewTabRequested += () => { NewTab(); _list.Focus(); };

        _status = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 20,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(4, 0, 0, 0),
            AutoEllipsis = true
        };

        _quickBar = new Panel { Dock = DockStyle.Bottom, Height = 24, Padding = new Padding(2), Visible = false };
        _quickLabel = new Label { Dock = DockStyle.Left, AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(2, 3, 4, 0) };
        _quickBox = new TextBox { Dock = DockStyle.Fill };
        _quickBox.TextChanged += (_, _) => OnQuickTextChanged();
        _quickBox.KeyDown += OnQuickKeyDown;
        _quickBox.LostFocus += (_, _) => { if (!_filterMode) _quickBar.Visible = false; };
        _quickBar.Controls.Add(_quickBox);
        _quickBar.Controls.Add(_quickLabel);

        Controls.Add(_list);
        Controls.Add(_quickBar);
        Controls.Add(_header);
        Controls.Add(_tabBar);
        Controls.Add(topBar);
        Controls.Add(_status);

        _reloadTimer.Tick += (_, _) => { _reloadTimer.Stop(); Reload(); };
        _previewTimer.Tick += (_, _) => { _previewTimer.Stop(); UpdatePreview(); };
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
            _thumbGeneration++;
            _watcher.Dispose();
            _reloadTimer.Dispose();
            _previewTimer.Dispose();
            _images.Dispose();
            _thumbs.Dispose();
            _boldFont.Dispose();
        }
        base.Dispose(disposing);
    }

    private PanelTab Tab => _tabs[_tabIndex];

    // ───────────────────────── nawigacja ─────────────────────────

    public bool Navigate(string path, string? selectName = null) => Navigate(path, selectName, addHistory: true);

    private bool Navigate(string path, string? selectName, bool addHistory)
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
                _thumbs.Images.Clear();
                if (_filter.Length > 0) CloseFilter(repopulate: false);
                if (addHistory && CurrentPath.Length > 0) PushHistory(Tab.Back, CurrentPath, clearForward: true);
                // Wychodząc do katalogu nadrzędnego ustawiamy kursor na katalogu, z którego przyszliśmy.
                if (selectName == null && CurrentPath.StartsWith(dir.FullName.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                    selectName = CurrentPath[(dir.FullName.TrimEnd('\\').Length + 1)..].Split('\\')[0];
            }
            CurrentPath = dir.FullName;
            Tab.Path = CurrentPath;
            _entries = entries;
            Populate(selectName);
            StartWatcher();
            UpdateTabBar();
            PathChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(FindForm(), ex.Message, "FilipCommander", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
    }

    private void PushHistory(List<string> stack, string path, bool clearForward)
    {
        if (stack.Count == 0 || !stack[^1].Equals(path, StringComparison.OrdinalIgnoreCase)) stack.Add(path);
        if (stack.Count > HistoryLimit) stack.RemoveAt(0);
        if (clearForward) Tab.Forward.Clear();
    }

    /// <summary>Alt+← — poprzedni katalog.</summary>
    public void GoBack() => StepHistory(Tab.Back, Tab.Forward);

    /// <summary>Alt+→ — następny katalog.</summary>
    public void GoForward() => StepHistory(Tab.Forward, Tab.Back);

    private void StepHistory(List<string> from, List<string> to)
    {
        while (from.Count > 0)
        {
            string path = from[^1];
            from.RemoveAt(from.Count - 1);
            if (!Directory.Exists(path)) continue;
            string current = CurrentPath;
            if (Navigate(path, null, addHistory: false)) PushHistory(to, current, clearForward: false);
            return;
        }
    }

    /// <summary>Alt+↓ — lista ostatnio odwiedzonych katalogów.</summary>
    public void ShowHistoryMenu()
    {
        var menu = new ContextMenuStrip();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { CurrentPath };
        foreach (var p in Enumerable.Reverse(Tab.Back).Concat(Tab.Forward))
        {
            if (!seen.Add(p)) continue;
            string target = p;
            menu.Items.Add(p, null, (_, _) => Navigate(target));
        }
        if (menu.Items.Count == 0) menu.Items.Add(new ToolStripMenuItem("(historia jest pusta)") { Enabled = false });
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Show(_header, new Point(0, _header.Height));
    }

    /// <summary>Odświeża bieżący katalog, zachowując kursor i zaznaczenia.</summary>
    public void Reload()
    {
        if (string.IsNullOrEmpty(CurrentPath)) return;
        string? focused = FocusedEntry?.Name;
        int focusedIndex = _list.FocusedItem?.Index ?? 0;

        // Jeśli katalog zniknął, cofamy się do najbliższego istniejącego.
        string path = ExistingAncestor(CurrentPath);
        if (!string.Equals(path, CurrentPath, StringComparison.OrdinalIgnoreCase))
        {
            Navigate(path, null, addHistory: false);
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

    private static string ExistingAncestor(string path)
    {
        while (!Directory.Exists(path))
        {
            var parent = Path.GetDirectoryName(path);
            if (parent == null) return Path.GetPathRoot(Environment.SystemDirectory)!;
            path = parent;
        }
        return path;
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

    // ───────────────────────── zakładki ─────────────────────────

    public IReadOnlyList<string> TabPaths => _tabs.Select(t => t.Path).ToList();
    public int ActiveTab => _tabIndex;

    /// <summary>Odtwarza zakładki z ustawień (nieistniejące katalogi zastępuje najbliższym istniejącym).</summary>
    public void RestoreTabs(IReadOnlyList<string> paths, int index, string fallback)
    {
        _tabs.Clear();
        foreach (var p in paths.Where(p => !string.IsNullOrWhiteSpace(p)))
            _tabs.Add(new PanelTab { Path = ExistingAncestor(p) });
        if (_tabs.Count == 0) _tabs.Add(new PanelTab { Path = fallback });
        _tabIndex = Math.Clamp(index, 0, _tabs.Count - 1);
        CurrentPath = "";
        if (!Navigate(Tab.Path, null, addHistory: false)) Navigate(fallback, null, addHistory: false);
    }

    /// <summary>Ctrl+T — nowa zakładka z bieżącym katalogiem (albo podanym).</summary>
    public void NewTab(string? path = null)
    {
        _tabs.Insert(_tabIndex + 1, new PanelTab { Path = path ?? CurrentPath });
        SelectTab(_tabIndex + 1);
    }

    /// <summary>Ctrl+W — zamyka zakładkę (ostatniej nie zamyka).</summary>
    public void CloseTab(int? index = null)
    {
        int i = index ?? _tabIndex;
        if (_tabs.Count <= 1 || i < 0 || i >= _tabs.Count) return;
        _tabs.RemoveAt(i);
        int next = i < _tabIndex ? _tabIndex - 1 : Math.Min(_tabIndex, _tabs.Count - 1);
        _tabIndex = -1;
        SelectTab(next);
    }

    /// <summary>Ctrl+Tab / Ctrl+Shift+Tab.</summary>
    public void CycleTab(int step)
    {
        if (_tabs.Count > 1) SelectTab((_tabIndex + step + _tabs.Count) % _tabs.Count);
    }

    private void SelectTab(int index)
    {
        if (index == _tabIndex) return;
        _tabIndex = index;
        if (!Navigate(ExistingAncestor(Tab.Path), null, addHistory: false)) UpdateTabBar();
    }

    private void UpdateTabBar()
    {
        _tabBar.Titles.Clear();
        foreach (var t in _tabs)
        {
            string name = Path.GetFileName(t.Path.TrimEnd('\\'));
            _tabBar.Titles.Add(name.Length > 0 ? name : t.Path.TrimEnd('\\'));
        }
        _tabBar.SelectedIndex = _tabIndex;
        _tabBar.Visible = _tabs.Count > 1;
        _tabBar.Invalidate();
    }

    // ───────────────────────── wyświetlanie ─────────────────────────

    public PanelView ViewMode
    {
        get => _view;
        set
        {
            _view = value;
            _list.View = value switch { PanelView.Brief => View.List, PanelView.Thumbnails => View.LargeIcon, _ => View.Details };
            if (CurrentPath.Length > 0) Populate(FocusedEntry?.Name);
        }
    }

    public void SetListFont(Font font)
    {
        _list.Font = font;
        _boldFont.Dispose();
        _boldFont = new Font(font, FontStyle.Bold);
        if (CurrentPath.Length > 0) Populate(FocusedEntry?.Name);
    }

    /// <summary>Po zmianie motywu.</summary>
    public void ApplyTheme()
    {
        SetActive(_active);
        _tabBar.Invalidate();
        if (CurrentPath.Length > 0) Populate(FocusedEntry?.Name);
    }

    private bool PassesFilter(FileSystemInfo e) => _filter.Length == 0 || Matches(e.Name, _filter, prefix: false);

    /// <summary>Maska z * lub ? — dopasowanie do maski; zwykły tekst — początek albo fragment nazwy.</summary>
    private static bool Matches(string name, string text, bool prefix)
    {
        if (text.IndexOfAny(new[] { '*', '?' }) >= 0)
            return FileSystemName.MatchesSimpleExpression(prefix ? text + "*" : text, name);
        return prefix ? name.StartsWith(text, StringComparison.CurrentCultureIgnoreCase)
                      : name.Contains(text, StringComparison.CurrentCultureIgnoreCase);
    }

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
                up.ForeColor = Theme.WindowText;
                items.Add(up);
            }

            foreach (var e in Sorted(_entries.Where(PassesFilter)))
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
        UpdateHeader();
        UpdateDriveCombo();
        UpdateStatus();
        if (_view == PanelView.Thumbnails) LoadThumbnails();
    }

    private void UpdateHeader()
    {
        if (IsQuickView) return;
        _header.Text = _filter.Length > 0 ? $"{CurrentPath}    [filtr: {_filter}]" : CurrentPath;
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
            // Jak w TC: nazwa bez rozszerzenia, rozszerzenie w osobnej kolumnie (tylko w widoku szczegółów).
            bool hasExt = _view == PanelView.Details && f.Extension.Length > 0 && f.Extension.Length < f.Name.Length;
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
        ApplyMarkStyle(item);
        return item;
    }

    /// <summary>Miniatury dla widoku Ctrl+Shift+F1 — w tle, w kolejności listy.</summary>
    private void LoadThumbnails()
    {
        int gen = ++_thumbGeneration;
        string parent = Path.GetDirectoryName(CurrentPath.TrimEnd('\\')) ?? CurrentPath;
        var work = _list.Items.Cast<ListViewItem>()
            .Select(i => (item: i, path: i.Tag is FileSystemInfo f ? f.FullName : parent))
            .ToList();
        var thread = new Thread(() =>
        {
            foreach (var (item, path) in work)
            {
                if (gen != _thumbGeneration) return;
                var bmp = ShellIcons.GetThumbnail(path, ThumbSize);
                if (bmp == null) continue;
                try
                {
                    BeginInvoke(() =>
                    {
                        if (gen != _thumbGeneration || item.ListView != _list) { bmp.Dispose(); return; }
                        string key = "t:" + path;
                        if (!_thumbs.Images.ContainsKey(key)) _thumbs.Images.Add(key, bmp);
                        item.ImageKey = key;
                    });
                }
                catch
                {
                    bmp.Dispose();
                    return; // okno zamknięte
                }
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
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
        var list = entries.ToList();
        var dirs = list.Where(e => e is DirectoryInfo).OrderBy(e => e, cmp);
        var files = list.Where(e => e is FileInfo).OrderBy(e => e, cmp);
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
        if (IsQuickView) return;
        int files = 0, dirs = 0, mFiles = 0, mDirs = 0;
        long total = 0, mTotal = 0;
        foreach (var e in _entries.Where(PassesFilter))
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
        _header.BackColor = active ? Theme.ActiveHeader : Theme.InactiveHeader;
        _header.ForeColor = active ? Theme.ActiveHeaderText : Theme.InactiveHeaderText;
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
        // W kolejności wyświetlania (ważne np. dla licznika przy zmianie nazw); odfiltrowane się nie liczą.
        var marked = _list.Items.Cast<ListViewItem>().Select(i => i.Tag).OfType<FileSystemInfo>()
            .Where(e => _marked.Contains(e.Name)).ToList();
        if (marked.Count > 0) return marked;
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
        item.ForeColor = marked ? Theme.Marked
            : item.Tag is FileSystemInfo h && h.Attributes.HasFlag(FileAttributes.Hidden) ? Theme.Gray
            : Theme.WindowText;
        item.Font = marked ? _boldFont : _list.Font;
    }

    public void ClearMarks()
    {
        _marked.Clear();
        foreach (ListViewItem i in _list.Items) ApplyMarkStyle(i);
        UpdateStatus();
    }

    /// <summary>Zastępuje zaznaczenie podanymi nazwami (porównywanie katalogów).</summary>
    public void SetMarks(IEnumerable<string> names)
    {
        _marked.Clear();
        _marked.UnionWith(names);
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
        FocusItem(_list.Items[item.Index + 1]);
    }

    private static void FocusItem(ListViewItem item)
    {
        item.Focused = true;
        item.Selected = true;
        item.EnsureVisible();
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

    // ───────────────────────── szybkie wyszukiwanie i filtr ─────────────────────────

    /// <summary>Ctrl+S — skok do pliku po wpisaniu początku nazwy; Ctrl+F — pokazuje tylko pasujące pliki.</summary>
    public void StartQuickSearch(bool filter)
    {
        if (IsQuickView) return;
        _filterMode = filter;
        _quickLabel.Text = filter ? "Filtr:" : "Szukaj:";
        _quickBox.Text = filter ? _filter : "";
        _quickBar.Visible = true;
        _quickBox.Focus();
        _quickBox.SelectAll();
    }

    private void OnQuickTextChanged()
    {
        if (!_quickBar.Visible) return;
        string text = _quickBox.Text;
        if (_filterMode)
        {
            _filter = text;
            Populate(FocusedEntry?.Name);
        }
        else if (text.Length > 0)
        {
            JumpToMatch(text, 0, +1);
        }
    }

    private void JumpToMatch(string text, int start, int step)
    {
        int count = _list.Items.Count;
        if (count == 0) return;
        // Najpierw początek nazwy, potem dowolny fragment.
        foreach (bool prefix in new[] { true, false })
        {
            for (int n = 0; n < count; n++)
            {
                int i = ((start + n * step) % count + count) % count;
                if (_list.Items[i].Tag is FileSystemInfo e && Matches(e.Name, text, prefix))
                {
                    FocusItem(_list.Items[i]);
                    return;
                }
            }
        }
    }

    private void OnQuickKeyDown(object? sender, KeyEventArgs e)
    {
        int current = _list.FocusedItem?.Index ?? 0;
        switch (e.KeyCode)
        {
            case Keys.Escape:
                if (_filterMode) CloseFilter(repopulate: true);
                _quickBar.Visible = false;
                _list.Focus();
                break;
            case Keys.Enter:
                if (!_filterMode) { _quickBar.Visible = false; _list.Focus(); OpenFocused(); }
                else { _list.Focus(); if (_filter.Length == 0) _quickBar.Visible = false; }
                break;
            case Keys.Down when !_filterMode:
                JumpToMatch(_quickBox.Text, current + 1, +1);
                break;
            case Keys.Up when !_filterMode:
                JumpToMatch(_quickBox.Text, current - 1, -1);
                break;
            case Keys.Down:
            case Keys.Up:
                _list.Focus();
                break;
            default:
                return;
        }
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    private void CloseFilter(bool repopulate)
    {
        _filter = "";
        _filterMode = false;
        _quickBar.Visible = false;
        if (repopulate) Populate(FocusedEntry?.Name);
    }

    // ───────────────────────── szybki podgląd ─────────────────────────

    /// <summary>Ctrl+Q — zamiast listy plików panel pokazuje podgląd pliku spod kursora drugiego panelu.</summary>
    public void SetQuickView(bool on)
    {
        if (on == IsQuickView) return;
        if (on)
        {
            if (_viewer == null)
            {
                _viewer = new ViewerControl(2 * 1024 * 1024) { Dock = DockStyle.Fill };
                Controls.Add(_viewer);
                Controls.SetChildIndex(_viewer, 0);
                Theme.Apply(_viewer);
            }
            _quickBar.Visible = false;
            _list.Visible = false;
            _viewer.Visible = true;
            _header.Text = "Szybki podgląd";
            _status.Text = "";
        }
        else
        {
            _previewTimer.Stop();
            _viewer!.Visible = false;
            _viewer.ShowText("");
            _list.Visible = true;
            Reload();
        }
    }

    public void ShowPreview(FileSystemInfo? entry)
    {
        _pendingPreview = entry;
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    public void FocusPreview() => _viewer?.FocusContent();

    public bool PreviewHasFocus => _viewer is { Visible: true } v && v.ContainsFocus;

    private void UpdatePreview()
    {
        if (_viewer == null || !IsQuickView) return;
        var e = _pendingPreview;
        _header.Text = e == null ? "Szybki podgląd" : $"Szybki podgląd — {e.Name}";
        switch (e)
        {
            case FileInfo f:
                _viewer.ShowFile(f.FullName);
                _status.Text = $"{f.Name}    {FormatSize(SafeLength(f))}    {SafeDate(f):yyyy-MM-dd HH:mm}";
                break;
            case DirectoryInfo d:
                int files = 0, dirs = 0;
                long size = 0;
                try
                {
                    foreach (var x in d.EnumerateFileSystemInfos("*", new EnumerationOptions { IgnoreInaccessible = true }))
                        if (x is FileInfo xf) { files++; size += SafeLength(xf); } else dirs++;
                }
                catch { }
                _viewer.ShowText($"Katalog: {d.FullName}\n\nPlików: {files}  ({FormatSize(size)}, bez podkatalogów)\nKatalogów: {dirs}\nZmieniony: {SafeDate(d):yyyy-MM-dd HH:mm}");
                _status.Text = d.Name;
                break;
            default:
                _viewer.ShowText("");
                _status.Text = "";
                break;
        }
    }

    // ───────────────────────── mysz, przeciąganie, menu kontekstowe ─────────────────────────

    private void OnListMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right) return;
        var item = _list.GetItemAt(e.X, e.Y);
        if (item != null) FocusItem(item);

        if (RightClickMenu)
        {
            var point = _list.PointToScreen(e.Location);
            BeginInvoke(() => RequestContextMenu(item, point));
        }
        else if (item != null)
        {
            // Prawy przycisk zaznacza/odznacza, jak w TC.
            ToggleMark(item);
        }
    }

    /// <summary>Shift+F10 / klawisz menu — menu kontekstowe dla zaznaczenia.</summary>
    public void RequestKeyboardContextMenu()
    {
        var item = _list.FocusedItem;
        var point = item != null
            ? _list.PointToScreen(new Point(item.Bounds.Left + 20, item.Bounds.Bottom))
            : _list.PointToScreen(new Point(20, 20));
        var selection = GetSelection();
        if (selection.Count == 0) selection.Add(new DirectoryInfo(CurrentPath));
        ContextMenuRequested?.Invoke(selection, point);
    }

    private void RequestContextMenu(ListViewItem? item, Point screen)
    {
        List<FileSystemInfo> selection = item?.Tag is FileSystemInfo fi
            ? (_marked.Contains(fi.Name) ? GetSelection() : new List<FileSystemInfo> { fi })
            : new List<FileSystemInfo> { new DirectoryInfo(CurrentPath) };
        ContextMenuRequested?.Invoke(selection, screen);
    }

    private void OnItemDrag(object? sender, ItemDragEventArgs e)
    {
        if (e.Button != MouseButtons.Left || e.Item is not ListViewItem { Tag: FileSystemInfo dragged }) return;
        var items = _marked.Contains(dragged.Name) ? GetSelection() : new List<FileSystemInfo> { dragged };
        var paths = new StringCollection();
        paths.AddRange(items.Select(i => i.FullName).ToArray());
        var data = new DataObject();
        data.SetFileDropList(paths);
        _draggedPaths = paths.Cast<string>().ToArray();
        try
        {
            _list.DoDragDrop(data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
        }
        finally
        {
            _draggedPaths = null;
        }
    }

    /// <summary>Katalog, do którego trafią upuszczone pliki: katalog pod kursorem albo bieżący.</summary>
    private string DropTarget(int screenX, int screenY)
    {
        var pt = _list.PointToClient(new Point(screenX, screenY));
        var item = _list.GetItemAt(pt.X, pt.Y);
        if (item == null) return CurrentPath;
        if (item.Tag == null) return Path.GetDirectoryName(CurrentPath.TrimEnd('\\')) ?? CurrentPath;
        if (item.Tag is DirectoryInfo d && !(_draggedPaths?.Contains(d.FullName, StringComparer.OrdinalIgnoreCase) ?? false)) return d.FullName;
        return CurrentPath;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } paths)
        {
            e.Effect = DragDropEffects.None;
            return;
        }
        string target = DropTarget(e.X, e.Y);
        // Upuszczenie na ten sam katalog, z którego pliki pochodzą, nic nie robi.
        if (paths.All(p => string.Equals(Path.GetDirectoryName(p), target.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(Path.GetDirectoryName(p), target, StringComparison.OrdinalIgnoreCase)))
        {
            e.Effect = DragDropEffects.None;
            return;
        }
        // Jak w Eksploratorze: Ctrl — kopiuj, Shift — przenieś, bez klawiszy — przenieś w obrębie dysku, kopiuj między dyskami.
        bool sameDrive = string.Equals(Path.GetPathRoot(paths[0]), Path.GetPathRoot(target), StringComparison.OrdinalIgnoreCase);
        var wanted = (e.KeyState & 8) != 0 ? DragDropEffects.Copy
            : (e.KeyState & 4) != 0 ? DragDropEffects.Move
            : sameDrive ? DragDropEffects.Move : DragDropEffects.Copy;
        e.Effect = (e.AllowedEffect & wanted) != 0 ? wanted
            : (e.AllowedEffect & DragDropEffects.Copy) != 0 ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } paths || e.Effect == DragDropEffects.None) return;
        string target = DropTarget(e.X, e.Y);
        bool move = e.Effect == DragDropEffects.Move;
        // Operację robimy po zakończeniu przeciągania, żeby nie blokować źródła (np. Eksploratora).
        BeginInvoke(() => FilesDropped?.Invoke(paths, target, move));
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
            case Keys.Escape when _filter.Length > 0:
                CloseFilter(repopulate: true);
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

/// <summary>Pasek zakładek nad panelem (widoczny, gdy zakładek jest więcej niż jedna).</summary>
internal sealed class TabBar : Control
{
    private readonly List<Rectangle> _rects = new();

    public List<string> Titles { get; } = new();
    public int SelectedIndex { get; set; }

    public event Action<int>? TabClicked;
    public event Action<int>? TabMiddleClicked;
    public event Action? NewTabRequested;

    public TabBar()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        TabStop = false;
        Height = Font.Height + 8;
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        Height = Font.Height + 8;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Back);
        _rects.Clear();
        int x = 2;
        using var border = new Pen(Theme.Border);
        using var selectedBack = new SolidBrush(Theme.Window);
        for (int i = 0; i < Titles.Count; i++)
        {
            int w = Math.Clamp(TextRenderer.MeasureText(Titles[i], Font).Width + 16, 50, 180);
            var r = new Rectangle(x, 2, w, Height - 3);
            _rects.Add(r);
            if (i == SelectedIndex) g.FillRectangle(selectedBack, r);
            g.DrawRectangle(border, r);
            TextRenderer.DrawText(g, Titles[i], Font, r, i == SelectedIndex ? Theme.WindowText : Theme.Gray,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            x += w + 1;
        }
    }

    private int HitTest(Point p) => _rects.FindIndex(r => r.Contains(p));

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        int i = HitTest(e.Location);
        if (i < 0) return;
        if (e.Button == MouseButtons.Left) TabClicked?.Invoke(i);
        else if (e.Button == MouseButtons.Middle) TabMiddleClicked?.Invoke(i);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (HitTest(e.Location) < 0) NewTabRequested?.Invoke();
    }
}
