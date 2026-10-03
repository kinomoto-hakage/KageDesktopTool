using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

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
    private bool disposed;
    private ScrollViewer? scroll;
    internal bool Active => origin != null || boxing || menuOpen;

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
            Select(initial);
            Finish();
            e.Handled = true;
        };
    }

    private bool OnScrollBar(DependencyObject? source) => FolderHeader.FindParent<ScrollBar>(source) != null;
    private void Down(object sender, MouseButtonEventArgs e)
    {
        if (OnScrollBar(e.OriginalSource as DependencyObject) || Runtime.Current.Moving || Runtime.Current.Interacting) return;
        pressed = FolderHeader.FindParent<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (e.ClickCount == 2)
        {
            Finish();
            if (pressed?.Tag is string path)
            {
                if (!pressed.IsSelected) { items.SelectedItems.Clear(); pressed.IsSelected = true; }
                Runtime.Open(path);
            }
            e.Handled = true;
            return;
        }
        items.Focus();
        initial = contents.SelectedPaths().ToHashSet(StringComparer.OrdinalIgnoreCase);
        modifiers = Keyboard.Modifiers;
        scroll = Descendant<ScrollViewer>(items);
        var point = e.GetPosition(contents.Viewport);
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
            if ((modifiers & ModifierKeys.Shift) == 0) anchor = hit;
        }
        else if (modifiers == ModifierKeys.None) items.SelectedItems.Clear();
        contents.Viewport.CaptureMouse();
        e.Handled = true;
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
        { items.SelectedItems.Clear(); pressed.IsSelected = true; }
        Finish();
        e.Handled = true;
    }

    private void RightDown(object sender, MouseButtonEventArgs e)
    {
        var item = FolderHeader.FindParent<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item == null) return;
        if (!item.IsSelected) { items.SelectedItems.Clear(); item.IsSelected = true; }
        e.Handled = true;
    }

    private async void RightUp(object sender, MouseButtonEventArgs e)
    {
        var item = FolderHeader.FindParent<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item == null) return;
        e.Handled = true;
        await ShowMenuAsync(items.PointToScreen(e.GetPosition(items)));
    }

    private async Task ShowMenuAsync(Point screen)
    {
        if (menuOpen || Runtime.Current.Moving || Runtime.Current.Interacting) return;
        menuOpen = true;
        try
        {
            var rename = ShellContextMenu.Show(Window.GetWindow(items), contents.SelectedPaths(), screen);
            if (rename != null) new ContentRenameDialog(contents.FolderId, rename).ShowDialog();
        }
        catch (Exception e) { Runtime.Current.Balloon("文件菜单无法完成：" + e.Message); }
        finally
        {
            menuOpen = false;
            var result = await Runtime.Current.Workspace.RefreshAsync(WindowsDesktop.Displays());
            if (!disposed) Runtime.Current.Render();
            if (!result.Succeeded) Runtime.Current.Balloon(result.Message);
        }
    }

    private void Select(HashSet<string> selected)
    { foreach (ListBoxItem item in items.Items) item.IsSelected = selected.Contains((string)item.Tag); }
    private void Finish()
    {
        origin = null; boxing = false;
        scrollTimer.Stop();
        contents.Feedback.Children.Clear();
        if (contents.Viewport.IsMouseCaptured) contents.Viewport.ReleaseMouseCapture();
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
