using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using Kage.Workspace;
using Microsoft.Win32;
using Windows.UI.ViewManagement;

namespace Kage.Desktop;

// 通过正式 Runtime、Folder 和样式入口观察确定性平台事实，不替代系统设置实测。
internal static class FrostedSystemChecks
{
    internal static int Run(bool realSettings = false, bool realEnergy = false)
    {
        var fixture = Path.Combine(Path.GetTempPath(), "Kage-frosted-system-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        var evidence = DesktopCheckSession.EvidenceFor("folder-frosted-background");
        Directory.CreateDirectory(evidence);
        var log = Path.Combine(evidence, realEnergy ? "03-真实节能模式.txt" : realSettings ? "03-真实系统设置.txt" : "03-确定性轨迹.txt");
        File.WriteAllText(log, realEnergy ? "真实 Windows 节能设置：物理鼠标切换系统设置控件。\n" : realSettings ? "真实 Windows 系统设置：注册表透明设置＋系统广播；高对比度 SPI。\n" : "确定性 Windows 平台事实，经正式 Runtime / Folder / StyleDialog。\n");
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Runtime? runtime = null;
        var exit = 1;
        app.Startup += async (_, _) =>
        {
            try
            {
                IDesktopWorkspace workspace = new DesktopWorkspace(new JsonWorkspaceStore(Path.Combine(fixture, "状态")), new NoStartup());
                await workspace.InitializeAsync(WindowsDesktop.Displays());
                await workspace.SelectRootAsync(Path.Combine(fixture, "内容"));
                await workspace.CreateFolderAsync("系统限制轨迹");
                var id = workspace.Snapshot.Folders.Single().Folder.Id;
                var move = workspace.BeginLayout(id)!;
                move.BeginDrag(0, 0);
                move.DragTo(1200 - move.Folders.Single().Folder.X, 250 - move.Folders.Single().Folder.Y);
                move.EndDrag();
                await workspace.CommitLayoutAsync(move);
                var style = workspace.BeginAppearance(id)!;
                style.SetFrosted(true);
                await workspace.ApplyAppearanceAsync(style);
                var saved = workspace.Snapshot.Folders.Single().Folder;
                if (realSettings || realEnergy)
                {
                    runtime = new Runtime(workspace);
                    if (realEnergy) await RealEnergy(runtime, id, evidence, log);
                    else await RealSettings(runtime, id, evidence, log);
                    exit = 0;
                    return;
                }
                var environment = new FrostedEnvironment();
                runtime = new Runtime(workspace, frostedEnvironment: () => environment);
                var header = runtime.Headers[id];
                await Task.Delay(300);
                Require(header.FrostedVisible, "无限制时显示毛玻璃");
                var dialog = runtime.CreateAppearance(id)!;
                environment = environment with { TransparencyEnabled = false, HighContrast = true };
                runtime.RefreshFrostedEnvironment();
                Require(!header.FrostedVisible && header.IsVisible && dialog.FrostedStatus.Text.Contains("透明效果")
                    && dialog.FrostedStatus.Text.Contains("高对比度") && dialog.Frosted.IsChecked == true
                    && workspace.Snapshot.Folders.Single().Folder == saved, "多限制释放效果、刷新提示并保留意图与前景");
                environment = environment with { TransparencyEnabled = true };
                runtime.RefreshFrostedEnvironment();
                Require(!header.FrostedVisible && !dialog.FrostedStatus.Text.Contains("透明效果")
                    && dialog.FrostedStatus.Text.Contains("高对比度"), "只解除一项限制仍暂停并清除过期原因");
                environment = new FrostedEnvironment();
                runtime.RefreshFrostedEnvironment();
                Require(header.FrostedVisible && dialog.FrostedStatus.Text == "", "全部解除自动重建并清除原因");
                var oldHost = header.Host;
                environment = environment with { EnergySaver = 1 };
                runtime.RefreshFrostedEnvironment();
                Require(!header.FrostedVisible && !WindowsDesktop.IsWindow(oldHost)
                    && dialog.FrostedStatus.Text.Contains("省电模式"), "标准节能暂停并销毁不需要的原生背景窗口");
                environment = environment with { EnergySaver = 2 };
                runtime.RefreshFrostedEnvironment();
                Require(!header.FrostedVisible && dialog.FrostedStatus.Text.Contains("省电模式"), "高度节能保持暂停");
                environment = environment with { EnergySaver = 0, Available = false };
                runtime.RefreshFrostedEnvironment();
                Require(!header.FrostedVisible && dialog.FrostedStatus.Text == "当前环境无法显示毛玻璃"
                    && workspace.Snapshot.Folders.Single().Folder.Frosted, "能力失效保留启用意图并区分环境原因");
                environment = new FrostedEnvironment(EnergySaver: uint.MaxValue);
                runtime.RefreshFrostedEnvironment();
                Require(!header.FrostedVisible && dialog.FrostedStatus.Text.Contains("当前环境无法显示毛玻璃"), "未知节能事实保持安全回退而不冒充关闭");
                environment = new FrostedEnvironment(TransparencyEnabled: false);
                dialog.Interaction.SetFrosted(false);
                dialog.SelectColor("#A02040");
                dialog.Interaction.SetOpacity(.35);
                dialog.UpdatePreview();
                runtime.RefreshFrostedEnvironment();
                runtime.Render();
                Require(!header.Record.Frosted && header.Record.Color == "#A02040" && header.Record.Opacity == .35
                    && !header.FrostedVisible && dialog.FrostedStatus.Text == ""
                    && workspace.Snapshot.Folders.Single().Folder == saved, "后台刷新和限制变化保持当前有效关闭、颜色与透明度草稿");
                dialog.Close();
                Require(header.Record == saved && !header.FrostedVisible && header.FrostedUnavailableReason.Contains("透明效果"),
                    "取消恢复保存样式并按当前环境继续暂停");
                dialog = runtime.CreateAppearance(id)!;
                dialog.Interaction.SetFrosted(true);
                dialog.SelectColor("#4080C0");
                dialog.Interaction.SetOpacity(.45);
                dialog.UpdatePreview();
                await dialog.ApplyAsync();
                saved = workspace.Snapshot.Folders.Single().Folder;
                Require(saved.Frosted && saved.Color == "#4080C0" && saved.Opacity == .45 && !header.FrostedVisible,
                    "暂停期间完整样式一次保存且前景正常");
                environment = new FrostedEnvironment();
                runtime.RefreshFrostedEnvironment();
                Require(header.FrostedVisible && header.Record == saved, "解除后恢复最新应用样式");
                dialog = runtime.CreateAppearance(id)!;
                dialog.Interaction.SetFrosted(false);
                dialog.UpdatePreview();
                environment = environment with { HighContrast = true };
                runtime.RefreshFrostedEnvironment();
                environment = new FrostedEnvironment();
                runtime.RefreshFrostedEnvironment();
                Require(!header.FrostedVisible && !header.Record.Frosted && workspace.Snapshot.Folders.Single().Folder.Frosted,
                    "关闭草稿在环境恢复后仍关闭且不污染正式意图");
                dialog.Close();
                Require(header.FrostedVisible, "取消关闭草稿按已恢复环境重新显示正式启用样式");
                dialog = runtime.CreateAppearance(id)!;
                dialog.Interaction.SetFrosted(false);
                dialog.SelectColor("#FF0000");
                var newer = workspace.BeginAppearance(id)!;
                newer.SetHex("#208060");
                await workspace.ApplyAppearanceAsync(newer);
                saved = workspace.Snapshot.Folders.Single().Folder;
                environment = new FrostedEnvironment(HighContrast: true);
                runtime.RefreshFrostedEnvironment();
                Require(header.Record == saved && !header.FrostedVisible && dialog.FrostedStatus.Text.Contains("高对比度"),
                    "环境变化重新核对草稿有效性，过期关闭草稿不能覆盖更新的正式样式");
                dialog.Close();
                environment = new FrostedEnvironment();
                runtime.RefreshFrostedEnvironment();
                using var process = Process.GetCurrentProcess();
                var gdiBefore = GetGuiResources(process.Handle, 0);
                var userBefore = GetGuiResources(process.Handle, 1);
                for (var cycle = 0; cycle < 15; cycle++)
                {
                    oldHost = header.Host;
                    environment = new FrostedEnvironment(TransparencyEnabled: false);
                    runtime.RefreshFrostedEnvironment();
                    Require(!WindowsDesktop.IsWindow(oldHost) && header.IsVisible, $"暂停循环 {cycle + 1} 释放旧 HWND 且保留前景");
                    environment = new FrostedEnvironment();
                    runtime.RefreshFrostedEnvironment();
                    Require(header.FrostedVisible, $"恢复循环 {cycle + 1} 重建当前有效效果");
                }
                File.AppendAllText(log, $"15次暂停／恢复：GDI {gdiBefore} → {GetGuiResources(process.Handle, 0)}；USER {userBefore} → {GetGuiResources(process.Handle, 1)}。每次旧背景 HWND 均已销毁。\n");
                runtime.Dispose();
                workspace = new DesktopWorkspace(new JsonWorkspaceStore(Path.Combine(fixture, "状态")), new NoStartup());
                await workspace.InitializeAsync(WindowsDesktop.Displays());
                environment = new FrostedEnvironment(TransparencyEnabled: false);
                runtime = new Runtime(workspace, frostedEnvironment: () => environment);
                header = runtime.Headers[id];
                Require(header.Record == saved && !header.FrostedVisible, "暂停状态下重启读取完整保存意图并继续尊重限制");
                environment = new FrostedEnvironment();
                runtime.RefreshFrostedEnvironment();
                Require(header.FrostedVisible, "重启后解除限制自动恢复");
                exit = 0;
            }
            catch (Exception error) { File.AppendAllText(log, "失败：" + error + "\n"); }
            finally { runtime?.Dispose(); app.Shutdown(); }
        };
        app.Run();
        return exit;

        void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            File.AppendAllText(log, "通过：" + message + "\n");
        }
    }

