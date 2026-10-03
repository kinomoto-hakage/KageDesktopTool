using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Kage.Workspace;

namespace Kage.Desktop;

// 一个窗口同时承载头部和展示部分，几何预览不触发内容读取。
internal sealed class FolderHeader : Window
{
    internal Guid FolderId { get; }
    internal IntPtr Handle => new WindowInteropHelper(this).Handle;
    internal IntPtr Host { get; private set; }
    internal Border Surface { get; }
    internal FolderContents Contents { get; }
    internal Grid HeaderInput => header;
    internal FolderRecord Record { get; private set; }
    private readonly Grid layout;
    private readonly Grid header;
    private readonly TextBlock title;
    private readonly TextBlock count;
    private readonly Button expand;
    private LayoutInteraction? interaction;
    private bool closed;
    private double displayScale = 1;

    internal FolderHeader(FolderSnapshot folder)
    {
        FolderId = folder.Folder.Id;
        Record = folder.Folder;
        Title = "Kage · " + Record.Name;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Icon = Runtime.ApplicationIcon;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        Surface = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromArgb(55, 255, 255, 255)) };
        layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Record.HeaderHeight) });
        layout.RowDefinitions.Add(new RowDefinition());
        header = new Grid { Margin = new Thickness(14, 0, 12, 0), Background = Brushes.Transparent };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        title = new TextBlock { Foreground = Brushes.White, FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Cursor = Cursors.SizeAll };
        count = new TextBlock { Foreground = Brushes.WhiteSmoke, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 8, 0) };
        expand = new Button { Content = "▾", FontSize = 20, Foreground = Brushes.White, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(1), ToolTip = "展开／折叠" };
        expand.Click += async (_, _) => await ToggleAsync();
        Grid.SetColumn(count, 1);
        Grid.SetColumn(expand, 2);
        header.Children.Add(title);
        header.Children.Add(count);
        header.Children.Add(expand);
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (FindParent<Button>(e.OriginalSource as DependencyObject) != null) return;
            BeginHeaderDrag(PointToScreen(e.GetPosition(this)));
            if (interaction != null) header.CaptureMouse();
            e.Handled = true;
        };
        header.MouseMove += (_, e) =>
        {
            if (interaction == null) return;
            if (e.LeftButton != MouseButtonState.Pressed) { _ = EndInteractionAsync(); return; }
            DragHeaderTo(PointToScreen(e.GetPosition(this)));
        };
        header.MouseLeftButtonUp += async (_, _) => await EndInteractionAsync();
        header.LostMouseCapture += async (_, _) => await EndInteractionAsync();
        layout.Children.Add(header);
        Contents = new FolderContents(FolderId);
        FileDrag.Receive(this);
        Grid.SetRow(Contents, 1);
        layout.Children.Add(Contents);
        var grip = new Thumb { Width = 14, Height = 14, Cursor = Cursors.SizeNWSE, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 3, 3), Opacity = .6 };
        var factory = new FrameworkElementFactory(typeof(TextBlock));
        factory.SetValue(TextBlock.TextProperty, "◢");
        factory.SetValue(TextBlock.ForegroundProperty, Brushes.White);
        grip.Template = new ControlTemplate(typeof(Thumb)) { VisualTree = factory };
        Grid.SetRowSpan(grip, 2);
        layout.Children.Add(grip);
        grip.DragStarted += (_, _) => interaction = Runtime.Current.BeginInteraction(FolderId);
        grip.DragDelta += (_, e) =>
        {
            if (interaction == null) return;
            interaction.ResizeBy(e.HorizontalChange, e.VerticalChange);
            Runtime.Current.Preview(interaction);
        };
        grip.DragCompleted += async (_, _) => await EndInteractionAsync();
        Surface.Child = layout;
        Content = Surface;
        var menu = new ContextMenu();
        Menu(menu, "展开／折叠", async () => await ToggleAsync());
        Menu(menu, "打开内容文件夹", () =>
        {
            foreach (var item in Runtime.Current.Workspace.Snapshot.Folders)
                if (item.Folder.Id == FolderId) { Runtime.Open(item.ActualPath); break; }
        });
        Menu(menu, "重命名…", () => Runtime.Current.ShowFolderAction(FolderId, true));
        Menu(menu, "删除…", () => Runtime.Current.ShowFolderAction(FolderId, false));
        Menu(menu, "外观…", () => Runtime.Current.ShowAppearance(FolderId));
        Menu(menu, "设置", () => Runtime.Current.ShowSettings());
        ContextMenu = menu;
        SourceInitialized += (_, _) => Attach();
        Closed += async (_, _) => { closed = true; Contents.Dispose(); await EndInteractionAsync(); };
        Update(folder);
    }

    private static void Menu(ContextMenu menu, string label, Action invoke)
    {
        var item = new MenuItem { Header = label };
        item.Click += (_, _) => invoke();
        menu.Items.Add(item);
    }

    internal static T? FindParent<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element != null)
        {
            if (element is T found) return found;
            element = VisualTreeHelper.GetParent(element);
        }
        return null;
    }

    internal async Task ToggleAsync()
    {
        if (Runtime.Current.Interacting || Runtime.Current.ChangingFolder || Runtime.Current.Migrating) return;
        var result = await Runtime.Current.Workspace.ToggleFolderAsync(FolderId);
        Runtime.Current.Render();
        if (!result.Succeeded) Runtime.Current.Balloon(result.Message);
    }

    // 会话轨迹检查与真实鼠标事件使用同一个入口。
    internal void BeginHeaderDrag(Point point)
    {
        interaction = Runtime.Current.BeginInteraction(FolderId);
        interaction?.BeginDrag((int)Math.Round(point.X), (int)Math.Round(point.Y));
    }

    internal void DragHeaderTo(Point point)
    {
        if (interaction == null) return;
        interaction.DragTo((int)Math.Round(point.X), (int)Math.Round(point.Y));
        Runtime.Current.Preview(interaction);
    }

    internal async Task EndInteractionAsync()
    {
        var completed = interaction;
        if (completed == null) return;
        interaction = null;
        completed.EndDrag();
        header.ReleaseMouseCapture();
        await Runtime.Current.CommitInteractionAsync(completed);
    }

    internal void Attach()
    {
        var previous = Host;
        Host = WindowsDesktop.Attach(Handle);
        if (Host != IntPtr.Zero && previous != Host)
        {
            // 重新挂接后通过 WPF 重新登记 OLE 接收，不重复订阅输入事件。
            AllowDrop = false;
            AllowDrop = true;
        }
    }

    internal void Update(FolderSnapshot folder)
    {
        title.Text = folder.Folder.Name;
        Title = "Kage · " + folder.Folder.Name;
        count.Text = folder.FileCount?.ToString() ?? "?";
        ToolTip = folder.Notice ?? folder.ActualPath;
        ApplyStyle(folder.Folder.Color, folder.Folder.Opacity);
        var area = Array.Find(WindowsDesktop.Displays(), area => folder.Folder.X >= area.X && folder.Folder.X < area.X + area.Width && folder.Folder.Y >= area.Y && folder.Folder.Y < area.Y + area.Height);
        displayScale = area?.Scale ?? 1;
        ApplyGeometry(folder);
        Contents.Update(folder, VisualTreeHelper.GetDpi(this).DpiScaleX);
    }

    internal void ApplyGeometry(FolderSnapshot folder)
    {
        if (closed) return;
        Record = folder.Folder;
        Width = Record.HeaderWidth;
        Height = Record.HeaderHeight + (Record.Expanded ? Record.BodyHeight : 0);
        layout.RowDefinitions[0].Height = new GridLength(Record.HeaderHeight);
        Contents.Visibility = Record.Expanded ? Visibility.Visible : Visibility.Collapsed;
        expand.Content = Record.Expanded ? "▴" : "▾";
        if (Handle == IntPtr.Zero) return;
        if (!WindowsDesktop.Attached(Handle, Host)) Attach();
        if (!folder.Visible || Host == IntPtr.Zero) { Hide(); return; }
        Show();
        if (!WindowsDesktop.Position(Handle, Host, Record.X, Record.Y,
            (int)Math.Ceiling(Width * displayScale), (int)Math.Ceiling(Height * displayScale)))
        { Host = IntPtr.Zero; Hide(); }
    }

    internal void ApplyStyle(string value, double opacity)
    {
        var color = (Color)ColorConverter.ConvertFromString(value);
        color.A = (byte)Math.Round(opacity * 255);
        Surface.Background = new SolidColorBrush(color);
    }
}
