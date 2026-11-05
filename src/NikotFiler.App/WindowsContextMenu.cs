using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace NikotFiler.App;

internal sealed class WindowsContextMenu : IDisposable
{
    public const uint NewTabCommand = 0x7000;
    private IShellItemArray? _items;
    private IContextMenu? _context;
    private IContextMenu2? _context2;
    private IContextMenu3? _context3;
    private IntPtr _menu;
    private readonly string _directory;
    public int CommandCount { get; private set; }

    public WindowsContextMenu(string[] paths)
    {
        if (paths.Length == 0) throw new ArgumentException("対象を選択してください。", nameof(paths));
        paths = paths.Select(Path.GetFullPath).ToArray();
        _directory = Path.GetDirectoryName(paths[0]) ?? throw new ArgumentException("親フォルダーがありません。");
        if (paths.Any(path => !string.Equals(Path.GetDirectoryName(path), _directory, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("同じフォルダーの項目を選択してください。");
        var ids = new IntPtr[paths.Length];
        try
        {
            for (var i = 0; i < paths.Length; i++)
                Marshal.ThrowExceptionForHR(SHParseDisplayName(paths[i], IntPtr.Zero, out ids[i], 0, out _));
            Marshal.ThrowExceptionForHR(SHCreateShellItemArrayFromIDLists((uint)ids.Length, ids, out _items));
            var handler = new Guid("3981e225-f559-11d3-8e3a-00c04f6837d5");
            var contextId = typeof(IContextMenu).GUID;
            _items.BindToHandler(IntPtr.Zero, ref handler, ref contextId, out var context);
            _context = (IContextMenu)context;
            _context2 = context as IContextMenu2;
            _context3 = context as IContextMenu3;
            _menu = CreatePopupMenu();
            if (_menu == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            var result = _context.QueryContextMenu(_menu, 0, 1, NewTabCommand - 1, 0x10 | 0x1000); // CANRENAME | SYNCCASCADEMENU
            Marshal.ThrowExceptionForHR(result);
            CommandCount = result & 0xffff;
            if (paths.Length == 1 && Directory.Exists(paths[0]))
            {
                AppendMenu(_menu, 0x800, 0, null);
                AppendMenu(_menu, 0, NewTabCommand, "新しいタブで開く");
            }
        }
        catch { Dispose(); throw; }
        finally { foreach (var id in ids) if (id != IntPtr.Zero) Marshal.FreeCoTaskMem(id); }
    }

    public string GetVerb(uint command)
    {
        if (command == 0 || command >= NewTabCommand) return "";
        var buffer = Marshal.AllocHGlobal(1024);
        try
        {
            Marshal.WriteInt16(buffer, 0);
            if (_context!.GetCommandString(command - 1, 4, IntPtr.Zero, buffer, 512) >= 0)
                return (Marshal.PtrToStringUni(buffer) ?? "").ToLowerInvariant();
            Marshal.WriteByte(buffer, 0);
            return _context.GetCommandString(command - 1, 0, IntPtr.Zero, buffer, 512) >= 0
                ? (Marshal.PtrToStringAnsi(buffer) ?? "").ToLowerInvariant() : "";
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    public string GetLabel(uint command)
    {
        var label = new StringBuilder(1024);
        GetMenuString(_menu, command, label, label.Capacity, 0);
        return label.ToString().Replace("&", "").Split('\t')[0];
    }

    public uint Show(Window owner, Point position)
    {
        var handle = new WindowInteropHelper(owner).EnsureHandle();
        var source = HwndSource.FromHwnd(handle) ?? throw new InvalidOperationException("ウィンドウが見つかりません。");
        source.AddHook(HandleMessage);
        try { return TrackPopupMenuEx(_menu, 0x100 | 0x80 | 0x2, (int)position.X, (int)position.Y, handle, IntPtr.Zero); }
        finally { source.RemoveHook(HandleMessage); }
    }

    public static bool UsesAppFileOperation(string verb) => verb is "copy" or "cut" or "paste" or "delete" or "rename";

    public void Invoke(Window owner, uint command)
    {
        if (command == 0 || command >= NewTabCommand) throw new ArgumentOutOfRangeException(nameof(command));
        if (UsesAppFileOperation(GetVerb(command))) throw new InvalidOperationException("この操作は nikotFiler の確認画面を通して実行します。");
        var directory = Marshal.StringToCoTaskMemUni(_directory);
        try
        {
            var invoke = new InvokeCommandInfo
            {
                Size = Marshal.SizeOf<InvokeCommandInfo>(),
                Mask = 0x4000, // UNICODE
                Owner = new WindowInteropHelper(owner).EnsureHandle(),
                Verb = new IntPtr(command - 1),
                VerbUnicode = new IntPtr(command - 1),
                DirectoryUnicode = directory,
                Show = 1
            };
            Marshal.ThrowExceptionForHR(_context!.InvokeCommand(ref invoke));
        }
        finally { Marshal.FreeCoTaskMem(directory); }
    }

    private IntPtr HandleMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message is not (0x117 or 0x2b or 0x2c or 0x120)) return IntPtr.Zero;
        if (message is 0x2b or 0x2c && wParam != IntPtr.Zero) return IntPtr.Zero;
        if (_context3 is not null && _context3.HandleMenuMsg2((uint)message, wParam, lParam, out var result) == 0)
        {
            handled = true;
            return result;
        }
        if (_context2 is not null && _context2.HandleMenuMsg((uint)message, wParam, lParam) == 0) handled = true;
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_menu != IntPtr.Zero) { DestroyMenu(_menu); _menu = IntPtr.Zero; }
        _context2 = null;
        _context3 = null;
        if (_context is not null) { Marshal.ReleaseComObject(_context); _context = null; }
        if (_items is not null) { Marshal.ReleaseComObject(_items); _items = null; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InvokeCommandInfo
    {
        public int Size;
        public uint Mask;
        public IntPtr Owner, Verb, Parameters, Directory;
        public int Show;
        public uint HotKey;
        public IntPtr Icon, Title, VerbUnicode, ParametersUnicode, DirectoryUnicode, TitleUnicode;
        public int X, Y;
    }

    [ComImport, Guid("b63ea76d-1f85-456f-a19c-48159efa858b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemArray
    {
        void BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid id, [MarshalAs(UnmanagedType.Interface)] out object result);
    }

    [ComImport, Guid("000214e4-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(IntPtr menu, uint index, uint first, uint last, uint flags);
        [PreserveSig] int InvokeCommand(ref InvokeCommandInfo command);
        [PreserveSig] int GetCommandString(nuint command, uint flags, IntPtr reserved, IntPtr name, uint maximum);
    }

    // Keep the native vtable slots explicit for each COM interface.
    [ComImport, Guid("000214f4-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu2
    {
        [PreserveSig] int QueryContextMenu(IntPtr menu, uint index, uint first, uint last, uint flags);
        [PreserveSig] int InvokeCommand(ref InvokeCommandInfo command);
        [PreserveSig] int GetCommandString(nuint command, uint flags, IntPtr reserved, IntPtr name, uint maximum);
        [PreserveSig] int HandleMenuMsg(uint message, IntPtr wParam, IntPtr lParam);
    }

    [ComImport, Guid("bcfce0a0-ec17-11d0-8d10-00a0c90f2719"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu3
    {
        [PreserveSig] int QueryContextMenu(IntPtr menu, uint index, uint first, uint last, uint flags);
        [PreserveSig] int InvokeCommand(ref InvokeCommandInfo command);
        [PreserveSig] int GetCommandString(nuint command, uint flags, IntPtr reserved, IntPtr name, uint maximum);
        [PreserveSig] int HandleMenuMsg(uint message, IntPtr wParam, IntPtr lParam);
        [PreserveSig] int HandleMenuMsg2(uint message, IntPtr wParam, IntPtr lParam, out IntPtr result);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, IntPtr bindContext, out IntPtr id, uint requested, out uint attributes);
    [DllImport("shell32.dll")]
    private static extern int SHCreateShellItemArrayFromIDLists(uint count, [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] IntPtr[] ids, out IShellItemArray items);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(IntPtr menu, uint flags, nuint command, string? text);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetMenuString(IntPtr menu, uint command, StringBuilder text, int maximum, uint flags);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr owner, IntPtr parameters);
}
