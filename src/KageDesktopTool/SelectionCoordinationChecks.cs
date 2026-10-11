using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Interop;
using Kage.Workspace;

namespace Kage.Desktop;

internal static partial class ContentInputChecks
{
    private static async Task SelectionCoordination(IDesktopWorkspace workspace, Runtime runtime, FolderHeader first,
        string fixture, string evidence, Action<bool, string> require, bool movesOnly)
    {
        var second = runtime.Headers.Values.Single(header => header != first);
        var target = workspace.Snapshot.Folders.Single(folder => folder.Folder.Id == second.FolderId).ActualPath;
        File.WriteAllText(Path.Combine(target, "目标.txt"), "隔离选择目标");
        await workspace.RefreshAsync(WindowsDesktop.Displays()); runtime.Render();
        await second.Contents.IconsLoaded;
        ListBoxItem Item(FolderHeader header, int index) => (ListBoxItem)header.Contents.Items.Items[index];
        async Task Click(FolderHeader header, int index, bool right = false)
        {
            await Task.Delay((int)GetDoubleClickTime() + 100);
            var item = Item(header, index);
            header.Contents.Items.ScrollIntoView(item); header.UpdateLayout();
            var icon = (Image)((Grid)item.Content).Children[0];
            var point = icon.PointToScreen(new Point(icon.ActualWidth / 2, icon.ActualHeight / 2));
            CheckHit(header, point); await MouseAt(point, false, right ? 8u : 2u); await Task.Delay(200);
        }
        bool Empty() => runtime.Headers.Values.All(header => header.Contents.SelectedPaths().Length == 0
            && !Texts(header.Contents.Items).Any(text => text.IsVisible && text.TextTrimming == TextTrimming.None));
        if (movesOnly)
        {
            await SelectionMoves(workspace, runtime, first, fixture, require, Click);
            return;
        }
        require(true, $"平台：{Environment.OSVersion}；显示工作区：{string.Join("；", WindowsDesktop.Displays().Select(display => display.ToString()))}");
        await Click(first, 0);
        require(first.Contents.SelectedPaths().Length == 1, "第一个 Folder 真实单击选择");
        await Click(second, 0);
        require(first.Contents.SelectedPaths().Length == 0 && second.Contents.SelectedPaths().Length == 1,
            "跨 Folder 真实单击先清除原选择，仅当前 Folder 持有选择");
        var head = second.HeaderInput.PointToScreen(new Point(60, 20));
        CheckHit(second, head); await MouseAt(head, false); await Task.Delay(200);
        require(second.Contents.SelectedPaths().Length == 0, "点击当前 Folder 头部清除图标选择");
        await Click(first, 0);
        var blank = second.Contents.Viewport.PointToScreen(new Point(second.Contents.Viewport.ActualWidth - 5, 30));
        CheckHit(second, blank); await MouseAt(blank, false); await Task.Delay(200);
        require(first.Contents.SelectedPaths().Length == 0, "点击其他 Folder 空白清除所有图标选择");
        await Click(first, 0);
        var area = WindowsDesktop.Displays().First();
        var desktopPoint = new Point(area.X + area.Width - 80, area.Y + area.Height / 2);
        var desktopHit = WindowsDesktop.WindowFromPoint(new WindowsDesktop.POINT { X = (int)desktopPoint.X, Y = (int)desktopPoint.Y });
        require(WindowsDesktop.IsChild(WindowsDesktop.Host(), desktopHit) || desktopHit == WindowsDesktop.Host(), "桌面空白真实命中桌面视图");
        await MouseAt(desktopPoint, false); await Task.Delay(200);
        require(first.Contents.SelectedPaths().Length == 0, "真实桌面空白点击清除 Folder 选择");
        require(Empty(), "外部清除同时收起所有完整名称");
        foreach (var modifier in new byte[] { 0x11, 0x10 })
        {
            await Click(first, 0);
            keybd_event(modifier, 0, 0, UIntPtr.Zero);
            await Click(second, 0);
            keybd_event(modifier, 0, 2, UIntPtr.Zero);
            require(first.Contents.SelectedPaths().Length == 0 && second.Contents.SelectedPaths().Length == 1,
                $"跨 Folder {(modifier == 0x11 ? "Ctrl" : "Shift")} 单击不累积跨 Folder 选择");
        }
        await Click(first, 0);
        keybd_event(0x11, 0, 0, UIntPtr.Zero); await Click(first, 1); keybd_event(0x11, 0, 2, UIntPtr.Zero);
        require(first.Contents.SelectedPaths().Length == 2, "当前 Folder Ctrl 点击保留多选");
        keybd_event(0x11, 0, 0, UIntPtr.Zero); await Click(first, 1); keybd_event(0x11, 0, 2, UIntPtr.Zero);
        require(first.Contents.SelectedPaths().Length == 1, "当前 Folder Ctrl 点击切换取消");
        await Click(first, 0);
        keybd_event(0x10, 0, 0, UIntPtr.Zero); await Click(first, 3); keybd_event(0x10, 0, 2, UIntPtr.Zero);
        require(first.Contents.SelectedPaths().Length == 4, "当前 Folder Shift 点击连选四项");
        await MouseAt(head, false); await Task.Delay(200);
        keybd_event(0x10, 0, 0, UIntPtr.Zero); await Click(first, 5); keybd_event(0x10, 0, 2, UIntPtr.Zero);
        require(first.Contents.SelectedPaths().SequenceEqual(new[] { (string)Item(first, 5).Tag }), "外部清除后 Shift 点击不复用旧连选锚点");
        await MouseAt(first.HeaderInput.PointToScreen(new Point(60, 20)), false); await Task.Delay(200);
        keybd_event(0x10, 0, 0, UIntPtr.Zero); Key(0x28); keybd_event(0x10, 0, 2, UIntPtr.Zero); await Task.Delay(200);
        require(first.Contents.SelectedPaths().Length == 1, "清除后真实 Shift 键盘导航选中当前项，不复活旧连选范围");
        await Click(first, 0);
        await workspace.RefreshAsync(WindowsDesktop.Displays()); runtime.Render(); await Task.Delay(200);
        require(first.Contents.SelectedPaths().Length == 1, "普通内容刷新保留可靠身份选择");
        await MouseAt(desktopPoint, false); await Task.Delay(200);
        File.WriteAllText(Path.Combine(target, "刷新.txt"), "触发真实内容刷新");
        await workspace.RefreshAsync(WindowsDesktop.Displays()); runtime.Render(); await Task.Delay(200);
        require(Empty(), "显式清除后真实内容重建不恢复旧集合或全文");
        foreach (var window in new[] { first, second })
        {
            await Click(first, 0);
            var headerPoint = window.HeaderInput.PointToScreen(new Point(60, 20));
            CheckHit(window, headerPoint); await MouseAt(headerPoint, false); await Task.Delay(200);
            require(Empty(), "任意 Folder 头部清除所有选择：" + window.Record.Name);
            await Click(first, 0);
            var gutter = window.Contents.Viewport.PointToScreen(new Point(window.Contents.Viewport.ActualWidth - 5, 30));
            CheckHit(window, gutter); await MouseAt(gutter, false, 8); await Task.Delay(250);
            require(Empty() && window.ContextMenu!.IsOpen, "任意 Folder 空白右键清除所有选择并显示 Folder 管理菜单：" + window.Record.Name);
            Key(0x1B); await Task.Delay(200);
        }
        await Click(first, 0);
        keybd_event(0x11, 0, 0, UIntPtr.Zero); await Click(first, 1); keybd_event(0x11, 0, 2, UIntPtr.Zero);
        await Click(first, 0, true); await WaitUntil(() => ShellContextMenu.ActiveMenuWindow != IntPtr.Zero);
        require(first.Contents.SelectedPaths().Length == 2 && ShellContextMenu.ActiveMenuWindow != IntPtr.Zero,
            "已选图标原生菜单前台归属保留真实多选集合");
        Key(0x1B); await WaitUntil(() => !first.Contents.InputActive);
        require(first.Contents.SelectedPaths().Length == 2, "原生菜单 Esc 取消及返回保留多选");
        await Click(second, 0, true); await WaitUntil(() => ShellContextMenu.ActiveMenuWindow != IntPtr.Zero);
        require(first.Contents.SelectedPaths().Length == 0 && second.Contents.SelectedPaths().Length == 1,
            "另一 Folder 未选项目右键先清旧集合再打开当前对象菜单");
        await MouseAt(desktopPoint, false); await WaitUntil(() => !second.Contents.InputActive); await Task.Delay(200);
        require(Empty(), "原生菜单外桌面点击退出并清除选择");
        await Click(first, 0);
        await Click(first, 0, true); await WaitUntil(() => ShellContextMenu.ActiveMenuWindow != IntPtr.Zero);
        var tray = FindWindow("Shell_TrayWnd", null);
        require(WindowsDesktop.GetWindowRect(tray, out var trayBounds), "真实任务栏矩形可读取");
        var trayPoint = new Point(trayBounds.Left + 15, trayBounds.Top + 8);
        await MouseAt(trayPoint, false); await WaitUntil(() => !first.Contents.InputActive); await Task.Delay(250);
        require(Empty(), "原生菜单外任务栏真实点击退出并清除选择");
        Key(0x1B);
        await Click(first, 0);
        await MouseAt(trayPoint, false); await Task.Delay(200);
        require(Empty(), "任务栏普通真实点击清除选择");
        await Click(first, 0);
        var selected = first.Contents.SelectedPaths();
        var viewer = ContentPointerInput.Descendant<ScrollViewer>(first.Contents.Items)!;
        var wheel = first.Contents.Viewport.PointToScreen(new Point(20, 60));
        CheckHit(first, wheel); SetCursorPos((int)wheel.X, (int)wheel.Y);
        mouse_event(0x800, 0, 0, unchecked((uint)-120), UIntPtr.Zero); await Task.Delay(200);
        require(viewer.VerticalOffset > 0 && first.Contents.SelectedPaths().SequenceEqual(selected), "当前 Folder 滚轮真实滚动并保留选择");
        var bar = ContentPointerInput.Descendant<System.Windows.Controls.Primitives.ScrollBar>(first.Contents.Items)!;
        var track = (System.Windows.Controls.Primitives.Track)bar.Template.FindName("PART_Track", bar);
        var thumb = track.Thumb;
        var thumbPoint = thumb.PointToScreen(new Point(thumb.ActualWidth / 2, thumb.ActualHeight / 2));
        var offset = viewer.VerticalOffset;
        await Drag(thumbPoint, new Point(thumbPoint.X, thumbPoint.Y + 20));
        require(viewer.VerticalOffset != offset && first.Contents.SelectedPaths().SequenceEqual(selected), "当前滚动条真实捕获拖动保留选择");
        var validOffset = viewer.VerticalOffset;
        await MouseAt(desktopPoint, false); await Task.Delay(200);
        require(Empty() && Math.Abs(viewer.VerticalOffset - Math.Min(validOffset, viewer.ScrollableHeight)) < 1,
            "外部清除全文保留有效滚动位置，超界才夹到有效末端");

        var desktopName = "Kage-selection-" + Guid.NewGuid().ToString("N");
        var desktopLink = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), desktopName + ".lnk");
        var desktopMarker = Path.Combine(fixture, "desktop-opened.txt");
        var appMarker = Path.Combine(fixture, "app-clicked.txt");
        Process? appProcess = null;
        IntPtr appWindow = IntPtr.Zero;
        try
        {
            Shortcut(desktopLink, desktopMarker);
            var desktop = AutomationElement.FromHandle(WindowsDesktop.Host());
            AutomationElement? desktopItem = null;
            await WaitUntil(() => (desktopItem = desktop.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.NameProperty, desktopName))) != null);
            require(desktopItem != null, "随机隔离桌面快捷方式可定位");
            var desktopRect = desktopItem!.Current.BoundingRectangle;
            var desktopItemPoint = new Point(desktopRect.Left + desktopRect.Width / 2, desktopRect.Top + 20);
            await Click(first, 0);
            await Task.Delay((int)GetDoubleClickTime() + 100);
            await MouseAt(desktopItemPoint, false); await Task.Delay(200);
            require(Empty() && ((SelectionItemPattern)desktopItem.GetCurrentPattern(SelectionItemPattern.Pattern)).Current.IsSelected,
                "桌面项目收到原始真实点击并被 Windows 选中，同时 Folder 清除");
            await Click(first, 0);
            await MouseAt(desktopPoint, false); await Task.Delay(200);
            require(Empty() && !((SelectionItemPattern)desktopItem.GetCurrentPattern(SelectionItemPattern.Pattern)).Current.IsSelected,
                "桌面空白收到原始点击并取消 Windows 项目选择，同时 Folder 清除");
            await Click(first, 0);
            await MouseAt(desktopItemPoint, true); await WaitUntil(() => File.Exists(desktopMarker));
            require(Empty() && File.Exists(desktopMarker), "桌面项目双击继续打开真实快捷方式目标");

            appProcess = Process.Start(new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "KageDesktopTool.exe"))
            { UseShellExecute = false, CreateNoWindow = true, ArgumentList = { "--selection-target-check", appMarker } });
            await WaitUntil(() => (appWindow = WindowWithTitle(appMarker)) != IntPtr.Zero);
            require(appWindow != IntPtr.Zero, "隔离外部进程普通窗口可定位");
            var button = AutomationElement.FromHandle(appWindow).FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.NameProperty, "记录隔离点击"));
            var buttonRect = button.Current.BoundingRectangle;
            var appPoint = new Point(buttonRect.Left + buttonRect.Width / 2, buttonRect.Top + buttonRect.Height / 2);
            async Task RevealTarget()
            {
                // Windows 的“显示桌面”可能再次隐藏新窗口；仅显露本验收进程，不抢焦点。
                ShowSelectionWindow(appWindow, 4);
                WindowsDesktop.SetWindowPos(appWindow, IntPtr.Zero, 0, 0, 0, 0, 0x53);
                await Task.Delay(200);
                var hit = WindowsDesktop.WindowFromPoint(new WindowsDesktop.POINT { X = (int)appPoint.X, Y = (int)appPoint.Y });
                require(hit == appWindow || WindowsDesktop.IsChild(appWindow, hit), "外部应用点击点真实命中隔离窗口");
            }
            await Click(first, 0);
            await RevealTarget();
            await MouseAt(appPoint, false); await WaitUntil(() => File.Exists(appMarker));
            require(Empty(), "真实鼠标切换其他进程清除选择");
            require(File.Exists(appMarker), "外部应用操作按钮收到原始真实点击并写入隔离结果");
            await Click(first, 0);
            keybd_event(0x12, 0, 0, UIntPtr.Zero); Key(0x09); keybd_event(0x12, 0, 2, UIntPtr.Zero); await Task.Delay(400);
            require(GetSelectionForeground() == appWindow && Empty(), "真实 Alt+Tab 切换到其他应用清除选择");
            await Click(first, 0);
            await Click(first, 0, true); await WaitUntil(() => ShellContextMenu.ActiveMenuWindow != IntPtr.Zero);
            Key(0x1B); await WaitUntil(() => !first.Contents.InputActive); await Task.Delay(250);
            require(first.Contents.SelectedPaths().Length == 1, "有其他应用前台历史时，菜单 Esc 返回仍保留当前选择");
            await Click(first, 0);
            await Click(first, 0, true); await WaitUntil(() => ShellContextMenu.ActiveMenuWindow != IntPtr.Zero);
            await RevealTarget();
            await MouseAt(appPoint, false); await WaitUntil(() => !first.Contents.InputActive); await Task.Delay(250);
            require(Empty(), "原生菜单外其他应用真实点击退出并清除选择");
            await Click(first, 1);
            ShellContextMenu.InvalidatePrepared();
            var prepareIcon = (Image)((Grid)Item(first, 1).Content).Children[0];
            var preparePoint = prepareIcon.PointToScreen(new Point(prepareIcon.ActualWidth / 2, prepareIcon.ActualHeight / 2));
            await MouseAt(preparePoint, false, 8);
            require(first.Contents.InputActive, "真实右键进入文件菜单请求");
            require(true, "外部取消时菜单阶段：" + (ShellContextMenu.ActiveMenuWindow == IntPtr.Zero ? "准备" : "显示"));
            await RevealTarget(); await MouseAt(appPoint, false); await WaitUntil(() => !first.Contents.InputActive); await Task.Delay(350);
            require(Empty() && ShellContextMenu.ActiveMenuWindow == IntPtr.Zero, "外部真实点击取消菜单请求，旧对象菜单不在之后重新弹出");

            await Click(first, 0);
            await Click(first, 0, true); await WaitUntil(() => ShellContextMenu.ActiveMenuWindow != IntPtr.Zero);
            await ClickMenu("重命名"); await Task.Delay(250);
            var rename = Application.Current.Windows.OfType<ContentRenameDialog>().Single();
            require(first.Contents.SelectedPaths().Length == 1,
                $"原生命令进入内部重命名窗口保留当前真实对象选择（实际 {first.Contents.SelectedPaths().Length}；前台 {GetSelectionForeground()}；重命名 {new WindowInteropHelper(rename).Handle}）");
            Key(0x1B); await WaitUntil(() => !first.Contents.InputActive); await Task.Delay(250);
            require(first.Contents.SelectedPaths().Length == 1, "取消内部重命名并返回不误清除选择");

            var fileIndices = first.Contents.Items.Items.Cast<ListBoxItem>().Select((item, index) => (item, index))
                .Where(pair => File.Exists((string)pair.item.Tag)).Take(2).Select(pair => pair.index).ToArray();
            await Click(first, fileIndices[0]);
            keybd_event(0x11, 0, 0, UIntPtr.Zero); await Click(first, fileIndices[1]); keybd_event(0x11, 0, 2, UIntPtr.Zero);
            var paths = first.Contents.SelectedPaths();
            var bytes = paths.Select(File.ReadAllBytes).ToArray();
            var icon = (Image)((Grid)Item(first, fileIndices[0]).Content).Children[0];
            var start = icon.PointToScreen(new Point(icon.ActualWidth / 2, icon.ActualHeight / 2));
            await Drag(start, second.HeaderInput.PointToScreen(new Point(60, 20)), duringDrag: async () =>
            {
                require(runtime.DraggingFiles, "真实多选文件拖动已经开始");
                keybd_event(0x12, 0, 0, UIntPtr.Zero); Key(0x09); keybd_event(0x12, 0, 2, UIntPtr.Zero);
                await Task.Delay(300);
                require(GetSelectionForeground() == appWindow && Empty(), "拖动中真实 Alt+Tab 切换应用清除可见选择");
            });
            await WaitUntil(() => !runtime.Moving); await Task.Delay(250);
            foreach (var dialog in Application.Current.Windows.OfType<MoveDialog>().ToArray()) dialog.Close();
            require(paths.All(path => !File.Exists(path)) && paths.Select((path, i) =>
                File.ReadAllBytes(Path.Combine(target, Path.GetFileName(path))).SequenceEqual(bytes[i])).All(same => same),
                "拖动开始后切换应用仍使用开始时的两条真实路径，跨 Folder 文件字节正确");
            PostMessage(appWindow, 0x10, IntPtr.Zero, IntPtr.Zero);
            await SelectionMoves(workspace, runtime, first, fixture, require, Click);
            first.Close(); runtime.Render(); await Task.Delay(250);
            var replacement = runtime.Headers[first.FolderId];
            require(replacement != first && replacement.Contents.SelectedPaths().Length == 0, "Folder 关闭重建不恢复旧选择或订阅归属");
            await Click(replacement, 0);
            await MouseAt(desktopPoint, false); await Task.Delay(200);
            require(Empty(), "重建后的窗口仍由唯一协调器清除选择");
            Render(replacement, Path.Combine(evidence, "selection-cleared.png"));
        }
        finally
        {
            if (appWindow != IntPtr.Zero) PostMessage(appWindow, 0x10, IntPtr.Zero, IntPtr.Zero);
            if (appProcess != null) { await Task.Run(() => appProcess.WaitForExit(5000)); appProcess.Dispose(); }
            if (File.Exists(desktopLink)) File.Delete(desktopLink);
        }
    }

    internal static int SelectionTarget(string marker)
    {
        var area = WindowsDesktop.Displays().First();
        var app = new Application();
        var button = new Button { Content = "记录隔离点击", Margin = new Thickness(25) };
        button.Click += (_, _) => File.WriteAllText(marker, "真实应用收到点击");
        var window = new Window { Title = marker, Width = 300, Height = 180,
            Left = (area.X + area.Width - 550) / area.Scale, Top = (area.Y + area.Height - 450) / area.Scale, Content = button };
        app.Run(window);
        return 0;
    }

    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")] private static extern IntPtr GetSelectionForeground();
    [DllImport("user32.dll", EntryPoint = "ShowWindow")] private static extern bool ShowSelectionWindow(IntPtr window, int command);
}
