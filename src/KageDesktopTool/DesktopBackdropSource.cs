using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Graphics.Canvas;
using Windows.Foundation;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Security.Authorization.AppCapabilityAccess;
using WinRT;

namespace Kage.Desktop;

// 同一桌面只捕获一次。Folder 的 owned 顶层窗口及其清晰子窗口不属于
// Explorer 的内容树，因此无需排除普通截图，也不会递归采入自身。
internal sealed class DesktopBackdropSource : IDisposable
{
    private static readonly Dictionary<IntPtr, DesktopBackdropSource> sources = new();
    private static Task<AppCapabilityAccessStatus>? borderAccess;
    internal static event Action? ReadinessChanged;
    internal object Sync { get; } = new();
    internal CanvasDevice Canvas { get; private set; } = null!;
    internal bool Failed => failed;
    private volatile bool failed;
    internal IntPtr Root { get; }
    private IDirect3DDevice? device;
    private GraphicsCaptureItem? item;
    private Direct3D11CaptureFramePool? pool;
    private GraphicsCaptureSession? session;
    private Direct3D11CaptureFrame? frame;
    private CanvasBitmap? bitmap;
    private readonly HashSet<Action<CanvasBitmap>> renderers = new();
    private int references;
    private bool disposed;

    internal static DesktopBackdropSource Acquire(IntPtr root)
    {
        // 先按平台合同请求 Borderless。完整信任的桌面应用通常直接返回；
        // 未完成或被拒时保留原着色回退，Runtime 的环境轮询会重试。
        if (borderAccess == null)
        {
            borderAccess = RequestBorderlessAsync();
            if (!borderAccess.IsCompleted)
                _ = borderAccess.ContinueWith(completed =>
                {
                    if (completed.IsCompletedSuccessfully && completed.Result == AppCapabilityAccessStatus.Allowed)
                        ReadinessChanged?.Invoke();
                }, TaskScheduler.Default);
        }
        if (!borderAccess.IsCompleted)
            throw new InvalidOperationException("当前环境尚未允许无边框桌面背景。");
        if (!borderAccess.IsCompletedSuccessfully || borderAccess.Result != AppCapabilityAccessStatus.Allowed)
        {
            borderAccess = null;
            throw new InvalidOperationException("当前环境尚未允许无边框桌面背景。");
        }
        if (!sources.TryGetValue(root, out var source))
        {
            source = new DesktopBackdropSource(root);
            sources.Add(root, source);
        }
        if (source.Failed) throw new InvalidOperationException("桌面背景来源正在恢复。");
        source.references++;
        return source;
    }

