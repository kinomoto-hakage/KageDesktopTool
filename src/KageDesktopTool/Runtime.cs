using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Kage.Workspace;
using Forms = System.Windows.Forms;
using Microsoft.Win32;

namespace Kage.Desktop;

internal sealed class Runtime : IDisposable
{
    internal static Runtime Current { get; private set; } = null!;
    internal static BitmapImage ApplicationIcon => Current?.applicationIcon ?? IconChoices.Image("d");
    internal IDesktopWorkspace Workspace { get; }
    internal Dictionary<Guid, FolderHeader> Headers { get; } = new();
    internal bool DesktopAvailable => WindowsDesktop.Host() != IntPtr.Zero
        && Headers.Values.All(h => WindowsDesktop.Attached(h.Handle, h.Host));
    internal string SessionStatus => (HotkeyRegistered ? "Ctrl+Alt+K 已启用。" : "Ctrl+Alt+K 注册失败，可能被占用；可从托盘创建。")
        + (DesktopAvailable ? "" : "\n桌面展示暂不可用，头部和展示部分等待恢复；内容及配置保留，可在设置中打开实际目录。");
    internal Window Controller { get; }
    internal bool HotkeyRegistered { get; }
    internal Forms.NotifyIcon Tray { get; }
    internal bool Exiting => exiting || disposed;
    private readonly HwndSource source;
    private readonly HwndSourceHook hook;
    private readonly DispatcherTimer refresh;
    private Icon trayIcon;
    private BitmapImage? applicationIcon;
    private string? iconChoice;
    private readonly Dictionary<string, Forms.ToolStripMenuItem> iconMenu = new();
    private StyleDialog? appearance;
    private SettingsWindow? settings;
    private bool refreshing;
    private bool creating;
    private bool exiting;
    private bool disposed;
    private bool sessionRefreshing;
    private bool taskbarRestarted;
    private bool? reportedAvailability;
    private bool displayRefreshPending;
    private DisplayArea[] displayEnvironment = WindowsDesktop.Displays();
    private LayoutInteraction? activeInteraction;
    private readonly DispatcherTimer layoutInput = new() { Interval = TimeSpan.FromMilliseconds(25) };
    internal bool Interacting => activeInteraction != null;
    internal bool ContentInputActive => Headers.Values.Any(header => header.Contents.InputActive);
    internal bool DraggingFiles { get; set; }
    internal bool Moving => moveDialog != null || DraggingFiles || Migrating;
    private RootMigrationDialog? rootMigration;
    internal bool Migrating => rootMigration != null;
    internal RootMigrationDialog? ActiveRootMigration => rootMigration;
    private MoveDialog? moveDialog;
    private FolderActionDialog? folderAction;
    internal FolderActionDialog? ActiveFolderAction => folderAction;
    internal bool ChangingFolder => folderAction != null;
    internal MoveDialog? ActiveMove => moveDialog;

