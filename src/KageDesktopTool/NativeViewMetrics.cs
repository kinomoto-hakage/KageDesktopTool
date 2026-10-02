// 网格使用系统图标网格与标题字体；列表行高由本机原生 ListView 控件实测。
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using Forms = System.Windows.Forms;

namespace Kage.Desktop;

internal sealed record NativeViewMetrics(double GridWidth, double GridHeight, double GridIcon, double SmallIcon, double ListHeight)
{
    private static readonly Dictionary<uint, NativeViewMetrics> Cache = new();

    internal static NativeViewMetrics ForScale(double scale)
    {
        var dpi = (uint)Math.Round(scale * 96);
        if (Cache.TryGetValue(dpi, out var cached)) return cached;
        var large = GetSystemMetricsForDpi(11, dpi) / scale;
        var small = GetSystemMetricsForDpi(49, dpi) / scale;
        var width = GetSystemMetricsForDpi(38, dpi) / scale;
        var height = GetSystemMetricsForDpi(39, dpi) / scale;
        var row = MeasureListRow();
        var metrics = new NativeViewMetrics(Math.Max(width, large + 12), Math.Max(height, large + 36), large, small, Math.Max(row, small + 2 / scale));
        Cache[dpi] = metrics; return metrics;
    }

    private static double MeasureListRow()
    {
        using var view = new Forms.ListView { View = Forms.View.List, Size = new System.Drawing.Size(300, 120) };
        using var font = new System.Drawing.Font(SystemFonts.IconFontFamily.Source, (float)(SystemFonts.IconFontSize * 72 / 96));
        view.Font = font;
        var dpi = GetDpiForWindow(view.Handle);
        using var images = new Forms.ImageList { ImageSize = new System.Drawing.Size(GetSystemMetricsForDpi(49, dpi), GetSystemMetricsForDpi(50, dpi)) };
        using var icon = System.Drawing.SystemIcons.Application.ToBitmap(); images.Images.Add(icon);
        view.SmallImageList = images;
        view.Items.Add(new Forms.ListViewItem("文件一", 0)); view.Items.Add(new Forms.ListViewItem("文件二", 0));
        var first = view.GetItemRect(0);
        var second = view.GetItemRect(1);
        var physical = second.Top > first.Top ? second.Top - first.Top : first.Height;
        return physical > 0 ? physical * 96.0 / dpi : Math.Max(SystemFonts.IconFontSize + 6, 20);
    }

    [DllImport("user32.dll")] private static extern int GetSystemMetricsForDpi(int index, uint dpi);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
}

