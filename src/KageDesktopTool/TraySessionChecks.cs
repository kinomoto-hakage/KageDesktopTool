using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using Kage.Workspace;
using Forms = System.Windows.Forms;

namespace Kage.Desktop;

// 真实鼠标经 Explorer 通知区域打开菜单，验证实际显示资源，而非注入菜单事件。
internal static class TraySessionChecks
{
    internal static int Run()
    {
        var fixture = Path.Combine(Path.GetTempPath(), "Kage-tray-" + Guid.NewGuid().ToString("N"));
        var evidence = Path.Combine(Environment.CurrentDirectory, ".scratch", "desktop-folder", "verification");
        Directory.CreateDirectory(evidence);
        var log = Path.Combine(evidence, "tray-session.txt");
        File.WriteAllText(log, $"04 真实托盘会话，{DateTimeOffset.Now:O}\n");
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Runtime? runtime = null;
        FileDrag.GetCursorPos(out var originalCursor);
        var result = 1;
        app.Startup += async (_, _) =>
        {
            try
            {
                var store = new JsonWorkspaceStore(Path.Combine(fixture, "状态"));
                IDesktopWorkspace workspace = new DesktopWorkspace(store, new NoStartup());
                Check((await workspace.InitializeAsync(WindowsDesktop.Displays())).Succeeded, "初始化隔离工作区");
                Check((await workspace.SelectRootAsync(Path.Combine(fixture, "内容"))).Succeeded, "选择隔离根目录");
                runtime = new Runtime(workspace);
                runtime.Tray.Text = "Kage 托盘检查 " + Guid.NewGuid().ToString("N")[..8];
                Check(workspace.Snapshot.IconChoice == "d", "新工作区默认 D");
                runtime.ShowSettings();
                foreach (var option in IconChoices.Options)
                {
                    await SelectAsync(runtime, option.Key, evidence);
                    Check(app.Windows.Cast<Window>().All(window => ReferenceEquals(window.Icon, Runtime.ApplicationIcon)), "真实托盘菜单切换并同步所有打开窗口：" + option.Name);
                }
                File.AppendAllText(log, $"实际任务栏资源：{runtime.Tray.Icon!.Size}；显示缩放：{string.Join(';', WindowsDesktop.Displays().Select(display => display.Scale))}\n");
                var committedIcon = runtime.Tray.Icon; var committedWindowIcon = Runtime.ApplicationIcon;
                using (File.Open(Path.Combine(fixture, "状态", "workspace.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var failure = await runtime.SelectIconAsync("a");
                    Check(!failure.Succeeded && workspace.Snapshot.IconChoice == "d" && store.Read().State.IconChoice == "d"
                        && ReferenceEquals(runtime.Tray.Icon, committedIcon) && ReferenceEquals(Runtime.ApplicationIcon, committedWindowIcon),
                        "真实文件锁定导致保存失败时，托盘、窗口、选中标记和持久偏好保持 D");
                    Verify(runtime, "d");
                }
                Check((await runtime.SelectIconAsync("b")).Succeeded, "解除文件锁定后可保存 B");
                runtime.Dispose();
                workspace = new DesktopWorkspace(store, new NoStartup());
                Check((await workspace.InitializeAsync(WindowsDesktop.Displays())).Succeeded, "重新读取磁盘状态");
                runtime = new Runtime(workspace);
                runtime.Tray.Text = "Kage 托盘检查 " + Guid.NewGuid().ToString("N")[..8];
                Verify(runtime, "b");
                Check(workspace.Snapshot.IconChoice == "b", "重建工作区及运行时恢复 B");
                await SelectAsync(runtime, "c", evidence);
                Check(true, "恢复后真实菜单继续有效");
                result = 0;
            }
            catch (Exception e) { File.AppendAllText(log, "失败：" + e + "\n"); }
            finally
            {
                runtime?.Dispose();
                SetCursorPos(originalCursor.X, originalCursor.Y);
                var full = Path.GetFullPath(fixture);
                if (full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                    && Path.GetFileName(full).StartsWith("Kage-tray-", StringComparison.Ordinal) && Directory.Exists(full)) Directory.Delete(full, true);
                app.Shutdown();
            }
        };
        app.Run();
        return result;

        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            File.AppendAllText(log, "通过：" + message + "\n");
        }
    }

    internal static async Task SelectAsync(Runtime runtime, string key, string evidence)
    {
        AutomationElement? button = null;
        var overflowOpened = false;
        await WaitUntil(() =>
        {
            var buttons = AutomationElement.RootElement.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)).Cast<AutomationElement>().ToArray();
            button = buttons.FirstOrDefault(element => element.Current.Name.Contains(runtime.Tray.Text, StringComparison.Ordinal)
                && !element.Current.IsOffscreen && element.Current.BoundingRectangle.Width > 0);
            if (button == null && !overflowOpened)
            {
                var overflow = buttons.FirstOrDefault(element => element.Current.Name is "显示隐藏的图标" or "Show hidden icons");
                if (overflow != null)
                {
                    var bounds = overflow.Current.BoundingRectangle;
                    Click(new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2));
                    overflowOpened = true;
                }
            }
            return button != null;
        });
        var rectangle = button!.Current.BoundingRectangle;
        var center = new Point(rectangle.Left + rectangle.Width / 2, rectangle.Top + rectangle.Height / 2);
        Click(center, right: true);
        var menu = runtime.Tray.ContextMenuStrip!;
        await WaitUntil(() => menu.Visible);
        var choices = (Forms.ToolStripMenuItem)menu.Items[2];
        Click(Center(menu, choices));
        await WaitUntil(() => choices.DropDown.Visible);
        var option = choices.DropDownItems.Cast<Forms.ToolStripMenuItem>().Single(item => item.Text == IconChoices.Options.Single(option => option.Key == key).Name);
        Click(Center(choices.DropDown, option));
        await WaitUntil(() => runtime.Workspace.Snapshot.IconChoice == key && !menu.Visible);
        Verify(runtime, key);
        await Task.Delay(250);
        using var capture = new System.Drawing.Bitmap((int)Math.Ceiling(rectangle.Width), (int)Math.Ceiling(rectangle.Height));
        using var graphics = System.Drawing.Graphics.FromImage(capture);
        graphics.CopyFromScreen((int)rectangle.Left, (int)rectangle.Top, 0, 0, capture.Size);
        capture.Save(Path.Combine(evidence, $"04-真实托盘-{key}.png"));
    }

    internal static void Verify(Runtime runtime, string key)
    {
        var actual = runtime.Tray.Icon ?? throw new InvalidOperationException("托盘缺少图标。");
        if (actual.Size != WindowsDesktop.TrayIconSize()) throw new InvalidOperationException("托盘图标尺寸与实际任务栏 DPI 不符。");
        using var expected = new System.Drawing.Icon(Path.Combine(AppContext.BaseDirectory, "Assets", $"tray-{key}.ico"), actual.Size);
        using var actualPixels = actual.ToBitmap(); using var expectedPixels = expected.ToBitmap();
        if (!Enumerable.Range(0, actual.Width).All(x => Enumerable.Range(0, actual.Height).All(y => actualPixels.GetPixel(x, y) == expectedPixels.GetPixel(x, y))))
            throw new InvalidOperationException("真实 NotifyIcon 资源与已提交方案不符。");
        var choices = (Forms.ToolStripMenuItem)runtime.Tray.ContextMenuStrip!.Items[2];
        if (choices.DropDownItems.Cast<Forms.ToolStripMenuItem>().Single(item => item.Checked).Text != IconChoices.Options.Single(option => option.Key == key).Name)
            throw new InvalidOperationException("菜单选中方案不符。");
    }

    private static Point Center(Forms.ToolStrip menu, Forms.ToolStripItem item)
    {
        var point = menu.PointToScreen(new System.Drawing.Point(item.Bounds.Left + item.Bounds.Width / 2, item.Bounds.Top + item.Bounds.Height / 2));
        return new Point(point.X, point.Y);
    }

    private static void Click(Point point, bool right = false)
    {
        SetCursorPos((int)point.X, (int)point.Y);
        mouse_event(right ? 8u : 2u, 0, 0, 0, UIntPtr.Zero);
        mouse_event(right ? 16u : 4u, 0, 0, 0, UIntPtr.Zero);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline) { if (condition()) return; await Task.Delay(100); }
        throw new TimeoutException("真实托盘交互未在时限内完成。");
    }

    private sealed class NoStartup : IStartupRegistration
    {
        public string LaunchCommand => "隔离托盘检查不注册自启";
        public string? ReadCommand() => null;
        public void WriteCommand(string? command) => throw new InvalidOperationException("托盘检查不得修改自启。");
    }

    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
}
