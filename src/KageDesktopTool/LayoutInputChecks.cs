using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kage.Workspace;

namespace Kage.Desktop;

// 系统鼠标输入经过真实命中和捕获；不注入 WPF 事件，不读取正式用户状态。
internal static class LayoutInputChecks
{
    internal static int Run()
    {
        var fixture = Path.Combine(Path.GetTempPath(), "Kage-layout-input-" + Guid.NewGuid().ToString("N"));
        var evidence = Path.Combine(Environment.CurrentDirectory, ".scratch", "desktop-folder", "verification");
        Directory.CreateDirectory(fixture); Directory.CreateDirectory(evidence);
        var log = Path.Combine(evidence, "upgrade-02-layout-input.txt");
        File.WriteAllText(log, "真实 Windows 布局输入验收\n");
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Runtime? runtime = null;
        var result = 1;
        var desktopShown = false;
        WindowsDesktop.GetCursorPos(out var cursor);
        app.Startup += async (_, _) =>
        {
            try
            {
                var displays = WindowsDesktop.Displays();
                foreach (var display in displays) File.AppendAllText(log, $"实际环境：{display}\n");
                var area = displays.OrderByDescending(d => d.Width * (long)d.Height).First();
                var scale = area.Scale;
                var moving = new FolderRecord(Guid.NewGuid(), "自由拖动", area.X + 30, area.Y + 160);
                var obstacle = new FolderRecord(Guid.NewGuid(), "固定入口", area.X + 30 + (int)(360 * scale), area.Y + 160);
                var bottom = new FolderRecord(Guid.NewGuid(), "底部展开", area.X + area.Width - (int)(320 * scale), area.Y + area.Height - (int)(48 * scale));
                var root = Path.Combine(fixture, "内容");
                foreach (var record in new[] { moving, obstacle, bottom })
                { Directory.CreateDirectory(Path.Combine(root, record.Name)); File.WriteAllText(Path.Combine(root, record.Name, "保留.txt"), "真实内容保留"); }
                var disk = new JsonWorkspaceStore(Path.Combine(fixture, "状态"));
                disk.Save(new WorkspaceState { Root = root, Folders = [moving, obstacle, bottom] });
                var store = new FaultStore(disk);
                IDesktopWorkspace workspace = new DesktopWorkspace(store, new NoStartup());
                Require((await workspace.InitializeAsync(displays)).Succeeded, "隔离初始化");
                runtime = new Runtime(workspace);
                Require(runtime.DesktopAvailable, "实际 Explorer 桌面宿主可见");
                WinD(); desktopShown = true; await Task.Delay(700);
                var header = runtime.Headers[moving.Id];
                Point HeaderPoint() { header.UpdateLayout(); return header.HeaderInput.PointToScreen(new Point(45, header.HeaderInput.ActualHeight / 2)); }
                FolderRecord Saved() => workspace.Snapshot.Folders.Single(f => f.Folder.Id == moving.Id).Folder;
                async Task Start(Point point)
                {
                    Require(WindowsDesktop.WindowFromPoint(new WindowsDesktop.POINT { X = (int)point.X, Y = (int)point.Y }) == header.Handle, "输入点命中真实窗口");
                    Move(point); Mouse(2); await Wait(() => runtime.Interacting);
                }
                async Task Release() { Mouse(4); await Wait(() => !runtime.Interacting); await Task.Delay(150); }
                void Geometry()
                {
                    foreach (var window in runtime.Headers.Values)
                    {
                        WindowsDesktop.GetWindowRect(window.Handle, out var rect);
                        var snapshot = workspace.Snapshot.Folders.Single(f => f.Folder.Id == window.FolderId);
                        Require(rect.Left == window.Record.X && rect.Top == window.Record.Y
                            && rect.Right - rect.Left == Math.Ceiling(window.Record.HeaderWidth * snapshot.DisplayScale)
                            && rect.Bottom - rect.Top == Math.Ceiling((window.Record.HeaderHeight + (window.Record.Expanded ? window.Record.BodyHeight : 0)) * snapshot.DisplayScale), "HWND 物理几何与业务记录一致");
                    }
                }
                var start = HeaderPoint();
                await Start(start);
                var dx = obstacle.X - moving.X + 20;
                Move(new Point(start.X + dx, start.Y)); await Task.Delay(180);
                Require(header.Record.X == obstacle.X + 20 && Saved() == moving, "真实拖动预览进入占用落点且未保存");
                Move(new Point(start.X + dx + (int)(330 * scale), start.Y)); await Task.Delay(150);
                Require(header.Record.X == obstacle.X + 20 + (int)(330 * scale), "真实鼠标直接穿越其他 Folder");
                Move(new Point(start.X + dx, start.Y)); await Task.Delay(150);
                await Release();
                Require(Saved() is { } placed && placed.X == obstacle.X + 20 && placed.Y == obstacle.Y - (int)Math.Ceiling(48 * scale) - 12, "真实释放使用最近空位及既有间隙");
                Require(workspace.Snapshot.Folders.Single(f => f.Folder.Id == obstacle.Id).Folder == obstacle, "释放不移动其他入口");
                Geometry();
                Require((await workspace.ToggleFolderAsync(moving.Id)).Succeeded, "隔离入口展开"); runtime.Render(); await Task.Delay(150);
                var before = Saved(); header.UpdateLayout();
                var divider = header.HeaderDivider;
                var dividerPoint = divider.PointToScreen(new Point(divider.ActualWidth / 2, divider.ActualHeight / 2));
                Require(header.InputHitTest(header.PointFromScreen(dividerPoint)) is DependencyObject hit && FolderHeader.FindParent<System.Windows.Controls.Primitives.Thumb>(hit) == divider, "分隔线物理命中 Thumb");
                await Start(dividerPoint);
                Move(new Point(dividerPoint.X, dividerPoint.Y + (int)(12 * scale))); await Task.Delay(150);
                Require(Saved() == before && Math.Abs(header.Record.HeaderHeight - before.HeaderHeight - 12) < .01, "真实分隔线预览只改变头部高度");
                await Release();
                Require(Saved() == before with { HeaderHeight = before.HeaderHeight + 12 }, "分隔线保存且展示高度、宽度和左侧不变");
                Geometry();
                var cancelled = Saved(); start = HeaderPoint(); await Start(start);
                Move(new Point(start.X + 30, start.Y + 20)); await Task.Delay(100);
                WindowsDesktop.keybd_event(0x1B, 0, 0, UIntPtr.Zero); await Task.Delay(100);
                WindowsDesktop.keybd_event(0x1B, 0, 2, UIntPtr.Zero);
                await Wait(() => !runtime.Interacting); Mouse(4);
                Require(Saved() == cancelled && header.Record == cancelled, "真实 Esc 取消拖动并恢复窗口");
                header.UpdateLayout(); dividerPoint = divider.PointToScreen(new Point(divider.ActualWidth / 2, divider.ActualHeight / 2));
                await Start(dividerPoint); Move(new Point(dividerPoint.X, dividerPoint.Y + 6 * scale)); await Task.Delay(100);
                WindowsDesktop.keybd_event(0x1B, 0, 0, UIntPtr.Zero); await Task.Delay(100); WindowsDesktop.keybd_event(0x1B, 0, 2, UIntPtr.Zero);
                await Wait(() => !runtime.Interacting); Mouse(4);
                Require(Saved() == cancelled && header.Record == cancelled, "真实 Esc 取消分隔线预览");
                start = HeaderPoint(); await Start(start); Move(new Point(start.X - 10, start.Y + 10)); await Task.Delay(100);
                var last = header.Record;
                ReleaseCapture(); await Wait(() => !runtime.Interacting); Mouse(4);
                Require(Saved() == last && header.Record == last, "丢失系统捕获以最后可提交布局结束");
                var saved = Saved(); start = HeaderPoint(); await Start(start);
                Move(new Point(start.X + 25, start.Y)); await Task.Delay(100);
                store.Fail = true; await Release();
                Require(Saved() == saved && header.Record == saved, "布局保存故障恢复已提交快照及实际窗口");
                Require((await workspace.ToggleFolderAsync(bottom.Id)).Succeeded, "真实屏幕底部展开成功"); runtime.Render();
                var expandedBottom = workspace.Snapshot.Folders.Single(f => f.Folder.Id == bottom.Id).Folder;
                Require(expandedBottom.X == bottom.X && expandedBottom.Y == area.Y + area.Height - (int)Math.Ceiling((bottom.HeaderHeight + bottom.BodyHeight) * scale), "底部只上移当前 Folder 以容纳整体");
                Require(Saved() == saved && workspace.Snapshot.Folders.Single(f => f.Folder.Id == obstacle.Id).Folder == obstacle, "底部展开保持其他布局");
                Geometry();
                header.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(header.Surface.ActualWidth), (int)Math.Ceiling(header.Surface.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(header.Surface); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = File.Create(Path.Combine(evidence, "upgrade-02-divider.png"))) png.Save(stream);
                IDesktopWorkspace restarted = new DesktopWorkspace(disk, new NoStartup()); await restarted.InitializeAsync(displays);
                Require(restarted.Snapshot.Folders.Select(f => f.Folder).SequenceEqual(workspace.Snapshot.Folders.Select(f => f.Folder)), "真实输入结束后重启恢复全部布局");
                Require(File.ReadAllText(Path.Combine(root, moving.Name, "保留.txt")) == "真实内容保留", "内容文件保留");
                File.AppendAllText(log, "未验证：本机之外的实际多屏、混合 DPI、拔屏、主屏及系统缩放／分辨率切换；业务轨迹和度量注入另行记录。\n");
                result = 0;
            }
            catch (Exception e) { File.AppendAllText(log, "失败：" + e + "\n"); }
            finally
            { Mouse(4); WindowsDesktop.keybd_event(0x1B, 0, 2, UIntPtr.Zero); runtime?.Dispose(); if (desktopShown) WinD(); SetCursorPos(cursor.X, cursor.Y); app.Shutdown(); }
        };
        try { app.Run(); }
        finally
        {
            var full = Path.GetFullPath(fixture);
            if (!full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("Kage-layout-input-", StringComparison.Ordinal))
                throw new IOException("拒绝清理隔离范围外的目录。");
            Directory.Delete(full, true);
            Console.WriteLine(File.ReadAllText(log));
        }
        return result;

        void Require(bool condition, string message) { if (!condition) throw new Exception(message); File.AppendAllText(log, "通过：" + message + "\n"); }
    }

