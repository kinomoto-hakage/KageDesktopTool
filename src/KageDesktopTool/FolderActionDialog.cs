using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Kage.Workspace;

namespace Kage.Desktop;

internal sealed class FolderActionDialog : Window
{
    private readonly Runtime runtime;
    private readonly Guid folderId;
    private readonly bool rename;
    private bool busy;
    private bool completed;
    internal TextBox NameInput { get; }
    internal TextBlock ResultText { get; }
    internal Button RenameButton { get; }
    internal Button KeepButton { get; }
    internal Button RecycleButton { get; }
    internal Button CancelButton { get; }
    internal Task<OperationResult>? Pending { get; private set; }

    internal FolderActionDialog(Runtime runtime, FolderSnapshot folder, bool rename)
    {
        this.runtime = runtime;
        this.rename = rename;
        folderId = folder.Folder.Id;
        Title = rename ? "重命名 Folder" : "删除 Folder";
        Width = 530;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = Runtime.ApplicationIcon;
        var body = new StackPanel { Margin = new Thickness(20) };
        body.Children.Add(new TextBlock { Text = rename ? "新名称将同步用于真实内容文件夹。" : $"{folder.Folder.Name}\n保留内容：文件留在原目录，并在桌面生成快捷方式。\n一同删除：整个内容文件夹及其中内容进入 Windows 回收站。", TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = folder.ActualPath, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 10) });
        NameInput = new TextBox { Text = folder.Folder.Name, Visibility = rename ? Visibility.Visible : Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 12) };
        body.Children.Add(NameInput);
        ResultText = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        body.Children.Add(ResultText);
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        RenameButton = Button("重命名", rename);
        RenameButton.IsDefault = rename;
        KeepButton = Button("保留内容并生成快捷方式", !rename);
        RecycleButton = Button("一同删除到回收站", !rename);
        CancelButton = Button("取消", true);
        RenameButton.Click += (_, _) => Start(FolderDeleteChoice.Cancel);
        KeepButton.Click += (_, _) => Start(FolderDeleteChoice.KeepContents);
        RecycleButton.Click += (_, _) => Start(FolderDeleteChoice.Recycle);
        CancelButton.Click += (_, _) => Close();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Escape) return;
            if (!busy) Close();
            e.Handled = true;
        };
        foreach (var button in new[] { RenameButton, KeepButton, RecycleButton, CancelButton }) buttons.Children.Add(button);
        body.Children.Add(buttons);
        Content = body;
        Loaded += (_, _) => { if (rename) { NameInput.Focus(); NameInput.SelectAll(); } else CancelButton.Focus(); };
        Closing += (_, e) => { if (busy) e.Cancel = true; };
    }

    private static Button Button(string label, bool visible) => new()
    {
        Content = label, Visibility = visible ? Visibility.Visible : Visibility.Collapsed,
        Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(4)
    };

    private void Start(FolderDeleteChoice choice)
    {
        if (busy || completed) return;
        Pending = ApplyAsync(choice);
    }

    private async Task<OperationResult> ApplyAsync(FolderDeleteChoice choice)
    {
        busy = true;
        Enable(false);
        ResultText.Text = "正在核对真实目录并执行…";
        try
        {
            var name = NameInput.Text;
            Task<OperationResult> Execute(ConflictChoice conflict) => rename
                ? runtime.Workspace.RenameFolderAsync(folderId, name, conflict)
                : runtime.Workspace.DeleteFolderAsync(folderId, choice, conflict);
            var result = await Execute(ConflictChoice.Ask);
            if (result.Outcome == Outcome.Conflict)
            {
                var answer = MessageBox.Show(this, result.Message + "\n\n是：保留两份并自动编号\n否：跳过\n取消：取消操作", "同名冲突", MessageBoxButton.YesNoCancel);
                result = await Execute(answer switch { MessageBoxResult.Yes => ConflictChoice.KeepBoth, MessageBoxResult.No => ConflictChoice.Skip, _ => ConflictChoice.Cancel });
            }
            ResultText.Text = result.Message + (result.ActualPath == null ? "" : $"\n实际位置：{result.ActualPath}");
            completed = result.Succeeded || result.Outcome == Outcome.RecoveryRequired;
            if (completed) CancelButton.Content = "关闭";
            return result;
        }
        catch (Exception e)
        {
            ResultText.Text = $"操作失败：{e.Message}。请核对实际路径后重试。";
            return new(Outcome.Failed, ResultText.Text);
        }
        finally { busy = false; Enable(true); runtime.Render(); }
    }

    private void Enable(bool enabled)
    {
        NameInput.IsEnabled = enabled && !completed;
        RenameButton.IsEnabled = KeepButton.IsEnabled = RecycleButton.IsEnabled = enabled && !completed;
        CancelButton.IsEnabled = enabled;
    }
}
