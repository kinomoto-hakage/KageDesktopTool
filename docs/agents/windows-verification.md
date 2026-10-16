# Windows 验证与交接

先运行 `tools/windows/verify-ci.ps1`；CI 只执行隔离业务分组、仓库约束和 Windows 构建。需要真实输入、Shell 或系统状态的检查在用户交互会话串行执行。

## 选择与运行

`tools/windows/checks.json` 是检查名称、参数、分组与系统影响的权威清单；构建和发布会将它复制到 EXE 同目录。`Test-Repository.ps1` 核对清单与 `Program.cs` 的入口，并拦截正式代码中的 `.scratch` 路径和直接追加文件日志。正式状态写入仍使用原业务路径。

```powershell
./verify-release.ps1 -List
./verify-release.ps1 -Plan -Suite frosted
./verify-release.ps1 -PackageDirectory <完整包目录> -OnlyCheck frosted-capture
./verify-release.ps1 -PackageDirectory <完整包目录> -Suite system
```

`system` 分组覆盖真实系统设置与组合恢复；计划中的 `changesSystem` 同时标识 Explorer 重启。执行前留出鼠标与桌面输入。截图需要真实前景无遮挡；模糊恢复需要蓝→绿背景的物理像素变化证据。对象存活、事件注入和离屏渲染不能替代这些结论。

检查共用 `DesktopCheckSession`：新检查通过 `EvidenceFor` 获取证据目录；输入前用 `InputPointAsync` 等待布局、手势结束和真实 HWND 命中稳定；截图用 `CaptureAsync` 检查遮挡；组合夹具用 `PlaceRecords` 避开已有 Folder 和样式窗口，只移动隔离记录。系统设置、鼠标与资源恢复用 `Defer` 注册，在 UI 仍存活时 `await RestoreAsync()`。恢复会尝试所有已登记步骤并报告聚合错误。

## 中断恢复

每次执行生成 `.scratch/check-runs/<运行 ID>/run.json`，记录提交、工作区修改、EXE／完整包／清单摘要、命令、PID、进程启动时刻、退出码与各次尝试的证据位置。`latest.json` 指向最近记录。.NET 的 EXE 是 apphost，恢复还会核对 DLL 和资源，防止 EXE 摘要未变却混入不同构建。

1. 读取记录，核对 PID、启动时刻与程序路径；进程仍存活时等待它完成清理。执行器会阻止后续检查。
2. 进程已退出后核对其日志及系统恢复结果。超时保留进程；记录中断于 `Starting` 时，需要先人工查明是否已启动子进程，并将该尝试标记为 `NeedsAttention`、补充查明的进程身份。完成核对后才恢复。
3. 使用原包与 `-Resume <运行 ID>`。提交、二进制或清单变化时创建新运行；恢复只跳过已通过的检查，为重试追加独立证据目录。

```powershell
./verify-release.ps1 -PackageDirectory <原包目录> -Resume <运行ID>
```

完成标准：所需检查均有本次代码对应的通过记录；未实测的硬件或系统组合明确留作限制；所有恢复步骤已完成。其他工作树的中断记录需要在其原工作树恢复；会话互斥只覆盖仍在运行的执行器。

## 人工包与精简交接

```powershell
./tools/windows/prepare-manual-review.ps1
```

人工包保存在**主工作区** `.scratch/manual-review/`，使用唯一目录和独立构建输出。`latest.json` 给出确切 EXE、提交与 SHA256；包内 `manual-review.json` 保存逐文件摘要与源工作区修改记录。先从托盘退出旧实例，再双击新 EXE。清理 worktree 前确认包目录完整、EXE 摘要一致并已给用户确切路径。

接手时先确认原执行器或 agent 是否仍在运行；已中断的任务从检查点继续，等待对象必须仍有活跃任务。交接只传以下字段，再按需读取目标文件：

```text
目标：<一句话>
规格：<spec 路径>
票据：<当前 ticket 路径>
基点／当前提交：<commit>／<commit>
运行记录：<run.json 绝对路径或尚未运行>
当前进程：<PID + 启动时刻 + EXE，或已结束>
剩余工作：<下一项可验证动作>
```

桌面相关代码按问题导航：

| 问题 | 入口 |
| --- | --- |
| 实时背景与原生 HWND | `FrostedBackdrop.cs`、`FolderHeader.cs`、`WindowsDesktop.cs` |
| 透明像素输入、捕获与 OLE | `FrostedBackdrop.cs`、`FolderHeader.cs`、`ContentPointerInput.cs` |
| 跨 Folder 选择 | `SelectionCoordinator.cs`、`WindowsSelectionInput.cs` |
| 系统限制与 Explorer 恢复 | `FrostedEnvironment.cs`、`WindowsFrostedEnvironment.cs`、`Runtime.cs` |
| 样式事务与持久化 | `AppearanceInteraction.cs`、`DesktopWorkspace.cs`、`JsonWorkspaceStore.cs` |

表中桌面文件位于 `src/KageDesktopTool/`，业务文件位于 `src/Kage.Workspace/`。先核对当前文件名，再阅读对应实现；验收结论和历史限制见 `docs/verification/`。
