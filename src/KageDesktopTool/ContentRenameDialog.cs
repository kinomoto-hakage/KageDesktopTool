using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Kage.Desktop;

// Shell 的重命名命令需要宿主提供编辑界面，实际副作用沿用持久化业务接口。
internal sealed class ContentRenameDialog : Window
{
    internal ContentRenameDialog(Guid folderId, string path)
    {
        Title = "重命名项目";
        Width = 460; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; Icon = Runtime.ApplicationIcon;
        var body = new StackPanel { Margin = new Thickness(20) };
        body.Children.Add(new TextBlock { Text = path, TextWrapping = TextWrapping.Wrap });
        var input = new TextBox { Text = Path.GetFileName(path), Margin = new Thickness(0, 12, 0, 12) };
        var result = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var save = new Button { Content = "重命名", IsDefault = true, Padding = new Thickness(12, 6, 12, 6) };
        var cancel = new Button { Content = "取消", IsCancel = true, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(12, 6, 12, 6) };
        var busy = false;
        save.Click += async (_, _) =>
        {
            busy = true; save.IsEnabled = cancel.IsEnabled = input.IsEnabled = false;
            try
            {
                var outcome = await Runtime.Current.Workspace.RenameContentAsync(folderId, path, input.Text);
                Runtime.Current.Complete("重命名项目", outcome);
                if (outcome.Succeeded) { busy = false; Close(); return; }
                result.Text = outcome.Message;
            }
            finally { busy = false; save.IsEnabled = cancel.IsEnabled = input.IsEnabled = true; Runtime.Current.Render(); }
        };
        cancel.Click += (_, _) => Close();
        Closing += (_, e) => { if (busy) e.Cancel = true; };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && !busy) { Close(); e.Handled = true; } };
        buttons.Children.Add(save); buttons.Children.Add(cancel);
        body.Children.Add(input); body.Children.Add(result); body.Children.Add(buttons); Content = body;
        Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
    }
}
