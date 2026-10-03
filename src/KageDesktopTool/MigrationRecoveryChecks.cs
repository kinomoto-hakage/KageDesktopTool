using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kage.Workspace;

namespace Kage.Desktop;

// 真实 Windows 设置、Explorer 定位及恢复按钮；指定目录故障仅用于固定未完成状态。
internal static class MigrationRecoveryChecks
{
    internal static int Run()
    {
        var fixture = Path.Combine(Path.GetTempPath(), "Kage-recovery-" + Guid.NewGuid().ToString("N"));
        var evidence = Path.Combine(Environment.CurrentDirectory, ".scratch", "desktop-folder", "verification");
        Directory.CreateDirectory(fixture);
        Directory.CreateDirectory(evidence);
        var log = Path.Combine(evidence, "migration-recovery-session.txt");
        File.WriteAllText(log, "10 中断迁移恢复 Windows 会话检查\n");
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Runtime? runtime = null;
        string? openedPath = null;
        var result = 1;
        app.Startup += async (_, _) =>
        {
            try
            {
                var shell = new WindowsFolderShell(Path.Combine(fixture, "隔离桌面"));
                Directory.CreateDirectory(shell.DesktopDirectory);
                var store = new JsonWorkspaceStore(Path.Combine(fixture, "状态"));
                var transfer = new FailedRestore();
                IDesktopWorkspace workspace = new DesktopWorkspace(store, new NoStartup(), shell, transfer);
                await workspace.InitializeAsync(WindowsDesktop.Displays());
                await workspace.SelectRootAsync(Path.Combine(fixture, "旧根"));
                await workspace.CreateFolderAsync("活动");
                var active = workspace.Snapshot.Folders.Single();
                File.WriteAllBytes(Path.Combine(active.ActualPath, "字节.bin"), [0, 255, 10]);
                await workspace.CreateFolderAsync("保留");
                var retained = workspace.Snapshot.Folders.Single(item => item.Folder.Name == "保留");
                File.WriteAllText(Path.Combine(retained.ActualPath, "内容.txt"), "保留内容恢复");
                await workspace.DeleteFolderAsync(retained.Folder.Id, FolderDeleteChoice.KeepContents);
                using var cancellation = new CancellationTokenSource();
                var failed = await workspace.MigrateRootAsync(Path.Combine(fixture, "新根"), new Observer(update =>
                {
                    if (update.Completed == update.Total) cancellation.Cancel();
                }), cancellation.Token);
                Check(failed.Outcome == Outcome.RecoveryRequired, "真实目录及链接移到新位置，指定恢复故障保留记录");
                workspace = new DesktopWorkspace(store, new NoStartup(), shell, transfer);
                Check((await workspace.InitializeAsync(WindowsDesktop.Displays())).Outcome == Outcome.RecoveryRequired,
                    "重新打开先核对路径，保持恢复状态");
                runtime = new Runtime(workspace);
                Check(runtime.Headers[active.Folder.Id].Host != IntPtr.Zero, "真实 Explorer 桌面宿主可用");
                runtime.ShowSettings();
                var settings = app.Windows.OfType<SettingsWindow>().Single();
                settings.Height = 880;
                settings.Top = Math.Max(0, settings.Top - 160);
                await Task.Delay(150);
                Check(settings.RecoverMigrationButton.IsVisible && !settings.RootInput.IsEnabled && !settings.MigrateButton.IsEnabled,
                    "设置显示恢复入口，禁止更改迁移路径");
                Check(settings.RecoveryRows.Children.OfType<StackPanel>().Count() == 2
                    && settings.RecoveryRows.Children.OfType<TextBlock>().Single().Text.Contains("2 / 2", StringComparison.Ordinal),
                    "未完成清单覆盖活动和保留内容");
                SavePreview(settings, "10-未完成清单.png");
                var row = settings.RecoveryRows.Children.OfType<StackPanel>().First();
                openedPath = workspace.Snapshot.MigrationRecovery.First().Item.DestinationPath;
                row.Children.OfType<Button>().Single(button => Equals(button.Content, "打开迁移位置"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var opened = false;
                for (var attempt = 0; attempt < 35 && !opened; attempt++)
                {
                    await Task.Delay(100);
                    opened = FindExplorer(openedPath, close: false);
                }
                Check(opened, "实际定位按钮打开 Explorer 中的现有内容目录");
                Check(FindExplorer(openedPath, close: true), "仅关闭本次隔离定位窗口");
                openedPath = null;
                settings.RecoverMigrationButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var dialog = runtime.ActiveRootMigration!;
                Check(dialog.IsVisible, "实际按钮打开恢复进度窗口");
                Check((await settings.PendingRecovery!)?.Outcome == Outcome.RecoveryRequired, "恢复再次失败显示可重试结果");
                dialog.Close();
                transfer.Fail = false;
                settings.RecoverMigrationButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                dialog = runtime.ActiveRootMigration!;
                Check((await settings.PendingRecovery!)?.Succeeded == true, "实际重试按钮恢复全部内容及链接");
                Check(!workspace.Snapshot.RecoveryRequired && workspace.Snapshot.RootMigration == null
                    && settings.RecoverMigrationButton.Visibility == Visibility.Collapsed && settings.RootInput.IsEnabled
                    && runtime.Headers.Count == 1 && runtime.Headers[active.Folder.Id].Record == active.Folder,
                    "恢复后解除暂停，保留内容入口不复活，桌面记录不变");
                Check(File.ReadAllBytes(Path.Combine(active.ActualPath, "字节.bin")).SequenceEqual(new byte[] { 0, 255, 10 })
                    && File.ReadAllText(Path.Combine(retained.ActualPath, "内容.txt")) == "保留内容恢复"
                    && shell.ShortcutTargets(Path.Combine(shell.DesktopDirectory, "保留.lnk"), retained.ActualPath), "恢复真实字节及旧快捷方式目标");
                SavePreview(dialog, "10-恢复成功.png");
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
                    if (openedPath != null) FindExplorer(openedPath, close: true);
                    var full = Path.GetFullPath(fixture);
                    if (!full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                        || !Path.GetFileName(full).StartsWith("Kage-recovery-", StringComparison.Ordinal)) throw new IOException("拒绝清理隔离范围之外的目录。");
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

    private static bool FindExplorer(string path, bool close)
    {
        object? shell = null;
        object? windows = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application", true)!);
            dynamic automation = shell!;
            windows = automation.Windows();
            dynamic collection = windows;
            for (var index = 0; index < (int)collection.Count; index++)
            {
                object? window = null;
                try
                {
                    window = collection.Item(index);
                    dynamic explorer = window;
                    string location = explorer.LocationURL;
                    if (Uri.TryCreate(location, UriKind.Absolute, out var uri) && uri.IsFile
                        && string.Equals(Path.TrimEndingDirectorySeparator(uri.LocalPath), Path.TrimEndingDirectorySeparator(path), StringComparison.OrdinalIgnoreCase))
                    {
                        if (close) explorer.Quit();
                        return true;
                    }
                }
                finally { if (window != null) Marshal.FinalReleaseComObject(window); }
            }
            return false;
        }
        finally
        {
            if (windows != null) Marshal.FinalReleaseComObject(windows);
            if (shell != null) Marshal.FinalReleaseComObject(shell);
        }
    }

    sealed class FailedRestore : IRootDirectoryTransfer
    {
        private readonly WindowsDirectoryTransfer real = new();
        internal bool Fail { get; set; } = true;
        public void Move(string source, string destination, CancellationToken cancellation) => real.Move(source, destination, cancellation);
        public void Restore(string source, string destination, CancellationToken cancellation)
        {
            if (Fail) throw new IOException("隔离检查：指定目录暂不可恢复");
            real.Restore(source, destination, cancellation);
        }
    }

    sealed class Observer(Action<RootMigrationProgress> action) : IProgress<RootMigrationProgress>
    {
        public void Report(RootMigrationProgress update) => action(update);
    }

    sealed class NoStartup : IStartupRegistration
    {
        public string LaunchCommand => "隔离恢复检查，不注册自启";
        public string? ReadCommand() => null;
        public void WriteCommand(string? command) => throw new InvalidOperationException("本检查不得修改自启。");
    }
}
