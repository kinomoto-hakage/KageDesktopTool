using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kage.Workspace;

namespace Kage.Desktop;

// 实际设置按钮、进度窗口和取消按钮；仅第二项目的取消时机使用可控文件系统适配器。
internal static class RootMigrationChecks
{
    internal static int Run()
    {
        var fixture = Path.Combine(Path.GetTempPath(), "Kage-migration-" + Guid.NewGuid().ToString("N"));
        var evidence = Path.Combine(Environment.CurrentDirectory, ".scratch", "desktop-folder", "verification");
        Directory.CreateDirectory(fixture);
        Directory.CreateDirectory(evidence);
        var log = Path.Combine(evidence, "root-migration-session.txt");
        File.WriteAllText(log, "09 根目录迁移 Windows 会话检查\n");
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Runtime? runtime = null;
        var result = 1;
        app.Startup += async (_, _) =>
        {
            try
            {
                var transfer = new CancelSecondTransfer();
                var shell = new WindowsFolderShell(Path.Combine(fixture, "隔离桌面"));
                Directory.CreateDirectory(shell.DesktopDirectory);
                var store = new JsonWorkspaceStore(Path.Combine(fixture, "状态"));
                IDesktopWorkspace workspace = new DesktopWorkspace(store, new NoStartup(), shell, transfer);
                await workspace.InitializeAsync(WindowsDesktop.Displays());
                await workspace.SelectRootAsync(Path.Combine(fixture, "旧根"));
                await workspace.CreateFolderAsync("活动");
                var active = workspace.Snapshot.Folders.Single();
                File.WriteAllText(Path.Combine(active.ActualPath, "内容.txt"), "迁移字节");
                await workspace.CreateFolderAsync("保留");
                var retained = workspace.Snapshot.Folders.Single(folder => folder.Folder.Name == "保留");
                await workspace.DeleteFolderAsync(retained.Folder.Id, FolderDeleteChoice.KeepContents);
                runtime = new Runtime(workspace);
                Check(runtime.Headers[active.Folder.Id].Host != IntPtr.Zero, "真实 Explorer 桌面头部可见");
                runtime.ShowSettings();
                var settings = app.Windows.OfType<SettingsWindow>().Single();
                settings.RootInput.Text = Path.Combine(fixture, "新根");
                settings.MigrateButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var dialog = runtime.ActiveRootMigration!;
                Check(dialog.IsVisible && dialog.CancelButton.IsEnabled, "设置按钮打开可取消迁移窗口");
                Check((await settings.PendingMigration!)?.Succeeded == true, "实际设置按钮完整迁移成功");
                Check(settings.RootInput.Text == workspace.Snapshot.Root && runtime.Headers[active.Folder.Id].Record.Id == active.Folder.Id
                    && File.ReadAllText(Path.Combine(workspace.Snapshot.Folders.Single().ActualPath, "内容.txt")) == "迁移字节", "设置新根、桌面标识和真实字节一致");
                Check(shell.ShortcutTargets(Path.Combine(shell.DesktopDirectory, "保留.lnk"), Path.Combine(fixture, "新根", "保留")), "设置迁移更新真实工具链接");
                SavePreview(dialog, "09-迁移成功.png");
                dialog.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                transfer.PauseSecond = true;
                settings.RootInput.Text = Path.Combine(fixture, "取消目标");
                settings.MigrateButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                dialog = runtime.ActiveRootMigration!;
                await transfer.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20));
                await Task.Delay(80);
                Check(runtime.Migrating && dialog.StatusText.Contains("1 / 2", StringComparison.Ordinal), "迁移中进度可见且业务入口暂停");
                Check(runtime.BeginInteraction(active.Folder.Id) == null && runtime.CreateAppearance(active.Folder.Id) == null, "迁移中拒绝桌面布局和外观操作");
                dialog.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check((await settings.PendingMigration!)?.Outcome == Outcome.Cancelled, "实际取消按钮完成当场恢复");
                Check(workspace.Snapshot.Root == Path.Combine(fixture, "新根") && !workspace.Snapshot.RecoveryRequired
                    && File.ReadAllText(Path.Combine(workspace.Snapshot.Folders.Single().ActualPath, "内容.txt")) == "迁移字节"
                    && shell.ShortcutTargets(Path.Combine(shell.DesktopDirectory, "保留.lnk"), Path.Combine(fixture, "新根", "保留")), "取消保留旧配置、原字节及正确链接");
                SavePreview(dialog, "09-取消恢复.png");
                dialog.Close();
                result = 0;
                File.AppendAllText(log, "全部检查通过。\n");
            }
            catch (Exception error) { File.AppendAllText(log, "失败：" + error + "\n"); }
            finally
            {
                runtime?.Dispose();
                try
                {
                    var full = Path.GetFullPath(fixture);
                    if (!full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                        || !Path.GetFileName(full).StartsWith("Kage-migration-", StringComparison.Ordinal)) throw new IOException("拒绝清理隔离范围之外的目录。");
                    Directory.Delete(full, true);
                }
                catch (Exception error) { result = 1; File.AppendAllText(log, "清理失败：" + error + "\n"); }
                app.Shutdown();
            }
        };
        app.Run();
        return result;

        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            File.AppendAllText(log, "通过：" + message + "\n");
        }
        void SavePreview(Window window, string name)
        {
            var visual = (FrameworkElement)window.Content;
            visual.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth), (int)Math.Ceiling(visual.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            var drawing = new DrawingVisual();
            using (var context = drawing.RenderOpen())
            {
                var bounds = new Rect(0, 0, visual.ActualWidth, visual.ActualHeight);
                context.DrawRectangle(Brushes.White, null, bounds);
                context.DrawRectangle(new VisualBrush(visual), null, bounds);
            }
            bitmap.Render(drawing);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(evidence, name));
            png.Save(file);
        }
    }

    private sealed class CancelSecondTransfer : IRootDirectoryTransfer
    {
        private readonly WindowsDirectoryTransfer real = new();
        internal bool PauseSecond { get; set; }
        internal TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Move(string source, string destination, CancellationToken cancellation)
        {
            if (PauseSecond && Path.GetFileName(source) == "保留")
            {
                Reached.TrySetResult();
                if (!cancellation.WaitHandle.WaitOne(TimeSpan.FromSeconds(20))) throw new TimeoutException("未收到设置窗口取消操作。");
                cancellation.ThrowIfCancellationRequested();
            }
            real.Move(source, destination, cancellation);
        }
        public void Restore(string source, string destination, CancellationToken cancellation) => real.Restore(source, destination, cancellation);
    }

    private sealed class NoStartup : IStartupRegistration
    {
        public string LaunchCommand => "隔离检查，不注册自启";
        public string? ReadCommand() => null;
        public void WriteCommand(string? command) => throw new InvalidOperationException("本检查不得修改自启。");
    }
}
