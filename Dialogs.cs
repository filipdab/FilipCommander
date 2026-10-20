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
        return form.ShowDialog(owner) == DialogResult.OK ? box.Text.Trim() : null;
    }
}

/// <summary>Podgląd pliku (F3): tekst, hex dla plików binarnych, obrazy.</summary>
public sealed class ViewerForm : Form
{
    private const int MaxTextBytes = 32 * 1024 * 1024;
    private const int MaxHexBytes = 1024 * 1024;

    private static readonly HashSet<string> ImageExt = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".ico", ".tif", ".tiff" };

    private readonly string _path;
    private readonly TextBox _text;
    private bool _hexMode;

    public ViewerForm(string path)
    {
        _path = path;
        Text = $"Podgląd — {path}";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1000, 700);
        KeyPreview = true;
        ShowIcon = false;

        _text = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font("Consolas", 10f),
            BackColor = SystemColors.Window,
            MaxLength = 0
        };

        var info = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 20,
            Text = "Esc/F3 — zamknij    H — tekst/hex    W — zawijanie wierszy",
            Padding = new Padding(4, 0, 0, 0),
            TextAlign = ContentAlignment.MiddleLeft
        };

        if (ImageExt.Contains(Path.GetExtension(path)) && TryLoadImage(path, out var image))
        {
            var pic = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Image = image, BackColor = Color.FromArgb(40, 40, 40) };
            Controls.Add(pic);
            info.Text = $"{image!.Width} × {image.Height} px    Esc/F3 — zamknij";
            FormClosed += (_, _) => image.Dispose();
        }
        else if (Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase) && TryZipListing(path, out var listing))
        {
            Controls.Add(_text);
            _text.Text = listing;
            info.Text = "Zawartość archiwum    Esc/F3 — zamknij    H — surowe bajty (hex)";
        }
        else
        {
            Controls.Add(_text);
            LoadContent(forceHex: false);
        }
        Controls.Add(info);
    }

    private static bool TryZipListing(string path, out string listing)
    {
        try
        {
            listing = ZipOps.Listing(path);
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
            using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            int toRead = (int)Math.Min(fs.Length, forceHex ? MaxHexBytes : MaxTextBytes);
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
                content += $"\r\n\r\n[… pokazano {FilePanel.FormatSize(Math.Min(read, _hexMode ? MaxHexBytes : MaxTextBytes))} z {FilePanel.FormatSize(fs.Length)}]";
            _text.Text = content;
            _text.Select(0, 0);
        }
        catch (Exception ex)
        {
            _text.Text = "Nie można odczytać pliku:\r\n" + ex.Message;
        }
    }

    private static bool LooksBinary(byte[] data, int length)
    {
        int check = Math.Min(length, 8192);
        // Pliki UTF-16 z BOM to tekst, mimo bajtów zerowych.
        if (check >= 2 && ((data[0] == 0xFF && data[1] == 0xFE) || (data[0] == 0xFE && data[1] == 0xFF))) return false;
        for (int i = 0; i < check; i++)
            if (data[i] == 0) return true;
        return false;
    }

    private static string DecodeText(byte[] data, int length)
    {
        // BOM → właściwe kodowanie; poprawny UTF-8 → UTF-8; w innym razie Windows-1250.
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

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Escape:
            case Keys.F3:
                Close();
                return true;
            case Keys.H:
                if (Controls.Contains(_text)) LoadContent(!_hexMode);
                return true;
            case Keys.W:
                _text.WordWrap = !_text.WordWrap;
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
