using System.Runtime.InteropServices;
using System.Text;

namespace FilipCommander;

/// <summary>
/// Systemowe menu kontekstowe Eksploratora (Otwórz za pomocą, Wyślij do, 7-Zip, Git…)
/// dla plików z jednego katalogu.
/// </summary>
internal static class ShellContextMenu
{
    /// <summary>
    /// Pokazuje menu w punkcie <paramref name="screen"/>. Zwraca czasownik wybranego polecenia
    /// (np. „rename”), jeśli program powinien obsłużyć je sam, albo null.
    /// </summary>
    public static string? Show(IWin32Window owner, IReadOnlyList<string> paths, Point screen)
    {
        if (paths.Count == 0) return null;
        var pidls = new List<IntPtr>();
        IntPtr folderPtr = IntPtr.Zero, menuPtr = IntPtr.Zero, hmenu = IntPtr.Zero;
        IShellFolder? folder = null;
        IContextMenu? menu = null;
        MenuMessageWindow? hook = null;
        try
        {
            var children = new IntPtr[paths.Count];
            for (int i = 0; i < paths.Count; i++)
            {
                if (SHParseDisplayName(paths[i], IntPtr.Zero, out var pidl, 0, out _) != 0) return null;
                pidls.Add(pidl);
                var iidFolder = typeof(IShellFolder).GUID;
                if (SHBindToParent(pidl, ref iidFolder, out var parent, out children[i]) != 0) return null;
                // Wszystkie elementy są w tym samym katalogu — wystarczy pierwszy rodzic.
                if (i == 0) folderPtr = parent; else Marshal.Release(parent);
            }

            folder = (IShellFolder)Marshal.GetObjectForIUnknown(folderPtr);
            var iidMenu = typeof(IContextMenu).GUID;
            if (folder.GetUIObjectOf(owner.Handle, (uint)children.Length, children, ref iidMenu, IntPtr.Zero, out menuPtr) != 0) return null;
            menu = (IContextMenu)Marshal.GetObjectForIUnknown(menuPtr);

            hmenu = CreatePopupMenu();
            uint flags = CMF_NORMAL | CMF_EXPLORE | CMF_CANRENAME;
            if ((Control.ModifierKeys & Keys.Shift) != 0) flags |= CMF_EXTENDEDVERBS;
            if (menu.QueryContextMenu(hmenu, 0, FirstId, LastId, flags) < 0) return null;

            // Podmenu „Wyślij do” / „Otwórz za pomocą” rysują się dopiero po przekazaniu im komunikatów okna.
            hook = new MenuMessageWindow(owner.Handle, menu as IContextMenu3, menu as IContextMenu2);
            uint cmd = TrackPopupMenuEx(hmenu, TPM_RETURNCMD | TPM_RIGHTBUTTON, screen.X, screen.Y, owner.Handle, IntPtr.Zero);
            hook.ReleaseHandle();
            hook = null;
            if (cmd < FirstId) return null;

            var verb = new StringBuilder(256);
            string? verbName = menu.GetCommandString((UIntPtr)(cmd - FirstId), GCS_VERBW, IntPtr.Zero, verb, verb.Capacity) == 0
                ? verb.ToString()
                : null;
            // Zmiana nazwy przez powłokę wymaga widoku Eksploratora — robimy ją sami.
            if (string.Equals(verbName, "rename", StringComparison.OrdinalIgnoreCase)) return "rename";

            var info = new CMINVOKECOMMANDINFOEX
            {
                cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFOEX>(),
                fMask = CMIC_MASK_UNICODE | CMIC_MASK_PTINVOKE,
                hwnd = owner.Handle,
                lpVerb = (IntPtr)(cmd - FirstId),
                lpVerbW = (IntPtr)(cmd - FirstId),
                lpDirectoryW = Path.GetDirectoryName(paths[0]),
                nShow = 1,
                ptInvoke = new POINT { X = screen.X, Y = screen.Y }
            };
            if ((Control.ModifierKeys & Keys.Control) != 0) info.fMask |= CMIC_MASK_CONTROL_DOWN;
            if ((Control.ModifierKeys & Keys.Shift) != 0) info.fMask |= CMIC_MASK_SHIFT_DOWN;
            menu.InvokeCommand(ref info);
            return verbName;
        }
        finally
        {
            hook?.ReleaseHandle();
            if (hmenu != IntPtr.Zero) DestroyMenu(hmenu);
            if (menu != null) Marshal.ReleaseComObject(menu);
            if (menuPtr != IntPtr.Zero) Marshal.Release(menuPtr);
            if (folder != null) Marshal.ReleaseComObject(folder);
            if (folderPtr != IntPtr.Zero) Marshal.Release(folderPtr);
            foreach (var p in pidls) Marshal.FreeCoTaskMem(p);
        }
    }

