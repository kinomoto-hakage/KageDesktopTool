# 桌面 Folder 实现索引

## Notes

- 正式规格见 [spec.md](spec.md)，任务依赖及既有交付记录见 [ticket-plan.md](ticket-plan.md)。

## Decisions-so-far

- 2026-10-02：用户明确启动的 [06 外观和图标](issues/06-appearance-and-icons.md) 已完成并验收。外观草稿独立于已提交快照，通过共用 `IDesktopWorkspace` 应用／取消；背景 alpha 不影响文字和 Shell 图标，四套应用／托盘图标保存后同步窗口并重启恢复。21 项业务检查、Release 外观及已有 04／05 真实 Windows 检查通过；规范和规格复核均 0 项发现。细节见 [验收记录](../../docs/verification/06-appearance-and-icons.md)。
- 2026-10-02：用户明确启动的 [07 真实文件移动及同名冲突](issues/07-file-moves-and-conflicts.md) 已完成。共用后台 `MoveAsync` 提供双向批量、异步冲突、取消、逐项实际结果及独立状态提交结果；原生 OLE 不凭全局效果重复删除源。25 项业务检查、Release 真实桌面／Explorer 多选拖入拖出、冲突、Esc 取消、ACL 失败及 04／05／06 回归通过。规格复核发现的 NTFS 命名流丢失经真实跨盘复现修复，文件／目录流均验证保真，规范和规格各 0 项遗留。详见 [验收记录](../../docs/verification/07-file-moves-and-conflicts.md)。

- 2026-10-03：[08 重命名及两种删除方式](issues/08-rename-and-delete.md) 已完成。共用业务接口同步改名真实目录，或先核对实际桌面快捷方式／整目录回收再移除入口；保存保留目录及工具链接关联，文件效果与状态提交异常通过操作意图恢复。30 项业务检查、Release 构建、新增真实窗口及既有 04／05／06／07 Windows 回归通过，含空格本地检查包已更新。快捷方式生成后 Shell 异常的边界先复现再修复，规范和规格复核各 0 项遗留。详见 [验收记录](../../docs/verification/08-rename-and-delete.md)。

- 2026-10-03：[10 中断迁移恢复](issues/10-recover-interrupted-migration.md) 已完成。启动只读核对真实目录及链接目标；设置提供活动／保留内容未恢复清单、Explorer 定位及共用业务重试入口，全部旧内容及配置确认后解除暂停。42 项业务检查、十二阶段独立进程退出和重启、Release 构建、新增真实恢复会话及 04—09 Windows 回归通过。目录创建中断边界先复现后修复，未知内容及目录命名流保留，规范／规格复审各 0 项遗留。详见 [验收记录](../../docs/verification/10-recover-interrupted-migration.md)。

- 2026-10-03：[11 桌面会话恢复](issues/11-desktop-session-recovery.md) 已完成并验收。实际复现确认 Explorer 销毁跨进程子窗口，按稳定标识重建失效窗口并恢复输入／OLE，保留托盘、控制器和热键。44 项业务检查、Release 及真实 Win+D／Explorer 恢复与 04—10 回归通过；规范／规格复核均 0 项代码发现。用户已确认人工 Win+D 检查通过，Status 更新为 resolved，13 对 11 的依赖已满足，仍等待 12 完成。详见 [验收记录](../../docs/verification/11-desktop-session-recovery.md)。

- 2026-10-03：[12 多显示器及 DPI 恢复](issues/12-display-dpi-recovery.md) 已完成，详见 [验收记录](../../docs/verification/12-display-dpi-recovery.md)。真实工作区并集允许连续跨屏并阻挡空洞，恢复优先保留剩余屏位置，窗口补偿宿主 DPI 并重新测量原生内容。52 项业务检查、Release、当前单屏 150% 的真实绘制／鼠标输入／显示通知及 05／06／07 回归通过；125%／150%／200% 注入度量通过。审查发现的高到低 DPI 接缝阻塞经复现修复，规范／规格最终均 0 项遗留。实际多屏、混合 DPI、拔屏及系统配置切换未验证；13 的依赖现已全部满足，等待用户明确启动。

## Fog

- 后续任务按 [ticket-plan.md](ticket-plan.md) 的依赖与用户明确启动要求继续，完整发布由 13 交付。
