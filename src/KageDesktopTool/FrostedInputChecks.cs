using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Kage.Workspace;

namespace Kage.Desktop;

// 验收通过系统输入操作正式样式入口；只读写随机隔离内容和状态。
internal static class FrostedInputChecks
{
    internal static int Run(bool unavailable = false)
    {
        var fixture = Path.Combine(Path.GetTempPath(), "Kage-frosted-" + Guid.NewGuid().ToString("N"));
        var evidence = Path.Combine(Environment.CurrentDirectory, ".scratch", "folder-frosted-background", "verification");
        Directory.CreateDirectory(fixture);
        Directory.CreateDirectory(evidence);
        var log = Path.Combine(evidence, unavailable ? "01-不可用.txt" : "01-正式输入.txt");
        File.WriteAllText(log, "毛玻璃正式入口真实输入\n");
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Runtime? runtime = null;
        Window? background = null;
        var exit = 1;
        var desktopShown = false;
        Func<bool>? capability = unavailable ? () => false : null;
        WindowsDesktop.GetCursorPos(out var cursor);
        app.Startup += async (_, _) =>
        {
            try
            {
                var areas = WindowsDesktop.Displays();
                File.AppendAllText(log, $"环境：{Environment.OSVersion}; {string.Join("; ", areas.Select(a => a.ToString()))}\n");
                var disk = new JsonWorkspaceStore(Path.Combine(fixture, "状态"));
                var store = new FaultStore(disk);
                IDesktopWorkspace workspace = new DesktopWorkspace(store, new NoStartup());
                await workspace.InitializeAsync(areas);
                await workspace.SelectRootAsync(Path.Combine(fixture, "内容"));
                await workspace.CreateFolderAsync("毛玻璃正式预览");
                var id = workspace.Snapshot.Folders.Single().Folder.Id;
                var move = workspace.BeginLayout(id)!;
                move.BeginDrag(0, 0);
                move.DragTo(1200 - move.Folders.Single().Folder.X, 250 - move.Folders.Single().Folder.Y);
                move.EndDrag();
                await workspace.CommitLayoutAsync(move);
                await workspace.ToggleFolderAsync(id);
                var file = Path.Combine(workspace.Snapshot.Folders.Single().ActualPath, "清晰原生图标.txt");
                File.WriteAllText(file, "实际内容保持不变");
                var marker = Path.Combine(fixture, "真实 Shell 启动.txt");
                var shortcut = Path.Combine(Path.GetDirectoryName(file)!, "启动检查.lnk");
                Shortcut(shortcut, marker);
                await workspace.RefreshAsync(areas);
                runtime = new Runtime(workspace, frostedCapability: capability);
                WinD(); desktopShown = true;
                await Task.Delay(500);
                var stripes = new Canvas { Background = Brushes.DarkBlue };
                for (var x = 0; x < 900; x += 12)
                {
                    var stripe = new Border { Width = 6, Height = 700, Background = Brushes.White };
                    Canvas.SetLeft(stripe, x); stripes.Children.Add(stripe);
                }
                background = new Window { WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false,
                    Width = 900, Height = 700, Content = stripes };
                background.Show();
                var backgroundHandle = new WindowInteropHelper(background).Handle;
                var backgroundHost = WindowsDesktop.Attach(backgroundHandle);
                WindowsDesktop.Position(backgroundHandle, backgroundHost, 1100, 220, 900, 700);
                var header = runtime.Headers[id];
                var original = workspace.Snapshot.Folders.Single().Folder;
                var dialog = runtime.CreateAppearance(id)!;
                dialog.WindowStartupLocation = WindowStartupLocation.Manual;
                dialog.Left = 25; dialog.Top = 25;
                dialog.Show(); dialog.Activate();
                await Task.Delay(300);
                await Click(dialog.Frosted);
                if (unavailable)
                {
                    Require(dialog.Frosted.IsChecked == true && !header.FrostedVisible
                        && dialog.FrostedStatus.Text == "当前环境无法显示毛玻璃"
                        && ((SolidColorBrush)header.Surface.Background).Color.A == 173,
                        "平台能力不可用时正式入口保留原颜色与透明度并解释原因");
                    await Click(dialog.ApplyButton);
                    Require(workspace.Snapshot.Folders.Single().Folder.Frosted, "不可用期间仍可保存毛玻璃启用意图");
                    runtime.Dispose(); runtime = null;
                    workspace = new DesktopWorkspace(store, new NoStartup());
                    await workspace.InitializeAsync(areas);
                    runtime = new Runtime(workspace, frostedCapability: capability);
                    header = runtime.Headers[id];
                    Require(header.Record.Frosted && !header.FrostedVisible && header.Record.Color == original.Color
                        && header.Record.Opacity == original.Opacity && File.ReadAllText(file) == "实际内容保持不变",
                        "不可用状态重启保留开启意图和完整旧样式、内容");
                    dialog = runtime.CreateAppearance(id)!;
                    dialog.WindowStartupLocation = WindowStartupLocation.Manual; dialog.Left = 25; dialog.Top = 25;
                    dialog.Show();
                    await header.Contents.IconsLoaded.WaitAsync(TimeSpan.FromSeconds(15));
                    await Task.Delay(350);
                    Screenshot("01-不可用保留意图.png");
                    dialog.Close();
                    exit = 0;
                    return;
                }
                Require(header.FrostedVisible && header.Record.Frosted && workspace.Snapshot.Folders.Single().Folder == original,
                    "真实开关立即显示毛玻璃，正式值及布局不变");
                await workspace.RefreshAsync(areas);
                runtime.Render();
                Require(header.FrostedVisible && header.Record.Frosted && workspace.Snapshot.Folders.Single().Folder == original,
                    "正式后台刷新保留有效毛玻璃草稿");
                Screenshot("01-正式预览.png");
                await Click(dialog.Transparency);
                Key(0x23);
                await Task.Delay(250);
                Require(((SolidColorBrush)header.Surface.Background).Color.A == 0 && header.FrostedVisible,
                    "真实滑块 100% 透明保留模糊且去除着色");
                Screenshot("01-完全透明毛玻璃.png");
                var blue = ScreenPixel(1500, 600);
                stripes.Background = Brushes.DarkGreen;
                await Task.Delay(400);
                var green = ScreenPixel(1500, 600);
                Require(green.G > blue.G + 20 && green.B < blue.B - 20, "正式毛玻璃随实际后方蓝白条纹变为绿白条纹实时变化");
                Screenshot("01-动态背景绿.png");
                Key(0x24);
                await Task.Delay(250);
                Require(((SolidColorBrush)header.Surface.Background).Color.A == 255 && header.FrostedVisible,
                    "真实滑块 0% 透明使用完全不透明颜色");
                Screenshot("01-完全不透明着色.png");
                Key(0x1B);
                await Task.Delay(300);
                Require(!header.FrostedVisible && workspace.Snapshot.Folders.Single().Folder == original,
                    "真实 Esc 撤销完整样式并释放效果");
                dialog = runtime.CreateAppearance(id)!;
                dialog.WindowStartupLocation = WindowStartupLocation.Manual;
                dialog.Left = 25; dialog.Top = 25;
                dialog.Show(); dialog.Activate();
                await Task.Delay(200);
                await Click(dialog.Frosted);
                store.FailNext = true;
                await Click(dialog.ApplyButton);
                Require(dialog.IsVisible && !dialog.Interaction.Closed && workspace.Snapshot.Folders.Single().Folder == original
                    && header.FrostedVisible && dialog.Error.Text.Length != 0, "真实应用保存故障保留有效草稿、原正式记录并显示可重试提示");
                await Click(dialog.ApplyButton);
                await Task.Delay(300);
                Require(workspace.Snapshot.Folders.Single().Folder.Frosted && header.FrostedVisible,
                    "正式应用按钮保存逐 Folder 毛玻璃意图");
                var saved = workspace.Snapshot.Folders.Single().Folder;
                var focus = new Window { Title = "隔离其他应用", Width = 300, Height = 150, Left = 25, Top = 650,
                    Content = new TextBlock { Text = "保持前台焦点", FontSize = 20 } };
                focus.Show(); focus.Activate();
                await Task.Delay(250);
                var active = GetForegroundWindow();
                var point = header.HeaderInput.PointToScreen(new Point(35, header.HeaderInput.ActualHeight / 2));
                await ClickAt(point);
                Require(GetForegroundWindow() == active && header.FrostedVisible, "真实前景点击仍保持另一应用焦点且毛玻璃持续显示");
                Screenshot("01-失焦持续效果.png");
                focus.Close();
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var process = System.Diagnostics.Process.GetCurrentProcess();
                var cpu = process.TotalProcessorTime;
                SetCursorPos((int)point.X, (int)point.Y);
                mouse_event(2, 0, 0, 0, UIntPtr.Zero);
                await Task.Delay(100);
                for (var step = 1; step <= 18; step++)
                {
                    SetCursorPos((int)point.X + step * 10, (int)point.Y + step * 4);
                    await Task.Delay(30);
                }
                mouse_event(4, 0, 0, 0, UIntPtr.Zero);
                await Task.Delay(300);
                Require(!runtime.Interacting && header.FrostedVisible
                    && workspace.Snapshot.Folders.Single().Folder.X == saved.X + 180,
                    "真实鼠标拖动后毛玻璃保持有效并按落点保存布局");
                saved = workspace.Snapshot.Folders.Single().Folder;
                Screenshot("01-真实拖动落点.png");
                File.AppendAllText(log, $"拖动基础性能：墙钟 {watch.Elapsed.TotalMilliseconds:0} ms；进程 CPU {(process.TotalProcessorTime - cpu).TotalMilliseconds:0} ms；私有内存 {process.PrivateMemorySize64 / 1048576.0:0.0} MiB；GDI {GetGuiResources(process.Handle, 0)}，USER {GetGuiResources(process.Handle, 1)}。\n");
                header.UpdateLayout();
                var item = header.Contents.Items.Items.Cast<ListBoxItem>().Single(entry => (string)entry.Tag == shortcut);
                await Click(item);
                await Click(item);
                for (var attempt = 0; attempt < 30 && !File.Exists(marker); attempt++) await Task.Delay(100);
                Require(File.Exists(marker), "毛玻璃前景真实双击快捷方式由 Windows Shell 启动隔离目标");
                await Click(header.ExpandButton);
                Require(!workspace.Snapshot.Folders.Single().Folder.Expanded && header.FrostedVisible,
                    "真实折叠只保留毛玻璃头部");
                Screenshot("01-折叠头部.png");
                await Click(header.ExpandButton);
                saved = workspace.Snapshot.Folders.Single().Folder;
                runtime.Dispose(); runtime = null;
                workspace = new DesktopWorkspace(store, new NoStartup());
                await workspace.InitializeAsync(areas);
                runtime = new Runtime(workspace, frostedCapability: capability);
                header = runtime.Headers[id];
                await header.Contents.IconsLoaded.WaitAsync(TimeSpan.FromSeconds(15));
                await Task.Delay(300);
                Require(header.FrostedVisible && workspace.Snapshot.Folders.Single().Folder == saved
                    && File.ReadAllText(file) == "实际内容保持不变", "重建正式桌面入口恢复毛玻璃及原内容");
                Screenshot("01-重启恢复.png");
                exit = 0;
            }
            catch (Exception e) { File.AppendAllText(log, "失败：" + e + "\n"); }
            finally
            {
                runtime?.Dispose();
                background?.Close();
                if (desktopShown) WinD();
                SetCursorPos(cursor.X, cursor.Y);
                app.Shutdown();
            }
        };
        app.Run();
        Directory.Delete(fixture, true);
        return exit;

        void Require(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            File.AppendAllText(log, "通过：" + message + "\n");
        }
        void Screenshot(string name)
        {
            using var bitmap = new System.Drawing.Bitmap(2560, 1600);
            using var graphics = System.Drawing.Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(0, 0, 0, 0, bitmap.Size);
            bitmap.Save(Path.Combine(evidence, name));
        }
    }