    private DesktopBackdropSource(IntPtr root)
    {
        Root = root;
        try
        {
            Marshal.ThrowExceptionForHR(D3D11CreateDevice(IntPtr.Zero, 1, IntPtr.Zero, 0x20, IntPtr.Zero, 0, 7,
                out var native, out _, out var context));
            try
            {
                var iid = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
                Marshal.ThrowExceptionForHR(Marshal.QueryInterface(native, in iid, out var dxgi));
                try
                {
                    Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out var pointer));
                    try { device = MarshalInterface<IDirect3DDevice>.FromAbi(pointer); }
                    finally { Marshal.Release(pointer); }
                }
                finally { Marshal.Release(dxgi); }
            }
            finally { Marshal.Release(context); Marshal.Release(native); }
            Canvas = CanvasDevice.CreateFromDirect3D11Device(device);
            var factory = Factory("Windows.Graphics.Capture.GraphicsCaptureItem", new Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"));
            try
            {
                var interop = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factory);
                try
                {
                    var iid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
                    interop.CreateForWindow(root, in iid, out var pointer);
                    try { item = MarshalInterface<GraphicsCaptureItem>.FromAbi(pointer); }
                    finally { Marshal.Release(pointer); }
                }
                finally { Marshal.FinalReleaseComObject(interop); }
            }
            finally { Marshal.Release(factory); }
            item.Closed += Closed;
            pool = Direct3D11CaptureFramePool.CreateFreeThreaded(device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
            pool.FrameArrived += FrameArrived;
            session = pool.CreateCaptureSession(item);
            session.IsCursorCaptureEnabled = false;
            session.As<IGraphicsCaptureSession3>().SetBorderRequired(0);
            session.StartCapture();
        }
        catch { Dispose(); throw; }
    }

    internal void Register(Action<CanvasBitmap> renderer)
    {
        lock (Sync) { renderers.Add(renderer); if (bitmap != null) renderer(bitmap); }
    }

    internal void RenderCurrent(Action<CanvasBitmap> renderer)
    {
        lock (Sync) { if (bitmap != null && !Failed && !disposed) renderer(bitmap); }
    }

    internal void Release(Action<CanvasBitmap>? renderer)
    {
        lock (Sync) { if (renderer != null) renderers.Remove(renderer); }
        if (--references != 0) return;
        sources.Remove(Root);
        Dispose();
    }

    private void Closed(GraphicsCaptureItem sender, object args) { lock (Sync) failed = true; }

    private void FrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        lock (Sync)
        {
            if (disposed || Failed) return;
            try
            {
                var next = sender.TryGetNextFrame();
                if (next == null) return;
                if (item == null || next.ContentSize.Width != item.Size.Width || next.ContentSize.Height != item.Size.Height)
                { next.Dispose(); failed = true; return; }
                bitmap?.Dispose(); frame?.Dispose();
                frame = next;
                bitmap = CanvasBitmap.CreateFromDirect3D11Surface(Canvas, next.Surface);
                foreach (var renderer in renderers) renderer(bitmap);
            }
            catch (Exception) { failed = true; }
        }
    }

    public void Dispose()
    {
        lock (Sync) { if (disposed) return; disposed = true; renderers.Clear(); }
        if (item != null) item.Closed -= Closed;
        if (pool != null) pool.FrameArrived -= FrameArrived;
        session?.Dispose(); session = null;
        pool?.Dispose(); pool = null;
        lock (Sync) { bitmap?.Dispose(); bitmap = null; frame?.Dispose(); frame = null; }
        Canvas?.Dispose(); device?.Dispose(); device = null; item = null;
    }

    private static async Task<AppCapabilityAccessStatus> RequestBorderlessAsync()
    {
        var pointer = Factory("Windows.Graphics.Capture.GraphicsCaptureAccess", new Guid("743ED370-06EC-5040-A58A-901F0F757095"));
        try
        {
            var factory = (IGraphicsCaptureAccessStatics)Marshal.GetObjectForIUnknown(pointer);
            try
            {
                factory.RequestAccessAsync(0, out var operationPointer);
                try
                {
                    var operation = MarshalInterface<IAsyncOperation<AppCapabilityAccessStatus>>.FromAbi(operationPointer);
                    return await operation;
                }
                finally { Marshal.Release(operationPointer); }
            }
            finally { Marshal.FinalReleaseComObject(factory); }
        }
        finally { Marshal.Release(pointer); }
    }

    private static IntPtr Factory(string className, Guid iid)
    {
        Marshal.ThrowExceptionForHR(WindowsCreateString(className, className.Length, out var name));
        try { Marshal.ThrowExceptionForHR(RoGetActivationFactory(name, in iid, out var factory)); return factory; }
        finally { WindowsDeleteString(name); }
    }

    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    { void CreateForWindow(IntPtr window, in Guid iid, out IntPtr result); void CreateForMonitor(IntPtr monitor, in Guid iid, out IntPtr result); }
    [ComImport, Guid("f2cdd966-22ae-5ea1-9596-3a289344c3be"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureSession3
    { void GetIIds(out uint count, out IntPtr ids); void GetRuntimeClassName(out IntPtr name); void GetTrustLevel(out int level); void GetBorderRequired(out byte value); void SetBorderRequired(byte value); }
    [ComImport, Guid("743ED370-06EC-5040-A58A-901F0F757095"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureAccessStatics
    { void GetIIds(out uint count, out IntPtr ids); void GetRuntimeClassName(out IntPtr name); void GetTrustLevel(out int level); void RequestAccessAsync(int request, out IntPtr operation); }
    [DllImport("combase.dll", CharSet = CharSet.Unicode)] private static extern int WindowsCreateString(string value, int length, out IntPtr name);
    [DllImport("combase.dll")] private static extern int WindowsDeleteString(IntPtr name);
    [DllImport("combase.dll")] private static extern int RoGetActivationFactory(IntPtr name, in Guid iid, out IntPtr factory);
    [DllImport("d3d11.dll")] private static extern int D3D11CreateDevice(IntPtr adapter, int driver, IntPtr software, uint flags,
        IntPtr levels, uint count, uint sdk, out IntPtr device, out int level, out IntPtr context);
    [DllImport("d3d11.dll")] private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgi, out IntPtr device);
}
