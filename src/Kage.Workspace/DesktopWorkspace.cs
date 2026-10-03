namespace Kage.Workspace;

public sealed partial class DesktopWorkspace(IWorkspaceStore store, IStartupRegistration startup, IFolderShell? folderShell = null,
    IRootDirectoryTransfer? rootTransfer = null) : IDesktopWorkspace
{
    private readonly SemaphoreSlim operations = new(1, 1);
    private IFolderShell Shell => folderShell ?? WindowsFolderShell.Default;
    private WorkspaceState state = new();
    private DisplayArea[] displays = [];
    private bool blocked = true;
    private bool backupRestore;
    private bool desktopAvailable;
    private readonly List<string> notices = [];
    private string? startupNotice;
    private volatile WorkspaceSnapshot snapshot = new(@"D:\KageFiles\", false, "d", [], true, []);
    public WorkspaceSnapshot Snapshot => snapshot;

    // 会话展示状态不写入配置，也不触发文件恢复；重启须重新核对宿主。
    public Task<OperationResult> ReportDesktopAvailabilityAsync(bool available) => Run(() =>
    {
        desktopAvailable = available;
        snapshot = snapshot with { DesktopAvailable = available };
        return new(Outcome.Success, available ? "桌面展示已恢复。" : "桌面展示暂不可用；内容及配置保留，可从设置打开实际目录。");
    });

    private async Task<OperationResult> Run(Func<OperationResult> operation)
    {
        await operations.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                try { return operation(); }
                catch (Exception e) { return new OperationResult(Outcome.Failed, e.Message); }
            }).ConfigureAwait(false);
        }
        finally { operations.Release(); }
    }

    public Task<OperationResult> InitializeAsync(IReadOnlyList<DisplayArea> areas) => Run(() =>
    {
        displays = areas.ToArray();
        notices.Clear();
        startupNotice = null;
        try
        {
            var loaded = store.Read();
            state = loaded.State;
            backupRestore = loaded.NeedsBackupRestore;
            blocked = backupRestore;
            if (loaded.Notice != null) notices.Add(loaded.Notice);
            if (!blocked) RecoverPending();
            PlaceAndPublish(!blocked);
            return new(blocked ? Outcome.RecoveryRequired : Outcome.Success, blocked ? string.Join("\n", notices) : "工作区已加载。");
        }
        catch (Exception e)
        {
            blocked = true;
            notices.Add($"工作区加载失败，停止修改并保留原状态：{e.Message}");
            Publish();
            return new(Outcome.RecoveryRequired, notices.Last());
        }
    });

    private void RecoverPending()
    {
        if (state.PendingRootMigration != null)
        {
            blocked = true;
            notices.Add("存在未完成的根目录迁移，已核对实际内容位置和快捷方式；请在设置查看未恢复清单，并重试恢复旧位置。修改操作暂时停用。");
            return;
        }
        RecoverFolderChange();
        RecoverContentRename();
        if (state.PendingCreate is { } pending)
        {
            var path = ContentPath(pending);
            WindowsPaths.CheckRoot(pending.ContentRoot ?? state.Root, false);
            if (File.Exists(path)) throw new IOException($"待恢复目录的位置出现同名文件：{path}");
            if (Directory.Exists(path) && !WindowsPaths.HasIdentity(path, pending.Id))
                throw new IOException($"待恢复目录无法确认属于本次创建：{path}。未自动关联；请核对后明确选择关联该目录，或先处理同名项目。记录及内容均保留。");
            var recovered = state with { PendingCreate = null,
                Folders = Directory.Exists(path) ? [.. state.Folders, pending] : state.Folders };
            store.Save(recovered);
            state = recovered;
            notices.Add($"已核对并恢复中断创建：{path}");
        }
        if (state.PendingStartup is { } pendingStartup)
        {
            var actual = startup.ReadCommand();
            if (actual != pendingStartup.PreviousCommand && actual != pendingStartup.TargetCommand)
                throw new IOException("中断后的自启配置与操作前／目标均不一致，需检查实际启动项后重启核对。");
            var recovered = state with { PendingStartup = null,
                StartupEnabled = actual == pendingStartup.TargetCommand ? pendingStartup.Enabled : state.StartupEnabled };
            store.Save(recovered);
            state = recovered;
            notices.Add("已核对实际启动配置并恢复中断的自启操作。");
        }
        try
        {
            var command = startup.ReadCommand();
            if ((state.StartupEnabled ? startup.LaunchCommand : null) != command)
                startupNotice = "保存的自启选择与实际启动配置不一致。请在设置中应用开关以修复；启动过程未更改配置。";
        }
        catch (Exception e) { startupNotice = $"无法读取实际启动配置：{e.Message}。其他业务操作仍可使用。"; }
    }

    public Task<OperationResult> SelectRootAsync(string root) => Run(() =>
    {
        if (blocked) return Locked();
        var full = WindowsPaths.Root(root);
        var folders = state.Folders;
        if ((state.Folders.Length != 0 || state.RetainedFolders.Length != 0) && !string.Equals(full, WindowsPaths.Root(state.Root), StringComparison.OrdinalIgnoreCase))
        {
            var unavailable = false;
            try { WindowsPaths.CheckRoot(state.Root, false); WindowsPaths.CheckRoot(state.Root, true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { unavailable = true; }
            if (!unavailable) return new(Outcome.Failed, "已有内容文件夹关联，需使用整体迁移操作更换可用根目录。");
            // 离线根目录的关联继续使用原路径；用户另选只决定后续新建位置，不伪造迁移成功。
            folders = state.Folders.Select(folder => folder with { ContentRoot = folder.ContentRoot ?? state.Root }).ToArray();
        }
        WindowsPaths.CheckRoot(full, true);
        var next = state with { Root = full, Folders = folders };
        store.Save(next);
        state = next;
        Publish();
        return new(Outcome.Success, $"存储根目录已保存：{full}" + (folders.Any(folder => folder.ContentRoot != null) ? "。旧内容仍保留原路径关联，未进行迁移；新 Folder 使用所选目录。" : ""), full);
    });

    public Task<OperationResult> CreateFolderAsync(string name, ConflictChoice conflict = ConflictChoice.Ask, CancellationToken cancellation = default) => Run(() =>
    {
        if (blocked) return Locked();
        if (cancellation.IsCancellationRequested || conflict == ConflictChoice.Cancel) return new(Outcome.Cancelled, "已取消创建。");
        WindowsPaths.Name(name);
        WindowsPaths.CheckRoot(state.Root, true);
        var path = Path.Combine(state.Root, name);
        if (WindowsPaths.Exists(path) || state.Folders.Any(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            if (conflict == ConflictChoice.Ask) return new(Outcome.Conflict, "同名目录或文件已存在，请选择保留两份、跳过或取消。", path);
            if (conflict == ConflictChoice.Skip) return new(Outcome.Skipped, "已跳过；现有内容未改变。", path);
            var original = name;
            for (var number = 2; ; number++)
            {
                name = $"{original} ({number})";
                WindowsPaths.Name(name);
                path = Path.Combine(state.Root, name);
                if (!WindowsPaths.Exists(path) && !state.Folders.Any(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))) break;
            }
        }
        var folder = new FolderRecord(Guid.NewGuid(), name);
        var intent = state with { PendingCreate = folder };
        store.Save(intent);
        state = intent;
        var created = false;
        try
        {
            if (cancellation.IsCancellationRequested) throw new OperationCanceledException("已取消创建。");
            WindowsPaths.CreateExclusive(path);
            created = true;
            WindowsPaths.WriteIdentity(path, folder.Id);
        }
        catch (Exception e)
        {
            if (created)
            {
                blocked = true;
                notices.Add($"目录已创建但归属标识未完成：{path}。{e.Message}。记录及目录保留，需核对后关联。");
                Publish();
                return new(Outcome.RecoveryRequired, notices.Last(), path);
            }
            var cleared = state with { PendingCreate = null };
            try { store.Save(cleared); state = cleared; }
            catch (Exception saveError) { blocked = true; notices.Add($"清理创建记录失败：{saveError.Message}"); }
            Publish();
            return new(blocked ? Outcome.RecoveryRequired : e is OperationCanceledException ? Outcome.Cancelled : Outcome.Failed, e.Message, path);
        }
        var complete = state with { Folders = [.. state.Folders, folder], PendingCreate = null };
        try
        {
            store.Save(complete);
            state = complete;
            PlaceAndPublish(true);
            return new(Outcome.Success, $"已创建内容文件夹：{path}" + (snapshot.Folders.Last().Visible ? "" : "。桌面空间不足，暂不展示；记录与内容均保留。"), path);
        }
        catch (Exception e)
        {
            blocked = true;
            notices.Add($"目录已创建但状态提交失败，需重启核对恢复：{path}。{e.Message}");
            Publish();
            return new(Outcome.RecoveryRequired, notices.Last(), path);
        }
    });

    public Task<OperationResult> SetStartupAsync(bool enabled) => Run(() =>
    {
        if (blocked) return Locked();
        var previous = startup.ReadCommand();
        var target = enabled ? startup.LaunchCommand : null;
        var original = state;
        var intent = state with { PendingStartup = new(enabled, previous, target) };
        store.Save(intent);
        state = intent;
        try
        {
            startup.WriteCommand(target);
            if (startup.ReadCommand() != target) throw new IOException("自启配置写入后核对不一致。");
            var complete = original with { StartupEnabled = enabled };
            store.Save(complete);
            state = complete;
            startupNotice = null;
            Publish();
            return new(Outcome.Success, enabled ? "已开启用户登录自启并保存选择。" : "已关闭用户登录自启并保存选择。");
        }
        catch (Exception e)
        {
            try
            {
                startup.WriteCommand(previous);
                if (startup.ReadCommand() != previous) throw new IOException("恢复自启配置后核对不一致。");
                store.Save(original);
                state = original;
                Publish();
                return new(Outcome.Failed, $"更新失败，已恢复原启动配置和偏好：{e.Message}");
            }
            catch (Exception restoreError)
            {
                blocked = true;
                notices.Add($"自启更新失败且恢复未完成：{e.Message}。恢复错误：{restoreError.Message}；已保留操作记录，请重启核对实际注册。");
                Publish();
                return new(Outcome.RecoveryRequired, notices.Last());
            }
        }
    });

    public Task<OperationResult> RestoreBackupAsync() => Run(() =>
    {
        if (!backupRestore) return new(Outcome.Failed, "没有已验证可恢复的备份；请保留状态文件并检查错误原因。");
        store.RestoreBackup();
        state = store.Read().State;
        blocked = false;
        backupRestore = false;
        notices.Clear();
        notices.Add("最近有效备份已恢复，损坏文件已另存保留。");
        try { RecoverPending(); PlaceAndPublish(true); }
        catch (Exception e)
        {
            blocked = true;
            notices.Add($"备份已恢复，但未完成操作仍需核对：{e.Message}");
            Publish();
            return new(Outcome.RecoveryRequired, notices.Last());
        }
        return new(Outcome.Success, "最近有效状态已恢复；损坏文件已另存为证据。");
    });

    public Task<OperationResult> ConfirmPendingCreateAsync() => Run(() =>
    {
        if (backupRestore) return new(Outcome.RecoveryRequired, "请先明确恢复状态备份，再核对待关联目录。");
        if (state.PendingCreate is not { } pending) return new(Outcome.Failed, "没有待核对的创建记录。");
        var path = ContentPath(pending);
        if (!Directory.Exists(path)) return new(Outcome.Failed, $"待关联目录不存在或不可访问：{path}", path);
        WindowsPaths.WriteIdentity(path, pending.Id, repairInvalid: true);
        var complete = state with { PendingCreate = null, Folders = [.. state.Folders, pending] };
        store.Save(complete);
        state = complete;
        blocked = false;
        notices.Clear();
        PlaceAndPublish(true);
        return new(Outcome.Success, $"已根据明确选择关联现有目录：{path}", path);
    });

    public Task<OperationResult> RefreshAsync(IReadOnlyList<DisplayArea> areas) => Run(() =>
    {
        displays = areas.ToArray();
        string? failure = null;
        if (!blocked)
        {
            var next = state with { Folders = state.Folders.Select(folder =>
            {
                if (folder.CustomOrder == null) return folder;
                try
                {
                    var order = ContentOrdering.Reconcile(ContentFiles.Read(ContentPath(folder)), folder.CustomOrder)
                        .Select(entry => new ContentOrderItem(entry.Name, entry.Identity)).ToArray();
                    return folder.CustomOrder.SequenceEqual(order) ? folder : folder with { CustomOrder = order };
                }
                catch (IOException) { return folder; }
                catch (UnauthorizedAccessException) { return folder; }
            }).ToArray() };
            if (!next.Folders.SequenceEqual(state.Folders))
            {
                try { store.Save(next); state = next; }
                catch (Exception e) { failure = $"内容顺序协调保存失败，原设置保留：{e.Message}"; }
            }
        }
        PlaceAndPublish(!blocked);
        if (failure != null) return new(Outcome.Failed, failure);
        return new(blocked ? Outcome.RecoveryRequired : Outcome.Success, "显示状态已刷新。");
    });

    private OperationResult Locked() => new(Outcome.RecoveryRequired, "工作区处于恢复状态，暂停修改。请检查说明并恢复备份或重启核对未完成操作。");

    public Task<OperationResult> ToggleFolderAsync(Guid id) => Run(() =>
    {
        if (blocked) return Locked();
        var target = snapshot.Folders.FirstOrDefault(folder => folder.Folder.Id == id && folder.Visible);
        if (target == null) return new(Outcome.Failed, "Folder 当前不可展示，请先在设置刷新展示。");
        var changed = target.Folder with { Expanded = !target.Folder.Expanded };
        var next = state.Folders.ToDictionary(folder => folder.Id);
        next[id] = changed;
        if (changed.Expanded)
        {
            var area = HeaderLayout.Available(changed, displays, []);
            if (area == null) return new(Outcome.Failed, "当前位置空间不足，无法展开；请移动或缩小 Folder。");
            var occupied = new List<(FolderRecord Folder, DisplayArea Area)> { (changed, area) };
            foreach (var other in snapshot.Folders.Where(folder => folder.Visible && folder.Folder.Id != id))
            {
                var placed = HeaderLayout.Find(other.Folder, displays, occupied);
                if (placed == null) return new(Outcome.Failed, "桌面空间不足，已取消展开并保留原布局；请缩小或折叠其他 Folder。");
                next[placed.Id] = placed;
                occupied.Add((placed, HeaderLayout.Available(placed, displays, occupied)!));
            }
        }
        var complete = state with { Folders = state.Folders.Select(folder => next[folder.Id]).ToArray() };
        store.Save(complete);
        state = complete;
        Publish();
        return new(Outcome.Success, changed.Expanded ? "已展开。" : "已折叠。");
    });
    private string ContentPath(FolderRecord folder) => Path.Combine(folder.ContentRoot ?? state.Root, folder.Name);

    public LayoutInteraction? BeginLayout(Guid id)
    {
        var current = snapshot;
        return current.RecoveryRequired || current.RootMigration != null || !current.Folders.Any(folder => folder.Folder.Id == id && folder.Visible)
            ? null : new(id, current, displays);
    }

    public Task<OperationResult> CommitLayoutAsync(LayoutInteraction interaction)
    {
        interaction.EndDrag();
        var proposed = interaction.Folders.ToArray();
        return Run(() =>
        {
            if (blocked) return Locked();
            if (!displays.SequenceEqual(interaction.Displays)) return new(Outcome.Failed, "显示环境已变化，本次输入未覆盖新布局；请重试。");
            if (!state.Folders.SequenceEqual(interaction.Original)) return new(Outcome.Failed, "布局已发生变化，本次输入未覆盖新布局；请重试。");
            var occupied = new List<(FolderRecord Folder, DisplayArea Area)>();
            foreach (var folder in proposed.Where(folder => folder.Visible))
            {
                var area = HeaderLayout.Available(folder.Folder, displays, occupied);
                if (area == null) return new(Outcome.Failed, "当前显示区域无法容纳本次布局，已保留原布局。");
                occupied.Add((folder.Folder, area));
            }
            var next = state with { Folders = proposed.Select(folder => folder.Folder).ToArray() };
            if (!next.Folders.SequenceEqual(state.Folders)) store.Save(next);
            state = next;
            Publish();
            return new(Outcome.Success, "位置和尺寸已保存。");
        });
    }

    public Task<OperationResult> SetViewAsync(Guid id, bool grid) => Run(() =>
    {
        if (blocked) return Locked();
        if (!state.Folders.Any(folder => folder.Id == id)) return new(Outcome.Failed, "Folder 不存在。");
        var next = state with { Folders = state.Folders.Select(folder => folder.Id == id ? folder with { Grid = grid } : folder).ToArray() };
        store.Save(next);
        state = next;
        Publish();
        return new(Outcome.Success, grid ? "已切换网格。" : "已切换列表。");
    });

    public Task<OperationResult> SetContentViewAsync(Guid id, bool grid, int iconSize, ContentSortKey sortKey, bool descending)
        => Run(() =>
        {
            if (blocked) return Locked();
            if (!Enum.IsDefined((ContentIconSize)iconSize) || !Enum.IsDefined(sortKey))
                return new(Outcome.Failed, "请选择有效的图标尺寸和排序方式。");
            if (!state.Folders.Any(folder => folder.Id == id)) return new(Outcome.Failed, "Folder 不存在。");
            var next = state with { Folders = state.Folders.Select(folder => folder.Id != id ? folder : folder with
            {
                Grid = grid, ListIconSize = grid ? folder.ListIconSize : iconSize,
                GridIconSize = grid ? iconSize : folder.GridIconSize, SortKey = sortKey, SortDescending = descending
            }).ToArray() };
            store.Save(next);
            state = next;
            Publish();
            return new(Outcome.Success, "查看方式与排序已保存。");
        });
    public Task<OperationResult> ReorderContentsAsync(Guid id, IReadOnlyList<string> selectedPaths, string? beforePath)
        => Run(() =>
        {
            if (blocked) return Locked();
            var folder = state.Folders.SingleOrDefault(folder => folder.Id == id);
            if (folder == null) return new(Outcome.Failed, "Folder 不存在。");
            var entries = ContentOrdering.Sort(ContentFiles.Read(ContentPath(folder)), folder);
            var selected = selectedPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (selected.Count == 0 || !selected.All(path => entries.Any(entry => string.Equals(entry.ActualPath, path, StringComparison.OrdinalIgnoreCase)))
                || beforePath != null && !entries.Any(entry => string.Equals(entry.ActualPath, beforePath, StringComparison.OrdinalIgnoreCase)))
                return new(Outcome.Failed, "拖动项目已变化，请刷新后重试。");
            if (beforePath != null && selected.Contains(beforePath)) return new(Outcome.Success, "顺序未改变。");
            var moving = entries.Where(entry => selected.Contains(entry.ActualPath)).ToArray();
            var reordered = entries.Where(entry => !selected.Contains(entry.ActualPath)).ToList();
            var index = beforePath == null ? reordered.Count : reordered.FindIndex(entry => string.Equals(entry.ActualPath, beforePath, StringComparison.OrdinalIgnoreCase));
            reordered.InsertRange(index, moving);
            if (entries.SequenceEqual(reordered)) return new(Outcome.Success, "顺序未改变。");
            var changed = folder with { SortKey = ContentSortKey.Custom,
                CustomOrder = reordered.Select(entry => new ContentOrderItem(entry.Name, entry.Identity)).ToArray() };
            var next = state with { Folders = state.Folders.Select(item => item.Id == id ? changed : item).ToArray() };
            store.Save(next);
            state = next;
            Publish();
            return new(Outcome.Success, "自定义顺序已保存。");
        });

    public Task<OperationResult> SetIconAsync(string choice) => Run(() =>
    {
        if (blocked) return Locked();
        if (choice is not ("a" or "b" or "c" or "d")) return new(Outcome.Failed, "请选择 A／B／C／D 图标方案。");
        var next = state with { IconChoice = choice };
        try { store.Save(next); }
        catch (Exception e) { return new(Outcome.Failed, $"图标选择保存失败，原方案保留：{e.Message}"); }
        state = next;
        Publish();
        return new(Outcome.Success, "图标选择已保存。");
    });

    public AppearanceInteraction? BeginAppearance(Guid id)
    {
        var current = snapshot;
        var folder = current.Folders.FirstOrDefault(folder => folder.Folder.Id == id);
        return current.RecoveryRequired || current.RootMigration != null || folder == null ? null : new(this, folder.Folder);
    }

    public Task<OperationResult> ApplyAppearanceAsync(AppearanceInteraction interaction) => Run(() =>
    {
        if (blocked) return Locked();
        if (interaction.Owner != this || interaction.Closed) return AppearanceInteraction.Ended();
        if (interaction.Error.Length != 0) return new(Outcome.Failed, interaction.Error);
        var original = interaction.Original;
        var current = state.Folders.FirstOrDefault(folder => folder.Id == interaction.FolderId);
        if (current == null || current.Color != original.Color || current.Opacity != original.Opacity)
            return new(Outcome.Failed, "Folder 外观已变化，本次草稿未覆盖新设置；请取消后重新打开。");
        var changed = current with { Color = interaction.Color, Opacity = interaction.Opacity };
        var next = state with { Folders = state.Folders.Select(folder => folder.Id == changed.Id ? changed : folder).ToArray() };
        try { store.Save(next); }
        catch (Exception e) { return new(Outcome.Failed, $"外观保存失败，原设置保留；可重试或取消：{e.Message}"); }
        state = next;
        interaction.Finish(true);
        Publish();
        return new(Outcome.Success, "Folder 外观已保存。");
    });

    public OperationResult CancelAppearance(AppearanceInteraction interaction)
    {
        if (interaction.Owner != this || interaction.Closed) return AppearanceInteraction.Ended();
        interaction.Finish(false);
        return new(Outcome.Cancelled, "已取消外观修改，恢复打开前的预览。");
    }

    private void PlaceAndPublish(bool save)
    {
        var placement = HeaderLayout.Place(state.Folders, displays);
        if (save && !state.Folders.SequenceEqual(placement))
        {
            var next = state with { Folders = placement };
            try { store.Save(next); }
            catch (Exception e)
            {
                // 显示区已经改变，即使保存失败也不能继续发布屏幕外的旧几何。
                // 保留已提交配置供下次刷新重试，当前会话展示安全的临时位置。
                Publish(placement);
                throw new IOException($"恢复位置尚未保存，当前按可用显示区临时展示；请在设置中刷新重试：{e.Message}", e);
            }
            state = next;
        }
        Publish(placement);
    }

    private void Publish(FolderRecord[]? placement = null)
    {
        // 发布已提交的几何结果，不再次重排；重新寻找空位只在显式刷新／恢复中执行并保存。
        var folders = placement ?? state.Folders;
        var recovery = state.PendingRootMigration?.Items.Select(InspectMigrationItem).ToArray() ?? [];
        if (state.PendingFolderChange is { Kind: FolderChangeKind.Rename } change && RenamedDirectoryExists(change, change.FolderId))
            folders = folders.Select(folder => folder.Id == change.FolderId ? folder with { Name = Path.GetFileName(change.Destination) } : folder).ToArray();
        var placed = new List<(FolderRecord Folder, DisplayArea Area)>();
        var rendered = new List<FolderSnapshot>();
        foreach (var folder in folders)
        {
            var area = folder.LayoutHidden ? null : HeaderLayout.Available(folder, displays, placed);
            if (area != null) placed.Add((folder, area));
            var path = ContentPath(folder);
            string? notice = area == null ? "桌面空间不足，记录与内容保留；释放空间后可在设置刷新。" : null;
            var migrationItem = state.PendingRootMigration?.Items.FirstOrDefault(item => item.FolderId == folder.Id);
            var readable = true;
            if (migrationItem != null)
            {
                var observed = recovery.Single(item => item.Item.FolderId == folder.Id);
                path = observed.SourceStatus == MigrationPathStatus.Owned ? migrationItem.SourcePath
                    : observed.DestinationStatus == MigrationPathStatus.Owned ? migrationItem.DestinationPath : migrationItem.SourcePath;
                readable = observed.SourceStatus == MigrationPathStatus.Owned || observed.DestinationStatus == MigrationPathStatus.Owned;
                if (!readable) area = null;
                notice = $"{notice}\n{(observed.Restored ? "已核对旧位置；等待恢复配置提交" : "迁移尚未恢复")}。原位置：{migrationItem.SourcePath}；目标：{migrationItem.DestinationPath}。{observed.Notice}".Trim();
            }
            int? count = null;
            var entries = new List<ContentEntry>();
            try
            {
                if (!readable) throw new IOException("尚无已确认归属的内容位置，暂停枚举。");
                entries.AddRange(ContentFiles.Read(path));
                count = entries.Count(entry => !entry.IsDirectory);
            }
            catch (Exception e) { notice = $"{notice}\n内容目录不可读或枚举未完成：{path}。{e.Message}".Trim(); }
            rendered.Add(new(folder, path, area != null, count, notice)
            {
                Entries = ContentOrdering.Sort(entries, folder),
                DisplayScale = area?.Scale ?? 1
            });
        }
        var messages = new List<string>(notices);
        if (startupNotice != null) messages.Add(startupNotice);
        if (state.Folders.Any(folder => folder.ContentRoot != null)) messages.Add("旧存储根目录不可用时保留的内容仍关联原路径；新 Folder 使用当前所选根目录。既有内容尚未迁移。");
        try { WindowsPaths.CheckRoot(state.Root, false); }
        catch (Exception e) { messages.Add($"存储根目录不可用：{e.Message}。请明确选择可用目录；已有 Folder 关联将保留。"); }
        if (state.PendingCreate is { } pending) messages.Add($"待恢复创建：{Path.Combine(state.Root, pending.Name)}，稳定标识 {pending.Id}");
        if (state.PendingContentRename is { } rename) messages.Add($"待协调内容改名：{rename.SourceName} → {rename.DestinationName}；稳定文件身份 {rename.Identity}。请核对实际项目后重启。");
        if (state.PendingRootMigration != null)
            messages.Add($"迁移清单已按实际位置核对：{recovery.Count(item => !item.Restored)} 项尚未恢复。查看下方原位置、迁移位置和实际快捷方式目标。");
        snapshot = new(state.Root, state.StartupEnabled, state.IconChoice, rendered.AsReadOnly(), blocked, messages.AsReadOnly())
            { DesktopAvailable = desktopAvailable, RootMigration = state.PendingRootMigration, MigrationRecovery = Array.AsReadOnly(recovery) };
    }
}
