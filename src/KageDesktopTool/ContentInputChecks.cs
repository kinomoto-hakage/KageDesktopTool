using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kage.Workspace;

namespace Kage.Desktop;

// 不注入 WPF 事件：经过系统鼠标输入、命中与 Preview 路由验收正式窗口。
internal static class ContentInputChecks
{
    internal static int Run(bool identityOnly = false, bool refreshOnly = false)
    {
        var fixture = Path.Combine(Path.GetTempPath(), "Kage-input-" + Guid.NewGuid().ToString("N"));
        var evidence = Path.Combine(Environment.CurrentDirectory, ".scratch", "desktop-folder", "verification");
        Directory.CreateDirectory(fixture);
        Directory.CreateDirectory(evidence);
        var log = Path.Combine(evidence, refreshOnly ? "content-refresh-session.txt" : identityOnly ? "content-identity-session.txt" : "content-input-session.txt");
        File.WriteAllText(log, "真实 Windows 内容输入验收\n");
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Runtime? runtime = null;
        var exit = 1;
        var desktopShown = false;
        app.Startup += async (_, _) =>
        {
            try
            {
                IDesktopWorkspace workspace = new DesktopWorkspace(new JsonWorkspaceStore(Path.Combine(fixture, "state")), new NoStartup());
                var displays = WindowsDesktop.Displays();
                Require((await workspace.InitializeAsync(displays)).Succeeded, "隔离初始化");
                Require((await workspace.SelectRootAsync(Path.Combine(fixture, "content"))).Succeeded, "隔离真实内容目录");
                await workspace.CreateFolderAsync("输入验收");
                await workspace.CreateFolderAsync("移动目标");
                var folder = workspace.Snapshot.Folders.First();
                var marker = Path.Combine(fixture, "opened.txt");
                var link = Path.Combine(folder.ActualPath, "01-shortcut.lnk");
                Shortcut(link, marker);
                var command = Path.Combine(folder.ActualPath, "02-program.cmd");
                File.WriteAllText(command, "@echo off\r\necho opened>\"" + marker + "\"\r\n");
                var document = Path.Combine(folder.ActualPath, "03-document-" + Guid.NewGuid().ToString("N") + ".txt");
                File.WriteAllText(document, "用于默认关联打开检查的隔离文档。");
                var directory = Path.Combine(folder.ActualPath, "directory");
                Directory.CreateDirectory(directory);
                for (var i = 1; i <= 45; i++) File.WriteAllText(Path.Combine(folder.ActualPath, $"file{i}.txt"), "内容");
                await workspace.ToggleFolderAsync(folder.Folder.Id);
                await workspace.ToggleFolderAsync(workspace.Snapshot.Folders.Last().Folder.Id);
                await workspace.RefreshAsync(displays);
                runtime = new Runtime(workspace);
                var header = runtime.Headers[folder.Folder.Id];
                Require(header.Host != IntPtr.Zero, "真实 Explorer 宿主可见");
                await header.Contents.IconsLoaded;
                Desktop(); desktopShown = true;
                await Task.Delay(650);
                header.UpdateLayout();
                var contents = header.Contents;
                ListBoxItem Item(string path) => contents.Items.Items.Cast<ListBoxItem>().Single(item => (string)item.Tag == path);
                Point Center(FrameworkElement element) => element.PointToScreen(new Point(element.ActualWidth / 2, element.ActualHeight / 2));
                async Task Click(FrameworkElement element, bool twice = false)
                {
                    await Task.Delay((int)GetDoubleClickTime() + 100);
                    var point = Center(element);
                    CheckHit(header, point);
                    await MouseAt(point, twice);
                    await Task.Delay(550);
                }
                if (refreshOnly)
                {
                    await workspace.SetContentViewAsync(folder.Folder.Id, false, 16, ContentSortKey.Name, false);
                    runtime.Render(); await contents.IconsLoaded; header.UpdateLayout();
                    var originalPath = Path.Combine(folder.ActualPath, "file1.txt");
                    var heldPoint = Center((Image)((Grid)Item(originalPath).Content).Children[0]);
                    SetCursorPos((int)heldPoint.X, (int)heldPoint.Y); await Task.Delay(70);
                    mouse_event(2, 0, 0, 0, UIntPtr.Zero); await Task.Delay(120);
                    Require(contents.InputActive, "真实鼠标按下后内容输入会话活动");
                    var countBefore = contents.Items.Items.Count;
                    var incoming = Path.Combine(folder.ActualPath, "zz-refresh.txt");
                    File.WriteAllText(incoming, "刷新期间新增");
                    await workspace.RefreshAsync(displays); runtime.Render();
                    Require(contents.Items.Items.Count == countBefore && contents.SelectedPaths().SequenceEqual(new[] { originalPath }),
                        "已开始的刷新完成后不重建活动输入列表");
                    mouse_event(4, 0, 0, 0, UIntPtr.Zero); await Task.Delay(450);
                    Require(contents.Items.Items.Count == countBefore + 1 && contents.SelectedPaths().SequenceEqual(new[] { originalPath }),
                        "鼠标释放后发布延迟刷新并保留准确选择");
                    exit = 0; return;
                }
                if (identityOnly)
                {
                    await workspace.SetContentViewAsync(folder.Folder.Id, false, 16, ContentSortKey.Name, false);
                    runtime.Render(); await contents.IconsLoaded; header.UpdateLayout();
                    var originalPath = Path.Combine(folder.ActualPath, "file1.txt");
                    await Click((Image)((Grid)Item(originalPath).Content).Children[0]);
                    var aliasPath = Path.Combine(folder.ActualPath, "zz-alias.txt");
                    Require(CreateHardLink(aliasPath, originalPath, IntPtr.Zero), "真实同目录硬链接夹具");
                    await workspace.RefreshAsync(displays); runtime.Render();
                    Require(contents.SelectedPaths().SequenceEqual(new[] { originalPath }), "刷新不连带选择同身份的其他硬链接");
                    File.Delete(originalPath);
                    await workspace.RefreshAsync(displays); runtime.Render();
                    Require(contents.SelectedPaths().Length == 0, "删除选中的别名不将选择迁移到仍存在的别名");
                    exit = 0; return;
                }
                foreach (var grid in new[] { true, false })
                {
                    await workspace.SetViewAsync(folder.Folder.Id, grid); runtime.Render();
                    await contents.IconsLoaded; header.UpdateLayout();
                    foreach (var selected in new[] { false, true })
                    foreach (var image in new[] { true, false })
                    {
                        contents.Items.SelectedItems.Clear();
                        var item = Item(link);
                        var cell = (Grid)item.Content;
                        var hit = (FrameworkElement)cell.Children[image ? 0 : 1];
                        if (selected) await Click(hit);
                        if (File.Exists(marker)) File.Delete(marker);
                        await Click(hit, true);
                        await WaitUntil(() => File.Exists(marker));
                        Require(File.Exists(marker), $"{(grid ? "网格" : "列表")} {(selected ? "已选" : "未选")} {(image ? "图像" : "文字")}真实双击启动快捷方式目标");
                    }
                    if (File.Exists(marker)) File.Delete(marker);
                    await Click(((Grid)Item(command).Content).Children[0] as FrameworkElement ?? throw new Exception(), true);
                    await WaitUntil(() => File.Exists(marker));
                    Require(File.Exists(marker), "真实程序按关联启动");
                }
                await Click(((Grid)Item(document).Content).Children[1] as FrameworkElement ?? throw new Exception(), true);
                IntPtr documentWindow = IntPtr.Zero;
                for (var attempt = 0; attempt < 30 && documentWindow == IntPtr.Zero; attempt++)
                { documentWindow = WindowWithTitle(Path.GetFileName(document)); await Task.Delay(150); }
                Require(documentWindow != IntPtr.Zero, "普通文本文件真实双击按默认关联打开可辨认的文档窗口");
                SetForegroundWindow(documentWindow); await Task.Delay(150);
                keybd_event(0x11, 0, 0, UIntPtr.Zero); Key(0x57); keybd_event(0x11, 0, 2, UIntPtr.Zero); await Task.Delay(300);
                // 只关闭本次打开的隔离文档标签。
                Desktop(); await Task.Delay(300);
                await Click(((Grid)Item(directory).Content).Children[0] as FrameworkElement ?? throw new Exception(), true);
                IntPtr explorer = IntPtr.Zero;
                for (var attempt = 0; attempt < 30 && explorer == IntPtr.Zero; attempt++)
                { explorer = ExplorerAt(directory); await Task.Delay(150); }
                Require(explorer != IntPtr.Zero, "子目录真实双击进入 Explorer 实际路径");
                PostMessage(explorer, 0x10, IntPtr.Zero, IntPtr.Zero); await Task.Delay(400);

                // 原生菜单的窗口类区别于 WPF Folder 管理菜单；Esc 只取消本次菜单。
                var right = Center(Item(link));
                await MouseAt(right, false, 8);
                await Task.Delay(600);
                Require(FindWindow("#32768", null) != IntPtr.Zero, "图标右键呈现 Windows 原生完整菜单");
                Key(0x1B); await Task.Delay(500);
                Require(FindWindow("#32768", null) == IntPtr.Zero, "Esc 真实输入取消原生菜单");
                Require(contents.SelectedPaths().SequenceEqual(new[] { link }), "右键未选项目先切换当前选择");
                contents.Items.UpdateLayout();
                // 空白框选向上覆盖多行，真实修饰键修改集合。
                var blank = contents.Viewport.PointToScreen(new Point(contents.Viewport.ActualWidth - 5, contents.Viewport.ActualHeight - 20));
                var first = contents.Viewport.PointToScreen(new Point(2, 2));
                await Drag(blank, first);
                Render(header, Path.Combine(evidence, "content-box.png"));
                Require(contents.Items.SelectedItems.Count > 1, "空白处真实框选多项并在释放后保留");
                var count = contents.Items.SelectedItems.Count;
                keybd_event(0x11, 0, 0, UIntPtr.Zero);
                await Click(((Grid)Item(link).Content).Children[0] as FrameworkElement ?? throw new Exception());
                keybd_event(0x11, 0, 2, UIntPtr.Zero);
                Require(contents.Items.SelectedItems.Count == count - 1, "Ctrl 点击修改当前选择集合");
                File.Delete(marker);
                await MouseAt(blank, true);
                Require(!File.Exists(marker) && contents.Items.SelectedItems.Count == 0, "空白双击不打开旧选择");
                await Click(((Grid)Item(link).Content).Children[0] as FrameworkElement ?? throw new Exception());
                var shiftEnd = Path.Combine(folder.ActualPath, "file3.txt");
                keybd_event(0x10, 0, 0, UIntPtr.Zero);
                await Click(((Grid)Item(shiftEnd).Content).Children[0] as FrameworkElement ?? throw new Exception());
                keybd_event(0x10, 0, 2, UIntPtr.Zero);
                Require(contents.SelectedPaths().SequenceEqual(new[] { link, command, document,
                    Path.Combine(folder.ActualPath, "file1.txt"), Path.Combine(folder.ActualPath, "file2.txt"), shiftEnd }),
                    "Shift 真实点击选择连续可见顺序范围");
                contents.Items.SelectedItems.Clear(); Item(link).IsSelected = true;
                var selectionScroll = ContentPointerInput.Descendant<ScrollViewer>(contents.Items)!;
                selectionScroll.ScrollToVerticalOffset(200); contents.Items.UpdateLayout();
                var modifierEnd = contents.Viewport.PointToScreen(new Point(2, 30));
                keybd_event(0x10, 0, 0, UIntPtr.Zero);
                await Drag(blank, modifierEnd);
                keybd_event(0x10, 0, 2, UIntPtr.Zero);
                Require(contents.SelectedPaths().Contains(link) && contents.Items.SelectedItems.Count > 1, "Shift 框选扩展视口之外的原选择");
                keybd_event(0x11, 0, 0, UIntPtr.Zero);
                await Drag(blank, modifierEnd);
                keybd_event(0x11, 0, 2, UIntPtr.Zero);
                Require(contents.SelectedPaths().SequenceEqual(new[] { link }), "Ctrl 框选切换命中项目而不清除视口外选择");
                selectionScroll.ScrollToTop(); contents.Items.UpdateLayout();
                var boxStart = contents.Viewport.PointToScreen(new Point(contents.Viewport.ActualWidth - 5, 3));
                var boxEnd = contents.Viewport.PointToScreen(new Point(2, contents.Viewport.ActualHeight - 2));
                await Drag(boxStart, boxEnd, hold: 850);
                Require(selectionScroll.VerticalOffset > 0 && contents.Items.SelectedItems.Count > 10, "反向框选靠近边缘自动滚动并保留视口外命中");
                await Drag(boxStart, boxEnd, beforeRelease: () => ReleaseCapture());
                Require(!contents.InputActive, "Windows 捕获丢失结束选框会话");
                selectionScroll.ScrollToTop(); contents.Items.UpdateLayout();

                // 单项系统命令及已安装编辑器处理器来自实际 .txt 对象菜单。
                contents.Items.SelectedItems.Clear();
                await Click(((Grid)Item(document).Content).Children[0] as FrameworkElement ?? throw new Exception());
                await MouseAt(Center(Item(document)), false, 8); await Task.Delay(500);
                var labels = MenuLabels();
                File.AppendAllText(log, "实际原生菜单：" + string.Join(" | ", labels) + "\n");
                Require(labels.Any(label => label.Contains("Code", StringComparison.OrdinalIgnoreCase) || label.Contains("7-Zip", StringComparison.OrdinalIgnoreCase)), "已安装 Code／7-Zip 的适用对象菜单处理器呈现");
                Require(labels.Any(label => label.Contains("属性", StringComparison.Ordinal)) && labels.Any(label => label.Contains("删除", StringComparison.Ordinal)), "系统文件命令呈现");
                await ClickMenu("重命名"); await Task.Delay(350);
                Require(WindowWithTitle("重命名项目") != IntPtr.Zero, "原生重命名命令进入真实名称输入");
                await TypeText("renamed.txt");
                await Task.Delay(150);
                File.AppendAllText(log, "真实改名输入：" + ContentPointerInput.Descendant<TextBox>(app.Windows.OfType<ContentRenameDialog>().Single())?.Text + "\n");
                Key(0x0D); await Task.Delay(700);
                var renamedPath = Path.Combine(folder.ActualPath, "renamed.txt");
                Require(File.Exists(renamedPath) && !File.Exists(document), "原生菜单改名更新实际路径");
                Require(contents.SelectedPaths().SequenceEqual(new[] { renamedPath }), "改名刷新通过可靠身份保留选择");
                // 改名后的项目在自动排序末尾，先用小列表并滚动到当前对象。
                var renamedItem = Item(renamedPath);
                contents.Items.ScrollIntoView(renamedItem); contents.Items.UpdateLayout();
                await MouseAt(Center(renamedItem), false, 8); await Task.Delay(500);
                await ClickMenu("删除"); await Task.Delay(900);
                Require(!File.Exists(renamedPath), "原生删除命令执行实际文件结果");
                Require(!contents.SelectedPaths().Contains(renamedPath) && workspace.Snapshot.Folders.First().FileCount == 47,
                    "菜单结束刷新项目、文件计数并清理删除选择");
                ContentPointerInput.Descendant<ScrollViewer>(contents.Items)?.ScrollToTop(); contents.Items.UpdateLayout();

                // 同 Folder 的多选重排与真实跨 Folder 移动使用不同落点。
                await workspace.SetContentViewAsync(folder.Folder.Id, false, 16, ContentSortKey.Name, false);
                runtime.Render(); await contents.IconsLoaded; header.UpdateLayout();
                contents.Items.SelectedItems.Clear();
                await Click(((Grid)Item(link).Content).Children[0] as FrameworkElement ?? throw new Exception());
                keybd_event(0x11, 0, 0, UIntPtr.Zero);
                await Click(((Grid)Item(command).Content).Children[0] as FrameworkElement ?? throw new Exception());
                keybd_event(0x11, 0, 2, UIntPtr.Zero);
                var selectedBeforeMenu = contents.SelectedPaths();
                await MouseAt(Center(Item(link)), false, 8); await Task.Delay(400);
                Require(contents.SelectedPaths().SequenceEqual(selectedBeforeMenu) && ShellContextMenu.ActiveMenu != IntPtr.Zero,
                    "右键已选项目保留多项集合并取得原生集合菜单");
                Key(0x1B); await Task.Delay(350);
                var beforeOrder = workspace.Snapshot.Folders.First().Entries.Select(entry => entry.Name).ToArray();
                var origin = Center((Image)((Grid)Item(link).Content).Children[0]);
                await Drag(origin, new Point(origin.X + 1, origin.Y));
                Require(workspace.Snapshot.Folders.First().Folder.SortKey == ContentSortKey.Name
                    && workspace.Snapshot.Folders.First().Entries.Select(entry => entry.Name).SequenceEqual(beforeOrder), "未越过系统拖动阈值不改变排序");
                await Drag(origin, new Point(origin.X + 24, origin.Y));
                Require(workspace.Snapshot.Folders.First().Folder.SortKey == ContentSortKey.Name, "原位置实际释放不切换自定义排序");
                await Drag(origin, Item(directory).PointToScreen(new Point(12, 2)), cancel: true);
                foreach (var resultWindow in app.Windows.OfType<MoveDialog>().ToArray()) resultWindow.Close();
                Require(workspace.Snapshot.Folders.First().Folder.SortKey == ContentSortKey.Name && File.Exists(link), "Esc 真实输入取消内部拖动且不改变顺序或文件");
                // 普通点击会收拢多选，重新用 Ctrl 加入第二项。
                keybd_event(0x11, 0, 0, UIntPtr.Zero);
                await Click(((Grid)Item(command).Content).Children[0] as FrameworkElement ?? throw new Exception());
                keybd_event(0x11, 0, 2, UIntPtr.Zero);
                var insertion = Item(directory).PointToScreen(new Point(12, 2));
                await Drag(origin, insertion, beforeRelease: () => Require(contents.Feedback.Children.Count > 0, "真实内部拖动提供插入标记"));
                await WaitUntil(() => workspace.Snapshot.Folders.First().Folder.SortKey == ContentSortKey.Custom);
                foreach (var resultWindow in app.Windows.OfType<MoveDialog>().ToArray()) resultWindow.Close();
                Require(workspace.Snapshot.Folders.First().Entries.Take(3).Select(entry => entry.Name).SequenceEqual(
                    new[] { "01-shortcut.lnk", "02-program.cmd", "directory" }), "真实多选内部拖动紧凑重排并保持相对顺序");
                Require(File.Exists(link) && File.Exists(command), "内部重排不移动内容文件");
                var target = runtime.Headers[workspace.Snapshot.Folders.Last().Folder.Id];
                await Drag(Center((Image)((Grid)Item(link).Content).Children[0]), target.HeaderInput.PointToScreen(new Point(60, 20)));
                await WaitUntil(() => workspace.Snapshot.Folders.Last().FileCount == 2 && runtime.ActiveMove == null);
                foreach (var resultWindow in app.Windows.OfType<MoveDialog>().ToArray()) resultWindow.Close();
                Require(!File.Exists(link) && !File.Exists(command) && workspace.Snapshot.Folders.Last().FileCount == 2,
                    "真实多选跨 Folder 拖动只执行一次实际移动");

                var viewer = ContentPointerInput.Descendant<ScrollViewer>(contents.Items)!;
                viewer.ScrollToTop(); contents.Items.UpdateLayout();
                var wheel = contents.Viewport.PointToScreen(new Point(70, 70));
                SetCursorPos((int)wheel.X, (int)wheel.Y); await Task.Delay(70);
                mouse_event(0x800, 0, 0, unchecked((uint)-120), UIntPtr.Zero); await Task.Delay(250);
                Require(viewer.VerticalOffset > 0, "圆角滚动条保留真实鼠标滚轮");
                viewer.ScrollToTop(); contents.Items.UpdateLayout();
                var bar = ContentPointerInput.Descendant<ScrollBar>(contents.Items)!;
                var track = (Track)bar.Template.FindName("PART_Track", bar);
                var thumb = track.Thumb;
                var thumbStart = Center(thumb);
                await Drag(thumbStart, new Point(thumbStart.X, thumbStart.Y + 65));
                Require(viewer.VerticalOffset > 0, "圆角滑块实际拖动有效");
                viewer.ScrollToTop(); contents.Items.UpdateLayout();
                await MouseAt(bar.PointToScreen(new Point(bar.ActualWidth / 2, bar.ActualHeight * .75)), false);
                Require(viewer.VerticalOffset > 0, "圆角轨道实际点击有效且可命中");
                viewer.ScrollToTop(); contents.Items.UpdateLayout();

                foreach (var grid in new[] { true, false })
                foreach (var size in new[] { 16, 32, 48, 96 })
                {
                    await workspace.SetContentViewAsync(folder.Folder.Id, grid, size, ContentSortKey.Name, false);
                    runtime.Render(); await contents.IconsLoaded; header.UpdateLayout();
                    Require(contents.Items.Items.Cast<ListBoxItem>().All(item =>
                    {
                        var icon = (Image)((Grid)item.Content).Children[0];
                        return icon.Width == size && icon.Height == size && icon.Source is BitmapSource source
                            && source.PixelWidth >= Math.Ceiling(size * workspace.Snapshot.Folders.First().DisplayScale);
                    }), $"{(grid ? "网格" : "列表")} {size} DIP 原生图像与物理像素");
                    Render(header, Path.Combine(evidence, $"content-{(grid ? "grid" : "list")}-{size}.png"));
                }
                var movedLink = Path.Combine(workspace.Snapshot.Folders.Last().ActualPath, Path.GetFileName(link));
                var native = (BitmapSource)ShellIcons.ForFile(movedLink, true, 24, false)!;
                var clean = (BitmapSource)ShellIcons.ForFile(movedLink, true, 24)!;
                Require(!Pixels(native).SequenceEqual(Pixels(clean)), "仅在本工具内移除快捷方式箭头，Shell 原生图像保留箭头");
                var label = (TextBlock)((Grid)target.Contents.Items.Items.Cast<ListBoxItem>().Single(item => (string)item.Tag == movedLink).Content).Children[1];
                Require(label.Text == "01-shortcut" && File.Exists(movedLink), "显示标签隐藏后缀但实际路径保留");
                exit = 0;
            }
            catch (Exception e) { File.AppendAllText(log, "失败：" + e + "\n"); }
            finally
            {
                keybd_event(0x11, 0, 2, UIntPtr.Zero);
                keybd_event(0x10, 0, 2, UIntPtr.Zero);
                mouse_event(4, 0, 0, 0, UIntPtr.Zero);
                runtime?.Dispose();
                if (desktopShown) Desktop();
                app.Shutdown();
            }
            void Require(bool passed, string message)
            { File.AppendAllText(log, (passed ? "通过：" : "失败：") + message + "\n"); if (!passed) throw new Exception(message); }
        };
        app.Run();
        var full = Path.GetFullPath(fixture);
        if (!full.StartsWith(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(full).StartsWith("Kage-input-", StringComparison.Ordinal)) throw new IOException("拒绝清理隔离目录之外的内容。");
        Directory.Delete(full, true);
        return exit;
    }

    private static byte[] Pixels(BitmapSource source)
    { var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0); var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4]; converted.CopyPixels(pixels, converted.PixelWidth * 4, 0); return pixels; }
    private static void Render(FolderHeader header, string path)
    {
        var scale = VisualTreeHelper.GetDpi(header).DpiScaleX;
        var image = new RenderTargetBitmap((int)Math.Ceiling(header.ActualWidth * scale), (int)Math.Ceiling(header.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        image.Render(header); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(path); encoder.Save(file);
    }
    private static void CheckHit(FolderHeader header, Point point)
    {
        var hwnd = WindowsDesktop.WindowFromPoint(new WindowsDesktop.POINT { X = (int)point.X, Y = (int)point.Y });
        if (hwnd != header.Handle && !WindowsDesktop.IsChild(header.Handle, hwnd)) throw new IOException($"鼠标未命中夹具窗口：{point}，请先结束其他桌面覆盖窗口。");
    }
    private static async Task MouseAt(Point point, bool twice, uint down = 2)
    {
        SetCursorPos((int)point.X, (int)point.Y); await Task.Delay(70);
        for (var i = 0; i < (twice ? 2 : 1); i++)
        { mouse_event(down, 0, 0, 0, UIntPtr.Zero); await Task.Delay(35); mouse_event(down * 2, 0, 0, 0, UIntPtr.Zero); await Task.Delay(65); }
    }
    private static async Task Drag(Point start, Point end, Action? started = null, int hold = 0, Action? beforeRelease = null, bool cancel = false)
    {
        SetCursorPos((int)start.X, (int)start.Y); await Task.Delay(70); mouse_event(2, 0, 0, 0, UIntPtr.Zero); await Task.Delay(80);
        started?.Invoke();
        for (var i = 1; i <= 15; i++) { SetCursorPos((int)(start.X + (end.X - start.X) * i / 15), (int)(start.Y + (end.Y - start.Y) * i / 15)); await Task.Delay(25); }
        if (hold > 0) await Task.Delay(hold);
        beforeRelease?.Invoke();
        if (cancel) { Key(0x1B); await Task.Delay(150); }
        mouse_event(4, 0, 0, 0, UIntPtr.Zero); await Task.Delay(500);
    }
    private static async Task WaitUntil(Func<bool> ready)
    { for (var i = 0; i < 30 && !ready(); i++) await Task.Delay(100); }
    private static void Key(byte key) { keybd_event(key, 0, 0, UIntPtr.Zero); keybd_event(key, 0, 2, UIntPtr.Zero); }
    private static string[] MenuLabels() => Enumerable.Range(0, GetMenuItemCount(ShellContextMenu.ActiveMenu)).Select(index =>
    { var text = new StringBuilder(512); GetMenuString(ShellContextMenu.ActiveMenu, (uint)index, text, text.Capacity, 0x400); return text.ToString(); }).ToArray();
    private static async Task ClickMenu(string text)
    {
        var index = Array.FindIndex(MenuLabels(), label => label.Contains(text, StringComparison.Ordinal));
        if (index < 0 || !GetMenuItemRect(IntPtr.Zero, ShellContextMenu.ActiveMenu, (uint)index, out var rect)) throw new IOException("原生菜单没有可命中的命令：" + text);
        await MouseAt(new Point((rect.Left + rect.Right) / 2, (rect.Top + rect.Bottom) / 2), false);
    }
    private static async Task TypeText(string text)
    {
        keybd_event(0x11, 0, 0, UIntPtr.Zero); Key(0x41); keybd_event(0x11, 0, 2, UIntPtr.Zero);
        await Task.Delay(100);
        foreach (var character in text)
        {
            var inputs = new[] { new NativeInput { Type = 1, Data = new InputUnion { Keyboard = new KeyboardInput { Scan = character, Flags = 4 } } },
                new NativeInput { Type = 1, Data = new InputUnion { Keyboard = new KeyboardInput { Scan = character, Flags = 6 } } } };
            if (SendInput(2, inputs, Marshal.SizeOf<NativeInput>()) != 2) throw new IOException("Windows 未接受隔离名称输入。");
            await Task.Delay(10);
        }
    }
    private static IntPtr WindowWithTitle(string token)
    {
        var result = IntPtr.Zero;
        EnumWindows((hwnd, _) => { var title = new StringBuilder(1024); GetWindowText(hwnd, title, title.Capacity); if (title.ToString().Contains(token, StringComparison.Ordinal)) result = hwnd; return true; }, IntPtr.Zero);
        return result;
    }
    private static IntPtr ExplorerAt(string path)
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
        dynamic windows = shell.Windows();
        try
        {
            for (var i = 0; i < (int)windows.Count; i++)
            {
                dynamic browser = windows.Item(i);
                try
                {
                    dynamic document = browser.Document; dynamic folder = document.Folder; dynamic self = folder.Self;
                    try { if (string.Equals((string)self.Path, path, StringComparison.OrdinalIgnoreCase)) return new IntPtr((long)browser.HWND); }
                    finally { Marshal.ReleaseComObject(self); Marshal.ReleaseComObject(folder); Marshal.ReleaseComObject(document); }
                }
                finally { Marshal.ReleaseComObject(browser); }
            }
        }
        finally { Marshal.ReleaseComObject(windows); Marshal.ReleaseComObject(shell); }
        return IntPtr.Zero;
    }
    private static void Desktop() { keybd_event(0x5B, 0, 0, UIntPtr.Zero); Key(0x44); keybd_event(0x5B, 0, 2, UIntPtr.Zero); }
    private static void Shortcut(string path, string marker)
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        dynamic link = shell.CreateShortcut(path);
        try { link.TargetPath = Path.Combine(AppContext.BaseDirectory, "KageDesktopTool.exe"); link.Arguments = "--open-target-check \"" + marker + "\""; link.IconLocation = Path.Combine(AppContext.BaseDirectory, "Assets", "app-d.ico"); link.Save(); }
        finally { Marshal.ReleaseComObject(link); Marshal.ReleaseComObject(shell); }
    }
    private sealed class NoStartup : IStartupRegistration
    { public string LaunchCommand => "隔离输入验收不注册自启"; public string? ReadCommand() => null; public void WriteCommand(string? command) => throw new InvalidOperationException(); }
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateHardLinkW")]
    private static extern bool CreateHardLink(string path, string target, IntPtr security);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] private static extern uint GetDoubleClickTime();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string className, string? name);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wp, IntPtr lp);
    [DllImport("user32.dll")] private static extern int GetMenuItemCount(IntPtr menu);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetMenuString(IntPtr menu, uint item, StringBuilder text, int capacity, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMenuItemRect(IntPtr owner, IntPtr menu, uint item, out WindowsDesktop.RECT rect);
    [StructLayout(LayoutKind.Sequential)] private struct NativeInput { internal uint Type; internal InputUnion Data; }
    [StructLayout(LayoutKind.Explicit, Size = 32)] private struct InputUnion { [FieldOffset(0)] internal KeyboardInput Keyboard; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { internal ushort Key, Scan; internal uint Flags, Time; internal UIntPtr Extra; }
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, NativeInput[] inputs, int size);
    private delegate bool WindowCallback(IntPtr hwnd, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int capacity);
}
