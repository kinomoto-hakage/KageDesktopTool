using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace Kage.DesktopFolderPrototype;

internal sealed class StyleDialog : Window
{
    private readonly FolderWindow folder;
    private readonly string originalColor;
    private readonly double originalOpacity;
    private readonly TextBox hex;
    private readonly TextBox[] rgb;
    private readonly Border swatch;
    private readonly TextBlock error;
    private readonly TextBlock opacityLabel;
    private bool syncing;
    private bool accepted;

    internal StyleDialog(FolderWindow folder)
    {
        this.folder = folder; originalColor = folder.State.Color; originalOpacity = folder.State.Opacity;
        Title = $"{folder.State.Name} · 外观"; Width = 420; SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(30, 33, 39)); Foreground = Brushes.White;
        Icon = IconChoices.ApplicationImage;
        var root = new StackPanel { Margin = new Thickness(24) };
        root.Children.Add(new TextBlock { Text = "背景颜色", FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 16) });
        swatch = new Border { Height = 45, CornerRadius = new CornerRadius(7), Margin = new Thickness(0, 0, 0, 14) }; root.Children.Add(swatch);
        var palette = new WrapPanel();
        foreach (var value in new[] { "#666666", "#3D434B", "#959BA3", "#4679AB", "#367F79", "#607C4A", "#947448", "#A55B63", "#785C96", "#4B5973", "#59514C", "#B69A67" })
        {
            var button = new Button { Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)), Width = 49, Height = 30, Margin = new Thickness(0, 0, 7, 7), BorderBrush = new SolidColorBrush(Color.FromRgb(120, 125, 135)), ToolTip = value };
            button.Click += (_, _) => Update((Color)ColorConverter.ConvertFromString(value)); palette.Children.Add(button);
        }
        root.Children.Add(palette);
        var choose = new Button { Content = "打开调色盘…", Padding = new Thickness(12, 7, 12, 7), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 18) };
        choose.Click += (_, _) =>
        {
            var color = (Color)ColorConverter.ConvertFromString(folder.State.Color);
            using var picker = new Forms.ColorDialog { FullOpen = true, AnyColor = true, Color = System.Drawing.Color.FromArgb(color.R, color.G, color.B) };
            if (picker.ShowDialog() == Forms.DialogResult.OK) Update(Color.FromRgb(picker.Color.R, picker.Color.G, picker.Color.B));
        };
        root.Children.Add(choose);
        var input = new Grid();
        input.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(136) });
        for (var i = 0; i < 3; i++) input.ColumnDefinitions.Add(new ColumnDefinition());
        hex = Field(input, "HEX", 0);
        rgb = new[] { Field(input, "R", 1), Field(input, "G", 2), Field(input, "B", 3) };
        error = new TextBlock { Foreground = Brushes.Salmon, FontSize = 11, Height = 26, Margin = new Thickness(0, 5, 0, 8) };
        hex.TextChanged += (_, _) =>
        {
            if (syncing) return;
            if (!Regex.IsMatch(hex.Text.Trim(), "^#?[0-9a-fA-F]{6}$")) { error.Text = "HEX 格式为 #RRGGBB。"; return; }
            Update((Color)ColorConverter.ConvertFromString("#" + hex.Text.Trim().TrimStart('#')));
        };
        foreach (var field in rgb) field.TextChanged += (_, _) =>
        {
            if (syncing) return;
            var values = new byte[3];
            for (var i = 0; i < 3; i++)
                if (!byte.TryParse(rgb[i].Text, NumberStyles.None, CultureInfo.InvariantCulture, out values[i])) { error.Text = "RGB 各分量必须为 0–255 的整数。"; return; }
            Update(Color.FromRgb(values[0], values[1], values[2]));
        };
        root.Children.Add(input);
        root.Children.Add(error);
        opacityLabel = new TextBlock { FontSize = 14, Margin = new Thickness(0, 0, 0, 9) }; root.Children.Add(opacityLabel);
        var opacity = new Slider { Minimum = 0, Maximum = 100, Value = (1 - folder.State.Opacity) * 100, TickFrequency = 10, SmallChange = 1, LargeChange = 10 };
        opacity.ValueChanged += (_, _) => { folder.State.Opacity = 1 - opacity.Value / 100; folder.ApplyStyle(); OpacityLabel(); };
        root.Children.Add(opacity);
        root.Children.Add(new TextBlock { Text = "不透明                                      完全透明", Foreground = Brushes.LightGray, FontSize = 11, Margin = new Thickness(0, 7, 0, 22) });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "取消", IsCancel = true, Width = 86, Padding = new Thickness(8), Margin = new Thickness(0, 0, 10, 0) };
        var apply = new Button { Content = "应用", IsDefault = true, Width = 86, Padding = new Thickness(8) };
        apply.Click += (_, _) => { if (error.Text.Length != 0) return; accepted = true; Program.Save(); DialogResult = true; };
        actions.Children.Add(cancel); actions.Children.Add(apply); root.Children.Add(actions); Content = root;
        Closed += (_, _) => { if (!accepted) { folder.State.Color = originalColor; folder.State.Opacity = originalOpacity; folder.ApplyStyle(); Program.Save(); } };
        Update((Color)ColorConverter.ConvertFromString(folder.State.Color)); OpacityLabel();
    }

    private void OpacityLabel() => opacityLabel.Text = $"背景透明度   {(1 - folder.State.Opacity) * 100:0}% · 实时预览";

    private static TextBox Field(Grid grid, string name, int column)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        panel.Children.Add(new TextBlock { Text = name, Foreground = Brushes.LightGray, FontSize = 11, Margin = new Thickness(0, 0, 0, 6) });
        var box = new TextBox { Padding = new Thickness(7), FontSize = 13 }; panel.Children.Add(box); Grid.SetColumn(panel, column); grid.Children.Add(panel); return box;
    }

    private void Update(Color color)
    {
        syncing = true;
        try
        {
            folder.State.Color = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            hex.Text = folder.State.Color;
            rgb[0].Text = color.R.ToString(); rgb[1].Text = color.G.ToString(); rgb[2].Text = color.B.ToString();
            swatch.Background = new SolidColorBrush(color); error.Text = ""; folder.ApplyStyle();
        }
        finally { syncing = false; }
    }
}
