using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using Kage.Workspace;

namespace Kage.Desktop;

// 验收真实 HWND 命中、物理鼠标和 OLE；Explorer 重启前拒绝存在文件操作的会话。
internal static class DesktopRecoveryChecks
{
    internal static int Run()
    {
        var fixture = Path.Combine(Path.GetTempPath(), "Kage-desktop-recovery-" + Guid.NewGuid().ToString("N"));
        var evidence = Path.Combine(Environment.CurrentDirectory, ".scratch", "desktop-folder", "verification");
        Directory.CreateDirectory(fixture);
        Directory.CreateDirectory(evidence);
        var log = Path.Combine(evidence, "desktop-recovery-session.txt");
        File.WriteAllText(log, $"11 桌面会话恢复，{DateTimeOffset.Now:O}，{Environment.OSVersion}\n");
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Runtime? runtime = null;
        Window? covering = null;
        IntPtr explorerWindow = IntPtr.Zero;
        string[] reopen = [];
        var retainedName = "Kage-recovery-" + Guid.NewGuid().ToString("N");
        string? retainedShortcut = null;
        var restartAttempted = false;
        var exit = 1;
        app.Startup += async (_, _) =>
        {
            try
            {
                FileMoveChecks.DesktopVisibility(true);
                IDesktopWorkspace workspace = new DesktopWorkspace(new JsonWorkspaceStore(Path.Combine(fixture, "状态")), new NoStartup());
                Check((await workspace.InitializeAsync(WindowsDesktop.Displays())).Succeeded, "初始化隔离工作区");
                Check((await workspace.SelectRootAsync(Path.Combine(fixture, "内容"))).Succeeded, "选择隔离内容目录");
                await workspace.CreateFolderAsync("恢复后可操作");
                var firstId = workspace.Snapshot.Folders.Single().Folder.Id;
                await workspace.ToggleFolderAsync(firstId);
                await workspace.CreateFolderAsync(retainedName);
                var retainedId = workspace.Snapshot.Folders.Last().Folder.Id;
                var retainedPath = workspace.Snapshot.Folders.Last().ActualPath;
                File.WriteAllText(Path.Combine(retainedPath, "保留.txt"), "保留状态及字节");
                Check((await workspace.DeleteFolderAsync(retainedId, FolderDeleteChoice.KeepContents)).Succeeded, "创建随机保留内容及真实链接");
                retainedShortcut = new JsonWorkspaceStore(Path.Combine(fixture, "状态")).Read().State.RetainedFolders.Single().ShortcutPath;
                runtime = new Runtime(workspace);
                await WaitUntil(() => runtime.DesktopAvailable && workspace.Snapshot.DesktopAvailable);
                var first = runtime.Headers[firstId];
                Check(runtime.HotkeyRegistered, "控制器注册唯一热键");
                covering = new Window { Title = "Kage 会话验收普通应用", Left = first.Record.X,
                    Top = first.Record.Y, Width = first.Width + 60, Height = first.Height + 60,
                    Background = Brushes.DarkBlue };
                covering.Show();
                covering.Activate();
                await Task.Delay(300);
                Check(Hit(HeaderPoint(first)) == new WindowInteropHelper(covering).Handle, "普通应用覆盖 Folder，不抢占正常应用");
                WinD();
                await Task.Delay(700);
                await VerifyInput(first, "Win+D 进入");
                Screenshot("11-WinD-进入.png");
                WinD();
                await Task.Delay(700);
                Check(Hit(HeaderPoint(first)) == new WindowInteropHelper(covering).Handle, "Win+D 退出恢复普通应用的覆盖顺序");
                covering.Hide();
                await VerifyInput(first, "Win+D 退出后显露");
                runtime.ShowSettings();
                await Task.Delay(200);
                var settings = app.Windows.OfType<SettingsWindow>().Single();
                Check(settings.IsVisible && GetForegroundWindow() == new WindowInteropHelper(settings).Handle,
                    "设置独立显示并获得前台焦点");
                settings.Close();
                await VerifyInput(first, "关闭设置");

                var savedFolders = workspace.Snapshot.Folders.Select(item => item.Folder).ToArray();
                var saved = new JsonWorkspaceStore(Path.Combine(fixture, "状态")).Read().State;
                var controller = new WindowInteropHelper(runtime.Controller).Handle;
                var tray = runtime.Tray;
                var menu = tray.ContextMenuStrip;
                // 只结束当前会话桌面宿主所属进程，不结束无关 Explorer 或子进程。
                GetWindowThreadProcessId(WindowsDesktop.Host(), out var shellId);
                using var shellProcess = Process.GetProcessById((int)shellId);
                Check(shellProcess.SessionId == Process.GetCurrentProcess().SessionId, "宿主属于当前交互会话");
                reopen = ExplorerPaths();
                EnsureNoFileOperations(shellProcess.Id);
                Check(!runtime.Moving && !runtime.Interacting && !runtime.ChangingFolder, "重启前本工具没有未完成文件或布局操作");
                File.AppendAllText(log, $"准备重启：Explorer PID {shellId}，需恢复 {reopen.Length} 个实际目录窗口。\n");
                restartAttempted = true;
                shellProcess.Kill();
                await shellProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8));
                await runtime.RefreshDesktopSessionAsync();
                Check(!runtime.DesktopAvailable && !workspace.Snapshot.DesktopAvailable, "宿主退出后报告展示不可用");
                Check(runtime.SessionStatus.Contains("实际目录", StringComparison.Ordinal), "不可用时说明内容保留和实际目录入口");
                Check(runtime.Headers.Values.All(header => !WindowsDesktop.IsWindowVisible(header.Handle)), "宿主不可用期间不浮到普通应用上");
                runtime.ShowSettings();
                Check(app.Windows.OfType<SettingsWindow>().Single().IsVisible, "宿主不可用时设置仍能显示内容入口");
                app.Windows.OfType<SettingsWindow>().Single().Close();
                Check(File.ReadAllText(Path.Combine(retainedPath, "保留.txt")) == "保留状态及字节"
                    && new JsonWorkspaceStore(Path.Combine(fixture, "状态")).Read().State.RetainedFolders.SequenceEqual(saved.RetainedFolders), "不可用期间保留内容及关联不丢失");
                await EnsureExplorer();
                await WaitUntil(() => runtime.DesktopAvailable && workspace.Snapshot.DesktopAvailable);
                first = runtime.Headers[firstId];
                Check(runtime.Headers.Count == 1 && workspace.Snapshot.Folders.Select(item => item.Folder).SequenceEqual(savedFolders), "恢复后无重复窗口且活动布局状态一致");
                Check(new WindowInteropHelper(runtime.Controller).Handle == controller && runtime.HotkeyRegistered
                    && !WindowsDesktop.RegisterHotKey(controller, 98, 0x4003, 0x4B), "控制器及原热键保留，不重复登记");
                WindowsDesktop.UnregisterHotKey(controller, 98);
                Check(ReferenceEquals(tray, runtime.Tray) && ReferenceEquals(menu, runtime.Tray.ContextMenuStrip), "恢复复用原托盘及菜单");
                await CheckTray();
                FileMoveChecks.DesktopVisibility(true);
                await VerifyInput(first, "Explorer 重启恢复");
                Screenshot("11-Explorer-恢复.png");
                await VerifyOle(first);
                Check(new JsonWorkspaceStore(Path.Combine(fixture, "状态")).Read().State.RetainedFolders.SequenceEqual(saved.RetainedFolders), "恢复及真实拖放后保留内容关联一致");
                exit = 0;
            }
            catch (Exception e) { File.AppendAllText(log, "失败：" + e + "\n"); Screenshot("11-会话失败.png"); }
            finally
            {
                if (restartAttempted)
                {
                    try
                    {
                        await EnsureExplorer();
                        var existing = ExplorerPaths();
                        foreach (var path in reopen)
                            if (!existing.Contains(path, StringComparer.OrdinalIgnoreCase)) OpenExplorer(path);
                    }
                    catch (Exception e) { File.AppendAllText(log, "恢复 Explorer 失败：" + e + "\n"); exit = 1; }
                }
                if (explorerWindow != IntPtr.Zero) WindowsDesktop.PostMessage(explorerWindow, 0x10, IntPtr.Zero, IntPtr.Zero);
                covering?.Close();
                runtime?.Dispose();
                FileMoveChecks.DesktopVisibility(false);
                app.Shutdown();
            }
        };
        try { app.Run(); }
        finally
        {
            if (retainedShortcut != null)
            {
                var desktop = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
                var shortcut = Path.GetFullPath(retainedShortcut);
                if (!string.Equals(Path.GetDirectoryName(shortcut), Path.TrimEndingDirectorySeparator(desktop), StringComparison.OrdinalIgnoreCase)
                    || !Path.GetFileName(shortcut).StartsWith(retainedName, StringComparison.Ordinal))
                    throw new IOException("拒绝清理非夹具快捷方式。");
                File.Delete(shortcut);
            }
            var full = Path.GetFullPath(fixture);
            if (!full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(full).StartsWith("Kage-desktop-recovery-", StringComparison.Ordinal))
                throw new IOException("拒绝清理隔离目录之外的内容。");
            Directory.Delete(full, true);
        }
        return exit;

        void Check(bool condition, string description)
        { if (!condition) throw new Exception(description); File.AppendAllText(log, "通过：" + description + "\n"); }

        async Task VerifyInput(FolderHeader header, string stage)
        {
            await Task.Delay(250);
            header.UpdateLayout();
            Check(WindowsDesktop.IsWindowVisible(header.Handle) && Hit(HeaderPoint(header)) == header.Handle, stage + "：头部实际命中");
            Check(Hit(header.Contents.PointToScreen(new Point(25, 20))) == header.Handle, stage + "：展示部分实际命中");
            var toggle = VisualChildren<Button>(header.HeaderInput).Single();
            Click(toggle.PointToScreen(new Point(toggle.ActualWidth / 2, toggle.ActualHeight / 2)));
            await WaitUntil(() => !header.Record.Expanded);
            Click(toggle.PointToScreen(new Point(toggle.ActualWidth / 2, toggle.ActualHeight / 2)));
            await WaitUntil(() => header.Record.Expanded);
            Check(true, stage + "：真实点击展开／折叠");
            var startX = header.Record.X;
            var start = HeaderPoint(header);
            await FileMoveChecks.PhysicalDrag(start, new Point(start.X + 24, start.Y + 10));
            await WaitUntil(() => !runtime!.Interacting);
            Check(header.Record.X == startX + 24, stage + "：真实鼠标拖动位置已保存");
            var grip = VisualChildren<Thumb>(header.Surface).Single();
            var width = header.Record.HeaderWidth;
            var resize = grip.PointToScreen(new Point(grip.ActualWidth / 2, grip.ActualHeight / 2));
            await FileMoveChecks.PhysicalDrag(resize, new Point(resize.X + 18, resize.Y + 12));
            await WaitUntil(() => !runtime!.Interacting);
            Check(header.Record.HeaderWidth > width + 10, stage + "：真实鼠标缩放已保存");
        }

        async Task VerifyOle(FolderHeader header)
        {
            var targetPath = Path.Combine(fixture, "OLE 目录");
            Directory.CreateDirectory(targetPath);
            File.WriteAllBytes(Path.Combine(targetPath, "恢复拖放.bin"), [0, 42, 255]);
            OpenExplorer(targetPath);
            await WaitUntil(() => (explorerWindow = FileMoveChecks.ExplorerWindow(targetPath)) != IntPtr.Zero);
            var screen = WindowsDesktop.Displays().First();
            WindowsDesktop.SetWindowPos(explorerWindow, IntPtr.Zero, screen.X + screen.Width / 2, screen.Y + 80,
                screen.Width / 2 - 30, screen.Height - 150, 0x40);
            SetForegroundWindow(explorerWindow);
            await Task.Delay(700);
            FileMoveChecks.SelectShellItems(targetPath, ["恢复拖放.bin"]);
            var item = AutomationElement.FromHandle(explorerWindow).FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem)).Cast<AutomationElement>()
                .Single(element => element.Current.Name is "恢复拖放.bin" or "恢复拖放");
            var rectangle = item.Current.BoundingRectangle;
            await FileMoveChecks.PhysicalDrag(new Point(rectangle.Left + rectangle.Width / 2, rectangle.Top + rectangle.Height / 2), HeaderPoint(header));
            var content = runtime!.Workspace.Snapshot.Folders.Single().ActualPath;
            await WaitUntil(() => File.Exists(Path.Combine(content, "恢复拖放.bin")) && runtime.ActiveMove == null);
            CloseResults();
            Check(!File.Exists(Path.Combine(targetPath, "恢复拖放.bin"))
                && File.ReadAllBytes(Path.Combine(content, "恢复拖放.bin")).SequenceEqual(new byte[] { 0, 42, 255 }), "恢复后 Explorer 实际 OLE 拖入字节及源消失");
            await header.Contents.IconsLoaded;
            header.UpdateLayout();
            var outgoing = header.Contents.Items.Items.Cast<ListBoxItem>().Single();
            outgoing.IsSelected = true;
            await FileMoveChecks.PhysicalDrag(outgoing.PointToScreen(new Point(outgoing.ActualWidth / 2, outgoing.ActualHeight / 2)),
                FileMoveChecks.ExplorerBackground(explorerWindow));
            await WaitUntil(() => File.Exists(Path.Combine(targetPath, "恢复拖放.bin")) && runtime.ActiveMove == null);
            CloseResults();
            Check(!File.Exists(Path.Combine(content, "恢复拖放.bin"))
                && File.ReadAllBytes(Path.Combine(targetPath, "恢复拖放.bin")).SequenceEqual(new byte[] { 0, 42, 255 }), "恢复后展示部分实际 OLE 拖出字节及源消失");
        }

        async Task CheckTray()
        {
            AutomationElement? button = null;
            var overflowOpened = false;
            await WaitUntil(() =>
            {
                var buttons = AutomationElement.RootElement.FindAll(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)).Cast<AutomationElement>()
                    .ToArray();
                button = buttons.FirstOrDefault(element => element.Current.Name.Contains("Kage 桌面整理", StringComparison.Ordinal));
                if (button == null && !overflowOpened)
                {
                    var overflow = buttons.FirstOrDefault(element => element.Current.Name is "显示隐藏的图标" or "Show hidden icons");
                    if (overflow != null)
                    {
                        var bounds = overflow.Current.BoundingRectangle;
                        Click(new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2));
                        overflowOpened = true;
                    }
                }
                return button != null;
            });
            Check(button != null, "Explorer 原生通知区域有恢复后的托盘按钮");
            var rectangle = button!.Current.BoundingRectangle;
            var point = new Point(rectangle.Left + rectangle.Width / 2, rectangle.Top + rectangle.Height / 2);
            Click(point);
            await Task.Delay(70);
            Click(point);
            await WaitUntil(() => app.Windows.OfType<SettingsWindow>().Any(window => window.IsVisible));
            Check(true, "恢复后真实双击托盘打开唯一设置窗口");
            app.Windows.OfType<SettingsWindow>().Single().Close();
        }

        void CloseResults() { foreach (var window in app.Windows.OfType<MoveDialog>().ToArray()) window.Close(); }
        void Screenshot(string name)
        {
            var area = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
            using var bitmap = new System.Drawing.Bitmap(area.Width, area.Height);
            using var graphics = System.Drawing.Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(area.Left, area.Top, 0, 0, area.Size);
            bitmap.Save(Path.Combine(evidence, name));
        }
    }

    private static void EnsureNoFileOperations(int shellId)
    {
        foreach (AutomationElement window in AutomationElement.RootElement.FindAll(TreeScope.Children, System.Windows.Automation.Condition.TrueCondition))
        {
            if (window.Current.ProcessId != shellId) continue;
            var name = window.Current.Name;
            if (window.Current.ClassName == "#32770" || window.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ProgressBar)) != null
                || name.Contains("复制", StringComparison.Ordinal) || name.Contains("移动", StringComparison.Ordinal)
                || name.Contains("删除", StringComparison.Ordinal))
                throw new InvalidOperationException("检测到 Explorer 对话框或未完成文件操作，请结束后重试会话验收。");
        }
    }

    private static string[] ExplorerPaths()
    {
        var paths = new List<string>();
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
        dynamic windows = shell.Windows();
        try
        {
            for (var i = 0; i < (int)windows.Count; i++)
            {
                dynamic window = windows.Item(i);
                if (window == null) continue;
                try
                {
                    string location = window.LocationURL;
                    if (Uri.TryCreate(location, UriKind.Absolute, out var uri) && uri.IsFile) paths.Add(uri.LocalPath);
                    else
                    {
                        dynamic document = window.Document;
                        dynamic folder = document.Folder;
                        dynamic self = folder.Self;
                        try
                        {
                            string path = self.Path;
                            if (!path.StartsWith("::{", StringComparison.Ordinal))
                                throw new InvalidOperationException("存在无法恢复的 Explorer 位置，先关闭该窗口再验收。");
                            paths.Add(path);
                        }
                        finally { Marshal.FinalReleaseComObject(self); Marshal.FinalReleaseComObject(folder); Marshal.FinalReleaseComObject(document); }
                    }
                }
                finally { Marshal.FinalReleaseComObject(window); }
            }
        }
        finally { Marshal.FinalReleaseComObject(windows); Marshal.FinalReleaseComObject(shell); }
        return paths.ToArray();
    }

    private static async Task EnsureExplorer()
    {
        if (WindowsDesktop.Host() != IntPtr.Zero) return;
        using var explorer = Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
        await WaitUntil(() => WindowsDesktop.Host() != IntPtr.Zero);
    }
    private static void OpenExplorer(string path)
    { using var process = Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true, ArgumentList = { path } }); }
    private static IEnumerable<T> VisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found) yield return found;
            foreach (var nested in VisualChildren<T>(child)) yield return nested;
        }
    }
    private static Point HeaderPoint(FolderHeader header)
    { WindowsDesktop.GetWindowRect(header.Handle, out var rectangle); return new(rectangle.Left + 65, rectangle.Top + 24); }
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
        var deadline = DateTime.UtcNow.AddSeconds(18);
        while (DateTime.UtcNow < deadline) { if (condition()) return; await Task.Delay(100); }
        throw new TimeoutException("桌面恢复操作未在时限内完成。");
    }
    private sealed class NoStartup : IStartupRegistration
    {
        public string LaunchCommand => "隔离检查不注册自启";
        public string? ReadCommand() => null;
        public void WriteCommand(string? command) => throw new InvalidOperationException("会话检查不应修改自启。");
    }
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
}
