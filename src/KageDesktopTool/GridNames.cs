using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Kage.Desktop;

internal sealed partial class FolderContents
{
    private string? currentNamePath;
    private readonly List<string> nameHistory = new();
    private bool rebuildingNames;
    private bool namesQueued;
    private bool revealName;
    private double? restoreOffset;

    private void InitializeNames()
    {
        Items.SelectionChanged += (_, e) =>
        {
            if (rebuildingNames) return;
            var added = e.AddedItems.Cast<ListBoxItem>().LastOrDefault();
            var changed = added != null && !string.Equals(currentNamePath, (string)added.Tag, StringComparison.OrdinalIgnoreCase);
            if (added != null) RememberName((string)added.Tag);
            QueueNames(changed);
        };
        Items.KeyUp += (_, e) =>
        {
            if (FolderHeader.FindParent<ListBoxItem>(Keyboard.FocusedElement as DependencyObject) is { IsSelected: true } focused)
            {
                if (e.Key is Key.Up or Key.Down or Key.Left or Key.Right or Key.Home or Key.End or Key.PageUp or Key.PageDown or Key.Space)
                    input.RememberKeyboardAnchor(focused);
                SetCurrentName(focused);
            }
        };
        Items.SizeChanged += (_, _) => QueueNames(false);
    }

    internal void SetCurrentName(ListBoxItem item)
    {
        var changed = item.IsSelected && !string.Equals(currentNamePath, (string)item.Tag, StringComparison.OrdinalIgnoreCase);
        if (item.IsSelected) RememberName((string)item.Tag);
        else currentNamePath = null;
        QueueNames(changed);
    }

    private void RememberName(string path)
    {
        nameHistory.RemoveAll(previous => string.Equals(previous, path, StringComparison.OrdinalIgnoreCase));
        nameHistory.Add(path);
        currentNamePath = path;
    }

    private void RemapNamePath(string previous, string current)
    {
        if (string.Equals(currentNamePath, previous, StringComparison.OrdinalIgnoreCase)) currentNamePath = current;
        for (var i = 0; i < nameHistory.Count; i++)
            if (string.Equals(nameHistory[i], previous, StringComparison.OrdinalIgnoreCase)) nameHistory[i] = current;
    }

    private void QueueNames(bool reveal, double? offset = null)
    {
        revealName |= reveal;
        if (offset != null) restoreOffset = offset;
        if (namesQueued) return;
        namesQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            namesQueued = false;
            if (disposed) return;
            UpdateNames();
        }));
    }

    private void UpdateNames()
    {
        var selected = Items.Items.Cast<ListBoxItem>().Where(item => item.IsSelected).ToArray();
        var selectedPaths = selected.Select(item => (string)item.Tag).ToHashSet(StringComparer.OrdinalIgnoreCase);
        nameHistory.RemoveAll(path => !selectedPaths.Contains(path));
        var recent = nameHistory.LastOrDefault();
        var current = selected.FirstOrDefault(item => string.Equals((string)item.Tag, currentNamePath, StringComparison.OrdinalIgnoreCase))
            ?? selected.FirstOrDefault(item => string.Equals((string)item.Tag, recent, StringComparison.OrdinalIgnoreCase))
            ?? selected.LastOrDefault();
        currentNamePath = current?.Tag as string;
        Items.UpdateLayout();
        foreach (ListBoxItem item in Items.Items)
        {
            var cell = (Grid)item.Content;
            var label = (TextBlock)cell.Children[1];
            var active = GridView && item == current;
            if (cell.Children.Count == 2 && GridView)
            {
                var canvas = new Canvas();
                Grid.SetRow(canvas, 1);
                cell.Children.Add(canvas);
            }
            double overflow = 0;
            if (cell.Children.Count > 2)
            {
                var canvas = (Canvas)cell.Children[2];
                canvas.Children.Clear();
                label.Visibility = Visibility.Visible;
                if (active)
                {
                    var full = new TextBlock
                    {
                        Text = label.Text, Foreground = label.Foreground, FontFamily = label.FontFamily, FontSize = label.FontSize,
                        FontWeight = label.FontWeight, FontStyle = label.FontStyle, TextAlignment = TextAlignment.Center,
                        TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.None,
                        LineHeight = label.LineHeight, LineStackingStrategy = label.LineStackingStrategy,
                        Width = Math.Max(1, cell.ActualWidth - 6), Background = new SolidColorBrush(Color.FromRgb(42, 76, 105))
                    };
                    full.Measure(new Size(full.Width, double.PositiveInfinity));
                    // 也覆盖未填满三行但因长单词而发生行内省略的名称。
                    full.Height = full.DesiredSize.Height;
                    Canvas.SetLeft(full, 3);
                    canvas.Children.Add(full);
                    label.Visibility = Visibility.Hidden;
                    overflow = Math.Max(0, label.TranslatePoint(new Point(), item).Y + full.Height + 3 - item.ActualHeight);
                }
            }
            Panel.SetZIndex(item, active ? 1 : 0);
            item.SetValue(ContentGridPanel.NameOverflowProperty, overflow);
        }
        Items.UpdateLayout();
        var scroll = ContentPointerInput.Descendant<ScrollViewer>(Items);
        if (scroll != null)
        {
            if (restoreOffset is { } saved) scroll.ScrollToVerticalOffset(Math.Min(saved, scroll.ScrollableHeight));
            if (revealName && current != null && GridView)
            {
                // 只保证图标和名称开头可见；全文末尾由用户滚动读取。
                var top = current.TranslatePoint(new Point(), Items).Y;
                var label = (TextBlock)((Grid)current.Content).Children[1];
                var startBottom = label.TranslatePoint(new Point(), Items).Y + label.LineHeight;
                if (top < 0) scroll.ScrollToVerticalOffset(scroll.VerticalOffset + top);
                else if (startBottom > scroll.ViewportHeight) scroll.ScrollToVerticalOffset(scroll.VerticalOffset + startBottom - scroll.ViewportHeight);
            }
        }
        revealName = false;
        restoreOffset = null;
    }
}
