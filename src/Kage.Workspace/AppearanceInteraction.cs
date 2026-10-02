using System.Globalization;
using System.Text.RegularExpressions;

namespace Kage.Workspace;

// 外观草稿独立于已提交快照，预览不写状态、不枚举目录、不改变布局。
public sealed class AppearanceInteraction
{
    internal DesktopWorkspace Owner { get; }
    internal FolderRecord Original { get; }
    public Guid FolderId => Original.Id;
    public string Color { get; private set; }
    public double Opacity { get; private set; }
    public byte Red => byte.Parse(Color.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    public byte Green => byte.Parse(Color.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    public byte Blue => byte.Parse(Color.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    public bool Closed { get; private set; }
    private string colorError = "";
    private string opacityError = "";
    public string Error => string.Join(" ", new[] { colorError, opacityError }.Where(value => value.Length != 0));

    internal AppearanceInteraction(DesktopWorkspace owner, FolderRecord original)
    {
        Owner = owner;
        Original = original;
        Color = original.Color;
        Opacity = original.Opacity;
    }

    public OperationResult SetHex(string value)
    {
        if (Closed) return Ended();
        var text = value.Trim();
        colorError = Regex.IsMatch(text, "^#?[0-9a-fA-F]{6}$") ? "" : "HEX 格式为 #RRGGBB。";
        if (colorError.Length != 0) return new(Outcome.Failed, colorError);
        Color = "#" + text.TrimStart('#').ToUpperInvariant();
        return new(Outcome.Success, "颜色已预览。");
    }

    public OperationResult SetRgb(string red, string green, string blue)
    {
        if (Closed) return Ended();
        if (!byte.TryParse(red, NumberStyles.None, CultureInfo.InvariantCulture, out var r)
            || !byte.TryParse(green, NumberStyles.None, CultureInfo.InvariantCulture, out var g)
            || !byte.TryParse(blue, NumberStyles.None, CultureInfo.InvariantCulture, out var b))
        {
            colorError = "RGB 各分量必须为 0–255 的整数。";
            return new(Outcome.Failed, colorError);
        }
        return SetHex($"#{r:X2}{g:X2}{b:X2}");
    }

    public OperationResult SetOpacity(double value)
    {
        if (Closed) return Ended();
        opacityError = double.IsFinite(value) && value is >= 0 and <= 1 ? "" : "背景不透明度必须在 0–1 之间。";
        if (opacityError.Length != 0) return new(Outcome.Failed, opacityError);
        Opacity = value;
        return new(Outcome.Success, "透明度已预览。");
    }

    internal void Finish(bool applied)
    {
        if (!applied) { Color = Original.Color; Opacity = Original.Opacity; }
        Closed = true;
    }

    internal static OperationResult Ended() => new(Outcome.Failed, "外观会话已结束，请重新打开。");
}
