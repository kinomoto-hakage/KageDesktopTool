// Folder 头部与展示区处于同一桌面窗口，可分别折叠；多个窗口可同时展开。
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace Kage.DesktopFolderPrototype;

public sealed class FolderWindow : Window
{
    internal readonly FolderState State;
    internal string Path => System.IO.Path.Combine(Program.Storage, State.Name);
    internal IntPtr Handle => new WindowInteropHelper(this).Handle;
    internal IntPtr Host;
    internal bool LayoutHidden;
    internal double Scale => Handle == IntPtr.Zero ? VisualTreeHelper.GetDpi(this).DpiScaleX : Math.Max(1, Native.GetDpiForWindow(Handle) / 96.0);
    internal double TotalHeight => State.Height + (State.Expanded ? State.PanelHeight : 0);
    private readonly Border surface;
    private readonly Grid layout;
    private readonly Grid header;
    private readonly TextBlock title;
    private readonly TextBlock count;
    private readonly Button expand;
    private readonly Button toggle;
    private readonly DockPanel body;
    private readonly ListBox list;
    private FileSystemWatcher? watcher;
    private Point? start;
    private Point fileDragStart;
    private int oldX, oldY;
    private bool movingFiles;
    private bool applying;
    private bool closed;

    internal FolderWindow(FolderState state)
    {
        State = state; Width = State.Width; Height = TotalHeight;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; AllowDrop = true; Title = "Kage · " + state.Name; Icon = IconChoices.ApplicationImage;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        surface = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromArgb(55, 255, 255, 255)) };
        layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(State.Height) });
        layout.RowDefinitions.Add(new RowDefinition());
        header = new Grid { Margin = new Thickness(14, 0, 12, 0), Background = Brushes.Transparent };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        title = new TextBlock { Foreground = Brushes.White, FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Cursor = Cursors.SizeAll };
        count = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(230, 230, 230)), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 8, 0) };
        expand = SmallButton("▾"); expand.FontSize = 20; expand.Padding = new Thickness(1);
        expand.ToolTip = "展开／折叠"; expand.Click += (_, _) => Toggle();
        Grid.SetColumn(count, 1); Grid.SetColumn(expand, 2);
        header.Children.Add(title); header.Children.Add(count); header.Children.Add(expand);
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is Button || FindParent<Button>(e.OriginalSource as DependencyObject) != null) return;
            start = PointToScreen(e.GetPosition(this)); oldX = State.X; oldY = State.Y; header.CaptureMouse(); e.Handled = true;
        };
        header.MouseMove += (_, e) =>
        {
            if (start == null || e.LeftButton != MouseButtonState.Pressed) return;
            var point = PointToScreen(e.GetPosition(this));
            DesktopLayout.Move(this, oldX + (int)(point.X - start.Value.X), oldY + (int)(point.Y - start.Value.Y));
        };
        header.MouseLeftButtonUp += (_, _) => { start = null; header.ReleaseMouseCapture(); Program.Save(); };
        layout.Children.Add(header);
        body = new DockPanel { Margin = new Thickness(10, 0, 10, 10) };
        var toolbar = new DockPanel { Margin = new Thickness(3, 7, 3, 8) };
        toggle = SmallButton("网格"); toggle.ToolTip = "切换列表／网格";
        toggle.Click += (_, _) => { State.Grid = !State.Grid; Refresh(); Program.Save(); };
        DockPanel.SetDock(toggle, Dock.Right); toolbar.Children.Add(toggle);
        toolbar.Children.Add(new TextBlock { Text = "内容", Foreground = new SolidColorBrush(Color.FromRgb(224, 224, 224)), FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
        var separator = new Border { Height = 1, Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), Margin = new Thickness(0, 0, 0, 1) };
        DockPanel.SetDock(separator, Dock.Top); body.Children.Add(separator);
        DockPanel.SetDock(toolbar, Dock.Top); body.Children.Add(toolbar);
        list = new ListBox { Background = Brushes.Transparent, BorderThickness = new Thickness(0), SelectionMode = SelectionMode.Extended, Foreground = Brushes.White };
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(list, ScrollBarVisibility.Auto);
        list.PreviewMouseLeftButtonDown += (_, e) => fileDragStart = e.GetPosition(list);
        list.PreviewMouseMove += (_, e) =>
        {
            if (movingFiles || e.LeftButton != MouseButtonState.Pressed) return;
            var delta = e.GetPosition(list) - fileDragStart;
            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            var paths = list.SelectedItems.Cast<ListBoxItem>().Select(item => (string)item.Tag).ToArray();
            if (paths.Length == 0) return;
            movingFiles = true;
            try { DragDrop.DoDragDrop(list, new DataObject(DataFormats.FileDrop, paths), DragDropEffects.Move); }
            finally { movingFiles = false; Refresh(); }
        };
        list.MouseDoubleClick += (_, e) => { if (FindParent<ListBoxItem>(e.OriginalSource as DependencyObject) is ListBoxItem item) Program.Open((string)item.Tag); };
        body.Children.Add(list); Grid.SetRow(body, 1); layout.Children.Add(body);
        var grip = new Thumb { Width = 14, Height = 14, Cursor = Cursors.SizeNWSE, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 3, 3), Opacity = .6 };
        grip.Template = GripTemplate(); Grid.SetRowSpan(grip, 2); layout.Children.Add(grip);
        grip.DragDelta += (_, e) => DesktopLayout.Resize(this, State.Width + e.HorizontalChange, State.Expanded ? State.Height : State.Height + e.VerticalChange, State.Expanded ? State.PanelHeight + e.VerticalChange : State.PanelHeight);
        grip.DragCompleted += (_, _) => Program.Save();
        surface.Child = layout; Content = surface;
        var menu = new ContextMenu();
        Menu(menu, "展开／折叠", Toggle);
        Menu(menu, "在资源管理器打开", () => Program.Open(Path));
        Menu(menu, "重命名", () => Program.Rename(this));
        Menu(menu, "颜色与透明度…", () => new StyleDialog(this).ShowDialog());
        menu.Items.Add(new Separator());
        Menu(menu, "删除入口，保留内容", () => Program.Delete(this, true));
        Menu(menu, "连同内容放入回收站", () => Program.Delete(this, false));
        ContextMenu = menu;
        SourceInitialized += (_, _) => Attach();
        DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; };
        Drop += (_, e) => { if (e.Data.GetData(DataFormats.FileDrop) is string[] files) { e.Effects = Program.MoveInto(this, files) ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; } };
        Closed += (_, _) => { closed = true; watcher?.Dispose(); };
        Watch(); Refresh();
    }

    private static ControlTemplate GripTemplate()
    {
        var factory = new FrameworkElementFactory(typeof(TextBlock));
        factory.SetValue(TextBlock.TextProperty, "◢"); factory.SetValue(TextBlock.ForegroundProperty, Brushes.White); factory.SetValue(TextBlock.FontSizeProperty, 12.0);
        return new ControlTemplate(typeof(Thumb)) { VisualTree = factory };
    }

    private static T? FindParent<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element != null) { if (element is T found) return found; element = VisualTreeHelper.GetParent(element); }
        return null;
    }

    private static Button SmallButton(string text) => new() { Content = text, Background = Brushes.Transparent, Foreground = Brushes.White, BorderThickness = new Thickness(0), Padding = new Thickness(8, 3, 8, 3), Cursor = Cursors.Hand, FontSize = 11 };

    internal void Toggle()
    { if (DesktopLayout.Toggle(this)) { Refresh(); Program.Save(); } }

    internal void Watch()
    {
        watcher?.Dispose();
        if (!Directory.Exists(Path)) return;
        watcher = new FileSystemWatcher(Path) { EnableRaisingEvents = true };
        void Queue(string path) => Dispatcher.BeginInvoke(() => { if (!closed && !movingFiles) { ShellIcons.Invalidate(path); Refresh(); } });
        watcher.Created += (_, e) => Queue(e.FullPath); watcher.Deleted += (_, e) => Queue(e.FullPath); watcher.Changed += (_, e) => Queue(e.FullPath); watcher.Renamed += (_, e) => Queue(e.FullPath);
    }

    internal void Refresh()
    {
        title.Text = State.Name;
        ApplyStyle();
        var paths = Directory.Exists(Path) ? Directory.GetFileSystemEntries(Path).OrderBy(System.IO.Path.GetFileName).ToArray() : Array.Empty<string>();
        // 文件数量不把普通子文件夹计为文件；展示区仍可打开普通子文件夹。
        count.Text = $"{paths.Count(File.Exists)} 个文件";
        expand.Content = State.Expanded ? "▴" : "▾";
        body.Visibility = State.Expanded ? Visibility.Visible : Visibility.Collapsed;
        toggle.Content = State.Grid ? "列表 ☷" : "网格 ▦";
        var factory = new FrameworkElementFactory(State.Grid ? typeof(WrapPanel) : typeof(StackPanel));
        list.ItemsPanel = new ItemsPanelTemplate(factory);
        var selected = list.SelectedItems.Cast<ListBoxItem>().Select(item => (string)item.Tag).ToHashSet();
        list.Items.Clear();
        foreach (var path in paths)
        {
            var name = System.IO.Path.GetFileName(path);
            var image = new Image { Source = ShellIcons.ForFile(path, !State.Grid), Width = State.Grid ? 32 : 20, Height = State.Grid ? 32 : 20, Margin = State.Grid ? new Thickness(0, 3, 0, 8) : new Thickness(0, 0, 10, 0) };
            var text = new TextBlock { Text = name, Foreground = Brushes.White, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = State.Grid ? TextAlignment.Center : TextAlignment.Left, VerticalAlignment = VerticalAlignment.Center, TextWrapping = State.Grid ? TextWrapping.Wrap : TextWrapping.NoWrap, MaxHeight = 34 };
            var cell = new StackPanel { Orientation = State.Grid ? Orientation.Vertical : Orientation.Horizontal, Margin = new Thickness(7) };
            cell.Children.Add(image); cell.Children.Add(text);
            var item = new ListBoxItem { Tag = path, Content = cell, Width = State.Grid ? 86 : Math.Max(170, State.Width - 48), MinHeight = State.Grid ? 90 : 38, Margin = new Thickness(1), ToolTip = name, Background = Brushes.Transparent, IsSelected = selected.Contains(path) };
            list.Items.Add(item);
        }
        ApplyGeometry();
    }

    internal void ApplyStyle()
    {
        var color = (Color)ColorConverter.ConvertFromString(State.Color);
        color.A = (byte)(Math.Clamp(State.Opacity, 0, 1) * 255);
        surface.Background = new SolidColorBrush(color);
    }

    internal void Attach()
    {
        Host = Native.FindDesktopHost();
        if (Host != IntPtr.Zero)
        {
            var style = Native.GetWindowLongPtr(Handle, -16).ToInt64();
            Native.SetWindowLongPtr(Handle, -16, new IntPtr((style & ~0x80000000L) | 0x40000000L));
            Native.SetParent(Handle, Host);
            Program.HostStatus = Native.GetParent(Handle) == Host ? "已挂接 SHELLDLL_DefView（实验）" : "桌面挂接失败";
        }
        else Program.HostStatus = "未找到桌面宿主，当前为普通窗口";
        ApplyGeometry();
    }

    internal void ApplyGeometry()
    {
        if (applying || closed) return;
        applying = true;
        try
        {
            Width = State.Width; Height = TotalHeight;
            layout.RowDefinitions[0].Height = new GridLength(State.Height);
            body.Visibility = State.Expanded ? Visibility.Visible : Visibility.Collapsed;
            expand.Content = State.Expanded ? "▴" : "▾";
            if (Handle == IntPtr.Zero) return;
            var point = new Native.POINT { X = State.X, Y = State.Y };
            if (Host != IntPtr.Zero) Native.ScreenToClient(Host, ref point);
            Native.SetWindowPos(Handle, IntPtr.Zero, point.X, point.Y, (int)Math.Ceiling(Width * Scale), (int)Math.Ceiling(Height * Scale), 0x10 | (LayoutHidden ? 0u : 0x40u));
        }
        finally { applying = false; }
    }

    internal static void Menu(ContextMenu menu, string text, Action action)
    { var item = new MenuItem { Header = text }; item.Click += (_, _) => action(); menu.Items.Add(item); }
}