    internal Runtime(IDesktopWorkspace workspace)
    {
        Current = this;
        layoutInput.Tick += (_, _) =>
        {
            // Explorer 子窗口不一定持有键盘焦点，Esc 仍须取消当前捕获会话。
            if (activeInteraction != null && (WindowsDesktop.GetAsyncKeyState(0x1B) & 0x8000) != 0)
            {
                foreach (var header in Headers.Values) header.CancelInteraction();
                activeInteraction = null;
                layoutInput.Stop();
                Render();
            }
        };
        Workspace = workspace;
        Controller = new Window { Width = 1, Height = 1, ShowInTaskbar = false, WindowStyle = WindowStyle.ToolWindow, Title = "Kage 桌面整理控制器" };
        var handle = new WindowInteropHelper(Controller).EnsureHandle();
        source = HwndSource.FromHwnd(handle);
        hook = (IntPtr hwnd, int message, IntPtr wp, IntPtr lp, ref bool handled) =>
        {
            if (message == 0x312 && wp.ToInt32() == 1) { _ = CreateAsync(); handled = true; }
            if (message == WindowsDesktop.TaskbarCreated)
            {
                taskbarRestarted = true;
                Dispatch(async () => await RefreshDesktopSessionAsync());
            }
            if (message is 0x7E or 0x1A or 0x2E0) RequestDisplayRefresh();
            return IntPtr.Zero;
        };
        source.AddHook(hook);
        // WPF 可在 HWND hook 之前消费 WM_SETTINGCHANGE；系统事件补足字体／间距变化。
        SystemEvents.DisplaySettingsChanged += SystemDisplayChanged;
        SystemEvents.UserPreferenceChanged += SystemPreferenceChanged;
        HotkeyRegistered = WindowsDesktop.RegisterHotKey(handle, 1, 0x4003, 0x4B);
        trayIcon = new Icon(Path.Combine(AppContext.BaseDirectory, "Assets", "tray-d.ico"));
        Tray = new Forms.NotifyIcon { Icon = trayIcon, Text = "Kage 桌面整理", Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("新建 Folder", null, (_, _) => Dispatch(async () => await CreateAsync()));
        menu.Items.Add("设置", null, (_, _) => Dispatch(ShowSettings));
        var icons = new Forms.ToolStripMenuItem("图标方案");
        foreach (var option in IconChoices.Options)
        {
            var item = new Forms.ToolStripMenuItem(option.Name);
            item.Click += (_, _) => Dispatch(async () => await SelectIconAsync(option.Key));
            icons.DropDownItems.Add(item);
            iconMenu.Add(option.Key, item);
        }
        menu.Items.Add(icons);
        menu.Items.Add("退出", null, (_, _) => Dispatch(async () => await ExitAsync()));
        Tray.ContextMenuStrip = menu;
        Tray.DoubleClick += (_, _) => Dispatch(ShowSettings);
        refresh = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        refresh.Tick += async (_, _) =>
        {
            await RefreshDesktopSessionAsync();
            await RefreshDisplayEnvironmentAsync();
        };
        refresh.Start();
        Render();
        _ = RefreshDesktopSessionAsync();
        if (!HotkeyRegistered) Balloon("Ctrl+Alt+K 注册失败，可能被其他程序占用；仍可从托盘创建 Folder。");
    }

    internal void Dispatch(Action action)
    {
        if (!disposed) Application.Current.Dispatcher.BeginInvoke(action);
    }

    private void RequestDisplayRefresh()
    {
        displayRefreshPending = true;
        NativeViewMetrics.Invalidate();
        ShellIcons.Invalidate();
        Dispatch(async () => await RefreshDisplayEnvironmentAsync());
    }

    private void SystemDisplayChanged(object? sender, EventArgs args) => Dispatch(RequestDisplayRefresh);
    private void SystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs args) => Dispatch(RequestDisplayRefresh);

    internal async Task RefreshDisplayEnvironmentAsync()
    {
        if (refreshing || Exiting || Moving || ChangingFolder || ContentInputActive) return;
        var displays = WindowsDesktop.Displays();
        var changed = displayRefreshPending || !displayEnvironment.SequenceEqual(displays);
        if (Interacting)
        {
            if (!changed) return;
            foreach (var header in Headers.Values) header.CancelInteraction();
            activeInteraction = null;
            layoutInput.Stop();
        }
        refreshing = true;
        var hidden = Workspace.Snapshot.Folders.Count(f => !f.Visible);
        try
        {
            if (changed) NativeViewMetrics.Invalidate();
            var result = await Workspace.RefreshAsync(displays);
            displayEnvironment = displays;
            displayRefreshPending = false;
            Render();
            if (!result.Succeeded) Balloon(result.Message);
            else if (Workspace.Snapshot.Folders.Count(f => !f.Visible) > hidden)
                Balloon("当前显示区域容纳不下部分 Folder，内容和记录保留；可在设置打开内容文件夹，释放空间后刷新展示。");
        }
        finally { refreshing = false; }
    }

    internal void Balloon(string message)
    {
        if (disposed) return;
        Tray.BalloonTipTitle = "Kage 桌面整理";
        Tray.BalloonTipText = message.Length > 240 ? message[..240] : message;
        Tray.ShowBalloonTip(5000);
    }

