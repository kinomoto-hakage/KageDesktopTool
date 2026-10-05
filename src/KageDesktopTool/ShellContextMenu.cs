using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Kage.Desktop;

internal enum ShellPathPresence { Present, Missing, Unavailable }
internal sealed record ShellObservedPath(string Path, ShellPathPresence Presence);
internal sealed record ShellCommandResult(string? RenamePath, string Command, string Verb, ShellObservedPath[] Paths, bool Cancelled = false);

// 真正的传统对象菜单在独立 STA 线程构建与跟踪，Folder 界面继续响应。
internal static class ShellContextMenu
{
    private static readonly Lazy<MenuWorker> worker = new(() => new MenuWorker());
    private static CancellationTokenSource? currentRequest;
    private static int showing;
    private static int cancellationRequestId;
    private static int invalidationGeneration;
    private const int CancelMenuMessage = 0x8000 + 42;
    private static IntPtr ownerWindow;
    private static IntPtr activeMenu;
    internal static IntPtr OwnerWindow => Volatile.Read(ref ownerWindow);
    internal static long LastMenuLatency { get; private set; }
    internal static long LastObjectLatency { get; private set; }
    internal static long LastBuildLatency { get; private set; }
    internal static long LastBuildTicks { get; private set; }
    internal static string LastMenuClass { get; private set; } = "";
    internal static IntPtr ActiveMenuWindow => Volatile.Read(ref activeMenu) == IntPtr.Zero ? IntPtr.Zero : FindMenu(OwnerWindow);

    internal static void InvalidatePrepared()
    {
        Interlocked.Increment(ref invalidationGeneration);
        if (worker.IsValueCreated) _ = worker.Value.ClearPreparedAsync();
    }

