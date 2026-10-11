namespace Kage.Workspace;

// X/Y 始终是头部左上角。共享边界负责业务放置、DPI 选择和 HWND 定位。
public sealed record FolderGeometry(int X, int Y, int Width, int Height, double DipWidth, double DipHeight)
{
    public static bool Side(FolderRecord folder) => folder.HeaderPosition is HeaderPosition.Left or HeaderPosition.Right;
    public static FolderGeometry For(FolderRecord folder, double scale)
    {
        var side = Side(folder);
        var width = side ? folder.SideHeaderWidth + (folder.Expanded ? folder.HeaderWidth : 0) : folder.HeaderWidth;
        var height = side ? folder.BodyHeight : folder.HeaderHeight + (folder.Expanded ? folder.BodyHeight : 0);
        return new(folder.X - (folder.Expanded && folder.HeaderPosition == HeaderPosition.Right ? Pixels(folder.HeaderWidth, scale) : 0),
            folder.Y - (folder.Expanded && folder.HeaderPosition == HeaderPosition.Bottom ? Pixels(folder.BodyHeight, scale) : 0),
            Pixels(width, scale), Pixels(height, scale), width, height);
    }

    private static int Pixels(double value, double scale) => (int)Math.Ceiling(value * scale);
}
