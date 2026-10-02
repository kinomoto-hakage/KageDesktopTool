using System;
using System.Windows.Media.Imaging;

namespace Kage.Desktop;

internal static class IconChoices
{
    internal static readonly (string Key, string Name)[] Options =
        [("a", "A · 收纳夹"), ("b", "B · 分区格"), ("c", "C · 叠层抽屉"), ("d", "D · K 文件夹")];

    internal static BitmapImage Image(string key)
    {
        var image = new BitmapImage(new Uri($"pack://application:,,,/Assets/app-{key}.png"));
        image.Freeze();
        return image;
    }
}
