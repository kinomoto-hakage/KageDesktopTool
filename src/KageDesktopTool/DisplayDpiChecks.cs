using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kage.Workspace;

namespace Kage.Desktop;

// 使用实际可用屏幕；额外 DPI 注入只验证适配器换算，不冒充系统配置验收。
internal static class DisplayDpiChecks
{
    internal static int Run()
    {
        var fixture = Path.Combine(Path.GetTempPath(), "Kage-display-dpi-" + Guid.NewGuid().ToString("N"));
        var evidence = Path.Combine(Environment.CurrentDirectory, ".scratch", "desktop-folder", "verification");
        Directory.CreateDirectory(evidence);
        Directory.CreateDirectory(fixture);
        var log = Path.Combine(evidence, "display-dpi-session.txt");
        File.WriteAllText(log, "12 多显示器与 DPI 检查\n");
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Runtime? runtime = null;
        var result = 1;
        var desktopShown = false;
        GetCursorPos(out var originalCursor);
        app.Startup += async (_, _) =>
        {
            try
            {
                var displays = WindowsDesktop.Displays();
                Check(displays.Length > 0, "重新枚举实际显示区域");
                foreach (var display in displays) File.AppendAllText(log, $"环境：{display}\n");
                var records = displays.Select((display, index) => new FolderRecord(Guid.NewGuid(), $"实际屏幕 {index + 1}",
                    display.X + 20, display.Y + 20, Expanded: true)).ToArray();
                var root = Path.Combine(fixture, "内容");
                foreach (var record in records)
                {
                    var path = Path.Combine(root, record.Name);
                    Directory.CreateDirectory(path);
                    File.WriteAllText(Path.Combine(path, "短.txt"), "保留内容");
                    File.WriteAllText(Path.Combine(path, "长文件名称用于验证每屏字体及列表左侧固定.txt"), "保留内容");
                    Directory.CreateDirectory(Path.Combine(path, "子文件夹"));
                }
                var store = new JsonWorkspaceStore(Path.Combine(fixture, "状态"));
                store.Save(new WorkspaceState { Root = root, Folders = records });
                IDesktopWorkspace workspace = new DesktopWorkspace(store, new NoStartup());
                Check((await workspace.InitializeAsync(displays)).Succeeded, "隔离业务状态加载");
                runtime = new Runtime(workspace);
                Check(runtime.DesktopAvailable, "实际 Explorer 桌面挂接");
                WinD();
                desktopShown = true;
                await Task.Delay(700);
                foreach (var record in records)
                {
                    var header = runtime.Headers[record.Id];
                    await CheckContent(header, "实际环境");
                    await workspace.SetViewAsync(record.Id, false);
                    runtime.Render();
                    await CheckContent(header, "实际列表");
                    var first = (ListBoxItem)header.Contents.Items.Items[0];
                    var left = Image(first).PointToScreen(new Point()).X;
                    var edit = runtime.BeginInteraction(record.Id)!;
                    Check(edit.ResizeBy(60, 0), "每屏列表可调整宽度");
                    runtime.Preview(edit);
                    header.UpdateLayout();
                    Check(Math.Abs(Image(first).PointToScreen(new Point()).X - left) < .6, "加宽列表图标左侧保持同一物理位置");
                    await runtime.CommitInteractionAsync(edit);
                    var itemPoint = first.PointToScreen(new Point(first.ActualWidth / 2, first.ActualHeight / 2));
                    Check(Hit(itemPoint) == header.Handle, "列表项物理命中实际桌面窗口");
                    Click(itemPoint);
                    await WaitUntil(() => first.IsSelected);
                    Check(first.IsSelected, "实际鼠标选择正确内容项");
                    Screenshot(header, $"12-实际屏幕-{Array.IndexOf(records, record) + 1}.png");
                }
                var target = runtime.Headers[records[0].Id];
                // 不更改系统缩放或分辨率；以注入的显示度量单独核对宿主 DPI 补偿。
                foreach (var injectedScale in new[] { 1.25, 1.5, 2.0 })
                {
                    var synthetic = displays.Select(d => d with { Scale = injectedScale }).ToArray();
                    Check((await workspace.RefreshAsync(synthetic)).Succeeded, "注入显示度量恢复布局");
                    runtime.Render();
                    await CheckContent(target, $"注入 {injectedScale:P0}（系统配置未改变）");
                    var first = (ListBoxItem)target.Contents.Items.Items[0];
                    var point = first.PointToScreen(new Point(30, first.ActualHeight / 2));
                    Check(Hit(point) == target.Handle && target.InputHitTest(target.PointFromScreen(point)) != null,
                        "补偿后物理坐标与 WPF 内容命中一致");
                    await workspace.SetViewAsync(target.FolderId, true);
                    runtime.Render();
                    await CheckContent(target, "注入网格");
                    await workspace.SetViewAsync(target.FolderId, false);
                    runtime.Render();
                }
                await runtime.RefreshDisplayEnvironmentAsync();
                target.BeginHeaderDrag(new Point(0, 0));
                target.DragHeaderTo(new Point(20, 10));
                Check(runtime.Interacting, "显示通知前有活动拖动");
                var oldRow = target.Contents.Items.Items[0];
                var oldRevision = NativeViewMetrics.Revision;
                Check(WindowsDesktop.PostMessage(new WindowInteropHelper(runtime.Controller).Handle, 0x7E, new IntPtr(32), IntPtr.Zero), "向控制器投递显示变化通知");
                await Task.Delay(200);
                File.AppendAllText(log, $"通知：活动输入={runtime.Interacting}，度量版本={oldRevision}→{NativeViewMetrics.Revision}，内容重建={target.Contents.Items.Items[0] != oldRow}\n");
                await WaitUntil(() => !runtime.Interacting && target.Contents.Items.Items[0] != oldRow);
                Check(!runtime.Interacting, "显示变化通知取消旧输入并重测原生度量");
                await CheckContent(target, "通知恢复");
                var headerPoint = target.HeaderInput.PointToScreen(new Point(45, target.HeaderInput.ActualHeight / 2));
                var oldX = target.Record.X;
                Check(Hit(headerPoint) == target.Handle, "通知恢复后头部物理命中正确");
                SetCursorPos((int)headerPoint.X, (int)headerPoint.Y);
                mouse_event(2, 0, 0, 0, UIntPtr.Zero);
                await Task.Delay(80);
                SetCursorPos((int)headerPoint.X + 15, (int)headerPoint.Y + 10);
                await Task.Delay(100);
                mouse_event(4, 0, 0, 0, UIntPtr.Zero);
                await WaitUntil(() => !runtime.Interacting);
                Check(target.Record.X == oldX + 15, "通知恢复后真实鼠标拖动保存十五物理像素");
                var before = target.Record;
                target.BeginHeaderDrag(new Point(0, 0));
                target.DragHeaderTo(new Point(-10000, 0));
                var edge = target.Record.X;
                target.DragHeaderTo(new Point(-20000, 0));
                target.DragHeaderTo(new Point(-19999, 8));
                Check(target.Record.X == edge + 1, "通知恢复后受阻立即反向一物理像素");
                await target.EndInteractionAsync();
                var grip = target.ResizeGrip;
                var gripPoint = grip.PointToScreen(new Point(grip.ActualWidth / 2, grip.ActualHeight / 2));
                Check(Hit(gripPoint) == target.Handle, "调尺寸把手物理命中正确");
                SetCursorPos((int)gripPoint.X, (int)gripPoint.Y);
                mouse_event(2, 0, 0, 0, UIntPtr.Zero);
                await Task.Delay(80);
                SetCursorPos((int)gripPoint.X + 12, (int)gripPoint.Y + 12);
                await Task.Delay(100);
                mouse_event(4, 0, 0, 0, UIntPtr.Zero);
                await WaitUntil(() => !runtime.Interacting);
                Check(target.Record.HeaderWidth > before.HeaderWidth, "通知恢复后真实鼠标调尺寸保存");
                File.AppendAllText(log, displays.Length == 1
                    ? "未验证：实际多屏跨屏、负坐标、混合 DPI、物理拔屏、主屏／缩放／分辨率变化。注入度量和业务轨迹不替代这些配置的真实验收。\n"
                    : "未验证：本日志环境之外的配置以及物理拔屏、系统缩放／分辨率切换；记录的多屏仅验收当前可用配置。\n");
                result = 0;
            }
            catch (Exception e) { File.AppendAllText(log, "失败：" + e + "\n"); }
            finally
            {
                mouse_event(4, 0, 0, 0, UIntPtr.Zero);
                runtime?.Dispose();
                if (desktopShown) WinD();
                SetCursorPos(originalCursor.X, originalCursor.Y);
                app.Shutdown();
            }
        };
        try { app.Run(); }
        finally
        {
            var full = Path.GetFullPath(fixture);
            if (!full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(full).StartsWith("Kage-display-dpi-", StringComparison.Ordinal))
                throw new IOException("拒绝清理隔离范围之外的目录。");
            Directory.Delete(full, true);
        }
        return result;

        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            File.AppendAllText(log, "通过：" + message + "\n");
        }
        async Task CheckContent(FolderHeader header, string label)
        {
            await header.Contents.IconsLoaded.WaitAsync(TimeSpan.FromSeconds(15));
            header.UpdateLayout();
            var folder = runtime!.Workspace.Snapshot.Folders.Single(f => f.Folder.Id == header.FolderId);
            Check(folder.Visible && WindowsDesktop.GetWindowRect(header.Handle, out var rectangle)
                && rectangle.Left == folder.Folder.X && rectangle.Top == folder.Folder.Y
                && rectangle.Right - rectangle.Left == Math.Ceiling(folder.Folder.HeaderWidth * folder.DisplayScale)
                && rectangle.Bottom - rectangle.Top == Math.Ceiling((folder.Folder.HeaderHeight + folder.Folder.BodyHeight) * folder.DisplayScale), label + "：实际 HWND 边界与业务物理边界一致");
            var metrics = NativeViewMetrics.ForScale(folder.DisplayScale);
            foreach (ListBoxItem item in header.Contents.Items.Items)
            {
                var image = Image(item);
                var text = (TextBlock)((Grid)item.Content).Children[1];
                Check(image.Source is BitmapSource source && source.PixelWidth >= Math.Round(image.Width * folder.DisplayScale), label + "：原生图标分辨率足够");
                Check(text.FontFamily.Source == metrics.FontFamily && text.FontSize == metrics.FontSize, label + "：目标 DPI 系统字体");
                var iconSize = folder.Folder.Grid ? folder.Folder.GridIconSize : folder.Folder.ListIconSize;
                var minimumHeight = folder.Folder.Grid ? Math.Max(metrics.GridHeight, iconSize + metrics.FontSize * 2)
                    : Math.Max(metrics.ListHeight, iconSize);
                Check(item.ActualHeight >= minimumHeight - .6, label + "：行高容纳当前图标及文字并保留原生下限");
                if (!folder.Folder.Grid && iconSize == (int)ContentIconSize.Small)
                    Check(Math.Abs(item.ActualHeight - metrics.ListHeight) < .6, label + "：小图标列表保持原生行高");
            }
            File.AppendAllText(log, $"{label}：目标 DPI={folder.DisplayScale * 96}，HWND DPI={WindowsDesktop.GetDpiForWindow(header.Handle)}，WPF DPI={VisualTreeHelper.GetDpi(header).PixelsPerInchX}\n");
        }
        void Screenshot(FolderHeader header, string name)
        {
            WindowsDesktop.GetWindowRect(header.Handle, out var rectangle);
            using var bitmap = new System.Drawing.Bitmap(rectangle.Right - rectangle.Left, rectangle.Bottom - rectangle.Top);
            using var graphics = System.Drawing.Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(rectangle.Left, rectangle.Top, 0, 0, bitmap.Size);
            bitmap.Save(Path.Combine(evidence, name));
        }
    }

    private static Image Image(ListBoxItem item) => (Image)((Grid)item.Content).Children[0];
    private static IntPtr Hit(Point point) => WindowsDesktop.WindowFromPoint(new WindowsDesktop.POINT { X = (int)point.X, Y = (int)point.Y });
    private static void Click(Point point)
    { SetCursorPos((int)point.X, (int)point.Y); mouse_event(2, 0, 0, 0, UIntPtr.Zero); mouse_event(4, 0, 0, 0, UIntPtr.Zero); }
    private static void WinD()
    {
        WindowsDesktop.keybd_event(0x5B, 0, 0, UIntPtr.Zero);
        WindowsDesktop.keybd_event(0x44, 0, 0, UIntPtr.Zero);
        WindowsDesktop.keybd_event(0x44, 0, 2, UIntPtr.Zero);
        WindowsDesktop.keybd_event(0x5B, 0, 2, UIntPtr.Zero);
    }
    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(50);
        }
        throw new TimeoutException("实际显示与输入检查超时。");
    }
    private static System.Collections.Generic.IEnumerable<T> VisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found) yield return found;
            foreach (var nested in VisualChildren<T>(child)) yield return nested;
        }
    }
    private sealed class NoStartup : IStartupRegistration
    {
        public string LaunchCommand => "隔离显示检查，不注册自启";
        public string? ReadCommand() => null;
        public void WriteCommand(string? command) => throw new InvalidOperationException("显示检查不得注册自启。");
    }
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out WindowsDesktop.POINT point);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
}
