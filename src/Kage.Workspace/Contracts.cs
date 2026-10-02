using System.Text.Json.Serialization;

namespace Kage.Workspace;

public enum ConflictChoice { Ask, KeepBoth, Skip, Cancel }
public enum Outcome { Success, Conflict, Skipped, Cancelled, Failed, RecoveryRequired }
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
    string? ContentRoot = null, bool LayoutHidden = false);
public sealed record ContentEntry(string ActualPath, string Name, bool IsDirectory, long ModifiedTicks, long Length);
public sealed record FolderSnapshot(FolderRecord Folder, string ActualPath, bool Visible, int? FileCount, string? Notice)
{
    public IReadOnlyList<ContentEntry> Entries { get; init; } = [];
}
public sealed record WorkspaceSnapshot(string Root, bool StartupEnabled, string IconChoice,
    IReadOnlyList<FolderSnapshot> Folders, bool RecoveryRequired, IReadOnlyList<string> Notices);

public interface IDesktopWorkspace
{
    WorkspaceSnapshot Snapshot { get; }
    Task<OperationResult> InitializeAsync(IReadOnlyList<DisplayArea> displays);
    Task<OperationResult> SelectRootAsync(string root);
    Task<OperationResult> CreateFolderAsync(string name, ConflictChoice conflict = ConflictChoice.Ask, CancellationToken cancellation = default);
    Task<OperationResult> SetStartupAsync(bool enabled);
    Task<OperationResult> RestoreBackupAsync();
    Task<OperationResult> ConfirmPendingCreateAsync();
    Task<OperationResult> RefreshAsync(IReadOnlyList<DisplayArea> displays);
    Task<OperationResult> ToggleFolderAsync(Guid id);
    LayoutInteraction? BeginLayout(Guid id);
    Task<OperationResult> CommitLayoutAsync(LayoutInteraction interaction);
    Task<OperationResult> SetViewAsync(Guid id, bool grid);
}

public sealed record PendingStartup([property: JsonRequired] bool Enabled,
    [property: JsonRequired] string? PreviousCommand, [property: JsonRequired] string? TargetCommand);
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