    internal void Render()
    {
        if (disposed || exiting || Interacting || ContentInputActive || DraggingFiles) return;
        var snapshot = Workspace.Snapshot;
        ApplyIcons(snapshot.IconChoice);
        foreach (var obsolete in Headers.Keys.Where(id => !snapshot.Folders.Any(folder => folder.Folder.Id == id)).ToArray())
        {
            Headers[obsolete].Close();
            Headers.Remove(obsolete);
        }
        foreach (var folder in snapshot.Folders)
        {
            // Explorer 退出可销毁跨进程子窗口；失效 HWND 必须重建 WPF 窗口。
            if (Headers.TryGetValue(folder.Folder.Id, out var lost) && !WindowsDesktop.IsWindow(lost.Handle))
            {
                lost.Close();
                Headers.Remove(folder.Folder.Id);
            }
            if (!Headers.TryGetValue(folder.Folder.Id, out var header))
            {
                header = new FolderHeader(folder);
                new WindowInteropHelper(header).EnsureHandle();
                Headers.Add(folder.Folder.Id, header);
            }
            header.Update(folder);
        }
        if (appearance != null && !appearance.Interaction.Closed) PreviewAppearance(appearance.Interaction);
        settings?.Refresh();
    }

    internal async Task RefreshDesktopSessionAsync()
    {
        if (Exiting || sessionRefreshing) return;
        sessionRefreshing = true;
        try
        {
            if (taskbarRestarted)
            {
                // 复用同一个 NotifyIcon 和菜单；控制器热键无需重新注册。
                Tray.Visible = false;
                Tray.Icon = trayIcon;
                Tray.Visible = true;
                taskbarRestarted = false;
            }
            // 不在文件移动、迁移或鼠标捕获期间重新挂接窗口。
            if (Interacting || Moving || ChangingFolder || refreshing || ContentInputActive) return;
            if (!DesktopAvailable || reportedAvailability != DesktopAvailable) Render();
            var available = DesktopAvailable;
            if (reportedAvailability == available) return;
            var previous = reportedAvailability;
            var result = await Workspace.ReportDesktopAvailabilityAsync(available);
            if (Exiting) return;
            reportedAvailability = available;
            settings?.Refresh();
            if (!available || previous == false) Balloon(result.Message);
        }
        finally { sessionRefreshing = false; }
    }

    private void ApplyIcons(string key)
    {
        if (iconChoice == key) return;
        var image = IconChoices.Image(key);
        var next = new Icon(Path.Combine(AppContext.BaseDirectory, "Assets", $"tray-{key}.ico"));
        Tray.Icon = next;
        trayIcon.Dispose();
        trayIcon = next;
        applicationIcon = image;
        iconChoice = key;
        foreach (Window window in Application.Current.Windows) window.Icon = image;
        foreach (var option in iconMenu) option.Value.Checked = option.Key == key;
    }

    internal async Task<OperationResult> SelectIconAsync(string key)
    {
        if (Exiting) return new(Outcome.Cancelled, "程序正在退出。");
        try
        {
            // 保存前核对包内资源可加载，失败保留当前显示及偏好。
            _ = IconChoices.Image(key);
            using var available = new Icon(Path.Combine(AppContext.BaseDirectory, "Assets", $"tray-{key}.ico"));
            var result = await Workspace.SetIconAsync(key);
            Render();
            if (!result.Succeeded) Balloon(result.Message);
            return result;
        }
        catch (Exception e)
        {
            var result = new OperationResult(Outcome.Failed, $"图标无法加载，原方案保留：{e.Message}");
            Balloon(result.Message);
            return result;
        }
    }

    internal void ShowAppearance(Guid id)
    {
        var dialog = CreateAppearance(id);
        if (dialog == null) return;
        if (dialog.IsVisible) { dialog.Activate(); return; }
        dialog.ShowDialog();
    }

