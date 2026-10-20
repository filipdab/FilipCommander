using System.Text;

namespace FilipCommander;

/// <summary>Proste okno z pytaniem i polem tekstowym (kopiuj do…, nowy folder, zmiana nazwy).</summary>
public static class PromptDialog
{
    public static string? Show(IWin32Window? owner, string title, string prompt, string defaultValue, bool selectNameOnly = false)
    {
        using var form = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(520, 110),
            AutoScaleMode = AutoScaleMode.Font
        };
        var label = new Label { Text = prompt, Left = 12, Top = 12, Width = 496, AutoEllipsis = true };
        var box = new TextBox { Text = defaultValue, Left = 12, Top = 36, Width = 496 };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 342, Top = 72, Width = 80 };
        var cancel = new Button { Text = "Anuluj", DialogResult = DialogResult.Cancel, Left = 428, Top = 72, Width = 80 };
        form.Controls.AddRange(new Control[] { label, box, ok, cancel });
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        form.Shown += (_, _) =>
        {
            box.Focus();
            if (selectNameOnly)
            {
                int dot = defaultValue.LastIndexOf('.');
                box.Select(0, dot > 0 ? dot : defaultValue.Length);
            }
            else
            {
                box.SelectAll();
            }
        };
        Theme.Apply(form);
        return form.ShowDialog(owner) == DialogResult.OK ? box.Text.Trim() : null;
    }
}

/// <summary>Okno z dłuższym tekstem: raport do przeczytania albo lista do edycji (jedna pozycja w wierszu).</summary>
public static class TextDialog
{
    /// <summary>Pokazuje tekst tylko do odczytu (np. wynik sprawdzania sum kontrolnych).</summary>
    /// <param name="save">Jeśli podane — przycisk „Zapisz…” wywołuje tę akcję z tekstem.</param>
    public static void ShowReport(IWin32Window? owner, string title, string text, Action<IWin32Window, string>? save = null) =>
        Run(owner, title, text, readOnly: true, save);

    /// <summary>Edycja tekstu; zwraca null po Anuluj.</summary>
    public static string? Edit(IWin32Window? owner, string title, string text) =>
        Run(owner, title, text, readOnly: false, null);

    private static string? Run(IWin32Window? owner, string title, string text, bool readOnly, Action<IWin32Window, string>? save)
    {
        using var form = new Form
        {
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            ShowInTaskbar = false,
            MinimizeBox = false,
            Size = new Size(720, 480),
            KeyPreview = true
        };
        var box = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            ReadOnly = readOnly,
            Font = new Font("Consolas", 10f),
            Text = text.Replace("\r\n", "\n").Replace("\n", "\r\n"),
            MaxLength = 0
        };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 38, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(4) };
        var ok = new Button { Text = readOnly ? "Zamknij" : "OK", DialogResult = DialogResult.OK, Width = 90 };
        bar.Controls.Add(ok);
        if (readOnly)
        {
            var copy = new Button { Text = "Kopiuj", Width = 90 };
            copy.Click += (_, _) => { if (box.Text.Length > 0) Clipboard.SetText(box.Text); };
            bar.Controls.Add(copy);
            if (save != null)
            {
                var saveButton = new Button { Text = "Zapisz…", Width = 90 };
                saveButton.Click += (_, _) => save(form, box.Text.Replace("\r\n", "\n"));
                bar.Controls.Add(saveButton);
            }
            form.CancelButton = ok;
        }
        else
        {
            var cancel = new Button { Text = "Anuluj", DialogResult = DialogResult.Cancel, Width = 90 };
            bar.Controls.Add(cancel);
            form.CancelButton = cancel;
        }
        form.Controls.Add(box);
        form.Controls.Add(bar);
        form.Shown += (_, _) => { box.Select(0, 0); box.Focus(); };
        Theme.Apply(form);
        return form.ShowDialog(owner) == DialogResult.OK ? box.Text : null;
    }
}

/// <summary>Podgląd pliku: tekst, hex dla plików binarnych, obrazy, zawartość archiwów. Używany w F3 i w szybkim podglądzie (Ctrl+Q).</summary>
public sealed class ViewerControl : UserControl
{
    private const int MaxHexBytes = 1024 * 1024;

