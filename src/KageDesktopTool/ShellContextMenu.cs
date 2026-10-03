using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;

namespace Kage.Desktop;

// 原生菜单由 Explorer 拥有，跟随当前 Windows 的现代／传统设置与扩展。
internal static class ShellContextMenu
{
    internal static IntPtr ActiveMenuWindow { get; private set; }
    internal static IntPtr ExplorerWindow { get; private set; }
    internal static long LastMenuLatency { get; private set; }
    internal static string LastMenuClass { get; private set; } = "";
    private static int showing;
    private static CancellationTokenSource? currentRequest;
    internal static void CancelPending() => Volatile.Read(ref currentRequest)?.Cancel();

    internal static async Task ShowAsync(string[] paths)
    {
        if (paths.Length == 0) return;
        if (Interlocked.CompareExchange(ref showing, 1, 0) != 0) throw new InvalidOperationException("当前系统菜单尚未结束，请先完成或取消。");
        using var cancellation = new CancellationTokenSource();
        currentRequest = cancellation;
        try { await ShowSelectionAsync(paths, cancellation.Token); }
        finally { currentRequest = null; ActiveMenuWindow = IntPtr.Zero; Interlocked.Exchange(ref showing, 0); }
    }

    private static async Task ShowSelectionAsync(string[] paths, CancellationToken cancellation)
    {
        var directory = Path.GetDirectoryName(paths[0])!;
        if (paths.Any(path => !string.Equals(Path.GetDirectoryName(path), directory, StringComparison.OrdinalIgnoreCase)))
            throw new IOException("原生菜单要求同一内容文件夹中的选择集合。");
        var watch = Stopwatch.StartNew();
        ActiveMenuWindow = ExplorerWindow = IntPtr.Zero;
        await Task.Run(() => OpenSelection(directory, paths), cancellation);
        cancellation.ThrowIfCancellationRequested();
        IntPtr window = IntPtr.Zero;
        Point? itemPoint = null;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            (window, itemPoint) = await Task.Run(() => FindSelection(directory, paths));
            if (window != IntPtr.Zero && itemPoint != null) break;
            await Task.Delay(80, cancellation);
        }
        if (window == IntPtr.Zero || itemPoint == null) throw new IOException("已请求在资源管理器定位选择，暂未找到可见项目；可在资源管理器直接右键。");
        ExplorerWindow = window;
        SetForegroundWindow(window);
        await Task.Delay(80, cancellation);
        var point = new WindowsDesktop.POINT { X = (int)itemPoint.Value.X, Y = (int)itemPoint.Value.Y };
        var hit = WindowsDesktop.WindowFromPoint(point);
        if (hit != window && !WindowsDesktop.IsChild(window, hit))
            throw new IOException("资源管理器中的选中项目被其他窗口遮挡，未发送右键；请在资源管理器直接操作。");
        SetCursorPos(point.X, point.Y);
        await Task.Delay(40, cancellation);
        mouse_event(8, 0, 0, 0, UIntPtr.Zero);
        try { await Task.Delay(30); }
        finally { mouse_event(16, 0, 0, 0, UIntPtr.Zero); }
        for (var attempt = 0; attempt < 60; attempt++)
        {
            ActiveMenuWindow = FindMenu(window);
            if (ActiveMenuWindow != IntPtr.Zero) break;
            await Task.Delay(50, cancellation);
        }
        LastMenuLatency = watch.ElapsedMilliseconds;
        if (ActiveMenuWindow == IntPtr.Zero) throw new IOException("资源管理器已定位当前选择，系统菜单暂未就绪；可直接右键重试。");
        var menuClass = new StringBuilder(256); GetClassName(ActiveMenuWindow, menuClass, menuClass.Capacity);
        LastMenuClass = menuClass.ToString();
        try
        {
            while (true)
            {
                var menu = FindMenu(window);
                if (menu != IntPtr.Zero) { ActiveMenuWindow = menu; await Task.Delay(60, cancellation); continue; }
                // “显示更多选项”可能短暂销毁现代窗口，再创建传统窗口。
                await Task.Delay(180, cancellation);
                if (FindMenu(window) == IntPtr.Zero) break;
            }
        }
        finally { ActiveMenuWindow = IntPtr.Zero; }
    }

    private static void OpenSelection(string directory, string[] paths)
    {
        var initialized = CoInitializeEx(IntPtr.Zero, 0) >= 0;
        var folder = IntPtr.Zero;
        var pidls = new IntPtr[paths.Length];
        try
        {
            Marshal.ThrowExceptionForHR(SHParseDisplayName(directory, IntPtr.Zero, out folder, 0, out _));
            var children = new IntPtr[paths.Length];
            for (var i = 0; i < paths.Length; i++)
            {
                Marshal.ThrowExceptionForHR(SHParseDisplayName(paths[i], IntPtr.Zero, out pidls[i], 0, out _));
                children[i] = ILFindLastID(pidls[i]);
            }
            Marshal.ThrowExceptionForHR(SHOpenFolderAndSelectItems(folder, (uint)children.Length, children, 0));
        }
        finally
        {
            foreach (var pidl in pidls) if (pidl != IntPtr.Zero) Marshal.FreeCoTaskMem(pidl);
            if (folder != IntPtr.Zero) Marshal.FreeCoTaskMem(folder);
            if (initialized) CoUninitialize();
        }
    }

    private static (IntPtr Window, Point? Item) FindSelection(string directory, string[] expected)
    {
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
                    dynamic document = browser.Document; dynamic folder = document.Folder; dynamic self = folder.Self;
                    try
                    {
                        if (!string.Equals((string)self.Path, directory, StringComparison.OrdinalIgnoreCase)) continue;
                        dynamic selected = document.SelectedItems();
                        var actual = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        try
                        {
                            for (var index = 0; index < (int)selected.Count; index++)
                            {
                                dynamic entry = selected.Item(index);
                                try { actual.Add((string)entry.Path); }
                                finally { Marshal.ReleaseComObject(entry); }
                            }
                        }
                        finally { Marshal.ReleaseComObject(selected); }
                        if (!actual.SetEquals(expected)) continue;
                        var hwnd = new IntPtr((long)browser.HWND);
                        var selection = AutomationElement.FromHandle(hwnd).FindAll(TreeScope.Descendants, new AndCondition(
                            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
                            new PropertyCondition(SelectionItemPattern.IsSelectedProperty, true),
                            new PropertyCondition(AutomationElement.IsOffscreenProperty, false)));
                        foreach (AutomationElement item in selection)
                        {
                            var rect = item.Current.BoundingRectangle;
                            if (!rect.IsEmpty && rect.Width > 0 && rect.Height > 0) return (hwnd, new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2));
                        }
                    }
                    finally { Marshal.ReleaseComObject(self); Marshal.ReleaseComObject(folder); Marshal.ReleaseComObject(document); }
                }
                catch (Exception e) when (e is COMException or ElementNotAvailableException or InvalidOperationException) { }
                finally { Marshal.ReleaseComObject(browser); }
            }
        }
        finally { Marshal.ReleaseComObject(windows); Marshal.ReleaseComObject(shell); }
        return (IntPtr.Zero, null);
    }

    private static IntPtr FindMenu(IntPtr explorer)
    {
        GetWindowThreadProcessId(explorer, out var pid);
        var result = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var ownerPid);
            if (ownerPid != pid || !WindowsDesktop.IsWindowVisible(hwnd)) return true;
            var name = new StringBuilder(256); GetClassName(hwnd, name, name.Capacity);
            if (name.ToString() is "#32768" or "Microsoft.UI.Content.PopupWindowSiteBridge" or "Xaml_WindowedPopupClass") result = hwnd;
            return true;
        }, IntPtr.Zero);
        return result;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string name, IntPtr context, out IntPtr pidl, uint mask, out uint attributes);
    [DllImport("shell32.dll")] private static extern IntPtr ILFindLastID(IntPtr pidl);
    [DllImport("shell32.dll")] private static extern int SHOpenFolderAndSelectItems(IntPtr folder, uint count, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] IntPtr[] children, uint flags);
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint flags);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder text, int count);
    private delegate bool WindowCallback(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, IntPtr parameter);
}
