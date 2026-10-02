using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Kage.DesktopFolderPrototype;

internal static class ShellIcons
{
    private static readonly Dictionary<(string, bool), ImageSource?> Cache = new();

    internal static void Invalidate(string path) { Cache.Remove((path, true)); Cache.Remove((path, false)); }

    internal static ImageSource? ForFile(string path, bool small)
    {
        if (Cache.TryGetValue((path, small), out var cached)) return cached;
        // 使用实际路径，让 Windows 解析文件关联、快捷方式覆盖和自定义文件夹图标。
        SHGetFileInfo(path, 0, out var info, (uint)Marshal.SizeOf<SHFILEINFO>(), 0x100 | 0x20 | (small ? 1u : 0u));
        if (info.Icon == IntPtr.Zero) return null;
        try
        {
            var image = Imaging.CreateBitmapSourceFromHIcon(info.Icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            image.Freeze(); Cache[(path, small)] = image; return image;
        }
        finally { DestroyIcon(info.Icon); }
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
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
}
