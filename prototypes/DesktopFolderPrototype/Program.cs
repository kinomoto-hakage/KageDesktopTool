// 临时原型：验证原生桌面入口、浮动面板、真实示例文件移动和位置／尺寸恢复。
// 桌面宿主使用 Explorer 内部窗口，仅用于实验，不作为正式实现的稳定契约。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Kage.DesktopFolderPrototype;

public sealed class FolderState
{
    public string Name { get; set; } = "工作";
    public int X { get; set; } = 250;
    public int Y { get; set; } = 160;
    public double Width { get; set; } = 180;
    public double Height { get; set; } = 154;
    public double PanelWidth { get; set; } = 460;
    public double PanelHeight { get; set; } = 400;
    public string Color { get; set; } = "#26364D";
    public double Opacity { get; set; } = .92;
    public bool Grid { get; set; } = true;
}

public static class Program
{
    internal static string Home = "";
    internal static string DataRoot => Path.Combine(Home, "PROTOTYPE-data");
    internal static string Storage => Path.Combine(DataRoot, "内容");
    internal static readonly List<FolderWindow> Folders = new();
    internal static Application App = null!;
    internal static Forms.NotifyIcon Tray = null!;
    internal static Window Controller = null!;
    internal static ContentWindow? Panel;
    internal static bool Capturing;
    internal static bool ProbeDesktop;
    private static bool shuttingDown;
    private static HwndSource? controllerSource;
    private static DispatcherTimer? recovery;
    internal static string HostStatus = "尚未挂接";

