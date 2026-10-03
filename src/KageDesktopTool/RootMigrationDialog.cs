using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Kage.Workspace;

namespace Kage.Desktop;

internal sealed class RootMigrationDialog : Window
{
    private readonly CancellationTokenSource cancellation = new();
    private readonly TextBlock status = new() { Text = "正在验证目录与迁移清单…", TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar progress = new() { Height = 18, Margin = new Thickness(0, 12, 0, 12) };
    private readonly TextBox details = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    internal Button CancelButton { get; } = new() { Content = "取消迁移并恢复", Padding = new Thickness(12, 6, 12, 6), HorizontalAlignment = HorizontalAlignment.Right };
    internal Task<OperationResult>? Pending { get; private set; }
    internal OperationResult? Result { get; private set; }
    internal string StatusText => status.Text;
    private bool finished;

    internal RootMigrationDialog()
    {
        Title = "更换存储根目录";
        Width = 720;
        Height = 460;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = Runtime.ApplicationIcon;
        var body = new DockPanel { Margin = new Thickness(18) };
        var summary = new StackPanel();
        summary.Children.Add(status);
        summary.Children.Add(progress);
        DockPanel.SetDock(summary, Dock.Top);
        body.Children.Add(summary);
        DockPanel.SetDock(CancelButton, Dock.Bottom);
        CancelButton.Margin = new Thickness(0, 12, 0, 0);
        body.Children.Add(CancelButton);
        body.Children.Add(details);
        Content = body;
        CancelButton.Click += (_, _) => { if (finished) Close(); else Cancel(); };
        Closing += (_, e) => { if (!finished) { Cancel(); e.Cancel = true; } };
        Closed += (_, _) => cancellation.Dispose();
    }

    internal void Cancel()
    {
        if (finished) return;
        cancellation.Cancel();
        status.Text = "已请求取消，正在停止迁移并尝试恢复原内容及快捷方式。请等待结果。";
        CancelButton.IsEnabled = false;
    }

    internal Task<OperationResult> StartAsync(IDesktopWorkspace workspace, string target)
        => Pending = ExecuteAsync(workspace, target);

    private async Task<OperationResult> ExecuteAsync(IDesktopWorkspace workspace, string target)
    {
        try
        {
            Result = await workspace.MigrateRootAsync(target, new Progress<RootMigrationProgress>(update =>
            {
                if (finished) return;
                progress.Maximum = Math.Max(1, update.Total);
                progress.Value = update.Completed;
                status.Text = $"{update.Completed} / {update.Total}：{update.Message}";
                if (update.Item != null)
                {
                    details.AppendText($"{update.Item.SourcePath}\n→ {update.Item.DestinationPath}\n{update.Message}\n\n");
                    details.ScrollToEnd();
                }
            }), cancellation.Token);
            status.Text = Result.Message;
            details.AppendText("\n最终结果：" + Result.Outcome + "\n" + string.Join("\n", workspace.Snapshot.Notices));
            return Result;
        }
        finally
        {
            finished = true;
            CancelButton.Content = "关闭";
            CancelButton.IsEnabled = true;
        }
    }
}