    internal static void CancelPending()
    {
        try { Volatile.Read(ref currentRequest)?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    internal static Task<ShellCommandResult?> ShowAsync(string[] paths)
    {
        GetCursorPos(out var point);
        return ShowAsync(paths, new Point(point.X, point.Y));
    }

    internal static async Task<ShellCommandResult?> ShowAsync(string[] paths, Point screen, bool shift = false, bool control = false)
    {
        if (paths.Length == 0) return null;
        if (Interlocked.CompareExchange(ref showing, 1, 0) != 0)
            throw new InvalidOperationException("当前文件菜单尚未结束，请先完成或取消。");
        using var cancellation = new CancellationTokenSource();
        Volatile.Write(ref currentRequest, cancellation);
        LastMenuLatency = LastObjectLatency = LastBuildLatency = LastBuildTicks = 0; LastMenuClass = "";
        var watch = Stopwatch.StartNew();
        try
        {
            cancellation.Token.ThrowIfCancellationRequested();
            return await worker.Value.ShowAsync(paths.ToArray(), screen, shift, control, watch, cancellation.Token);
        }
        finally { Volatile.Write(ref currentRequest, null); Interlocked.Exchange(ref showing, 0); }
    }

    private sealed class MenuWorker
    {
        private readonly TaskCompletionSource<Dispatcher> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private HwndSource? source;
        private PreparedMenu? prepared;
        private DispatcherTimer? expiry;

        internal MenuWorker()
        {
            var thread = new Thread(() =>
            {
                var initialized = false;
                try
                {
                    Marshal.ThrowExceptionForHR(CoInitializeEx(IntPtr.Zero, 2));
                    initialized = true;
                    source = new HwndSource(new HwndSourceParameters("Kage 文件菜单宿主")
                    {
                        Width = 0, Height = 0, WindowStyle = unchecked((int)0x80000000),
                        ExtendedWindowStyle = 0x80
                    });
                    ready.SetResult(Dispatcher.CurrentDispatcher);
                    Dispatcher.Run();
                }
                catch (Exception e) { ready.TrySetException(e); }
                finally
                {
                    prepared?.Dispose();
                    source?.Dispose();
                    if (initialized) CoUninitialize();
                }
            }) { IsBackground = true, Name = "Kage 原生文件菜单" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        private void Expire(object? sender, EventArgs e) => Clear();

        internal async Task ClearPreparedAsync()
        {
            try
            {
                var dispatcher = await ready.Task;
                await dispatcher.InvokeAsync(Clear).Task;
            }
            catch (Exception) { /* 后台准备失败不会改变实际右键的失败处理。 */ }
        }

        private void Clear()
        {
            expiry?.Stop();
            prepared?.Dispose(); prepared = null;
        }

        internal async Task<ShellCommandResult?> ShowAsync(string[] paths, Point screen, bool shift, bool control, Stopwatch watch, CancellationToken cancellation)
        {
            var dispatcher = await ready.Task.WaitAsync(cancellation);
            return await dispatcher.InvokeAsync(() => Show(paths, screen, shift, control, watch, cancellation)).Task;
        }

        private ShellCommandResult? Show(string[] paths, Point screen, bool shift, bool control, Stopwatch watch, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            var handle = source!.Handle;
            Volatile.Write(ref ownerWindow, handle);
            PreparedMenu? native = null;
            HwndSourceHook? hook = null;
            var requestId = Interlocked.Increment(ref cancellationRequestId);
            using var registration = cancellation.Register(() => PostMessage(handle, CancelMenuMessage, new IntPtr(requestId), IntPtr.Zero));
            try
            {
                if (prepared != null)
                {
                    expiry?.Stop();
                    if (prepared.Matches(paths, shift)) native = prepared;
                    else prepared.Dispose();
                    prepared = null;
                }
                if (native == null)
                {
                    native = Build(paths, shift);
                    LastObjectLatency = native.ObjectLatency;
                    LastBuildLatency = native.BuildLatency;
                    LastBuildTicks = native.BuildTicks;
                }
                var context = native.Context;
                var commands = (IContextMenu)context!;
                var menu = native.Menu;
                cancellation.ThrowIfCancellationRequested();
                hook = (IntPtr hwnd, int message, IntPtr wp, IntPtr lp, ref bool handled) =>
                {
                    if (message == CancelMenuMessage)
                    {
                        if (wp.ToInt32() == requestId && cancellation.IsCancellationRequested) EndMenu();
                        handled = true; return IntPtr.Zero;
                    }
                    if (message is not (0x117 or 0x2b or 0x2c or 0x120)) return IntPtr.Zero;
                    if (context is IContextMenu3 third && third.HandleMenuMsg2((uint)message, wp, lp, out var result) >= 0)
                    { handled = true; return result; }
                    if (message != 0x120 && context is IContextMenu2 second && second.HandleMenuMsg((uint)message, wp, lp) >= 0) handled = true;
                    return IntPtr.Zero;
                };
                source.AddHook(hook);
                SetWindowPos(handle, IntPtr.Zero, (int)screen.X, (int)screen.Y, 0, 0, 0x14);
                ShowWindow(handle, 5);
                SetForegroundWindow(handle);
                cancellation.ThrowIfCancellationRequested();
                LastMenuLatency = watch.ElapsedMilliseconds; LastMenuClass = "#32768";
                Volatile.Write(ref activeMenu, menu);
                var command = TrackPopupMenuEx(menu, 0x100 | 0x2, (int)screen.X, (int)screen.Y, handle, IntPtr.Zero);
                Volatile.Write(ref activeMenu, IntPtr.Zero);
                if (command == 0 && native.Matches(paths, shift))
                {
                    // 仅取消的完整菜单短期复用；不额外发起可能阻塞其他图标的后台 Shell 查询。
                    prepared = native; native = null;
                    expiry ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
                    expiry.Interval = TimeSpan.FromSeconds(Math.Max(0.01, 5 - Stopwatch.GetElapsedTime(prepared.Completed).TotalSeconds));
                    expiry.Tick -= Expire; expiry.Tick += Expire; expiry.Start();
                }
                cancellation.ThrowIfCancellationRequested();
                if (command == 0) return null;
                var verb = new StringBuilder(256);
                _ = commands.GetCommandString(new UIntPtr(command - 1), 4, IntPtr.Zero, verb, (uint)verb.Capacity);
                var label = new StringBuilder(256);
                GetMenuString(menu, command, label, label.Capacity, 0);
                var commandName = label.Length == 0 ? (verb.Length == 0 ? "所选命令" : verb.ToString()) : label.ToString().Replace("&", "").Split('\t')[0];
                if (paths.Length == 1 && verb.ToString().Equals("rename", StringComparison.OrdinalIgnoreCase))
                    return new(paths[0], commandName, verb.ToString(), []);
                var info = new InvokeInfo
                {
                    Size = Marshal.SizeOf<InvokeInfo>(), Mask = 0x4000 | 0x20000000
                        | (shift ? 0x10000000 : 0) | (control ? 0x40000000 : 0),
                    Window = handle, Verb = new IntPtr(command - 1), VerbUnicode = new IntPtr(command - 1), Show = 1,
                    Point = new NativePoint { X = (int)screen.X, Y = (int)screen.Y }
                };
                var outcome = commands.InvokeCommand(ref info);
                if (outcome is unchecked((int)0x800704C7) or unchecked((int)0x80004004))
                    return new(null, commandName, verb.ToString(), paths.Select(ObservePath).ToArray(), Cancelled: true);
                Marshal.ThrowExceptionForHR(outcome);
                return new(null, commandName, verb.ToString(), paths.Select(ObservePath).ToArray());
            }
            finally
            {
                Volatile.Write(ref activeMenu, IntPtr.Zero);
                if (hook != null) source.RemoveHook(hook);
                ShowWindow(handle, 0);
                native?.Dispose();
            }
        }

        private PreparedMenu Build(string[] paths, bool shift)
        {
            if (paths.Any(path => !string.Equals(Path.GetDirectoryName(path), Path.GetDirectoryName(paths[0]), StringComparison.OrdinalIgnoreCase)))
                throw new IOException("原生菜单要求同一内容文件夹中的选择集合。");
            var native = new PreparedMenu(paths, shift);
            var watch = Stopwatch.StartNew();
            try
            {
                var children = new IntPtr[paths.Length];
                for (var i = 0; i < paths.Length; i++)
                {
                    Marshal.ThrowExceptionForHR(SHParseDisplayName(paths[i], IntPtr.Zero, out native.Pidls[i], 0, out _));
                    var iid = typeof(IShellFolder).GUID;
                    Marshal.ThrowExceptionForHR(SHBindToParent(native.Pidls[i], ref iid, out var parent, out children[i]));
                    if (i == 0) native.Folder = parent; else Marshal.ReleaseComObject(parent);
                }
                var contextId = typeof(IContextMenu).GUID;
                Marshal.ThrowExceptionForHR(native.Folder!.GetUIObjectOf(source!.Handle, (uint)children.Length, children, ref contextId, IntPtr.Zero, out native.Context));
                native.ObjectLatency = watch.ElapsedMilliseconds;
                native.Menu = CreatePopupMenu();
                if (native.Menu == IntPtr.Zero) throw new IOException("Windows 未能创建文件菜单。");
                // 完整系统及第三方菜单仍由 Shell 提供；诊断计时围绕实际原生调用。
                var queryStart = Stopwatch.GetTimestamp();
                Marshal.ThrowExceptionForHR(((IContextMenu)native.Context!).QueryContextMenu(native.Menu, 0, 1, 0x7fff,
                    0x400u | (paths.Length == 1 ? 0x10u : 0u) | (shift ? 0x100u : 0u)));
                native.BuildTicks = Stopwatch.GetTimestamp() - queryStart;
                native.BuildLatency = watch.ElapsedMilliseconds - native.ObjectLatency;
                native.Completed = Stopwatch.GetTimestamp();
                return native;
            }
            catch { native.Dispose(); throw; }
        }
    }

    private static ShellObservedPath ObservePath(string path)
    {
        try { _ = File.GetAttributes(path); return new(path, ShellPathPresence.Present); }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) { return new(path, ShellPathPresence.Missing); }
        catch (Exception) { return new(path, ShellPathPresence.Unavailable); }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetMenuString(IntPtr menu, uint command, StringBuilder text, int maximum, uint flags);

    private sealed class PreparedMenu : IDisposable
    {
        private readonly string[] paths;
        private readonly bool shift;
        private readonly uint clipboard;
        private readonly int generation;
        private readonly FileStamp[] stamps;
        internal readonly IntPtr[] Pidls;
        internal IShellFolder? Folder;
        internal object? Context;
        internal IntPtr Menu;
        internal long Completed, ObjectLatency, BuildLatency, BuildTicks;

        internal PreparedMenu(string[] paths, bool shift)
        {
            this.paths = paths; this.shift = shift;
            generation = Volatile.Read(ref invalidationGeneration);
            clipboard = GetClipboardSequenceNumber();
            stamps = paths.Select(FileStamp.Read).ToArray();
            Pidls = new IntPtr[paths.Length];
        }

        internal bool Matches(string[] current, bool extended)
        {
            if (generation != Volatile.Read(ref invalidationGeneration)
                || extended != shift || !paths.SequenceEqual(current, StringComparer.OrdinalIgnoreCase)
                || clipboard != GetClipboardSequenceNumber() || Stopwatch.GetElapsedTime(Completed).TotalSeconds > 5) return false;
            try { return stamps.SequenceEqual(current.Select(FileStamp.Read)); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
        }

        public void Dispose()
        {
            if (Menu != IntPtr.Zero) { DestroyMenu(Menu); Menu = IntPtr.Zero; }
            if (Context != null) { Marshal.ReleaseComObject(Context); Context = null; }
            if (Folder != null) { Marshal.ReleaseComObject(Folder); Folder = null; }
            for (var i = 0; i < Pidls.Length; i++)
                if (Pidls[i] != IntPtr.Zero) { Marshal.FreeCoTaskMem(Pidls[i]); Pidls[i] = IntPtr.Zero; }
        }
    }

    private sealed record FileStamp(long Created, long Modified, long Length, FileAttributes Attributes)
    {
        internal static FileStamp Read(string path)
        {
            var attributes = File.GetAttributes(path);
            var info = new FileInfo(path);
            return new(info.CreationTimeUtc.Ticks, info.LastWriteTimeUtc.Ticks,
                (attributes & FileAttributes.Directory) != 0 ? 0 : info.Length, attributes);
        }
    }

    private static IntPtr FindMenu(IntPtr owner)
    {
        if (owner == IntPtr.Zero) return IntPtr.Zero;
        var thread = GetWindowThreadProcessId(owner, out _);
        var result = IntPtr.Zero;
        EnumWindows((hwnd, parameter) =>
        {
            if (GetWindowThreadProcessId(hwnd, out _) != thread || !WindowsDesktop.IsWindowVisible(hwnd)) return true;
            var name = new StringBuilder(64); GetClassName(hwnd, name, name.Capacity);
            if (name.ToString() == "#32768") result = hwnd;
            return true;
        }, IntPtr.Zero);
        return result;
    }

    [ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolder
    {
        void ParseDisplayName(); void EnumObjects(); void BindToObject(); void BindToStorage(); void CompareIDs(); void CreateViewObject(); void GetAttributesOf();
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
    private delegate bool WindowCallback(IntPtr window, IntPtr parameter);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string name, IntPtr bind, out IntPtr pidl, uint mask, out uint attributes);
    [DllImport("shell32.dll")] private static extern int SHBindToParent(IntPtr pidl, ref Guid iid, out IShellFolder folder, out IntPtr child);
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint flags);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wp, IntPtr lp);
    [DllImport("user32.dll")] private static extern bool EndMenu();
    [DllImport("user32.dll")] private static extern uint TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr owner, IntPtr parameters);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, IntPtr parameter);
}
