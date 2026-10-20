using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace FilipCommander;

/// <summary>Okno „O programie”.</summary>
public sealed class AboutForm : Form
{
    private const string Author = "Filip Dąbrowski";
    private const string Email = "filip@dabrowski.in";
    private const string Website = "www.dabrowski.in";

    public AboutForm()
    {
        Text = "O programie";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ShowIcon = false;
        AutoScaleMode = AutoScaleMode.Font;
        Font = new Font("Segoe UI", 9f);
        ClientSize = new Size(400, 270);

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        string versionText = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0";

        // Pasek z nazwą programu.
        var banner = new Panel { Dock = DockStyle.Top, Height = 78, BackColor = Theme.ActiveHeader };
        var icon = new PictureBox
        {
            Left = 20, Top = 15, Width = 48, Height = 48,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent
        };
        try { icon.Image = (Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application).ToBitmap(); }
        catch { icon.Image = SystemIcons.Application.ToBitmap(); }
        var title = new Label
        {
            Text = "FilipCommander",
            Left = 80, Top = 14, AutoSize = true,
            Font = new Font("Segoe UI", 16f, FontStyle.Bold),
            ForeColor = Theme.ActiveHeaderText,
            BackColor = Color.Transparent
        };
        var subtitle = new Label
        {
            Text = $"wersja {versionText}  ·  dwupanelowy menedżer plików",
            Left = 82, Top = 48, AutoSize = true,
            ForeColor = Theme.ActiveHeaderText,
            BackColor = Color.Transparent
        };
        banner.Controls.AddRange(new Control[] { icon, title, subtitle });

        var authorCaption = Caption("Autor:", 100);
        var author = new Label { Text = Author, Left = 110, Top = 100, AutoSize = true, Font = new Font(Font, FontStyle.Bold) };

        var emailCaption = Caption("E-mail:", 126);
        var email = Link(Email, 126, "mailto:" + Email);

        var wwwCaption = Caption("WWW:", 152);
        var www = Link(Website, 152, "https://" + Website);

        var runtime = new Label
        {
            Text = $"© {DateTime.Now.Year} {Author}\n{RuntimeInformation.FrameworkDescription}, {RuntimeInformation.OSDescription}",
            Left = 24, Top = 186, Width = 360, Height = 34,
            ForeColor = Theme.Gray
        };

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 90, Height = 28, Left = 290, Top = 228 };

        Controls.AddRange(new Control[] { banner, authorCaption, author, emailCaption, email, wwwCaption, www, runtime, ok });
        AcceptButton = ok;
        CancelButton = ok;
        Theme.Apply(this);
        BackColor = Theme.Window;
    }

    private static Label Caption(string text, int top) =>
        new() { Text = text, Left = 24, Top = top, Width = 80, ForeColor = Theme.Gray };

    private LinkLabel Link(string text, int top, string target)
    {
        var link = new LinkLabel { Text = text, Left = 110, Top = top, AutoSize = true, TabStop = true };
        link.LinkClicked += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
                link.LinkVisited = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "O programie", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
        return link;
    }
}
