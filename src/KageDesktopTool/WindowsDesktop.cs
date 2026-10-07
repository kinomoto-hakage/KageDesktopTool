using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using Kage.Workspace;

namespace Kage.Desktop;

// Explorer 窗口层次只在此适配器使用；找不到宿主时停止展示并保留实际目录入口。
internal static class WindowsDesktop
{
    internal static readonly int TaskbarCreated = (int)RegisterWindowMessage("TaskbarCreated");

    // 通知区域位于主任务栏；不能使用可能处于其他 DPI 的设置窗口计算图标尺寸。
    internal static System.Drawing.Size TrayIconSize()
    {
        var dpi = GetDpiForWindow(FindWindow("Shell_TrayWnd", null));
        if (dpi == 0) dpi = GetDpiForSystem();
        return new System.Drawing.Size(GetSystemMetricsForDpi(49, dpi), GetSystemMetricsForDpi(50, dpi));
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string className, string? title);
    [DllImport("user32.dll")] private static extern uint GetDpiForSystem();
    [DllImport("user32.dll")] private static extern int GetSystemMetricsForDpi(int index, uint dpi);

    internal static bool Attached(IntPtr window, IntPtr host) => IsWindow(window) && IsWindow(host) && GetParent(window) == host;

    internal static IntPtr Attach(IntPtr window)
    {
        var host = Host();
        if (!IsWindow(window) || host == IntPtr.Zero) return IntPtr.Zero;
        if (Attached(window, host)) return host;
        // SetParent 不转换 WS_POPUP／WS_CHILD，须在挂接前显式调整。
        var style = GetWindowLongPtr(window, -16).ToInt64();
        SetWindowLongPtr(window, -16, new IntPtr((style & ~0x80000000L) | 0x40000000L));
        SetParent(window, host);
        return Attached(window, host) ? host : IntPtr.Zero;
    }

    internal static bool Position(IntPtr window, IntPtr host, int x, int y, int width, int height)
    {
        if (!Attached(window, host)) return false;
        var point = new POINT { X = x, Y = y };
        return ScreenToClient(host, ref point)
            && SetWindowPos(window, IntPtr.Zero, point.X, point.Y, width, height, 0x10 | 0x40);
    }

    // 文件拖放只向调用方返回实际目标，不泄露 Explorer 窗口类名或层次。
    internal static MoveTarget? ContentTargetAt(POINT point)
    {
        var window = WindowFromPoint(point);
        var inView = false;
        for (var current = window; current != IntPtr.Zero; current = GetParent(current))
        {
            var name = new StringBuilder(256);
            GetClassName(current, name, name.Capacity);
            if (name.ToString() == "SHELLDLL_DefView") { inView = true; break; }
        }
        if (!inView) return null;
        var element = AutomationElement.FromPoint(new Point(point.X, point.Y));
        string? directoryName = null;
        for (var current = element; current != null; current = TreeWalker.ControlViewWalker.GetParent(current))
        {
            if (current.Current.ControlType == ControlType.ListItem) { directoryName = current.Current.Name; break; }
            if (current.Current.ControlType == ControlType.List) break;
        }
        var host = Host();
        if (window == host || IsChild(host, window))
        {
            if (directoryName == null) return MoveTarget.Desktop;
            var desktopItem = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), directoryName);
            return Directory.Exists(desktopItem) ? MoveTarget.Directory(desktopItem) : null;
        }
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
        dynamic windows = shell.Windows();
        try
        {
            for (var i = 0; i < (int)windows.Count; i++)
            {
                dynamic browser = windows.Item(i);
                if (browser == null) continue;
                try
                {
                    var handle = new IntPtr((long)browser.HWND);
                    if (window != handle && !IsChild(handle, window)) continue;
                    dynamic document = browser.Document;
                    dynamic folder = document.Folder;
                    dynamic self = folder.Self;
                    try
                    {
                        string actual = self.Path;
                        if (directoryName != null) actual = Path.Combine(actual, directoryName);
                        if (Path.IsPathFullyQualified(actual) && Directory.Exists(actual)) return MoveTarget.Directory(actual);
                        throw new IOException("资源管理器目标不是可用的实际目录；请选择明确的文件系统目录。");
                    }
                    finally { Marshal.FinalReleaseComObject(self); Marshal.FinalReleaseComObject(folder); Marshal.FinalReleaseComObject(document); }
                }
                finally { Marshal.FinalReleaseComObject(browser); }
            }
        }
        finally { Marshal.FinalReleaseComObject(windows); Marshal.FinalReleaseComObject(shell); }
        return null;
    }

    internal static DisplayArea[] Displays()
    {
        // 直接重新枚举，避免 Screen 的缓存仍指向已拔出的显示器或旧工作区。
        var areas = new List<DisplayArea>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr dc, ref RECT bounds, IntPtr data) =>
        {
            var info = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(monitor, ref info)) return true;
            var area = info.Work;
            var scale = GetDpiForMonitor(monitor, 0, out var x, out _) == 0 && x > 0 ? x / 96.0 : 1;
            if (area.Right > area.Left && area.Bottom > area.Top)
                areas.Add(new(area.Left, area.Top, area.Right - area.Left, area.Bottom - area.Top, scale));
            return true;
        }, IntPtr.Zero);
        return areas.ToArray();
    }

    internal static IntPtr Host()
    {
        var found = IntPtr.Zero;
        EnumWindows((window, _) =>
        {
            var view = FindWindowEx(window, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (view == IntPtr.Zero) return true;
            found = view;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    [StructLayout(LayoutKind.Sequential)] internal struct POINT { internal int X, Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct RECT { internal int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MONITORINFO { internal int Size; internal RECT Monitor, Work; internal uint Flags; }
    private delegate bool MonitorProc(IntPtr monitor, IntPtr dc, ref RECT bounds, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorProc callback, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr hwnd);
    private delegate bool EnumProc(IntPtr hwnd, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int capacity);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? className, string? title);
    [DllImport("user32.dll")] internal static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll")] internal static extern IntPtr GetParent(IntPtr child);
    [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hwnd, out RECT rectangle);
    [DllImport("user32.dll")] internal static extern bool ScreenToClient(IntPtr hwnd, ref POINT point);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] internal static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
    [DllImport("user32.dll")] internal static extern IntPtr WindowFromPoint(POINT point);
    [DllImport("user32.dll")] internal static extern bool IsChild(IntPtr parent, IntPtr child);
    [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wp, IntPtr lp);
    [DllImport("user32.dll")] internal static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extraInfo);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out POINT point);
}