    /// <summary>Przekazuje komunikaty rysowania menu do rozszerzeń powłoki.</summary>
    private sealed class MenuMessageWindow : NativeWindow
    {
        private readonly IContextMenu3? _cm3;
        private readonly IContextMenu2? _cm2;

        public MenuMessageWindow(IntPtr owner, IContextMenu3? cm3, IContextMenu2? cm2)
        {
            _cm3 = cm3;
            _cm2 = cm2;
            AssignHandle(owner);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg is WM_INITMENUPOPUP or WM_DRAWITEM or WM_MEASUREITEM or WM_MENUCHAR)
            {
                try
                {
                    if (_cm3 != null)
                    {
                        if (_cm3.HandleMenuMsg2((uint)m.Msg, m.WParam, m.LParam, out var result) == 0)
                        {
                            m.Result = result;
                            return;
                        }
                    }
                    else if (_cm2 != null && _cm2.HandleMenuMsg((uint)m.Msg, m.WParam, m.LParam) == 0)
                    {
                        m.Result = m.Msg == WM_INITMENUPOPUP ? IntPtr.Zero : (IntPtr)1;
                        return;
                    }
                }
                catch { /* źle napisane rozszerzenie nie może wywrócić programu */ }
            }
            base.WndProc(ref m);
        }
    }

    // ───────────────────────── WinAPI ─────────────────────────

    private const uint FirstId = 1, LastId = 0x7FFF;
    private const uint CMF_NORMAL = 0x0, CMF_EXPLORE = 0x4, CMF_CANRENAME = 0x10, CMF_EXTENDEDVERBS = 0x100;
    private const uint GCS_VERBW = 0x4;
    private const uint TPM_RETURNCMD = 0x100, TPM_RIGHTBUTTON = 0x2;
    private const uint CMIC_MASK_UNICODE = 0x4000, CMIC_MASK_PTINVOKE = 0x20000000;
    private const uint CMIC_MASK_SHIFT_DOWN = 0x10000000, CMIC_MASK_CONTROL_DOWN = 0x40000000;
    private const int WM_INITMENUPOPUP = 0x0117, WM_DRAWITEM = 0x002B, WM_MEASUREITEM = 0x002C, WM_MENUCHAR = 0x0120;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, IntPtr bindingContext, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);

    [DllImport("shell32.dll")]
    private static extern int SHBindToParent(IntPtr pidl, ref Guid riid, out IntPtr ppv, out IntPtr ppidlLast);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenuEx(IntPtr hmenu, uint flags, int x, int y, IntPtr hwnd, IntPtr lptpm);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CMINVOKECOMMANDINFOEX
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public IntPtr lpVerb;
        [MarshalAs(UnmanagedType.LPStr)] public string? lpParameters;
        [MarshalAs(UnmanagedType.LPStr)] public string? lpDirectory;
        public int nShow;
        public uint dwHotKey;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.LPStr)] public string? lpTitle;
        public IntPtr lpVerbW;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpParametersW;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpDirectoryW;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpTitleW;
        public POINT ptInvoke;
    }

    [ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolder
    {
        void ParseDisplayName(IntPtr hwnd, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string name, out uint eaten, out IntPtr pidl, ref uint attributes);
        void EnumObjects(IntPtr hwnd, int flags, out IntPtr enumIdList);
        void BindToObject(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
        void BindToStorage(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);
        void CreateViewObject(IntPtr hwnd, ref Guid riid, out IntPtr ppv);
        void GetAttributesOf(uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, ref uint rgfInOut);
        [PreserveSig] int GetUIObjectOf(IntPtr hwnd, uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, ref Guid riid, IntPtr reserved, out IntPtr ppv);
        void GetDisplayNameOf(IntPtr pidl, uint flags, IntPtr name);
        void SetNameOf(IntPtr hwnd, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string name, uint flags, out IntPtr ppidlOut);
    }

    [ComImport, Guid("000214e4-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint flags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX info);
        [PreserveSig] int GetCommandString(UIntPtr idCmd, uint flags, IntPtr reserved, [MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int cch);
    }

    [ComImport, Guid("000214f4-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu2
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint flags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX info);
        [PreserveSig] int GetCommandString(UIntPtr idCmd, uint flags, IntPtr reserved, [MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int cch);
        [PreserveSig] int HandleMenuMsg(uint msg, IntPtr wParam, IntPtr lParam);
    }

    [ComImport, Guid("bcfce0a0-ec17-11d0-8d10-00a0c90f2719"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu3
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint flags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX info);
        [PreserveSig] int GetCommandString(UIntPtr idCmd, uint flags, IntPtr reserved, [MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int cch);
        [PreserveSig] int HandleMenuMsg(uint msg, IntPtr wParam, IntPtr lParam);
        [PreserveSig] int HandleMenuMsg2(uint msg, IntPtr wParam, IntPtr lParam, out IntPtr result);
    }
}