    [STAThread]
    public static void Main(string[] args)
    {
        // 根据可执行文件位置查找原型项目，不依赖调用者的工作目录。
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "DesktopFolderPrototype.csproj"))) directory = directory.Parent;
        Home = directory?.FullName ?? AppContext.BaseDirectory;
        Capturing = args.Contains("--capture");
        ProbeDesktop = args.Contains("--desktop-probe");
        App = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        App.DispatcherUnhandledException += (_, e) => { Notice(e.Exception.Message); e.Handled = true; };
        App.Startup += (_, _) => Start();
        App.Run();
    }

    private static void Start()
    {
        Directory.CreateDirectory(Storage);
        var stateFile = Path.Combine(Home, "PROTOTYPE-state.json");
        var saved = File.Exists(stateFile) ? JsonSerializer.Deserialize<List<FolderState>>(File.ReadAllText(stateFile)) : null;
        if (saved == null)
        {
            saved = new() { new(), new() { Name = "灵感", X = 650, Color = "#345347" } };
            foreach (var state in saved)
            {
                var path = Path.Combine(Storage, state.Name);
                Directory.CreateDirectory(path);
                File.WriteAllText(Path.Combine(path, "欢迎.txt"), "这是原型示例文件，可自由移动。\n");
                File.WriteAllText(Path.Combine(path, "使用说明.md"), "拖动标题调整位置，单击缩略图展开。\n");
            }
            var desktop = Path.Combine(DataRoot, "模拟桌面");
            Directory.CreateDirectory(desktop);
            File.WriteAllText(Path.Combine(desktop, "待整理.txt"), "从资源管理器拖入 Folder，验证实际移动。\n");
        }
        Controller = new Window { Title = "Kage 桌面原型", Width = 1, Height = 1, ShowInTaskbar = false, WindowStyle = WindowStyle.ToolWindow };
        var hwnd = new WindowInteropHelper(Controller).EnsureHandle();
        controllerSource = HwndSource.FromHwnd(hwnd);
        controllerSource.AddHook((IntPtr handle, int message, IntPtr wp, IntPtr lp, ref bool handled) =>
        {
            if (message == 0x312) { Create(); handled = true; }
            return IntPtr.Zero;
        });
        var hotkey = Native.RegisterHotKey(hwnd, 1, 0x4003, 0x4B);
        Tray = new Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Text = "Kage 桌面原型（示例数据）", Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("新建 Folder", null, (_, _) => App.Dispatcher.Invoke(Create));
        menu.Items.Add("打开示例数据", null, (_, _) => Open(DataRoot));
        menu.Items.Add("原型状态与说明", null, (_, _) => App.Dispatcher.Invoke(ShowStatus));
        menu.Items.Add("重新挂接桌面", null, (_, _) => App.Dispatcher.Invoke(Reattach));
        menu.Items.Add("退出", null, (_, _) => App.Dispatcher.Invoke(Exit));
        Tray.ContextMenuStrip = menu;
        Tray.DoubleClick += (_, _) => App.Dispatcher.Invoke(ShowStatus);
        foreach (var state in saved)
        {
            if (Directory.Exists(Path.Combine(Storage, state.Name))) Add(state);
        }
        Save();
        recovery = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        recovery.Tick += (_, _) =>
        {
            foreach (var folder in Folders.ToArray())
                if (!Native.IsWindow(folder.Host)) folder.Attach();
        };
        recovery.Start();
        if (!hotkey && !Capturing) Notice("Ctrl+Alt+K 注册失败，可能被其他程序占用；仍可使用托盘菜单新建。");
        if (Capturing)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (_, _) => { timer.Stop(); Capture(); };
            timer.Start();
        }
    }

    internal static void Add(FolderState state)
    {
        var folder = new FolderWindow(state);
        Folders.Add(folder);
        folder.Show();
    }

    internal static void Create()
    {
        var name = Ask("新建 Folder", "文件夹名称", "新建文件夹");
        if (name == null || !ValidName(name)) return;
        if (Directory.Exists(Path.Combine(Storage, name))) { Notice("已存在同名 Folder，请选择其他名称。"); return; }
        Directory.CreateDirectory(Path.Combine(Storage, name));
        Add(new() { Name = name, X = 250 + Folders.Count * 36, Y = 160 + Folders.Count * 30 });
        Save();
    }

    internal static bool ValidName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith('.') || name.EndsWith(' '))
        { Notice("请输入有效的 Windows 文件夹名称。"); return false; }
        return true;
    }

    internal static void Expand(FolderWindow folder)
    {
        if (Panel?.Folder == folder) { Panel.Close(); return; }
        Panel?.Close();
        Panel = new ContentWindow(folder);
        Panel.Show();
        Panel.Activate();
    }

    internal static void Rename(FolderWindow folder)
    {
        var name = Ask("重命名 Folder", "同时重命名对应的内容文件夹", folder.State.Name);
        if (name == null || name == folder.State.Name || !ValidName(name)) return;
        try
        {
            var destination = Resolve(Path.Combine(Storage, name));
            if (destination == null) return;
            Panel?.Close();
            Directory.Move(folder.Path, destination);
            folder.State.Name = System.IO.Path.GetFileName(destination);
            folder.Watch(); folder.Refresh(); Save();
        }
        catch (Exception e) { Notice(e.Message); }
    }

    internal static void Delete(FolderWindow folder, bool keep)
    {
        if (System.Windows.MessageBox.Show(keep ? "移除原型入口，保留内容，并在模拟桌面创建快捷方式？" : "将本原型内容文件夹连同示例内容放入回收站？", "删除 Folder", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
        try
        {
            if (keep)
            {
                var shortcut = Resolve(Path.Combine(DataRoot, "模拟桌面", folder.State.Name + ".lnk"));
                if (shortcut == null) return;
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(shortcut)!);
                dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
                dynamic link = shell.CreateShortcut(shortcut);
                link.TargetPath = folder.Path;
                link.Save();
                Marshal.FinalReleaseComObject(link); Marshal.FinalReleaseComObject(shell);
            }
            else
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(folder.Path, Microsoft.VisualBasic.FileIO.UIOption.AllDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin, Microsoft.VisualBasic.FileIO.UICancelOption.ThrowException);
            }
            if (Panel?.Folder == folder) Panel.Close();
            Folders.Remove(folder); folder.Close(); Save();
        }
        catch (Exception e) { Notice(e.Message); }
    }

    internal static string? Resolve(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return path;
        var result = System.Windows.MessageBox.Show("目标已存在。\n是：保留两份并自动编号\n否：跳过\n取消：取消本次操作", "同名冲突", MessageBoxButton.YesNoCancel);
        if (result == MessageBoxResult.Cancel) throw new OperationCanceledException("本次操作已取消。");
        if (result == MessageBoxResult.No) return null;
        var directory = System.IO.Path.GetDirectoryName(path)!;
        var extension = Directory.Exists(path) ? "" : System.IO.Path.GetExtension(path);
        var name = System.IO.Path.GetFileName(path);
        if (extension.Length != 0) name = name[..^extension.Length];
        for (var i = 2; ; i++)
        {
            var next = System.IO.Path.Combine(directory, $"{name} ({i}){extension}");
            if (!File.Exists(next) && !Directory.Exists(next)) return next;
        }
    }

    internal static bool MoveInto(FolderWindow folder, string[] sources)
    {
        var moved = false;
        try
        {
            foreach (var source in sources)
            {
                var full = System.IO.Path.GetFullPath(source);
                // 只接收原型数据，避免在桌面集成实验期间处理真实个人文件。
                if (!full.StartsWith(DataRoot + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                { Notice("此原型仅移动 PROTOTYPE-data 中的示例文件。请从托盘菜单打开示例数据。"); continue; }
                if (Folders.Any(item => string.Equals(item.Path, full, StringComparison.OrdinalIgnoreCase)))
                { Notice("请移动 Folder 中的示例内容，不要移动另一个受管理的内容文件夹本身。"); continue; }
                var destination = System.IO.Path.Combine(folder.Path, System.IO.Path.GetFileName(full));
                if (string.Equals(full, destination, StringComparison.OrdinalIgnoreCase)) continue;
                if (Directory.Exists(full) && (string.Equals(full, folder.Path, StringComparison.OrdinalIgnoreCase) || folder.Path.StartsWith(full + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                { Notice("不能将文件夹移动到自身或其子目录中。"); continue; }
                destination = Resolve(destination);
                if (destination == null) continue;
                if (Directory.Exists(full)) Directory.Move(full, destination); else File.Move(full, destination);
                moved = true;
            }
            folder.Refresh(); Panel?.Refresh();
        }
        catch (Exception e) { Notice(e.Message); }
        return moved;
    }

    internal static void Save()
    {
        if (!shuttingDown) File.WriteAllText(System.IO.Path.Combine(Home, "PROTOTYPE-state.json"), JsonSerializer.Serialize(Folders.Select(f => f.State), new JsonSerializerOptions { WriteIndented = true }));
    }

    internal static void Reattach() { foreach (var folder in Folders) folder.Attach(); }
    internal static void Open(string path) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
    internal static void Notice(string text) { if (!Capturing) System.Windows.MessageBox.Show(text, "Kage 桌面原型"); }
    internal static void ShowStatus() => Notice($"临时原型 · 示例文件\n桌面宿主：{HostStatus}\n数据目录：{DataRoot}\n\n拖动入口标题调整位置；右下角调整尺寸。\n右键入口：重命名、颜色、透明度、删除。\n单击缩略图区打开列表／网格面板。\n\n尚未实现：根目录迁移、开机自启开关。\n这些功能保留在正式需求中。");

    internal static string? Ask(string title, string caption, string value)
    {
        var dialog = new Window { Title = title, Width = 390, Height = 170, WindowStartupLocation = WindowStartupLocation.CenterScreen, ResizeMode = ResizeMode.NoResize };
        var box = new TextBox { Text = value, Margin = new Thickness(0, 10, 0, 12) };
        var ok = new Button { Content = "确定", IsDefault = true, Width = 90, HorizontalAlignment = HorizontalAlignment.Right };
        ok.Click += (_, _) => dialog.DialogResult = true;
        var layout = new StackPanel { Margin = new Thickness(20) };
        layout.Children.Add(new TextBlock { Text = caption }); layout.Children.Add(box); layout.Children.Add(ok);
        dialog.Content = layout;
        dialog.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        return dialog.ShowDialog() == true ? box.Text.Trim() : null;
    }

    private static async void Capture()
    {
        try
        {
            var first = Folders.First();
            bool? showDesktopHit = null;
            if (ProbeDesktop && first.Host != IntPtr.Zero)
            {
                dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
                var toggled = false;
                try
                {
                    shell.ToggleDesktop(); toggled = true;
                    await Task.Delay(750);
                    Native.GetWindowRect(first.Handle, out var rectangle);
                    var point = new Native.POINT { X = (rectangle.Left + rectangle.Right) / 2, Y = (rectangle.Top + rectangle.Bottom) / 2 };
                    var hit = Native.WindowFromPoint(point);
                    showDesktopHit = hit == first.Handle || Native.IsChild(first.Handle, hit);
                    if (showDesktopHit == true)
                    {
                        using var bitmap = new System.Drawing.Bitmap(rectangle.Right - rectangle.Left, rectangle.Bottom - rectangle.Top);
                        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
                        // 使用 CAPTUREBLT 包含透明分层窗口，避免只截到其下方的桌面。
                        var outputDc = graphics.GetHdc();
                        var screenDc = Native.GetDC(IntPtr.Zero);
                        try { Native.BitBlt(outputDc, 0, 0, bitmap.Width, bitmap.Height, screenDc, rectangle.Left, rectangle.Top, 0x40CC0020); }
                        finally { Native.ReleaseDC(IntPtr.Zero, screenDc); graphics.ReleaseHdc(outputDc); }
                        bitmap.Save(System.IO.Path.Combine(Home, "PROTOTYPE-preview-桌面.png"), System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
                finally
                {
                    if (toggled) shell.ToggleDesktop();
                    Marshal.FinalReleaseComObject(shell);
                }
                await Task.Delay(250);
            }
            Render(first, "入口");
            Expand(first);
            Panel!.UpdateLayout();
            Render(Panel, "网格");
            first.State.Grid = false; Panel.Refresh(); Panel.UpdateLayout();
            Render(Panel, "列表");
            first.State.Grid = true;
            var report = new
            {
                os = Environment.OSVersion.ToString(), root = DataRoot, host = HostStatus,
                windows = Folders.Select(f => new { name = f.State.Name, handle = f.Handle.ToInt64(), host = f.Host.ToInt64(), parent = Native.GetParent(f.Handle).ToInt64(), visible = Native.IsWindowVisible(f.Handle) }),
                desktopAttached = Folders.All(f => f.Host != IntPtr.Zero && Native.GetParent(f.Handle) == f.Host),
                showDesktopHitTest = showDesktopHit,
                rendering = "普通 WPF 子窗口可显示；透明分层子窗口实测未正常绘制，真正背景透明效果待解决",
                windowClasses = Native.WindowClasses(),
                verified = new[] { "WPF 界面构建与渲染", "示例文件读取", "列表／网格切换" },
                pending = new[] { "Win+D 实际交互", "桌面及资源管理器拖拽", "Explorer 重启", "多显示器与 DPI" }
            };
            File.WriteAllText(System.IO.Path.Combine(Home, "PROTOTYPE-report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) { File.WriteAllText(System.IO.Path.Combine(Home, "PROTOTYPE-report.json"), e.ToString()); }
        finally { Exit(); }
    }

    private static void Render(Window window, string name)
    {
        window.UpdateLayout();
        var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        image.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var output = File.Create(System.IO.Path.Combine(Home, $"PROTOTYPE-preview-{name}.png")); encoder.Save(output);
    }

    private static void Exit()
    {
        Save(); shuttingDown = true; recovery?.Stop(); Panel?.Close();
        foreach (var folder in Folders.ToArray()) folder.Close();
        Native.UnregisterHotKey(new WindowInteropHelper(Controller).Handle, 1);
        Tray.Dispose(); Controller.Close(); App.Shutdown();
    }
}

public sealed class FolderWindow : Window
{
    internal readonly FolderState State;
    internal string Path => System.IO.Path.Combine(Program.Storage, State.Name);
    internal IntPtr Handle => new WindowInteropHelper(this).Handle;
    internal IntPtr Host;
    private readonly Border surface;
    private readonly TextBlock title;
    private readonly TextBlock preview;
    private FileSystemWatcher? watcher;
    private Point? start;
    private int oldX, oldY;

    internal FolderWindow(FolderState state)
    {
        State = state; Width = state.Width; Height = state.Height;
        MinWidth = 140; MinHeight = 120; Title = "Kage · " + state.Name;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        // Explorer 子窗口下先使用普通 WPF 渲染；分层透明窗口的实际显示需单独验证。
        AllowsTransparency = false; Background = new SolidColorBrush(Color.FromRgb(25, 33, 46)); ShowInTaskbar = false; AllowDrop = true;
        surface = new Border { CornerRadius = new CornerRadius(18), Padding = new Thickness(14), BorderBrush = new SolidColorBrush(Color.FromArgb(55, 255, 255, 255)), BorderThickness = new Thickness(1) };
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        title = new TextBlock { Foreground = Brushes.White, FontSize = 15, FontWeight = FontWeights.SemiBold, Cursor = Cursors.SizeAll };
        title.MouseLeftButtonDown += (_, e) => { start = PointToScreen(e.GetPosition(this)); oldX = State.X; oldY = State.Y; title.CaptureMouse(); e.Handled = true; };
        title.MouseMove += (_, e) =>
        {
            if (start == null || e.LeftButton != MouseButtonState.Pressed) return;
            var point = PointToScreen(e.GetPosition(this));
            State.X = oldX + (int)(point.X - start.Value.X); State.Y = oldY + (int)(point.Y - start.Value.Y); Place();
        };
        title.MouseLeftButtonUp += (_, _) => { start = null; title.ReleaseMouseCapture(); Program.Save(); };
        grid.Children.Add(title);
        preview = new TextBlock { Foreground = Brushes.White, FontSize = 14, Margin = new Thickness(0, 12, 0, 6), TextWrapping = TextWrapping.Wrap, Cursor = Cursors.Hand };
        preview.MouseLeftButtonUp += (_, _) => Program.Expand(this);
        Grid.SetRow(preview, 1); grid.Children.Add(preview);
        var bottom = new Grid(); bottom.ColumnDefinitions.Add(new ColumnDefinition()); bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bottom.Children.Add(new TextBlock { Text = "单击展开", Foreground = Brushes.LightGray, FontSize = 11 });
        var thumb = new Thumb { Width = 14, Height = 14, Cursor = Cursors.SizeNWSE, Background = Brushes.Transparent };
        thumb.DragDelta += (_, e) => { Width = Math.Clamp(Width + e.HorizontalChange, MinWidth, 600); Height = Math.Clamp(Height + e.VerticalChange, MinHeight, 500); };
        thumb.DragCompleted += (_, _) => Program.Save();
        Grid.SetColumn(thumb, 1); bottom.Children.Add(thumb); Grid.SetRow(bottom, 2); grid.Children.Add(bottom);
        surface.Child = grid; Content = surface;
        var menu = new ContextMenu();
        Menu(menu, "打开内容面板", () => Program.Expand(this));
        Menu(menu, "在资源管理器打开", () => Program.Open(Path));
        Menu(menu, "重命名", () => Program.Rename(this));
        Menu(menu, "背景颜色", () => { var value = Program.Ask("背景颜色", "输入颜色，如 #26364D", State.Color); if (value != null) { try { _ = ColorConverter.ConvertFromString(value); State.Color = value; Refresh(); Program.Save(); } catch { Program.Notice("颜色格式无效。"); } } });
        Menu(menu, "背景透明度", () => { var value = Program.Ask("背景透明度", "输入 20 到 100；文字保持清晰", (State.Opacity * 100).ToString("0")); if (double.TryParse(value, out var number)) { State.Opacity = Math.Clamp(number / 100, .2, 1); Refresh(); Program.Save(); } });
        menu.Items.Add(new Separator());
        Menu(menu, "删除入口，保留内容", () => Program.Delete(this, true));
        Menu(menu, "连同内容放入回收站", () => Program.Delete(this, false));
        ContextMenu = menu;
        SourceInitialized += (_, _) => Attach();
        SizeChanged += (_, _) => { State.Width = Width; State.Height = Height; Place(); };
        DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; };
        Drop += (_, e) => { if (e.Data.GetData(DataFormats.FileDrop) is string[] files) { e.Effects = Program.MoveInto(this, files) ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; } };
        Closed += (_, _) => watcher?.Dispose();
        Watch(); Refresh();
    }

    internal void Watch()
    {
        watcher?.Dispose();
        if (!Directory.Exists(Path)) return;
        watcher = new FileSystemWatcher(Path) { EnableRaisingEvents = true };
        FileSystemEventHandler change = (_, _) => Dispatcher.BeginInvoke(() => { Refresh(); if (Program.Panel?.Folder == this) Program.Panel.Refresh(); });
        watcher.Created += change; watcher.Deleted += change; watcher.Changed += change;
        watcher.Renamed += (_, _) => Dispatcher.BeginInvoke(() => { Refresh(); if (Program.Panel?.Folder == this) Program.Panel.Refresh(); });
    }

    internal void Refresh()
    {
        title.Text = State.Name;
        var color = (Color)ColorConverter.ConvertFromString(State.Color);
        color.A = (byte)(State.Opacity * 255); surface.Background = new SolidColorBrush(color);
        var names = Directory.Exists(Path) ? Directory.GetFileSystemEntries(Path).Select(System.IO.Path.GetFileName).ToArray() : Array.Empty<string>();
        preview.Text = names.Length == 0 ? "空文件夹\n拖入示例文件" : string.Join("\n", names.Take(3)) + $"\n\n{names.Length} 个项目";
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
        Place();
    }

    private void Place()
    {
        if (Handle == IntPtr.Zero) return;
        var scale = Native.GetDpiForWindow(Handle) / 96.0;
        var bounds = Forms.SystemInformation.VirtualScreen;
        State.X = Math.Clamp(State.X, bounds.Left, Math.Max(bounds.Left, bounds.Right - (int)(Width * scale)));
        State.Y = Math.Clamp(State.Y, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - (int)(Height * scale)));
        var point = new Native.POINT { X = State.X, Y = State.Y };
        if (Host != IntPtr.Zero) Native.ScreenToClient(Host, ref point);
        Native.SetWindowPos(Handle, IntPtr.Zero, point.X, point.Y, (int)(Width * scale), (int)(Height * scale), 0x10 | 0x40);
    }

    internal static void Menu(ContextMenu menu, string title, Action action)
    { var item = new MenuItem { Header = title }; item.Click += (_, _) => action(); menu.Items.Add(item); }
}

public sealed class ContentWindow : Window
{
    internal readonly FolderWindow Folder;
    private readonly ListBox list;
    private readonly Button toggle;
    private Point dragStart;
    private bool dragging;

    internal ContentWindow(FolderWindow folder)
    {
        Folder = folder; Width = folder.State.PanelWidth; Height = folder.State.PanelHeight;
        MinWidth = 320; MinHeight = 240; Title = "Kage · " + folder.State.Name;
        WindowStyle = WindowStyle.ToolWindow; ResizeMode = ResizeMode.CanResizeWithGrip; ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.FromRgb(25, 33, 46)); Foreground = Brushes.White;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var root = new DockPanel { Margin = new Thickness(16) };
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        toggle = new Button { Padding = new Thickness(12, 5, 12, 5), HorizontalAlignment = HorizontalAlignment.Right };
        toggle.Click += (_, _) => { Folder.State.Grid = !Folder.State.Grid; Refresh(); Program.Save(); };
        DockPanel.SetDock(toggle, Dock.Right); header.Children.Add(toggle);
        header.Children.Add(new TextBlock { Text = folder.State.Name, FontSize = 22, FontWeight = FontWeights.SemiBold });
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var footer = new TextBlock { Text = "示例文件 · 双击打开 · 多选拖出", FontSize = 11, Foreground = Brushes.LightGray, Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        list = new ListBox { Background = Brushes.Transparent, BorderThickness = new Thickness(0), SelectionMode = SelectionMode.Extended, AllowDrop = true };
        list.PreviewMouseLeftButtonDown += (_, e) => dragStart = e.GetPosition(list);
        list.PreviewMouseMove += (_, e) =>
        {
            if (dragging || e.LeftButton != MouseButtonState.Pressed) return;
            var point = e.GetPosition(list);
            if (Math.Abs(point.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            var paths = list.SelectedItems.Cast<ListBoxItem>().Select(item => (string)item.Tag).ToArray();
            if (paths.Length == 0) return;
            dragging = true;
            try { DragDrop.DoDragDrop(list, new DataObject(DataFormats.FileDrop, paths), DragDropEffects.Move); }
            finally { dragging = false; Refresh(); Folder.Refresh(); }
        };
        list.MouseDoubleClick += (_, _) => { if (list.SelectedItem is ListBoxItem item) Program.Open((string)item.Tag); };
        list.DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; };
        list.Drop += (_, e) => { if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) e.Effects = Program.MoveInto(Folder, paths) ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; };
        root.Children.Add(list); Content = root;
        Deactivated += (_, _) => { if (!dragging && !Program.Capturing) Close(); };
        SizeChanged += (_, _) => { Folder.State.PanelWidth = Width; Folder.State.PanelHeight = Height; };
        Closed += (_, _) => { if (Program.Panel == this) Program.Panel = null; Program.Save(); };
        Refresh();
    }

    internal void Refresh()
    {
        toggle.Content = Folder.State.Grid ? "切换列表" : "切换网格";
        var factory = new FrameworkElementFactory(Folder.State.Grid ? typeof(WrapPanel) : typeof(StackPanel));
        list.ItemsPanel = new ItemsPanelTemplate(factory);
        list.Items.Clear();
        if (!Directory.Exists(Folder.Path)) return;
        foreach (var path in Directory.GetFileSystemEntries(Folder.Path).OrderBy(System.IO.Path.GetFileName))
        {
            var name = System.IO.Path.GetFileName(path);
            var symbol = Directory.Exists(path) ? "▣" : "▤";
            var content = new TextBlock { Text = Folder.State.Grid ? $"{symbol}\n\n{name}" : $"{symbol}   {name}", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White, Margin = new Thickness(10), FontSize = 14 };
            list.Items.Add(new ListBoxItem { Tag = path, Content = content, Width = Folder.State.Grid ? 122 : double.NaN, MinHeight = Folder.State.Grid ? 110 : 40, Margin = new Thickness(3), Background = new SolidColorBrush(Color.FromRgb(43, 55, 72)) });
        }
    }
}

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct POINT { internal int X, Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct RECT { internal int Left, Top, Right, Bottom; }
    internal delegate bool EnumProc(IntPtr hwnd, IntPtr parameter);
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc callback, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? className, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(IntPtr hwnd, StringBuilder name, int length);
    [DllImport("user32.dll")] internal static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll")] internal static extern IntPtr GetParent(IntPtr child);
    [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hwnd, out RECT rectangle);
    [DllImport("user32.dll")] internal static extern IntPtr WindowFromPoint(POINT point);
    [DllImport("user32.dll")] internal static extern bool IsChild(IntPtr parent, IntPtr child);
    [DllImport("user32.dll")] internal static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] internal static extern bool BitBlt(IntPtr output, int x, int y, int width, int height, IntPtr input, int sourceX, int sourceY, uint operation);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern bool ScreenToClient(IntPtr hwnd, ref POINT point);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] internal static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    internal static IntPtr FindDesktopHost()
    {
        var found = IntPtr.Zero;
        EnumWindows((window, _) =>
        {
            var view = FindWindowEx(window, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (view == IntPtr.Zero) return true;
            found = view; return false;
        }, IntPtr.Zero);
        return found;
    }

    internal static string[] WindowClasses()
    {
        var classes = new HashSet<string>();
        EnumWindows((window, _) => { var name = new StringBuilder(256); GetClassName(window, name, name.Capacity); classes.Add(name.ToString()); return true; }, IntPtr.Zero);
        return classes.Where(value => value is "Progman" or "WorkerW" or "SHELLDLL_DefView" or "Shell_TrayWnd").OrderBy(value => value).ToArray();
    }
}
