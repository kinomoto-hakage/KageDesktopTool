using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Kage.Desktop;

internal static class ShellIcons
{
    internal static ImageSource? ForFile(string path, bool small, int physicalSize = 0, bool hideShortcutArrow = true)
    {
        // 后台线程显式初始化 COM；每次内容刷新重新读实际路径，避免保留失效覆盖图标。
        var initialized = CoInitializeEx(IntPtr.Zero, 0) >= 0;
        try { return Read(path, small, physicalSize, hideShortcutArrow); }
        finally { if (initialized) CoUninitialize(); }
    }

    private static ImageSource? Read(string path, bool small, int physicalSize, bool hideShortcutArrow)
    {
        // 使用实际路径，让 Windows 解析文件关联、快捷方式覆盖和自定义文件夹图标。
        SHGetFileInfo(path, 0, out var info, (uint)Marshal.SizeOf<SHFILEINFO>(), 0x100 | 0x20 | 0x40 | 0x4000 | (small ? 1u : 0u));
        if (info.Icon == IntPtr.Zero) return null;
        try
        {
            var image = Imaging.CreateBitmapSourceFromHIcon(info.Icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            var shortcut = hideShortcutArrow && path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase);
            if (image.PixelWidth < physicalSize || shortcut)
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
                        var overlay = (uint)overlayIndex << 8;
                        var icon = ImageList_GetIcon(list, info.IconIndex & 0x00ffffff, 1 | overlay);
                        if (icon == IntPtr.Zero) continue;
                        try { image = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); }
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
        var table = Marshal.ReadIntPtr(list);
        var getOverlay = Marshal.GetDelegateForFunctionPointer<GetOverlay>(Marshal.ReadIntPtr(table, 31 * IntPtr.Size));
        return getOverlay(list, overlay, out var index) >= 0 && index == stock.SystemIndex;
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
