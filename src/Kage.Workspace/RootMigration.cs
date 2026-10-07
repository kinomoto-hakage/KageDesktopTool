namespace Kage.Workspace;

public sealed partial class DesktopWorkspace
{
    public Task<OperationResult> RecoverRootMigrationAsync(IProgress<RootMigrationProgress>? progress = null)
        => Run(() =>
        {
            if (backupRestore) return new(Outcome.RecoveryRequired, "请先恢复状态备份，再核对迁移内容。");
            if (state.PendingRootMigration == null)
                return blocked ? Locked() : new(Outcome.Success, "没有未完成的根目录迁移。");
            var original = state with { PendingRootMigration = null };
            return RollbackMigration(original, new IOException("正在恢复中断的迁移"), progress, recovering: true);
        });

    public Task<OperationResult> MigrateRootAsync(string root, IProgress<RootMigrationProgress>? progress = null, CancellationToken cancellation = default)
        => Run(() =>
        {
            if (blocked) return Locked();
            if (cancellation.IsCancellationRequested) return new(Outcome.Cancelled, "迁移开始前已取消，原配置及内容保留。", state.Root);
            var target = WindowsPaths.Root(root);
            if (SamePath(target, state.Root)) return new(Outcome.Success, "存储根目录未改变。", state.Root);
            cancellation.ThrowIfCancellationRequested();
            var items = state.Folders.Select(folder => new RootMigrationItem(folder.Id, ContentPath(folder), Path.Combine(target, folder.Name), null))
                .Concat(state.RetainedFolders.Select(folder => new RootMigrationItem(folder.FolderId, folder.ContentPath,
                    Path.Combine(target, Path.GetFileName(folder.ContentPath)), folder.ShortcutPath))).ToArray();
            try { ValidateMigration(target, items); }
            catch (MigrationConflict error) { return new(Outcome.Conflict, error.Message, target); }
            var original = state;
            var pending = new PendingRootMigration(Guid.NewGuid(), state.Root, target, items);
            SaveMigration(pending);
            try
            {
                Publish();
                foreach (var item in items)
                {
                    cancellation.ThrowIfCancellationRequested();
                    SaveMigrationItem(item with { Phase = MigrationPhase.Moving });
                    ReportMigration(progress, "正在复制并核对内容；可取消，取消后尝试恢复原位置。", item);
                    (rootTransfer ?? new WindowsDirectoryTransfer()).Move(item.SourcePath, item.DestinationPath, cancellation);
                    if (Directory.Exists(item.SourcePath) || !WindowsPaths.HasIdentity(item.DestinationPath, item.FolderId))
                        throw new IOException("目录移动未完成或归属核对失败。");
                    SaveMigrationItem(item with { Phase = MigrationPhase.Moved });
                    if (item.ShortcutPath != null)
                    {
                        SaveMigrationItem(item with { Phase = MigrationPhase.UpdatingShortcut });
                        Shell.RetargetShortcut(item.ShortcutPath, item.SourcePath, item.DestinationPath, item.FolderId);
                        if (!Shell.ShortcutTargets(item.ShortcutPath, item.DestinationPath)) throw new IOException("快捷方式目标核对失败。");
                    }
                    SaveMigrationItem(item with { Phase = MigrationPhase.Completed });
                    ReportMigration(progress, "内容目录及关联快捷方式已完成。", item);
                }
                cancellation.ThrowIfCancellationRequested();
                var complete = RebindMigratedOrder(original with { Root = target,
                    Folders = original.Folders.Select(folder => folder with { ContentRoot = null }).ToArray(),
                    RetainedFolders = original.RetainedFolders.Select(folder => folder with { ContentPath = items.Single(item => item.FolderId == folder.FolderId).DestinationPath }).ToArray() });
                store.Save(complete);
                state = complete;
                Publish();
                return new(Outcome.Success, $"全部 {items.Length} 个内容目录及关联快捷方式已迁移，存储根目录已保存：{target}", target);
            }
            catch (Exception error) { return RollbackMigration(original, error, progress); }
        });

    private void ReportMigration(IProgress<RootMigrationProgress>? progress, string message, RootMigrationItem item, bool restoring = false)
    {
        Publish();
        try { progress?.Report(new(state.PendingRootMigration!.Items.Count(entry => entry.Phase == (restoring ? MigrationPhase.Restored : MigrationPhase.Completed)),
            state.PendingRootMigration.Items.Length, message, item)); } catch { }
    }

