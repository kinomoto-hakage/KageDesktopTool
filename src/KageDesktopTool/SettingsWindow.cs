using System;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Kage.Workspace;

namespace Kage.Desktop;

internal sealed class SettingsWindow : Window
{
    private readonly Runtime runtime;
    private readonly TextBox root = new() { Margin = new Thickness(0, 8, 0, 8) };
    private readonly CheckBox startup = new() { Content = "登录 Windows 后自动运行", Margin = new Thickness(0, 18, 0, 8) };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 15, 0, 10) };
    private readonly StackPanel folders = new();
    private readonly StackPanel recovery = new();
    private readonly WrapPanel rootActions = new();
    private readonly StackPanel controls = new() { Margin = new Thickness(22) };
    private readonly Dictionary<string, StackPanel> sections = new();
    private readonly ListBox navigation = new() { BorderThickness = new Thickness(0), Background = Brushes.Transparent };
    private readonly CheckBox notifications = new() { Content = "接收操作结果消息提示", Margin = new Thickness(0, 12, 0, 12) };
    private readonly StackPanel results = new();
    private readonly TextBlock notificationStatus = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
    private Guid? selectedResult;
    private OperationDetails[] renderedResults = [];
    private string? renderedNotice;
    private Guid? renderedSelection;
    private bool resultsRendered;
    private bool resultsRequested;
    private bool rootEdited;
    private bool startupEdited;
    private bool notificationsEdited;
    private bool refreshingUi;
    internal TextBox RootInput => root;
    internal Button MigrateButton { get; }
    internal Button RecoverMigrationButton { get; }
    internal StackPanel RecoveryRows => recovery;
    internal System.Threading.Tasks.Task<OperationResult?>? PendingRecovery { get; private set; }
    internal System.Threading.Tasks.Task<Kage.Workspace.OperationResult?>? PendingMigration { get; private set; }

    internal SettingsWindow(Runtime runtime)
    {
        this.runtime = runtime;
        Title = "Kage 桌面工具 · 设置";
        Width = 840;
        Height = 650;
        MinWidth = 620;
        MinHeight = 350;
        Icon = Runtime.ApplicationIcon;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(250, 250, 252));
        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(156) });
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        var sidebar = new Border { Background = new SolidColorBrush(Color.FromRgb(239, 241, 245)), Padding = new Thickness(12, 20, 12, 20), Child = navigation };
        layout.Children.Add(sidebar);
        var scroll = new ScrollViewer { Content = controls, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetColumn(scroll, 1);
        layout.Children.Add(scroll);
        Content = layout;
        foreach (var title in new[] { "通用", "桌面 Folder", "存储", "通知", "恢复与状态", "关于" })
        {
            var section = new StackPanel { Visibility = Visibility.Collapsed };
            section.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 16) });
            section.Children.Add(new Separator { Margin = new Thickness(0, 0, 0, 18) });
            sections.Add(title, section);
            controls.Children.Add(section);
            navigation.Items.Add(new ListBoxItem { Content = title, Padding = new Thickness(10, 12, 10, 12) });
        }
        navigation.SelectionChanged += (_, _) =>
        {
            foreach (var pair in sections) pair.Value.Visibility = ((ListBoxItem?)navigation.SelectedItem)?.Content as string == pair.Key ? Visibility.Visible : Visibility.Collapsed;
        };
        var storage = sections["存储"];
        var general = sections["通用"];
        var folderSection = sections["桌面 Folder"];
        var recoverySection = sections["恢复与状态"];
        storage.Children.Add(new TextBlock { Text = "存储根目录", FontWeight = FontWeights.SemiBold });
        storage.Children.Add(root);
        root.TextChanged += (_, _) => { if (!refreshingUi) rootEdited = true; };
        startup.Checked += (_, _) => { if (!refreshingUi) startupEdited = true; };
        startup.Unchecked += (_, _) => { if (!refreshingUi) startupEdited = true; };
        notifications.Checked += (_, _) => { if (!refreshingUi) notificationsEdited = true; };
        notifications.Unchecked += (_, _) => { if (!refreshingUi) notificationsEdited = true; };
        AddButton(rootActions, "选择目录…", () =>
        {
            using var picker = new System.Windows.Forms.FolderBrowserDialog { Description = "选择集中存放内容文件夹的目录", UseDescriptionForTitle = true };
            if (picker.ShowDialog() == System.Windows.Forms.DialogResult.OK) root.Text = picker.SelectedPath;
        });
        MigrateButton = AddButton(rootActions, "迁移并保存目录", async () =>
        {
            controls.IsEnabled = false;
            try
            {
                PendingMigration = runtime.MigrateRootAsync(root.Text);
                var result = await PendingMigration;
                if (result?.Succeeded == true) rootEdited = false;
            }
            finally { controls.IsEnabled = true; Refresh(); }
        });
        AddButton(rootActions, "离线时另选新建目录", async () =>
        {
            await Apply(() => runtime.Workspace.SelectRootAsync(root.Text));
            rootEdited = false;
            Refresh();
        });
        storage.Children.Add(rootActions);
        storage.Children.Add(new TextBlock { Text = "更换目录会迁移全部活动及保留内容，并更新工具生成的快捷方式；取消时尝试恢复原位置。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
        general.Children.Add(startup);
        AddButton(general, "应用自启选择", async () => { await Apply(() => runtime.Workspace.SetStartupAsync(startup.IsChecked == true)); startupEdited = false; Refresh(); });
        var actions = new WrapPanel { Margin = new Thickness(0, 15, 0, 12) };
        AddButton(actions, "新建 Folder", async () => await runtime.CreateAsync());
        AddButton(actions, "刷新展示", async () => await runtime.RefreshDisplayEnvironmentAsync(report: true));
        AddButton(recoverySection, "恢复状态备份", async () =>
        {
            if (MessageBox.Show("使用最近有效备份恢复？损坏的状态文件会另存保留。", "恢复工作区", MessageBoxButton.OKCancel) == MessageBoxResult.OK)
                await Apply(runtime.Workspace.RestoreBackupAsync);
        });
        folderSection.Children.Add(actions);
        AddButton(recoverySection, "关联待核对的创建目录…", async () =>
        {
            var explanation = string.Join("\n", runtime.Workspace.Snapshot.Notices);
            if (MessageBox.Show($"{explanation}\n\n确认该记录对应的目录就是希望管理的内容文件夹，并关联它？现有内容会保留。", "核对实际目录", MessageBoxButton.OKCancel) == MessageBoxResult.OK)
                await Apply(runtime.Workspace.ConfirmPendingCreateAsync);
        });
        recoverySection.Children.Add(status);
        RecoverMigrationButton = AddButton(recoverySection, "重试恢复旧位置及快捷方式", async () =>
        {
            controls.IsEnabled = false;
            try
            {
                PendingRecovery = runtime.RecoverRootMigrationAsync();
                var result = await PendingRecovery;
                if (result?.Succeeded == true) rootEdited = false;
            }
            finally { controls.IsEnabled = true; Refresh(); }
        });
        recoverySection.Children.Add(recovery);
        folderSection.Children.Add(folders);
        var notificationSection = sections["通知"];
        notificationSection.Children.Add(notifications);
        AddButton(notificationSection, "保存消息提示选择", async () =>
        {
            await Apply(() => runtime.Workspace.SetNotificationsAsync(notifications.IsChecked == true));
            notificationsEdited = false;
            Refresh();
        });
        notificationSection.Children.Add(new TextBlock { Text = "关闭提示后，操作详情与必要恢复状态仍会保留。Windows 通知设置决定系统是否显示提示。最近 100 条结果保存在本机。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) });
        notificationSection.Children.Add(notificationStatus);
        notificationSection.Children.Add(results);
        sections["关于"].Children.Add(new TextBlock { Text = "Kage 桌面工具 " + typeof(Program).Assembly.GetName().Version?.ToString(3), FontSize = 18, FontWeight = FontWeights.SemiBold });
        sections["关于"].Children.Add(new TextBlock { Text = "关闭设置后继续在后台运行。请从托盘“退出”结束程序，内容文件夹会保留。",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 20, 0, 0) });
        navigation.SelectedIndex = runtime.Workspace.Snapshot.RecoveryRequired ? 4 : 0;
        Refresh();
    }

    private async System.Threading.Tasks.Task Apply(Func<System.Threading.Tasks.Task<Kage.Workspace.OperationResult>> operation)
    {
        controls.IsEnabled = false;
        try
        {
            var result = await operation();
            runtime.Render();
            if (!runtime.Exiting) runtime.Complete("保存设置", result);
        }
        finally { controls.IsEnabled = true; Refresh(); }
    }

    internal void Refresh()
    {
        var snapshot = runtime.Workspace.Snapshot;
        root.IsEnabled = !snapshot.RecoveryRequired;
        rootActions.IsEnabled = !snapshot.RecoveryRequired;
        MigrateButton.IsEnabled = !snapshot.RecoveryRequired;
        RecoverMigrationButton.Visibility = snapshot.RootMigration == null ? Visibility.Collapsed : Visibility.Visible;
        refreshingUi = true;
        if (!rootEdited) root.Text = snapshot.Root;
        if (!startupEdited) startup.IsChecked = snapshot.StartupEnabled;
        if (!notificationsEdited) notifications.IsChecked = snapshot.NotificationsEnabled;
        notifications.IsEnabled = !snapshot.RecoveryRequired;
        refreshingUi = false;
        status.Text = string.Join("\n", new[] { runtime.SessionStatus }.Concat(snapshot.Notices));
        recovery.Children.Clear();
        if (snapshot.RootMigration != null)
        {
            recovery.Children.Add(new TextBlock { Text = $"未恢复项目：{snapshot.MigrationRecovery.Count(item => !item.Restored)} / {snapshot.MigrationRecovery.Count}。全部内容及链接核对完成、配置保存成功后解除恢复状态。",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 8) });
            foreach (var observed in snapshot.MigrationRecovery)
            {
                var item = observed.Item;
                var row = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
                row.Children.Add(new TextBlock { Text = $"{System.IO.Path.GetFileName(item.SourcePath)} · {(observed.Restored ? "原位置已核对，等待提交" : "未恢复")}", FontWeight = FontWeights.SemiBold });
                row.Children.Add(new TextBlock { Text = $"原位置：{item.SourcePath}（{PathStatus(observed.SourceStatus)}）\n迁移位置：{item.DestinationPath}（{PathStatus(observed.DestinationStatus)}）"
                    + (item.ShortcutPath == null ? "" : $"\n快捷方式：{item.ShortcutPath}\n实际目标：{observed.ShortcutTarget ?? "缺失或无法核对"}")
                    + $"\n{observed.Notice ?? item.Error}", TextWrapping = TextWrapping.Wrap });
                if (observed.SourceStatus is MigrationPathStatus.Owned or MigrationPathStatus.Unverified)
                    AddButton(row, "打开原位置", () => Runtime.Open(item.SourcePath));
                if (observed.DestinationStatus is MigrationPathStatus.Owned or MigrationPathStatus.Unverified)
                    AddButton(row, "打开迁移位置", () => Runtime.Open(item.DestinationPath));
                recovery.Children.Add(row);
            }
        }
        folders.Children.Clear();
        foreach (var folder in snapshot.Folders)
        {
            var row = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            row.Children.Add(new TextBlock { Text = $"{folder.Folder.Name} · {(folder.Visible && runtime.DesktopAvailable ? "已展示" : "暂未展示")}", FontWeight = FontWeights.SemiBold });
            row.Children.Add(new TextBlock { Text = folder.ActualPath + (folder.Notice == null ? "" : "\n" + folder.Notice), TextWrapping = TextWrapping.Wrap });
            AddButton(row, "打开内容文件夹", () => Runtime.Open(folder.ActualPath));
            AddButton(row, "样式…", () => runtime.ShowAppearance(folder.Folder.Id));
            AddButton(row, "重命名…", () => runtime.ShowFolderAction(folder.Folder.Id, true));
            AddButton(row, "删除…", () => runtime.ShowFolderAction(folder.Folder.Id, false));
            folders.Children.Add(row);
        }
        RefreshResults();
    }

    internal void SelectResults(Guid? id)
    {
        selectedResult = id;
        resultsRequested = true;
        navigation.SelectedIndex = 3;
        RefreshResults();
    }

    private void RefreshResults()
    {
        var entries = runtime.Feedback.Entries;
        if (!resultsRequested && resultsRendered && renderedResults.SequenceEqual(entries) && renderedNotice == runtime.Feedback.StorageNotice
            && renderedSelection == selectedResult) return;
        var selectionChanged = resultsRequested || !resultsRendered || renderedSelection != selectedResult;
        resultsRequested = false;
        renderedResults = entries.ToArray();
        renderedNotice = runtime.Feedback.StorageNotice;
        renderedSelection = selectedResult;
        resultsRendered = true;
        notificationStatus.Text = runtime.Feedback.StorageNotice ?? (selectedResult != null && !runtime.Feedback.Entries.Any(entry => entry.Id == selectedResult)
            ? "该通知的结果已超过历史保留范围。可在恢复与状态查看当前状态。" : "操作结果详情");
        var expanded = results.Children.OfType<Expander>().Where(row => row.IsExpanded).Select(row => row.Tag).ToArray();
        results.Children.Clear();
        foreach (var entry in runtime.Feedback.Entries)
        {
            var text = new TextBox { Text = entry.Summary + "\n\n" + entry.Details + "\n\n" + entry.Delivery, IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
                BorderThickness = new Thickness(0), Background = Brushes.Transparent, Margin = new Thickness(0, 8, 0, 8) };
            var row = new Expander { Header = $"{entry.Time:MM-dd HH:mm:ss} · {entry.Title}", Content = text,
                Tag = entry.Id, IsExpanded = selectionChanged && selectedResult == entry.Id || expanded.Contains(entry.Id), Margin = new Thickness(0, 0, 0, 10) };
            results.Children.Add(row);
            if (selectionChanged && selectedResult == entry.Id) Dispatcher.BeginInvoke(new Action(() => row.BringIntoView()));
        }
        if (selectionChanged && selectedResult != null && !entries.Any(entry => entry.Id == selectedResult))
            Dispatcher.BeginInvoke(new Action(() => notificationStatus.BringIntoView()));
    }

    private static string PathStatus(MigrationPathStatus status) => status switch
    {
        MigrationPathStatus.Missing => "不存在",
        MigrationPathStatus.Owned => "真实目录，归属已确认",
        MigrationPathStatus.Unverified => "存在，归属未确认",
        _ => "无法核对"
    };

    private static Button AddButton(Panel parent, string text, Action action)
    {
        var button = new Button { Content = text, Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 0, 8, 8), HorizontalAlignment = HorizontalAlignment.Left };
        button.Click += (_, _) => action();
        parent.Children.Add(button);
        return button;
    }
}
