using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Kage.Workspace;

public sealed record MoveTarget
{
    public Guid? FolderId { get; private init; }
    public string? Path { get; private init; }
    public bool IsDesktop { get; private init; }
    private MoveTarget() { }
    public static MoveTarget Folder(Guid id) => new() { FolderId = id };
    public static MoveTarget Directory(string path) => new() { Path = path };
    public static MoveTarget Desktop { get; } = new() { IsDesktop = true };
}
public sealed record MoveConflict(string SourcePath, string DestinationPath, bool IsDirectory);
public sealed record MoveItemResult(string SourcePath, Outcome Outcome, string Message, string? ActualPath = null);
public sealed record BatchMoveResult(IReadOnlyList<MoveItemResult> Items, OperationResult StateCommit);

public sealed partial class DesktopWorkspace
{
    public async Task<BatchMoveResult> MoveAsync(IReadOnlyList<string> sources, MoveTarget target,
        Func<MoveConflict, Task<ConflictChoice>>? resolveConflict = null,
        IProgress<MoveItemResult>? progress = null, CancellationToken cancellation = default)
    {
        // 调用者的选择集合在进入队列前固定；锁覆盖批量及冲突等待，刷新不会发布半批结果。
        var paths = sources.ToArray();
        var results = new List<MoveItemResult>();
        await operations.WaitAsync().ConfigureAwait(false);
        try
        {
            string? destination = null;
            string? targetError = null;
            try
            {
                if (blocked) throw new IOException(Locked().Message);
                destination = target.FolderId is { } id
                    ? ContentPath(state.Folders.Single(folder => folder.Id == id))
                    : target.IsDesktop ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) : target.Path!;
                destination = WindowsPaths.Root(destination);
                await Task.Run(() => WindowsPaths.CheckRoot(destination, false)).ConfigureAwait(false);
            }
            catch (Exception e) { targetError = e.Message; }
            var stopped = cancellation.IsCancellationRequested;
            foreach (var source in paths)
            {
                MoveItemResult item;
                if (stopped || cancellation.IsCancellationRequested)
                {
                    stopped = true;
                    item = new(source, Outcome.Cancelled, "已取消，未执行此项目；此前完成的移动保留。", source);
                }
                else if (targetError != null) item = new(source, Outcome.Failed, $"目标不可用：{targetError}", source);
                else
                {
                    item = await MoveOneAsync(source, destination!, resolveConflict, cancellation).ConfigureAwait(false);
                    stopped = item.Outcome is Outcome.Cancelled or Outcome.Conflict;
                }
                results.Add(item);
                // 观察者异常不改变已完成的文件操作，也不丢失后续逐项结果。
                try { progress?.Report(item); } catch { }
            }
            OperationResult commit;
            try
            {
                await Task.Run(() => { if (results.Any(item => item.Outcome == Outcome.Success)) store.Save(state); Publish(); }).ConfigureAwait(false);
                commit = new(Outcome.Success, "已核对两边实际内容，工作区状态提交成功。");
            }
            catch (Exception e)
            {
                commit = new(Outcome.Failed, $"文件逐项结果保留，但工作区状态提交失败：{e.Message}。刷新或重启会重新核对实际目录。");
                await Task.Run(PublishCurrent).ConfigureAwait(false);
            }
            return new(results.AsReadOnly(), commit);
        }
        finally { operations.Release(); }
    }

    private void PublishCurrent() => Publish();

    private async Task<MoveItemResult> MoveOneAsync(string source, string destination,
        Func<MoveConflict, Task<ConflictChoice>>? resolveConflict, CancellationToken cancellation)
    {
        string? actual = source;
        try
        {
            source = WindowsPaths.Root(source);
            var info = await Task.Run(() => ValidateMove(source, destination)).ConfigureAwait(false);
            actual = source;
            var proposed = Path.Combine(destination, Path.GetFileName(source));
            var choice = ConflictChoice.Ask;
            if (WindowsPaths.Exists(proposed))
            {
                choice = resolveConflict == null ? ConflictChoice.Ask
                    : await resolveConflict(new(source, proposed, info)).ConfigureAwait(false);
                if (choice == ConflictChoice.Ask) return new(source, Outcome.Conflict, "同名项目已存在，请选择保留两份、跳过或取消；后续未执行。", source);
                if (choice == ConflictChoice.Skip) return new(source, Outcome.Skipped, "同名项目已跳过，源与目标均保留。", source);
                if (choice == ConflictChoice.Cancel) return new(source, Outcome.Cancelled, "已取消批量移动；此前完成的项目保留。", source);
            }
            cancellation.ThrowIfCancellationRequested();
            return await Task.Run(() =>
            {
                // 不覆盖；排他移动遇到竞争同名时，保留两份继续寻找下一个编号。
                for (var number = 1; ; number++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var name = Path.GetFileName(source);
                    var candidate = number == 1 ? proposed : Path.Combine(destination,
                        info ? $"{name} ({number})" : $"{Path.GetFileNameWithoutExtension(name)} ({number}){Path.GetExtension(name)}");
                    if (WindowsPaths.Exists(candidate))
                    {
                        if (choice == ConflictChoice.KeepBoth) continue;
                        throw new IOException("目标在移动前出现同名项目，未覆盖；请重试并选择冲突处理。");
                    }
                    try
                    {
                        // MoveFileEx 在跨盘文件移动中先复制再删除；不设置覆盖标志。
                        if (!MoveFileEx(source, candidate, 2)) throw new Win32Exception(Marshal.GetLastWin32Error());
                        actual = candidate;
                        // Windows 跨盘复制成功但源删除失败时也可能返回成功，必须核对实际两边。
                        if (WindowsPaths.Exists(source)) return new MoveItemResult(source, Outcome.Failed, "目标已复制，但源仍存在，未完成移动；两边内容均保留。", candidate);
                        if (!WindowsPaths.Exists(candidate)) return new MoveItemResult(source, Outcome.Failed, "原生操作返回后目标不可见，不能确认移动成功；请核对实际位置。", null);
                        return new MoveItemResult(source, Outcome.Success, "已实际移动。", candidate);
                    }
                    catch (Win32Exception e) when (info && (e.NativeErrorCode == 17 ||
                        e.NativeErrorCode == 5 && !SamePath(Path.GetPathRoot(source)!, Path.GetPathRoot(candidate)!)))
                    {
                        try { WindowsPaths.CreateExclusive(candidate); }
                        catch (Win32Exception conflict) when (choice == ConflictChoice.KeepBoth && conflict.NativeErrorCode is 80 or 183) { continue; }
                        actual = candidate;
                        WindowsDirectoryTransfer.CopyDirectoryAcrossVolumes(source, candidate, cancellation);
                        return new MoveItemResult(source, Outcome.Success, "已跨磁盘移动普通子文件夹。", candidate);
                    }
                    catch (Win32Exception e) when (choice == ConflictChoice.KeepBoth && e.NativeErrorCode is 80 or 183) { }
                }
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return new(source, Outcome.Cancelled, "已取消；如复制已开始，实际目标中的内容保留。", actual); }
        catch (Exception e) { return new(source, Outcome.Failed, $"移动失败：{e.Message}。源及可能已复制的目标内容均可核对。", actual); }
    }

    private bool ValidateMove(string source, string destination)
    {
        var attributes = File.GetAttributes(source);
        var directory = (attributes & FileAttributes.Directory) != 0;
        // 拒绝重解析路径，防止目录联接绕过自身／子目录检查；.lnk 是普通文件。
        CheckAncestors(source);
        CheckAncestors(destination);
        if (WindowsPaths.IsIdentityFile(source)) throw new IOException("Folder 内部归属标识不能作为内容移动。");
        if (state.Folders.Any(folder => SamePath(source, ContentPath(folder)) || IsChild(ContentPath(folder), source)))
            throw new IOException("不能移动桌面 Folder 的内容文件夹或它的父目录；请选择其中的内容项目。");
        if (SamePath(Path.GetDirectoryName(source)!, destination)) throw new IOException("源与目标为同一目录，未执行移动。");
        if (directory && (SamePath(source, destination) || IsChild(destination, source))) throw new IOException("不能将目录移入自身或子目录。");
        return directory;
    }

    private static bool SamePath(string first, string second) => string.Equals(Path.TrimEndingDirectorySeparator(first), Path.TrimEndingDirectorySeparator(second), StringComparison.OrdinalIgnoreCase);
    private static bool IsChild(string child, string parent) => child.StartsWith(Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static void CheckAncestors(string path)
    {
        for (var current = path; current != null; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException($"重解析路径暂不支持移动，请选择实际目录：{current}");
    }

    [DllImport("kernel32.dll", EntryPoint = "MoveFileExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool MoveFileEx(string existing, string destination, int flags);
}
