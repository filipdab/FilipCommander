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
}
