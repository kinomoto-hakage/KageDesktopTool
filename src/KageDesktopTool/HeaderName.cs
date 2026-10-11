using System.Globalization;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Kage.Workspace;

namespace Kage.Desktop;

// 按用户可见字符排版；省略时保留完整 ToolTip，不旋转字形或拆开组合字符。
internal sealed class HeaderName : FrameworkElement
{
    private string text = "";
    private NameOrientation orientation;
    private bool alignTop;
    internal void Set(string value, NameOrientation direction, bool side)
    {
        text = value; orientation = direction; alignTop = side; ToolTip = value;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        drawing.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        var elements = new List<string>();
        var iterator = StringInfo.GetTextElementEnumerator(text);
        while (iterator.MoveNext()) elements.Add(iterator.GetTextElement());
        FormattedText Format(string value) => new(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            14, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        drawing.PushClip(new RectangleGeometry(new Rect(RenderSize)));
        if (orientation == NameOrientation.Horizontal)
        {
            var value = text;
            while (elements.Count > 0 && Format(value).Width > ActualWidth)
            { elements.RemoveAt(elements.Count - 1); value = string.Concat(elements) + "…"; }
            var line = Format(value);
            drawing.DrawText(line, new Point(0, alignTop ? 0 : System.Math.Max(0, (ActualHeight - line.Height) / 2)));
        }
        else
        {
            const double lineHeight = 20;
            var capacity = (int)(ActualHeight / lineHeight);
            for (var i = 0; i < System.Math.Min(capacity, elements.Count); i++)
            {
                var glyph = Format(i == capacity - 1 && elements.Count > capacity ? "…" : elements[i]);
                drawing.DrawText(glyph, new Point(System.Math.Max(0, (ActualWidth - glyph.Width) / 2), i * lineHeight));
            }
        }
        drawing.Pop();
    }
}
