using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kage.Workspace;

namespace Kage.Desktop;

internal static partial class ContentInputChecks
{
    // 复用真实系统输入工具，夹具不读取正式配置、不注册自启。
    internal static int RunFourWay()
    {
        var fixture = Path.Combine(Path.GetTempPath(), "Kage-four-way-" + Guid.NewGuid().ToString("N"));
        var evidence = Path.Combine(Environment.CurrentDirectory, ".scratch", "folder-grid-selection-and-header", "verification");
        Directory.CreateDirectory(fixture); Directory.CreateDirectory(evidence);
        var log = Path.Combine(evidence, "four-way-input.txt");
        File.WriteAllText(log, "四向头部真实 Windows 输入验收\n");
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Runtime? runtime = null; StyleDialog? dialog = null;
        var result = 1; var desktopShown = false;
        WindowsDesktop.GetCursorPos(out var cursor);
        app.Startup += async (_, _) =>
        {
            try
            {
                var displays = WindowsDesktop.Displays();
                foreach (var display in displays) File.AppendAllText(log, $"实际硬件：{display}\n");
                var area = displays.OrderByDescending(d => (long)d.Width * d.Height).First();
                var root = Path.Combine(fixture, "内容");
                var name = "中文ABC123e\u0301👩‍💻-" + new string('长', 46);
                var target = new FolderRecord(Guid.NewGuid(), name, area.X + area.Width * 2 / 5, area.Y + area.Height / 3,
                    HeaderWidth: 360, BodyHeight: 280, Expanded: true);
                var other = new FolderRecord(Guid.NewGuid(), "其他 Folder", area.X + 30, area.Y + 30);
                foreach (var record in new[] { target, other }) Directory.CreateDirectory(Path.Combine(root, record.Name));
                var marker = Path.Combine(fixture, "打开证据.txt");
                var path = Path.Combine(root, target.Name);
                var shortLink = Path.Combine(fixture, "temporary.lnk"); Shortcut(shortLink, marker);
                var longLink = Path.Combine(path, "00-" + new string('文', 70) + "完整名称.lnk"); File.Move(shortLink, longLink);
                var lastLink = Path.Combine(path, "zz-" + new string('末', 100) + "全文末尾.lnk");
                Shortcut(shortLink, marker); File.Move(shortLink, lastLink);
                for (var i = 1; i <= 28; i++) File.WriteAllText(Path.Combine(path, $"01-文件{i}.txt"), "真实内容");
                var disk = new JsonWorkspaceStore(Path.Combine(fixture, "状态"));
                disk.Save(new WorkspaceState { Root = root, Folders = [target, other] });
                var store = new FourWayFaultStore(disk);
                IDesktopWorkspace workspace = new DesktopWorkspace(store, new NoStartup());
                Require((await workspace.InitializeAsync(displays)).Succeeded, "隔离初始化");
                runtime = new Runtime(workspace); Desktop(); desktopShown = true; await Task.Delay(700);
                var header = runtime.Headers[target.Id];
                Require(header.Host != IntPtr.Zero && WindowsDesktop.IsWindowVisible(header.Handle), "Explorer 宿主中的实际窗口可见");
                await header.Contents.IconsLoaded;
                FolderRecord Saved() => workspace.Snapshot.Folders.Single(f => f.Folder.Id == target.Id).Folder;
                Point Center(FrameworkElement element) => element.PointToScreen(new Point(element.ActualWidth / 2, element.ActualHeight / 2));
                async Task Click(FrameworkElement element)
                {
                    var expanded = Saved().Expanded;
                    header.UpdateLayout(); CheckHit(header, Center(element)); await MouseAt(Center(element), false);
                    if (element == header.ExpandButton) await WaitUntil(() => Saved().Expanded != expanded);
                    await Task.Delay(200);
                }
                void Bounds()
                {
                    WindowsDesktop.GetWindowRect(header.Handle, out var rect);
                    var scale = workspace.Snapshot.Folders.Single(f => f.Folder.Id == target.Id).DisplayScale;
                    var expected = FolderGeometry.For(header.Record, scale);
                    Require(rect.Left == expected.X && rect.Top == expected.Y && rect.Right - rect.Left == expected.Width
                        && rect.Bottom - rect.Top == expected.Height, "HWND 物理原点与四向可见边界一致");
                }
                async Task OpenStyle()
                {
                    dialog = runtime.CreateAppearance(target.Id)!; dialog.Show(); dialog.Activate();
                    dialog.MaxHeight = Math.Min(dialog.MaxHeight, 620);
                    var dpi = VisualTreeHelper.GetDpi(dialog).DpiScaleX;
                    dialog.Left = (area.X + area.Width - 450 * dpi) / dpi; dialog.Top = (area.Y + 20) / dpi;
                    await Task.Delay(180);
                    var buttonPoint = Center(dialog.ApplyButton);
                    Require(buttonPoint.Y < area.Y + area.Height && buttonPoint.Y > area.Y, "较矮样式窗口的应用按钮始终可见");
                }
                async Task Choose(ComboBox combo, int index)
                {
                    await MouseAt(Center(combo), false); await Task.Delay(80); Key(0x24);
                    for (var i = 0; i < index; i++) Key(0x28);
                    Key(0x0D); await Task.Delay(200);
                    Require(combo.SelectedIndex == index, "真实鼠标与键盘选择样式选项");
                }
                async Task ApplyStyle()
                {
                    await MouseAt(Center(dialog!.ApplyButton), false);
                    await WaitUntil(() => dialog.Interaction.Closed); Require(dialog.Interaction.Closed, "真实应用按钮保存并关闭草稿");
                    dialog = null; await Task.Delay(200); header.UpdateLayout();
                }
                foreach (var direction in Enum.GetValues<HeaderPosition>())
                {
                    foreach (var orientation in Enum.GetValues<NameOrientation>())
                    {
                        var original = Saved(); await OpenStyle();
                        await Choose(dialog!.PositionChoice, (int)direction); await Choose(dialog.NameChoice, (int)orientation);
                        Require(Saved() == original && header.Record.HeaderPosition == direction && header.Record.NameOrientation == orientation,
                            "八组合实时预览不修改正式配置");
                        Require(header.Record.HeaderWidth == original.HeaderWidth && header.Record.BodyHeight == original.BodyHeight,
                            "切换方向保持展示宽高");
                        await ApplyStyle(); Bounds();
                        var anchor = header.HeaderInput.PointToScreen(new Point());
                        await Click(header.ExpandButton);
                        Require(!Saved().Expanded, "方向按钮真实命中折叠");
                        var collapsedAnchor = header.HeaderInput.PointToScreen(new Point());
                        Require((anchor - collapsedAnchor).Length < 3, "折叠前后头部物理位置稳定"); Bounds();
                        await Click(header.ExpandButton); Require(Saved().Expanded, "方向按钮真实命中展开"); Bounds();
                        var expectedArrow = direction switch { HeaderPosition.Top => "▴", HeaderPosition.Bottom => "▾", HeaderPosition.Left => "◂", _ => "▸" };
                        Require((string)header.ExpandButton.Content == expectedArrow, "展开后的按钮指向收起方向");
                        // 名称真实悬停，由 WPF Popup 提供完整名称。
                        var namePoint = Center(header.NameDisplay); CheckHit(header, namePoint);
                        SetCursorPos((int)namePoint.X, (int)namePoint.Y); await Task.Delay(1600);
                        Require(header.NameDisplay.IsMouseOver && PresentationSource.CurrentSources.Cast<PresentationSource>()
                            .Any(source => source.RootVisual != null && Texts(source.RootVisual).Any(t => t.IsVisible && t.Text == name)),
                            "八组合真实悬停显示完整 Folder 名称");
                        SetCursorPos(area.X + 10, area.Y + 10); await Task.Delay(100);
                        Render(header, Path.Combine(evidence, $"header-{direction}-{orientation}.png"));
                    }
                    var before = Saved(); var scale = workspace.Snapshot.Folders.Single(f => f.Folder.Id == target.Id).DisplayScale;
                    var dragPoint = Center(header.NameDisplay); CheckHit(header, dragPoint);
                    await Drag(dragPoint, dragPoint + new Vector(15, 12));
                    Require(Saved().X == before.X + 15 && Saved().Y == before.Y + 12, "四向真实头部拖动保存物理像素位移"); Bounds();
                    before = Saved(); dragPoint = Center(header.NameDisplay);
                    await Drag(dragPoint, dragPoint + new Vector(23, 18), cancel: true);
                    Require(Saved() == before && header.Record == before, "四向真实 Esc 取消拖动恢复锚点");
                    var divider = Center(header.HeaderDivider); CheckHit(header, divider);
                    var delta = direction switch { HeaderPosition.Top => new Vector(0, 9 * scale), HeaderPosition.Bottom => new Vector(0, -9 * scale),
                        HeaderPosition.Left => new Vector(12 * scale, 0), _ => new Vector(-12 * scale, 0) };
                    await Drag(divider, divider + delta);
                    Require(Saved().HeaderWidth == before.HeaderWidth && Saved().BodyHeight == before.BodyHeight
                        && (FolderGeometry.Side(before) ? Saved().SideHeaderWidth > before.SideHeaderWidth : Saved().HeaderHeight > before.HeaderHeight),
                        "四向真实分隔线只调头部厚度"); Bounds();
                    before = Saved(); var grip = Center(header.ResizeGrip); CheckHit(header, grip);
                    var corner = new Vector(direction == HeaderPosition.Right ? -12 * scale : 12 * scale,
                        direction == HeaderPosition.Bottom ? -10 * scale : 10 * scale);
                    await Drag(grip, grip + corner);
                    Require(Saved().HeaderWidth > before.HeaderWidth && Saved().BodyHeight > before.BodyHeight
                        && Saved().HeaderHeight == before.HeaderHeight && Saved().SideHeaderWidth == before.SideHeaderWidth,
                        "四向真实远端外角保持厚度并调展示宽高"); Bounds();
                    // 同一内容视口组合验证全文、滚动、选择清除和双击打开。
                    await workspace.SetContentViewAsync(target.Id, true, 16, ContentSortKey.Name, false); runtime.Render();
                    await header.Contents.IconsLoaded; header.UpdateLayout();
                    var item = header.Contents.Items.Items.Cast<ListBoxItem>().Single(i => (string)i.Tag == longLink);
                    header.Contents.Items.ScrollIntoView(item); header.UpdateLayout();
                    await Click((Image)((Grid)item.Content).Children[0]);
                    Require(header.Contents.SelectedPaths().SequenceEqual(new[] { longLink })
                        && Texts(header.Contents.Items).Any(t => t.IsVisible && t.TextTrimming == TextTrimming.None),
                        "四向视口真实选中展开完整名称");
                    var viewport = header.Contents.Viewport; var viewportPoint = Center(viewport);
                    SetCursorPos((int)viewportPoint.X, (int)viewportPoint.Y); mouse_event(0x800, 0, 0, unchecked((uint)-120), UIntPtr.Zero); await Task.Delay(180);
                    Require(header.Contents.SelectedPaths().Length == 1, "四向视口真实滚轮保留选择");
                    await Click(header.NameDisplay); Require(header.Contents.SelectedPaths().Length == 0, "四向头部真实点击清除选择和全文");
                    var lastItem = header.Contents.Items.Items.Cast<ListBoxItem>().Single(i => (string)i.Tag == lastLink);
                    Require(header.Contents.Items.Items.Cast<ListBoxItem>().Last() == lastItem, "全文末端夹具位于真实排序最后一项");
                    header.Contents.Items.ScrollIntoView(lastItem); header.UpdateLayout();
                    var noFullPixels = Capture();
                    await Click((Image)((Grid)lastItem.Content).Children[0]); header.UpdateLayout();
                    var full = Texts(lastItem).Single(t => t.IsVisible && t.TextTrimming == TextTrimming.None
                        && t.Text == Path.GetFileNameWithoutExtension(lastLink));
                    var viewer = ContentPointerInput.Descendant<ScrollViewer>(header.Contents.Items)!;
                    var initialOffset = viewer.VerticalOffset;
                    var beforePixels = Pixels(noFullPixels); var expandedPixels = Capture(); var afterPixels = Pixels(expandedPixels);
                    var dpi = VisualTreeHelper.GetDpi(header).DpiScaleX;
                    var viewportRect = viewport.TransformToAncestor(header).TransformBounds(new Rect(viewport.RenderSize));
                    Require(Enumerable.Range(0, expandedPixels.PixelHeight).All(y => Enumerable.Range(0, expandedPixels.PixelWidth).All(x =>
                        viewportRect.Contains(new Point(x / dpi, y / dpi)) || Enumerable.Range(0, 4).All(c =>
                            beforePixels[(y * expandedPixels.PixelWidth + x) * 4 + c] == afterPixels[(y * expandedPixels.PixelWidth + x) * 4 + c]))),
                        "四向全文展开的真实渲染变化全部裁剪在所属视口内");
                    var wheelPoint = viewport.PointToScreen(new Point(20, 60)); CheckHit(header, wheelPoint);
                    SetCursorPos((int)wheelPoint.X, (int)wheelPoint.Y);
                    for (var i = 0; i < 20; i++) { mouse_event(0x800, 0, 0, unchecked((uint)-120), UIntPtr.Zero); await Task.Delay(30); }
                    header.UpdateLayout();
                    full = Texts(lastItem).Single(t => t.IsVisible && t.TextTrimming == TextTrimming.None
                        && t.Text == Path.GetFileNameWithoutExtension(lastLink));
                    var bottom = full.TranslatePoint(new Point(0, full.ActualHeight), header.Contents.Items).Y;
                    Render(header, Path.Combine(evidence, $"full-name-tail-{direction}.png"));
                    Require(viewer.VerticalOffset > initialOffset && Math.Abs(viewer.VerticalOffset - viewer.ScrollableHeight) < 1
                        && bottom > 0 && bottom <= viewer.ViewportHeight + 1 && header.Contents.SelectedPaths().SequenceEqual(new[] { lastLink }),
                        $"四向真实滚轮滚到全文末端并保持选择：{direction}，offset {initialOffset}->{viewer.VerticalOffset}/{viewer.ScrollableHeight}，底部 {bottom}，视口 {viewer.ViewportHeight}，全文高 {full.ActualHeight}，选中 {string.Join("；", header.Contents.SelectedPaths())}");
                    var tailPoint = full.PointToScreen(new Point(full.ActualWidth / 2, full.ActualHeight - 10));
                    CheckHit(header, tailPoint); await MouseAt(tailPoint, false); await Task.Delay(150);
                    Require(header.Contents.SelectedPaths().SequenceEqual(new[] { lastLink }), "四向全文末端真实点击仍命中当前项：" + string.Join("；", header.Contents.SelectedPaths()));
                    Render(header, Path.Combine(evidence, $"full-name-tail-{direction}.png"));
                    await Click(header.NameDisplay);
                    header.Contents.Items.ScrollIntoView(item); header.UpdateLayout();
                    await Click((Image)((Grid)item.Content).Children[0]);
                    await MouseAt(new Point(area.X + 20, area.Y + area.Height - 20), false); await Task.Delay(180);
                    Require(header.Contents.SelectedPaths().Length == 0
                        && !Texts(header.Contents.Items).Any(t => t.IsVisible && t.TextTrimming == TextTrimming.None), "四向视口外部真实点击清除选择及全文");
                    header.Contents.Items.ScrollIntoView(item); header.UpdateLayout();
                    var icon = (Image)((Grid)item.Content).Children[0]; CheckHit(header, Center(icon));
                    await MouseAt(Center(icon), true); await WaitUntil(() => File.Exists(marker));
                    Require(File.Exists(marker), "四向视口真实双击打开隔离快捷方式"); File.Delete(marker);
                    await workspace.SetViewAsync(target.Id, false); runtime.Render(); await header.Contents.IconsLoaded; header.UpdateLayout();
                    Require(header.Contents.Items.Items.Count == 30 && header.Contents.Viewport.ActualHeight > 0, "四向列表正常展示并有内容视口");
                    await workspace.SetViewAsync(target.Id, true); runtime.Render(); await header.Contents.IconsLoaded; header.UpdateLayout();
                    before = Saved(); await Click(header.ExpandButton);
                    var collapsed = Saved(); grip = Center(header.ResizeGrip); CheckHit(header, grip);
                    await Drag(grip, grip + new Vector(8 * scale, 8 * scale));
                    Require(FolderGeometry.Side(collapsed) ? Saved().SideHeaderWidth > collapsed.SideHeaderWidth && Saved().BodyHeight > collapsed.BodyHeight
                        : Saved().HeaderWidth > collapsed.HeaderWidth && Saved().HeaderHeight >= collapsed.HeaderHeight,
                        "四向折叠真实外角调整头部长度与厚度");
                    await Click(header.ExpandButton); Bounds();
                }
                var saved = Saved(); await OpenStyle();
                await Choose(dialog!.PositionChoice, (int)HeaderPosition.Bottom);
                dialog.SelectColor("#2468AC"); dialog.Transparency.Value = 60;
                Key(0x1B); await Task.Delay(200);
                Require(dialog.Interaction.Closed && header.Record == saved && Saved() == saved, "真实 Esc 取消恢复整个样式及几何"); dialog = null; Bounds();
                await OpenStyle(); await Choose(dialog!.PositionChoice, (int)HeaderPosition.Left);
                store.Fail = true; await MouseAt(Center(dialog.ApplyButton), false); await Task.Delay(300);
                Require(!dialog.Interaction.Closed && Saved() == saved && dialog.Error.Text.Length > 0, "实际样式窗口保存失败保留可重试草稿");
                await ApplyStyle();
                // 将当前 Folder 移到顶部，越界方向预览必须保留当前草稿，不自动挪位。
                var layout = workspace.BeginLayout(target.Id)!; layout.BeginDrag(0, 0); layout.DragTo(0, 20 - Saved().Y);
                Require((await workspace.CommitLayoutAsync(layout)).Succeeded, "顶部隔离锚点准备"); runtime.Render(); saved = Saved();
                await OpenStyle();
                await MouseAt(Center(dialog!.PositionChoice), false); Key(0x24); Key(0x28); Key(0x0D); await Task.Delay(200);
                Require(header.Record == saved && Saved() == saved && dialog.PositionChoice.SelectedIndex == (int)saved.HeaderPosition
                    && dialog.Error.Text.Length > 0, "真实方向预览越界保留有效方向、位置并提示");
                Key(0x1B); await Task.Delay(200);
                Require(dialog.Interaction.Closed && header.Record == saved && Saved() == saved, "真实取消受阻方向预览保持原布局"); dialog = null; Bounds();
                Require(workspace.Snapshot.Folders.Last().Folder == other, "整套真实交互保持其他 Folder 不变");
                IDesktopWorkspace restarted = new DesktopWorkspace(disk, new NoStartup()); await restarted.InitializeAsync(displays);
                Require(restarted.Snapshot.Folders.Select(f => f.Folder).SequenceEqual(workspace.Snapshot.Folders.Select(f => f.Folder)), "真实输入后逐 Folder 重启恢复");
                File.AppendAllText(log, "未验证实际硬件：多屏、混合 DPI、拔屏、主屏和系统缩放／分辨率切换。对应业务轨迹另行验证。\n");
                result = 0;

                RenderTargetBitmap Capture()
                {
                    var dpi = VisualTreeHelper.GetDpi(header).DpiScaleX;
                    var bitmap = new RenderTargetBitmap((int)Math.Ceiling(header.ActualWidth * dpi), (int)Math.Ceiling(header.ActualHeight * dpi),
                        96 * dpi, 96 * dpi, PixelFormats.Pbgra32);
                    bitmap.Render(header); return bitmap;
                }
            }
            catch (Exception e) { File.AppendAllText(log, "失败：" + e + "\n"); }
            finally
            { dialog?.Close(); mouse_event(4, 0, 0, 0, UIntPtr.Zero); runtime?.Dispose(); if (desktopShown) Desktop(); SetCursorPos(cursor.X, cursor.Y); app.Shutdown(); }
        };
        try { app.Run(); }
        finally
        {
            var full = Path.GetFullPath(fixture);
            if (!full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(full).StartsWith("Kage-four-way-", StringComparison.Ordinal)) throw new IOException("拒绝清理隔离目录范围外的路径。");
            Directory.Delete(full, true); Console.WriteLine(File.ReadAllText(log));
        }
        return result;
        void Require(bool condition, string message)
        { if (!condition) throw new Exception(message); File.AppendAllText(log, "通过：" + message + "\n"); }
    }

