using System.Runtime.InteropServices;

namespace FilipCommander;

/// <summary>Pobieranie systemowych ikon plików/folderów (SHGetFileInfo) z cache.</summary>
internal static class ShellIcons
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHELLEXECUTEINFO
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public string? lpVerb;
        public string? lpFile;
        public string? lpParameters;
        public string? lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        public string? lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr hProcess;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO lpExecInfo);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private const uint SHGFI_ICON = 0x100;
    private const uint SHGFI_SMALLICON = 0x1;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x10;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
    private const uint SEE_MASK_INVOKEIDLIST = 0x0C;

    // Rozszerzenia, które mają własne, indywidualne ikony.
    private static readonly HashSet<string> PerFileIcons = new(StringComparer.OrdinalIgnoreCase)
        { ".exe", ".ico", ".lnk", ".url", ".cur", ".ani", ".msc" };

    public const string FolderKey = "<dir>";
    public const string UpKey = "<up>";

    /// <summary>Zwraca klucz do ImageList, dokładając ikonę, jeśli jeszcze jej tam nie ma.</summary>
    public static string GetKey(ImageList list, FileSystemInfo? info)
    {
        if (info == null)
        {
            Ensure(list, UpKey, () => Load("folder", FILE_ATTRIBUTE_DIRECTORY, true));
            return UpKey;
        }
        if (info is DirectoryInfo)
        {
            Ensure(list, FolderKey, () => Load("folder", FILE_ATTRIBUTE_DIRECTORY, true));
            return FolderKey;
        }

        string ext = info.Extension;
        if (PerFileIcons.Contains(ext))
        {
            string key = "f:" + info.FullName;
            Ensure(list, key, () => Load(info.FullName, 0, false) ?? Load(ext, FILE_ATTRIBUTE_NORMAL, true));
            return key;
        }

        string extKey = "e:" + (ext.Length == 0 ? "." : ext.ToLowerInvariant());
        Ensure(list, extKey, () => Load(ext.Length == 0 ? "file" : "x" + ext, FILE_ATTRIBUTE_NORMAL, true));
        return extKey;
    }

    private static void Ensure(ImageList list, string key, Func<Icon?> loader)
    {
        if (list.Images.ContainsKey(key)) return;
        var icon = loader();
        if (icon != null) list.Images.Add(key, icon);
        else list.Images.Add(key, SystemIcons.WinLogo); // awaryjnie
    }

    private static Icon? Load(string path, uint attributes, bool useAttributes)
    {
        try
        {
            var info = new SHFILEINFO();
            uint flags = SHGFI_ICON | SHGFI_SMALLICON | (useAttributes ? SHGFI_USEFILEATTRIBUTES : 0);
            SHGetFileInfo(path, attributes, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), flags);
            if (info.hIcon == IntPtr.Zero) return null;
            var icon = (Icon)Icon.FromHandle(info.hIcon).Clone();
            DestroyIcon(info.hIcon);
            return icon;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Otwiera systemowe okno "Właściwości" dla pliku/folderu.</summary>
    public static void ShowProperties(string path, IntPtr owner)
    {
        var info = new SHELLEXECUTEINFO
        {
            cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(),
            fMask = SEE_MASK_INVOKEIDLIST,
            hwnd = owner,
            lpVerb = "properties",
            lpFile = path,
            nShow = 1
        };
        ShellExecuteEx(ref info);
    }

    // ───────────────────────── miniatury ─────────────────────────

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(NativeSize size, int flags, out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize { public int Width, Height; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory? factory);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    /// <summary>
    /// Miniatura pliku tak, jak pokazuje ją Eksplorator (zdjęcia, filmy, PDF…),
    /// a dla innych plików — duża ikona. Wynik ma dokładnie <paramref name="size"/>×<paramref name="size"/>
    /// (obraz wyśrodkowany), bo ImageList rozciąga obrazy. Wywoływać w wątku STA.
    /// </summary>
    public static Bitmap? GetThumbnail(string path, int size)
    {
        var iid = typeof(IShellItemImageFactory).GUID;
        if (SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var factory) != 0 || factory == null) return null;
        IntPtr hbm = IntPtr.Zero;
        try
        {
            if (factory.GetImage(new NativeSize { Width = size, Height = size }, 0, out hbm) != 0 || hbm == IntPtr.Zero) return null;
            using var image = FromHBitmap(hbm);
            var result = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(result);
            float scale = Math.Min(1f, Math.Min((float)size / image.Width, (float)size / image.Height));
            int w = (int)(image.Width * scale), h = (int)(image.Height * scale);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(image, (size - w) / 2, (size - h) / 2, w, h);
            return result;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (hbm != IntPtr.Zero) DeleteObject(hbm);
            Marshal.ReleaseComObject(factory);
        }
    }

    /// <summary>HBITMAP z kanałem alfa (Image.FromHbitmap gubi przezroczystość ikon).</summary>
    private static Bitmap FromHBitmap(IntPtr hbm)
    {
        var bmp = Image.FromHbitmap(hbm);
        if (Image.GetPixelFormatSize(bmp.PixelFormat) < 32) return bmp;

        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, bmp.PixelFormat);
        Bitmap? copy = null;
        try
        {
            // Zdjęcia często mają alfę = 0 w całym obrazie — wtedy alfy nie ma w ogóle.
            bool hasAlpha = false;
            for (int y = 0; y < data.Height && !hasAlpha; y++)
                for (int x = 0; x < data.Width; x++)
                    if (Marshal.ReadByte(data.Scan0, y * data.Stride + x * 4 + 3) != 0) { hasAlpha = true; break; }
            if (hasAlpha)
            {
                using var argb = new Bitmap(data.Width, data.Height, data.Stride, System.Drawing.Imaging.PixelFormat.Format32bppArgb, data.Scan0);
                copy = new Bitmap(argb);
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }
        if (copy == null) return bmp;
        bmp.Dispose();
        return copy;
    }

    // ───────────────────────── znane katalogi ─────────────────────────

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(ref Guid id, uint flags, IntPtr token, out IntPtr path);

    /// <summary>Katalog „Pobrane” (może być przeniesiony przez użytkownika, więc pytamy system).</summary>
    public static string Downloads
    {
        get
        {
            var id = new Guid("374DE290-123F-4565-9164-39C4925E467B");
            try
            {
                if (SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out var p) == 0)
                {
                    string s = Marshal.PtrToStringUni(p)!;
                    Marshal.FreeCoTaskMem(p);
                    return s;
                }
            }
            catch { }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }
    }
}
