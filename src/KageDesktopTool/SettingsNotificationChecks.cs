using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kage.Workspace;
using Microsoft.Windows.AppNotifications;
using Condition = System.Windows.Automation.Condition;

namespace Kage.Desktop;

internal static class SettingsNotificationChecks
{
    internal static int Run(bool notificationOnly = false, bool settingsOnly = false)
    {
        var fixture = Path.Combine(Path.GetTempPath(), "Kage-notifications-" + Guid.NewGuid().ToString("N"));
        var evidence = Path.Combine(Environment.CurrentDirectory, ".scratch", "desktop-folder", "verification");
        Directory.CreateDirectory(evidence);
        var log = Path.Combine(evidence, settingsOnly ? "upgrade-03-settings.txt" : "upgrade-03-settings-notifications.txt");
        File.WriteAllText(log, "设置、样式、结果详情与真实 Windows 通知检查\n");
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Runtime? runtime = null;
        var exit = 1;
        FileDrag.GetCursorPos(out var cursor);
        var activated = new TaskCompletionSource<Guid?>();
        WindowsNotifications.Register(argument =>
        {
            var id = WindowsNotifications.ResultId(argument);
            activated.TrySetResult(id);
            runtime?.Dispatch(() => runtime.ShowResults(id));
        });
        app.Startup += async (_, _) =>
        {
            try
            {
                if (notificationOnly)
                {
                    var probe = new OperationDetails(Guid.NewGuid(), DateTimeOffset.Now, "最小通知", "最小通知夹具" + Guid.NewGuid().ToString("N")[..6], "只投递一条通知");
                    WindowsNotifications.Send(probe);
                    var rectangle = await Task.Run(() => FindNotification(probe.Summary));
                    SetCursorPos((int)(rectangle.Left + rectangle.Width / 2), (int)(rectangle.Top + rectangle.Height / 2));
                    mouse_event(2, 0, 0, 0, UIntPtr.Zero); mouse_event(4, 0, 0, 0, UIntPtr.Zero);
                    Check(await activated.Task.WaitAsync(TimeSpan.FromSeconds(10)) == probe.Id, "最小通知实际可见并经系统鼠标点击激活");
                    exit = 0;
                    return;
                }
                var store = new FaultStore(new JsonWorkspaceStore(Path.Combine(fixture, "状态")));
                IDesktopWorkspace workspace = new DesktopWorkspace(store, new NoStartup());
                await workspace.InitializeAsync(WindowsDesktop.Displays());
                await workspace.SelectRootAsync(Path.Combine(fixture, "内容"));
                await workspace.CreateFolderAsync("样式与通知 · 长路径检查");
                var folder = workspace.Snapshot.Folders.Single();
                File.WriteAllText(Path.Combine(folder.ActualPath, "保留.txt"), "真实内容");
                // 结果存储和投递属于平台边界，业务仍使用实际文件夹具。
                var delivered = new List<OperationDetails>();
                var feedback = new OperationFeedback(Path.Combine(fixture, "结果"), entry => delivered.Add(entry));
                var batch = new BatchMoveResult([
                    new("源一", Outcome.Success, "已移动", "目标一"), new("源二", Outcome.Skipped, "跳过", "源二"),
                    new("源三", Outcome.Cancelled, "取消", "源三"), new("源四", Outcome.Failed, "拒绝访问", "源四")],
                    new(Outcome.Success, "状态已保存"));
                var recorded = feedback.Record(batch, true);
                Check(delivered.Count == 1 && recorded.Summary.Contains("成功 1，跳过 1，取消 1，失败 1")
                    && recorded.Details.Contains("目标一") && recorded.Summary.Contains("拒绝访问"), "批次仅投递一次，四类计数及真实路径保留");
                feedback.Record(batch, false);
                Check(delivered.Count == 1 && feedback.Entries.Count == 2, "关闭只抑制投递，仍保存详情");
                var failingDelivery = new OperationFeedback(Path.Combine(fixture, "投递故障"), _ => throw new IOException("投递故障"));
                var failed = failingDelivery.Record("真实结果", new(Outcome.Success, "文件已完成", folder.ActualPath), true);
                Check(failed.Delivery.Contains("投递故障") && failed.Details.Contains(folder.ActualPath), "通知故障保留原成功结果与实际位置");
                Check(new OperationFeedback(Path.Combine(fixture, "投递故障")).Entries.Single().Id == failed.Id, "结果及投递故障重启可查");
                runtime = new Runtime(workspace, Path.Combine(fixture, "会话结果"));
                runtime.ShowSettings();
                var settings = app.Windows.OfType<SettingsWindow>().Single();
                Check(settings.Title == "Kage 桌面工具 · 设置", "设置统一标题");
                var nav = Descendants<ListBox>(settings).Single();
                Check(nav.Items.Count == 6, "六个功能分区");
                for (var i = 0; i < 6; i++) { nav.SelectedIndex = i; settings.UpdateLayout(); SavePreview(settings, $"upgrade-03-settings-{i}.png"); }
                settings.SelectResults(null);
                var notifications = Descendants<CheckBox>(settings).Single(box => Equals(box.Content, "接收操作结果消息提示"));
                notifications.IsChecked = false;
                await runtime.RefreshDisplayEnvironmentAsync();
                Check(notifications.IsChecked == false, "后台刷新保留未提交选择");
                store.FailNext = true;
                Button(settings, "保存消息提示选择").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                await WaitUntil(() => runtime.Feedback.Entries.Any(entry => entry.Details.Contains("注入提交故障")));
                Check(workspace.Snapshot.NotificationsEnabled && notifications.IsChecked == true, "保存失败恢复已提交开关并保留原因");
                notifications.IsChecked = false;
                Button(settings, "保存消息提示选择").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                await WaitUntil(() => !workspace.Snapshot.NotificationsEnabled);
                Check(runtime.Feedback.Entries[0].Delivery.Contains("已关闭"), "关闭操作也不投递完成提示");
                var reading = runtime.Feedback.Record("长详情阅读", new(Outcome.Success, string.Join("\n", Enumerable.Repeat("实际路径与结果详情的阅读夹具", 120))), false);
                runtime.ShowResults(reading.Id);
                await Task.Delay(150);
                settings.UpdateLayout();
                var resultScroll = Descendants<ScrollViewer>(settings).Single(viewer => viewer.ScrollableHeight > 0);
                resultScroll.ScrollToVerticalOffset(350);
                await Task.Delay(100);
                var readingOffset = resultScroll.VerticalOffset;
                await runtime.RefreshDisplayEnvironmentAsync();
                await Task.Delay(150);
                Check(readingOffset > 100 && Math.Abs(resultScroll.VerticalOffset - readingOffset) < 1,
                    "通知定位后后台刷新保留长详情阅读位置");
                var style = runtime.CreateAppearance(folder.Folder.Id)!;
                style.Show(); style.UpdateLayout();
                Check(style.Title == "Folder 样式", "样式统一标题");
                var percent = Descendants<TextBlock>(style).Single(text => text.Text == "32%");
                var endpoint = Descendants<TextBlock>(style).Single(text => text.Text == "完全透明");
                Check(percent.PointToScreen(new Point()).X >= style.Transparency.PointToScreen(new Point(style.Transparency.ActualWidth, 0)).X
                    && endpoint.ActualWidth > 0, "实际 DPI 的百分比与完全透明标签位于滑块右侧");
                style.Transparency.Value = 75;
                style.SelectColor("#123456");
                store.FailNext = true;
                await style.ApplyAsync();
                Check(style.IsVisible && style.Error.Text.Contains("注入提交故障") && workspace.Snapshot.Folders.Single().Folder.Color == folder.Folder.Color, "样式提交失败保留旧值与可重试草稿");
                SavePreview(style, "upgrade-03-style.png");
                await style.ApplyAsync();
                Check(!style.IsVisible && workspace.Snapshot.Folders.Single().Folder.Opacity == .25, "重试应用关闭并保存背景透明度");
                var source = Path.Combine(fixture, "移入.txt");
                File.WriteAllText(source, "真实移动");
                var moved = await runtime.MoveFilesAsync([source], MoveTarget.Folder(folder.Folder.Id));
                Check(moved!.Items.Single().Outcome == Outcome.Success && !app.Windows.OfType<MoveDialog>().Any()
                    && runtime.Feedback.Entries[0].Title == "文件移动", "实际移动完成进度自动结束且详情保留");
                settings.Close();
                Check(runtime.Tray.Visible, "关闭设置后后台运行");
                if (settingsOnly)
                {
                    exit = 0;
                    File.AppendAllText(log, "界面与结果检查通过；本次未运行系统通知显示／点击。\n");
                    return;
                }
                await workspace.SetNotificationsAsync(true);
                await Task.Delay(1500);
                var marker = "通知点击夹具" + Guid.NewGuid().ToString("N")[..6];
                runtime.Complete("通知验收", new(Outcome.Success, marker, folder.ActualPath));
                var expected = runtime.Feedback.Entries[0];
                var history = await AppNotificationManager.Default.GetAllAsync();
                Check(history.Any(toast => toast.Payload.Contains(marker)), "真实 Windows 通知历史包含本次结果");
                Check(true, "普通无 MSIX 发布程序向 Windows 投递真实通知并进入系统历史");
                File.AppendAllText(log, "Windows 投递设置：" + AppNotificationManager.Default.Setting + "\n");
                await Task.Delay(1000);
                var bounds = await Task.Run(() => FindNotification(marker));
                SetCursorPos((int)(bounds.Left + bounds.Width / 2), (int)(bounds.Top + bounds.Height / 2));
                mouse_event(2, 0, 0, 0, UIntPtr.Zero);
                mouse_event(4, 0, 0, 0, UIntPtr.Zero);
                Check(await activated.Task.WaitAsync(TimeSpan.FromSeconds(15)) == expected.Id, "系统鼠标点击返回对应结果标识");
                await Task.Delay(100);
                Check(app.Windows.OfType<SettingsWindow>().Single().IsVisible && runtime.Feedback.Entries[0].Id == expected.Id,
                    "点击查看原结果且未产生新操作");
                SavePreview(app.Windows.OfType<SettingsWindow>().Single(), "upgrade-03-click-results.png");
                Check(File.ReadAllText(Path.Combine(folder.ActualPath, "保留.txt")) == "真实内容", "通知与设置保留原内容");
                exit = 0;
                File.AppendAllText(log, "全部检查通过。\n");
            }
            catch (Exception e) { File.AppendAllText(log, "失败：" + e + "\n"); }
            finally
            {
                runtime?.Dispose();
                SetCursorPos(cursor.X, cursor.Y);
                AppNotificationManager.Default.UnregisterAll();
                app.Shutdown();
            }
        };
        app.Run();
        if (Directory.Exists(fixture) && Path.GetFullPath(fixture).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(fixture).StartsWith("Kage-notifications-", StringComparison.Ordinal)) Directory.Delete(fixture, true);
        return exit;

        void Check(bool passed, string message)
        {
            File.AppendAllText(log, (passed ? "通过：" : "失败：") + message + "\n");
            if (!passed) throw new Exception(message);
        }
    }

