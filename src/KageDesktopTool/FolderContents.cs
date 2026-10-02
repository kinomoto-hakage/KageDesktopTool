using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Kage.Workspace;

namespace Kage.Desktop;

internal sealed class FolderContents : DockPanel
{
    internal ListBox Items { get; }
    private readonly TextBlock notice;
    private readonly Button view;
    private FolderSnapshot? snapshot;
    private double scale;
    private volatile int generation;
    private bool loading;
    private volatile bool disposed;
    internal Task IconsLoaded { get; private set; } = Task.CompletedTask;

    internal FolderContents(Guid id)
    {
        Margin = new Thickness(10, 0, 10, 10);
        var toolbar = new DockPanel { Margin = new Thickness(3, 7, 3, 8) };
        view = new Button { Foreground = Brushes.White, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(8, 3, 8, 3), ToolTip = "切换列表／网格" };
        view.Click += async (_, _) =>
        {
            if (snapshot == null || Runtime.Current.Interacting) return;
            var result = await Runtime.Current.Workspace.SetViewAsync(id, !snapshot.Folder.Grid);
            Runtime.Current.Render();
            if (!result.Succeeded) Runtime.Current.Balloon(result.Message);
        };
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
        Items.MouseDoubleClick += (_, e) =>
        {
            var item = FolderHeader.FindParent<ListBoxItem>(e.OriginalSource as DependencyObject);
            if (item?.Tag is string path) Runtime.Open(path);
        };
        Children.Add(Items);
    }

    internal void Update(FolderSnapshot folder, double currentScale)
    {
        notice.Text = folder.Notice ?? "";
        notice.Visibility = folder.Notice == null ? Visibility.Collapsed : Visibility.Visible;
        view.Content = folder.Folder.Grid ? "列表 ☷" : "网格 ▦";
        var rebuild = snapshot == null || folder.Folder.Grid != snapshot.Folder.Grid || currentScale != scale || !folder.Entries.SequenceEqual(snapshot.Entries);
        snapshot = folder;
        scale = currentScale;
        if (rebuild)
        {
            generation++;
            BuildItems(folder);
        }
        // 定期刷新也核对 Shell 覆盖／关联变化，布局输入不走此入口。
        if (!loading) IconsLoaded = LoadIconsAsync();
    }

    private void BuildItems(FolderSnapshot folder)
    {
        var grid = folder.Folder.Grid;
        var metrics = NativeViewMetrics.ForScale(scale);
        var factory = new FrameworkElementFactory(grid ? typeof(WrapPanel) : typeof(StackPanel));
        factory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        Items.ItemsPanel = new ItemsPanelTemplate(factory);
        var selected = Items.SelectedItems.Cast<ListBoxItem>().Select(item => (string)item.Tag).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Items.Items.Clear();
        foreach (var entry in folder.Entries)
        {
            var image = new Image { Width = grid ? metrics.GridIcon : metrics.SmallIcon, Height = grid ? metrics.GridIcon : metrics.SmallIcon, HorizontalAlignment = grid ? HorizontalAlignment.Center : HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            var text = new TextBlock { Text = entry.Name, Foreground = Brushes.White, FontFamily = SystemFonts.IconFontFamily, FontSize = SystemFonts.IconFontSize, FontStyle = SystemFonts.IconFontStyle, FontWeight = SystemFonts.IconFontWeight, TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = grid ? TextAlignment.Center : TextAlignment.Left, VerticalAlignment = grid ? VerticalAlignment.Top : VerticalAlignment.Center, TextWrapping = grid ? TextWrapping.Wrap : TextWrapping.NoWrap };
            var cell = new Grid();
            if (grid)
            {
                cell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(metrics.GridIcon + 8) });
                cell.RowDefinitions.Add(new RowDefinition());
                Grid.SetRow(text, 1);
                text.Margin = new Thickness(3, 0, 3, 0);
            }
            else
            {
                cell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(metrics.SmallIcon + 4) });
                cell.ColumnDefinitions.Add(new ColumnDefinition());
                Grid.SetColumn(text, 1);
            }
            cell.Children.Add(image);
            cell.Children.Add(text);
            Items.Items.Add(new ListBoxItem { Tag = entry.ActualPath, Content = cell, Width = grid ? metrics.GridWidth : double.NaN, Height = grid ? metrics.GridHeight : metrics.ListHeight, BorderThickness = new Thickness(1 / scale), Margin = new Thickness(0), Padding = new Thickness(2, 0, 2, 0), HorizontalAlignment = grid ? HorizontalAlignment.Left : HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch, ToolTip = entry.ActualPath, IsSelected = selected.Contains(entry.ActualPath) });
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
                var items = Items.Items.Cast<ListBoxItem>().ToArray();
                var paths = items.Select(item => (string)item.Tag).ToArray();
                var icons = await Task.Run(() => paths.TakeWhile(_ => !disposed && currentGeneration == generation).Select(path =>
                {
                    try { return (Source: ShellIcons.ForFile(path, small), Error: (string?)null); }
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

    internal void Dispose() { disposed = true; generation++; }
}
