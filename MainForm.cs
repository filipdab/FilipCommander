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
    private FilePanel _active;

    private FilePanel Other => _active == _left ? _right : _left;

    public MainForm()
    {
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
        file.DropDownItems.Add(Item("&Nowy folder…", Keys.F7, (_, _) => MakeDirectory()));
        file.DropDownItems.Add(Item("&Nowy plik tekstowy…", Keys.Shift | Keys.F4, (_, _) => NewTextFile()));
        file.DropDownItems.Add(Item("&Usuń", Keys.F8, (_, _) => Delete(false)));
        file.DropDownItems.Add(Item("Usuń &trwale", Keys.Shift | Keys.Delete, (_, _) => Delete(true)));
        file.DropDownItems.Add(Item("&Właściwości", Keys.Alt | Keys.Enter, (_, _) => ShowProperties()));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Item("&Spakuj do ZIP…", Keys.Alt | Keys.F5, (_, _) => PackZip()));
        file.DropDownItems.Add(Item("&Rozpakuj ZIP…", Keys.Alt | Keys.F9, (_, _) => UnpackZip()));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Item("Sz&ukaj plików…", Keys.Alt | Keys.F7, (_, _) => Search()));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Item("Wy&jście", Keys.Alt | Keys.F4, (_, _) => Close()));

        var mark = new ToolStripMenuItem("&Zaznacz");
        mark.DropDownItems.Add(Item("Zaznacz &wszystko", Keys.Control | Keys.A, (_, _) => _active.MarkByPattern("*", true)));
        mark.DropDownItems.Add(Item("&Odznacz wszystko", Keys.None, (_, _) => _active.ClearMarks()));
        mark.DropDownItems.Add(Item("Odwróć zaznaczenie", Keys.None, (_, _) => _active.InvertMarks()));

        var cmd = new ToolStripMenuItem("&Polecenia");
        cmd.DropDownItems.Add(Item("&Odśwież", Keys.Control | Keys.R, (_, _) => { _left.Reload(); _right.Reload(); }));
        cmd.DropDownItems.Add(Item("&Zamień panele", Keys.Control | Keys.U, (_, _) => SwapPanels()));
        cmd.DropDownItems.Add(Item("&Ten sam katalog w drugim panelu", Keys.Control | Keys.E, (_, _) => Other.Navigate(_active.CurrentPath)));
        cmd.DropDownItems.Add(Item("&Wiersz poleceń tutaj", Keys.Control | Keys.T, (_, _) => OpenTerminal()));
        cmd.DropDownItems.Add(Item("Otwórz w &Eksploratorze", Keys.Control | Keys.O, (_, _) => OpenExplorer()));
        cmd.DropDownItems.Add(Item("&Lewy panel — dysk", Keys.Alt | Keys.F1, (_, _) => _left.DropDownDrives()));
        cmd.DropDownItems.Add(Item("&Prawy panel — dysk", Keys.Alt | Keys.F2, (_, _) => _right.DropDownDrives()));

        var view = new ToolStripMenuItem("&Widok");
        _hiddenItem = Item("Pokaż pliki &ukryte/systemowe", Keys.Control | Keys.H, (_, _) => ToggleHidden());
        _hiddenItem.Checked = _settings.ShowHidden;
        view.DropDownItems.Add(_hiddenItem);
        view.DropDownItems.Add(Item("&Edytor dla F4…", Keys.None, (_, _) => ChooseEditor()));

        var help = new ToolStripMenuItem("Pomo&c");
        help.DropDownItems.Add(Item("&Skróty klawiszowe", Keys.F1, (_, _) => ShowHelp()));
        help.DropDownItems.Add(new ToolStripSeparator());
        help.DropDownItems.Add(Item("&O programie…", Keys.None, (_, _) => { using var about = new AboutForm(); about.ShowDialog(this); }));

        menu.Items.AddRange(new ToolStripItem[] { file, mark, cmd, view, help });
        MainMenuStrip = menu;

        // ── panele ──
        _split = new SplitContainer { Dock = DockStyle.Fill, SplitterWidth = 5, TabStop = false };
        _split.Panel1.Controls.Add(_left);
        _split.Panel2.Controls.Add(_right);
        _split.SizeChanged += (_, _) => { if (_split.Width > 50) _split.SplitterDistance = _split.Width / 2; };

        foreach (var p in new[] { _left, _right })
        {
            p.ShowHidden = _settings.ShowHidden;
            p.PanelActivated += (s, _) => SetActive((FilePanel)s!);
            p.PathChanged += (s, _) => { if (s == _active) UpdateCmdPath(); };
            p.InsertNameRequested += name =>
            {
                string n = name.Contains(' ') ? $"\"{name}\"" : name;
                _cmdLine.Text = _cmdLine.Text.Length == 0 ? n : _cmdLine.Text.TrimEnd() + " " + n;
            };
        }

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
        if (!Directory.Exists(_settings.LeftPath) || !_left.Navigate(_settings.LeftPath)) _left.Navigate(fallback);
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!Directory.Exists(_settings.RightPath) || !_right.Navigate(_settings.RightPath)) _right.Navigate(home);
        SetActive(_settings.RightActive ? _right : _left);
        _active.List.Focus();
    }

    private void SetActive(FilePanel panel)
    {
        _active = panel;
        _left.SetActive(panel == _left);
        _right.SetActive(panel == _right);
        UpdateCmdPath();
    }

    private void UpdateCmdPath() => _cmdPath.Text = _active.CurrentPath + ">";

    // ───────────────────────── klawiatura ─────────────────────────

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        bool inText = ActiveControl is TextBox || (ActiveControl as ContainerControl)?.ActiveControl is TextBox;
        bool inList = _left.List.Focused || _right.List.Focused;

        switch (keyData)
        {
            case Keys.Tab:
            case Keys.Shift | Keys.Tab:
                SetActive(Other);
                _active.List.Focus();
                return true;
            case Keys.F1: ShowHelp(); return true;
            case Keys.F3: ViewFile(); return true;
            case Keys.F4: EditFile(); return true;
            case Keys.Shift | Keys.F4: NewTextFile(); return true;
            case Keys.F5: Transfer(FileOps.Kind.Copy); return true;
            case Keys.F6: Transfer(FileOps.Kind.Move); return true;
            case Keys.Shift | Keys.F6: RenameFile(); return true;
            case Keys.F7: MakeDirectory(); return true;
            case Keys.F8: Delete(false); return true;
            case Keys.Delete when inList: Delete(false); return true;
            case Keys.Shift | Keys.Delete when inList: Delete(true); return true;
            case Keys.Alt | Keys.Enter when inList: ShowProperties(); return true;
            case Keys.Alt | Keys.F1: _left.DropDownDrives(); return true;
            case Keys.Alt | Keys.F2: _right.DropDownDrives(); return true;
            case Keys.Alt | Keys.F5: PackZip(); return true;
            case Keys.Alt | Keys.F7: Search(); return true;
            case Keys.Alt | Keys.F9: UnpackZip(); return true;
            case Keys.Control | Keys.R: _left.Reload(); _right.Reload(); return true;
            case Keys.Control | Keys.H: ToggleHidden(); return true;
            case Keys.Control | Keys.U: SwapPanels(); return true;
            case Keys.Control | Keys.E: Other.Navigate(_active.CurrentPath); return true;
            case Keys.Control | Keys.T: OpenTerminal(); return true;
            case Keys.Control | Keys.O: OpenExplorer(); return true;
            case Keys.Control | Keys.Left when inList: PanelFromCursor(_left); return true;
            case Keys.Control | Keys.Right when inList: PanelFromCursor(_right); return true;
            case Keys.Escape when inText:
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
        if (command.StartsWith("cd ", StringComparison.OrdinalIgnoreCase))
        {
            string arg = Environment.ExpandEnvironmentVariables(command[3..].Trim().Trim('"'));
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

    // ───────────────────────── szukanie i ZIP ─────────────────────────

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

    private async void PackZip()
    {
        var items = _active.GetSelection();
        if (items.Count == 0) return;

        string baseName = items.Count == 1
            ? (items[0] is FileInfo f ? Path.GetFileNameWithoutExtension(f.Name) : items[0].Name)
            : new DirectoryInfo(_active.CurrentPath).Name;
        if (string.IsNullOrEmpty(baseName) || baseName.EndsWith(':')) baseName = "archiwum";
        string what = items.Count == 1 ? $"„{items[0].Name}”" : $"{items.Count} element(y/ów)";

        var target = PromptDialog.Show(this, "Spakuj", $"Spakuj {what} do archiwum ZIP:",
            Path.Combine(Other.CurrentPath, baseName + ".zip"), selectNameOnly: false);
        if (string.IsNullOrEmpty(target)) return;
        target = Environment.ExpandEnvironmentVariables(target.Trim('"'));
        if (!Path.IsPathRooted(target)) target = Path.Combine(_active.CurrentPath, target);
        if (!target.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) target += ".zip";

        if (File.Exists(target) &&
            MessageBox.Show(this, $"Archiwum już istnieje:\n{target}\n\nZastąpić je?", "Spakuj",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        string baseDir = _active.CurrentPath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
            bool done = await ProgressDialog.RunAsync(this, "Pakowanie",
                (progress, ct) => ZipOps.Pack(items, baseDir, target, System.IO.Compression.CompressionLevel.Optimal, progress, ct));
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

    private async void UnpackZip()
    {
        var zips = _active.GetSelection().Where(ZipOps.IsZip).Select(z => z.FullName).ToList();
        if (zips.Count == 0)
        {
            Error("Zaznacz archiwum .zip (albo ustaw na nim kursor).");
            return;
        }

        string what = zips.Count == 1 ? $"„{Path.GetFileName(zips[0])}”" : $"{zips.Count} archiwa";
        string defaultTarget = zips.Count == 1
            ? Path.Combine(Other.CurrentPath, Path.GetFileNameWithoutExtension(zips[0])) + "\\"
            : Other.CurrentPath.TrimEnd('\\') + "\\";
        var target = PromptDialog.Show(this, "Rozpakuj", $"Rozpakuj {what} do:", defaultTarget);
        if (string.IsNullOrEmpty(target)) return;
        target = Environment.ExpandEnvironmentVariables(target.Trim('"'));
        if (!Path.IsPathRooted(target)) target = Path.Combine(_active.CurrentPath, target);

        try
        {
            bool overwrite = false;
            int conflicts = Directory.Exists(target) ? ZipOps.CountConflicts(zips, target) : 0;
            if (conflicts > 0)
            {
                var answer = MessageBox.Show(this,
                    $"{conflicts} plik(ów) już istnieje w katalogu docelowym.\n\nTak — nadpisz\nNie — pomiń istniejące\nAnuluj — przerwij",
                    "Rozpakuj", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (answer == DialogResult.Cancel) return;
                overwrite = answer == DialogResult.Yes;
            }

            bool done = await ProgressDialog.RunAsync(this, "Rozpakowywanie",
                (progress, ct) => ZipOps.Unpack(zips, target, overwrite, progress, ct));
            if (done) _active.ClearMarks();
        }
        catch (Exception ex)
        {
            Error("Problem z rozpakowaniem:\n" + ex.Message);
        }
        _left.Reload();
        _right.Reload();
    }

    private void ShowProperties()
    {
        var item = _active.FocusedEntry;
        ShellIcons.ShowProperties(item?.FullName ?? _active.CurrentPath, Handle);
    }

    private void SwapPanels()
    {
        string l = _left.CurrentPath, r = _right.CurrentPath;
        _left.Navigate(r);
        _right.Navigate(l);
        SetActive(Other);
        _active.List.Focus();
    }

    private void PanelFromCursor(FilePanel target)
    {
        // Ctrl+←/→: otwórz w panelu docelowym katalog spod kursora (albo bieżący).
        string path = _active.FocusedEntry is DirectoryInfo d ? d.FullName : _active.CurrentPath;
        target.Navigate(path);
    }

    private void ToggleHidden()
    {
        _settings.ShowHidden = !_settings.ShowHidden;
        _hiddenItem.Checked = _settings.ShowHidden;
        _left.ShowHidden = _right.ShowHidden = _settings.ShowHidden;
        _left.Reload();
        _right.Reload();
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
        MessageBox.Show(this,
            """
            FilipCommander — skróty klawiszowe

            Tab                przełącz panel
            Enter / dwuklik    wejdź do katalogu / uruchom plik
            Backspace          katalog wyżej       Ctrl+\    katalog główny
            Insert / Spacja    zaznacz (Spacja liczy rozmiar katalogu)
            Prawy przycisk     zaznacz
            Num + / Num −      zaznacz / odznacz wg maski
            Num *              odwróć zaznaczenie     Ctrl+A   zaznacz wszystko

            F3   podgląd (H – hex, W – zawijanie)
            F4   edycja                Shift+F4   nowy plik
            F5   kopiuj                F6         przenieś
            Shift+F6  zmień nazwę      F7         nowy folder
            F8 / Del  do kosza         Shift+Del  usuń trwale
            Alt+Enter właściwości
            Alt+F5    spakuj do ZIP      Alt+F9     rozpakuj ZIP
            Alt+F7    szukaj plików

            Alt+F1 / Alt+F2   wybór dysku (lewy/prawy)
            Ctrl+R odśwież     Ctrl+H ukryte pliki     Ctrl+U zamień panele
            Ctrl+E ten sam katalog w drugim panelu
            Ctrl+←/→  katalog spod kursora w lewym/prawym panelu
            Ctrl+T  cmd tutaj     Ctrl+O  pokaż w Eksploratorze
            Ctrl+Enter  nazwa pliku do wiersza poleceń

            Wiersz poleceń: wpisz polecenie i Enter (cd, D: itp. działają w panelu).
            """,
            "Pomoc", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