    private static readonly HashSet<string> ImageExt = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".ico", ".tif", ".tiff" };

    private readonly TextBox _text;
    private readonly PictureBox _picture;
    private readonly Label _info;
    private readonly int _maxTextBytes;
    private string? _path;
    private bool _hexMode;

    public ViewerControl(int maxTextBytes)
    {
        _maxTextBytes = maxTextBytes;
        _text = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font("Consolas", 10f),
            MaxLength = 0
        };
        _picture = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(40, 40, 40), Visible = false };
        _info = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 20,
            Padding = new Padding(4, 0, 0, 0),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
        Controls.Add(_text);
        Controls.Add(_picture);
        Controls.Add(_info);
    }

    public void ShowFile(string path)
    {
        _path = path;
        ClearImage();
        if (ImageExt.Contains(Path.GetExtension(path)) && TryLoadImage(path, out var image))
        {
            _picture.Image = image;
            _picture.Visible = true;
            _text.Visible = false;
            _info.Text = $"{image!.Width} × {image.Height} px";
            return;
        }
        _picture.Visible = false;
        _text.Visible = true;
        if (ArchiveOps.IsArchive(path) && TryListing(path, out var listing))
        {
            _hexMode = false;
            _text.Text = listing;
            _info.Text = "Zawartość archiwum    H — surowe bajty (hex)";
            return;
        }
        LoadContent(forceHex: false);
    }

    /// <summary>Dowolny tekst zamiast pliku (np. opis katalogu w szybkim podglądzie).</summary>
    public void ShowText(string text, string info = "")
    {
        _path = null;
        ClearImage();
        _picture.Visible = false;
        _text.Visible = true;
        _text.Text = text.Replace("\r\n", "\n").Replace("\n", "\r\n");
        _info.Text = info;
    }

    public void ToggleHex()
    {
        if (_path != null && _text.Visible) LoadContent(!_hexMode);
    }

    public void ToggleWrap() => _text.WordWrap = !_text.WordWrap;

    public void FocusContent()
    {
        if (_text.Visible) _text.Focus(); else _picture.Focus();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.H: ToggleHex(); return true;
            case Keys.W: ToggleWrap(); return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void ClearImage()
    {
        var old = _picture.Image;
        _picture.Image = null;
        old?.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) ClearImage();
        base.Dispose(disposing);
    }

    private static bool TryListing(string path, out string listing)
    {
        try
        {
            listing = ArchiveOps.Listing(path);
            return true;
        }
        catch
        {
            listing = "";
            return false;
        }
    }

    private static bool TryLoadImage(string path, out Image? image)
    {
        try
        {
            // Kopia w pamięci — nie blokujemy pliku na dysku.
            using var fs = File.OpenRead(path);
            using var tmp = Image.FromStream(fs);
            image = new Bitmap(tmp);
            return true;
        }
        catch
        {
            image = null;
            return false;
        }
    }

    private void LoadContent(bool forceHex)
    {
        try
        {
            using var fs = new FileStream(_path!, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            int toRead = (int)Math.Min(fs.Length, forceHex ? MaxHexBytes : _maxTextBytes);
            var buffer = new byte[toRead];
            int read = 0;
            while (read < toRead)
            {
                int n = fs.Read(buffer, read, toRead - read);
                if (n == 0) break;
                read += n;
            }

            _hexMode = forceHex || LooksBinary(buffer, read);
            string content = _hexMode ? HexDump(buffer, Math.Min(read, MaxHexBytes)) : DecodeText(buffer, read);
            if (fs.Length > read || (_hexMode && read > MaxHexBytes))
                content += $"\r\n\r\n[… pokazano {FilePanel.FormatSize(Math.Min(read, _hexMode ? MaxHexBytes : _maxTextBytes))} z {FilePanel.FormatSize(fs.Length)}]";
            _text.Text = content;
            _text.Select(0, 0);
            _info.Text = (_hexMode ? "Hex" : "Tekst") + "    H — tekst/hex    W — zawijanie wierszy";
        }
        catch (Exception ex)
        {
            _text.Text = "Nie można odczytać pliku:\r\n" + ex.Message;
            _info.Text = "";
        }
    }

    public static bool LooksBinary(byte[] data, int length)
    {
        int check = Math.Min(length, 8192);
        // Pliki UTF-16 z BOM to tekst, mimo bajtów zerowych.
        if (check >= 2 && ((data[0] == 0xFF && data[1] == 0xFE) || (data[0] == 0xFE && data[1] == 0xFF))) return false;
        for (int i = 0; i < check; i++)
            if (data[i] == 0) return true;
        return false;
    }

    /// <summary>BOM → właściwe kodowanie; poprawny UTF-8 → UTF-8; w innym razie Windows-1250. Wynik ma końce wierszy \r\n.</summary>
    public static string DecodeText(byte[] data, int length)
    {
        using var ms = new MemoryStream(data, 0, length);
        Encoding fallback;
        try
        {
            new UTF8Encoding(false, true).GetString(data, 0, length);
            fallback = Encoding.UTF8;
        }
        catch (DecoderFallbackException)
        {
            fallback = Encoding.GetEncoding(1250);
        }
        using var reader = new StreamReader(ms, fallback, detectEncodingFromByteOrderMarks: true);
        string text = reader.ReadToEnd();
        // TextBox wymaga \r\n.
        return text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n");
    }

    private static string HexDump(byte[] data, int length)
    {
        var sb = new StringBuilder(length * 4 + length / 16 * 12);
        for (int offset = 0; offset < length; offset += 16)
        {
            sb.Append(offset.ToString("X8")).Append("  ");
            for (int i = 0; i < 16; i++)
            {
                if (offset + i < length) sb.Append(data[offset + i].ToString("X2")).Append(' ');
                else sb.Append("   ");
                if (i == 7) sb.Append(' ');
            }
            sb.Append(' ');
            for (int i = 0; i < 16 && offset + i < length; i++)
            {
                byte b = data[offset + i];
                sb.Append(b >= 32 && b < 127 ? (char)b : '.');
            }
            sb.Append("\r\n");
        }
        return sb.ToString();
    }
}

/// <summary>Okno podglądu F3.</summary>
public sealed class ViewerForm : Form
{
    private readonly ViewerControl _viewer;

    public ViewerForm(string path)
    {
        Text = $"Podgląd — {path}";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1000, 700);
        KeyPreview = true;
        ShowIcon = false;

        _viewer = new ViewerControl(32 * 1024 * 1024) { Dock = DockStyle.Fill };
        _viewer.ShowFile(path);
        Controls.Add(_viewer);
        Theme.Apply(this);
        Shown += (_, _) => _viewer.FocusContent();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData is Keys.Escape or Keys.F3)
        {
            Close();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}

