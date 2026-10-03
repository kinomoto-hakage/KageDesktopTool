using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace Kage.Desktop;

// 菜单来自实际对象的 IShellFolder，不重建系统或第三方命令。
internal static class ShellContextMenu
{
    internal static IntPtr ActiveMenu { get; private set; }
    internal static string? Show(Window owner, string[] paths, Point screen)
    {
        if (paths.Length == 0) return null;
        if (paths.Any(path => !string.Equals(Path.GetDirectoryName(path), Path.GetDirectoryName(paths[0]), StringComparison.OrdinalIgnoreCase)))
            throw new IOException("原生菜单要求同一内容文件夹中的选择集合。");
        // Explorer 子 HWND 不能成为前台菜单所有者；临时顶层窗口承接键盘及扩展菜单消息。
        var menuOwner = new Window { Width = 1, Height = 1, Opacity = 0, ShowInTaskbar = false,
            WindowStyle = WindowStyle.ToolWindow, Left = owner.Left, Top = owner.Top };
        menuOwner.Show();
        menuOwner.Activate();
        var handle = new WindowInteropHelper(menuOwner).Handle;
        var pidls = new IntPtr[paths.Length];
        IShellFolder? folder = null;
        object? context = null;
        var menu = IntPtr.Zero;
        HwndSource? source = null;
        HwndSourceHook? hook = null;
        try
        {
            var children = new IntPtr[paths.Length];
            for (var i = 0; i < paths.Length; i++)
            {
                Marshal.ThrowExceptionForHR(SHParseDisplayName(paths[i], IntPtr.Zero, out pidls[i], 0, out _));
                var iid = typeof(IShellFolder).GUID;
                Marshal.ThrowExceptionForHR(SHBindToParent(pidls[i], ref iid, out var parent, out children[i]));
                if (i == 0) folder = parent;
                else Marshal.ReleaseComObject(parent);
            }
            var contextId = typeof(IContextMenu).GUID;
            Marshal.ThrowExceptionForHR(folder!.GetUIObjectOf(handle, (uint)children.Length, children, ref contextId, IntPtr.Zero, out context));
            var commands = (IContextMenu)context;
            menu = CreatePopupMenu();
            if (menu == IntPtr.Zero) throw new IOException("Windows 未能创建菜单。");
            // CMF_NORMAL；Shift 同时请求扩展命令，保留传统完整菜单的处理器。
            Marshal.ThrowExceptionForHR(commands.QueryContextMenu(menu, 0, 1, 0x7fff, (paths.Length == 1 ? 0x10u : 0u) | ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 0x100u : 0u)));
            source = HwndSource.FromHwnd(handle);
            hook = (IntPtr hwnd, int message, IntPtr wp, IntPtr lp, ref bool handled) =>
            {
                if (message is not (0x117 or 0x2b or 0x2c or 0x120)) return IntPtr.Zero;
                if (context is IContextMenu3 third && third.HandleMenuMsg2((uint)message, wp, lp, out var result) >= 0)
                { handled = true; return result; }
                if (message != 0x120 && context is IContextMenu2 second && second.HandleMenuMsg((uint)message, wp, lp) >= 0) handled = true;
                return IntPtr.Zero;
            };
            source.AddHook(hook);
            SetForegroundWindow(handle);
            ActiveMenu = menu;
            var command = TrackPopupMenuEx(menu, 0x100 | 0x2, (int)screen.X, (int)screen.Y, handle, IntPtr.Zero);
            if (command == 0) return null;
            var verb = new StringBuilder(256);
            // Shell 的 rename 依赖宿主内联编辑；本宿主经业务接口提交真实改名。
            if (paths.Length == 1 && commands.GetCommandString(new UIntPtr(command - 1), 4, IntPtr.Zero, verb, (uint)verb.Capacity) >= 0
                && verb.ToString().Equals("rename", StringComparison.OrdinalIgnoreCase)) return paths[0];
            var info = new InvokeInfo
            {
                Size = Marshal.SizeOf<InvokeInfo>(), Mask = 0x4000 | 0x20000000
                    | ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 0x10000000 : 0)
                    | ((Keyboard.Modifiers & ModifierKeys.Control) != 0 ? 0x40000000 : 0),
                Window = handle, Verb = new IntPtr(command - 1), VerbUnicode = new IntPtr(command - 1), Show = 1,
                Point = new NativePoint { X = (int)screen.X, Y = (int)screen.Y }
            };
            Marshal.ThrowExceptionForHR(commands.InvokeCommand(ref info));
            return null;
        }
        finally
        {
            if (hook != null) source?.RemoveHook(hook);
            ActiveMenu = IntPtr.Zero;
            if (menu != IntPtr.Zero) DestroyMenu(menu);
            if (context != null) Marshal.ReleaseComObject(context);
            if (folder != null) Marshal.ReleaseComObject(folder);
            foreach (var pidl in pidls) if (pidl != IntPtr.Zero) Marshal.FreeCoTaskMem(pidl);
            menuOwner.Close();
        }
    }

    [ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolder
    {
        void ParseDisplayName(); void EnumObjects(); void BindToObject(); void BindToStorage(); void CompareIDs();
        void CreateViewObject(); void GetAttributesOf();
        [PreserveSig] int GetUIObjectOf(IntPtr owner, uint count, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] IntPtr[] children,
            ref Guid iid, IntPtr reserved, [MarshalAs(UnmanagedType.IUnknown)] out object context);
    }

    [ComImport, Guid("000214E4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(IntPtr menu, uint index, uint first, uint last, uint flags);
        [PreserveSig] int InvokeCommand(ref InvokeInfo info);
        [PreserveSig] int GetCommandString(UIntPtr command, uint flags, IntPtr reserved, [MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, uint capacity);
    }
    [ComImport, Guid("000214F4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu2
    {
        void QueryContextMenu(); void InvokeCommand(); void GetCommandString();
        [PreserveSig] int HandleMenuMsg(uint message, IntPtr wp, IntPtr lp);
    }
    [ComImport, Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu3
    {
        void QueryContextMenu(); void InvokeCommand(); void GetCommandString(); void HandleMenuMsg();
        [PreserveSig] int HandleMenuMsg2(uint message, IntPtr wp, IntPtr lp, out IntPtr result);
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { internal int X, Y; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct InvokeInfo
    {
        internal int Size, Mask;
        internal IntPtr Window, Verb, Parameters, Directory;
        internal int Show, HotKey;
        internal IntPtr Icon, Title, VerbUnicode, ParametersUnicode, DirectoryUnicode, TitleUnicode;
        internal NativePoint Point;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string name, IntPtr bind, out IntPtr pidl, uint mask, out uint attributes);
    [DllImport("shell32.dll")] private static extern int SHBindToParent(IntPtr pidl, ref Guid iid, out IShellFolder folder, out IntPtr child);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr owner, IntPtr parameters);
}
