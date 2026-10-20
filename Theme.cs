using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace FilipCommander;

/// <summary>Jasny/ciemny motyw. Kolory jasnego to kolory systemowe, więc wygląd domyślny się nie zmienia.</summary>
public static class Theme
{
    public static bool Dark { get; private set; }

    public static Color Back => Dark ? Color.FromArgb(32, 32, 32) : SystemColors.Control;
    public static Color Fore => Dark ? Color.FromArgb(230, 230, 230) : SystemColors.ControlText;
    public static Color Window => Dark ? Color.FromArgb(25, 25, 25) : SystemColors.Window;
    public static Color WindowText => Dark ? Color.FromArgb(230, 230, 230) : SystemColors.WindowText;
    public static Color Gray => Dark ? Color.FromArgb(140, 140, 140) : SystemColors.GrayText;
    public static Color Marked => Dark ? Color.FromArgb(255, 110, 110) : Color.Red;
    public static Color ActiveHeader => Dark ? Color.FromArgb(38, 79, 120) : SystemColors.ActiveCaption;
    public static Color ActiveHeaderText => Dark ? Color.White : SystemColors.ActiveCaptionText;
    public static Color InactiveHeader => Dark ? Color.FromArgb(50, 50, 50) : SystemColors.InactiveCaption;
    public static Color InactiveHeaderText => Dark ? Color.FromArgb(180, 180, 180) : SystemColors.InactiveCaptionText;
    public static Color Border => Dark ? Color.FromArgb(70, 70, 70) : SystemColors.ControlDark;

    // Kolory porównywania plików/katalogów.
    public static Color DiffRemoved => Dark ? Color.FromArgb(90, 40, 40) : Color.FromArgb(255, 220, 220);
    public static Color DiffAdded => Dark ? Color.FromArgb(35, 80, 40) : Color.FromArgb(215, 245, 215);
    public static Color DiffChanged => Dark ? Color.FromArgb(85, 75, 30) : Color.FromArgb(255, 245, 200);

    /// <summary>„system”, „light” albo „dark”.</summary>
    public static void Set(string mode)
    {
        Dark = mode switch
        {
            "dark" => true,
            "light" => false,
            _ => SystemPrefersDark()
        };
        ToolStripManager.Renderer = Dark ? new DarkRenderer() : new ToolStripProfessionalRenderer();
    }

    private static bool SystemPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Koloruje okno i wszystkie kontrolki w środku.</summary>
    public static void Apply(Control root)
    {
        if (root is Form form)
        {
            form.BackColor = Back;
            form.ForeColor = Fore;
            if (form.IsHandleCreated) DarkTitleBar(form.Handle);
            else form.HandleCreated += (_, _) => DarkTitleBar(form.Handle);
        }
        ApplyTo(root);
    }

    private static void ApplyTo(Control c)
    {
        switch (c)
        {
            case ListView lv:
                lv.BackColor = Window;
                lv.ForeColor = WindowText;
                if (lv.IsHandleCreated) ExplorerTheme(lv);
                else lv.HandleCreated += (_, _) => ExplorerTheme(lv);
                break;
            case TextBoxBase or ComboBox or ListBox or NumericUpDown or TreeView:
                c.BackColor = Window;
                c.ForeColor = WindowText;
                break;
            case Button b:
                if (Dark)
                {
                    b.FlatStyle = FlatStyle.Flat;
                    b.FlatAppearance.BorderColor = Border;
                    b.BackColor = Color.FromArgb(50, 50, 50);
                    b.ForeColor = Fore;
                }
                else
                {
                    b.FlatStyle = FlatStyle.System;
                    b.BackColor = SystemColors.Control;
                    b.ForeColor = SystemColors.ControlText;
                    b.UseVisualStyleBackColor = true;
                }
                break;
            case CheckBox or RadioButton or GroupBox:
                c.ForeColor = Fore;
                break;
        }
        foreach (Control child in c.Controls) ApplyTo(child);
    }

    // ── WinAPI: ciemny pasek tytułu i ciemne paski przewijania/nagłówki ──

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? appName, string? idList);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);

    private const int LVM_GETHEADER = 0x101F;

    private static void DarkTitleBar(IntPtr hwnd)
    {
        int value = Dark ? 1 : 0;
        try { DwmSetWindowAttribute(hwnd, 20, ref value, sizeof(int)); } catch { }
    }

    private static void ExplorerTheme(ListView lv)
    {
        try
        {
            SetWindowTheme(lv.Handle, Dark ? "DarkMode_Explorer" : "Explorer", null);
            var header = SendMessage(lv.Handle, LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);
            if (header != IntPtr.Zero) SetWindowTheme(header, Dark ? "DarkMode_ItemsView" : null, null);
        }
        catch { }
    }

    private sealed class DarkRenderer : ToolStripProfessionalRenderer
    {
        public DarkRenderer() : base(new DarkColors()) { RoundedEdges = false; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Color.FromArgb(230, 230, 230) : Color.FromArgb(120, 120, 120);
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Color.FromArgb(230, 230, 230);
            base.OnRenderArrow(e);
        }
    }

    private sealed class DarkColors : ProfessionalColorTable
    {
        private static readonly Color Bg = Color.FromArgb(32, 32, 32);
        private static readonly Color Menu = Color.FromArgb(43, 43, 43);
        private static readonly Color Hi = Color.FromArgb(65, 65, 65);
        private static readonly Color Line = Color.FromArgb(70, 70, 70);

        public override Color MenuStripGradientBegin => Bg;
        public override Color MenuStripGradientEnd => Bg;
        public override Color ToolStripDropDownBackground => Menu;
        public override Color ImageMarginGradientBegin => Menu;
        public override Color ImageMarginGradientMiddle => Menu;
        public override Color ImageMarginGradientEnd => Menu;
        public override Color MenuItemSelected => Hi;
        public override Color MenuItemSelectedGradientBegin => Hi;
        public override Color MenuItemSelectedGradientEnd => Hi;
        public override Color MenuItemPressedGradientBegin => Menu;
        public override Color MenuItemPressedGradientMiddle => Menu;
        public override Color MenuItemPressedGradientEnd => Menu;
        public override Color MenuItemBorder => Line;
        public override Color MenuBorder => Line;
        public override Color SeparatorDark => Line;
        public override Color SeparatorLight => Line;
        public override Color CheckBackground => Hi;
        public override Color CheckSelectedBackground => Hi;
        public override Color CheckPressedBackground => Hi;
        public override Color ButtonSelectedHighlight => Hi;
        public override Color ButtonSelectedGradientBegin => Hi;
        public override Color ButtonSelectedGradientEnd => Hi;
    }
}
