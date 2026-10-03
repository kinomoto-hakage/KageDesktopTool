using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Kage.Desktop;

internal static class ShellIcons
{
    private readonly record struct IconKey(string Path, long Modified, long Length, FileAttributes Attributes, bool Small, int Pixels, bool HideArrow);
    private sealed record CachedIcon(ImageSource Image, long Loaded);
    private static readonly ConcurrentDictionary<IconKey, CachedIcon> Cache = new();
    internal static void Invalidate() => Cache.Clear();
    internal static ImageSource? ForFile(string path, bool small, int physicalSize = 0, bool hideShortcutArrow = true)
    {
        IconKey? key = null;
        try
        {
            var info = new FileInfo(path);
            key = new(path.ToUpperInvariant(), info.LastWriteTimeUtc.Ticks, info.Exists ? info.Length : 0, File.GetAttributes(path), small, physicalSize, hideShortcutArrow);
            if (Cache.TryGetValue(key.Value, out var cached) && Stopwatch.GetElapsedTime(cached.Loaded).TotalSeconds < 30) return cached.Image;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        // 缓存之外在后台初始化 COM；改动、系统偏好变化及短期过期会重新读实际资源。
        var initialized = CoInitializeEx(IntPtr.Zero, 0) >= 0;
        try
        {
            var image = Read(path, small, physicalSize, hideShortcutArrow);
            if (image != null && key != null)
            {
                if (Cache.Count >= 512) Cache.Clear();
                Cache[key.Value] = new(image, Stopwatch.GetTimestamp());
            }
            return image;
        }
        finally { if (initialized) CoUninitialize(); }
    }

    private static ImageSource? Read(string path, bool small, int physicalSize, bool hideShortcutArrow)
    {
        BitmapSource? resource = null;
        if (hideShortcutArrow)
        {
            try { resource = ShellIconResources.Read(path, physicalSize > 0 ? physicalSize : small ? 16 : 32); }
            catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException or COMException or System.NotSupportedException) { }
        }
        // 使用实际路径，让 Windows 解析文件关联、快捷方式覆盖和自定义文件夹图标。
        SHGetFileInfo(path, 0, out var info, (uint)Marshal.SizeOf<SHFILEINFO>(), 0x100 | 0x20 | 0x40 | 0x4000 | (small ? 1u : 0u));
        if (info.Icon == IntPtr.Zero) return resource;
        try
        {
            var image = resource ?? Imaging.CreateBitmapSourceFromHIcon(info.Icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            var shortcut = hideShortcutArrow && path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase);
            if (image.PixelWidth < physicalSize || shortcut || resource != null)
            {
                // 从 Shell 系统图像列表取足够大的原生图像，保留实际路径的关联及覆盖。
                var iid = new Guid("46EB5926-582E-4017-9FDF-E8998DAA0950");
                foreach (var kind in new[] { 1, 0, 2, 4 })
                {
                    if (SHGetImageList(kind, ref iid, out var list) < 0) continue;
                    try
                    {
                        if (!ImageList_GetIconSize(list, out var width, out _) || width < physicalSize) continue;
                        var overlayIndex = (info.IconIndex >> 24) & 0xff;
                        if (shortcut && IsShortcutOverlay(list, overlayIndex)) overlayIndex = 0;
                        if (resource != null && overlayIndex == 0) return resource;
                        var index = resource == null ? info.IconIndex & 0x00ffffff : OverlayImageIndex(list, overlayIndex);
                        if (index < 0) return resource;
                        var overlay = (uint)overlayIndex << 8;
                        var icon = ImageList_GetIcon(list, index, 1 | (resource != null ? 0u : overlay));
                        if (icon == IntPtr.Zero) continue;
                        try
                        {
                            var native = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                            if (resource == null) image = native;
                            else
                            {
                                // 资源修复也保留 Shell 提供的非箭头状态覆盖。
                                var visual = new DrawingVisual();
                                using (var drawing = visual.RenderOpen())
                                {
                                    var bounds = new Rect(0, 0, resource.PixelWidth, resource.PixelHeight);
                                    drawing.DrawImage(resource, bounds); drawing.DrawImage(native, bounds);
                                }
                                var combined = new RenderTargetBitmap(resource.PixelWidth, resource.PixelHeight, 96, 96, PixelFormats.Pbgra32);
                                combined.Render(visual); image = combined;
                            }
                        }
                        finally { DestroyIcon(icon); }
                        break;
                    }
                    finally { Marshal.Release(list); }
                }
            }
            image.Freeze(); return image;
        }
        finally { DestroyIcon(info.Icon); }
    }

    private static bool IsShortcutOverlay(IntPtr list, int overlay)
    {
        if (overlay == 0) return false;
        var stock = new StockInfo { Size = (uint)Marshal.SizeOf<StockInfo>() };
        if (SHGetStockIconInfo(29, 0x4000, ref stock) < 0) return false;
        // IImageList::GetOverlayImage 位于 IUnknown 后第 29 个槽；只移除系统快捷方式覆盖。
        return OverlayImageIndex(list, overlay) == stock.SystemIndex;
    }

    private static int OverlayImageIndex(IntPtr list, int overlay)
    {
        var table = Marshal.ReadIntPtr(list);
        var getOverlay = Marshal.GetDelegateForFunctionPointer<GetOverlay>(Marshal.ReadIntPtr(table, 31 * IntPtr.Size));
        return getOverlay(list, overlay, out var index) >= 0 ? index : -1;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetOverlay(IntPtr self, int overlay, out int index);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StockInfo
    {
        internal uint Size;
        internal IntPtr Icon;
        internal int SystemIndex, IconIndex;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] internal string Path;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        internal IntPtr Icon;
        internal int IconIndex;
        internal uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] internal string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] internal string TypeName;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SHGetFileInfo(string path, uint attributes, out SHFILEINFO info, uint size, uint flags);
    [DllImport("shell32.dll")] private static extern int SHGetStockIconInfo(int id, uint flags, ref StockInfo info);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("shell32.dll")] private static extern int SHGetImageList(int kind, ref Guid iid, out IntPtr list);
    [DllImport("comctl32.dll")] private static extern bool ImageList_GetIconSize(IntPtr list, out int width, out int height);
    [DllImport("comctl32.dll")] private static extern IntPtr ImageList_GetIcon(IntPtr list, int index, uint flags);
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint flags);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
}