    private sealed class FourWayFaultStore(IWorkspaceStore inner) : IWorkspaceStore
    {
        internal bool Fail;
        public StateRead Read() => inner.Read();
        public void Save(WorkspaceState state) { if (Fail) { Fail = false; throw new IOException("隔离样式保存故障"); } inner.Save(state); }
        public void RestoreBackup() => inner.RestoreBackup();
    }

    internal static int RunFourWayRestart()
    {
        var fixture = Path.Combine(Path.GetTempPath(), "Kage-four-way-restart-" + Guid.NewGuid().ToString("N"));
        var evidence = Path.Combine(Environment.CurrentDirectory, ".scratch", "folder-grid-selection-and-header", "verification");
        Directory.CreateDirectory(fixture); Directory.CreateDirectory(evidence);
        var log = Path.Combine(evidence, "four-way-restart.txt"); File.WriteAllText(log, "四向实际窗口重建与重启验收\n");
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Runtime? runtime = null; var result = 1; var desktopShown = false;
        WindowsDesktop.GetCursorPos(out var cursor);
        app.Startup += async (_, _) =>
        {
            try
            {
                var displays = WindowsDesktop.Displays(); var area = displays.OrderByDescending(d => (long)d.Width * d.Height).First();
                var root = Path.Combine(fixture, "内容"); var scale = area.Scale;
                var records = Enum.GetValues<HeaderPosition>().Select((position, i) => new FolderRecord(Guid.NewGuid(), $"重启 {position}",
                    area.X + 40 + i % 2 * ((int)Math.Ceiling(336 * scale) + 40) + (position == HeaderPosition.Right ? (int)Math.Ceiling(240 * scale) : 0),
                    area.Y + 40 + i / 2 * ((int)Math.Ceiling(208 * scale) + 40) + (position == HeaderPosition.Bottom ? (int)Math.Ceiling(160 * scale) : 0),
                    HeaderWidth: 240, BodyHeight: 160, Expanded: true, Color: "#2468AC", Opacity: .4, HeaderPosition: position,
                    NameOrientation: i % 2 == 0 ? NameOrientation.Horizontal : NameOrientation.Vertical)).ToArray();
                foreach (var record in records)
                { Directory.CreateDirectory(Path.Combine(root, record.Name)); File.WriteAllText(Path.Combine(root, record.Name, "真实内容.txt"), "恢复内容"); }
                var store = new JsonWorkspaceStore(Path.Combine(fixture, "状态")); store.Save(new WorkspaceState { Root = root, Folders = records });
                for (var launch = 0; launch < 2; launch++)
                {
                    IDesktopWorkspace workspace = new DesktopWorkspace(store, new NoStartup());
                    Require((await workspace.InitializeAsync(displays)).Succeeded, "真实四向配置加载"); runtime = new Runtime(workspace);
                    if (!desktopShown) { Desktop(); desktopShown = true; } await Task.Delay(250);
                    foreach (var record in records)
                    {
                        var header = runtime.Headers[record.Id]; await header.Contents.IconsLoaded; header.UpdateLayout();
                        var snapshot = workspace.Snapshot.Folders.Single(f => f.Folder.Id == record.Id);
                        var expected = FolderGeometry.For(record, snapshot.DisplayScale);
                        WindowsDesktop.GetWindowRect(header.Handle, out var rect);
                        Require(header.Record == record && rect.Left == expected.X && rect.Top == expected.Y
                            && rect.Right - rect.Left == expected.Width && rect.Bottom - rect.Top == expected.Height
                            && WindowsDesktop.IsWindowVisible(header.Handle) && header.Contents.Items.Items.Count == 1,
                            $"第 {launch + 1} 次启动 {record.HeaderPosition} 的样式、窗口原点、尺寸及内容恢复");
                        var button = header.ExpandButton.PointToScreen(new Point(header.ExpandButton.ActualWidth / 2, header.ExpandButton.ActualHeight / 2));
                        CheckHit(header, button); await MouseAt(button, false);
                        await WaitUntil(() => !workspace.Snapshot.Folders.Single(f => f.Folder.Id == record.Id).Folder.Expanded);
                        Require(!workspace.Snapshot.Folders.Single(f => f.Folder.Id == record.Id).Folder.Expanded,
                            "实际重建后方向按钮确实完成折叠");
                        header.UpdateLayout(); button = header.ExpandButton.PointToScreen(new Point(header.ExpandButton.ActualWidth / 2, header.ExpandButton.ActualHeight / 2));
                        CheckHit(header, button); await MouseAt(button, false);
                        await WaitUntil(() => workspace.Snapshot.Folders.Single(f => f.Folder.Id == record.Id).Folder.Expanded);
                        Require(workspace.Snapshot.Folders.Single(f => f.Folder.Id == record.Id).Folder == record,
                            "实际重建后方向按钮仍可命中且展开不改变锚点");
                    }
                    runtime.Dispose(); runtime = null;
                }
                result = 0;
            }
            catch (Exception e) { File.AppendAllText(log, "失败：" + e + "\n"); }
            finally { runtime?.Dispose(); if (desktopShown) Desktop(); SetCursorPos(cursor.X, cursor.Y); app.Shutdown(); }
        };
        try { app.Run(); }
        finally
        {
            var full = Path.GetFullPath(fixture);
            if (!full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(full).StartsWith("Kage-four-way-restart-", StringComparison.Ordinal)) throw new IOException("拒绝清理隔离范围外的路径。");
            Directory.Delete(full, true); Console.WriteLine(File.ReadAllText(log));
        }
        return result;
        void Require(bool condition, string message)
        { if (!condition) throw new Exception(message); File.AppendAllText(log, "通过：" + message + "\n"); }
    }
}
