using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Kage.Workspace;
using Forms = System.Windows.Forms;

namespace Kage.Desktop;

// Explorer 窗口层次只在此适配器使用；找不到宿主时停止展示并保留实际目录入口。
internal static class WindowsDesktop
{
    internal static DisplayArea[] Displays() => Forms.Screen.AllScreens.Select(screen =>
    {
        var area = screen.WorkingArea;
        var monitor = MonitorFromPoint(new POINT { X = area.Left + area.Width / 2, Y = area.Top + area.Height / 2 }, 2);
        var scale = GetDpiForMonitor(monitor, 0, out var x, out _) == 0 ? x / 96.0 : 1;
        return new DisplayArea(area.Left, area.Top, area.Width, area.Height, scale);
    }).ToArray();

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
    private delegate bool EnumProc(IntPtr hwnd, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr parameter);
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
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT point, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
    [DllImport("user32.dll")] internal static extern IntPtr WindowFromPoint(POINT point);
    [DllImport("user32.dll")] internal static extern bool IsChild(IntPtr parent, IntPtr child);
    [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wp, IntPtr lp);
    [DllImport("user32.dll")] internal static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extraInfo);
}
