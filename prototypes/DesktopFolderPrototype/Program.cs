// 临时原型：验证头部与下方展示区、多 Folder 不重叠布局及真实示例文件移动。
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
    public double Width { get; set; } = 300;
    public double Height { get; set; } = 48;
    public double PanelWidth { get; set; } = 460;
    public double PanelHeight { get; set; } = 260;
    public string Color { get; set; } = "#666666";
    public double Opacity { get; set; } = .68;
    public bool Grid { get; set; } = true;
    public bool Expanded { get; set; }
    public int LayoutVersion { get; set; }
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
        if (args.Contains("--make-icons")) { IconChoices.Generate(); return; }
        if (args.Contains("--drag-regression")) { Environment.ExitCode = DragRegression.Run(args.Contains("--minimal")); return; }
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
            saved = new() { new(), new() { Name = "灵感", X = 850 } };
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
        IconChoices.AddMenu(menu);
        Tray.ContextMenuStrip = menu;
        IconChoices.Load();
        Tray.DoubleClick += (_, _) => App.Dispatcher.Invoke(ShowStatus);
        foreach (var state in saved)
        {
            if (state.LayoutVersion < 2)
            {
                state.Width = Math.Max(300, state.Width); state.Height = 48;
                state.PanelHeight = Math.Clamp(state.PanelHeight, 220, 440);
                state.Color = "#666666"; state.Opacity = .68; state.Expanded = false; state.LayoutVersion = 2;
            }
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
        DesktopLayout.Restore(folder);
    }

    internal static void Create()
    {
        var name = Ask("新建 Folder", "文件夹名称", "新建文件夹");
        if (name == null || !ValidName(name)) return;
        if (Directory.Exists(Path.Combine(Storage, name))) { Notice("已存在同名 Folder，请选择其他名称。"); return; }
        Directory.CreateDirectory(Path.Combine(Storage, name));
        Add(new() { Name = name, X = 250, Y = 160, LayoutVersion = 2 });
        Save();
    }

    internal static bool ValidName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith('.') || name.EndsWith(' '))
        { Notice("请输入有效的 Windows 文件夹名称。"); return false; }
        return true;
    }

    internal static void Expand(FolderWindow folder) => folder.Toggle();

    internal static void Rename(FolderWindow folder)
    {
        var name = Ask("重命名 Folder", "同时重命名对应的内容文件夹", folder.State.Name);
        if (name == null || name == folder.State.Name || !ValidName(name)) return;
        try
        {
            var destination = Resolve(Path.Combine(Storage, name));
            if (destination == null) return;
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
            folder.Refresh();
        }
        catch (Exception e) { Notice(e.Message); }
        return moved;
    }

    internal static void Save()
    {
        if (!shuttingDown) File.WriteAllText(System.IO.Path.Combine(Home, "PROTOTYPE-state.json"), JsonSerializer.Serialize(Folders.Select(f => f.State), new JsonSerializerOptions { WriteIndented = true }));
    }

    internal static void Reattach() { foreach (var folder in Folders.ToArray()) { folder.Attach(); DesktopLayout.Restore(folder); } }
    internal static void Open(string path) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
    internal static void Notice(string text) { if (!Capturing) System.Windows.MessageBox.Show(text, "Kage 桌面原型"); }
    internal static void ShowStatus() => Notice($"临时原型 · 示例文件\n桌面宿主：{HostStatus}\n数据目录：{DataRoot}\n\n拖动入口标题调整位置；右下角调整尺寸。\n右键入口：重命名、颜色、透明度、删除。\n倒三角展开／折叠；多个 Folder 可以同时展开。\n\n尚未实现：根目录迁移、开机自启开关。\n这些功能保留在正式需求中。");

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
        var original = Folders.ToDictionary(folder => folder, folder => (folder.State.Expanded, folder.State.Grid, folder.State.X, folder.State.Y));
        try
        {
            var first = Folders.First();
            if (first.State.Expanded) Expand(first);
            Render(first, "入口");
            foreach (var folder in Folders) if (!folder.State.Expanded) Expand(folder);
            await Task.Delay(250);
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
            first.UpdateLayout();
            Render(first, "网格");
            first.State.Grid = false; first.Refresh(); first.UpdateLayout();
            Render(first, "列表");
            first.State.Grid = true; first.Refresh();
            using (var pickerLifetime = new DialogCapture(new StyleDialog(first)))
            {
                pickerLifetime.Window.Show(); await Task.Delay(100);
                Render(pickerLifetime.Window, "外观设置");
            }
            var rectangles = Folders.Select(folder => { Native.GetWindowRect(folder.Handle, out var r); return new Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top); }).ToArray();
            var actualNonOverlap = rectangles.SelectMany((a, index) => rectangles.Skip(index + 1).Select(b => !a.IntersectsWith(b))).All(value => value);
            var rejectedOverlapMove = Folders.Count < 2 || !DesktopLayout.Move(first, Folders[1].State.X, Folders[1].State.Y);
            var rejectedOverlapResize = Folders.Count < 2 || !DesktopLayout.Resize(first, (Folders[1].State.X - first.State.X) / first.Scale + Folders[1].State.Width + 20, first.State.Height, first.State.PanelHeight);
            var report = new
            {
                os = Environment.OSVersion.ToString(), root = DataRoot, host = HostStatus,
                windows = Folders.Select(f => new { name = f.State.Name, handle = f.Handle.ToInt64(), host = f.Host.ToInt64(), parent = Native.GetParent(f.Handle).ToInt64(), visible = Native.IsWindowVisible(f.Handle) }),
                desktopAttached = Folders.All(f => f.Host != IntPtr.Zero && Native.GetParent(f.Handle) == f.Host),
                showDesktopHitTest = showDesktopHit,
                simultaneousExpanded = Folders.Count(folder => folder.State.Expanded),
                actualNonOverlap,
                rejectedOverlapMove,
                rejectedOverlapResize,
                rendering = "透明背景与前景文字分离；Windows 10 兼容声明启用分层子窗口",
                windowClasses = Native.WindowClasses(),
                verified = new[] { "头部与下方展示区", "多个 Folder 同时展开", "列表／网格切换", "调色盘与 HEX／RGB 输入", "透明度滑块", "Windows Shell 图标" },
                pending = new[] { "Win+D 实际交互", "桌面及资源管理器拖拽", "Explorer 重启", "多显示器与 DPI" }
            };
            File.WriteAllText(System.IO.Path.Combine(Home, "PROTOTYPE-report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) { File.WriteAllText(System.IO.Path.Combine(Home, "PROTOTYPE-report.json"), e.ToString()); }
        finally
        {
            foreach (var entry in original)
            { (entry.Key.State.Expanded, entry.Key.State.Grid, entry.Key.State.X, entry.Key.State.Y) = entry.Value; entry.Key.Refresh(); }
            Exit();
        }
    }

    private sealed class DialogCapture : IDisposable
    {
        internal readonly Window Window;
        internal DialogCapture(Window window) => Window = window;
        public void Dispose() => Window.Close();
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
        Save(); shuttingDown = true; recovery?.Stop();
        foreach (var folder in Folders.ToArray()) folder.Close();
        Native.UnregisterHotKey(new WindowInteropHelper(Controller).Handle, 1);
        Tray.Dispose(); Controller.Close(); App.Shutdown();
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
