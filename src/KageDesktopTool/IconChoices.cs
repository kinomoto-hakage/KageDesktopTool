using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace Kage.Desktop;

internal static class IconChoices
{
    internal static readonly (string Key, string Name)[] Options =
        [("a", "A · 收纳夹"), ("b", "B · 分区格"), ("c", "C · 叠层抽屉"), ("d", "D · K 文件夹")];

    internal static Icon Tray(string key)
    {
        var size = WindowsDesktop.TrayIconSize();
        var handle = LoadImage(IntPtr.Zero, Path.Combine(AppContext.BaseDirectory, "Assets", $"tray-{key}.ico"), 1, size.Width, size.Height, 0x10);
        if (handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            using var loaded = Icon.FromHandle(handle);
            // Clone 拥有独立 HICON，原生加载的句柄在此释放，调用方按原生命周期 Dispose。
            return (Icon)loaded.Clone();
        }
        finally { DestroyIcon(handle); }
    }

    internal static BitmapImage Image(string key)
    {
        var image = new BitmapImage(new Uri($"pack://application:,,,/Assets/app-{key}.png"));
        image.Freeze();
        return image;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr LoadImage(IntPtr instance, string name, uint type, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
}
