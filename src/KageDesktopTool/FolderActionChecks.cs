using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kage.Workspace;

namespace Kage.Desktop;

// 真实桌面头部菜单、WPF 操作窗口和 Windows Shell；只使用随机隔离夹具。
internal static class FolderActionChecks
{
    internal static int Run()
    {
        var fixture = Path.Combine(Path.GetTempPath(), "Kage-actions-" + Guid.NewGuid().ToString("N"));
        var evidence = Path.Combine(Environment.CurrentDirectory, ".scratch", "desktop-folder", "verification");
        Directory.CreateDirectory(fixture);
        Directory.CreateDirectory(evidence);
        var log = Path.Combine(evidence, "folder-actions-session.txt");
        File.WriteAllText(log, "08 重命名及删除 Windows 会话检查\n");
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Runtime? runtime = null;
        FolderSnapshot? recycleFixture = null;
        var shell = new WindowsFolderShell();
        string? desktopLink = null;
        var result = 1;
        app.Startup += async (_, _) =>
        {
            try
            {
                var store = new JsonWorkspaceStore(Path.Combine(fixture, "状态"));
                IDesktopWorkspace workspace = new DesktopWorkspace(store, new NoStartup(), shell);
                var displays = WindowsDesktop.Displays();
                Check((await workspace.InitializeAsync(displays)).Succeeded, "隔离工作区初始化");
                await workspace.SelectRootAsync(Path.Combine(fixture, "内容"));
                var unique = "Kage-08-" + Guid.NewGuid().ToString("N");
                await workspace.CreateFolderAsync(unique);
                var original = workspace.Snapshot.Folders.Single();
                File.WriteAllText(Path.Combine(original.ActualPath, "实际内容.txt"), "完整保留");
                await workspace.ToggleFolderAsync(original.Folder.Id);
                runtime = new Runtime(workspace);
                var header = runtime.Headers[original.Folder.Id];
                Check(header.Host != IntPtr.Zero && WindowsDesktop.IsWindowVisible(header.Handle), "实际桌面头部可见");
                OpenMenu(header, "重命名…");
                var dialog = runtime.ActiveFolderAction!;
                await Task.Delay(200);
                Check(dialog.IsVisible && dialog.NameInput.IsKeyboardFocused, "头部菜单打开实际改名窗口并聚焦输入");
                dialog.NameInput.Text = "NUL";
                dialog.RenameButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check((await dialog.Pending!).Outcome == Outcome.Failed && header.Record.Name == unique, "实际输入拒绝设备名称并保留头部");
                dialog.NameInput.Text = unique + "-改名后";
                dialog.RenameButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check((await dialog.Pending!).Succeeded, "实际按钮同步改名真实目录");
                var changed = workspace.Snapshot.Folders.Single();
                Check(changed.Folder.Id == original.Folder.Id && changed.Folder.Color == original.Folder.Color
                    && changed.Folder.X == original.Folder.X && changed.Folder.Y == original.Folder.Y
                    && header.Record.Name == changed.Folder.Name && !Directory.Exists(original.ActualPath), "头部名称标识位置及外观一致");
                SavePreview(dialog, "08-重命名结果.png");
                dialog.Close();
                File.WriteAllText(Path.Combine(changed.ActualPath, "外部变化.txt"), "改名后新增");
                await workspace.RefreshAsync(displays);
                runtime.Render();
                Check(workspace.Snapshot.Folders.Single().FileCount == 2 && header.Contents.Items.Items.Count == 2, "改名后真实展示和文件数继续刷新");
                OpenMenu(header, "删除…");
                dialog = runtime.ActiveFolderAction!;
                await Task.Delay(100);
                Check(dialog.KeepButton.IsVisible && dialog.RecycleButton.IsVisible && dialog.CancelButton.IsVisible, "明确展示两种删除方式及取消");
                SavePreview(dialog, "08-删除选择.png");
                dialog.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(workspace.Snapshot.Folders.Count == 1 && Directory.Exists(changed.ActualPath), "取消不移除入口和内容");
                OpenMenu(header, "删除…");
                dialog = runtime.ActiveFolderAction!;
                desktopLink = Path.Combine(shell.DesktopDirectory, changed.Folder.Name + ".lnk");
                Check(!File.Exists(desktopLink), "实际桌面随机夹具名称无冲突");
                dialog.KeepButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check((await dialog.Pending!).Succeeded && !runtime.Headers.ContainsKey(changed.Folder.Id), "实际桌面快捷方式成功后才移除头部");
                Check(shell.ShortcutTargets(desktopLink, changed.ActualPath)
                    && File.ReadAllText(Path.Combine(changed.ActualPath, "实际内容.txt")) == "完整保留", "实际用户桌面快捷方式指向原目录，字节未移动");
                dialog.Close();
                runtime.Dispose();
                workspace = new DesktopWorkspace(store, new NoStartup(), shell);
                await workspace.InitializeAsync(displays);
                Check(workspace.Snapshot.Folders.Count == 0, "重启不复活保留内容入口");
                await workspace.CreateFolderAsync("临时整目录回收");
                recycleFixture = workspace.Snapshot.Folders.Single();
                Directory.CreateDirectory(Path.Combine(recycleFixture.ActualPath, "子目录"));
                File.WriteAllText(Path.Combine(recycleFixture.ActualPath, "子目录", "保留.txt"), "回收字节");
                runtime = new Runtime(workspace);
                OpenMenu(runtime.Headers[recycleFixture.Folder.Id], "删除…");
                dialog = runtime.ActiveFolderAction!;
                dialog.RecycleButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check((await dialog.Pending!).Succeeded && workspace.Snapshot.Folders.Count == 0, "实际按钮将整个内容目录回收后移除头部");
                var recycled = shell.FindRecycledFolder(recycleFixture.ActualPath, recycleFixture.Folder.Id);
                Check(recycled != null && File.ReadAllText(Path.Combine(recycled, "子目录", "保留.txt")) == "回收字节", "原生回收站内完整子目录字节保留");
                dialog.Close();
                result = 0;
                File.AppendAllText(log, "全部检查通过。\n");
            }
            catch (Exception e) { File.AppendAllText(log, "失败：" + e + "\n"); }
            finally
            {
                try
                {
                    runtime?.Dispose();
                    if (desktopLink != null && Path.GetFileName(desktopLink).StartsWith("Kage-08-", StringComparison.Ordinal)
                        && string.Equals(Path.GetDirectoryName(desktopLink), shell.DesktopDirectory, StringComparison.OrdinalIgnoreCase) && File.Exists(desktopLink)) File.Delete(desktopLink);
                    if (recycleFixture != null)
                    {
                        var recycled = shell.FindRecycledFolder(recycleFixture.ActualPath, recycleFixture.Folder.Id);
                        if (recycled != null)
                        {
                            var full = Path.GetFullPath(recycled);
                            Check(full.Contains("\\$Recycle.Bin\\", StringComparison.OrdinalIgnoreCase) && Path.GetFileName(full).StartsWith("$R", StringComparison.Ordinal)
                                && recycleFixture.ActualPath.StartsWith(fixture + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "仅恢复本次随机回收夹具");
                            Directory.Move(full, recycleFixture.ActualPath);
                            var metadata = Path.Combine(Path.GetDirectoryName(full)!, "$I" + Path.GetFileName(full)[2..]);
                            if (File.Exists(metadata)) File.Delete(metadata);
                        }
                    }
                    var resolved = Path.GetFullPath(fixture);
                    if (resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                        && Path.GetFileName(resolved).StartsWith("Kage-actions-", StringComparison.Ordinal)) Directory.Delete(resolved, true);
                }
                catch (Exception e) { result = 1; File.AppendAllText(log, "清理失败：" + e + "\n"); }
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

    private static void OpenMenu(FolderHeader header, string label)
        => header.ContextMenu.Items.OfType<MenuItem>().Single(item => (string)item.Header == label).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

    private sealed class NoStartup : IStartupRegistration
    {
        public string LaunchCommand => "隔离检查，不注册自启";
        public string? ReadCommand() => null;
        public void WriteCommand(string? command) => throw new InvalidOperationException("本检查不得修改自启。");
    }
}
