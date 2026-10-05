using System;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Kage.Workspace;
using Forms = System.Windows.Forms;

namespace Kage.Desktop;

internal sealed class StyleDialog : Window
{
    internal AppearanceInteraction Interaction { get; }
    internal TextBox Hex { get; }
    internal TextBox[] Rgb { get; }
    internal Slider Transparency { get; }
    internal TextBlock Error { get; }
    internal Button ApplyButton { get; }
    private readonly Runtime runtime;
    private readonly StackPanel root = new() { Margin = new Thickness(24) };
    private readonly Border swatch = new() { Height = 42, CornerRadius = new CornerRadius(7), Margin = new Thickness(0, 0, 0, 12) };
    private readonly TextBlock opacityLabel = new() { Margin = new Thickness(0, 0, 0, 8) };
    private bool syncing;
    private bool saving;

    internal StyleDialog(Runtime runtime, AppearanceInteraction interaction, string name)
    {
        this.runtime = runtime;
        Interaction = interaction;
        Title = "Folder 样式";
        Width = 430;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = Runtime.ApplicationIcon;
        Background = new SolidColorBrush(Color.FromRgb(30, 33, 39));
        Foreground = Brushes.White;
        Content = root;
        root.Children.Add(new TextBlock { Text = name, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        root.Children.Add(new TextBlock { Text = "背景颜色", FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 16) });
        root.Children.Add(swatch);
        var palette = new WrapPanel();
        foreach (var value in new[] { "#666666", "#3D434B", "#959BA3", "#4679AB", "#367F79", "#607C4A", "#947448", "#A55B63", "#785C96", "#4B5973", "#59514C", "#B69A67" })
        {
            var button = new Button { Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)), Width = 49, Height = 30, Margin = new Thickness(0, 0, 7, 7), ToolTip = value };
            button.Click += (_, _) => SelectColor(value);
            palette.Children.Add(button);
        }
        root.Children.Add(palette);
        var choose = new Button { Content = "打开调色盘…", Padding = new Thickness(12, 7, 12, 7), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 18) };
        choose.Click += (_, _) => ChooseColor();
        root.Children.Add(choose);
        var input = new Grid();
        input.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(136) });
        for (var i = 0; i < 3; i++) input.ColumnDefinitions.Add(new ColumnDefinition());
        Hex = Field(input, "HEX", 0);
        Rgb = [Field(input, "R", 1), Field(input, "G", 2), Field(input, "B", 3)];
        Error = new TextBlock { Foreground = Brushes.Salmon, TextWrapping = TextWrapping.Wrap, MinHeight = 26, Margin = new Thickness(0, 5, 0, 8) };
        Hex.TextChanged += (_, _) =>
        {
            if (syncing) return;
            if (Interaction.SetHex(Hex.Text).Succeeded) SyncColor();
            UpdatePreview();
        };
        foreach (var field in Rgb) field.TextChanged += (_, _) =>
        {
            if (syncing) return;
            if (Interaction.SetRgb(Rgb[0].Text, Rgb[1].Text, Rgb[2].Text).Succeeded) SyncColor();
            UpdatePreview();
        };
        root.Children.Add(input);
        root.Children.Add(Error);
        root.Children.Add(new TextBlock { Text = "背景透明度", Margin = new Thickness(0, 0, 0, 8) });
        Transparency = new Slider { Minimum = 0, Maximum = 100, Value = (1 - Interaction.Opacity) * 100, TickFrequency = 10, SmallChange = 1, LargeChange = 10 };
        Transparency.ValueChanged += (_, _) => { Interaction.SetOpacity(1 - Transparency.Value / 100); UpdatePreview(); };
        var opacityRow = new Grid { Margin = new Thickness(0, 0, 0, 22) };
        opacityRow.ColumnDefinitions.Add(new ColumnDefinition());
        opacityRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        opacityRow.Children.Add(Transparency);
        var labels = new StackPanel { Margin = new Thickness(14, 0, 0, 0), MinWidth = 70 };
        labels.Children.Add(opacityLabel);
        labels.Children.Add(new TextBlock { Text = "完全透明", Foreground = Brushes.LightGray, FontSize = 11 });
        Grid.SetColumn(labels, 1);
        opacityRow.Children.Add(labels);
        root.Children.Add(opacityRow);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "取消", IsCancel = true, Width = 86, Padding = new Thickness(8), Margin = new Thickness(0, 0, 10, 0) };
        cancel.Click += (_, _) => Close();
        ApplyButton = new Button { Content = "应用", IsDefault = true, Width = 86, Padding = new Thickness(8) };
        ApplyButton.Click += async (_, _) => await ApplyAsync();
        actions.Children.Add(cancel);
        actions.Children.Add(ApplyButton);
        root.Children.Add(actions);
        Closing += (_, e) => { if (saving && !runtime.Exiting) e.Cancel = true; };
        Closed += (_, _) =>
        {
            if (!Interaction.Closed) runtime.Workspace.CancelAppearance(Interaction);
            runtime.EndAppearance(this);
        };
        Loaded += (_, _) => { Hex.Focus(); Hex.SelectAll(); };
        SyncColor();
        UpdatePreview();
    }

    internal async Task ApplyAsync()
    {
        if (saving || Interaction.Error.Length != 0 || runtime.Exiting) return;
        saving = true;
        root.IsEnabled = false;
        try
        {
            var result = await runtime.Workspace.ApplyAppearanceAsync(Interaction);
            runtime.Complete("Folder 样式", result);
            if (runtime.Exiting) return;
            if (result.Succeeded) { saving = false; Close(); }
            else Error.Text = result.Message;
        }
        finally { saving = false; root.IsEnabled = true; }
    }

    internal void SelectColor(string value)
    {
        Interaction.SetHex(value);
        SyncColor();
        UpdatePreview();
    }

    internal void ChooseColor()
    {
        Activate();
        using var picker = new Forms.ColorDialog { FullOpen = true, AnyColor = true,
            Color = System.Drawing.Color.FromArgb(Interaction.Red, Interaction.Green, Interaction.Blue) };
        // 指定真实设置窗口为 owner，保证调色盘在桌面子窗口上方获取焦点。
        if (picker.ShowDialog(new DialogOwner(new WindowInteropHelper(this).Handle)) == Forms.DialogResult.OK)
            SelectColor($"#{picker.Color.R:X2}{picker.Color.G:X2}{picker.Color.B:X2}");
    }

    private sealed class DialogOwner(IntPtr handle) : Forms.IWin32Window
    {
        public IntPtr Handle => handle;
    }

    private void SyncColor()
    {
        syncing = true;
        try
        {
            Hex.Text = Interaction.Color;
            Rgb[0].Text = Interaction.Red.ToString(CultureInfo.InvariantCulture);
            Rgb[1].Text = Interaction.Green.ToString(CultureInfo.InvariantCulture);
            Rgb[2].Text = Interaction.Blue.ToString(CultureInfo.InvariantCulture);
            swatch.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(Interaction.Color));
        }
        finally { syncing = false; }
    }

    internal void UpdatePreview()
    {
        Error.Text = Interaction.Error;
        ApplyButton.IsEnabled = Interaction.Error.Length == 0;
        opacityLabel.Text = $"{Transparency.Value:0}%";
        runtime.PreviewAppearance(Interaction);
    }

    private static TextBox Field(Grid grid, string label, int column)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        panel.Children.Add(new TextBlock { Text = label, Foreground = Brushes.LightGray, FontSize = 11, Margin = new Thickness(0, 0, 0, 6) });
        var box = new TextBox { Padding = new Thickness(7), FontSize = 13 };
        panel.Children.Add(box);
        Grid.SetColumn(panel, column);
        grid.Children.Add(panel);
        return box;
    }
}