    internal StyleDialog? CreateAppearance(Guid id)
    {
        if (Exiting || Interacting || Moving || ChangingFolder) return null;
        if (appearance != null) return appearance;
        var interaction = Workspace.BeginAppearance(id);
        if (interaction == null) { Balloon("工作区正在恢复或 Folder 不存在，暂不能修改外观。"); return null; }
        var name = Workspace.Snapshot.Folders.Single(folder => folder.Folder.Id == id).Folder.Name;
        appearance = new StyleDialog(this, interaction, name);
        return appearance;
    }

    internal void PreviewAppearance(AppearanceInteraction interaction)
    {
        if (Headers.TryGetValue(interaction.FolderId, out var header)) header.ApplyStyle(interaction.Color, interaction.Opacity);
    }

    internal void EndAppearance(StyleDialog dialog)
    {
        if (appearance == dialog) appearance = null;
        Render();
    }

    internal void ShowSettings()
    {
        if (exiting || disposed) return;
        if (settings == null)
        {
            settings = new SettingsWindow(this);
            settings.Closed += (_, _) => settings = null;
        }
        settings.Show();
        if (settings.WindowState == WindowState.Minimized) settings.WindowState = WindowState.Normal;
        settings.Activate();
    }

    internal LayoutInteraction? BeginInteraction(Guid id)
    {
        if (Interacting || Exiting || Moving || ChangingFolder || refreshing || displayRefreshPending) return null;
        activeInteraction = Workspace.BeginLayout(id);
        if (activeInteraction != null) layoutInput.Start();
        return activeInteraction;
    }

    internal void Preview(LayoutInteraction interaction)
    {
        foreach (var folder in interaction.Folders)
            if (Headers.TryGetValue(folder.Folder.Id, out var header) && header.Record != folder.Folder) header.ApplyGeometry(folder, preview: true);
    }

    internal async Task CommitInteractionAsync(LayoutInteraction interaction)
    {
        layoutInput.Stop();
        try
        {
            var result = await Workspace.CommitLayoutAsync(interaction);
            if (!result.Succeeded) Balloon(result.Message);
        }
        finally { activeInteraction = null; layoutInput.Stop(); Render(); }
    }

    internal async Task CreateAsync()
    {
        if (creating || exiting || disposed || ChangingFolder || Migrating) return;
        creating = true;
        try
        {
            var name = AskName();
            if (name == null || exiting) return;
            var result = await Workspace.CreateFolderAsync(name);
            if (result.Outcome == Outcome.Conflict)
            {
                var choice = MessageBox.Show("目标已存在。\n是：保留两份并自动编号\n否：跳过\n取消：取消创建", "同名冲突", MessageBoxButton.YesNoCancel);
                result = await Workspace.CreateFolderAsync(name, choice switch
                {
                    MessageBoxResult.Yes => ConflictChoice.KeepBoth,
                    MessageBoxResult.No => ConflictChoice.Skip,
                    _ => ConflictChoice.Cancel
                });
            }
            Render();
            if (Exiting) return;
            if (result.Outcome is Outcome.Failed or Outcome.RecoveryRequired) { MessageBox.Show(result.Message, "创建 Folder"); ShowSettings(); }
            else if (result.Outcome == Outcome.Success) Balloon(result.Message);
        }
        finally { creating = false; }
    }

    private static string? AskName()
    {
        var dialog = new Window { Title = "新建 Folder", Width = 400, Height = 180, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterScreen, Icon = ApplicationIcon };
        var body = new StackPanel { Margin = new Thickness(20) };
        var name = new TextBox { Text = "新建文件夹", Margin = new Thickness(0, 10, 0, 12) };
        var confirm = new Button { Content = "创建", Width = 90, IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right };
        confirm.Click += (_, _) => dialog.DialogResult = true;
        body.Children.Add(new TextBlock { Text = "内容文件夹名称" });
        body.Children.Add(name);
        body.Children.Add(confirm);
        dialog.Content = body;
        dialog.Loaded += (_, _) => { name.Focus(); name.SelectAll(); };
        // 原样校验名称，不通过修剪把非法的末尾空格伪装为有效输入。
        return dialog.ShowDialog() == true ? name.Text : null;
    }

