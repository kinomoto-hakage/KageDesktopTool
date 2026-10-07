using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Kage.Workspace;

namespace Kage.Desktop;

// 操作期间保留取消与冲突询问；结束后详情进入设置并自动关闭进度。
internal sealed class MoveDialog : Window
{
    private readonly CancellationTokenSource cancellation = new();
    private readonly TextBox results;
    private readonly TextBlock status;
    private readonly Button cancel;
    private readonly DispatcherTimer showProgress = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
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
        showProgress.Tick += (_, _) => ShowProgress();
        Closing += (_, e) => { if (!finished) { Cancel(); e.Cancel = true; } };
        Closed += (_, _) => cancellation.Dispose();
    }

    internal void Cancel()
    {
        cancellation.Cancel();
        status.Text = "已请求取消，正在核对当前项目；此前完成的移动保留。";
        cancel.IsEnabled = false;
    }

    internal async Task<BatchMoveResult> MoveAsync(string[] paths, MoveTarget target, string? targetNotice = null,
        WindowsDesktop.POINT? desktopPosition = null)
    {
        // 快速完成的移动只发送结果通知；后台优先级让已排队的完成回调先结束窗口。
        showProgress.Start();
        var progress = new Progress<MoveItemResult>(Append);
        try
        {
            Result = await Runtime.Current.Workspace.MoveAsync(paths, target, conflict =>
                Dispatcher.InvokeAsync(() => AskConflict(conflict)).Task.Unwrap(), progress, cancellation.Token);
            if (target.IsDesktop && desktopPosition is { } point)
            {
                // 文件移动已结束，不因 Shell 位置更新迟到而闪出一个不可再取消的进度窗口。
                showProgress.Stop();
                status.Text = "文件移动已结束，正在安排桌面图标…";
                cancel.IsEnabled = false;
                var notice = await DesktopIconPlacement.PlaceAsync(Result, point);
                if (notice != null) targetNotice = string.Join("\n", new[] { targetNotice, notice }.Where(text => !string.IsNullOrEmpty(text)));
            }
            Runtime.Current.Complete(Result, targetNotice);
            return Result;
        }
        finally
        {
            finished = true;
            showProgress.Stop();
            cancel.IsEnabled = true;
            cancel.Content = "关闭";
            Runtime.Current.Render();
            Close();
        }
    }

    private Task<ConflictChoice> AskConflict(MoveConflict conflict)
    {
        if (cancellation.IsCancellationRequested) return Task.FromResult(ConflictChoice.Cancel);
        ShowProgress();
        var answer = MessageBox.Show(this, $"目标已存在：\n{conflict.DestinationPath}\n\n是：保留两份并自动编号\n否：跳过此项目\n取消：停止后续项目（已完成的移动保留）", "同名冲突", MessageBoxButton.YesNoCancel);
        return Task.FromResult(answer switch { MessageBoxResult.Yes => ConflictChoice.KeepBoth, MessageBoxResult.No => ConflictChoice.Skip, _ => ConflictChoice.Cancel });
    }

    private void ShowProgress()
    {
        showProgress.Stop();
        if (!finished && !IsVisible) Show();
    }

    private void Append(MoveItemResult item)
    {
        if (!finished) { results.AppendText(OperationFeedback.Describe(item) + "\n\n"); results.ScrollToEnd(); }
    }
}
