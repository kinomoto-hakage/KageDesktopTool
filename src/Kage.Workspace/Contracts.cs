using System.Text.Json.Serialization;

namespace Kage.Workspace;

public enum ConflictChoice { Ask, KeepBoth, Skip, Cancel }
public enum Outcome { Success, Conflict, Skipped, Cancelled, Failed, RecoveryRequired }
public enum FolderDeleteChoice { KeepContents, Recycle, Cancel }
public enum ContentSortKey { Name, Modified, Size, Custom }
public enum ContentIconSize { Small = 16, Medium = 32, Large = 48, ExtraLarge = 96 }
public sealed record ContentOrderItem(string Name, string? Identity = null);
public sealed record OperationResult(Outcome Outcome, string Message, string? ActualPath = null)
{
    public bool Succeeded => Outcome == Outcome.Success;
}

// 位置使用屏幕物理像素，尺寸使用 DIP；布局按显示器 Scale 换算边界。
public sealed record DisplayArea(int X, int Y, int Width, int Height, double Scale = 1);
public sealed record FolderRecord([property: JsonRequired] Guid Id, [property: JsonRequired] string Name,
    [property: JsonRequired] int X = 0, [property: JsonRequired] int Y = 0,
    [property: JsonRequired] double HeaderWidth = 300, [property: JsonRequired] double HeaderHeight = 48, [property: JsonRequired] double BodyHeight = 260,
    [property: JsonRequired] bool Expanded = false, [property: JsonRequired] bool Grid = true,
    [property: JsonRequired] string Color = "#666666", [property: JsonRequired] double Opacity = .68,
    string? ContentRoot = null, bool LayoutHidden = false,
    int ListIconSize = (int)ContentIconSize.Small, int GridIconSize = (int)ContentIconSize.Medium, ContentSortKey SortKey = ContentSortKey.Name,
    bool SortDescending = false, ContentOrderItem[]? CustomOrder = null);
public sealed record ContentEntry(string ActualPath, string Name, bool IsDirectory, long? ModifiedTicks, long? Length,
    string? Identity = null)
{
    public string DisplayName => !IsDirectory && Name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
        ? Name[..^4] : Name;
}
public sealed record FolderSnapshot(FolderRecord Folder, string ActualPath, bool Visible, int? FileCount, string? Notice)
{
    public IReadOnlyList<ContentEntry> Entries { get; init; } = [];
    public double DisplayScale { get; init; } = 1;
}
public sealed record WorkspaceSnapshot(string Root, bool StartupEnabled, string IconChoice,
    IReadOnlyList<FolderSnapshot> Folders, bool RecoveryRequired, IReadOnlyList<string> Notices)
{
    public bool DesktopAvailable { get; init; }
    public PendingRootMigration? RootMigration { get; init; }
    public IReadOnlyList<MigrationRecoveryItem> MigrationRecovery { get; init; } = [];
}

public interface IDesktopWorkspace
{
    WorkspaceSnapshot Snapshot { get; }
    Task<OperationResult> InitializeAsync(IReadOnlyList<DisplayArea> displays);
    Task<OperationResult> SelectRootAsync(string root);
    Task<OperationResult> MigrateRootAsync(string root, IProgress<RootMigrationProgress>? progress = null, CancellationToken cancellation = default);
    Task<OperationResult> RecoverRootMigrationAsync(IProgress<RootMigrationProgress>? progress = null);
    Task<OperationResult> CreateFolderAsync(string name, ConflictChoice conflict = ConflictChoice.Ask, CancellationToken cancellation = default);
    Task<OperationResult> RenameFolderAsync(Guid id, string name, ConflictChoice conflict = ConflictChoice.Ask, CancellationToken cancellation = default);
    Task<OperationResult> DeleteFolderAsync(Guid id, FolderDeleteChoice choice, ConflictChoice conflict = ConflictChoice.Ask, CancellationToken cancellation = default);
    Task<OperationResult> SetStartupAsync(bool enabled);
    Task<OperationResult> RestoreBackupAsync();
    Task<OperationResult> ConfirmPendingCreateAsync();
    Task<OperationResult> RefreshAsync(IReadOnlyList<DisplayArea> displays);
    Task<OperationResult> ReportDesktopAvailabilityAsync(bool available);
    Task<OperationResult> ToggleFolderAsync(Guid id);
    LayoutInteraction? BeginLayout(Guid id);
    Task<OperationResult> CommitLayoutAsync(LayoutInteraction interaction);
    Task<OperationResult> SetViewAsync(Guid id, bool grid);
    Task<OperationResult> SetContentViewAsync(Guid id, bool grid, int iconSize, ContentSortKey sortKey, bool descending);
    Task<OperationResult> ReorderContentsAsync(Guid id, IReadOnlyList<string> selectedPaths, string? beforePath);
    Task<OperationResult> RenameContentAsync(Guid id, string actualPath, string name);
    AppearanceInteraction? BeginAppearance(Guid id);
    Task<OperationResult> ApplyAppearanceAsync(AppearanceInteraction interaction);
    OperationResult CancelAppearance(AppearanceInteraction interaction);
    Task<OperationResult> SetIconAsync(string choice);
    Task<BatchMoveResult> MoveAsync(IReadOnlyList<string> sources, MoveTarget target,
        Func<MoveConflict, Task<ConflictChoice>>? resolveConflict = null,
        IProgress<MoveItemResult>? progress = null, CancellationToken cancellation = default);
}

