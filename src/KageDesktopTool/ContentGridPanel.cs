using System;
using System.Windows;
using System.Windows.Controls;

namespace Kage.Desktop;

// 固定格子参与排列；全文只增加内容末端，不能推开后续项目。
internal sealed class ContentGridPanel : WrapPanel
{
    internal static readonly DependencyProperty NameOverflowProperty = DependencyProperty.RegisterAttached(
        "NameOverflow", typeof(double), typeof(ContentGridPanel), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    protected override Size MeasureOverride(Size constraint)
    {
        var size = base.MeasureOverride(constraint);
        double x = 0, y = 0, rowHeight = 0, bottom = size.Height;
        foreach (UIElement child in InternalChildren)
        {
            if (x > 0 && x + child.DesiredSize.Width > constraint.Width)
            { y += rowHeight; x = 0; rowHeight = 0; }
            bottom = Math.Max(bottom, y + child.DesiredSize.Height + (double)child.GetValue(NameOverflowProperty));
            x += child.DesiredSize.Width;
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
        }
        return new Size(size.Width, bottom);
    }
}
