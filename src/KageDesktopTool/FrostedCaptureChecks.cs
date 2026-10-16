using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Kage.Workspace;

namespace Kage.Desktop;

// 真实物理按下，再通过 Windows 捕获转移产生 WM_CAPTURECHANGED；不注入 WPF 事件。
internal static class FrostedCaptureChecks
{
    internal static int Run()
    {
        var fixture = Path.Combine(Path.GetTempPath(), "Kage-frosted-capture-" + Guid.NewGuid().ToString("N"));
        var evidence = Path.Combine(Environment.CurrentDirectory, ".scratch", "folder-frosted-background", "verification");
        Directory.CreateDirectory(fixture); Directory.CreateDirectory(evidence);
        var log = Path.Combine(evidence, "review-native-capture.txt"); File.WriteAllText(log, "审查回归：透明原生捕获丢失\n");
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Runtime? runtime = null; var exit = 1; WindowsDesktop.GetCursorPos(out var cursor);
        app.Startup += async (_, _) =>
        {
            try
            {
                IDesktopWorkspace workspace = new DesktopWorkspace(new JsonWorkspaceStore(Path.Combine(fixture, "状态")), new NoStartup());
                await workspace.InitializeAsync(WindowsDesktop.Displays());
                await workspace.SelectRootAsync(Path.Combine(fixture, "内容")); await workspace.CreateFolderAsync("真实捕获检查");
                var id = workspace.Snapshot.Folders.Single().Folder.Id;
                var move = workspace.BeginLayout(id)!; var original = workspace.Snapshot.Folders.Single().Folder;
                move.BeginDrag(0, 0); move.DragTo(1000 - original.X, 220 - original.Y); move.EndDrag();
                Require((await workspace.CommitLayoutAsync(move)).Succeeded, "隔离 Folder 放在空白区域");
                var style = workspace.BeginAppearance(id)!; style.SetFrosted(true); style.SetOpacity(0);
                Require((await workspace.ApplyAppearanceAsync(style)).Succeeded && (await workspace.ToggleFolderAsync(id)).Succeeded, "正式开启完全透明毛玻璃并展开");
                var file = Path.Combine(workspace.Snapshot.Folders.Single().ActualPath, "保留.bin"); File.WriteAllBytes(file, [0, 42, 255]);
                await workspace.RefreshAsync(WindowsDesktop.Displays());
                var environment = new FrostedEnvironment();
                FileMoveChecks.DesktopVisibility(true); runtime = new Runtime(workspace, frostedEnvironment: () => environment);
                var header = runtime.Headers[id]; await Task.Delay(300); header.UpdateLayout();
                var point = header.HeaderDivider.PointToScreen(new Point(20, 1));
                Require(WindowsDesktop.WindowFromPoint(new WindowsDesktop.POINT { X = (int)point.X, Y = (int)point.Y }) == header.Host,
                    "分隔线透明像素真实命中原生背景");
                SetCursorPos((int)point.X, (int)point.Y); await Task.Delay(80); mouse_event(2, 0, 0, 0, UIntPtr.Zero);
                await Wait(() => runtime.Interacting, "物理按下透明 Thumb 开始布局交互");
                Require(GetCapture() == header.Host, "Windows 实际捕获属于背景 HWND");
                SetCursorPos((int)point.X, (int)point.Y + 12); await Task.Delay(150);
                var previewHeight = header.Record.HeaderHeight;
                Require(previewHeight > original.HeaderHeight, "真实鼠标移动预览头部厚度");
                var controller = new WindowInteropHelper(runtime.Controller).Handle;
                SetCapture(controller);
                Require(GetCapture() == controller, "实际 Win32 转移捕获触发原背景 WM_CAPTURECHANGED");
                await Wait(() => !runtime.Interacting, "捕获转移后布局会话结束，不依赖返回原窗口的鼠标松开");
                ReleaseCapture(); mouse_event(4, 0, 0, 0, UIntPtr.Zero); await Task.Delay(150);
                Require(workspace.Snapshot.Folders.Single().Folder.HeaderHeight == previewHeight
                    && header.Record == workspace.Snapshot.Folders.Single().Folder, "丢捕获沿用既有结束语义保存当前预览并完成刷新");
                var dialog = runtime.CreateAppearance(id); Require(dialog != null, "捕获丢失后正式样式入口继续可用"); dialog!.Close();
                var suspended = await BeginResize();
                environment = environment with { EnergySaver = 1 }; runtime.RefreshFrostedEnvironment();
                Require(!WindowsDesktop.IsWindow(suspended.Host) && !header.FrostedVisible, "暂停实际销毁正在捕获的旧背景 HWND");
                await Wait(() => !runtime.Interacting, "暂停释放背景后布局会话结束，不残留 activeInteraction");
                mouse_event(4, 0, 0, 0, UIntPtr.Zero); await Task.Delay(100);
                Require(workspace.Snapshot.Folders.Single().Folder.HeaderHeight == suspended.Height
                    && runtime.Feedback.Entries.Count(e => e.Title == "Folder 布局") == suspended.Feedback + 1,
                    "背景释放与系统丢捕获重入只提交一次，保留当前预览");
                dialog = runtime.CreateAppearance(id); Require(dialog != null && dialog.FrostedStatus.Text.Contains("省电模式"), "暂停后正式样式入口和系统原因继续可用"); dialog!.Close();
                environment = new FrostedEnvironment(); runtime.RefreshFrostedEnvironment();
                Require(header.FrostedVisible && header.Record.Frosted, "解除暂停后重建已保存的开启效果");
                var cancelled = await BeginResize();
                EnableWindow(cancelled.Host, false);
                Require(!IsWindowEnabled(cancelled.Host), "实际 EnableWindow 禁用原生捕获窗口触发系统 WM_CANCELMODE");
                await Wait(() => !runtime.Interacting, "系统取消模式结束原生缩放交互");
                EnableWindow(cancelled.Host, true); mouse_event(4, 0, 0, 0, UIntPtr.Zero); await Task.Delay(100);
                Require(workspace.Snapshot.Folders.Single().Folder.HeaderHeight == cancelled.Height
                    && runtime.Feedback.Entries.Count(e => e.Title == "Folder 布局") == cancelled.Feedback + 1, "取消模式与丢捕获组合只提交一次");
                var released = await BeginResize();
                mouse_event(4, 0, 0, 0, UIntPtr.Zero);
                await Wait(() => !runtime.Interacting, "普通鼠标松开结束原生缩放"); await Task.Delay(150);
                Require(workspace.Snapshot.Folders.Single().Folder.HeaderHeight == released.Height
                    && runtime.Feedback.Entries.Count(e => e.Title == "Folder 布局") == released.Feedback + 1, "普通 ReleaseCapture 同步重入不产生第二次提交");
                await Toggle(false); await Toggle(true);
                dialog = runtime.CreateAppearance(id); Require(dialog != null, "全部捕获轨迹之后样式窗口仍可打开"); dialog!.Close();
                Require(File.ReadAllBytes(file).SequenceEqual(new byte[] { 0, 42, 255 }), "捕获丢失不改变真实内容字节");
                exit = 0;

                async Task<(double Height, IntPtr Host, int Feedback)> BeginResize()
                {
                    await app.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                    header.UpdateLayout();
                    var start = header.HeaderDivider.PointToScreen(new Point(20, 1));
                    Require(WindowsDesktop.WindowFromPoint(new WindowsDesktop.POINT { X = (int)start.X, Y = (int)start.Y }) == header.Host,
                        "下一次缩放仍由真实透明 Thumb 命中背景");
                    var feedback = runtime.Feedback.Entries.Count(e => e.Title == "Folder 布局");
                    SetCursorPos((int)start.X, (int)start.Y); await Task.Delay(80); mouse_event(2, 0, 0, 0, UIntPtr.Zero);
                    await Wait(() => runtime.Interacting && GetCapture() == header.Host, "真实再次按下取得原生缩放捕获");
                    var before = header.Record.HeaderHeight;
                    SetCursorPos((int)start.X, (int)start.Y + 12); await Task.Delay(150);
                    Require(header.Record.HeaderHeight > before, "真实再次移动产生有效尺寸预览");
                    return (header.Record.HeaderHeight, header.Host, feedback);
                }
                async Task Toggle(bool expanded)
                {
                    await app.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle); header.UpdateLayout();
                    var target = header.ExpandButton.PointToScreen(new Point(header.ExpandButton.ActualWidth / 2, header.ExpandButton.ActualHeight / 2));
                    Require(header.OwnsInputWindow(WindowsDesktop.WindowFromPoint(new WindowsDesktop.POINT { X = (int)target.X, Y = (int)target.Y })), "后续展开按钮真实命中当前 Folder");
                    SetCursorPos((int)target.X, (int)target.Y); await Task.Delay(80); mouse_event(2, 0, 0, 0, UIntPtr.Zero); mouse_event(4, 0, 0, 0, UIntPtr.Zero);
                    await Wait(() => header.Record.Expanded == expanded, "捕获结束后真实展开／折叠继续可用");
                }
            }
            catch (Exception error) { File.AppendAllText(log, "失败：" + error + "\n"); }
            finally
            { ReleaseCapture(); mouse_event(4, 0, 0, 0, UIntPtr.Zero); runtime?.Dispose(); FileMoveChecks.DesktopVisibility(false); SetCursorPos(cursor.X, cursor.Y); app.Shutdown(); }
        };
        try { app.Run(); }
        finally
        {
            var full = Path.GetFullPath(fixture);
            if (!full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(full).StartsWith("Kage-frosted-capture-", StringComparison.Ordinal)) throw new IOException("拒绝清理隔离范围之外的目录。");
            Directory.Delete(full, true);
        }
        return exit;
        void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); File.AppendAllText(log, "通过：" + message + "\n"); }
        async Task Wait(Func<bool> condition, string message)
        { for (var i = 0; i < 25 && !condition(); i++) await Task.Delay(50); Require(condition(), message); }
    }
    private sealed class NoStartup : IStartupRegistration
    { public string LaunchCommand => "隔离检查不注册自启"; public string? ReadCommand() => null; public void WriteCommand(string? command) => throw new InvalidOperationException(); }
    [DllImport("user32.dll")] private static extern IntPtr SetCapture(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetCapture();
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern bool EnableWindow(IntPtr hwnd, bool enabled);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
}