    private OperationResult RollbackMigration(WorkspaceState original, Exception error, IProgress<RootMigrationProgress>? progress, bool recovering = false)
    {
        var failures = new List<string>();
        foreach (var item in state.PendingRootMigration!.Items.Reverse().ToArray())
        {
            try
            {
                var observed = InspectMigrationItem(item);
                if (observed.Notice != null && item.Phase is MigrationPhase.Moving or MigrationPhase.Restoring or MigrationPhase.RecoveryRequired
                    && (item.ShortcutPath == null || observed.ShortcutTarget != null
                        && (SamePath(observed.ShortcutTarget, item.SourcePath) || SamePath(observed.ShortcutTarget, item.DestinationPath))))
                {
                    // 仅处理工具自身中断留下的无内容目录；非空目录、命名流及未知归属标识均保留。
                    var empty = observed.SourceStatus == MigrationPathStatus.Unverified && observed.DestinationStatus == MigrationPathStatus.Owned ? item.SourcePath
                        : observed.DestinationStatus == MigrationPathStatus.Unverified && observed.SourceStatus == MigrationPathStatus.Owned ? item.DestinationPath : null;
                    if (empty != null && WindowsDirectoryTransfer.EmptyWithoutStreams(empty))
                    {
                        SaveMigrationItem(item with { Phase = MigrationPhase.Restoring });
                        CheckAncestors(empty);
                        Directory.Delete(empty, false);
                        observed = InspectMigrationItem(item);
                    }
                }
                if (observed.Notice != null) throw new IOException(observed.Notice);
                SaveMigrationItem(item with { Phase = MigrationPhase.Restoring });
                ReportMigration(progress, "迁移已停止，正在恢复原位置及快捷方式目标。", item, restoring: true);
                CheckAncestors(Path.GetDirectoryName(item.SourcePath)!);
                if (Directory.Exists(item.SourcePath))
                {
                    CheckAncestors(item.SourcePath);
                    if (!WindowsPaths.HasIdentity(item.SourcePath, item.FolderId))
                        throw new IOException("恢复原位置已有其他归属标识，未合并或覆盖。");
                }
                if (Directory.Exists(item.DestinationPath))
                {
                    CheckAncestors(item.DestinationPath);
                    if (!WindowsPaths.HasIdentity(item.DestinationPath, item.FolderId))
                        throw new IOException($"迁移目标归属无法确认，未删除或覆盖：{item.DestinationPath}");
                    (rootTransfer ?? new WindowsDirectoryTransfer()).Restore(item.SourcePath, item.DestinationPath, CancellationToken.None);
                }
                if (!WindowsPaths.HasIdentity(item.SourcePath, item.FolderId) || WindowsPaths.Exists(item.DestinationPath))
                    throw new IOException("原内容归属或目标移除尚未确认。");
                if (item.ShortcutPath != null && !Shell.ShortcutTargets(item.ShortcutPath, item.SourcePath))
                    Shell.RetargetShortcut(item.ShortcutPath, item.DestinationPath, item.SourcePath, item.FolderId);
                if (item.ShortcutPath != null && !Shell.ShortcutTargets(item.ShortcutPath, item.SourcePath))
                    throw new IOException("原快捷方式目标尚未恢复。");
                var restored = item with { Phase = MigrationPhase.Restored, Error = null };
                SaveMigrationItem(restored);
                ReportMigration(progress, "已核对原内容及快捷方式目标。", restored, restoring: true);
            }
            catch (Exception restoreError)
            {
                var failed = item with { Phase = MigrationPhase.RecoveryRequired, Error = restoreError.Message };
                state = state with { PendingRootMigration = state.PendingRootMigration! with
                    { Items = state.PendingRootMigration!.Items.Select(entry => entry.FolderId == item.FolderId ? failed : entry).ToArray() } };
                try { store.Save(state); } catch (Exception saveError) { failures.Add($"恢复状态保存失败：{saveError.Message}"); }
                failures.Add($"{item.SourcePath} ↔ {item.DestinationPath}：{restoreError.Message}");
            }
        }
        if (failures.Count == 0)
        {
            if (state.PendingRootMigration!.Items.Select(InspectMigrationItem).Any(item => !item.Restored))
                failures.Add("最终核对仍有未恢复内容或快捷方式，迁移记录保留。");
        }
        if (failures.Count == 0)
        {
            try
            {
                var restored = RebindMigratedOrder(original);
                store.Save(restored); state = restored; blocked = false; notices.Clear();
            }
            catch (Exception saveError) { failures.Add($"原配置恢复提交失败：{saveError.Message}"); }
        }
        if (failures.Count != 0)
        {
            blocked = true;
            notices.Add($"迁移未完成：{error.Message}。旧根目录配置保留；恢复尚未完成，逐项记录及两边路径保留。\n{string.Join("\n", failures)}");
            Publish();
            return new(Outcome.RecoveryRequired, notices.Last(), original.Root);
        }
        Publish();
        if (recovering) return new(Outcome.Success, "已恢复全部原内容和快捷方式目标，旧根目录配置已确认，恢复状态已解除。", original.Root);
        return new(error is OperationCanceledException ? Outcome.Cancelled : Outcome.Failed,
            $"迁移未完成：{(error is OperationCanceledException ? "已取消" : error.Message)}。已恢复全部原内容和快捷方式目标，旧根目录配置保留。", original.Root);
    }

