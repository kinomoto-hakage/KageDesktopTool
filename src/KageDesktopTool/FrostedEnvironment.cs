using System.Collections.Generic;

namespace Kage.Desktop;

// Windows 平台事实；不属于保存的 Folder 样式。现代节能级别：0 关闭、1 标准、2 高度节能。
internal sealed record FrostedEnvironment(bool Available = true, bool TransparencyEnabled = true,
    bool HighContrast = false, uint EnergySaver = 0)
{
    internal bool AllowsFrosted => Available && TransparencyEnabled && !HighContrast && EnergySaver == 0;
    internal string Reason
    {
        get
        {
            var reasons = new List<string>();
            if (!Available || EnergySaver > 2) reasons.Add("当前环境无法显示毛玻璃");
            if (!TransparencyEnabled) reasons.Add("Windows 已关闭透明效果，毛玻璃暂停");
            if (HighContrast) reasons.Add("Windows 已开启高对比度，毛玻璃暂停");
            if (EnergySaver is 1 or 2) reasons.Add("Windows 已进入省电模式，毛玻璃暂停");
            return string.Join("\n", reasons);
        }
    }
}
