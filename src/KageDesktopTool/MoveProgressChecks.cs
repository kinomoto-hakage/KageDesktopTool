using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using Kage.Workspace;

namespace Kage.Desktop;

// 实际文件移动与真实 WPF 可见性共用运行入口；仅在状态存储边界控制耗时。
internal static class MoveProgressChecks
{
    internal static int Run(bool quickOnly = false)
    {
        var fixture = Path.Combine(Path.GetTempPath(), "Kage-move-progress-" + Guid.NewGuid().ToString("N"));
        var evidence = Path.Combine(Environment.CurrentDirectory, ".scratch", "desktop-folder", "verification");
        Directory.CreateDirectory(fixture);
        Directory.CreateDirectory(evidence);
        var log = Path.Combine(evidence, "upgrade-03-move-progress.txt");
        File.WriteAllText(log, "移入／移出快速完成不闪窗与耗时进度检查\n");
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var store = new DelayedStore(new JsonWorkspaceStore(Path.Combine(fixture, "状态")));
        Runtime? runtime = null;
        Task<BatchMoveResult?>? active = null;
        var loaded = 0;
        var exit = 1;
        EventManager.RegisterClassHandler(typeof(MoveDialog), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((_, _) => loaded++));
        app.Startup += async (_, _) =>
        {
            try
            {
                IDesktopWorkspace workspace = new DesktopWorkspace(store, new NoStartup());
                await workspace.InitializeAsync(WindowsDesktop.Displays());
                await workspace.SelectRootAsync(Path.Combine(fixture, "内容"));
                await workspace.CreateFolderAsync("进度夹具");
                var folder = workspace.Snapshot.Folders.Single();
                var output = Path.Combine(fixture, "移出目标");
                Directory.CreateDirectory(output);
                runtime = new Runtime(workspace);
                var source = Path.Combine(fixture, "快速.txt");
                File.WriteAllText(source, "快速移动的真实字节");
                await QuickMove(source, MoveTarget.Folder(folder.Folder.Id), "移入");
                var inside = Path.Combine(folder.ActualPath, "快速.txt");
                Check(File.ReadAllText(inside) == "快速移动的真实字节" && !File.Exists(source), "快速移入真实字节与源消失");
                await QuickMove(inside, MoveTarget.Directory(output), "移出");
                Check(File.ReadAllText(Path.Combine(output, "快速.txt")) == "快速移动的真实字节" && !File.Exists(inside), "快速移出真实字节与源消失");
                source = Path.Combine(fixture, "取消.txt");
                File.WriteAllText(source, "取消保留源内容");
                await QuickMove(source, MoveTarget.Folder(folder.Folder.Id), "拖放取消", cancelled: true);
                Check(File.ReadAllText(source) == "取消保留源内容", "快速取消不移动源内容");
                if (quickOnly) { exit = 0; return; }

                source = Path.Combine(fixture, "耗时.txt");
                File.WriteAllText(source, "已完成的内容保留");
                store.DelayNext = true;
                var slow = active = runtime.MoveFilesAsync([source], MoveTarget.Folder(folder.Folder.Id));
                var dialog = runtime.ActiveMove!;
                await store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Check(!dialog.IsVisible, "耗时操作开始时不立即显示进度");
                await Task.Delay(800);
                Check(dialog.IsVisible && !slow.IsCompleted, "操作持续时显示真实进度窗口");
                var cancel = AutomationElement.FromHandle(new WindowInteropHelper(dialog).Handle).FindFirst(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.NameProperty, "取消后续项目"));
                Check(cancel != null && cancel.Current.IsEnabled, "真实进度取消入口可用");
                ((InvokePattern)cancel!.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
                await Task.Delay(100);
                Check(!cancel.Current.IsEnabled, "取消请求后禁止重复提交取消");
                store.Release();
                var completed = await slow;
                Check(completed!.Items.Single().Outcome == Outcome.Success && !dialog.IsVisible && runtime.ActiveMove == null,
                    "取消保留已完成文件，状态提交后进度自动关闭");
                Check(File.ReadAllText(Path.Combine(folder.ActualPath, "耗时.txt")) == "已完成的内容保留", "耗时检查不回滚已移动字节");
                exit = 0;
                File.AppendAllText(log, "全部检查通过。\n");
            }
            catch (Exception e) { File.AppendAllText(log, "失败：" + e + "\n"); }
            finally
            {
                store.Release();
                if (active != null) await active.WaitAsync(TimeSpan.FromSeconds(5));
                runtime?.Dispose();
                app.Shutdown();
            }

            async Task QuickMove(string path, MoveTarget target, string label, bool cancelled = false)
            {
                var loadedBefore = loaded;
                var detailsBefore = runtime!.Feedback.Entries.Count;
                var pending = runtime.MoveFilesAsync([path], target, cancelled: cancelled);
                var dialog = runtime.ActiveMove;
                var shown = dialog?.IsVisible == true;
                if (dialog != null) dialog.IsVisibleChanged += (_, _) => { if (dialog.IsVisible) shown = true; };
                var result = await pending;
                await Task.Delay(750);
                Check(result!.Items.Single().Outcome == (cancelled ? Outcome.Cancelled : Outcome.Success), label + "实际结果正确");
                Check(!shown && loaded == loadedBefore, label + "快速完成全程没有中央进度闪窗或迟到显示");
                Check(runtime.ActiveMove == null && runtime.Feedback.Entries.Count == detailsBefore + 1,
                    label + "仅记录一条完成汇总且释放移动状态");
            }
        };
        app.Run();
        var full = Path.GetFullPath(fixture);
        if (!full.StartsWith(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(full).StartsWith("Kage-move-progress-", StringComparison.Ordinal)) throw new IOException("拒绝清理隔离目录之外的路径。");
        Directory.Delete(full, true);
        return exit;

        void Check(bool passed, string message)
        { File.AppendAllText(log, (passed ? "通过：" : "失败：") + message + "\n"); if (!passed) throw new Exception(message); }
    }

    private sealed class DelayedStore(IWorkspaceStore real) : IWorkspaceStore
    {
        private readonly ManualResetEventSlim released = new(false);
        internal bool DelayNext { get; set; }
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Release() => released.Set();
        public StateRead Read() => real.Read();
        public void Save(WorkspaceState state)
        {
            if (DelayNext) { DelayNext = false; Entered.TrySetResult(); if (!released.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("耗时夹具未释放。"); }
            real.Save(state);
        }
        public void RestoreBackup() => real.RestoreBackup();
    }

    private sealed class NoStartup : IStartupRegistration
    {
        public string LaunchCommand => "隔离检查，不注册自启";
        public string? ReadCommand() => null;
        public void WriteCommand(string? command) => throw new InvalidOperationException("检查不应注册自启。");
    }
}