    private void ValidateMigration(string target, RootMigrationItem[] items)
    {
        if (IsChild(target, state.Root)) throw new IOException("不能将存储根目录迁入自身的子目录。");
        for (var ancestor = target; ancestor != null; ancestor = Path.GetDirectoryName(ancestor))
            if (Directory.Exists(ancestor)) CheckAncestors(ancestor);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            CheckAncestors(item.SourcePath);
            if (!WindowsPaths.HasIdentity(item.SourcePath, item.FolderId)) throw new IOException($"原内容目录不可用或归属无效：{item.SourcePath}");
            if (SamePath(target, item.SourcePath) || IsChild(target, item.SourcePath)
                || SamePath(item.DestinationPath, item.SourcePath) || IsChild(item.SourcePath, item.DestinationPath))
                throw new IOException($"迁移路径存在自身或嵌套关系：{item.SourcePath}；{item.DestinationPath}");
            if (!sources.Add(item.SourcePath) || !paths.Add(item.DestinationPath) || WindowsPaths.Exists(item.DestinationPath))
                throw new MigrationConflict($"迁移清单或目标存在同名冲突，整体未执行；请处理冲突或选择其他目录：{item.DestinationPath}");
            if (item.ShortcutPath != null && !Shell.ShortcutTargets(item.ShortcutPath, item.SourcePath))
                throw new IOException($"工具快捷方式目标已改变或不可用：{item.ShortcutPath}");
        }
        WindowsPaths.CheckRoot(target, true);
        CheckAncestors(target);
    }

    private static WorkspaceState RebindMigratedOrder(WorkspaceState configuration)
        => configuration with { Folders = configuration.Folders.Select(folder =>
        {
            if (folder.CustomOrder == null) return folder;
            var path = Path.Combine(folder.ContentRoot ?? configuration.Root, folder.Name);
            if (!WindowsPaths.HasIdentity(path, folder.Id)) throw new IOException("迁移顺序对应的内容归属尚未确认：" + path);
            // 复制／恢复会改变 NTFS 文件身份。目录与字节核对完成后，按已保存名称顺序重新绑定身份。
            var names = folder.CustomOrder.Select(item => item with { Identity = null }).ToArray();
            var rebound = ContentOrdering.Reconcile(ContentFiles.Read(path), names)
                .Select(entry => new ContentOrderItem(entry.Name, entry.Identity)).ToArray();
            return folder.CustomOrder.SequenceEqual(rebound) ? folder : folder with { CustomOrder = rebound };
        }).ToArray() };

    private void SaveMigration(PendingRootMigration pending)
    {
        var next = state with { PendingRootMigration = pending };
        store.Save(next);
        state = next;
    }

    private void SaveMigrationItem(RootMigrationItem item)
        => SaveMigration(state.PendingRootMigration! with { Items = state.PendingRootMigration!.Items.Select(entry => entry.FolderId == item.FolderId ? item : entry).ToArray() });

    private sealed class MigrationConflict(string message) : IOException(message);
}
