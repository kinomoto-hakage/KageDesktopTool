using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Shapes;
using Kage.Workspace;

namespace Kage.Desktop;

internal sealed class FolderContents : DockPanel
{
    internal ListBox Items { get; }
    private readonly TextBlock notice;
    private readonly Button view;
    private readonly Guid id;
    private readonly ContentPointerInput input;
    internal Grid Viewport { get; }
    internal Canvas Feedback { get; }
    internal bool GridView => snapshot?.Folder.Grid == true;
    internal Guid FolderId => id;
    internal bool InputActive => input.Active;
    private FolderSnapshot? snapshot;
    private double scale;
    private int metricsRevision = -1;
    private volatile int generation;
    private bool loading;
    private volatile bool disposed;
    internal Task IconsLoaded { get; private set; } = Task.CompletedTask;

    internal FolderContents(Guid id)
    {
        this.id = id;
        Margin = new Thickness(10, 0, 10, 10);
        var toolbar = new DockPanel { Margin = new Thickness(3, 7, 3, 8) };
        view = new Button { Foreground = Brushes.White, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(8, 3, 8, 3), ToolTip = "查看方式、图标尺寸和排序" };
        view.Click += (_, _) => ShowViewMenu();
        SetDock(view, Dock.Right);
        toolbar.Children.Add(view);
        toolbar.Children.Add(new TextBlock { Text = "内容", Foreground = Brushes.WhiteSmoke, VerticalAlignment = VerticalAlignment.Center });
        SetDock(toolbar, Dock.Top);
        Children.Add(toolbar);
        notice = new TextBlock { Foreground = Brushes.WhiteSmoke, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(3, 0, 3, 5), FontSize = 11 };
        SetDock(notice, Dock.Top);
        Children.Add(notice);
        Items = new ListBox { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0), SelectionMode = SelectionMode.Extended, Foreground = Brushes.White, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        ScrollViewer.SetHorizontalScrollBarVisibility(Items, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(Items, ScrollBarVisibility.Auto);
        ScrollViewer.SetCanContentScroll(Items, false);
        ContentScrollStyle.Apply(Items);
        Viewport = new Grid { ClipToBounds = true, Background = Brushes.Transparent };
        Viewport.ColumnDefinitions.Add(new ColumnDefinition());
        Viewport.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        Feedback = new Canvas { IsHitTestVisible = false, ClipToBounds = true };
        Grid.SetColumnSpan(Feedback, 2);
        Viewport.Children.Add(Items);
        Viewport.Children.Add(Feedback);
        Children.Add(Viewport);
        input = new ContentPointerInput(this);
        Items.ContextMenuOpening += (_, e) =>
        {
            if (FolderHeader.FindParent<ListBoxItem>(e.OriginalSource as DependencyObject) != null) e.Handled = true;
        };
    }

    internal void Update(FolderSnapshot folder, double currentScale)
    {
        notice.Text = folder.Notice ?? "";
        notice.Visibility = folder.Notice == null ? Visibility.Collapsed : Visibility.Visible;
        view.Content = "查看 ▾";
        var rebuild = snapshot == null || folder.Folder.Grid != snapshot.Folder.Grid || currentScale != scale
            || folder.Folder.ListIconSize != snapshot.Folder.ListIconSize || folder.Folder.GridIconSize != snapshot.Folder.GridIconSize
            || metricsRevision != NativeViewMetrics.Revision || !folder.Entries.SequenceEqual(snapshot.Entries);
        var previousEntries = snapshot?.Entries;
        snapshot = folder;
        scale = currentScale;
        if (rebuild)
        {
            generation++;
            BuildItems(folder, previousEntries);
            metricsRevision = NativeViewMetrics.Revision;
        }
        // 定期刷新也核对 Shell 覆盖／关联变化，布局输入不走此入口。
        if (!loading) IconsLoaded = LoadIconsAsync();
    }

    private void BuildItems(FolderSnapshot folder, System.Collections.Generic.IReadOnlyList<ContentEntry>? previousEntries)
    {
        var grid = folder.Folder.Grid;
        var metrics = NativeViewMetrics.ForScale(scale);
        var iconSize = grid ? folder.Folder.GridIconSize : folder.Folder.ListIconSize;
        var gridWidth = Math.Max(metrics.GridWidth, iconSize + 24);
        var gridHeight = Math.Max(metrics.GridHeight, iconSize + metrics.FontSize * 2 + 18);
        var rowHeight = iconSize == (int)ContentIconSize.Small ? metrics.ListHeight : Math.Max(metrics.ListHeight, iconSize + 8);
        var factory = new FrameworkElementFactory(grid ? typeof(WrapPanel) : typeof(StackPanel));
        factory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        Items.ItemsPanel = new ItemsPanelTemplate(factory);
        var selected = Items.SelectedItems.Cast<ListBoxItem>().Select(item => (string)item.Tag).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var previousByPath = (previousEntries ?? []).ToDictionary(entry => entry.ActualPath, StringComparer.OrdinalIgnoreCase);
        var currentByPath = folder.Entries.ToDictionary(entry => entry.ActualPath, StringComparer.OrdinalIgnoreCase);
        bool SameIdentity(ContentEntry left, ContentEntry right) => left.Identity == null || right.Identity == null || left.Identity == right.Identity;
        var preserved = selected.Where(path => currentByPath.TryGetValue(path, out var current)
            && previousByPath.TryGetValue(path, out var previous) && SameIdentity(previous, current)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = (previousEntries ?? []).Where(entry => entry.Identity != null
            && (!currentByPath.TryGetValue(entry.ActualPath, out var current) || !SameIdentity(entry, current))).GroupBy(entry => entry.Identity);
        foreach (var group in missing)
        {
            var removed = group.ToArray();
            if (removed.Length != 1 || !selected.Contains(removed[0].ActualPath)) continue;
            var candidates = folder.Entries.Where(entry => entry.Identity == group.Key
                && (!previousByPath.TryGetValue(entry.ActualPath, out var previous) || !SameIdentity(entry, previous))).ToArray();
            if (candidates.Length == 1) preserved.Add(candidates[0].ActualPath);
        }
        Items.Items.Clear();
        foreach (var entry in folder.Entries)
        {
            var image = new Image { Width = iconSize, Height = iconSize, HorizontalAlignment = grid ? HorizontalAlignment.Center : HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            var text = new TextBlock { Text = entry.DisplayName, Foreground = Brushes.White, FontFamily = new FontFamily(metrics.FontFamily), FontSize = metrics.FontSize,
                FontStyle = metrics.Italic ? FontStyles.Italic : FontStyles.Normal, FontWeight = metrics.Bold ? FontWeights.Bold : FontWeights.Normal,
                TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = grid ? TextAlignment.Center : TextAlignment.Left, VerticalAlignment = grid ? VerticalAlignment.Top : VerticalAlignment.Center, TextWrapping = grid ? TextWrapping.Wrap : TextWrapping.NoWrap };
            var cell = new Grid();
            if (grid)
            {
                cell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(iconSize + 8) });
                cell.RowDefinitions.Add(new RowDefinition());
                Grid.SetRow(text, 1);
                text.Margin = new Thickness(3, 0, 3, 0);
            }
            else
            {
                cell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(iconSize + 8) });
                cell.ColumnDefinitions.Add(new ColumnDefinition());
                Grid.SetColumn(text, 1);
            }
            cell.Children.Add(image);
            cell.Children.Add(text);
            Items.Items.Add(new ListBoxItem { Tag = entry.ActualPath, Content = cell, Width = grid ? gridWidth : double.NaN, Height = grid ? gridHeight : rowHeight, BorderThickness = new Thickness(1 / scale), Margin = new Thickness(0), Padding = new Thickness(2, 0, 2, 0), HorizontalAlignment = grid ? HorizontalAlignment.Left : HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch, ToolTip = entry.ActualPath, IsSelected = preserved.Contains(entry.ActualPath) });
        }
    }

    private async Task LoadIconsAsync()
    {
        loading = true;
        try
        {
            do
            {
                var currentGeneration = generation;
                var small = !snapshot!.Folder.Grid;
                var metrics = NativeViewMetrics.ForScale(scale);
                var iconPixels = (int)Math.Ceiling((small ? snapshot.Folder.ListIconSize : snapshot.Folder.GridIconSize) * scale);
                var items = Items.Items.Cast<ListBoxItem>().ToArray();
                var paths = items.Select(item => (string)item.Tag).ToArray();
                var icons = await Task.Run(() => paths.TakeWhile(_ => !disposed && currentGeneration == generation).Select(path =>
                {
                    try { return (Source: ShellIcons.ForFile(path, small, iconPixels), Error: (string?)null); }
                    catch (Exception e) { return (Source: (ImageSource?)null, Error: e.Message); }
                }).ToArray());
                if (disposed) return;
                if (currentGeneration != generation) continue;
                for (var i = 0; i < items.Length; i++)
                {
                    ((Image)((Grid)items[i].Content).Children[0]).Source = icons[i].Source;
                    items[i].ToolTip = paths[i] + (icons[i].Source == null ? "\n图标读取失败：" + (icons[i].Error ?? "Windows Shell 未返回图标。") : "");
                }
                break;
            } while (!disposed);
        }
        finally { loading = false; }
    }

    private void ShowViewMenu()
    {
        if (snapshot == null || Runtime.Current.Interacting || Runtime.Current.Moving) return;
        var folder = snapshot.Folder;
        var menu = new ContextMenu();
        void Option(ItemsControl parent, string label, bool selected, bool grid, int size, ContentSortKey sort, bool descending)
        {
            var item = new MenuItem { Header = label, IsCheckable = true, IsChecked = selected };
            item.Click += async (_, _) =>
            {
                var result = await Runtime.Current.Workspace.SetContentViewAsync(id, grid, size, sort, descending);
                Runtime.Current.Render();
                Runtime.Current.Complete("查看与排序", result);
            };
            parent.Items.Add(item);
        }
        Option(menu, "网格", folder.Grid, true, folder.GridIconSize, folder.SortKey, folder.SortDescending);
        Option(menu, "列表", !folder.Grid, false, folder.ListIconSize, folder.SortKey, folder.SortDescending);
        var sizes = new MenuItem { Header = "图标尺寸" };
        var currentSize = folder.Grid ? folder.GridIconSize : folder.ListIconSize;
        foreach (var (label, size) in new[] { ("小", ContentIconSize.Small), ("中", ContentIconSize.Medium), ("大", ContentIconSize.Large), ("超大", ContentIconSize.ExtraLarge) })
            Option(sizes, $"{label} · {(int)size}", currentSize == (int)size, folder.Grid, (int)size, folder.SortKey, folder.SortDescending);
        menu.Items.Add(sizes);
        var sorting = new MenuItem { Header = "排序" };
        foreach (var (label, key) in new[] { ("名称", ContentSortKey.Name), ("修改日期", ContentSortKey.Modified), ("大小", ContentSortKey.Size), ("自定义", ContentSortKey.Custom) })
            Option(sorting, label, folder.SortKey == key, folder.Grid, currentSize, key, folder.SortDescending);
        sorting.Items.Add(new Separator());
        Option(sorting, "升序", !folder.SortDescending, folder.Grid, currentSize, folder.SortKey, false);
        Option(sorting, "降序", folder.SortDescending, folder.Grid, currentSize, folder.SortKey, true);
        menu.Items.Add(sorting);
        menu.PlacementTarget = view;
        menu.IsOpen = true;
    }

    internal string[] SelectedPaths() => Items.Items.Cast<ListBoxItem>().Where(item => item.IsSelected).Select(item => (string)item.Tag).ToArray();
    internal Rect BoundsOf(ListBoxItem item) => new(item.TranslatePoint(new Point(), Viewport), new Size(item.ActualWidth, item.ActualHeight));
    internal bool ContainsScreenPoint(Point screen) => new Rect(new Point(), Viewport.RenderSize).Contains(Viewport.PointFromScreen(screen));
    internal string? InsertionAt(Point screen, bool show)
    {
        var point = Viewport.PointFromScreen(screen);
        var items = Items.Items.Cast<ListBoxItem>().ToArray();
        var index = Array.FindIndex(items, item =>
        {
            var bounds = BoundsOf(item);
            return GridView ? point.Y < bounds.Top || point.Y <= bounds.Bottom && point.X < bounds.Left + bounds.Width / 2
                : point.Y < bounds.Top + bounds.Height / 2;
        });
        if (show)
        {
            Feedback.Children.Clear();
            var anchor = index < 0 ? items.LastOrDefault() : items[index];
            if (anchor != null)
            {
                var rect = BoundsOf(anchor);
                var line = new Rectangle { Fill = Brushes.DeepSkyBlue, Width = GridView ? 3 : rect.Width, Height = GridView ? rect.Height : 3 };
                Canvas.SetLeft(line, GridView && index < 0 ? rect.Right : rect.Left);
                Canvas.SetTop(line, !GridView && index < 0 ? rect.Bottom : rect.Top);
                Feedback.Children.Add(line);
            }
        }
        return index < 0 ? null : (string)items[index].Tag;
    }

    internal void Dispose() { input.Dispose(); disposed = true; generation++; }
}