    private static async Task Wait(Func<bool> condition)
    { var end = DateTime.UtcNow.AddSeconds(8); while (DateTime.UtcNow < end) { if (condition()) return; await Task.Delay(30); } throw new TimeoutException("布局输入验收超时。"); }
    private static void Move(Point point) => SetCursorPos((int)Math.Round(point.X), (int)Math.Round(point.Y));
    private static void Mouse(uint flags) => mouse_event(flags, 0, 0, 0, UIntPtr.Zero);
    private static void WinD() { WindowsDesktop.keybd_event(0x5B, 0, 0, UIntPtr.Zero); WindowsDesktop.keybd_event(0x44, 0, 0, UIntPtr.Zero); WindowsDesktop.keybd_event(0x44, 0, 2, UIntPtr.Zero); WindowsDesktop.keybd_event(0x5B, 0, 2, UIntPtr.Zero); }
    private sealed class NoStartup : IStartupRegistration
    { public string LaunchCommand => "隔离布局验收不注册自启"; public string? ReadCommand() => null; public void WriteCommand(string? command) => throw new InvalidOperationException(); }
    private sealed class FaultStore(IWorkspaceStore inner) : IWorkspaceStore
    {
        internal bool Fail;
        public StateRead Read() => inner.Read();
        public void Save(WorkspaceState state) { if (Fail) { Fail = false; throw new IOException("隔离布局保存故障"); } inner.Save(state); }
        public void RestoreBackup() => inner.RestoreBackup();
    }
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
}
