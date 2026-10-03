// 网格使用系统图标网格与标题字体；列表行高由本机原生 ListView 控件实测。
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace Kage.Desktop;

internal sealed record NativeViewMetrics(double GridWidth, double GridHeight, double GridIcon, double SmallIcon, double ListHeight,
    string FontFamily, double FontSize, bool Bold, bool Italic)
{
    private static readonly Dictionary<uint, NativeViewMetrics> Cache = new();
    internal static int Revision { get; private set; }
    internal static void Invalidate() { Cache.Clear(); Revision++; }

    internal static NativeViewMetrics ForScale(double scale)
    {
        var dpi = (uint)Math.Round(scale * 96);
        if (Cache.TryGetValue(dpi, out var cached)) return cached;
        var large = GetSystemMetricsForDpi(11, dpi) / scale;
        var small = GetSystemMetricsForDpi(49, dpi) / scale;
        var width = GetSystemMetricsForDpi(38, dpi) / scale;
        var height = GetSystemMetricsForDpi(39, dpi) / scale;
        if (!SystemParametersInfoForDpi(0x1F, (uint)Marshal.SizeOf<LOGFONT>(), out var font, 0, dpi))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "无法读取当前 DPI 的系统标题字体。");
        var fontSize = Math.Abs(font.Height) / scale;
        var row = MeasureListRow(dpi, font);
        var metrics = new NativeViewMetrics(Math.Max(width, large + 12), Math.Max(height, large + 36), large, small,
            Math.Max(row / scale, small + 2 / scale), font.FaceName, fontSize, font.Weight >= 700, font.Italic != 0);
        Cache[dpi] = metrics; return metrics;
    }

    private static double MeasureListRow(uint dpi, LOGFONT logicalFont)
    {
        // 控件的字体和小图标明确使用目标 DPI 像素，不能测量默认主屏控件后复用所有屏幕。
        var display = Array.Find(WindowsDesktop.Displays(), area => Math.Abs(area.Scale * 96 - dpi) < .5);
        using var owner = new Forms.Form
        {
            StartPosition = Forms.FormStartPosition.Manual,
            Location = new System.Drawing.Point((display?.X ?? 0) + 1, (display?.Y ?? 0) + 1),
            Size = new System.Drawing.Size(600, 300), AutoScaleMode = Forms.AutoScaleMode.None,
            ShowInTaskbar = false
        };
        // 不显示测量窗口。创建在目标屏的父 HWND，让原生控件内部度量也采用该屏 DPI。
        _ = owner.Handle;
        using var view = new Forms.ListView { View = Forms.View.List, Size = new System.Drawing.Size(600, 300) };
        owner.Controls.Add(view);
        var style = (logicalFont.Weight >= 700 ? System.Drawing.FontStyle.Bold : System.Drawing.FontStyle.Regular)
            | (logicalFont.Italic != 0 ? System.Drawing.FontStyle.Italic : System.Drawing.FontStyle.Regular);
        using var font = new System.Drawing.Font(logicalFont.FaceName, Math.Abs(logicalFont.Height), style, System.Drawing.GraphicsUnit.Pixel);
        view.Font = font;
        using var images = new Forms.ImageList { ImageSize = new System.Drawing.Size(GetSystemMetricsForDpi(49, dpi), GetSystemMetricsForDpi(50, dpi)) };
        using var icon = System.Drawing.SystemIcons.Application.ToBitmap(); images.Images.Add(icon);
        view.SmallImageList = images;
        view.Items.Add(new Forms.ListViewItem("文件一", 0)); view.Items.Add(new Forms.ListViewItem("文件二", 0));
        var first = view.GetItemRect(0);
        var second = view.GetItemRect(1);
        var physical = second.Top > first.Top ? second.Top - first.Top : first.Height;
        if (physical <= 0) throw new InvalidOperationException("原生 ListView 未返回有效行高。");
        return physical;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct LOGFONT
    {
        internal int Height, Width, Escapement, Orientation, Weight;
        internal byte Italic, Underline, StrikeOut, FontCharSet, OutPrecision, ClipPrecision, Quality, PitchAndFamily;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] internal string FaceName;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfoForDpi(uint action, uint size, out LOGFONT font, uint flags, uint dpi);

    [DllImport("user32.dll")] private static extern int GetSystemMetricsForDpi(int index, uint dpi);
}

