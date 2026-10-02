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

namespace Kage.Desktop;

internal sealed class Runtime : IDisposable
{
    internal static Runtime Current { get; private set; } = null!;
    internal static BitmapImage ApplicationIcon => new(new Uri("pack://application:,,,/Assets/app-d.png"));
    internal IDesktopWorkspace Workspace { get; }
    internal Dictionary<Guid, FolderHeader> Headers { get; } = new();
    internal bool DesktopAvailable => Headers.Values.All(h => h.Host != IntPtr.Zero);
    internal string SessionStatus => (HotkeyRegistered ? "Ctrl+Alt+K 已启用。" : "Ctrl+Alt+K 注册失败，可能被占用；可从托盘创建。")
        + (DesktopAvailable ? "" : "\n桌面宿主不可用，暂不展示头部；可以在设置中打开实际内容目录。");
    internal Window Controller { get; }
    internal bool HotkeyRegistered { get; }
    internal Forms.NotifyIcon Tray { get; }
    internal bool Exiting => exiting || disposed;
    private readonly HwndSource source;
    private readonly HwndSourceHook hook;
    private readonly DispatcherTimer refresh;
    private readonly Icon trayIcon;
    private SettingsWindow? settings;
    private bool refreshing;
    private bool creating;
    private bool exiting;
    private bool disposed;

    internal Runtime(IDesktopWorkspace workspace)
    {
        Current = this;
        Workspace = workspace;
        Controller = new Window { Width = 1, Height = 1, ShowInTaskbar = false, WindowStyle = WindowStyle.ToolWindow, Title = "Kage 桌面整理控制器" };
        var handle = new WindowInteropHelper(Controller).EnsureHandle();
        source = HwndSource.FromHwnd(handle);
        hook = (IntPtr hwnd, int message, IntPtr wp, IntPtr lp, ref bool handled) =>
        {
            if (message == 0x312 && wp.ToInt32() == 1) { _ = CreateAsync(); handled = true; }
            return IntPtr.Zero;
        };
        source.AddHook(hook);
        HotkeyRegistered = WindowsDesktop.RegisterHotKey(handle, 1, 0x4003, 0x4B);
        trayIcon = new Icon(Path.Combine(AppContext.BaseDirectory, "Assets", "tray-d.ico"));
        Tray = new Forms.NotifyIcon { Icon = trayIcon, Text = "Kage 桌面整理", Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("新建 Folder", null, (_, _) => Dispatch(async () => await CreateAsync()));
        menu.Items.Add("设置", null, (_, _) => Dispatch(ShowSettings));
        menu.Items.Add("退出", null, (_, _) => Dispatch(async () => await ExitAsync()));
        Tray.ContextMenuStrip = menu;
        Tray.DoubleClick += (_, _) => Dispatch(ShowSettings);
        refresh = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        refresh.Tick += async (_, _) =>
        {
            if (refreshing || exiting) return;
            refreshing = true;
            try
            {
                var result = await Workspace.RefreshAsync(WindowsDesktop.Displays());
                Render();
                if (result.Outcome == Outcome.Failed) Balloon(result.Message);
            }
            finally { refreshing = false; }
        };
        refresh.Start();
        Render();
        if (!HotkeyRegistered) Balloon("Ctrl+Alt+K 注册失败，可能被其他程序占用；仍可从托盘创建 Folder。");
    }

    internal void Dispatch(Action action)
    {
        if (!disposed) Application.Current.Dispatcher.BeginInvoke(action);
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
        if (disposed || exiting) return;
        var snapshot = Workspace.Snapshot;
        foreach (var obsolete in Headers.Keys.Where(id => !snapshot.Folders.Any(folder => folder.Folder.Id == id)).ToArray())
        {
            Headers[obsolete].Close();
            Headers.Remove(obsolete);
        }
        foreach (var folder in snapshot.Folders)
        {
            if (!Headers.TryGetValue(folder.Folder.Id, out var header))
            {
                header = new FolderHeader(folder);
                new WindowInteropHelper(header).EnsureHandle();
                Headers.Add(folder.Folder.Id, header);
            }
            header.Update(folder);
        }
        settings?.Refresh();
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

    internal async Task CreateAsync()
    {
        if (creating || exiting || disposed) return;
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

    internal async Task ExitAsync()
    {
        if (exiting) return;
        exiting = true;
        refresh.Stop();
        var result = await Workspace.RefreshAsync(WindowsDesktop.Displays());
        if (result.Outcome == Outcome.Failed) MessageBox.Show($"退出前状态提交失败：{result.Message}。原记录及内容均保留。", "退出 Kage");
        Dispose();
        Application.Current.Shutdown();
    }

    internal static void Open(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception e) { MessageBox.Show($"无法打开 {path}\n{e.Message}", "Kage 桌面整理"); }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        refresh.Stop();
        source.RemoveHook(hook);
        if (HotkeyRegistered) WindowsDesktop.UnregisterHotKey(new WindowInteropHelper(Controller).Handle, 1);
        settings?.Close();
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