public sealed record PendingStartup([property: JsonRequired] bool Enabled,
    [property: JsonRequired] string? PreviousCommand, [property: JsonRequired] string? TargetCommand);
public enum FolderChangeKind { Rename, KeepContents, Recycle }
public sealed record PendingFolderChange([property: JsonRequired] Guid FolderId,
    [property: JsonRequired] FolderChangeKind Kind, [property: JsonRequired] string Destination);
public sealed record PendingContentRename([property: JsonRequired] Guid FolderId, [property: JsonRequired] string SourceName,
    [property: JsonRequired] string DestinationName, [property: JsonRequired] string Identity);
public sealed record RetainedFolder([property: JsonRequired] Guid FolderId,
    [property: JsonRequired] string ContentPath, [property: JsonRequired] string ShortcutPath);
public enum MigrationPhase { Planned, Moving, Moved, UpdatingShortcut, Completed, Restoring, Restored, RecoveryRequired }
public sealed record RootMigrationItem([property: JsonRequired] Guid FolderId,
    [property: JsonRequired] string SourcePath, [property: JsonRequired] string DestinationPath,
    [property: JsonRequired] string? ShortcutPath, [property: JsonRequired] MigrationPhase Phase = MigrationPhase.Planned,
    string? Error = null);
public sealed record PendingRootMigration([property: JsonRequired] Guid OperationId,
    [property: JsonRequired] string OldRoot, [property: JsonRequired] string NewRoot,
    [property: JsonRequired] RootMigrationItem[] Items);
public sealed record RootMigrationProgress(int Completed, int Total, string Message, RootMigrationItem? Item = null);
public enum MigrationPathStatus { Missing, Owned, Unverified, Unavailable }
public sealed record MigrationRecoveryItem(RootMigrationItem Item, MigrationPathStatus SourceStatus,
    MigrationPathStatus DestinationStatus, string? ShortcutTarget, bool Restored, string? Notice);
public sealed record WorkspaceState
{
    [System.Text.Json.Serialization.JsonRequired]
    public int Version { get; init; } = 1;
    [System.Text.Json.Serialization.JsonRequired]
    public string Root { get; init; } = @"D:\KageFiles\";
    [System.Text.Json.Serialization.JsonRequired]
    public bool StartupEnabled { get; init; }
    [System.Text.Json.Serialization.JsonRequired]
    public string IconChoice { get; init; } = "d";
    [System.Text.Json.Serialization.JsonRequired]
    public FolderRecord[] Folders { get; init; } = [];
    public FolderRecord? PendingCreate { get; init; }
    public PendingStartup? PendingStartup { get; init; }
    public PendingFolderChange? PendingFolderChange { get; init; }
    public PendingContentRename? PendingContentRename { get; init; }
    public RetainedFolder[] RetainedFolders { get; init; } = [];
    public PendingRootMigration? PendingRootMigration { get; init; }
}

public sealed record StateRead(WorkspaceState State, bool NeedsBackupRestore = false, string? Notice = null);
public interface IWorkspaceStore
{
    StateRead Read();
    void Save(WorkspaceState state);
    void RestoreBackup();
}
public interface IStartupRegistration
{
    string LaunchCommand { get; }
    string? ReadCommand();
    void WriteCommand(string? command);
}

// 仅 Windows Shell 能力与桌面位置可替换；目录操作和状态事务由业务模块负责。
public interface IFolderShell
{
    string DesktopDirectory { get; }
    void CreateShortcut(string shortcutPath, string targetPath);
    bool ShortcutTargets(string shortcutPath, string targetPath);
    string? ReadShortcutTarget(string shortcutPath, Guid folderId);
    void RetargetShortcut(string shortcutPath, string previousTarget, string targetPath, Guid folderId);
    OperationResult Recycle(string contentPath, Guid folderId);
    string? FindRecycledFolder(string contentPath, Guid folderId);
}

// 只替换指定目录的文件系统故障；默认适配器执行真实复制、字节核对和源移除。
public interface IRootDirectoryTransfer
{
    void Move(string source, string destination, CancellationToken cancellation);
    void Restore(string source, string destination, CancellationToken cancellation);
}