    private static async Task Click(FrameworkElement element)
    {
        element.UpdateLayout();
        var point = element.PointToScreen(new Point(Math.Min(30, element.ActualWidth / 2), element.ActualHeight / 2));
        await ClickAt(point);
    }
    private static async Task ClickAt(Point point)
    {
        SetCursorPos((int)point.X, (int)point.Y);
        mouse_event(2, 0, 0, 0, UIntPtr.Zero);
        mouse_event(4, 0, 0, 0, UIntPtr.Zero);
        await Task.Delay(180);
    }
    private static System.Drawing.Color ScreenPixel(int x, int y)
    {
        using var bitmap = new System.Drawing.Bitmap(1, 1);
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(x, y, 0, 0, bitmap.Size);
        return bitmap.GetPixel(0, 0);
    }
    private static void Shortcut(string path, string marker)
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        dynamic link = shell.CreateShortcut(path);
        try { link.TargetPath = Path.Combine(AppContext.BaseDirectory, "KageDesktopTool.exe"); link.Arguments = "--open-target-check \"" + marker + "\""; link.Save(); }
        finally { Marshal.FinalReleaseComObject(link); Marshal.FinalReleaseComObject(shell); }
    }
    private sealed class FaultStore(IWorkspaceStore real) : IWorkspaceStore
    {
        internal bool FailNext;
        public StateRead Read() => real.Read();
        public void RestoreBackup() => real.RestoreBackup();
        public void Save(WorkspaceState state)
        {
            if (FailNext) { FailNext = false; throw new IOException("隔离样式保存故障"); }
            real.Save(state);
        }
    }
    private static void Key(byte key)
    {
        WindowsDesktop.keybd_event(key, 0, 0, UIntPtr.Zero);
        WindowsDesktop.keybd_event(key, 0, 2, UIntPtr.Zero);
    }
    private static void WinD()
    {
        WindowsDesktop.keybd_event(0x5B, 0, 0, UIntPtr.Zero);
        Key(0x44);
        WindowsDesktop.keybd_event(0x5B, 0, 2, UIntPtr.Zero);
    }
    private sealed class NoStartup : IStartupRegistration
    {
        public string LaunchCommand => "隔离毛玻璃验收不注册自启";
        public string? ReadCommand() => null;
        public void WriteCommand(string? command) => throw new InvalidOperationException();
    }
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetGuiResources(IntPtr process, uint flags);
}
