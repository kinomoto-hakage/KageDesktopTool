namespace Kage.Workspace;

public enum ConflictChoice { Ask, KeepBoth, Skip, Cancel }
public enum Outcome { Success, Conflict, Skipped, Cancelled, Failed, RecoveryRequired }
public sealed record OperationResult(Outcome Outcome, string Message, string? ActualPath = null)
{
    public bool Succeeded => Outcome == Outcome.Success;
}

// 坐标及布局尺寸使用物理像素，WPF 适配器负责按屏幕 DPI 转换。
public sealed record DisplayArea(int X, int Y, int Width, int Height, double Scale = 1);
public sealed record FolderRecord(Guid Id, string Name, int X = 0, int Y = 0,
    double HeaderWidth = 300, double HeaderHeight = 48, double BodyHeight = 260,
    bool Expanded = false, bool Grid = true, string Color = "#666666", double Opacity = .68,
    string? ContentRoot = null);
public sealed record FolderSnapshot(FolderRecord Folder, string ActualPath, bool Visible, int? FileCount, string? Notice);
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
    Task<OperationResult> RefreshAsync(IReadOnlyList<DisplayArea> displays);
}

public sealed record PendingStartup(bool Enabled, string? PreviousCommand, string? TargetCommand);
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
