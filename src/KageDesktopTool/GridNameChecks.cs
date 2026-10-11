using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Kage.Workspace;

namespace Kage.Desktop;

internal static partial class ContentInputChecks
{
    private static async Task GridNames(IDesktopWorkspace workspace, Runtime runtime, FolderHeader header,
        string fixture, string evidence, Action<bool, string> require)
    {
        var contents = header.Contents;
        var id = contents.FolderId;
        var sizes = new List<Size>();
        var gaps = new List<double>();
        foreach (var size in new[] { 16, 32, 48, 96 })
        {
            await workspace.SetContentViewAsync(id, true, size, ContentSortKey.Name, false);
            runtime.Render(); await contents.IconsLoaded; header.UpdateLayout();
            var cells = contents.Items.Items.Cast<ListBoxItem>().ToArray();
            var first = cells[0];
            sizes.Add(first.RenderSize);
            var icon = (Image)((Grid)first.Content).Children[0];
            gaps.Add(first.ActualWidth - icon.ActualWidth);
            require(cells.All(item => item.RenderSize == first.RenderSize), $"{size} DIP 同档格子一致");
            Render(header, Path.Combine(evidence, $"grid-names-{size}.png"));
        }
        require(sizes.Zip(sizes.Skip(1)).All(pair => pair.First.Width < pair.Second.Width && pair.First.Height < pair.Second.Height)
            && gaps.Distinct().Count() == 4, "四档格子宽高及图标边缘间距实际变化，小中档不共用系统下限");

        var path = workspace.Snapshot.Folders.First(f => f.Folder.Id == id).ActualPath;
        var longLink = Path.Combine(path, "00-" + new string('长', 100) + "全文末尾.lnk");
        var lastLink = Path.Combine(path, "zz-" + new string('末', 100) + "全文末尾.lnk");
        var marker = Path.Combine(fixture, "全文打开.txt");
        // WScript 的保存入口限制长路径；先创建真实 .lnk，再由文件系统改名。
        var temporaryLink = Path.Combine(path, "full.lnk");
        Shortcut(temporaryLink, marker); File.Move(temporaryLink, longLink);
        Shortcut(temporaryLink, marker); File.Move(temporaryLink, lastLink);
        await workspace.RefreshAsync(WindowsDesktop.Displays());
        await workspace.SetContentViewAsync(id, true, 16, ContentSortKey.Name, false);
        runtime.Render(); await contents.IconsLoaded; header.UpdateLayout();
        ListBoxItem Item(string file) => contents.Items.Items.Cast<ListBoxItem>().Single(item => (string)item.Tag == file);
        async Task ClickItem(string file)
        {
            var item = Item(file);
            contents.Items.ScrollIntoView(item); header.UpdateLayout();
            var icon = (Image)((Grid)item.Content).Children[0];
            var point = icon.PointToScreen(new Point(icon.ActualWidth / 2, icon.ActualHeight / 2));
            CheckHit(header, point);
            await Task.Delay((int)GetDoubleClickTime() + 100);
            await MouseAt(point, false); await Task.Delay(200); header.UpdateLayout();
        }
        TextBlock? FullLabel(ListBoxItem item) => Texts(item).FirstOrDefault(text => text.Text == Path.GetFileNameWithoutExtension((string)item.Tag)
            && text.TextTrimming == TextTrimming.None && text.IsVisible);
        var savedFolders = workspace.Snapshot.Folders.Select(f => f.Folder).ToArray();
        var positions = contents.Items.Items.Cast<ListBoxItem>().Select(item => item.TranslatePoint(new Point(), contents.Items)).ToArray();
        Render(header, Path.Combine(evidence, "grid-names-collapsed.png"));
        var collapsedLabel = (TextBlock)((Grid)Item(longLink).Content).Children[1];
        require(collapsedLabel.ActualHeight + .01 >= collapsedLabel.LineHeight * 3,
            $"三行标签实际高度足够：{collapsedLabel.ActualHeight:F3}，行高 {collapsedLabel.LineHeight:F3}，缩放 {workspace.Snapshot.Folders.First(f => f.Folder.Id == id).DisplayScale}");
        await ClickItem(longLink);
        var full = FullLabel(Item(longLink));
        require(full != null && full.ActualHeight > Item(longLink).ActualHeight * 2, "真实单击后完整长名称在固定格子外展开");
        require(contents.Items.Items.Cast<ListBoxItem>().Select(item => item.TranslatePoint(new Point(), contents.Items)).SequenceEqual(positions)
            && workspace.Snapshot.Folders.Select(f => f.Folder).SequenceEqual(savedFolders), "全文展开不移动格子或修改任意 Folder 几何");
        Render(header, Path.Combine(evidence, "grid-names-expanded.png"));
        var overlayPoint = full!.PointToScreen(new Point(full.ActualWidth / 2, Item(longLink).ActualHeight + 8));
        CheckHit(header, overlayPoint);
        await MouseAt(overlayPoint, false); await Task.Delay(200);
        require(contents.SelectedPaths().SequenceEqual(new[] { longLink }), "覆盖下方格子的全文真实点击归属原项目");
        await MouseAt(overlayPoint, false, 8); await WaitUntil(() => ShellContextMenu.ActiveMenuWindow != IntPtr.Zero);
        require(contents.SelectedPaths().SequenceEqual(new[] { longLink }) && ShellContextMenu.ActiveMenuWindow != IntPtr.Zero,
            "全文右键命中原项目并打开现有原生菜单");
        Key(0x1B); await Task.Delay(250);
        await Task.Delay((int)GetDoubleClickTime() + 100);
        await MouseAt(overlayPoint, true);
        await WaitUntil(() => File.Exists(marker));
        require(File.Exists(marker), "覆盖区域真实双击打开原快捷方式目标");
        await ClickItem(lastLink);
        var viewer = ContentPointerInput.Descendant<System.Windows.Controls.ScrollViewer>(contents.Items)!;
        var lastFull = FullLabel(Item(lastLink));
        require(lastFull != null && FullLabel(Item(longLink)) == null, "换项仅最后操作项展开，旧全文收起");
        var initialOffset = viewer.VerticalOffset;
        var wheel = contents.Viewport.PointToScreen(new Point(20, 60));
        CheckHit(header, wheel); SetCursorPos((int)wheel.X, (int)wheel.Y);
        for (var i = 0; i < 20; i++) { mouse_event(0x800, 0, 0, unchecked((uint)-120), UIntPtr.Zero); await Task.Delay(30); }
        header.UpdateLayout();
        require(viewer.VerticalOffset > initialOffset && contents.SelectedPaths().SequenceEqual(new[] { lastLink }), "滚轮读取全文并保留选择，不自动拉回开头");
        var bottom = lastFull!.TranslatePoint(new Point(0, lastFull.ActualHeight), contents.Items).Y;
        require(Math.Abs(viewer.VerticalOffset - viewer.ScrollableHeight) < 1 && bottom <= viewer.ViewportHeight + 1,
            "末行全文增加整体滚动范围，末尾可进入所属视口");
        Render(header, Path.Combine(evidence, "grid-names-last-bottom.png"));
        var offsetAtBottom = viewer.VerticalOffset;
        var tailPoint = lastFull.PointToScreen(new Point(lastFull.ActualWidth / 2, lastFull.ActualHeight - 10));
        await Task.Delay((int)GetDoubleClickTime() + 100); CheckHit(header, tailPoint);
        await MouseAt(tailPoint, false); await Task.Delay(200);
        require(Math.Abs(viewer.VerticalOffset - offsetAtBottom) < 1, "点击已展开全文末尾保留用户滚动位置");
        // 在保留名称末尾的当前位置点击空白清除；不直接改控件选择。
        var blank = contents.Items.PointToScreen(new Point(contents.Items.ActualWidth - 20, 20));
        CheckHit(header, blank); await MouseAt(blank, false); await Task.Delay(200); header.UpdateLayout();
        require(contents.SelectedPaths().Length == 0 && FullLabel(Item(lastLink)) == null
            && viewer.VerticalOffset <= offsetAtBottom && Math.Abs(viewer.VerticalOffset - viewer.ScrollableHeight) < 1,
            "取消后全文消失，超界偏移仅夹到收起后的末端");
        await workspace.RefreshAsync(WindowsDesktop.Displays()); runtime.Render(); await Task.Delay(200);
        require(contents.SelectedPaths().Length == 0 && FullLabel(Item(lastLink)) == null, "已取消选择与全文不被普通刷新恢复");
        await ClickItem(lastLink);
        var bar = ContentPointerInput.Descendant<System.Windows.Controls.Primitives.ScrollBar>(contents.Items)!;
        var track = (System.Windows.Controls.Primitives.Track)bar.Template.FindName("PART_Track", bar);
        var thumb = track.Thumb;
        var thumbPoint = thumb.PointToScreen(new Point(thumb.ActualWidth / 2, thumb.ActualHeight / 2));
        await Drag(thumbPoint, new Point(thumbPoint.X, thumbPoint.Y - 25));
        require(contents.SelectedPaths().SequenceEqual(new[] { lastLink }) && FullLabel(Item(lastLink)) != null,
            "真实滚动条拖动保留当前选择与全文");
        var validOffset = viewer.VerticalOffset;
        var gutter = contents.Viewport.PointToScreen(new Point(contents.Viewport.ActualWidth - 5, 20));
        CheckHit(header, gutter); await MouseAt(gutter, false); await Task.Delay(200);
        require(contents.SelectedPaths().Length == 0 && Math.Abs(viewer.VerticalOffset - validOffset) < 1,
            "收起全文保留仍在基础范围内的当前滚动偏移");
        await ClickItem(longLink);
        Render(header, Path.Combine(evidence, "grid-names-three-lines.png"));
        keybd_event(0x11, 0, 0, UIntPtr.Zero);
        await ClickItem(lastLink);
        keybd_event(0x11, 0, 2, UIntPtr.Zero);
        require(contents.SelectedPaths().Length == 2 && FullLabel(Item(lastLink)) != null && FullLabel(Item(longLink)) == null,
            "Ctrl 多选仅最后点击项全文展开");
        await workspace.RefreshAsync(WindowsDesktop.Displays()); runtime.Render(); await Task.Delay(200);
        require(contents.SelectedPaths().Length == 2 && FullLabel(Item(lastLink)) != null, "普通身份刷新保留多选与当前全文");
        await ClickItem(longLink);
        Key(0x23); await Task.Delay(200); header.UpdateLayout();
        require(contents.SelectedPaths().SequenceEqual(new[] { lastLink }) && FullLabel(Item(lastLink)) != null && FullLabel(Item(longLink)) == null,
            "真实 End 键定位最后项并切换全文");
        var renamed = Path.Combine(path, "zy-" + new string('名', 100) + "全文末尾.lnk");
        File.Move(lastLink, renamed);
        await workspace.RefreshAsync(WindowsDesktop.Displays()); runtime.Render(); await Task.Delay(200); header.UpdateLayout();
        require(contents.SelectedPaths().SequenceEqual(new[] { renamed }) && FullLabel(Item(renamed)) != null, "外部改名按真实身份保留当前全文对象");
        File.Delete(renamed);
        await workspace.RefreshAsync(WindowsDesktop.Displays()); runtime.Render(); await Task.Delay(200);
        require(contents.SelectedPaths().Length == 0 && !Texts(contents.Items).Any(text => text.IsVisible && text.Text.StartsWith("zy-", StringComparison.Ordinal)),
            "已删除对象不留下可操作的旧全文");
        await ClickItem(longLink);
        full = FullLabel(Item(longLink))!;
        overlayPoint = full.PointToScreen(new Point(full.ActualWidth / 2, Item(longLink).ActualHeight + 8));
        var covered = contents.Items.Items.Cast<ListBoxItem>().First(item => item != Item(longLink)
            && contents.BoundsOf(item).Contains(contents.Viewport.PointFromScreen(overlayPoint)));
        await MouseAt(gutter, false); await Task.Delay(200);
        await Task.Delay((int)GetDoubleClickTime() + 100); await MouseAt(overlayPoint, false); await Task.Delay(200);
        require(contents.SelectedPaths().SequenceEqual(new[] { (string)covered.Tag }), "全文取消后下方项目恢复真实命中");
        await ClickItem(longLink);
        var shortLink = Path.Combine(path, "01-shortcut.lnk");
        keybd_event(0x11, 0, 0, UIntPtr.Zero); await ClickItem(shortLink); keybd_event(0x11, 0, 2, UIntPtr.Zero);
        var longIcon = (Image)((Grid)Item(longLink).Content).Children[0];
        var iconPoint = longIcon.PointToScreen(new Point(longIcon.ActualWidth / 2, longIcon.ActualHeight / 2));
        await MouseAt(iconPoint, false, 8); await WaitUntil(() => ShellContextMenu.ActiveMenuWindow != IntPtr.Zero);
        require(contents.SelectedPaths().ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(new[] { longLink, shortLink }),
            "右键已选项保留原生多选集合并切换当前全文");
        Key(0x1B); await Task.Delay(250);
        full = FullLabel(Item(longLink))!;
        overlayPoint = full.PointToScreen(new Point(full.ActualWidth / 2, Item(longLink).ActualHeight + 8));
        var target = runtime.Headers.Values.Single(window => window != header);
        var targetPath = workspace.Snapshot.Folders.Single(f => f.Folder.Id == target.FolderId).ActualPath;
        CheckHit(header, overlayPoint);
        await Drag(overlayPoint, target.HeaderInput.PointToScreen(new Point(60, 20)));
        await WaitUntil(() => runtime.ActiveMove == null && workspace.Snapshot.Folders.Single(f => f.Folder.Id == target.FolderId).FileCount == 2);
        foreach (var dialog in Application.Current.Windows.OfType<MoveDialog>().ToArray()) dialog.Close();
        require(!File.Exists(longLink) && !File.Exists(shortLink)
            && File.Exists(Path.Combine(targetPath, Path.GetFileName(longLink))) && File.Exists(Path.Combine(targetPath, Path.GetFileName(shortLink))),
            "从全文覆盖区域真实拖动使用完整选择集合并实际跨 Folder 移动");
        File.WriteAllText(Path.Combine(targetPath, "短.txt"), "无基础滚动条的排列回归");
        await workspace.RefreshAsync(WindowsDesktop.Displays());
        await workspace.SetContentViewAsync(target.FolderId, true, 32, ContentSortKey.Name, false);
        runtime.Render(); await target.Contents.IconsLoaded; target.UpdateLayout();
        var targetCells = target.Contents.Items.Items.Cast<ListBoxItem>().ToArray();
        var resize = workspace.BeginLayout(target.FolderId)!;
        resize.ResizeBy(targetCells[0].ActualWidth * 3 + 5 - target.Contents.Items.ActualWidth, 0);
        require((await workspace.CommitLayoutAsync(resize)).Succeeded, "固定三列临界宽度夹具");
        runtime.Render(); target.UpdateLayout();
        require(ContentPointerInput.Descendant<ScrollViewer>(target.Contents.Items)!.ScrollableHeight == 0, "少量项目基础内容无需滚动");
        var beforeExpansion = targetCells.Select(item => item.TranslatePoint(new Point(), target.Contents.Items)).ToArray();
        var targetLong = targetCells.Single(item => Path.GetFileName((string)item.Tag) == Path.GetFileName(longLink));
        var targetIcon = (Image)((Grid)targetLong.Content).Children[0];
        var targetPoint = targetIcon.PointToScreen(new Point(targetIcon.ActualWidth / 2, targetIcon.ActualHeight / 2));
        var targetHit = WindowsDesktop.WindowFromPoint(new WindowsDesktop.POINT { X = (int)targetPoint.X, Y = (int)targetPoint.Y });
        require(targetHit == target.Handle || WindowsDesktop.IsChild(target.Handle, targetHit), "目标 Folder 图标真实命中");
        await MouseAt(targetPoint, false); await Task.Delay(250); target.UpdateLayout();
        require(targetCells.Select(item => item.TranslatePoint(new Point(), target.Contents.Items)).SequenceEqual(beforeExpansion),
            "基础内容无需滚动时展开全文也不改变列数和其他格子位置");
    }

    private static IEnumerable<TextBlock> Texts(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBlock text) yield return text;
            foreach (var nested in Texts(child)) yield return nested;
        }
    }
}
