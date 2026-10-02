using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Kage.Workspace;

namespace Kage.Desktop;

internal sealed class FolderHeader : Window
{
    internal Guid FolderId { get; }
    internal IntPtr Handle => new WindowInteropHelper(this).Handle;
    internal IntPtr Host { get; private set; }
    private readonly TextBlock title;
    private readonly TextBlock count;
    internal Border Surface { get; }

    internal FolderHeader(FolderSnapshot folder)
    {
        FolderId = folder.Folder.Id;
        Title = "Kage · " + folder.Folder.Name;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Icon = Runtime.ApplicationIcon;
        UseLayoutRounding = true;
        Surface = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromArgb(55, 255, 255, 255)) };
        var header = new Grid { Margin = new Thickness(14, 0, 12, 0) };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        title = new TextBlock { Foreground = Brushes.White, FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        count = new TextBlock { Foreground = Brushes.WhiteSmoke, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 8, 0) };
        // 完整展示部分及展开布局由 05 提供，本票交付真实目录和折叠头部。
        var expand = new Button { Content = "▾", FontSize = 20, Foreground = Brushes.White, Background = Brushes.Transparent,
            BorderThickness = new Thickness(0), Padding = new Thickness(1), ToolTip = "打开内容文件夹" };
        expand.Click += (_, _) => Runtime.Open(folder.ActualPath);
        Grid.SetColumn(count, 1);
        Grid.SetColumn(expand, 2);
        header.Children.Add(title);
        header.Children.Add(count);
        header.Children.Add(expand);
        Surface.Child = header;
        Content = Surface;
        var menu = new ContextMenu();
        var open = new MenuItem { Header = "打开内容文件夹" };
        open.Click += (_, _) => Runtime.Open(folder.ActualPath);
        var settings = new MenuItem { Header = "设置" };
        settings.Click += (_, _) => Runtime.Current.ShowSettings();
        menu.Items.Add(open);
        menu.Items.Add(settings);
        ContextMenu = menu;
        SourceInitialized += (_, _) => Attach();
        Update(folder);
    }

    internal void Attach()
    {
        Host = WindowsDesktop.Host();
        if (Host == IntPtr.Zero) return;
        var style = WindowsDesktop.GetWindowLongPtr(Handle, -16).ToInt64();
        WindowsDesktop.SetWindowLongPtr(Handle, -16, new IntPtr((style & ~0x80000000L) | 0x40000000L));
        WindowsDesktop.SetParent(Handle, Host);
        if (WindowsDesktop.GetParent(Handle) != Host) Host = IntPtr.Zero;
    }

    internal void Update(FolderSnapshot folder)
    {
        Width = folder.Folder.HeaderWidth;
        Height = folder.Folder.HeaderHeight;
        title.Text = folder.Folder.Name;
        count.Text = folder.FileCount?.ToString() ?? "?";
        ToolTip = folder.Notice ?? folder.ActualPath;
        var color = (Color)ColorConverter.ConvertFromString(folder.Folder.Color);
        color.A = (byte)(folder.Folder.Opacity * 255);
        Surface.Background = new SolidColorBrush(color);
        if (Handle == IntPtr.Zero) return;
        if (!WindowsDesktop.IsWindow(Host)) Attach();
        if (!folder.Visible || Host == IntPtr.Zero) { Hide(); return; }
        var area = Array.Find(WindowsDesktop.Displays(), area => folder.Folder.X >= area.X && folder.Folder.X < area.X + area.Width
            && folder.Folder.Y >= area.Y && folder.Folder.Y < area.Y + area.Height);
        var scale = area?.Scale ?? 1;
        var point = new WindowsDesktop.POINT { X = folder.Folder.X, Y = folder.Folder.Y };
        WindowsDesktop.ScreenToClient(Host, ref point);
        Show();
        WindowsDesktop.SetWindowPos(Handle, IntPtr.Zero, point.X, point.Y, (int)Math.Ceiling(Width * scale), (int)Math.Ceiling(Height * scale), 0x10 | 0x40);
    }
}
