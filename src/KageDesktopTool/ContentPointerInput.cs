using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Kage.Workspace;

namespace Kage.Desktop;

// 点击、框选和图标拖动共用当前命中与选择集合，避免 Preview 吞掉双击。
internal sealed class ContentPointerInput : IDisposable
{
    private readonly FolderContents contents;
    private readonly ListBox items;
    private readonly DispatcherTimer scrollTimer;
    private Point? origin;
    private ListBoxItem? pressed;
    private string? anchor;
    private HashSet<string> initial = new(StringComparer.OrdinalIgnoreCase);
    private ModifierKeys modifiers;
    private bool boxing;
    private bool menuOpen;
    private ContentRenameDialog? renameDialog;
    private bool disposed;
    private ScrollViewer? scroll;
    private long lastPointerDown;
    private Point lastPointerScreen;
    private string? lastPointerPath;
    internal bool Active => origin != null || boxing || menuOpen;
    internal bool OwnsCommandWindow(IntPtr window) => menuOpen && (ShellContextMenu.OwnsWindow(window)
        || renameDialog != null && (window == new WindowInteropHelper(renameDialog).Handle
            || WindowsDesktop.IsChild(new WindowInteropHelper(renameDialog).Handle, window)));

    internal ContentPointerInput(FolderContents contents)
    {
        this.contents = contents;
        items = contents.Items;
        scrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(45) };
        scrollTimer.Tick += (_, _) => { if (boxing) Box(Mouse.GetPosition(contents.Viewport), true); };
        contents.Viewport.PreviewMouseLeftButtonDown += Down;
        contents.Viewport.PreviewMouseMove += Move;
        contents.Viewport.PreviewMouseLeftButtonUp += Up;
        contents.Viewport.LostMouseCapture += (_, _) => { if (origin != null) Finish(); };
        items.PreviewMouseRightButtonDown += RightDown;
        items.PreviewMouseRightButtonUp += RightUp;
        items.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || !Active) return;
            if (menuOpen)
            {
                ShellContextMenu.CancelPending();
                e.Handled = true;
                return;
            }
            Select(initial);
            Finish();
            e.Handled = true;
        };
    }

    private bool OnScrollBar(DependencyObject? source) => FolderHeader.FindParent<ScrollBar>(source) != null;
    private void Down(object sender, MouseButtonEventArgs e)
    {
        e.Handled = PointerDown(e.OriginalSource as DependencyObject, e.GetPosition(contents.Viewport), e.ClickCount);
    }

    internal bool PointerDown(DependencyObject? source, Point point, int clickCount)
    {
        if (OnScrollBar(source) || Runtime.Current.Moving || Runtime.Current.Interacting) return false;
        // 第一次透明像素由原生背景接收，选中后第二次可能落到 WPF 有色像素。
        // 在共用输入入口记录真实屏幕轨迹，让两条原生路由组成同一次双击。
        var screen = contents.Viewport.PointToScreen(point);
        var tick = Environment.TickCount64;
        var size = System.Windows.Forms.SystemInformation.DoubleClickSize;
        pressed = FolderHeader.FindParent<ListBoxItem>(source);
        var currentPath = pressed?.Tag as string;
        if (tick - lastPointerDown <= System.Windows.Forms.SystemInformation.DoubleClickTime
            && Math.Abs(screen.X - lastPointerScreen.X) <= size.Width / 2.0
            && Math.Abs(screen.Y - lastPointerScreen.Y) <= size.Height / 2.0 && currentPath == lastPointerPath) clickCount = 2;
        if (currentPath != lastPointerPath) clickCount = 1;
        lastPointerDown = tick; lastPointerScreen = screen;
        lastPointerPath = currentPath;
        Runtime.Current.Selection.Claim(contents);
        if (clickCount == 2)
        {
            Finish();
            if (pressed?.Tag is string path)
            {
                if (!pressed.IsSelected) { items.SelectedItems.Clear(); pressed.IsSelected = true; }
                Runtime.Open(path);
            }
            return true;
        }
        items.Focus();
        initial = contents.SelectedPaths().ToHashSet(StringComparer.OrdinalIgnoreCase);
        modifiers = Keyboard.Modifiers;
        scroll = Descendant<ScrollViewer>(items);
        origin = new Point(point.X, point.Y + (scroll?.VerticalOffset ?? 0));
        if (pressed?.Tag is string hit)
        {
            if ((modifiers & ModifierKeys.Shift) != 0 && anchor != null)
            {
                var all = items.Items.Cast<ListBoxItem>().ToArray();
                var start = Array.FindIndex(all, item => (string)item.Tag == anchor);
                var end = Array.IndexOf(all, pressed);
                var selected = (modifiers & ModifierKeys.Control) != 0 ? new HashSet<string>(initial, StringComparer.OrdinalIgnoreCase) : new(StringComparer.OrdinalIgnoreCase);
                if (start < 0) start = end;
                foreach (var item in all.Skip(Math.Min(start, end)).Take(Math.Abs(end - start) + 1)) selected.Add((string)item.Tag);
                Select(selected);
            }
            else if ((modifiers & ModifierKeys.Control) != 0) pressed.IsSelected = !pressed.IsSelected;
            else if (!pressed.IsSelected) { items.SelectedItems.Clear(); pressed.IsSelected = true; }
            if ((modifiers & ModifierKeys.Shift) == 0 || anchor == null)
            {
                anchor = hit;
                contents.Items.SetSelectionAnchor(pressed);
            }
            contents.SetCurrentName(pressed);
        }
        else if (modifiers == ModifierKeys.None) items.SelectedItems.Clear();
        contents.Viewport.CaptureMouse();
        return true;
    }

    private async void Move(object sender, MouseEventArgs e)
    {
        if (origin == null || e.LeftButton != MouseButtonState.Pressed || Runtime.Current.Moving) return;
        var point = e.GetPosition(contents.Viewport);
        var relativeOrigin = new Point(origin.Value.X, origin.Value.Y - (scroll?.VerticalOffset ?? 0));
        if (!boxing && Math.Abs(point.X - relativeOrigin.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(point.Y - relativeOrigin.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        e.Handled = true;
        if (pressed != null)
        {
            var paths = contents.SelectedPaths();
            if (!pressed.IsSelected) { Finish(); return; }
            Finish();
            if (paths.Length != 0) await FileDrag.DragAsync(items, paths, contents);
            return;
        }
        if (!boxing) { boxing = true; contents.Viewport.CaptureMouse(); scrollTimer.Start(); }
        Box(point, false);
    }

    private void Box(Point point, bool autoScroll)
    {
        if (origin == null) return;
        var height = contents.Viewport.ActualHeight;
        var width = contents.Viewport.ActualWidth;
        if (autoScroll && scroll != null)
        {
            if (point.Y < 22) scroll.ScrollToVerticalOffset(Math.Max(0, scroll.VerticalOffset - 14));
            else if (point.Y > height - 22) scroll.ScrollToVerticalOffset(scroll.VerticalOffset + 14);
            items.UpdateLayout();
        }
        point = new Point(Math.Clamp(point.X, 0, width), Math.Clamp(point.Y, 0, height));
        var start = new Point(origin.Value.X, origin.Value.Y - (scroll?.VerticalOffset ?? 0));
        var full = new Rect(start, point);
        var visible = Rect.Intersect(full, new Rect(0, 0, width, height));
        var hit = items.Items.Cast<ListBoxItem>().Where(item => full.IntersectsWith(contents.BoundsOf(item)))
            .Select(item => (string)item.Tag).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if ((modifiers & ModifierKeys.Control) != 0) hit.SymmetricExceptWith(initial);
        else if ((modifiers & ModifierKeys.Shift) != 0) hit.UnionWith(initial);
        Select(hit);
        contents.Feedback.Children.Clear();
        if (!visible.IsEmpty)
        {
            var box = new Rectangle { Width = visible.Width, Height = visible.Height, Fill = new SolidColorBrush(Color.FromArgb(45, 40, 150, 255)), Stroke = Brushes.DeepSkyBlue, StrokeThickness = 1 };
            Canvas.SetLeft(box, visible.Left); Canvas.SetTop(box, visible.Top);
            contents.Feedback.Children.Add(box);
        }
    }

    private void Up(object sender, MouseButtonEventArgs e)
    {
        if (origin == null) return;
        if (!boxing && pressed != null && modifiers == ModifierKeys.None)
        {
            if (items.SelectedItems.Count != 1 || !pressed.IsSelected)
            { items.SelectedItems.Clear(); pressed.IsSelected = true; }
            contents.SetCurrentName(pressed);
        }
        Finish();
        e.Handled = true;
    }

    private void RightDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = PointerRightDown(e.OriginalSource as DependencyObject);
    }

    internal bool PointerRightDown(DependencyObject? source)
    {
        var item = FolderHeader.FindParent<ListBoxItem>(source);
        if (item == null) return false;
        Runtime.Current.Selection.Claim(contents);
        items.Focus();
        if (!item.IsSelected) { items.SelectedItems.Clear(); item.IsSelected = true; }
        anchor = (string)item.Tag;
        contents.Items.SetSelectionAnchor(item);
        contents.SetCurrentName(item);
        return true;
    }

    private async void RightUp(object sender, MouseButtonEventArgs e)
    {
        if (FolderHeader.FindParent<ListBoxItem>(e.OriginalSource as DependencyObject) == null) return;
        e.Handled = true;
        await PointerRightUpAsync(items.PointToScreen(e.GetPosition(items)));
    }

    internal Task PointerRightUpAsync(Point screen) => ShowMenuAsync(screen, Keyboard.Modifiers);

    private async Task ShowMenuAsync(Point screen, ModifierKeys modifiers)
    {
        if (menuOpen || Runtime.Current.Moving || Runtime.Current.Interacting) return;
        menuOpen = true;
        ShellCommandResult? command = null;
        var paths = contents.SelectedPaths();
        try
        {
            command = await ShellContextMenu.ShowAsync(paths, screen,
                (modifiers & ModifierKeys.Shift) != 0, (modifiers & ModifierKeys.Control) != 0,
                Runtime.Current.Headers.TryGetValue(contents.FolderId, out var header) ? header.Handle : IntPtr.Zero);
            if (command?.RenamePath is { } rename && !disposed)
            {
                renameDialog = new ContentRenameDialog(contents.FolderId, rename);
                renameDialog.ShowDialog();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { Runtime.Current.Complete("文件菜单", new OperationResult(Outcome.Failed, "Windows 菜单未正常返回：" + e.Message + "\n所选实际路径：\n" + string.Join("\n", paths))); }
        finally
        {
            menuOpen = false;
            renameDialog = null;
            if (!disposed)
            {
                var result = await Runtime.Current.Workspace.RefreshAsync(WindowsDesktop.Displays());
                Runtime.Current.Render();
                if (command is { RenamePath: null }) Runtime.Current.Complete(command, result);
                else if (!result.Succeeded) Runtime.Current.Complete("菜单后刷新", result);
            }
        }
    }

    private void Select(HashSet<string> selected)
    { foreach (ListBoxItem item in items.Items) item.IsSelected = selected.Contains((string)item.Tag); }
    internal void ClearSelection()
    {
        lastPointerDown = 0; lastPointerPath = null;
        if (menuOpen) ShellContextMenu.CancelPending();
        anchor = null;
        initial.Clear();
        pressed = null;
        if (origin != null || boxing) Finish();
    }
    internal void RememberKeyboardAnchor(ListBoxItem item)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Shift) == 0 || anchor == null) anchor = (string)item.Tag;
    }
    private void Finish()
    {
        origin = null; boxing = false;
        scrollTimer.Stop();
        contents.Feedback.Children.Clear();
        if (contents.Viewport.IsMouseCaptured) contents.Viewport.ReleaseMouseCapture();
        if (!disposed) Runtime.Current.Dispatch(Runtime.Current.Render);
    }
    internal static T? Descendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            var nested = Descendant<T>(child);
            if (nested != null) return nested;
        }
        return null;
    }
    public void Dispose() { disposed = true; Finish(); }
}
