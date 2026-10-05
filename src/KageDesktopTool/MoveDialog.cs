using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Kage.Workspace;

namespace Kage.Desktop;

// 操作期间保留取消与冲突询问；结束后详情进入设置并自动关闭进度。
internal sealed class MoveDialog : Window
{
    private readonly CancellationTokenSource cancellation = new();
    private readonly TextBox results;
    private readonly TextBlock status;
    private readonly Button cancel;
    private bool finished;
    internal BatchMoveResult? Result { get; private set; }

    internal MoveDialog()
    {
        Title = "文件移动进度";
        Width = 650;
        Height = 390;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = Runtime.ApplicationIcon;
        var body = new DockPanel { Margin = new Thickness(16) };
        status = new TextBlock { Text = "正在移动…已完成的项目会保留。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(status, Dock.Top);
        body.Children.Add(status);
        cancel = new Button { Content = "取消后续项目", HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 12, 0, 0) };
        cancel.Click += (_, _) => { if (finished) Close(); else Cancel(); };
        DockPanel.SetDock(cancel, Dock.Bottom);
        body.Children.Add(cancel);
        results = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        body.Children.Add(results);
        Content = body;
        Closing += (_, e) => { if (!finished) { Cancel(); e.Cancel = true; } };
        Closed += (_, _) => cancellation.Dispose();
    }

    internal void Cancel()
    {
        cancellation.Cancel();
        status.Text = "已请求取消，正在核对当前项目；此前完成的移动保留。";
        cancel.IsEnabled = false;
    }

    internal async Task<BatchMoveResult> MoveAsync(string[] paths, MoveTarget target, string? targetNotice = null)
    {
        var progress = new Progress<MoveItemResult>(Append);
        try
        {
            Result = await Runtime.Current.Workspace.MoveAsync(paths, target, conflict =>
                Dispatcher.InvokeAsync(() => AskConflict(conflict)).Task.Unwrap(), progress, cancellation.Token);
            Runtime.Current.Complete(Result, targetNotice);
            return Result;
        }
        finally
        {
            finished = true;
            cancel.IsEnabled = true;
            cancel.Content = "关闭";
            Runtime.Current.Render();
            Close();
        }
    }

    private Task<ConflictChoice> AskConflict(MoveConflict conflict)
    {
        if (cancellation.IsCancellationRequested) return Task.FromResult(ConflictChoice.Cancel);
        var answer = MessageBox.Show(this, $"目标已存在：\n{conflict.DestinationPath}\n\n是：保留两份并自动编号\n否：跳过此项目\n取消：停止后续项目（已完成的移动保留）", "同名冲突", MessageBoxButton.YesNoCancel);
        return Task.FromResult(answer switch { MessageBoxResult.Yes => ConflictChoice.KeepBoth, MessageBoxResult.No => ConflictChoice.Skip, _ => ConflictChoice.Cancel });
    }

    private void Append(MoveItemResult item)
    {
        if (!finished) { results.AppendText(OperationFeedback.Describe(item) + "\n\n"); results.ScrollToEnd(); }
    }
}
