using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Kage.Desktop;

internal sealed class SettingsWindow : Window
{
    private readonly Runtime runtime;
    private readonly TextBox root = new() { Margin = new Thickness(0, 8, 0, 8) };
    private readonly CheckBox startup = new() { Content = "登录 Windows 后自动运行", Margin = new Thickness(0, 18, 0, 8) };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 15, 0, 10) };
    private readonly StackPanel folders = new();
    private readonly StackPanel controls = new() { Margin = new Thickness(22) };
    private bool rootEdited;
    private bool startupEdited;
    private bool refreshingUi;

    internal SettingsWindow(Runtime runtime)
    {
        this.runtime = runtime;
        Title = "Kage 桌面整理 · 设置";
        Width = 590;
        Height = 580;
        MinWidth = 460;
        MinHeight = 350;
        Icon = Runtime.ApplicationIcon;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Content = new ScrollViewer { Content = controls, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        controls.Children.Add(new TextBlock { Text = "存储根目录", FontWeight = FontWeights.SemiBold });
        controls.Children.Add(root);
        root.TextChanged += (_, _) => { if (!refreshingUi) rootEdited = true; };
        startup.Checked += (_, _) => { if (!refreshingUi) startupEdited = true; };
        startup.Unchecked += (_, _) => { if (!refreshingUi) startupEdited = true; };
        var rootActions = new StackPanel { Orientation = Orientation.Horizontal };
        AddButton(rootActions, "选择目录…", () =>
        {
            using var picker = new System.Windows.Forms.FolderBrowserDialog { Description = "选择集中存放内容文件夹的目录", UseDescriptionForTitle = true };
            if (picker.ShowDialog() == System.Windows.Forms.DialogResult.OK) root.Text = picker.SelectedPath;
        });
        AddButton(rootActions, "保存目录", async () => { await Apply(() => runtime.Workspace.SelectRootAsync(root.Text)); rootEdited = false; Refresh(); });
        controls.Children.Add(rootActions);
        controls.Children.Add(startup);
        AddButton(controls, "应用自启选择", async () => { await Apply(() => runtime.Workspace.SetStartupAsync(startup.IsChecked == true)); startupEdited = false; Refresh(); });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 15, 0, 0) };
        AddButton(actions, "新建 Folder", async () => await runtime.CreateAsync());
        AddButton(actions, "刷新展示", async () => await Apply(() => runtime.Workspace.RefreshAsync(WindowsDesktop.Displays())));
        AddButton(actions, "恢复状态备份", async () =>
        {
            if (MessageBox.Show("使用最近有效备份恢复？损坏的状态文件会另存保留。", "恢复工作区", MessageBoxButton.OKCancel) == MessageBoxResult.OK)
                await Apply(runtime.Workspace.RestoreBackupAsync);
        });
        controls.Children.Add(actions);
        controls.Children.Add(status);
        controls.Children.Add(new TextBlock { Text = "桌面 Folder", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 8) });
        controls.Children.Add(folders);
        controls.Children.Add(new TextBlock { Text = "关闭设置后继续在后台运行。请从托盘“退出”结束程序，内容文件夹会保留。",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 20, 0, 0) });
        Refresh();
    }

    private async System.Threading.Tasks.Task Apply(Func<System.Threading.Tasks.Task<Kage.Workspace.OperationResult>> operation)
    {
        controls.IsEnabled = false;
        try
        {
            var result = await operation();
            runtime.Render();
            if (!runtime.Exiting) MessageBox.Show(this, result.Message, "Kage 桌面整理");
        }
        finally { controls.IsEnabled = true; Refresh(); }
    }

    internal void Refresh()
    {
        var snapshot = runtime.Workspace.Snapshot;
        refreshingUi = true;
        if (!rootEdited) root.Text = snapshot.Root;
        if (!startupEdited) startup.IsChecked = snapshot.StartupEnabled;
        refreshingUi = false;
        status.Text = string.Join("\n", new[] { runtime.SessionStatus }.Concat(snapshot.Notices));
        folders.Children.Clear();
        foreach (var folder in snapshot.Folders)
        {
            var row = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            row.Children.Add(new TextBlock { Text = $"{folder.Folder.Name} · {(folder.Visible && runtime.DesktopAvailable ? "已展示" : "暂未展示")}", FontWeight = FontWeights.SemiBold });
            row.Children.Add(new TextBlock { Text = folder.Notice ?? folder.ActualPath, TextWrapping = TextWrapping.Wrap });
            AddButton(row, "打开内容文件夹", () => Runtime.Open(folder.ActualPath));
            folders.Children.Add(row);
        }
    }

    private static void AddButton(Panel parent, string text, Action action)
    {
        var button = new Button { Content = text, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 8, 0), HorizontalAlignment = HorizontalAlignment.Left };
        button.Click += (_, _) => action();
        parent.Children.Add(button);
    }
}
