using System.Globalization;
using System.Text.RegularExpressions;

namespace Kage.Workspace;

// 外观和几何构成一个有效草稿；预览不写正式状态、不枚举目录。
public sealed class AppearanceInteraction
{
    internal DesktopWorkspace Owner { get; }
    internal FolderRecord Original { get; }
    internal DisplayArea[] Displays { get; }
    public FolderRecord Draft { get; private set; }
    public FolderSnapshot Preview => Owner.AppearancePreview(this);
    internal bool GeometryChanged => Draft.HeaderPosition != Original.HeaderPosition
        || Draft.HeaderHeight != Original.HeaderHeight || Draft.SideHeaderWidth != Original.SideHeaderWidth || Draft.X != Original.X || Draft.Y != Original.Y;
    public Guid FolderId => Original.Id;
    public string Color => Draft.Color;
    public double Opacity => Draft.Opacity;
    public bool Frosted => Draft.Frosted;
    public int FrostedStrength => Draft.FrostedStrength;
    public byte Red => byte.Parse(Color.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    public byte Green => byte.Parse(Color.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    public byte Blue => byte.Parse(Color.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    public bool Closed { get; private set; }
    private string colorError = "";
    private string opacityError = "";
    private string strengthError = "";
    public string Error => string.Join(" ", new[] { colorError, opacityError, strengthError }.Where(value => value.Length != 0));

    internal AppearanceInteraction(DesktopWorkspace owner, FolderRecord original, DisplayArea[] displays)
    {
        Owner = owner;
        Original = original;
        Draft = original;
        Displays = displays.ToArray();
    }

    public OperationResult SetHex(string value)
    {
        if (Closed) return Ended();
        var text = value.Trim();
        colorError = Regex.IsMatch(text, "^#?[0-9a-fA-F]{6}$") ? "" : "HEX 格式为 #RRGGBB。";
        if (colorError.Length != 0) return new(Outcome.Failed, colorError);
        Draft = Draft with { Color = "#" + text.TrimStart('#').ToUpperInvariant() };
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
        Draft = Draft with { Opacity = value };
        return new(Outcome.Success, "透明度已预览。");
    }

    public OperationResult SetFrosted(bool value)
    {
        if (Closed) return Ended();
        Draft = Draft with { Frosted = value };
        return new(Outcome.Success, "毛玻璃已预览。");
    }

    public OperationResult SetFrostedStrength(int value)
    {
        if (Closed) return Ended();
        strengthError = value is >= 0 and <= 100 ? "" : "模糊强度必须为 0–100 的整数。";
        if (strengthError.Length != 0) return new(Outcome.Failed, strengthError);
        Draft = Draft with { FrostedStrength = value };
        return new(Outcome.Success, "模糊强度已预览。");
    }

    internal void Finish(bool applied)
    {
        if (!applied) Draft = Original;
        Closed = true;
    }

    public OperationResult SetHeaderPosition(HeaderPosition value)
        => Enum.IsDefined(value) ? ChangeLayout(Draft with { HeaderPosition = value }) : new(Outcome.Failed, "头部位置无效。");

    public OperationResult SetSideHeaderWidth(double value)
        => double.IsFinite(value) && value is >= 64 and <= 360
            ? ChangeLayout(Draft with { SideHeaderWidth = value }) : new(Outcome.Failed, "侧栏宽度必须为 64–360 DIP。");

    public OperationResult SetHeaderHeight(double value)
        => double.IsFinite(value) && value is >= 42 and <= 82
            ? ChangeLayout(Draft with { HeaderHeight = value }) : new(Outcome.Failed, "头部高度必须为 42–82 DIP。");

    public OperationResult SetNameOrientation(NameOrientation value)
    {
        if (Closed) return Ended();
        if (!Enum.IsDefined(value)) return new(Outcome.Failed, "名称方向无效。");
        var valid = Owner.ValidateAppearance(this);
        if (!valid.Succeeded) return valid;
        Draft = Draft with { NameOrientation = value };
        return new(Outcome.Success, "名称方向已预览。");
    }

    private OperationResult ChangeLayout(FolderRecord candidate)
    {
        if (Closed) return Ended();
        var result = Owner.PlaceAppearance(this, candidate, out var placed);
        if (result.Succeeded) Draft = placed!;
        return result;
    }

    internal static OperationResult Ended() => new(Outcome.Failed, "外观会话已结束，请重新打开。");
}
