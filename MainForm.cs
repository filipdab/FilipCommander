using System.Collections.Specialized;
using System.Diagnostics;

namespace FilipCommander;

public sealed class MainForm : Form
{
    private readonly Settings _settings = Settings.Load();
    private readonly FilePanel _left = new() { Dock = DockStyle.Fill };
    private readonly FilePanel _right = new() { Dock = DockStyle.Fill };
    private readonly SplitContainer _split;
    private readonly Label _cmdPath;
    private readonly TextBox _cmdLine;
    private readonly ToolStripMenuItem _hiddenItem;
    private readonly ToolStripMenuItem _rightClickItem;
    private readonly Dictionary<PanelView, ToolStripMenuItem> _viewItems = new();
    private readonly Dictionary<string, ToolStripMenuItem> _themeItems = new();
    private FilePanel _active;

    private FilePanel Other => _active == _left ? _right : _left;

    public MainForm()
    {
        Theme.Set(_settings.Theme);
        _active = _left;
        _cmdLine = new TextBox { Dock = DockStyle.Fill };
        _cmdPath = new Label { Dock = DockStyle.Left, AutoSize = true, TextAlign = ContentAlignment.MiddleRight, Padding = new Padding(0, 3, 4, 0) };

        Text = "FilipCommander";
        KeyPreview = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9f);
        MinimumSize = new Size(700, 400);
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        // ── menu ──
        var menu = new MenuStrip();
        var file = new ToolStripMenuItem("&Plik");
        file.DropDownItems.Add(Item("&Podgląd", Keys.F3, (_, _) => ViewFile()));
        file.DropDownItems.Add(Item("&Edycja", Keys.F4, (_, _) => EditFile()));
        file.DropDownItems.Add(Item("&Kopiuj…", Keys.F5, (_, _) => Transfer(FileOps.Kind.Copy)));
        file.DropDownItems.Add(Item("P&rzenieś…", Keys.F6, (_, _) => Transfer(FileOps.Kind.Move)));
        file.DropDownItems.Add(Item("&Zmień nazwę…", Keys.Shift | Keys.F6, (_, _) => RenameFile()));
        file.DropDownItems.Add(Item("Zmień nazwy &wielu plików…", Keys.Control | Keys.M, (_, _) => MultiRename()));
        file.DropDownItems.Add(Item("&Nowy folder…", Keys.F7, (_, _) => MakeDirectory()));
        file.DropDownItems.Add(Item("&Nowy plik tekstowy…", Keys.Shift | Keys.F4, (_, _) => NewTextFile()));
        file.DropDownItems.Add(Item("&Usuń", Keys.F8, (_, _) => Delete(false)));
        file.DropDownItems.Add(Item("Usuń &trwale", Keys.Shift | Keys.Delete, (_, _) => Delete(true)));
        file.DropDownItems.Add(Item("&Właściwości", Keys.Alt | Keys.Enter, (_, _) => ShowProperties()));
        file.DropDownItems.Add(Item("&Atrybuty i daty…", Keys.None, (_, _) => ChangeAttributes()));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Item("&Spakuj (ZIP, TAR.GZ)…", Keys.Alt | Keys.F5, (_, _) => Pack()));
        file.DropDownItems.Add(Item("&Rozpakuj (ZIP, 7z, RAR, TAR…)…", Keys.Alt | Keys.F9, (_, _) => Unpack()));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Item("Oblicz sumy &kontrolne…", Keys.None, (_, _) => CreateChecksums()));
        file.DropDownItems.Add(Item("Sprawdź sumy kontrolne", Keys.None, (_, _) => VerifyChecksums()));
        file.DropDownItems.Add(Item("Po&dziel plik…", Keys.None, (_, _) => SplitFile()));
        file.DropDownItems.Add(Item("Po&łącz pliki (.001)…", Keys.None, (_, _) => JoinFiles()));
        file.DropDownItems.Add(Item("Porównaj pliki wg &zawartości…", Keys.None, (_, _) => CompareFiles()));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Item("Sz&ukaj plików…", Keys.Alt | Keys.F7, (_, _) => Search()));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Item("Wy&jście", Keys.Alt | Keys.F4, (_, _) => Close()));

        var edit = new ToolStripMenuItem("&Edycja");
        edit.DropDownItems.Add(Item("&Wytnij", Keys.Control | Keys.X, (_, _) => CopyToClipboard(cut: true)));
        edit.DropDownItems.Add(Item("&Kopiuj", Keys.Control | Keys.C, (_, _) => CopyToClipboard(cut: false)));
        edit.DropDownItems.Add(Item("W&klej", Keys.Control | Keys.V, (_, _) => Paste()));
        edit.DropDownItems.Add(Item("Kopiuj ś&cieżki", Keys.Control | Keys.Shift | Keys.C, (_, _) => CopyPaths()));
        edit.DropDownItems.Add(new ToolStripSeparator());
        edit.DropDownItems.Add(Item("&Menu kontekstowe Windows", Keys.Shift | Keys.F10, (_, _) => _active.RequestKeyboardContextMenu()));

        var mark = new ToolStripMenuItem("&Zaznacz");
        mark.DropDownItems.Add(Item("Zaznacz &wszystko", Keys.Control | Keys.A, (_, _) => _active.MarkByPattern("*", true)));
        mark.DropDownItems.Add(Item("&Odznacz wszystko", Keys.None, (_, _) => _active.ClearMarks()));
        mark.DropDownItems.Add(Item("Odwróć zaznaczenie", Keys.None, (_, _) => _active.InvertMarks()));
        mark.DropDownItems.Add(new ToolStripSeparator());
        mark.DropDownItems.Add(Item("&Szybkie wyszukiwanie", Keys.Control | Keys.S, (_, _) => _active.StartQuickSearch(filter: false)));
        mark.DropDownItems.Add(Item("&Filtr", Keys.Control | Keys.F, (_, _) => _active.StartQuickSearch(filter: true)));
        mark.DropDownItems.Add(Item("&Porównaj katalogi", Keys.Shift | Keys.F2, (_, _) => CompareDirectories()));

        var cmd = new ToolStripMenuItem("&Polecenia");
        cmd.DropDownItems.Add(Item("&Odśwież", Keys.Control | Keys.R, (_, _) => { _left.Reload(); _right.Reload(); }));
        cmd.DropDownItems.Add(Item("&Zamień panele", Keys.Control | Keys.U, (_, _) => SwapPanels()));
        cmd.DropDownItems.Add(Item("&Ten sam katalog w drugim panelu", Keys.Control | Keys.E, (_, _) => Other.Navigate(_active.CurrentPath)));
        cmd.DropDownItems.Add(new ToolStripSeparator());
        cmd.DropDownItems.Add(Item("&Wstecz", Keys.Alt | Keys.Left, (_, _) => _active.GoBack()));
        cmd.DropDownItems.Add(Item("&Dalej", Keys.Alt | Keys.Right, (_, _) => _active.GoForward()));
        cmd.DropDownItems.Add(Item("&Historia katalogów", Keys.Alt | Keys.Down, (_, _) => _active.ShowHistoryMenu()));
        cmd.DropDownItems.Add(Item("&Ulubione katalogi", Keys.Control | Keys.D, (_, _) => ShowHotlist()));
        cmd.DropDownItems.Add(new ToolStripSeparator());
        cmd.DropDownItems.Add(Item("&Nowa zakładka", Keys.Control | Keys.T, (_, _) => _active.NewTab()));
        cmd.DropDownItems.Add(Item("Zamknij &zakładkę", Keys.Control | Keys.W, (_, _) => _active.CloseTab()));
        cmd.DropDownItems.Add(Item("Następna zakładka", Keys.Control | Keys.Tab, (_, _) => _active.CycleTab(+1)));
        cmd.DropDownItems.Add(new ToolStripSeparator());
        cmd.DropDownItems.Add(Item("Szybki &podgląd", Keys.Control | Keys.Q, (_, _) => ToggleQuickView()));
        cmd.DropDownItems.Add(Item("&Synchronizuj katalogi…", Keys.None, (_, _) => SynchronizeDirectories()));
        cmd.DropDownItems.Add(new ToolStripSeparator());
        cmd.DropDownItems.Add(Item("Wiersz &poleceń tutaj", Keys.Control | Keys.Shift | Keys.T, (_, _) => OpenTerminal()));
        cmd.DropDownItems.Add(Item("Otwórz w &Eksploratorze", Keys.Control | Keys.O, (_, _) => OpenExplorer()));
        cmd.DropDownItems.Add(Item("&Lewy panel — dysk", Keys.Alt | Keys.F1, (_, _) => _left.DropDownDrives()));
        cmd.DropDownItems.Add(Item("P&rawy panel — dysk", Keys.Alt | Keys.F2, (_, _) => _right.DropDownDrives()));

        var view = new ToolStripMenuItem("&Widok");
        foreach (var (mode, label, keys) in new[]
                 {
                     (PanelView.Details, "&Szczegóły", Keys.Control | Keys.F2),
                     (PanelView.Brief, "&Krótki (same nazwy)", Keys.Control | Keys.F1),
                     (PanelView.Thumbnails, "&Miniatury", Keys.Control | Keys.Shift | Keys.F1)
                 })
        {
            var m = mode;
            _viewItems[m] = Item(label, keys, (_, _) => SetView(m));
            view.DropDownItems.Add(_viewItems[m]);
        }
        view.DropDownItems.Add(new ToolStripSeparator());
        _hiddenItem = Item("Pokaż pliki &ukryte/systemowe", Keys.Control | Keys.H, (_, _) => ToggleHidden());
        _hiddenItem.Checked = _settings.ShowHidden;
        view.DropDownItems.Add(_hiddenItem);
        var themeMenu = new ToolStripMenuItem("&Motyw");
        foreach (var (key, label) in new[] { ("system", "Jak w &systemie"), ("light", "&Jasny"), ("dark", "&Ciemny") })
        {
            string k = key;
            _themeItems[k] = Item(label, Keys.None, (_, _) => SetTheme(k));
            themeMenu.DropDownItems.Add(_themeItems[k]);
        }
        view.DropDownItems.Add(themeMenu);
        view.DropDownItems.Add(Item("&Czcionka paneli…", Keys.None, (_, _) => ChooseFont()));
        _rightClickItem = Item("&Prawy przycisk otwiera menu kontekstowe", Keys.None, (_, _) => ToggleRightClickMenu());
        _rightClickItem.Checked = _settings.RightClickMenu;
        view.DropDownItems.Add(_rightClickItem);
        view.DropDownItems.Add(new ToolStripSeparator());
        view.DropDownItems.Add(Item("&Edytor dla F4…", Keys.None, (_, _) => ChooseEditor()));

        var help = new ToolStripMenuItem("Pomo&c");
        help.DropDownItems.Add(Item("&Skróty klawiszowe", Keys.F1, (_, _) => ShowHelp()));
        help.DropDownItems.Add(new ToolStripSeparator());
        help.DropDownItems.Add(Item("&O programie…", Keys.None, (_, _) => { using var about = new AboutForm(); about.ShowDialog(this); }));

        menu.Items.AddRange(new ToolStripItem[] { file, edit, mark, cmd, view, help });
        MainMenuStrip = menu;

        // ── panele ──
        _split = new SplitContainer { Dock = DockStyle.Fill, SplitterWidth = 5, TabStop = false };
        _split.Panel1.Controls.Add(_left);
        _split.Panel2.Controls.Add(_right);
        _split.SizeChanged += (_, _) => { if (_split.Width > 50) _split.SplitterDistance = _split.Width / 2; };

        var listFont = CreateListFont();
        foreach (var p in new[] { _left, _right })
        {
            p.ShowHidden = _settings.ShowHidden;
            p.RightClickMenu = _settings.RightClickMenu;
            p.SetListFont(listFont);
            p.PanelActivated += (s, _) => SetActive((FilePanel)s!);
            p.PathChanged += (s, _) => { if (s == _active) UpdateCmdPath(); };
            p.FocusedEntryChanged += (s, _) => { if (s == _active && Other.IsQuickView) Other.ShowPreview(_active.FocusedEntry); };
            p.InsertNameRequested += name =>
            {
                string n = name.Contains(' ') ? $"\"{name}\"" : name;
                _cmdLine.Text = _cmdLine.Text.Length == 0 ? n : _cmdLine.Text.TrimEnd() + " " + n;
            };
            p.FilesDropped += (paths, target, move) => TransferPaths(paths, target, move);
            p.ContextMenuRequested += ShowShellMenu;
        }
        _left.ViewMode = _settings.LeftView;
        _right.ViewMode = _settings.RightView;

        // ── wiersz poleceń ──
        var cmdPanel = new Panel { Dock = DockStyle.Bottom, Height = 26, Padding = new Padding(4, 2, 4, 2) };
        _cmdLine.KeyDown += OnCmdKeyDown;
        cmdPanel.Controls.Add(_cmdLine);
        cmdPanel.Controls.Add(_cmdPath);

        // ── przyciski F3–F8 ──
        var buttons = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 30, ColumnCount = 7, RowCount = 1 };
        var defs = new (string text, Action action)[]
        {
            ("F3 Podgląd", ViewFile),
            ("F4 Edycja", EditFile),
            ("F5 Kopiuj", () => Transfer(FileOps.Kind.Copy)),
            ("F6 Przenieś", () => Transfer(FileOps.Kind.Move)),
            ("F7 Nowy folder", MakeDirectory),
            ("F8 Usuń", () => Delete(false)),
            ("Alt+F4 Wyjście", Close)
        };
        for (int i = 0; i < defs.Length; i++)
        {
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / defs.Length));
            var (text, action) = defs[i];
            var b = new Button { Text = text, Dock = DockStyle.Fill, TabStop = false, FlatStyle = FlatStyle.System, Margin = new Padding(1) };
            b.Click += (_, _) => { action(); _active.List.Focus(); };
            buttons.Controls.Add(b, i, 0);
        }

        Controls.Add(_split);
        Controls.Add(cmdPanel);
        Controls.Add(buttons);
        Controls.Add(menu);

        ApplyTheme();
        RestoreWindow();
    }

    private static ToolStripMenuItem Item(string text, Keys keys, EventHandler onClick)
    {
        // Skróty obsługujemy sami w ProcessCmdKey — tu tylko je pokazujemy.
        var item = new ToolStripMenuItem(text, null, onClick);
        if (keys != Keys.None) item.ShortcutKeyDisplayString = new KeysConverter().ConvertToString(keys)?.Replace("Control", "Ctrl");
        return item;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        string fallback = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        // Starsze ustawienia nie mają zakładek — wtedy jedna zakładka z zapamiętanym katalogiem.
        _left.RestoreTabs(_settings.LeftTabs.Count > 0 ? _settings.LeftTabs : new List<string> { _settings.LeftPath }, _settings.LeftTab, fallback);
        _right.RestoreTabs(_settings.RightTabs.Count > 0 ? _settings.RightTabs : new List<string> { _settings.RightPath }, _settings.RightTab, home);
        SetActive(_settings.RightActive ? _right : _left);
        _active.List.Focus();
    }

    private void SetActive(FilePanel panel)
    {
        _active = panel;
        _left.SetActive(panel == _left);
        _right.SetActive(panel == _right);
        UpdateCmdPath();
        foreach (var (mode, item) in _viewItems) item.Checked = _active.ViewMode == mode;
    }

    private void UpdateCmdPath() => _cmdPath.Text = _active.CurrentPath + ">";

    // ───────────────────────── klawiatura ─────────────────────────

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        bool inList = _left.List.Focused || _right.List.Focused;

        switch (keyData)
        {
            case Keys.Tab:
            case Keys.Shift | Keys.Tab:
                // Gdy drugi panel to szybki podgląd, Tab przenosi fokus do podglądu i z powrotem.
                if (Other.IsQuickView)
                {
                    if (Other.PreviewHasFocus) _active.List.Focus(); else Other.FocusPreview();
                    return true;
                }
                SetActive(Other);
                _active.List.Focus();
                return true;
            case Keys.Control | Keys.Tab: _active.CycleTab(+1); return true;
            case Keys.Control | Keys.Shift | Keys.Tab: _active.CycleTab(-1); return true;
            case Keys.F1: ShowHelp(); return true;
            case Keys.F3: ViewFile(); return true;
            case Keys.F4: EditFile(); return true;
            case Keys.Shift | Keys.F4: NewTextFile(); return true;
            case Keys.F5: Transfer(FileOps.Kind.Copy); return true;
            case Keys.F6: Transfer(FileOps.Kind.Move); return true;
            case Keys.Shift | Keys.F6: RenameFile(); return true;
            case Keys.F7: MakeDirectory(); return true;
            case Keys.F8: Delete(false); return true;
            case Keys.Shift | Keys.F2: CompareDirectories(); return true;
            case Keys.Shift | Keys.F10 when inList:
            case Keys.Apps when inList:
                _active.RequestKeyboardContextMenu();
                return true;
            case Keys.Delete when inList: Delete(false); return true;
            case Keys.Shift | Keys.Delete when inList: Delete(true); return true;
            case Keys.Alt | Keys.Enter when inList: ShowProperties(); return true;
            case Keys.Alt | Keys.Left when inList: _active.GoBack(); return true;
            case Keys.Alt | Keys.Right when inList: _active.GoForward(); return true;
            case Keys.Alt | Keys.Down when inList: _active.ShowHistoryMenu(); return true;
            case Keys.Alt | Keys.F1: _left.DropDownDrives(); return true;
            case Keys.Alt | Keys.F2: _right.DropDownDrives(); return true;
            case Keys.Alt | Keys.F5: Pack(); return true;
            case Keys.Alt | Keys.F7: Search(); return true;
            case Keys.Alt | Keys.F9: Unpack(); return true;
            case Keys.Control | Keys.F1: SetView(PanelView.Brief); return true;
            case Keys.Control | Keys.F2: SetView(PanelView.Details); return true;
            case Keys.Control | Keys.Shift | Keys.F1: SetView(PanelView.Thumbnails); return true;
            case Keys.Control | Keys.C when inList: CopyToClipboard(cut: false); return true;
            case Keys.Control | Keys.X when inList: CopyToClipboard(cut: true); return true;
            case Keys.Control | Keys.V when inList: Paste(); return true;
            case Keys.Control | Keys.Shift | Keys.C when inList: CopyPaths(); return true;
            case Keys.Control | Keys.S when inList: _active.StartQuickSearch(filter: false); return true;
            case Keys.Control | Keys.F when inList: _active.StartQuickSearch(filter: true); return true;
            case Keys.Control | Keys.D: ShowHotlist(); return true;
            case Keys.Control | Keys.M: MultiRename(); return true;
            case Keys.Control | Keys.Q: ToggleQuickView(); return true;
            case Keys.Control | Keys.T: _active.NewTab(); _active.List.Focus(); return true;
            case Keys.Control | Keys.W: _active.CloseTab(); _active.List.Focus(); return true;
            case Keys.Control | Keys.R: _left.Reload(); _right.Reload(); return true;
            case Keys.Control | Keys.H: ToggleHidden(); return true;
            case Keys.Control | Keys.U: SwapPanels(); return true;
            case Keys.Control | Keys.E: Other.Navigate(_active.CurrentPath); return true;
            case Keys.Control | Keys.Shift | Keys.T: OpenTerminal(); return true;
            case Keys.Control | Keys.O: OpenExplorer(); return true;
            case Keys.Control | Keys.Left when inList: PanelFromCursor(_left); return true;
            case Keys.Control | Keys.Right when inList: PanelFromCursor(_right); return true;
            case Keys.Escape when _cmdLine.Focused:
                _cmdLine.Clear();
                _active.List.Focus();
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        // Pisanie w liście trafia do wiersza poleceń (jak w TC), chyba że to zwykłe szukanie po literze z Alt.
        if ((_left.List.Focused || _right.List.Focused) && !char.IsControl(e.KeyChar) &&
            e.KeyChar != ' ' && e.KeyChar != '+' && e.KeyChar != '-' && e.KeyChar != '*' && ModifierKeys == Keys.None)
        {
            _cmdLine.Focus();
            _cmdLine.AppendText(e.KeyChar.ToString());
            e.Handled = true;
            return;
        }
        base.OnKeyPress(e);
    }

    private void OnCmdKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)
        {
            _active.List.Focus();
            return;
        }
        if (e.KeyCode != Keys.Enter) return;
        e.SuppressKeyPress = true;
        string command = _cmdLine.Text.Trim();
        _cmdLine.Clear();
        _active.List.Focus();
        if (command.Length == 0) return;

        if (command.Equals("cd..", StringComparison.OrdinalIgnoreCase)) { _active.GoUp(); return; }
        if (command.StartsWith("cd ", StringComparison.OrdinalIgnoreCase) || command == "~" || command.StartsWith("~\\") || command.StartsWith("~/"))
        {
            string arg = command.StartsWith("cd ", StringComparison.OrdinalIgnoreCase) ? command[3..].Trim().Trim('"') : command;
            arg = Environment.ExpandEnvironmentVariables(arg);
            // ~ — katalog domowy, jak w PowerShellu.
            if (arg == "~" || arg.StartsWith("~\\") || arg.StartsWith("~/"))
                arg = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + arg[1..];
            if (arg == "..") _active.GoUp();
            else if (arg == "\\") _active.GoRoot();
            else _active.Navigate(Path.GetFullPath(Path.Combine(_active.CurrentPath, arg)));
            return;
        }
        if (command.Length == 2 && command[1] == ':')
        {
            _active.Navigate(command.ToUpperInvariant() + "\\");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("cmd.exe", "/k " + command)
            {
                WorkingDirectory = _active.CurrentPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Error(ex.Message);
        }
    }

    // ───────────────────────── polecenia ─────────────────────────

    private void ViewFile()
    {
        if (_active.FocusedEntry is FileInfo f)
            new ViewerForm(f.FullName).Show(this);
        else if (_active.FocusedEntry is DirectoryInfo)
            _active.OpenFocused();
    }

    private void EditFile()
    {
        if (_active.FocusedEntry is not FileInfo f) return;
        try
        {
            Process.Start(new ProcessStartInfo(_settings.Editor, $"\"{f.FullName}\"")
            {
                UseShellExecute = true,
                WorkingDirectory = _active.CurrentPath
            });
        }
        catch (Exception ex)
        {
            Error($"Nie można uruchomić edytora „{_settings.Editor}”:\n{ex.Message}");
        }
    }

    private void NewTextFile()
    {
        var name = PromptDialog.Show(this, "Nowy plik", "Nazwa nowego pliku:", "nowy.txt", selectNameOnly: true);
        if (string.IsNullOrEmpty(name)) return;
        string path = Path.Combine(_active.CurrentPath, name);
        try
        {
            if (!File.Exists(path)) File.WriteAllText(path, "");
            _active.Navigate(_active.CurrentPath, Path.GetFileName(path));
            EditFile();
        }
        catch (Exception ex)
        {
            Error(ex.Message);
        }
    }

    private async void Transfer(FileOps.Kind kind)
    {
        var items = _active.GetSelection();
        if (items.Count == 0) return;

        string what = items.Count == 1 ? $"„{items[0].Name}”" : $"{items.Count} element(y/ów)";
        string verb = kind == FileOps.Kind.Copy ? "Kopiuj" : "Przenieś";
        string defaultTarget = Path.Combine(Other.CurrentPath, "").TrimEnd('\\') + "\\";
        // Przenoszenie pojedynczego elementu w obrębie tego samego katalogu = zmiana nazwy.
        if (kind == FileOps.Kind.Move && items.Count == 1 &&
            string.Equals(Other.CurrentPath, _active.CurrentPath, StringComparison.OrdinalIgnoreCase))
            defaultTarget = items[0].Name;

        var target = PromptDialog.Show(this, verb, $"{verb} {what} do:", defaultTarget);
        if (string.IsNullOrEmpty(target)) return;
        if (!Path.IsPathRooted(target)) target = Path.Combine(_active.CurrentPath, target);

        try
        {
            await FileOps.TransferAsync(kind, items, target);
            _active.ClearMarks();
        }
        catch (Exception ex)
        {
            Error(ex.Message);
        }
        _left.Reload();
        _right.Reload();
    }

    private void RenameFile()
    {
        if (_active.FocusedEntry is not { } item) return;
        var name = PromptDialog.Show(this, "Zmień nazwę", $"Nowa nazwa dla „{item.Name}”:", item.Name, selectNameOnly: item is FileInfo);
        if (string.IsNullOrEmpty(name) || name == item.Name) return;
        try
        {
            FileOps.Rename(item, name);
            _active.Navigate(_active.CurrentPath, name);
        }
        catch (Exception ex)
        {
            Error(ex.Message);
        }
    }

    private void MultiRename()
    {
        var items = _active.GetSelection();
        if (items.Count == 0) return;
        using var form = new MultiRenameForm(items);
        form.ShowDialog(this);
        if (!form.Changed) return;
        _active.ClearMarks();
        _left.Reload();
        _right.Reload();
    }

    private void MakeDirectory()
    {
        var name = PromptDialog.Show(this, "Nowy folder", "Nazwa nowego folderu (można podać ścieżkę a\\b\\c):", "");
        if (string.IsNullOrEmpty(name)) return;
        try
        {
            var dir = Directory.CreateDirectory(Path.Combine(_active.CurrentPath, name));
            // Kursor na pierwszym poziomie nowo utworzonej ścieżki.
            string first = name.Split('\\', '/')[0];
            _active.Navigate(_active.CurrentPath, Path.IsPathRooted(name) ? dir.Name : first);
        }
        catch (Exception ex)
        {
            Error(ex.Message);
        }
    }

    private async void Delete(bool permanently)
    {
        var items = _active.GetSelection();
        if (items.Count == 0) return;

        string what = items.Count == 1 ? $"„{items[0].Name}”" : $"{items.Count} zaznaczonych elementów";
        string question = permanently
            ? $"Usunąć TRWALE {what}?\nTej operacji nie można cofnąć."
            : $"Przenieść {what} do kosza?";
        if (MessageBox.Show(this, question, "Usuń", MessageBoxButtons.YesNo,
                permanently ? MessageBoxIcon.Warning : MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button1) != DialogResult.Yes)
            return;

        try
        {
            await FileOps.DeleteAsync(items, permanently);
            _active.ClearMarks();
        }
        catch (Exception ex)
        {
            Error(ex.Message);
        }
        _left.Reload();
        _right.Reload();
    }

    private void ChangeAttributes()
    {
        var items = _active.GetSelection();
        if (items.Count == 0) return;
        using var form = new AttributesForm(items);
        if (form.ShowDialog(this) == DialogResult.OK) _active.Reload();
    }

    // ───────────────────────── schowek, przeciąganie, menu powłoki ─────────────────────────

    private void CopyToClipboard(bool cut)
    {
        var items = _active.GetSelection();
        if (items.Count == 0) return;
        var paths = new StringCollection();
        paths.AddRange(items.Select(i => i.FullName).ToArray());
        var data = new DataObject();
        data.SetFileDropList(paths);
        // Tak samo jak Eksplorator: 2 = przenieś (wytnij), 5 = kopiuj/skrót.
        data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(cut ? 2 : 5)));
        try { Clipboard.SetDataObject(data, true); }
        catch (Exception ex) { Error("Nie udało się skopiować do schowka:\n" + ex.Message); }
    }

    private void Paste()
    {
        try
        {
            if (!Clipboard.ContainsFileDropList()) return;
            var paths = Clipboard.GetFileDropList().Cast<string>().ToArray();
            bool move = false;
            if (Clipboard.GetData("Preferred DropEffect") is MemoryStream ms && ms.Length >= 4)
            {
                var b = new byte[4];
                ms.ReadExactly(b);
                move = (BitConverter.ToInt32(b) & 2) != 0;
            }
            TransferPaths(paths, _active.CurrentPath, move);
            // Wycięte pliki wkleja się tylko raz.
            if (move) Clipboard.Clear();
        }
        catch (Exception ex)
        {
            Error("Nie udało się wkleić:\n" + ex.Message);
        }
    }

    private void CopyPaths()
    {
        var items = _active.GetSelection();
        if (items.Count == 0) return;
        Clipboard.SetText(string.Join(Environment.NewLine, items.Select(i => i.FullName)));
    }

    /// <summary>Kopiuje/przenosi pliki (wklejanie, przeciąganie). Kopia do tego samego katalogu dostaje nazwę „… - kopia”.</summary>
    private async void TransferPaths(string[] paths, string targetDir, bool move)
    {
        var items = paths
            .Select(p => Directory.Exists(p) ? (FileSystemInfo)new DirectoryInfo(p) : new FileInfo(p))
            .Where(i => i.Exists)
            .ToList();
        string target = targetDir.TrimEnd('\\');
        var sameDir = items.Where(i => string.Equals(Path.GetDirectoryName(i.FullName.TrimEnd('\\'))?.TrimEnd('\\'), target,
            StringComparison.OrdinalIgnoreCase)).ToList();
        var others = items.Except(sameDir).ToList();
        try
        {
            if (others.Count > 0)
                await FileOps.TransferAsync(move ? FileOps.Kind.Move : FileOps.Kind.Copy, others, target + "\\");
            if (!move)
                foreach (var s in sameDir)
                    await FileOps.TransferAsync(FileOps.Kind.Copy, new[] { s }, UniqueCopyName(s));
        }
        catch (Exception ex)
        {
            Error(ex.Message);
        }
        _left.Reload();
        _right.Reload();
    }

    private static string UniqueCopyName(FileSystemInfo item)
    {
        string dir = Path.GetDirectoryName(item.FullName.TrimEnd('\\'))!;
        string name = item is FileInfo ? Path.GetFileNameWithoutExtension(item.Name) : item.Name;
        string ext = item is FileInfo ? item.Extension : "";
        for (int i = 1; ; i++)
        {
            string candidate = Path.Combine(dir, $"{name} - kopia{(i > 1 ? $" ({i})" : "")}{ext}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
    }

    private void ShowShellMenu(List<FileSystemInfo> items, Point screen)
    {
        try
        {
            var verb = ShellContextMenu.Show(this, items.Select(i => i.FullName).ToList(), screen);
            if (verb == "rename") RenameFile();
        }
        catch (Exception ex)
        {
            Error("Menu kontekstowe nie zadziałało:\n" + ex.Message);
        }
    }

    // ───────────────────────── szukanie, archiwa, narzędzia ─────────────────────────

    private SearchForm? _search;

    private void Search()
    {
        if (_search is { IsDisposed: false })
        {
            _search.Activate();
            return;
        }
        _search = new SearchForm(_active.CurrentPath, path =>
        {
            // Przejdź w aktywnym panelu do znalezionego pliku/katalogu.
            string? dir = Path.GetDirectoryName(path);
            if (dir == null) return;
            _active.Navigate(dir, Path.GetFileName(path));
            Activate();
            _active.List.Focus();
        });
        _search.Show(this);
    }

    private async void Pack()
    {
        var items = _active.GetSelection();
        if (items.Count == 0) return;

        string baseName = items.Count == 1
            ? (items[0] is FileInfo f ? Path.GetFileNameWithoutExtension(f.Name) : items[0].Name)
            : new DirectoryInfo(_active.CurrentPath).Name;
        if (string.IsNullOrEmpty(baseName) || baseName.EndsWith(':')) baseName = "archiwum";
        string what = items.Count == 1 ? $"„{items[0].Name}”" : $"{items.Count} element(y/ów)";

        var target = PromptDialog.Show(this, "Spakuj", $"Spakuj {what} do archiwum (.zip, .tar, .tar.gz, .tar.bz2 — wg rozszerzenia):",
            Path.Combine(Other.CurrentPath, baseName + ".zip"), selectNameOnly: false);
        if (string.IsNullOrEmpty(target)) return;
        target = Environment.ExpandEnvironmentVariables(target.Trim('"'));
        if (!Path.IsPathRooted(target)) target = Path.Combine(_active.CurrentPath, target);
        bool tar = ArchiveOps.IsTarTarget(target, out var compression);
        if (!tar && !target.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) target += ".zip";

        if (File.Exists(target) &&
            MessageBox.Show(this, $"Archiwum już istnieje:\n{target}\n\nZastąpić je?", "Spakuj",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        string baseDir = _active.CurrentPath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
            bool done = await ProgressDialog.RunAsync(this, "Pakowanie", (progress, ct) =>
            {
                if (tar) ArchiveOps.PackTar(items, baseDir, target, compression, progress, ct);
                else ZipOps.Pack(items, baseDir, target, System.IO.Compression.CompressionLevel.Optimal, progress, ct);
            });
            if (done)
            {
                _active.ClearMarks();
                // Kursor na nowym archiwum, jeśli powstało w którymś z widocznych katalogów.
                string zipDir = Path.GetDirectoryName(Path.GetFullPath(target))!;
                foreach (var p in new[] { _left, _right })
                    if (string.Equals(p.CurrentPath.TrimEnd('\\'), zipDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                        p.Navigate(p.CurrentPath, Path.GetFileName(target));
            }
        }
        catch (Exception ex)
        {
            Error("Nie udało się spakować:\n" + ex.Message);
        }
        _left.Reload();
        _right.Reload();
    }

    /// <summary>Nazwa archiwum bez rozszerzeń archiwum (projekt.tar.gz → projekt).</summary>
    private static string ArchiveBaseName(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        return name.EndsWith(".tar", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    private async void Unpack()
    {
        var archives = _active.GetSelection().Where(ArchiveOps.IsArchive).Select(z => z.FullName).ToList();
        if (archives.Count == 0)
        {
            Error("Zaznacz archiwum (.zip, .7z, .rar, .tar, .tar.gz…) albo ustaw na nim kursor.");
            return;
        }

        string what = archives.Count == 1 ? $"„{Path.GetFileName(archives[0])}”" : $"{archives.Count} archiwa";
        string defaultTarget = archives.Count == 1
            ? Path.Combine(Other.CurrentPath, ArchiveBaseName(archives[0])) + "\\"
            : Other.CurrentPath.TrimEnd('\\') + "\\";
        var target = PromptDialog.Show(this, "Rozpakuj", $"Rozpakuj {what} do:", defaultTarget);
        if (string.IsNullOrEmpty(target)) return;
        target = Environment.ExpandEnvironmentVariables(target.Trim('"'));
        if (!Path.IsPathRooted(target)) target = Path.Combine(_active.CurrentPath, target);

        try
        {
            bool overwrite = false;
            int conflicts = ArchiveOps.CountConflicts(archives, target, out bool unknown);
            if (conflicts > 0 || unknown)
            {
                string intro = conflicts > 0
                    ? $"{conflicts} plik(ów) już istnieje w katalogu docelowym."
                    : "Katalog docelowy nie jest pusty — niektóre pliki mogą już istnieć.";
                var answer = MessageBox.Show(this,
                    $"{intro}\n\nTak — nadpisz\nNie — pomiń istniejące\nAnuluj — przerwij",
                    "Rozpakuj", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (answer == DialogResult.Cancel) return;
                overwrite = answer == DialogResult.Yes;
            }

            bool done = await ProgressDialog.RunAsync(this, "Rozpakowywanie",
                (progress, ct) => ArchiveOps.Unpack(archives, target, overwrite, progress, ct));
            if (done) _active.ClearMarks();
        }
        catch (Exception ex)
        {
            Error("Problem z rozpakowaniem:\n" + ex.Message);
        }
        _left.Reload();
        _right.Reload();
    }

    private void CreateChecksums()
    {
        var items = _active.GetSelection();
        if (items.Count > 0) Checksums.CreateAsync(this, items, _active.CurrentPath);
    }

    private void VerifyChecksums()
    {
        if (_active.FocusedEntry is { } f && Checksums.IsChecksumFile(f)) Checksums.VerifyAsync(this, f.FullName);
        else Error("Ustaw kursor na pliku sum kontrolnych (.md5, .sha1, .sha256, .sha512).");
    }

    private void SplitFile()
    {
        if (_active.FocusedEntry is FileInfo f) SplitJoin.SplitAsync(this, f, Other.CurrentPath);
        else Error("Ustaw kursor na pliku, który chcesz podzielić.");
    }

    private void JoinFiles()
    {
        if (_active.FocusedEntry is FileInfo f && SplitJoin.IsFirstPart(f)) SplitJoin.JoinAsync(this, f, Other.CurrentPath);
        else Error("Ustaw kursor na pierwszej części (plik .001).");
    }

    private void CompareFiles()
    {
        // Dwa zaznaczone pliki w jednym panelu albo pliki pod kursorem w obu panelach.
        var marked = _active.GetSelection().OfType<FileInfo>().ToList();
        string? a = null, b = null;
        if (marked.Count == 2)
        {
            a = marked[0].FullName;
            b = marked[1].FullName;
        }
        else if (_active.FocusedEntry is FileInfo f)
        {
            string? other = Other.FocusedEntry is FileInfo g ? g.FullName
                : File.Exists(Path.Combine(Other.CurrentPath, f.Name)) ? Path.Combine(Other.CurrentPath, f.Name) : null;
            if (other != null)
            {
                (a, b) = _active == _left ? (f.FullName, other) : (other, f.FullName);
            }
        }
        if (a == null || b == null)
        {
            Error("Zaznacz dwa pliki w panelu albo ustaw kursor na plikach w obu panelach.");
            return;
        }
        DiffForm.Compare(this, a, b);
    }

    /// <summary>Shift+F2: zaznacza w obu panelach pliki nowsze i takie, których brakuje po drugiej stronie.</summary>
    private void CompareDirectories()
    {
        if (_left.IsQuickView || _right.IsQuickView) return;
        var l = _left.Entries.ToDictionary(e => e.Name, StringComparer.OrdinalIgnoreCase);
        var r = _right.Entries.ToDictionary(e => e.Name, StringComparer.OrdinalIgnoreCase);
        var markL = new List<string>();
        var markR = new List<string>();
        foreach (var (name, le) in l)
        {
            if (!r.TryGetValue(name, out var re)) { markL.Add(name); continue; }
            if (le is FileInfo lf && re is FileInfo rf)
            {
                double seconds = (lf.LastWriteTimeUtc - rf.LastWriteTimeUtc).TotalSeconds;
                if (seconds > 2) markL.Add(name);
                else if (seconds < -2) markR.Add(name);
                else if (lf.Length != rf.Length) { markL.Add(name); markR.Add(name); }
            }
            else if (le.GetType() != re.GetType())
            {
                markL.Add(name);
                markR.Add(name);
            }
        }
        markR.AddRange(r.Keys.Where(n => !l.ContainsKey(n)));
        _left.SetMarks(markL);
        _right.SetMarks(markR);
        if (markL.Count == 0 && markR.Count == 0)
            MessageBox.Show(this, "Katalogi wyglądają na identyczne (nazwy, daty i rozmiary; bez zaglądania do podkatalogów).",
                "Porównaj katalogi", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void SynchronizeDirectories()
    {
        using var form = new SyncForm(_left.CurrentPath, _right.CurrentPath);
        form.ShowDialog(this);
        if (!form.Changed) return;
        _left.Reload();
        _right.Reload();
    }

    // ───────────────────────── nawigacja i widok ─────────────────────────

    private void ShowProperties()
    {
        var item = _active.FocusedEntry;
        ShellIcons.ShowProperties(item?.FullName ?? _active.CurrentPath, Handle);
    }

    private void SwapPanels()
    {
        _left.SetQuickView(false);
        _right.SetQuickView(false);
        string l = _left.CurrentPath, r = _right.CurrentPath;
        _left.Navigate(r);
        _right.Navigate(l);
        SetActive(Other);
        _active.List.Focus();
    }

    private void PanelFromCursor(FilePanel target)
    {
        // Ctrl+←/→: otwórz w panelu docelowym katalog spod kursora (albo bieżący).
        if (target.IsQuickView) return;
        string path = _active.FocusedEntry is DirectoryInfo d ? d.FullName : _active.CurrentPath;
        target.Navigate(path);
    }

    private void ToggleQuickView()
    {
        var target = Other;
        target.SetQuickView(!target.IsQuickView);
        if (target.IsQuickView) target.ShowPreview(_active.FocusedEntry);
        _active.List.Focus();
    }

    /// <summary>Ctrl+D: ulubione katalogi i katalogi systemowe.</summary>
    private void ShowHotlist()
    {
        var menu = new ContextMenuStrip();
        int n = 1;
        foreach (var fav in _settings.Favorites)
        {
            string target = fav;
            string label = n <= 9 ? $"&{n}  {fav}" : $"    {fav}";
            menu.Items.Add(label, null, (_, _) => _active.Navigate(target));
            n++;
        }
        if (_settings.Favorites.Count > 0) menu.Items.Add(new ToolStripSeparator());

        foreach (var (label, path) in new[]
                 {
                     ("&Pulpit", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
                     ("&Dokumenty", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
                     ("Po&brane", ShellIcons.Downloads),
                     ("Katalog &domowy", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
                 })
        {
            string target = path;
            menu.Items.Add(label, null, (_, _) => _active.Navigate(target));
        }
        menu.Items.Add(new ToolStripSeparator());

        string current = _active.CurrentPath;
        int index = _settings.Favorites.FindIndex(f => string.Equals(f, current, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            menu.Items.Add("Dod&aj bieżący katalog", null, (_, _) => _settings.Favorites.Add(current));
        else
            menu.Items.Add("&Usuń bieżący katalog z listy", null, (_, _) => _settings.Favorites.RemoveAt(index));
        menu.Items.Add("&Edytuj listę…", null, (_, _) =>
        {
            var text = TextDialog.Edit(this, "Ulubione katalogi (jeden w wierszu)", string.Join("\n", _settings.Favorites));
            if (text == null) return;
            _settings.Favorites = text.Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        });
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Show(_active.List, new Point(0, 0));
    }

    private void SetView(PanelView mode)
    {
        _active.ViewMode = mode;
        foreach (var (m, item) in _viewItems) item.Checked = m == mode;
    }

    private void ToggleHidden()
    {
        _settings.ShowHidden = !_settings.ShowHidden;
        _hiddenItem.Checked = _settings.ShowHidden;
        _left.ShowHidden = _right.ShowHidden = _settings.ShowHidden;
        _left.Reload();
        _right.Reload();
    }

    private void ToggleRightClickMenu()
    {
        _settings.RightClickMenu = !_settings.RightClickMenu;
        _rightClickItem.Checked = _settings.RightClickMenu;
        _left.RightClickMenu = _right.RightClickMenu = _settings.RightClickMenu;
    }

    private void SetTheme(string mode)
    {
        _settings.Theme = mode;
        Theme.Set(mode);
        ApplyTheme();
    }

    private void ApplyTheme()
    {
        Theme.Apply(this);
        _left.ApplyTheme();
        _right.ApplyTheme();
        foreach (var (key, item) in _themeItems) item.Checked = key == _settings.Theme;
    }

    private Font CreateListFont()
    {
        try { return new Font(_settings.FontName, Math.Clamp(_settings.FontSize, 6f, 32f)); }
        catch { return new Font("Segoe UI", 9f); }
    }

    private void ChooseFont()
    {
        using var dlg = new FontDialog { Font = _active.List.Font, ShowEffects = false, AllowVerticalFonts = false };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _settings.FontName = dlg.Font.Name;
        _settings.FontSize = dlg.Font.Size;
        var font = CreateListFont();
        _left.SetListFont(font);
        _right.SetListFont(font);
    }

    private void OpenTerminal()
    {
        try
        {
            Process.Start(new ProcessStartInfo("cmd.exe") { WorkingDirectory = _active.CurrentPath, UseShellExecute = true });
        }
        catch (Exception ex) { Error(ex.Message); }
    }

    private void OpenExplorer()
    {
        try
        {
            string args = _active.FocusedEntry is { } f ? $"/select,\"{f.FullName}\"" : $"\"{_active.CurrentPath}\"";
            Process.Start(new ProcessStartInfo("explorer.exe", args) { UseShellExecute = true });
        }
        catch (Exception ex) { Error(ex.Message); }
    }

    private void ChooseEditor()
    {
        var e = PromptDialog.Show(this, "Edytor", "Program uruchamiany klawiszem F4 (np. notepad.exe albo pełna ścieżka do Notepad++):", _settings.Editor);
        if (!string.IsNullOrEmpty(e)) _settings.Editor = e;
    }

    private void ShowHelp()
    {
        TextDialog.ShowReport(this, "Pomoc — skróty klawiszowe",
            """
            FilipCommander — skróty klawiszowe

            Tab                przełącz panel
            Enter / dwuklik    wejdź do katalogu / uruchom plik
            Backspace          katalog wyżej       Ctrl+\    katalog główny
            Alt+← / Alt+→      wstecz / dalej      Alt+↓     historia katalogów
            Ctrl+D             ulubione katalogi (+ Pulpit, Dokumenty, Pobrane)
            Insert / Spacja    zaznacz (Spacja liczy rozmiar katalogu)
            Prawy przycisk     zaznacz (albo menu kontekstowe — Widok)
            Num + / Num −      zaznacz / odznacz wg maski
            Num *              odwróć zaznaczenie     Ctrl+A   zaznacz wszystko
            Ctrl+S             szybkie wyszukiwanie (↑/↓ — poprzedni/następny)
            Ctrl+F             filtr (Esc — wyłącz)
            Shift+F2           porównaj katalogi (zaznacza nowsze i brakujące)

            F3   podgląd (H – hex, W – zawijanie)
            F4   edycja                Shift+F4   nowy plik
            F5   kopiuj                F6         przenieś
            Shift+F6  zmień nazwę      Ctrl+M     zmień nazwy wielu plików
            F7   nowy folder
            F8 / Del  do kosza         Shift+Del  usuń trwale
            Alt+Enter właściwości      Shift+F10  menu kontekstowe Windows
            Alt+F5    spakuj (ZIP/TAR)  Alt+F9    rozpakuj (ZIP/7z/RAR/TAR)
            Alt+F7    szukaj plików
            Ctrl+C / Ctrl+X / Ctrl+V   kopiuj / wytnij / wklej (działa z Eksploratorem)
            Ctrl+Shift+C               kopiuj pełne ścieżki

            Ctrl+T / Ctrl+W   nowa / zamknij zakładkę     Ctrl+Tab  następna zakładka
            Ctrl+Q            szybki podgląd w drugim panelu
            Ctrl+F1 / Ctrl+F2 / Ctrl+Shift+F1   widok: krótki / szczegóły / miniatury

            Alt+F1 / Alt+F2   wybór dysku (lewy/prawy)
            Ctrl+R odśwież     Ctrl+H ukryte pliki     Ctrl+U zamień panele
            Ctrl+E ten sam katalog w drugim panelu
            Ctrl+←/→  katalog spod kursora w lewym/prawym panelu
            Ctrl+Shift+T  cmd tutaj     Ctrl+O  pokaż w Eksploratorze
            Ctrl+Enter  nazwa pliku do wiersza poleceń

            Wiersz poleceń: wpisz polecenie i Enter (cd, D:, cd ~ itp. działają w panelu).
            Pliki można przeciągać między panelami i z/do Eksploratora.
            W menu Plik: sumy kontrolne, dzielenie/łączenie plików, atrybuty i daty,
            porównanie plików. W menu Polecenia: synchronizacja katalogów.
            """);
    }

    private void Error(string message) =>
        MessageBox.Show(this, message, "FilipCommander", MessageBoxButtons.OK, MessageBoxIcon.Warning);

    // ───────────────────────── okno i ustawienia ─────────────────────────

    private void RestoreWindow()
    {
        Size = new Size(Math.Max(700, _settings.WindowWidth), Math.Max(400, _settings.WindowHeight));
        var bounds = new Rectangle(_settings.WindowX, _settings.WindowY, Width, Height);
        if (_settings.WindowX >= 0 && Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(bounds)))
        {
            StartPosition = FormStartPosition.Manual;
            Location = bounds.Location;
        }
        else
        {
            StartPosition = FormStartPosition.CenterScreen;
        }
        if (_settings.Maximized) WindowState = FormWindowState.Maximized;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _settings.LeftPath = _left.CurrentPath;
        _settings.RightPath = _right.CurrentPath;
        _settings.LeftTabs = _left.TabPaths.ToList();
        _settings.RightTabs = _right.TabPaths.ToList();
        _settings.LeftTab = _left.ActiveTab;
        _settings.RightTab = _right.ActiveTab;
        _settings.LeftView = _left.ViewMode;
        _settings.RightView = _right.ViewMode;
        _settings.RightActive = _active == _right;
        _settings.Maximized = WindowState == FormWindowState.Maximized;
        var b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        _settings.WindowX = b.X;
        _settings.WindowY = b.Y;
        _settings.WindowWidth = b.Width;
        _settings.WindowHeight = b.Height;
        _settings.Save();
        base.OnFormClosing(e);
    }
}
