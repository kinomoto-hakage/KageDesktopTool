using Kage.Workspace;
using System.Security.AccessControl;
using System.Security.Principal;

internal static class MoveChecks
{
    internal static async Task RealContents()
    {
        using var fixture = new Fixture();
        IDesktopWorkspace workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
        await workspace.InitializeAsync([new(0, 0, 1600, 1000)]);
        await workspace.SelectRootAsync(fixture.Content);
        await workspace.CreateFolderAsync("移入");
        var folder = workspace.Snapshot.Folders.Single();
        var file = Path.Combine(fixture.Home, "任意格式.custom");
        var link = Path.Combine(fixture.Home, "快捷方式.lnk");
        var directory = Path.Combine(fixture.Home, "普通子文件夹");
        File.WriteAllBytes(file, [0, 1, 255, 42]);
        File.WriteAllBytes(link, [76, 0, 0, 0]);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "内容.txt"), "子文件夹内容");
        var result = await workspace.MoveAsync([file, link, directory], MoveTarget.Folder(folder.Folder.Id));
        Check(result.Items.Count == 3 && result.Items.All(item => item.Outcome == Outcome.Success), "逐项真实移动成功");
        Check(!File.Exists(file) && !File.Exists(link) && !Directory.Exists(directory), "源项目实际消失");
        Check(File.ReadAllBytes(result.Items[0].ActualPath!).SequenceEqual(new byte[] { 0, 1, 255, 42 }), "不限制扩展名并保持字节内容");
        Check(File.ReadAllBytes(result.Items[1].ActualPath!).SequenceEqual(new byte[] { 76, 0, 0, 0 }), "快捷方式作为文件移动");
        Check(File.ReadAllText(Path.Combine(result.Items[2].ActualPath!, "内容.txt")) == "子文件夹内容", "普通子文件夹不转为桌面 Folder");
        Check(workspace.Snapshot.Folders.Count == 1 && workspace.Snapshot.Folders.Single().FileCount == 2, "展示与实际文件数一致");
        var destination = Path.Combine(fixture.Home, "移出");
        Directory.CreateDirectory(destination);
        var outgoing = await workspace.MoveAsync(result.Items.Select(item => item.ActualPath!).ToArray(), MoveTarget.Directory(destination));
        Check(outgoing.Items.All(item => item.Outcome == Outcome.Success) && workspace.Snapshot.Folders.Single().Entries.Count == 0, "双向批量移动同步展示");
        Check(File.ReadAllBytes(Path.Combine(destination, "任意格式.custom")).SequenceEqual(new byte[] { 0, 1, 255, 42 }), "移出保持字节");
    }

    internal static async Task ConflictsAndCancel()
    {
        using var fixture = new Fixture();
        IDesktopWorkspace workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
        await workspace.InitializeAsync([new(0, 0, 1600, 1000)]);
        await workspace.SelectRootAsync(fixture.Content);
        await workspace.CreateFolderAsync("目标");
        var folder = workspace.Snapshot.Folders.Single();
        var source = Path.Combine(fixture.Home, "报告.txt");
        File.WriteAllText(source, "新内容");
        File.WriteAllText(Path.Combine(folder.ActualPath, "报告.txt"), "原内容");
        File.WriteAllText(Path.Combine(folder.ActualPath, "报告 (2).txt"), "已有编号");
        var result = await workspace.MoveAsync([source], MoveTarget.Folder(folder.Folder.Id), _ => Task.FromResult(ConflictChoice.KeepBoth));
        Check(result.Items.Single().ActualPath == Path.Combine(folder.ActualPath, "报告 (3).txt") && File.ReadAllText(result.Items[0].ActualPath!) == "新内容", "保留两份编号放在扩展名前");
        Check(File.ReadAllText(Path.Combine(folder.ActualPath, "报告.txt")) == "原内容", "绝不静默覆盖");
        var names = new[] { "先完成.txt", "跳过.lnk", "取消目录", "后续.txt" };
        var sources = names.Select(name => Path.Combine(fixture.Home, name)).ToArray();
        File.WriteAllText(sources[0], "先完成");
        File.WriteAllText(sources[1], "源快捷方式字节");
        File.WriteAllText(Path.Combine(folder.ActualPath, names[1]), "现有快捷方式字节");
        Directory.CreateDirectory(sources[2]);
        Directory.CreateDirectory(Path.Combine(folder.ActualPath, names[2]));
        File.WriteAllText(sources[3], "不执行");
        result = await workspace.MoveAsync(sources, MoveTarget.Folder(folder.Folder.Id), conflict =>
            Task.FromResult(conflict.IsDirectory ? ConflictChoice.Cancel : ConflictChoice.Skip));
        Check(result.Items.Select(item => item.Outcome).SequenceEqual(new[] { Outcome.Success, Outcome.Skipped, Outcome.Cancelled, Outcome.Cancelled }), "逐项列出成功跳过取消并停止后续");
        Check(!File.Exists(sources[0]) && File.ReadAllText(sources[1]) == "源快捷方式字节" && Directory.Exists(sources[2]) && File.Exists(sources[3]), "取消保留已移动和未执行内容");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        result = await workspace.MoveAsync([sources[3]], MoveTarget.Folder(folder.Folder.Id), cancellation: cancellation.Token);
        Check(result.Items.Single().Outcome == Outcome.Cancelled && File.Exists(sources[3]), "取消令牌不产生移动");
        result = await workspace.MoveAsync([sources[1], sources[3]], MoveTarget.Folder(folder.Folder.Id));
        Check(result.Items[0].Outcome == Outcome.Conflict && result.Items[1].Outcome == Outcome.Cancelled, "缺少冲突选择不执行后续");
        result = await workspace.MoveAsync([sources[2]], MoveTarget.Folder(folder.Folder.Id), _ => Task.FromResult(ConflictChoice.KeepBoth));
        Check(result.Items.Single().ActualPath == Path.Combine(folder.ActualPath, "取消目录 (2)"), "目录保留两份编号");
    }

    internal static async Task FailuresAndCommit()
    {
        using var fixture = new Fixture();
        var failing = new FailingStore(fixture.Store);
        IDesktopWorkspace workspace = new DesktopWorkspace(failing, new TestStartup());
        await workspace.InitializeAsync([new(0, 0, 1600, 1000)]);
        await workspace.SelectRootAsync(fixture.Content);
        await workspace.CreateFolderAsync("甲");
        await workspace.CreateFolderAsync("乙");
        var first = workspace.Snapshot.Folders[0];
        var second = workspace.Snapshot.Folders[1];
        var locked = Path.Combine(first.ActualPath, "占用.txt");
        var normal = Path.Combine(first.ActualPath, "正常.txt");
        File.WriteAllText(locked, "占用原内容");
        File.WriteAllText(normal, "正常原内容");
        using (var handle = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var result = await workspace.MoveAsync([locked, normal], MoveTarget.Folder(second.Folder.Id));
            Check(result.Items[0].Outcome == Outcome.Failed && result.Items[1].Outcome == Outcome.Success, "占用失败可见且其他项目继续");
        }
        Check(File.ReadAllText(locked) == "占用原内容" && File.ReadAllText(Path.Combine(second.ActualPath, "正常.txt")) == "正常原内容", "失败保留源，成功字节一致");
        var child = Path.Combine(fixture.Home, "父", "子");
        Directory.CreateDirectory(child);
        var result2 = await workspace.MoveAsync([Path.GetDirectoryName(child)!], MoveTarget.Directory(child));
        Check(result2.Items.Single().Outcome == Outcome.Failed && Directory.Exists(child), "拒绝自身子目录");
        result2 = await workspace.MoveAsync([first.ActualPath], MoveTarget.Folder(second.Folder.Id));
        Check(result2.Items.Single().Outcome == Outcome.Failed, "不移动托管内容文件夹");
        result2 = await workspace.MoveAsync([locked], MoveTarget.Folder(first.Folder.Id));
        Check(result2.Items.Single().Outcome == Outcome.Failed && File.Exists(locked), "同目录不移动");
        result2 = await workspace.MoveAsync([locked], MoveTarget.Directory(Path.Combine(fixture.Home, "不存在")));
        Check(result2.Items.Single().Outcome == Outcome.Failed && File.Exists(locked), "不伪造目标目录");
        failing.FailOnSave = failing.Saves + 1;
        result2 = await workspace.MoveAsync([locked], MoveTarget.Folder(second.Folder.Id));
        Check(result2.Items.Single().Outcome == Outcome.Success && result2.StateCommit.Outcome == Outcome.Failed, "状态失败与实际移动成功分别报告");
        Check(!File.Exists(locked) && workspace.Snapshot.Folders[1].FileCount == 2 && workspace.Snapshot.Folders[0].FileCount == 0, "提交失败仍按实际目录更新展示");
        var restarted = new DesktopWorkspace(fixture.Store, new TestStartup());
        await restarted.InitializeAsync([new(0, 0, 1600, 1000)]);
        Check(restarted.Snapshot.Folders[1].FileCount == 2, "重启核对真实目录而非重复移动");
    }

    internal static async Task CrossVolume()
    {
        using var fixture = new Fixture();
        var target = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, ".scratch", "desktop-folder", "verification", "Kage-cross-" + Guid.NewGuid().ToString("N")));
        if (Path.GetPathRoot(fixture.Home) == Path.GetPathRoot(target)) throw new Exception("跨盘检查需要两个不同本地磁盘。");
        Directory.CreateDirectory(target);
        try
        {
            IDesktopWorkspace workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
            await workspace.InitializeAsync([new(0, 0, 1600, 1000)]);
            await workspace.SelectRootAsync(fixture.Content);
            var file = Path.Combine(fixture.Home, "跨盘.bin");
            File.WriteAllBytes(file, [0, 42, 255]);
            var directory = Path.Combine(fixture.Home, "跨盘子目录");
            Directory.CreateDirectory(Path.Combine(directory, "更深"));
            File.WriteAllText(Path.Combine(directory, "更深", "字节.txt"), "完整复制后删除源");
            var result = await workspace.MoveAsync([file, directory], MoveTarget.Directory(target));
            Check(result.Items.All(item => item.Outcome == Outcome.Success), "跨本地磁盘文件与普通子目录逐项成功：" + string.Join(";", result.Items.Select(item => item.Message)));
            Check(!File.Exists(file) && !Directory.Exists(directory), "跨盘源实际消失");
            Check(File.ReadAllBytes(Path.Combine(target, "跨盘.bin")).SequenceEqual(new byte[] { 0, 42, 255 }) && File.ReadAllText(Path.Combine(target, "跨盘子目录", "更深", "字节.txt")) == "完整复制后删除源", "跨盘目标字节一致");
            var readOnly = Path.Combine(fixture.Home, "跨盘只读.bin");
            File.WriteAllBytes(readOnly, [9, 0, 255]);
            if (OperatingSystem.IsWindows())
            {
                using var identity = WindowsIdentity.GetCurrent();
                var fileInfo = new FileInfo(readOnly);
                var parentInfo = new DirectoryInfo(fixture.Home);
                var fileOriginal = fileInfo.GetAccessControl();
                var parentOriginal = parentInfo.GetAccessControl();
                var fileDenied = fileInfo.GetAccessControl();
                var parentDenied = parentInfo.GetAccessControl();
                fileDenied.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.Delete, AccessControlType.Deny));
                parentDenied.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.DeleteSubdirectoriesAndFiles, AccessControlType.Deny));
                fileInfo.SetAccessControl(fileDenied);
                parentInfo.SetAccessControl(parentDenied);
                try
                {
                    var partial = await workspace.MoveAsync([readOnly], MoveTarget.Directory(target));
                    Check(partial.Items.Single().Outcome == Outcome.Failed && File.ReadAllBytes(readOnly).SequenceEqual(new byte[] { 9, 0, 255 }), "跨盘源无法删除不得报告移动成功：" + partial.Items.Single());
                    if (File.Exists(Path.Combine(target, "跨盘只读.bin"))) Check(File.ReadAllBytes(Path.Combine(target, "跨盘只读.bin")).SequenceEqual(new byte[] { 9, 0, 255 }), "源删除失败保留已复制目标");
                }
                finally
                {
                    parentDenied.SetSecurityDescriptorBinaryForm(parentOriginal.GetSecurityDescriptorBinaryForm(), AccessControlSections.Access);
                    parentInfo.SetAccessControl(parentDenied);
                    fileDenied.SetSecurityDescriptorBinaryForm(fileOriginal.GetSecurityDescriptorBinaryForm(), AccessControlSections.Access);
                    if (File.Exists(readOnly)) fileInfo.SetAccessControl(fileDenied);
                    var copied = new FileInfo(Path.Combine(target, "跨盘只读.bin"));
                    if (copied.Exists)
                    {
                        fileDenied.SetSecurityDescriptorBinaryForm(fileOriginal.GetSecurityDescriptorBinaryForm(), AccessControlSections.Access);
                        copied.SetAccessControl(fileDenied);
                    }
                }
            }
            var incomplete = Path.Combine(fixture.Home, "跨盘部分复制");
            Directory.CreateDirectory(incomplete);
            File.WriteAllText(Path.Combine(incomplete, "保留.txt"), "部分复制失败不得删除源");
            var busy = Path.Combine(incomplete, "占用.txt");
            File.WriteAllText(busy, "锁定内容");
            using (var handle = new FileStream(busy, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var partial = await workspace.MoveAsync([incomplete], MoveTarget.Directory(target));
                Check(partial.Items.Single().Outcome == Outcome.Failed && File.ReadAllText(Path.Combine(incomplete, "保留.txt")) == "部分复制失败不得删除源", "跨盘目录复制失败保留完整源");
            }
        }
        catch (Exception error) { Console.Error.WriteLine("跨盘检查原始失败：" + error); throw; }
        finally
        {
            var allowed = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, ".scratch", "desktop-folder", "verification")) + Path.DirectorySeparatorChar;
            if (!target.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(target).StartsWith("Kage-cross-", StringComparison.Ordinal)) throw new Exception("拒绝清理隔离范围外目录。");
            foreach (var file in Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(target, true);
        }
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
