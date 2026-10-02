# 桌面 Folder 实现索引

## Notes

- 正式规格见 [spec.md](spec.md)，任务依赖及既有交付记录见 [ticket-plan.md](ticket-plan.md)。

## Decisions-so-far

- 2026-10-02：用户明确启动的 [06 外观和图标](issues/06-appearance-and-icons.md) 已完成并验收。外观草稿独立于已提交快照，通过共用 `IDesktopWorkspace` 应用／取消；背景 alpha 不影响文字和 Shell 图标，四套应用／托盘图标保存后同步窗口并重启恢复。21 项业务检查、Release 外观及已有 04／05 真实 Windows 检查通过；规范和规格复核均 0 项发现。细节见 [验收记录](../../docs/verification/06-appearance-and-icons.md)。
- 2026-10-02：用户明确启动的 [07 真实文件移动及同名冲突](issues/07-file-moves-and-conflicts.md) 已完成。共用后台 `MoveAsync` 提供双向批量、异步冲突、取消、逐项实际结果及独立状态提交结果；原生 OLE 不凭全局效果重复删除源。25 项业务检查、Release 真实桌面／Explorer 多选拖入拖出、冲突、Esc 取消、ACL 失败及 04／05／06 回归通过。规格复核发现的 NTFS 命名流丢失经真实跨盘复现修复，文件／目录流均验证保真，规范和规格各 0 项遗留。详见 [验收记录](../../docs/verification/07-file-moves-and-conflicts.md)。

## Fog

- 后续任务按 [ticket-plan.md](ticket-plan.md) 的依赖与用户明确启动要求继续，完整发布由 13 交付。
