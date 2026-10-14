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

    internal bool Attach(IntPtr foreground)
    {
        var style = WindowsDesktop.GetWindowLongPtr(foreground, -16).ToInt64();
        WindowsDesktop.SetWindowLongPtr(foreground, -16, new IntPtr((style & ~0x80000000L) | 0x40000000L));
        WindowsDesktop.SetParent(foreground, Handle);
        return WindowsDesktop.Attached(foreground, Handle);
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
        => message == 0x21 ? new IntPtr(3) : CallWindowProc(previousProcedure, hwnd, message, wp, lp);

    public void Dispose()
    {
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
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