    private static async Task RealSettings(Runtime runtime, Guid id, string evidence, string log)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", true)!;
        var originalTransparency = key.GetValue("EnableTransparency");
        var ui = new UISettings();
        var originalHighContrast = new HighContrast { Size = (uint)Marshal.SizeOf<HighContrast>() };
        if (!SystemParametersInfo(0x42, originalHighContrast.Size, ref originalHighContrast, 0)) throw new InvalidOperationException("无法读取高对比度原设置");
        File.AppendAllText(log, $"原设置：EnableTransparency={originalTransparency}，AdvancedEffectsEnabled={ui.AdvancedEffectsEnabled}，HighContrast={SystemParameters.HighContrast}，HCFlags={originalHighContrast.Flags}。\n");
        var header = runtime.Headers[id];
        var dialog = runtime.CreateAppearance(id)!;
        dialog.WindowStartupLocation = WindowStartupLocation.Manual;
        dialog.Left = 10; dialog.Top = 10;
        dialog.Show();
        var contrastChanged = false;
        try
        {
            key.SetValue("EnableTransparency", 0, RegistryValueKind.DWord);
            await Task.Run(Broadcast);
            await WaitFor(() => !ui.AdvancedEffectsEnabled, "真实系统透明效果已关闭");
            await Task.Delay(500);
            Require(!header.FrostedVisible && header.IsVisible && dialog.FrostedStatus.Text.Contains("透明效果"), "真实关闭透明效果后自动暂停并提示原因");
            Screenshot("03-真实透明效果关闭.png");
            key.SetValue("EnableTransparency", 1, RegistryValueKind.DWord);
            await Task.Run(Broadcast);
            await WaitFor(() => ui.AdvancedEffectsEnabled && header.FrostedVisible && dialog.FrostedStatus.Text == "", "真实透明效果恢复后自动重建");
            Screenshot("03-真实透明效果恢复.png");
            var highContrast = originalHighContrast;
            highContrast.Flags |= 1;
            contrastChanged = true;
            Require(await Task.Run(() => SystemParametersInfo(0x43, highContrast.Size, ref highContrast, 3)), "Windows 接受真实高对比度设置");
            await WaitFor(() => SystemParameters.HighContrast && !header.FrostedVisible && dialog.FrostedStatus.Text.Contains("高对比度"), "真实高对比度开启自动暂停并提示");
            await Task.Delay(2500);
            Screenshot("03-真实高对比度开启.png");
            Require(await Task.Run(() => SystemParametersInfo(0x43, originalHighContrast.Size, ref originalHighContrast, 3)), "Windows 恢复原高对比度设置");
            contrastChanged = false;
            await WaitFor(() => !SystemParameters.HighContrast && header.FrostedVisible && dialog.FrostedStatus.Text == "", "高对比度解除后自动恢复效果");
            await Task.Delay(2500);
            Screenshot("03-真实高对比度恢复.png");
            File.AppendAllText(log, $"现代节能事实：{runtime.FrostedEnvironment.EnergySaver}（0/OFF）；此轮未实际触发 STANDARD/HIGH。\n");
        }
        finally
        {
            if (originalTransparency == null) key.DeleteValue("EnableTransparency", false);
            else key.SetValue("EnableTransparency", originalTransparency, RegistryValueKind.DWord);
            await Task.Run(Broadcast);
            if (contrastChanged) await Task.Run(() => SystemParametersInfo(0x43, originalHighContrast.Size, ref originalHighContrast, 3));
            await Task.Delay(800);
            File.AppendAllText(log, $"恢复后：EnableTransparency={key.GetValue("EnableTransparency")}，AdvancedEffectsEnabled={ui.AdvancedEffectsEnabled}，HighContrast={SystemParameters.HighContrast}。\n");
            dialog.Close();
        }

