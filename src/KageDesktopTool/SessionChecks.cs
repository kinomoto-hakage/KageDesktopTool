using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Kage.Workspace;

namespace Kage.Desktop;

// 使用真实会话和随机隔离目录／启动项，不读取正式工作区或个人内容。
internal static class SessionChecks
{
    internal static int Run()
    {
        var fixture = Path.Combine(Path.GetTempPath(), "Kage-session-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        var evidence = Path.Combine(Environment.CurrentDirectory, ".scratch", "desktop-folder", "verification");
        Directory.CreateDirectory(evidence);
        var log = Path.Combine(evidence, "windows-session.txt");
        var exit = 1;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Runtime? runtime = null;
        using var instance = new SingleInstance(fixture);
        var startup = new WindowsStartup(Path.Combine(AppContext.BaseDirectory, "KageDesktopTool.exe"), "KageDesktopTool.Check." + Guid.NewGuid().ToString("N"));
        var controller = new Window { ShowInTaskbar = false, Width = 1, Height = 1 };
        var occupiedHandle = new WindowInteropHelper(controller).EnsureHandle();
        var occupied = WindowsDesktop.RegisterHotKey(occupiedHandle, 99, 0x4003, 0x4B);
        app.Startup += async (_, _) =>
        {
            try
            {
                Check(instance.IsOwner, "隔离会话取得单实例锁");
                var store = new JsonWorkspaceStore(Path.Combine(fixture, "状态"));
                var workspace = new DesktopWorkspace(store, startup);
                Check((await workspace.InitializeAsync(WindowsDesktop.Displays())).Succeeded, "工作区初始化");
                Check(startup.ReadCommand() == null, "首次运行不注册自启");
                Check((await workspace.SelectRootAsync(Path.Combine(fixture, "内容"))).Succeeded, "选择隔离根目录");
                Check((await workspace.CreateFolderAsync("真实工作")).Succeeded, "创建真实目录");
                Check((await workspace.CreateFolderAsync("真实灵感")).Succeeded, "创建第二个真实目录");
                var beforeRestart = workspace.Snapshot.Folders.Select(f => f.Folder).ToArray();
                runtime = new Runtime(workspace);
                await Task.Delay(500);
                Check(runtime.Tray.Visible && runtime.Tray.ContextMenuStrip!.Items.Cast<System.Windows.Forms.ToolStripItem>().Any(item => item.Text == "新建 Folder"), "托盘可见且具有新建入口");
                Check(!runtime.HotkeyRegistered, "真实热键占用时仍有托盘入口及说明");
                Check(runtime.SessionStatus.Contains("注册失败", StringComparison.Ordinal), "热键失败有明确说明");
                await CreateFromEntry(() => ((System.Windows.Forms.ToolStripMenuItem)runtime.Tray.ContextMenuStrip!.Items[0]).PerformClick(), "托盘创建");
                Check(Directory.Exists(Path.Combine(fixture, "内容", "托盘创建")), "真实托盘菜单经共用操作创建目录与头部");
                Check(runtime.Headers.Values.All(header => header.Host != IntPtr.Zero && WindowsDesktop.GetParent(header.Handle) == header.Host && WindowsDesktop.IsWindowVisible(header.Handle)), "头部挂接真实桌面宿主且可见");
                var headers = runtime.Headers.Values.ToArray();
                var first = headers[0];
                Check(first.AllowsTransparency && ((SolidColorBrush)first.Surface.Background).Color.A is > 0 and < 255, "头部背景半透明");
                first.UpdateLayout();
                var image = new RenderTargetBitmap((int)first.Width, (int)first.Height, 96, 96, PixelFormats.Pbgra32);
                image.Render(first);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                using (var preview = File.Create(Path.Combine(evidence, "正式头部.png"))) encoder.Save(preview);
                WindowsDesktop.GetWindowRect(headers[0].Handle, out var a);
                WindowsDesktop.GetWindowRect(headers[1].Handle, out var b);
                Check(a.Right <= b.Left || b.Right <= a.Left || a.Bottom <= b.Top || b.Bottom <= a.Top, "实际头部窗口不重叠");
                File.WriteAllText(Path.Combine(fixture, "内容", "真实工作", "保留.txt"), "隔离真实内容");
                runtime.ShowSettings();
                app.Windows.OfType<SettingsWindow>().Single().Close();
                Check(runtime.Tray.Visible && runtime.Headers.Values.All(h => WindowsDesktop.IsWindowVisible(h.Handle)), "关闭设置保留托盘和头部");

                var request = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                instance.Listen(() => { runtime.Dispatch(runtime.ShowSettings); request.TrySetResult(); }, error => request.TrySetException(new Exception(error)));
                using (var child = Process.Start(new ProcessStartInfo(startupExecutable()) { UseShellExecute = false, CreateNoWindow = true,
                    ArgumentList = { "--instance-check", fixture } })!)
                {
                    await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8));
                    Check(child.ExitCode == 0, "重复进程转交设置请求并结束");
                }
                await request.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await Task.Delay(100);
                Check(app.Windows.OfType<SettingsWindow>().Count() == 1 && runtime.Headers.Count == 3, "重复启动只打开现有设置，不创建重复头部");
                app.Windows.OfType<SettingsWindow>().Single().Close();

                runtime.Dispose();
                if (occupied) { WindowsDesktop.UnregisterHotKey(occupiedHandle, 99); occupied = false; }
                runtime = new Runtime(workspace);
                Check(runtime.HotkeyRegistered, "真实 Ctrl+Alt+K 成功注册");
                await CreateFromEntry(() =>
                {
                    WindowsDesktop.keybd_event(0x11, 0, 0, UIntPtr.Zero);
                    WindowsDesktop.keybd_event(0x12, 0, 0, UIntPtr.Zero);
                    WindowsDesktop.keybd_event(0x4B, 0, 0, UIntPtr.Zero);
                    WindowsDesktop.keybd_event(0x4B, 0, 2, UIntPtr.Zero);
                    WindowsDesktop.keybd_event(0x12, 0, 2, UIntPtr.Zero);
                    WindowsDesktop.keybd_event(0x11, 0, 2, UIntPtr.Zero);
                }, "热键创建");
                Check(Directory.Exists(Path.Combine(fixture, "内容", "热键创建")), "真实快捷键经共用操作创建目录与头部");
                beforeRestart = workspace.Snapshot.Folders.Select(f => f.Folder).ToArray();

                Check((await workspace.SetStartupAsync(true)).Succeeded && startup.ReadCommand() == startup.LaunchCommand, "真实 HKCU 自启开启与目标验证");
                Check((await workspace.SetStartupAsync(true)).Succeeded && startup.ReadCommand() == startup.LaunchCommand, "重复开启不生成重复启动项");
                var restarted = new DesktopWorkspace(store, startup);
                Check((await restarted.InitializeAsync(WindowsDesktop.Displays())).Succeeded && restarted.Snapshot.StartupEnabled, "重启恢复自启偏好");
                Check(restarted.Snapshot.Folders.Select(f => f.Folder).SequenceEqual(beforeRestart), "重启恢复稳定标识、名称与位置");
                Check((await restarted.SetStartupAsync(false)).Succeeded && startup.ReadCommand() == null, "真实 HKCU 自启关闭");
                var handles = runtime.Headers.Values.Select(h => h.Handle).Append(new WindowInteropHelper(runtime.Controller).Handle).ToArray();
                await runtime.ExitAsync();
                Check(handles.All(handle => !WindowsDesktop.IsWindow(handle)), "退出释放全部窗口");
                Check(File.ReadAllText(Path.Combine(fixture, "内容", "真实工作", "保留.txt")) == "隔离真实内容", "退出保留真实内容");
                if (occupied) WindowsDesktop.UnregisterHotKey(occupiedHandle, 99);
                Check(WindowsDesktop.RegisterHotKey(occupiedHandle, 100, 0x4003, 0x4B), "退出及测试占用清理后可重新注册热键");
                WindowsDesktop.UnregisterHotKey(occupiedHandle, 100);
                File.AppendAllText(log, $"完成：{DateTimeOffset.Now:O}，{Environment.OSVersion}\n");
                exit = 0;
            }
            catch (Exception e) { File.AppendAllText(log, "失败：" + e + "\n"); }
            finally
            {
                runtime?.Dispose();
                try { startup.WriteCommand(null); Check(startup.ReadCommand() == null, "清理随机启动项"); }
                catch (Exception e) { File.AppendAllText(log, "启动项清理失败：" + e + "\n"); exit = 1; }
                if (occupied) WindowsDesktop.UnregisterHotKey(occupiedHandle, 99);
                controller.Close();
                app.Shutdown();
            }

            string startupExecutable() => Path.Combine(AppContext.BaseDirectory, "KageDesktopTool.exe");
            void Check(bool condition, string description)
            {
                if (!condition) throw new Exception(description);
                File.AppendAllText(log, "通过：" + description + "\n");
            }
            async Task CreateFromEntry(Action invoke, string name)
            {
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                var dialogCompleted = false;
                timer.Tick += (_, _) =>
                {
                    var dialog = app.Windows.Cast<Window>().FirstOrDefault(window => window.Title == "新建 Folder");
                    if (dialog?.Content is not StackPanel panel) return;
                    panel.Children.OfType<TextBox>().Single().Text = name;
                    panel.Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    dialogCompleted = true;
                    timer.Stop();
                };
                timer.Start();
                try
                {
                    invoke();
                    var deadline = DateTime.UtcNow.AddSeconds(8);
                    while ((!dialogCompleted || !runtime.Workspace.Snapshot.Folders.Any(folder => folder.Folder.Name == name) || runtime.Headers.Count != runtime.Workspace.Snapshot.Folders.Count) && DateTime.UtcNow < deadline)
                        await Task.Delay(50);
                    Check(dialogCompleted && runtime.Workspace.Snapshot.Folders.Any(folder => folder.Folder.Name == name), $"{name}：输入界面与真实操作完成");
                }
                finally { timer.Stop(); }
            }
        };
        File.WriteAllText(log, "Windows 实际会话检查\n");
        try { app.Run(); }
        finally
        {
            var full = Path.GetFullPath(fixture);
            if (!full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("Kage-session-", StringComparison.Ordinal))
                throw new IOException("拒绝清理隔离目录范围以外的路径。");
            Directory.Delete(full, true);
        }
        return exit;
    }
}
