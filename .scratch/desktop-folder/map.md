# 桌面 Folder 实现索引

## Notes

- 正式规格见 [spec.md](spec.md)，任务依赖及既有交付记录见 [ticket-plan.md](ticket-plan.md)。

## Decisions-so-far

- 2026-10-02：用户明确启动的 [06 外观和图标](issues/06-appearance-and-icons.md) 已完成并验收。外观草稿独立于已提交快照，通过共用 `IDesktopWorkspace` 应用／取消；背景 alpha 不影响文字和 Shell 图标，四套应用／托盘图标保存后同步窗口并重启恢复。21 项业务检查、Release 外观及已有 04／05 真实 Windows 检查通过；规范和规格复核均 0 项发现。细节见 [验收记录](../../docs/verification/06-appearance-and-icons.md)。

## Fog

- 后续任务按 [ticket-plan.md](ticket-plan.md) 的依赖与用户明确启动要求继续，完整发布由 13 交付。
