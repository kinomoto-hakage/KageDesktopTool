using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Kage.Workspace;

namespace Kage.Desktop;

// 文件移动仍由业务层完成；只用公开 IFolderView 接口安排本批成功移出的桌面项目。
internal static class DesktopIconPlacement
{
    internal static async Task<string?> PlaceAsync(BatchMoveResult result, WindowsDesktop.POINT screenPoint)
    {
        var paths = result.Items.Where(item => item.Outcome == Outcome.Success && item.ActualPath != null)
            .Select(item => item.ActualPath!).ToArray();
        if (paths.Length == 0) return null;
        object? windows = null, desktop = null, browser = null, view = null, folder = null;
        var pidls = new List<IntPtr>();
        try
        {
            windows = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"))!);
            object location = 0, reserved = null!;
            desktop = ((dynamic)windows!).FindWindowSW(ref location, ref reserved, 8, out int _, 1);
            if (desktop == null) throw new IOException("当前 Windows 桌面视图不可用。");
            var service = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837");
            var browserId = typeof(IShellBrowser).GUID;
            Marshal.ThrowExceptionForHR(((IServiceProvider)desktop!).QueryService(ref service, ref browserId, out browser));
            Marshal.ThrowExceptionForHR(((IShellBrowser)browser).QueryActiveShellView(out view));
            var desktopView = (IFolderView)view;
            var autoArrange = desktopView.GetAutoArrange();
            Marshal.ThrowExceptionForHR(autoArrange);
            if (autoArrange == 0)
                return "文件已移出；Windows 已启用桌面图标自动排列，关闭自动排列后再拖放可保留鼠标落点。";
            // 主动交付目录变更，避免快速移入再移出时旧的视图更新覆盖新位置。
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            await Task.Run(() => SHChangeNotify(0x1000, 0x1000 | 5, desktopPath, IntPtr.Zero));
            var folderId = typeof(IShellFolder).GUID;
            Marshal.ThrowExceptionForHR(desktopView.GetFolder(ref folderId, out folder));
            foreach (var path in paths)
            {
                uint eaten = 0, attributes = 0;
                Marshal.ThrowExceptionForHR(((IShellFolder)folder).ParseDisplayName(IntPtr.Zero, IntPtr.Zero, Path.GetFileName(path), ref eaten, out var pidl, ref attributes));
                pidls.Add(pidl);
            }
            // 文件系统通知与 Shell 视图异步更新；等待真实项目出现，不再次移动文件。
            var ready = false;
            for (var attempt = 0; attempt < 30; attempt++)
            {
                ready = pidls.All(pidl => desktopView.GetItemPosition(pidl, out _) >= 0);
                if (ready) break;
                await Task.Delay(100);
            }
            if (!ready) throw new IOException("桌面视图尚未显示本批项目。");
            Marshal.ThrowExceptionForHR(desktopView.GetSpacing(out var spacing));
            var area = WindowsDesktop.Displays().FirstOrDefault(display => screenPoint.X >= display.X && screenPoint.X < display.X + display.Width
                && screenPoint.Y >= display.Y && screenPoint.Y < display.Y + display.Height);
            var stepX = Math.Max(32, spacing.X); var stepY = Math.Max(32, spacing.Y);
            var x = screenPoint.X - stepX / 2; var y = screenPoint.Y - stepY / 2;
            if (area == null) throw new IOException("鼠标落点所在显示器已不可用。");
            // 从落点附近的网格开始，边缘向屏内展开，避免多选在右下角排到屏幕之外。
            var positions = Enumerable.Range(0, Math.Max(1, area.Width / stepX))
                .SelectMany(column => Enumerable.Range(0, Math.Max(1, area.Height / stepY))
                    .Select(row => new WindowsDesktop.POINT { X = area.X + column * stepX, Y = area.Y + row * stepY }))
                .OrderBy(point => Math.Pow(point.X - x, 2) + Math.Pow(point.Y - y, 2)).Take(pidls.Count).ToArray();
            // TRANSLATEPT 接收物理屏幕坐标，支持桌面原点偏移；不抢前台焦点。
            Marshal.ThrowExceptionForHR(desktopView.SelectAndPositionItems((uint)positions.Length, pidls.Take(positions.Length).ToArray(), positions, 0x80 | 0x20 | 0x1 | 0x4 | 0x40000000));
            return positions.Length == pidls.Count ? null : "文件已移出；落点屏幕的网格数量不足，其余图标由 Windows 安排。";
        }
        catch (Exception e) when (e is COMException or InvalidCastException or IOException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        { return "文件已移出，但桌面图标位置未能设置：" + e.Message; }
        finally
        {
            foreach (var pidl in pidls) Marshal.FreeCoTaskMem(pidl);
            foreach (var instance in new[] { folder, view, browser, desktop, windows })
                if (instance != null && Marshal.IsComObject(instance)) Marshal.ReleaseComObject(instance);
        }
    }

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    { [PreserveSig] int QueryService(ref Guid service, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object value); }

    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        void GetWindow(); void ContextSensitiveHelp(); void InsertMenusSB(); void SetMenuSB(); void RemoveMenusSB();
        void SetStatusTextSB(); void EnableModelessSB(); void TranslateAcceleratorSB(); void BrowseObject();
        void GetViewStateStream(); void GetControlWindow(); void SendControlMsg();
        [PreserveSig] int QueryActiveShellView([MarshalAs(UnmanagedType.Interface)] out object view);
    }

    [ComImport, Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFolderView
    {
        void GetCurrentViewMode(); void SetCurrentViewMode();
        [PreserveSig] int GetFolder(ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object folder);
        void Item(); void ItemCount(); void Items(); void GetSelectionMarkedItem(); void GetFocusedItem();
        [PreserveSig] int GetItemPosition(IntPtr pidl, out WindowsDesktop.POINT point);
        [PreserveSig] int GetSpacing(out WindowsDesktop.POINT point);
        void GetDefaultSpacing();
        [PreserveSig] int GetAutoArrange();
        void SelectItem();
        [PreserveSig] int SelectAndPositionItems(uint count, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] IntPtr[] pidls,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] WindowsDesktop.POINT[] points, uint flags);
    }

    [ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolder
    {
        [PreserveSig] int ParseDisplayName(IntPtr owner, IntPtr context, [MarshalAs(UnmanagedType.LPWStr)] string name,
            ref uint eaten, out IntPtr pidl, ref uint attributes);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(uint events, uint flags, string path, IntPtr other);
}
