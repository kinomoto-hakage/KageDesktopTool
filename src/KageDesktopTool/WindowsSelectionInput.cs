using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Kage.Desktop;

// 每个 Runtime 只安装一组系统监听；回调只判定 HWND 并排队，不消费原始输入。
internal sealed class WindowsSelectionInput : IDisposable
{
    private readonly Runtime runtime;
    private readonly SelectionCoordinator selection;
    private readonly MouseHook mouseCallback;
    private readonly ForegroundHook foregroundCallback;
    private IntPtr mouseHook;
    private IntPtr foregroundHook;
    private bool disposed;

    internal WindowsSelectionInput(Runtime runtime, SelectionCoordinator selection)
    {
        this.runtime = runtime;
        this.selection = selection;
        mouseCallback = Mouse;
        foregroundCallback = Foreground;
        mouseHook = SetWindowsHookEx(14, mouseCallback, GetModuleHandle(null), 0);
        foregroundHook = SetWinEventHook(3, 3, IntPtr.Zero, foregroundCallback, 0, 0, 0);
        if (mouseHook != IntPtr.Zero && foregroundHook != IntPtr.Zero) return;
        var error = Marshal.GetLastWin32Error();
        Dispose();
        throw new Win32Exception(error, "无法监听桌面点击或前台切换。");
    }

    private bool InFolder(IntPtr window)
    {
        foreach (var header in runtime.Headers.Values)
            if (header.OwnsInputWindow(window)) return true;
        return false;
    }

    private bool OnDesktop(IntPtr window)
    {
        if (InFolder(window)) return true;
        var root = GetAncestor(window, 2);
        foreach (var header in runtime.Headers.Values)
            if (header.Host != IntPtr.Zero && root == GetAncestor(header.Host, 2)) return true;
        return false;
    }

    private IntPtr Mouse(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && !disposed && message.ToInt64() is 0x201 or 0x204 or 0x207 or 0x20B)
        {
            var input = Marshal.PtrToStructure<MouseInput>(data);
            var window = WindowsDesktop.WindowFromPoint(input.Point);
            if (!InFolder(window) && !selection.OwnsCommandWindow(window)) QueueClear();
        }
        return CallNextHookEx(mouseHook, code, message, data);
    }

    private void Foreground(IntPtr hook, uint type, IntPtr window, int objectId, int childId, uint thread, uint time)
    {
        if (!disposed && window != IntPtr.Zero && !OnDesktop(window) && !selection.OwnsCommandWindow(window))
            QueueClear(window);
    }

    private void QueueClear(IntPtr foreground = default)
    {
        var revision = selection.Revision;
        runtime.Controller.Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() =>
        {
            // 旧通知不能清除其后新建的选择；关闭后排队的通知不能影响新 Runtime。
            if (disposed || runtime.Exiting || selection.Revision != revision) return;
            if (foreground != IntPtr.Zero && (GetForegroundWindow() != foreground
                || OnDesktop(foreground) || selection.OwnsCommandWindow(foreground))) return;
            selection.Clear();
        }));
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (mouseHook != IntPtr.Zero) UnhookWindowsHookEx(mouseHook);
        if (foregroundHook != IntPtr.Zero) UnhookWinEvent(foregroundHook);
        mouseHook = foregroundHook = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        internal WindowsDesktop.POINT Point;
        internal uint Data, Flags, Time;
        internal UIntPtr Extra;
    }
    private delegate IntPtr MouseHook(int code, IntPtr message, IntPtr data);
    private delegate void ForegroundHook(IntPtr hook, uint type, IntPtr window, int objectId, int childId, uint thread, uint time);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int type, MouseHook callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWinEventHook(uint first, uint last, IntPtr module, ForegroundHook callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
}