    private static Rect FindNotification(string marker)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        var centerOpened = false;
        while (DateTime.UtcNow < deadline)
        {
            var items = AutomationElement.RootElement.FindAll(TreeScope.Descendants, Condition.TrueCondition);
            foreach (AutomationElement item in items)
            {
                try
                {
                    if (item.Current.Name.Contains(marker, StringComparison.Ordinal) && !item.Current.IsOffscreen && item.Current.BoundingRectangle.Width > 0)
                        return item.Current.BoundingRectangle;
                }
                catch (ElementNotAvailableException) { }
            }
            if (!centerOpened && DateTime.UtcNow > deadline.AddSeconds(-12))
            {
                centerOpened = true;
                WindowsDesktop.keybd_event(0x5B, 0, 0, UIntPtr.Zero);
                WindowsDesktop.keybd_event(0x4E, 0, 0, UIntPtr.Zero);
                WindowsDesktop.keybd_event(0x4E, 0, 2, UIntPtr.Zero);
                WindowsDesktop.keybd_event(0x5B, 0, 2, UIntPtr.Zero);
                System.Threading.Thread.Sleep(800);
            }
            System.Threading.Thread.Sleep(150);
        }
        var evidence = Path.Combine(Environment.CurrentDirectory, ".scratch", "desktop-folder", "verification");
        var screen = System.Windows.Forms.SystemInformation.VirtualScreen;
        using (var bitmap = new System.Drawing.Bitmap(screen.Width, screen.Height))
        {
            using var graphics = System.Drawing.Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(screen.Left, screen.Top, 0, 0, bitmap.Size);
            bitmap.Save(Path.Combine(evidence, "upgrade-03-notification-screen.png"));
        }
        throw new Exception("Windows 通知文字未出现在实际 UIAutomation 树，不能宣称真实点击通过。");
    }

    private static async Task WaitUntil(Func<bool> check)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!check()) { if (DateTime.UtcNow >= deadline) throw new TimeoutException("等待实际结果超时"); await Task.Delay(100); }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T found) yield return found;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    private static Button Button(Window window, string label) => Descendants<Button>(window).Single(button => Equals(button.Content, label));
    private static void SavePreview(Window window, string name)
    {
        window.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(window);
        var bitmap = new RenderTargetBitmap((int)(window.ActualWidth * dpi.DpiScaleX), (int)(window.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(Environment.CurrentDirectory, ".scratch", "desktop-folder", "verification", name));
        encoder.Save(file);
    }

    private sealed class FaultStore(IWorkspaceStore real) : IWorkspaceStore
    {
        internal bool FailNext { get; set; }
        public StateRead Read() => real.Read();
        public void Save(WorkspaceState state) { if (FailNext) { FailNext = false; throw new IOException("注入提交故障"); } real.Save(state); }
        public void RestoreBackup() => real.RestoreBackup();
    }
    private sealed class NoStartup : IStartupRegistration
    {
        public string LaunchCommand => "隔离启动，不注册";
        public string? ReadCommand() => null;
        public void WriteCommand(string? command) => throw new InvalidOperationException("检查不应注册自启");
    }
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
}
