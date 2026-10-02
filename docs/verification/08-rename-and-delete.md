# 08 重命名及两种删除方式验收

日期：2026-10-02。对应 [任务 08](../../.scratch/desktop-folder/issues/08-rename-and-delete.md)。

## 实现与结果口径

头部右键“重命名…”及“删除…”通过 `IDesktopWorkspace.RenameFolderAsync`／`DeleteFolderAsync` 执行业务操作，原样校验名称，支持同名编号、跳过和取消。重命名采用不覆盖的原生同盘移动；成功后只更新名称，保持稳定标识、外观、位置、尺寸及查看方式。头部菜单打开路径和后台内容刷新读取当前快照，不继续使用构造窗口时的旧路径。

保留内容先在 Windows 当前用户实际桌面生成 `.lnk`，核对目标及带归属标识的工具说明，再移除活动映射。内容原地保留；独立保存 `RetainedFolders` 中的稳定标识、实际内容路径和快捷方式路径，供任务 09 使用。快捷方式先保存随机临时文件，再以不覆盖方式移入最终名称；并发同名抢占失败时保留原入口。

一同删除在专用 STA 使用 Windows `IFileOperation` 的 `FOFX_RECYCLEONDELETE`。回收前回调不提供可回收标志时终止，回收后还核对非空回收项、源消失及回收站中的稳定归属。没有永久删除分支。该次序依据 [SetOperationFlags](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifileoperation-setoperationflags)、[PreDeleteItem](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifileoperationprogresssink-predeleteitem) 和 [PostDeleteItem](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifileoperationprogresssink-postdeleteitem)。COM 对象在同一 STA 创建与释放，目录操作和状态提交保持在共用业务模块内。

三种文件效果执行前均原子保存操作意图。意图保存失败不执行文件操作；文件失败保留入口；最终提交失败返回 `RecoveryRequired`、实际路径和原／目标说明。成功改名后的恢复快照展示实际新名称；重启核对归属后完成提交。保留内容核对实际工具快捷方式，回收核对回收站稳定标识；仅源路径失联不能证明回收成功，保留待核对状态。新字段为格式 1 的可选扩展，既有状态仍可加载，非法恢复目标和重复保留关联不能保存。

## 检查

- 新增五组共用接口检查：目录字节、标识及外观位置、改名后外部新增、重启；同名三种选择、设备名、真实文件占用、真实 ACL 拒绝、意图和最终提交故障、仅大小写改名、意图保存后并发抢占；实际 `.lnk` 编号、目标和关联、不复活入口；快捷方式／回收失败与取消、提交中断恢复；原生完整目录回收、子目录字节及提交失败后重启核对。
- `--folder-action-check` 在实际用户桌面使用真实 WPF 头部菜单和按钮，检查输入焦点、无效名称、改名后的内容计数与展示、两种删除选择、取消、实际桌面快捷方式、移除头部及真实回收效果。记录位于忽略目录 `.scratch/desktop-folder/verification/folder-actions-session.txt`，预览为 `08-重命名结果.png` 和 `08-删除选择.png`。
- 原生回收检查需在实际用户会话运行；受限进程不能访问实际用户的回收站，会按失败保留入口，不将受限检查视作回收能力验收。检查仅回收随机临时夹具，验证归属后恢复该夹具再清理对应元数据，不清空回收站。

```powershell
rtk proxy dotnet run --project tests/Kage.Workspace.Checks --no-restore
rtk proxy dotnet build src/KageDesktopTool/KageDesktopTool.csproj -c Release --no-restore
rtk proxy dotnet src/KageDesktopTool/bin/Release/net10.0-windows/KageDesktopTool.dll --folder-action-check
```

全套 30 项共用业务检查及 Release 构建通过。Windows 新增会话检查已通过；最终复核和既有会话回归结果在任务 Answer 中记录。
