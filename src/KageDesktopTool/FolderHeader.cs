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
    internal Thumb HeaderDivider { get; }
    internal Thumb ResizeGrip { get; }
    internal FolderRecord Record { get; private set; }
    internal bool FrostedVisible => backdrop != null && WindowsDesktop.Attached(Handle, backdrop.Handle);
    internal bool OwnsInputWindow(IntPtr window) => window == Handle || WindowsDesktop.IsChild(Handle, window)
        || backdrop != null && window == backdrop.Handle;
    internal string FrostedUnavailableReason => !Record.Frosted ? ""
        : Runtime.Current.FrostedEnvironment.Reason.Length != 0 ? Runtime.Current.FrostedEnvironment.Reason
        : !FrostedVisible ? "当前环境无法显示毛玻璃" : "";
    private FrostedBackdrop? backdrop;
    private bool backdropFailed;
    private readonly Grid layout;
    private readonly Grid header;
    private readonly HeaderName title;
    internal HeaderName NameDisplay => title;
    internal Button ExpandButton => expand;
    private readonly TextBlock count;
    private readonly Button expand;
    private LayoutInteraction? interaction;
    private bool closed;
    private double displayScale = 1;
    private FolderSnapshot? geometry;
    private bool positioning;
    private bool previewingGeometry;
    private Point? resizePoint;
    private Thumb? nativeThumb;
    private Button? nativeButton;
    private bool nativeRightItem;
    private double nativeScrollOffset;
    private Point nativeScrollPoint;
    private long lastNativeClick;
    private Point lastNativeClickPoint;
    private bool nativeNameHover;

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
        Surface = new Border { CornerRadius = new CornerRadius(10) };
        layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Record.HeaderHeight) });
        layout.RowDefinitions.Add(new RowDefinition());
        header = new Grid { Margin = new Thickness(14, 0, 12, 0), Background = Brushes.Transparent };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        title = new HeaderName { Cursor = Cursors.SizeAll };
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
        header.LostMouseCapture += async (_, e) => { if (e.OriginalSource == header) await EndInteractionAsync(); };
        layout.Children.Add(header);
        Contents = new FolderContents(FolderId);
        FileDrag.Receive(this);
        Grid.SetRow(Contents, 1);
        layout.Children.Add(Contents);
        var grip = new Thumb { Width = 14, Height = 14, Cursor = Cursors.SizeNWSE, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 3, 3), Opacity = .6 };
        ResizeGrip = grip;
        var factory = new FrameworkElementFactory(typeof(TextBlock));
        factory.SetValue(TextBlock.TextProperty, "◢");
        factory.SetValue(TextBlock.ForegroundProperty, Brushes.White);
        grip.Template = new ControlTemplate(typeof(Thumb)) { VisualTree = factory };
        Grid.SetRowSpan(grip, 2);
        layout.Children.Add(grip);
        HeaderDivider = new Thumb { Height = 8, Cursor = Cursors.SizeNS, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(8, 0, 8, -4), ToolTip = "拖动调整头部高度" };
        var divider = new FrameworkElementFactory(typeof(Border));
        divider.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var line = new FrameworkElementFactory(typeof(Border));
        line.SetValue(Border.HeightProperty, 1.0);
        line.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)));
        line.SetValue(Border.VerticalAlignmentProperty, VerticalAlignment.Center);
        divider.AppendChild(line);
        HeaderDivider.Template = new ControlTemplate(typeof(Thumb)) { VisualTree = divider };
        layout.Children.Add(HeaderDivider);
        ConfigureResize(grip, false);
        ConfigureResize(HeaderDivider, true);
        // 装饰描边叠在几何上，避免下／右头部的锚点受边框占用尺寸影响。
        var surfaceLayout = new Grid(); surfaceLayout.Children.Add(layout);
        surfaceLayout.Children.Add(new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(55, 255, 255, 255)), IsHitTestVisible = false });
        Surface.Child = surfaceLayout;
        Content = Surface;
        PreviewMouseMove += (_, e) =>
        {
            if (!nativeNameHover || Mouse.Captured != title) return;
            WindowsDesktop.GetCursorPos(out var cursor);
            if (InputHitTest(PointFromScreen(new Point(cursor.X, cursor.Y))) == title) return;
            nativeNameHover = false; Mouse.Capture(null);
        };
        PreviewMouseDown += (_, e) =>
        {
            if (!nativeNameHover || Mouse.Captured != title) return;
            WindowsDesktop.GetCursorPos(out var cursor);
            if (InputHitTest(PointFromScreen(new Point(cursor.X, cursor.Y))) == title) return;
            // 光标移动与按下可紧邻到达；按实际落点释放旧 hover，不能将这次
            // 内容点击当作名称点击。继续同一个正式输入入口，不重发 routed event。
            nativeNameHover = false; Mouse.Capture(null); e.Handled = true;
            if (e.ChangedButton is MouseButton.Left or MouseButton.Right)
                BackdropInput(e.ChangedButton == MouseButton.Right ? 0x204u : 0x201u, IntPtr.Zero, backdrop?.Handle ?? Handle);
        };
        PreviewMouseDown += (_, e) =>
        {
            var hit = e.OriginalSource as DependencyObject;
            var selection = Runtime.Current.Selection;
            if (FindParent<FolderContents>(hit) == Contents
                && (FindParent<ScrollBar>(hit) != null || FindParent<ListBoxItem>(hit) != null
                    || e.ChangedButton == MouseButton.Left && Keyboard.Modifiers != ModifierKeys.None))
                selection.Claim(Contents);
            else selection.Clear();
        };
        var menu = new ContextMenu();
        Menu(menu, "展开／折叠", async () => await ToggleAsync());
        Menu(menu, "打开内容文件夹", () =>
        {
            foreach (var item in Runtime.Current.Workspace.Snapshot.Folders)
                if (item.Folder.Id == FolderId) { Runtime.Open(item.ActualPath); break; }
        });
        Menu(menu, "重命名…", () => Runtime.Current.ShowFolderAction(FolderId, true));
        Menu(menu, "删除…", () => Runtime.Current.ShowFolderAction(FolderId, false));
        Menu(menu, "样式…", () => Runtime.Current.ShowAppearance(FolderId));
        Menu(menu, "设置", () => Runtime.Current.ShowSettings());
        ContextMenu = menu;
        SourceInitialized += (_, _) => Attach();
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            if (!closed && !positioning && geometry != null) ApplyGeometry(geometry, previewingGeometry);
        });
        Closed += async (_, _) => { closed = true; backdrop?.Dispose(); backdrop = null; Contents.Dispose(); await EndInteractionAsync(); };
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
        Runtime.Current.Complete("展开／折叠 Folder", result);
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
        resizePoint = null;
        completed.EndDrag();
        header.ReleaseMouseCapture();
        await Runtime.Current.CommitInteractionAsync(completed);
    }

    internal void CancelInteraction()
    {
        interaction?.EndDrag();
        interaction = null;
        resizePoint = null;
        nativeThumb = null; nativeButton = null; NativeRelease();
        header.ReleaseMouseCapture();
        if (Mouse.Captured != null && IsAncestorOf(Mouse.Captured as DependencyObject)) Mouse.Capture(null);
    }

    private void ConfigureResize(Thumb thumb, bool headerOnly)
    {
        thumb.DragStarted += (_, _) =>
        {
            BeginResize();
        };
        thumb.DragDelta += (_, _) =>
        {
            ResizeFromCursor(headerOnly);
        };
        thumb.DragCompleted += async (_, _) => await EndInteractionAsync();
    }

    private void BeginResize()
    {
        interaction = Runtime.Current.BeginInteraction(FolderId);
        WindowsDesktop.GetCursorPos(out var cursor);
        resizePoint = new Point(cursor.X, cursor.Y);
    }

    private void ResizeFromCursor(bool headerOnly)
    {
            if (interaction == null || resizePoint is not { } previous) return;
            WindowsDesktop.GetCursorPos(out var cursor);
            resizePoint = new Point(cursor.X, cursor.Y);
            // HWND 宿主与目标屏可能有不同 DPI；输入按真实物理像素换算为目标 DIP。
            var dx = (cursor.X - previous.X) / displayScale;
            var dy = (cursor.Y - previous.Y) / displayScale;
            if (headerOnly) interaction.ResizeHeaderBy(Record.HeaderPosition switch
            { HeaderPosition.Top => dy, HeaderPosition.Bottom => -dy, HeaderPosition.Left => dx, _ => -dx });
            else interaction.ResizeBy(Record.Expanded && Record.HeaderPosition == HeaderPosition.Right ? -dx : dx,
                Record.Expanded && Record.HeaderPosition == HeaderPosition.Bottom ? -dy : dy);
            Runtime.Current.Preview(interaction);
    }

    // 背景 alpha=0 像素得到真实原生消息时，以同一可视树命中和业务入口
    // 处理初次按下。之后仍使用既有 WPF 鼠标捕获，不产生人工 routed event。
    internal void BackdropInput(uint message, IntPtr wp, IntPtr nativeHandle)
    {
        WindowsDesktop.GetCursorPos(out var cursor);
        var screen = new Point(cursor.X, cursor.Y);
        var hit = InputHitTest(PointFromScreen(screen)) as DependencyObject;
        if (message is 0x201 or 0x203)
        {
            var tick = Environment.TickCount64;
            var doubleClick = message == 0x203 || tick - lastNativeClick <= NativeDoubleClickTime()
                && Math.Abs(screen.X - lastNativeClickPoint.X) <= NativeMetric(36, (uint)Math.Round(96 * displayScale)) / 2.0
                && Math.Abs(screen.Y - lastNativeClickPoint.Y) <= NativeMetric(37, (uint)Math.Round(96 * displayScale)) / 2.0;
            lastNativeClick = tick; lastNativeClickPoint = screen;
            nativeThumb = FindParent<Thumb>(hit);
            nativeButton = FindParent<Button>(hit);
            if (nativeThumb != null && FindParent<ScrollBar>(nativeThumb) != null)
            {
                nativeScrollOffset = ContentPointerInput.Descendant<ScrollViewer>(Contents.Items)?.VerticalOffset ?? 0;
                nativeScrollPoint = screen; NativeCapture(nativeHandle); return;
            }
            if (nativeThumb != null) { Runtime.Current.Selection.Clear(); BeginResize(); NativeCapture(nativeHandle); return; }
            if (nativeButton != null) { NativeCapture(nativeHandle); return; }
            if (FindParent<FolderContents>(hit) == Contents && FindParent<ScrollBar>(hit) == null)
            {
                if (FindParent<Grid>(hit) == Contents.Viewport || Contents.Viewport.IsAncestorOf(hit))
                    Contents.PointerInput.PointerDown(hit, Contents.Viewport.PointFromScreen(screen), doubleClick ? 2 : 1);
                else Runtime.Current.Selection.Clear();
                return;
            }
            Runtime.Current.Selection.Clear();
            if (header.IsAncestorOf(hit) || hit == header) { BeginHeaderDrag(screen); if (interaction != null) header.CaptureMouse(); }
        }
        else if (message == 0x200 && nativeThumb != null)
        {
            var bar = FindParent<ScrollBar>(nativeThumb);
            if (bar != null)
            {
                var viewer = ContentPointerInput.Descendant<ScrollViewer>(Contents.Items);
                if (viewer != null) viewer.ScrollToVerticalOffset(nativeScrollOffset + (screen.Y - nativeScrollPoint.Y) / displayScale
                    * viewer.ScrollableHeight / Math.Max(1, bar.ActualHeight - nativeThumb.ActualHeight));
            }
            else ResizeFromCursor(nativeThumb == HeaderDivider);
        }
        else if (message == 0x202)
        {
            if (nativeThumb != null) { nativeThumb = null; NativeRelease(); _ = EndInteractionAsync(); }
            if (nativeButton != null)
            {
                var pressed = nativeButton; nativeButton = null; NativeRelease();
                if (FindParent<Button>(hit) == pressed)
                {
                    var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(pressed);
                    (peer?.GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)
                        as System.Windows.Automation.Provider.IInvokeProvider)?.Invoke();
                }
            }
        }
        else if (message == 0x204)
        {
            nativeRightItem = Contents.PointerInput.PointerRightDown(hit);
            NativeCapture(nativeHandle);
        }
        else if (message == 0x205)
        {
            NativeRelease();
            if (nativeRightItem) { nativeRightItem = false; _ = Contents.PointerInput.PointerRightUpAsync(screen); }
            else if (ContextMenu != null) { Runtime.Current.Selection.Clear(); ContextMenu.PlacementTarget = this; ContextMenu.IsOpen = true; }
        }
        else if (message == 0x20A && FindParent<FolderContents>(hit) == Contents)
        {
            var viewer = ContentPointerInput.Descendant<ScrollViewer>(Contents.Items);
            if (viewer != null) viewer.ScrollToVerticalOffset(viewer.VerticalOffset - (short)(wp.ToInt64() >> 16) / 120.0
                * SystemParameters.WheelScrollLines * 16);
        }
        else if (message == 0x200 && hit == title && Mouse.Captured == null && Mouse.LeftButton == MouseButtonState.Released)
        {
            // 透明名称留白无法产生 WPF MouseOver；在真实悬停期间交给名称元素
            // 接收后续真实消息，离开其边界立即释放，仍由标准 ToolTip 显示全文。
            nativeNameHover = title.CaptureMouse();
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetCapture")] private static extern IntPtr NativeCapture(IntPtr hwnd);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "ReleaseCapture")] private static extern bool NativeRelease();
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetDoubleClickTime")] private static extern uint NativeDoubleClickTime();
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetSystemMetricsForDpi")] private static extern int NativeMetric(int metric, uint dpi);

    internal void Attach()
    {
        var previous = Host;
        Host = WindowsDesktop.Attach(Handle);
        // 先将清晰前景移回 Explorer，再释放旧背景，避免销毁其子 HWND。
        if (backdrop != null && WindowsDesktop.Attached(Handle, backdrop.Handle))
        {
            Hide();
            WindowsDesktop.SetParent(Handle, IntPtr.Zero);
        }
        backdrop?.Dispose(); backdrop = null;
        backdropFailed = false;
        if (Record.Frosted && Host != IntPtr.Zero && Runtime.Current.SupportsFrosted)
        {
            var candidate = FrostedBackdrop.TryCreate(Host);
            if (candidate != null && candidate.Attach(this, Handle, FolderId)) { backdrop = candidate; Host = candidate.Handle; }
            else
            {
                if (candidate != null) Host = WindowsDesktop.Attach(Handle);
                if (candidate != null && WindowsDesktop.Attached(Handle, candidate.Handle))
                { Hide(); WindowsDesktop.SetParent(Handle, IntPtr.Zero); }
                candidate?.Dispose(); backdropFailed = true;
            }
        }
        else backdropFailed = false;
        if (Host != IntPtr.Zero && previous != Host)
        {
            // 重新挂接后通过 WPF 重新登记 OLE 接收，不重复订阅输入事件。
            AllowDrop = false;
            AllowDrop = true;
        }
    }

    internal void Update(FolderSnapshot folder)
    {
        Title = "Kage · " + folder.Folder.Name;
        count.Text = folder.FileCount?.ToString() ?? "?";
        ToolTip = folder.Notice ?? folder.ActualPath;
        ApplyStyle(folder.Folder.Color, folder.Folder.Opacity);
        ApplyGeometry(folder);
        Contents.Update(folder, displayScale);
    }

    internal void ApplyGeometry(FolderSnapshot folder, bool preview = false)
    {
        if (closed) return;
        geometry = folder;
        previewingGeometry = preview;
        var scaleChanged = displayScale != folder.DisplayScale;
        displayScale = folder.DisplayScale;
        Record = folder.Folder;
        var sourceScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        // Explorer 子窗口可能沿用宿主 DPI。显式补偿 WPF 渲染尺寸和命中变换。
        var compensation = displayScale / sourceScale;
        Surface.LayoutTransform = compensation == 1 ? Transform.Identity : new ScaleTransform(compensation, compensation);
        var bounds = FolderGeometry.For(Record, displayScale);
        Width = bounds.Width / sourceScale;
        Height = bounds.Height / sourceScale;
        ConfigureDirection();
        title.Set(Record.Name, Record.NameOrientation, FolderGeometry.Side(Record));
        Contents.Visibility = Record.Expanded ? Visibility.Visible : Visibility.Collapsed;
        HeaderDivider.Visibility = Record.Expanded ? Visibility.Visible : Visibility.Collapsed;
        expand.Content = Record.HeaderPosition switch
        { HeaderPosition.Top => Record.Expanded ? "▴" : "▾", HeaderPosition.Bottom => Record.Expanded ? "▾" : "▴",
            HeaderPosition.Left => Record.Expanded ? "◂" : "▸", _ => Record.Expanded ? "▸" : "◂" };
        if (Handle == IntPtr.Zero) return;
        if (!WindowsDesktop.Attached(Handle, Host) || backdrop != null && !WindowsDesktop.IsWindow(backdrop.DesktopHost)
            || (!Record.Frosted || !Runtime.Current.SupportsFrosted) && backdrop != null
            || Record.Frosted && Runtime.Current.SupportsFrosted && backdrop == null && !backdropFailed) Attach();
        if (!folder.Visible || Host == IntPtr.Zero) { Hide(); backdrop?.Hide(); return; }
        Show();
        positioning = true;
        try
        {
            if (backdrop != null && !backdrop.Position(bounds.X, bounds.Y, bounds.Width, bounds.Height, displayScale))
                FallBackFromBackdrop();
            if (!WindowsDesktop.Position(Handle, Host, bounds.X, bounds.Y, bounds.Width, bounds.Height))
            {
                FallBackFromBackdrop();
                if (!WindowsDesktop.Position(Handle, Host, bounds.X, bounds.Y, bounds.Width, bounds.Height))
                { Host = IntPtr.Zero; Hide(); }
            }
        }
        finally { positioning = false; }
        // 预览复用已有内容和图标，只补偿窗口变换；结束会话后 Update 再读取目标 DPI 图标。
        if (scaleChanged && !preview) Contents.Update(folder, displayScale);
    }

    private void FallBackFromBackdrop()
    {
        Host = WindowsDesktop.Attach(Handle);
        if (backdrop != null && WindowsDesktop.Attached(Handle, backdrop.Handle))
        { Hide(); WindowsDesktop.SetParent(Handle, IntPtr.Zero); }
        backdrop?.Dispose(); backdrop = null;
        backdropFailed = Record.Frosted;
        AllowDrop = false; AllowDrop = true;
    }

    internal void ApplyStyle(string value, double opacity)
    {
        var color = (Color)ColorConverter.ConvertFromString(value);
        color.A = (byte)Math.Round(opacity * 255);
        Surface.Background = new SolidColorBrush(color);
    }

    internal void RefreshFrostedEnvironment(FolderSnapshot? effective = null, bool recreateBackdrop = false)
    {
        // 只有系统通知／定时轮询重试能力；鼠标移动不会不断重建失败资源。
        if (recreateBackdrop && backdrop != null) FallBackFromBackdrop();
        backdropFailed = false;
        if (effective != null) ApplyStyle(effective.Folder.Color, effective.Folder.Opacity);
        if ((effective ?? geometry) is { } current) ApplyGeometry(current, effective != null || previewingGeometry);
    }

    private HeaderPosition? arrangedDirection;
    private void ConfigureDirection()
    {
        var side = FolderGeometry.Side(Record);
        var reverse = Record.HeaderPosition is HeaderPosition.Bottom or HeaderPosition.Right;
        layout.RowDefinitions.Clear(); layout.ColumnDefinitions.Clear();
        if (side)
        {
            layout.RowDefinitions.Add(new RowDefinition());
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(reverse && Record.Expanded ? Record.HeaderWidth : Record.SideHeaderWidth) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Record.Expanded ? reverse ? Record.SideHeaderWidth : Record.HeaderWidth : 0) });
        }
        else
        {
            layout.ColumnDefinitions.Add(new ColumnDefinition());
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(reverse && Record.Expanded ? Record.BodyHeight : Record.HeaderHeight) });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Record.Expanded ? reverse ? Record.HeaderHeight : Record.BodyHeight : 0) });
        }
        var headIndex = reverse && Record.Expanded ? 1 : 0;
        foreach (var element in new FrameworkElement[] { header, HeaderDivider })
        { Grid.SetRow(element, side ? 0 : headIndex); Grid.SetColumn(element, side ? headIndex : 0); }
        Grid.SetRow(Contents, side ? 0 : 1 - headIndex); Grid.SetColumn(Contents, side ? 1 - headIndex : 0);
        Grid.SetRow(ResizeGrip, 0); Grid.SetColumn(ResizeGrip, 0);
        Grid.SetRowSpan(ResizeGrip, layout.RowDefinitions.Count); Grid.SetColumnSpan(ResizeGrip, layout.ColumnDefinitions.Count);
        ResizeGrip.HorizontalAlignment = Record.Expanded && Record.HeaderPosition == HeaderPosition.Right ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        ResizeGrip.VerticalAlignment = Record.Expanded && Record.HeaderPosition == HeaderPosition.Bottom ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        ResizeGrip.Cursor = Record.Expanded && reverse ? Cursors.SizeNESW : Cursors.SizeNWSE;
        if (arrangedDirection == Record.HeaderPosition) return;
        arrangedDirection = Record.HeaderPosition;
        var grip = new FrameworkElementFactory(typeof(TextBlock));
        grip.SetValue(TextBlock.TextProperty, Record.HeaderPosition switch
        { HeaderPosition.Bottom => "◥", HeaderPosition.Right => "◣", _ => "◢" });
        grip.SetValue(TextBlock.ForegroundProperty, Brushes.White);
        ResizeGrip.Template = new ControlTemplate(typeof(Thumb)) { VisualTree = grip };
        header.RowDefinitions.Clear(); header.ColumnDefinitions.Clear();
        header.Margin = side ? new Thickness(8, 8, 8, 6) : new Thickness(14, 0, 12, 0);
        if (side)
        {
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.RowDefinitions.Add(new RowDefinition());
            header.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });
            header.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
        }
        else
        {
            header.RowDefinitions.Add(new RowDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        }
        Grid.SetRow(title, 0); Grid.SetColumn(title, 0);
        Grid.SetRow(count, side ? 1 : 0); Grid.SetColumn(count, side ? 0 : 1);
        Grid.SetRow(expand, side ? 2 : 0); Grid.SetColumn(expand, side ? 0 : 2);
        count.Margin = side ? new Thickness(0) : new Thickness(10, 0, 8, 0);
        count.HorizontalAlignment = side ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        HeaderDivider.Width = side ? 8 : double.NaN; HeaderDivider.Height = side ? double.NaN : 8;
        HeaderDivider.Cursor = side ? Cursors.SizeWE : Cursors.SizeNS;
        HeaderDivider.ToolTip = side ? "拖动调整侧栏宽度" : "拖动调整头部高度";
        HeaderDivider.HorizontalAlignment = side ? reverse ? HorizontalAlignment.Left : HorizontalAlignment.Right : HorizontalAlignment.Stretch;
        HeaderDivider.VerticalAlignment = side ? VerticalAlignment.Stretch : reverse ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        HeaderDivider.Margin = side ? reverse ? new Thickness(-4, 8, 0, 8) : new Thickness(0, 8, -4, 8)
            : reverse ? new Thickness(8, -4, 8, 0) : new Thickness(8, 0, 8, -4);
        var divider = new FrameworkElementFactory(typeof(Border)); divider.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var line = new FrameworkElementFactory(typeof(Border));
        line.SetValue(side ? Border.WidthProperty : Border.HeightProperty, 1.0);
        line.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)));
        line.SetValue(side ? Border.HorizontalAlignmentProperty : Border.VerticalAlignmentProperty,
            side ? (object)HorizontalAlignment.Center : VerticalAlignment.Center);
        divider.AppendChild(line); HeaderDivider.Template = new ControlTemplate(typeof(Thumb)) { VisualTree = divider };
    }
}
