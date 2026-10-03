using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kage.Workspace;

namespace Kage.Desktop;

// 真实 WPF、Shell 和桌面窗口共用正式输入入口，夹具仅使用随机临时目录。
internal static class ContentLayoutChecks
{
    internal static int Run()
    {
        var fixture = Path.Combine(Path.GetTempPath(), "Kage-content-layout-" + Guid.NewGuid().ToString("N"));
        var evidence = Path.Combine(Environment.CurrentDirectory, ".scratch", "desktop-folder", "verification");
        Directory.CreateDirectory(fixture);
        Directory.CreateDirectory(evidence);
        var log = Path.Combine(evidence, "content-layout-session.txt");
        File.WriteAllText(log, "05 真实内容及窗口布局检查\n");
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Runtime? runtime = null;
        var result = 1;
        app.Startup += async (_, _) =>
        {
            try
            {
                var store = new JsonWorkspaceStore(Path.Combine(fixture, "状态"));
                IDesktopWorkspace workspace = new DesktopWorkspace(store, new NoStartup());
                var displays = WindowsDesktop.Displays();
                Check((await workspace.InitializeAsync(displays)).Succeeded, "隔离工作区初始化");
                await workspace.SelectRootAsync(Path.Combine(fixture, "内容"));
                await workspace.CreateFolderAsync("真实内容");
                await workspace.CreateFolderAsync("同时展开");
                var folder = workspace.Snapshot.Folders.First();
                var path = folder.ActualPath;
                File.WriteAllText(Path.Combine(path, "短.txt"), "真实内容");
                File.WriteAllText(Path.Combine(path, "很长的文件名称用于核对系统网格换行和列表省略的实际排列结果.txt"), "真实内容");
                File.WriteAllBytes(Path.Combine(path, "图片.png"), Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jX1sAAAAASUVORK5CYII="));
                Directory.CreateDirectory(Path.Combine(path, "普通子文件夹"));
                var marker = Path.Combine(fixture, "打开结果.txt");
                var link = Path.Combine(path, "真实快捷方式.lnk");
                MakeShortcut(link, marker);
                await workspace.RefreshAsync(displays);
                runtime = new Runtime(workspace);
                var first = runtime.Headers[folder.Folder.Id];
                var second = runtime.Headers.Values.Single(h => h != first);
                Check(runtime.Headers.Values.All(h => h.Host != IntPtr.Zero && WindowsDesktop.IsWindowVisible(h.Handle)), "真实桌面宿主及全部窗口可见");
                await first.ToggleAsync();
                await second.ToggleAsync();
                Check(runtime.Headers.Values.All(h => h.Record.Expanded), "多个真实窗口同时展开");
                CheckWindows();
                await first.Contents.IconsLoaded.WaitAsync(TimeSpan.FromSeconds(15));
                Arrange(first);
                var list = first.Contents.Items;
                var metrics = NativeViewMetrics.ForScale(VisualTreeHelper.GetDpi(first).DpiScaleX);
                Check(list.Items.Count == 5 && workspace.Snapshot.Folders.First().FileCount == 4, "真实文件、快捷方式和普通子文件夹计数正确");
                Check(list.Items.Cast<ListBoxItem>().All(item => Image(item).Source != null), "实际路径 Shell 图标和快捷方式覆盖加载成功");
                foreach (ListBoxItem item in list.Items)
                {
                    var image = Image(item);
                    var text = (TextBlock)((Grid)item.Content).Children[1];
                    Check(image.Width == 48 && image.Height == 48 && item.ActualWidth >= 60 && item.ActualHeight >= 48 + 2 * metrics.FontSize,
                        "默认大 48 DIP 图标及两行文字拥有足够完整格子");
                    Check(Math.Abs(image.TranslatePoint(new Point(image.ActualWidth / 2, 0), item).X - item.ActualWidth / 2) < .6, "图标位于完整格子中心");
                    Check(Math.Abs(text.TranslatePoint(new Point(text.ActualWidth / 2, 0), item).X - item.ActualWidth / 2) < .6 && text.TextAlignment == TextAlignment.Center && text.TextWrapping == TextWrapping.Wrap, "文件名居中且长名称换行");
                    Check(text.FontFamily.Source == SystemFonts.IconFontFamily.Source && text.FontSize == SystemFonts.IconFontSize, "系统标题字体");
                }
                var cells = list.Items.Cast<ListBoxItem>().ToArray();
                var firstRow = cells.Where(item => Math.Abs(item.TranslatePoint(new Point(), list).Y - cells[0].TranslatePoint(new Point(), list).Y) < .6).ToArray();
                for (var i = 1; i < firstRow.Length; i++)
                    Check(Math.Abs(firstRow[i].TranslatePoint(new Point(), list).X - firstRow[i - 1].TranslatePoint(new Point(), list).X - firstRow[i - 1].ActualWidth) < .6, "网格项间距一致且不重叠");
                cells[0].IsSelected = true;
                Arrange(first);
                Render(first, "05-网格.png");

                await workspace.SetViewAsync(first.FolderId, false);
                runtime.Render();
                await first.Contents.IconsLoaded.WaitAsync(TimeSpan.FromSeconds(15));
                Arrange(first);
                var row = (ListBoxItem)list.Items[0];
                var left = Image(row).TranslatePoint(new Point(), first.Surface).X;
                var textLeft = ((TextBlock)((Grid)row.Content).Children[1]).TranslatePoint(new Point(), first.Surface).X;
                var originalWidth = row.ActualWidth;
                Check(double.IsNaN(row.Width) && Image(row).Width == 32 && row.ActualHeight >= 34, "默认中 32 DIP 列表行高容纳图像，行宽由视口计算");
                var rows = list.Items.Cast<ListBoxItem>().ToArray();
                Check(Math.Abs(rows[1].TranslatePoint(new Point(), list).Y - rows[0].TranslatePoint(new Point(), list).Y - rows[0].ActualHeight) < .6, "中图标列表相邻行连续且不重叠");
                row.IsSelected = true;
                Render(first, "05-列表.png");
                foreach (var width in new[] { 460.0, 260.0, 480.0, 300.0 })
                {
                    var edit = runtime.BeginInteraction(first.FolderId)!;
                    Check(edit.ResizeBy(width - first.Record.HeaderWidth, 0), "真实窗口调整宽度成功");
                    runtime.Preview(edit);
                    Arrange(first);
                    Check(Math.Abs(Image(row).TranslatePoint(new Point(), first.Surface).X - left) < .6, "反复加宽缩窄图标零左侧偏移");
                    Check(Math.Abs(((TextBlock)((Grid)row.Content).Children[1]).TranslatePoint(new Point(), first.Surface).X - textLeft) < .6, "反复加宽缩窄文件名零左侧偏移");
                    Check(Math.Abs(row.ActualWidth - originalWidth - (width - 300)) < 1, "列表右侧随视口伸缩");
                    CheckWindows();
                    await runtime.CommitInteractionAsync(edit);
                }
                Render(first, "05-列表恢复.png");

                // 用真实 HWND 边界检查受阻、反向和丢失捕获；输入入口与鼠标事件相同。
                first.BeginHeaderDrag(new Point(0, 0));
                first.DragHeaderTo(new Point(-3000, 0));
                WindowsDesktop.GetWindowRect(first.Handle, out var atEdge);
                first.DragHeaderTo(new Point(-6000, 0));
                first.DragHeaderTo(new Point(-5999, 0));
                WindowsDesktop.GetWindowRect(first.Handle, out var reversed);
                Check(reversed.Left == atEdge.Left + 1, $"实际窗口受阻后立即反向一屏幕像素：{atEdge.Left} → {reversed.Left}，记录 {first.Record.X}，宿主 {first.Host}");
                CheckWindows();
                Check(first.HeaderInput.CaptureMouse(), "真实头部取得鼠标捕获");
                first.HeaderInput.ReleaseMouseCapture();
                await WaitUntil(() => !runtime.Interacting);
                WindowsDesktop.GetWindowRect(first.Handle, out var ended);
                first.DragHeaderTo(new Point(200, 200));
                WindowsDesktop.GetWindowRect(first.Handle, out var ignored);
                Check(ended.Left == ignored.Left && ended.Top == ignored.Top, "丢失鼠标捕获结束拖动并忽略后续轨迹");

                var linkItem = list.Items.Cast<ListBoxItem>().Single(item => (string)item.Tag == link);
                Runtime.Open((string)linkItem.Tag);
                await WaitUntil(() => File.Exists(marker));
                Check(File.ReadAllText(marker).Contains("Windows Shell", StringComparison.Ordinal), "Shell 路径打开快捷方式目标；真实双击另由 content-input-check 覆盖");
                var directoryItem = list.Items.Cast<ListBoxItem>().Single(item => (string)item.Tag == Path.Combine(path, "普通子文件夹"));
                list.SelectedItem = directoryItem;
                Runtime.Open((string)directoryItem.Tag);
                await WaitUntil(() => CloseFixtureExplorer((string)directoryItem.Tag, log));
                Check(true, "Shell 路径在资源管理器打开真实子目录，检查后关闭该夹具窗口");
                File.Move(Path.Combine(path, "短.txt"), Path.Combine(path, "外部重命名.txt"));
                File.Delete(Path.Combine(path, "图片.png"));
                File.WriteAllText(Path.Combine(path, "外部新增.txt"), "新增");
                await WaitUntil(() => workspace.Snapshot.Folders.First().Entries.Any(e => e.Name == "外部新增.txt") && list.Items.Cast<ListBoxItem>().Any(item => ((string)item.Tag).EndsWith("外部新增.txt", StringComparison.Ordinal)));
                Check(!list.Items.Cast<ListBoxItem>().Any(item => ((string)item.Tag).EndsWith("短.txt", StringComparison.Ordinal) || ((string)item.Tag).EndsWith("图片.png", StringComparison.Ordinal)), "正式自动刷新反映外部新增删除重命名并移除失效图标");
                Directory.Move(path, path + "-失联");
                await WaitUntil(() => workspace.Snapshot.Folders.First().FileCount == null && list.Items.Count == 0);
                Check(!Directory.Exists(path) && workspace.Snapshot.Folders.First().Notice != null, "失联保留记录和明确说明，不创建空目录");
                var saved = workspace.Snapshot.Folders.Select(f => f.Folder).ToArray();
                var restarted = new DesktopWorkspace(store, new NoStartup());
                await restarted.InitializeAsync(displays);
                Check(restarted.Snapshot.Folders.Select(f => f.Folder).SequenceEqual(saved), "重启恢复窗口布局、尺寸、展开和查看方式");
                result = 0;

                void CheckWindows()
                {
                    var windows = runtime.Headers.Values.Where(h => WindowsDesktop.IsWindowVisible(h.Handle)).ToArray();
                    foreach (var window in windows)
                    {
                        WindowsDesktop.GetWindowRect(window.Handle, out var bounds);
                        Check(displays.Any(d => bounds.Left >= d.X && bounds.Top >= d.Y && bounds.Right <= d.X + d.Width && bounds.Bottom <= d.Y + d.Height), "真实窗口在工作区域内");
                    }
                    for (var i = 0; i < windows.Length; i++)
                    for (var j = i + 1; j < windows.Length; j++)
                    {
                        WindowsDesktop.GetWindowRect(windows[i].Handle, out var a);
                        WindowsDesktop.GetWindowRect(windows[j].Handle, out var b);
                        Check(a.Right <= b.Left || b.Right <= a.Left || a.Bottom <= b.Top || b.Bottom <= a.Top, "实际展开窗口不重叠");
                    }
                }
            }
            catch (Exception e) { File.AppendAllText(log, "失败：" + e + "\n"); }
            finally { runtime?.Dispose(); app.Shutdown(); }
        };
        try { app.Run(); }
        finally
        {
            var full = Path.GetFullPath(fixture);
            if (!full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("Kage-content-layout-", StringComparison.Ordinal))
                throw new IOException("拒绝清理隔离目录范围之外的路径。");
            Directory.Delete(full, true);
        }
        return result;

        void Check(bool condition, string description)
        {
            if (!condition) throw new Exception(description);
            File.AppendAllText(log, "通过：" + description + "\n");
        }
        void Render(FolderHeader window, string name)
        {
            var image = new RenderTargetBitmap((int)window.Width, (int)window.Height, 96, 96, PixelFormats.Pbgra32);
            image.Render(window.Surface);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var output = File.Create(Path.Combine(evidence, name));
            encoder.Save(output);
        }
    }

    private static Image Image(ListBoxItem item) => (Image)((Grid)item.Content).Children[0];
    private static void Arrange(FolderHeader window)
    {
        window.Surface.Measure(new Size(window.Width, window.Height));
        window.Surface.Arrange(new Rect(0, 0, window.Width, window.Height));
        window.Surface.UpdateLayout();
    }
    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(12);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(50);
        }
        throw new TimeoutException("实际会话操作未在时限内完成。");
    }

    private static void MakeShortcut(string path, string marker)
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        dynamic shortcut = shell.CreateShortcut(path);
        try
        {
            shortcut.TargetPath = Path.Combine(AppContext.BaseDirectory, "KageDesktopTool.exe");
            shortcut.Arguments = "--open-target-check \"" + marker + "\"";
            shortcut.IconLocation = Path.Combine(AppContext.BaseDirectory, "Assets", "app-d.ico");
            shortcut.Save();
        }
        finally { Marshal.FinalReleaseComObject(shortcut); Marshal.FinalReleaseComObject(shell); }
    }

    private static bool CloseFixtureExplorer(string expectedPath, string log)
    {
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
                    if (Uri.TryCreate(location, UriKind.Absolute, out var uri) && uri.IsFile
                        && string.Equals(Path.GetFullPath(uri.LocalPath), Path.GetFullPath(expectedPath), StringComparison.OrdinalIgnoreCase))
                    {
                        window.Quit();
                        return true;
                    }
                }
                finally { Marshal.FinalReleaseComObject(window); }
            }
            return false;
        }
        finally { Marshal.FinalReleaseComObject(windows); Marshal.FinalReleaseComObject(shell); }
    }

    private sealed class NoStartup : IStartupRegistration
    {
        public string LaunchCommand => "隔离检查不注册自启";
        public string? ReadCommand() => null;
        public void WriteCommand(string? command) => throw new InvalidOperationException("内容布局检查不应修改自启。");
    }
}
