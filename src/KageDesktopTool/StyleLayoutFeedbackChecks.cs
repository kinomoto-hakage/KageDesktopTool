using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Kage.Workspace;

namespace Kage.Desktop;

internal static partial class ContentInputChecks
{
    // 观察真实 HWND 消息与系统鼠标输入，覆盖人工反馈的瞬时预览和分隔线轨迹。
    internal static int RunStyleLayoutFeedback()
    {
        var fixture = Path.Combine(Path.GetTempPath(), "Kage-style-feedback-" + Guid.NewGuid().ToString("N"));
        var evidence = Path.Combine(Environment.CurrentDirectory, ".scratch", "folder-grid-selection-and-header", "verification");
        Directory.CreateDirectory(fixture); Directory.CreateDirectory(evidence);
        var log = Path.Combine(evidence, "style-layout-feedback.txt");
        File.WriteAllText(log, "样式与尺寸人工反馈回归\n");
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Runtime? runtime = null; StyleDialog? dialog = null;
        var result = 1; var desktopShown = false; var failures = 0;
        WindowsDesktop.GetCursorPos(out var cursor);
        app.Startup += async (_, _) =>
        {
            try
            {
                var displays = WindowsDesktop.Displays();
                foreach (var display in displays) File.AppendAllText(log, $"实际硬件：{display}\n");
                var area = displays.OrderByDescending(d => (long)d.Width * d.Height).First();
                var root = Path.Combine(fixture, "内容");
                var target = new FolderRecord(Guid.NewGuid(), "样式反馈", area.X + area.Width / 2, area.Y + area.Height / 2,
                    HeaderWidth: 300, BodyHeight: 200, Expanded: true);
                Directory.CreateDirectory(Path.Combine(root, target.Name));
                var disk = new JsonWorkspaceStore(Path.Combine(fixture, "状态"));
                disk.Save(new WorkspaceState { Root = root, Folders = [target] });
                IDesktopWorkspace workspace = new DesktopWorkspace(disk, new NoStartup());
                if (!(await workspace.InitializeAsync(displays)).Succeeded) throw new IOException("隔离初始化失败。");
                runtime = new Runtime(workspace); Desktop(); desktopShown = true; await Task.Delay(650);
                var header = runtime.Headers[target.Id];
                FolderRecord Saved() => workspace.Snapshot.Folders.Single().Folder;
                Point Center(FrameworkElement element) => element.PointToScreen(new Point(element.ActualWidth / 2, element.ActualHeight / 2));
                // 实际移入 InternetShortcut，显示名隐藏后缀，图标仍按实际文件加载。
                var incoming = Path.Combine(fixture, "Wallpaper Engine：壁纸引擎.url");
                var bytes = System.Text.Encoding.UTF8.GetBytes("[InternetShortcut]\nURL=steam://rungameid/431960\nIconIndex=0\nIconFile="
                    + Path.Combine(AppContext.BaseDirectory, "Assets", "app-d.ico") + "\n");
                File.WriteAllBytes(incoming, bytes);
                var moved = await workspace.MoveAsync([incoming], MoveTarget.Folder(target.Id)); runtime.Render();
                await header.Contents.IconsLoaded; header.UpdateLayout();
                var shortcut = workspace.Snapshot.Folders.Single().Entries.Single();
                Check(moved.Items.All(item => item.Outcome == Outcome.Success) && !File.Exists(incoming)
                    && shortcut.Name.EndsWith(".url", StringComparison.OrdinalIgnoreCase)
                    && shortcut.DisplayName == "Wallpaper Engine：壁纸引擎"
                    && Texts(header.Contents).Any(t => t.Text == shortcut.DisplayName), "URL 实际移入保留文件类型并隐藏显示后缀");
                var image = (System.Windows.Media.Imaging.BitmapSource)ShellIcons.ForFile(shortcut.ActualPath, false, 48)!;
                Check(Pixels(image).SequenceEqual(Pixels(ShellIconResources.Read(shortcut.ActualPath, 48)!)),
                    "URL 移入后显示其真实显式图标资源");
                var rename = new ContentRenameDialog(target.Id, shortcut.ActualPath); rename.Show(); rename.Activate();
                await Task.Delay(150);
                Check(FindElements<TextBox>(rename).Single().Text == shortcut.DisplayName, "快捷方式改名仅编辑显示名称");
                await TypeText("Wallpaper Engine 已改名"); Key(0x0D); await WaitUntil(() => !rename.IsVisible);
                shortcut = workspace.Snapshot.Folders.Single().Entries.Single();
                Check(shortcut.Name == "Wallpaper Engine 已改名.url" && File.ReadAllBytes(shortcut.ActualPath).SequenceEqual(bytes)
                    && Pixels((System.Windows.Media.Imaging.BitmapSource)ShellIcons.ForFile(shortcut.ActualPath, false, 48)!).SequenceEqual(Pixels(image)),
                    "真实改名保留 URL 扩展名、链接字节及图标");
                async Task OpenStyle()
                {
                    dialog = runtime.CreateAppearance(target.Id)!; dialog.Show(); dialog.Activate();
                    var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(dialog).DpiScaleX;
                    dialog.Left = (area.X + 20) / dpi; dialog.Top = (area.Y + 20) / dpi;
                    await Task.Delay(150);
                }
                async Task Choose(HeaderPosition direction)
                {
                    await MouseAt(Center(dialog!.PositionChoice), false); Key(0x24);
                    for (var i = 0; i < (int)direction; i++) Key(0x28);
                    Key(0x0D); await Task.Delay(180);
                }
                await OpenStyle(); await Choose(HeaderPosition.Right);
                var expected = header.Record;
                var transient = new List<string>();
                var source = HwndSource.FromHwnd(header.Handle);
                HwndSourceHook hook = (IntPtr hwnd, int message, IntPtr wp, IntPtr lp, ref bool handled) =>
                {
                    if (message == 0x47 && header.Record.HeaderPosition != expected.HeaderPosition)
                        transient.Add(header.Record.HeaderPosition.ToString());
                    return IntPtr.Zero;
                };
                source.AddHook(hook);
                try
                {
                    // 两个完整的三秒周期，另外主动刷新以稳定捕获瞬时还原。
                    await runtime.RefreshDisplayEnvironmentAsync();
                    File.WriteAllText(Path.Combine(root, target.Name, "预览期间新增.txt"), "真实内容");
                    await Task.Delay(6500);
                    Check(transient.Count == 0 && header.Record == expected && Saved() == target,
                        $"待应用预览跨周期不回切正式方向，瞬时回切次数={transient.Count}");
                    Check(header.Contents.Items.Items.Count == 2, "样式预览期间仍能刷新真实目录内容");
                }
                finally { source.RemoveHook(hook); }
                dialog!.Close(); dialog = null; await Task.Delay(150);
                Check(header.Record == target && Saved() == target, "取消预览恢复正式几何与样式");
                foreach (var direction in Enum.GetValues<HeaderPosition>())
                {
                    await OpenStyle(); await Choose(direction);
                    var side = direction is HeaderPosition.Left or HeaderPosition.Right;
                    Check(Texts(dialog!).Any(t => t.Text.Contains(side ? "头部宽度" : "头部高度", StringComparison.Ordinal)),
                        $"样式厚度标签适应头部方向：{direction}");
                    var thickness = side ? dialog!.Interaction.Draft.SideHeaderWidth : dialog!.Interaction.Draft.HeaderHeight;
                    Check(dialog!.SideWidth.Minimum == (side ? 64 : 42) && dialog.SideWidth.Maximum == (side ? 360 : 82)
                        && Math.Abs(dialog.SideWidth.Value - thickness) < .01, $"样式厚度滑块范围和值适应方向：{direction}");
                    var sliderPoint = Center(dialog.SideWidth);
                    await MouseAt(sliderPoint, false); Key(0x27); await Task.Delay(150);
                    thickness = side ? dialog.Interaction.Draft.SideHeaderWidth : dialog.Interaction.Draft.HeaderHeight;
                    Check(Math.Abs(thickness - dialog.SideWidth.Value) < .01
                        && Texts(dialog).Any(t => t.Text == $"{thickness:0.#} DIP"), $"真实滑块输入预览并显示厚度数值：{direction}");
                    var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(dialog).DpiScaleX;
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(dialog.ActualWidth * dpi),
                        (int)Math.Ceiling(dialog.ActualHeight * dpi), 96 * dpi, 96 * dpi, System.Windows.Media.PixelFormats.Pbgra32);
                    bitmap.Render(dialog); var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    using (var file = File.Create(Path.Combine(evidence, $"feedback-style-{direction}.png"))) png.Save(file);
                    Render(header, Path.Combine(evidence, $"feedback-preview-{direction}.png"));
                    await MouseAt(Center(dialog.ApplyButton), false); await WaitUntil(() => dialog.Interaction.Closed);
                    if (!dialog.Interaction.Closed) throw new IOException("样式未完成保存。");
                    dialog = null; await Task.Delay(150); header.UpdateLayout();
                    var before = Saved(); var divider = Center(header.HeaderDivider); CheckHit(header, divider);
                    var scale = workspace.Snapshot.Folders.Single().DisplayScale;
                    var delta = direction switch { HeaderPosition.Top => new Vector(0, 6 * scale), HeaderPosition.Bottom => new Vector(0, -6 * scale),
                        HeaderPosition.Left => new Vector(8 * scale, 0), _ => new Vector(-8 * scale, 0) };
                    await Drag(divider, divider + delta); header.UpdateLayout();
                    var after = Center(header.HeaderDivider);
                    Check(Math.Abs((side ? after.X - divider.X : after.Y - divider.Y) - (side ? delta.X : delta.Y)) <= 2,
                        $"分隔线实际位置跟随鼠标方向和位移：{direction}，变化={after - divider}，鼠标={delta}");
                    Check(Saved().HeaderWidth == before.HeaderWidth && Saved().BodyHeight == before.BodyHeight,
                        $"分隔线保持展示宽高：{direction}");
                }
                var style = workspace.BeginAppearance(target.Id)!; style.SetHeaderPosition(HeaderPosition.Top);
                if (!(await workspace.ApplyAppearanceAsync(style)).Succeeded) throw new IOException("屏幕边界夹具方向准备失败。");
                var current = Saved(); var geometry = FolderGeometry.For(current, area.Scale);
                var layout = workspace.BeginLayout(target.Id)!; layout.BeginDrag(0, 0);
                layout.DragTo(area.X + area.Width - geometry.Width - current.X, area.Y + 80 - current.Y);
                if (!(await workspace.CommitLayoutAsync(layout)).Succeeded) throw new IOException("屏幕边界夹具位置准备失败。");
                runtime.Render(); await Task.Delay(150); header.UpdateLayout();
                var blocked = Saved(); var gripPoint = Center(header.ResizeGrip); CheckHit(header, gripPoint);
                await Drag(gripPoint, gripPoint + new Vector(60, 0));
                Check(Saved() == blocked && header.Record == blocked, "真实外角碰到屏幕边缘不挪位或改尺寸");
                header.UpdateLayout(); gripPoint = Center(header.ResizeGrip); CheckHit(header, gripPoint);
                await Drag(gripPoint, gripPoint + new Vector(-12 * area.Scale, 0));
                Check(Saved().X == blocked.X && Saved().Y == blocked.Y && Saved().HeaderWidth < blocked.HeaderWidth,
                    "真实尺寸受阻后反向缩小，原点保持不变");
                blocked = Saved(); await OpenStyle(); await Choose(HeaderPosition.Left);
                Check(dialog!.PositionChoice.SelectedIndex == (int)HeaderPosition.Top && dialog.Interaction.Draft == blocked
                    && header.Record == blocked && dialog.Error.Text.Length > 0, "真实方向切换受阻保留有效草稿、原点并提示");
                dialog.Close(); dialog = null;
                current = Saved(); geometry = FolderGeometry.For(current, area.Scale);
                layout = workspace.BeginLayout(target.Id)!; layout.BeginDrag(0, 0);
                layout.DragTo(0, area.Y + area.Height - geometry.Height - current.Y);
                if (!(await workspace.CommitLayoutAsync(layout)).Succeeded) throw new IOException("底部滑块边界夹具准备失败。");
                runtime.Render(); await Task.Delay(150); blocked = Saved();
                await OpenStyle();
                var thumb = FindElements<System.Windows.Controls.Primitives.Thumb>(dialog!.SideWidth).Single();
                await MouseAt(Center(thumb), false); Key(0x23); await Task.Delay(150);
                Check(dialog.Interaction.Draft == blocked && header.Record == blocked && Saved() == blocked
                    && dialog.SideWidth.Value == blocked.HeaderHeight && dialog.Error.Text.Length > 0,
                    "真实厚度滑块越界保留最后有效尺寸及原点并提示");
                dialog.Close(); dialog = null;
                if (!(await workspace.ToggleFolderAsync(target.Id)).Succeeded) throw new IOException("底部折叠夹具准备失败。");
                current = Saved(); layout = workspace.BeginLayout(target.Id)!; layout.BeginDrag(0, 0);
                layout.DragTo(0, area.Y + area.Height - (int)Math.Ceiling(current.HeaderHeight * area.Scale) - current.Y);
                if (!(await workspace.CommitLayoutAsync(layout)).Succeeded) throw new IOException("底部展开受阻夹具准备失败。");
                runtime.Render(); await Task.Delay(150); header.UpdateLayout(); blocked = Saved();
                var expandPoint = Center(header.ExpandButton); CheckHit(header, expandPoint); await MouseAt(expandPoint, false); await Task.Delay(150);
                Check(Saved() == blocked && header.Record == blocked && !blocked.Expanded, "真实展开按钮受屏幕边界阻挡保持折叠和原点");
                result = failures == 0 ? 0 : 1;
            }
            catch (Exception e) { File.AppendAllText(log, "失败：" + e + "\n"); }
            finally
            {
                mouse_event(4, 0, 0, 0, UIntPtr.Zero); dialog?.Close(); runtime?.Dispose();
                if (desktopShown) Desktop(); SetCursorPos(cursor.X, cursor.Y); app.Shutdown();
            }
        };
        app.Run();
        var full = Path.GetFullPath(fixture);
        if (!full.StartsWith(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(full).StartsWith("Kage-style-feedback-", StringComparison.Ordinal)) throw new IOException("拒绝清理隔离范围外的目录。");
        Directory.Delete(full, true);
        Console.WriteLine(File.ReadAllText(log));
        return result;
        void Check(bool passed, string message)
        { if (!passed) failures++; File.AppendAllText(log, (passed ? "通过：" : "失败：") + message + "\n"); }
    }

    private static System.Collections.Generic.IEnumerable<T> FindElements<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T item) yield return item;
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in FindElements<T>(System.Windows.Media.VisualTreeHelper.GetChild(root, i))) yield return child;
    }
}
