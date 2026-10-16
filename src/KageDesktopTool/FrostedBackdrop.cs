using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using WinRT;

namespace Kage.Desktop;

// Explorer 的透明子窗口不能实时使用 HostBackdrop。独立背景由桌面拥有，
// 清晰的 WPF 前景仍作为子窗口；Composition 位于子窗口下方，不采样自身。
internal sealed class FrostedBackdrop : IDisposable
{
    internal IntPtr Handle { get; private set; }
    internal IntPtr DesktopHost { get; }
    private Compositor? compositor;
    private DesktopWindowTarget? target;
    private SpriteVisual? visual;
    private readonly WindowProcedure procedure;
    private IntPtr previousProcedure;
    private IntPtr foreground;
    private FolderHeader? foregroundWindow;
    private BackdropDropTarget? dropTarget;
    private bool oleInitialized;
    private static IntPtr queueController;

    internal static FrostedBackdrop? TryCreate(IntPtr desktopHost)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return null;
        try { return new FrostedBackdrop(desktopHost); }
        catch (Exception) { return null; }
    }

    private FrostedBackdrop(IntPtr desktopHost)
    {
        DesktopHost = desktopHost;
        procedure = WindowMessage;
        try
        {
            var owner = GetAncestor(desktopHost, 2);
            if (owner == IntPtr.Zero) throw new InvalidOperationException("桌面宿主不可用。");
            compositor = CreateCompositor();
            Handle = CreateWindowEx(0x08000080, "STATIC", "Kage 毛玻璃背景", 0x80000000,
                0, 0, 1, 1, owner, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (Handle == IntPtr.Zero) throw new InvalidOperationException("无法建立背景窗口。");
            previousProcedure = WindowsDesktop.SetWindowLongPtr(Handle, -4, Marshal.GetFunctionPointerForDelegate(procedure));
            var enabled = 1;
            Marshal.ThrowExceptionForHR(DwmSetWindowAttribute(Handle, 17, ref enabled, 4));
            compositor.As<ICompositorDesktopInterop>().CreateDesktopWindowTarget(Handle, false, out var pointer);
            try { target = MarshalInterface<DesktopWindowTarget>.FromAbi(pointer); }
            finally { Marshal.Release(pointer); }
            visual = compositor.CreateSpriteVisual();
            visual.Brush = compositor.CreateHostBackdropBrush();
            target.Root = visual;
        }
        catch { Dispose(); throw; }
    }

    private static Compositor CreateCompositor()
    {
        try { return new Compositor(); }
        catch (UnauthorizedAccessException) when (queueController == IntPtr.Zero)
        {
            var options = new QueueOptions { Size = Marshal.SizeOf<QueueOptions>(), ThreadType = 2, ApartmentType = 2 };
            Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(options, out queueController));
            Application.Current.Exit += (_, _) =>
            {
                if (queueController != IntPtr.Zero) { Marshal.Release(queueController); queueController = IntPtr.Zero; }
            };
            return new Compositor();
        }
    }

    internal bool Attach(FolderHeader window, IntPtr foreground, Guid folderId)
    {
        var style = WindowsDesktop.GetWindowLongPtr(foreground, -16).ToInt64();
        WindowsDesktop.SetWindowLongPtr(foreground, -16, new IntPtr((style & ~0x80000000L) | 0x40000000L));
        WindowsDesktop.SetParent(foreground, Handle);
        if (!WindowsDesktop.Attached(foreground, Handle)) return false;
        this.foreground = foreground;
        foregroundWindow = window;
        var initialized = OleInitialize(IntPtr.Zero);
        if (initialized < 0) return false;
        oleInitialized = true;
        dropTarget = new BackdropDropTarget(folderId);
        return RegisterDragDrop(Handle, dropTarget) >= 0;
    }

    internal bool Position(int x, int y, int width, int height, double scale)
    {
        if (!WindowsDesktop.IsWindow(Handle) || visual == null) return false;
        visual.Size = new Vector2(width, height);
        var diameter = Math.Max(1, (int)Math.Round(20 * scale));
        var region = CreateRoundRectRgn(0, 0, width + 1, height + 1, diameter, diameter);
        if (region == IntPtr.Zero) return false;
        // 成功时窗口取得 HRGN 所有权；失败时由本适配器释放。
        if (SetWindowRgn(Handle, region, true) == 0) { DeleteObject(region); return false; }
        return WindowsDesktop.SetWindowPos(Handle, new IntPtr(1), x, y, width, height, 0x10 | 0x40);
    }

    internal void Hide() { if (Handle != IntPtr.Zero) ShowWindow(Handle, 0); }

    private IntPtr WindowMessage(IntPtr hwnd, uint message, IntPtr wp, IntPtr lp)
    {
        if (message == 0x21) return new IntPtr(3);
        if (message == 0x84) return new IntPtr(1);
        if (message is 0x215 or 0x1F) foregroundWindow?.BackdropCaptureLost();
        // layered 前景在 alpha=0 的空白处让原生背景命中。将实际输入送回同一
        // WPF 输入入口，不能用额外颜色 alpha 换取命中，否则改变完全透明语义。
        if (WindowsDesktop.IsWindow(foreground) && (message is >= 0x200 and <= 0x20E || message == 0x7B))
        {
            foregroundWindow?.BackdropInput(message, wp, hwnd);
            if (message == 0x200 && foregroundWindow != null && System.Windows.Input.Mouse.Captured == foregroundWindow.NameDisplay)
                SendMessage(foreground, message, wp, lp);
            return IntPtr.Zero;
        }
        return CallWindowProc(previousProcedure, hwnd, message, wp, lp);
    }

    public void Dispose()
    {
        foregroundWindow?.BackdropCaptureLost();
        if (dropTarget != null && Handle != IntPtr.Zero) RevokeDragDrop(Handle);
        dropTarget = null;
        if (oleInitialized) { OleUninitialize(); oleInitialized = false; }
        if (target != null) { target.Root = null; target.Dispose(); target = null; }
        visual?.Brush?.Dispose();
        visual?.Dispose(); visual = null;
        compositor?.Dispose(); compositor = null;
        if (Handle != IntPtr.Zero) { DestroyWindow(Handle); Handle = IntPtr.Zero; }
        GC.KeepAlive(procedure);
    }

    [ComImport, Guid("29E691FA-4567-4DCA-B319-D0F207EB6807"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICompositorDesktopInterop
    {
        void CreateDesktopWindowTarget(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool topmost, out IntPtr target);
        void EnsureOnThread(int threadId);
    }
    [StructLayout(LayoutKind.Sequential)] private struct QueueOptions { internal int Size, ThreadType, ApartmentType; }
    private delegate IntPtr WindowProcedure(IntPtr hwnd, uint message, IntPtr wp, IntPtr lp);
    [DllImport("coremessaging.dll")] private static extern int CreateDispatcherQueueController(QueueOptions options, out IntPtr controller);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowEx(uint extendedStyle, string className,
        string title, uint style, int x, int y, int width, int height, IntPtr owner, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CallWindowProc(IntPtr procedure, IntPtr hwnd, uint message, IntPtr wp, IntPtr lp);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wp, IntPtr lp);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [DllImport("ole32.dll")] private static extern int RegisterDragDrop(IntPtr hwnd, IBackdropDropTarget target);
    [DllImport("ole32.dll")] private static extern int RevokeDragDrop(IntPtr hwnd);
    [DllImport("ole32.dll")] private static extern int OleInitialize(IntPtr reserved);
    [DllImport("ole32.dll")] private static extern void OleUninitialize();

    [ComVisible(true), Guid("00000122-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IBackdropDropTarget
    {
        [PreserveSig] int DragEnter([MarshalAs(UnmanagedType.Interface)] System.Runtime.InteropServices.ComTypes.IDataObject data, uint keys, long point, ref uint effect);
        [PreserveSig] int DragOver(uint keys, long point, ref uint effect);
        [PreserveSig] int DragLeave();
        [PreserveSig] int Drop([MarshalAs(UnmanagedType.Interface)] System.Runtime.InteropServices.ComTypes.IDataObject data, uint keys, long point, ref uint effect);
    }

    // 只接收真实 OLE 的 FileDrop，移动仍由原 FileDrag/Workspace 路径执行。
    // WPF 有色像素继续由 WPF 自己接收，空白像素由此背景 HWND 接收。
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class BackdropDropTarget(Guid folderId) : IBackdropDropTarget
    {
        private bool files;
        private static System.Runtime.InteropServices.ComTypes.FORMATETC Format => new()
        { cfFormat = 15, dwAspect = System.Runtime.InteropServices.ComTypes.DVASPECT.DVASPECT_CONTENT,
            lindex = -1, tymed = System.Runtime.InteropServices.ComTypes.TYMED.TYMED_HGLOBAL };

        public int DragEnter(System.Runtime.InteropServices.ComTypes.IDataObject data, uint keys, long point, ref uint effect)
        { var format = Format; files = data.QueryGetData(ref format) == 0; return DragOver(keys, point, ref effect); }
        public int DragOver(uint keys, long point, ref uint effect)
        { effect = files && FileDrag.CanReceive ? effect & 2 : 0; return 0; }
        public int DragLeave() { files = false; return 0; }
        public int Drop(System.Runtime.InteropServices.ComTypes.IDataObject data, uint keys, long point, ref uint effect)
        {
            effect = 0;
            try
            {
                if (!FileDrag.CanReceive) return 0;
                var format = Format;
                data.GetData(ref format, out var medium);
                try
                {
                    var count = DragQueryFile(medium.unionmember, uint.MaxValue, null, 0);
                    var paths = new string[count];
                    for (uint i = 0; i < count; i++)
                    {
                        var text = new System.Text.StringBuilder((int)DragQueryFile(medium.unionmember, i, null, 0) + 1);
                        DragQueryFile(medium.unionmember, i, text, (uint)text.Capacity); paths[i] = text.ToString();
                    }
                    Application.Current.Dispatcher.BeginInvoke(async () => await FileDrag.ReceiveAsync(folderId, paths));
                }
                finally { ReleaseStgMedium(ref medium); }
            }
            catch (COMException) { }
            finally { files = false; }
            return 0;
        }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern uint DragQueryFile(IntPtr drop, uint index, System.Text.StringBuilder? path, uint length);
        [DllImport("ole32.dll")] private static extern void ReleaseStgMedium(ref System.Runtime.InteropServices.ComTypes.STGMEDIUM medium);
    }
}
