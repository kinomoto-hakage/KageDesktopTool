using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Kage.Desktop;

// 包应用快捷方式解析真实 PIDL；InternetShortcut 尊重其显式图标资源。
internal static class ShellIconResources
{
    internal static BitmapSource? Read(string path, int pixels)
    {
        if (path.EndsWith(".url", StringComparison.OrdinalIgnoreCase))
        {
            var values = File.ReadAllLines(path).Select(line => line.Split('=', 2)).Where(parts => parts.Length == 2)
                .GroupBy(parts => parts[0].Trim(), StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.Last()[1].Trim(), StringComparer.OrdinalIgnoreCase);
            if (values.TryGetValue("IconFile", out var iconPath))
            {
                _ = int.TryParse(values.GetValueOrDefault("IconIndex"), out var index);
                return ReadResource(Environment.ExpandEnvironmentVariables(iconPath.Trim('"')), index, pixels);
            }
            return null;
        }
        if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return null;
        var instance = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"))!)!;
        var pidl = IntPtr.Zero;
        try
        {
            ((IPersistFile)instance).Load(path, 0);
            var link = (IShellLink)instance;
            var location = new StringBuilder(1024);
            Marshal.ThrowExceptionForHR(link.GetIconLocation(location, location.Capacity, out var index));
            if (location.Length != 0 && ReadResource(Environment.ExpandEnvironmentVariables(location.ToString()), index, pixels) is { } custom) return custom;
            Marshal.ThrowExceptionForHR(link.GetIDList(out pidl));
            if (pidl == IntPtr.Zero) return null;
            if (PackageIcons.Read(pidl, pixels) is { } package) return package;
            var iid = typeof(IShellItemImageFactory).GUID;
            if (SHCreateItemFromIDList(pidl, ref iid, out var factory) < 0) return null;
            try
            {
                // ICONONLY 排除缩略图；BIGGER/SCALEUP 请求实际像素，避免 jumbo 格中只有小图案。
                if (factory.GetImage(new NativeSize { Width = pixels, Height = pixels }, 0x4 | 0x1 | 0x100, out var bitmap) < 0 || bitmap == IntPtr.Zero) return null;
                try
                {
                    var image = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    image.Freeze(); return image;
                }
                finally { DeleteObject(bitmap); }
            }
            finally { Marshal.ReleaseComObject(factory); }
        }
        finally
        {
            if (pidl != IntPtr.Zero) Marshal.FreeCoTaskMem(pidl);
            Marshal.ReleaseComObject(instance);
        }
    }

    private static BitmapSource? ReadResource(string path, int index, int pixels)
    {
        if (!File.Exists(path)) return null;
        if (path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
        {
            using var file = File.OpenRead(path);
            var frames = BitmapDecoder.Create(file, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames;
            var frame = frames.Where(image => image.PixelWidth >= pixels).OrderBy(image => image.PixelWidth).FirstOrDefault()
                ?? frames.OrderByDescending(image => image.PixelWidth).FirstOrDefault();
            frame?.Freeze(); return frame;
        }
        var count = PrivateExtractIcons(path, index, pixels, pixels, out var icon, out _, 1, 0);
        if (count == 0 || count == uint.MaxValue || icon == IntPtr.Zero) return null;
        try
        {
            var image = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            image.Freeze(); return image;
        }
        finally { DestroyIcon(icon); }
    }
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLink
    {
        void GetPath(); [PreserveSig] int GetIDList(out IntPtr pidl); void SetIDList(); void GetDescription(); void SetDescription();
        void GetWorkingDirectory(); void SetWorkingDirectory(); void GetArguments(); void SetArguments(); void GetHotkey(); void SetHotkey(); void GetShowCmd(); void SetShowCmd();
        [PreserveSig] int GetIconLocation([MarshalAs(UnmanagedType.LPWStr)] StringBuilder location, int count, out int index);
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeSize { internal int Width, Height; }
    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory { [PreserveSig] int GetImage(NativeSize size, uint flags, out IntPtr bitmap); }
    [DllImport("shell32.dll")] private static extern int SHCreateItemFromIDList(IntPtr pidl, ref Guid iid, out IShellItemImageFactory factory);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr bitmap);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint PrivateExtractIcons(string file, int index, int width, int height, out IntPtr icon, out uint id, uint count, uint flags);
}