        void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            File.AppendAllText(log, "通过：" + message + "\n");
        }
        async Task WaitFor(Func<bool> condition, string message)
        {
            for (var attempt = 0; attempt < 50 && !condition(); attempt++) await Task.Delay(100);
            Require(condition(), message);
        }
        void Screenshot(string name)
        {
            using var bitmap = new System.Drawing.Bitmap(2560, 1600);
            using var graphics = System.Drawing.Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(0, 0, 0, 0, bitmap.Size);
            bitmap.Save(Path.Combine(evidence, name));
        }
    }

    private static async Task RealEnergy(Runtime runtime, Guid id, string evidence, string log)
    {
        var header = runtime.Headers[id];
        var dialog = runtime.CreateAppearance(id)!;
        dialog.WindowStartupLocation = WindowStartupLocation.Manual;
        dialog.Left = 10; dialog.Top = 10;
        dialog.Show();
        AutomationElement? window = null;
        AutomationElement? toggle = null;
        ToggleState? original = null;
        WindowsDesktop.GetCursorPos(out var cursor);
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:powersleep") { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden });
            for (var attempt = 0; attempt < 30 && window == null; attempt++)
            {
                await Task.Delay(200);
                window = await Task.Run(() => AutomationElement.RootElement.FindFirst(TreeScope.Children,
                    new OrCondition(new PropertyCondition(AutomationElement.NameProperty, "设置"),
                        new PropertyCondition(AutomationElement.NameProperty, "Settings"))));
            }
            if (window == null) throw new InvalidOperationException("未找到实际 Windows 设置窗口");
            for (var attempt = 0; attempt < 30 && toggle == null; attempt++)
            {
                toggle = window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty,
                    "SystemSettings_PowerAndBattery_EnergySaverAlwaysOn_ToggleSwitch"));
                if (toggle != null) break;
                var group = window.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Group))
                    .Cast<AutomationElement>().FirstOrDefault(element => element.Current.Name.StartsWith("节能模式,", StringComparison.Ordinal)
                        || element.Current.Name.StartsWith("Energy saver,", StringComparison.OrdinalIgnoreCase));
                if (group != null)
                {
                    var button = group.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "EntityItemButton"));
                    if (button != null) await Click(button);
                }
                await Task.Delay(300);
            }
            if (toggle == null) throw new InvalidOperationException("当前 Windows 电源页面没有可用的始终使用节能模式控件");
            original = ((TogglePattern)toggle.GetCurrentPattern(TogglePattern.Pattern)).Current.ToggleState;
            File.AppendAllText(log, $"物理系统控件：{toggle.Current.Name}；原状态={original}；现代节能初值={runtime.FrostedEnvironment.EnergySaver}。\n");
            if (original != ToggleState.Off) throw new InvalidOperationException("原节能开关已开启，当前夹具要求从关闭演示；未修改");
            await Click(toggle);
            await WaitFor(() => runtime.FrostedEnvironment.EnergySaver is 1 or 2 && !header.FrostedVisible && dialog.FrostedStatus.Text.Contains("省电模式"),
                "物理开启真实节能模式后，现代通知暂停毛玻璃并提示省电原因");
            File.AppendAllText(log, $"实际开启读数：EnergySaver={runtime.FrostedEnvironment.EnergySaver}；完整原因={dialog.FrostedStatus.Text.Replace("\n", "；")}。\n");
            ShowWindow(new IntPtr(window.Current.NativeWindowHandle), 6);
            await Task.Delay(500);
            Screenshot("03-真实节能模式开启.png");
            ShowWindow(new IntPtr(window.Current.NativeWindowHandle), 9);
            await Task.Delay(300);
            await Click(toggle);
            await WaitFor(() => runtime.FrostedEnvironment.EnergySaver == 0 && header.FrostedVisible && dialog.FrostedStatus.Text == "",
                "物理关闭真实节能模式后自动恢复当前启用样式");
            ShowWindow(new IntPtr(window.Current.NativeWindowHandle), 6);
            await Task.Delay(500);
            Screenshot("03-真实节能模式恢复.png");
        }
        finally
        {
            if (original != null && toggle != null && window != null)
            {
                ShowWindow(new IntPtr(window.Current.NativeWindowHandle), 9);
                await Task.Delay(300);
                if (((TogglePattern)toggle.GetCurrentPattern(TogglePattern.Pattern)).Current.ToggleState != original) await Click(toggle);
            }
            await Task.Delay(500);
            File.AppendAllText(log, $"结束后现代节能={runtime.FrostedEnvironment.EnergySaver}；透明={runtime.FrostedEnvironment.TransparencyEnabled}；高对比={runtime.FrostedEnvironment.HighContrast}。\n");
            if (window != null)
            {
                var close = window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "Close"));
                if (close != null) ((InvokePattern)close.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            }
            dialog.Close();
            SetCursorPos(cursor.X, cursor.Y);
        }

        async Task WaitFor(Func<bool> condition, string message)
        {
            for (var attempt = 0; attempt < 80 && !condition(); attempt++) await Task.Delay(100);
            if (!condition()) throw new InvalidOperationException(message);
            File.AppendAllText(log, "通过：" + message + "\n");
        }
        void Screenshot(string name)
        {
            using var bitmap = new System.Drawing.Bitmap(2560, 1600);
            using var graphics = System.Drawing.Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(0, 0, 0, 0, bitmap.Size);
            bitmap.Save(Path.Combine(evidence, name));
        }
        static async Task Click(AutomationElement element)
        {
            var rectangle = element.Current.BoundingRectangle;
            SetCursorPos((int)(rectangle.Left + rectangle.Width / 2), (int)(rectangle.Top + rectangle.Height / 2));
            mouse_event(2, 0, 0, 0, UIntPtr.Zero);
            mouse_event(4, 0, 0, 0, UIntPtr.Zero);
            await Task.Delay(300);
        }
    }

    private static void Broadcast() => SendMessageTimeout(new IntPtr(0xffff), 0x1a, UIntPtr.Zero, "ImmersiveColorSet", 2, 100, out _);
    [StructLayout(LayoutKind.Sequential)] private struct HighContrast { internal uint Size; internal uint Flags; internal IntPtr Scheme; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint message, UIntPtr wp, string lp, uint flags, uint timeout, out UIntPtr result);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SystemParametersInfo(uint action, uint size, ref HighContrast value, uint flags);
    [DllImport("user32.dll")] private static extern uint GetGuiResources(IntPtr process, uint flags);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);

    private sealed class NoStartup : IStartupRegistration
    {
        public string LaunchCommand => "隔离系统限制验收不注册自启";
        public string? ReadCommand() => null;
        public void WriteCommand(string? command) => throw new InvalidOperationException();
    }
}