    internal async Task<BatchMoveResult?> MoveFilesAsync(string[] paths, MoveTarget target, bool cancelled = false, string? targetError = null)
    {
        if (Exiting || Moving || ChangingFolder) return null;
        moveDialog = new MoveDialog();
        var dialog = moveDialog;
        dialog.Show();
        if (cancelled) dialog.Cancel();
        if (targetError != null) Balloon(targetError);
        try { return await dialog.MoveAsync(paths, target, targetError); }
        finally { moveDialog = null; Render(); }
    }

    internal async Task ExitAsync()
    {
        if (Migrating) { rootMigration?.Cancel(); Balloon("正在停止根目录迁移并恢复，请等待结果后再退出。"); return; }
        if (ChangingFolder) { Balloon("请先完成或关闭 Folder 操作窗口再退出。"); folderAction?.Activate(); return; }
        if (Moving) { moveDialog?.Cancel(); Balloon("正在结束文件移动，请等待逐项结果后再退出。"); return; }
        if (exiting) return;
        exiting = true;
        refresh.Stop();
        if (activeInteraction != null) await CommitInteractionAsync(activeInteraction);
        var result = await Workspace.RefreshAsync(WindowsDesktop.Displays());
        if (result.Outcome == Outcome.Failed) MessageBox.Show($"退出前状态提交失败：{result.Message}。原记录及内容均保留。", "退出 Kage");
        Dispose();
        Application.Current.Shutdown();
    }

    internal async Task<OperationResult?> MigrateRootAsync(string target)
        => await RunRootMigrationAsync(target);

    internal async Task<OperationResult?> RecoverRootMigrationAsync()
        => await RunRootMigrationAsync(null);

    private async Task<OperationResult?> RunRootMigrationAsync(string? target)
    {
        if (Exiting || Moving || ChangingFolder || Interacting || creating || appearance != null)
        {
            Balloon("请先结束当前操作，再迁移或恢复存储根目录。");
            return null;
        }
        var dialog = rootMigration = new RootMigrationDialog(recovering: target == null);
        dialog.Show();
        try { return target == null ? await dialog.StartRecoveryAsync(Workspace) : await dialog.StartAsync(Workspace, target); }
        finally { rootMigration = null; Render(); }
    }

    internal static void Open(string path)
    {
        try
        {
            var start = Directory.Exists(path)
                ? new ProcessStartInfo("explorer.exe") { UseShellExecute = false, ArgumentList = { path } }
                : new ProcessStartInfo(path) { UseShellExecute = true };
            Process.Start(start);
        }
        catch (Exception e) { MessageBox.Show($"无法打开 {path}\n{e.Message}", "Kage 桌面整理"); }
    }

    internal void ShowFolderAction(Guid id, bool rename)
    {
        if (Exiting || Interacting || Moving || creating || appearance != null) return;
        if (folderAction != null) { folderAction.Activate(); return; }
        var folder = Workspace.Snapshot.Folders.FirstOrDefault(item => item.Folder.Id == id);
        if (folder == null) return;
        folderAction = new FolderActionDialog(this, folder, rename);
        folderAction.Closed += (_, _) => { folderAction = null; Render(); };
        folderAction.Show();
        folderAction.Activate();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        layoutInput.Stop();
        ShellContextMenu.CancelPending();
        ShellContextMenu.InvalidatePrepared();
        refresh.Stop();
        source.RemoveHook(hook);
        SystemEvents.DisplaySettingsChanged -= SystemDisplayChanged;
        SystemEvents.UserPreferenceChanged -= SystemPreferenceChanged;
        if (HotkeyRegistered) WindowsDesktop.UnregisterHotKey(new WindowInteropHelper(Controller).Handle, 1);
        settings?.Close();
        appearance?.Close();
        folderAction?.Close();
        foreach (var header in Headers.Values) header.Close();
        Headers.Clear();
        Tray.Visible = false;
        var menu = Tray.ContextMenuStrip;
        Tray.Dispose();
        menu?.Dispose();
        trayIcon.Dispose();
        Controller.Close();
    }
}
