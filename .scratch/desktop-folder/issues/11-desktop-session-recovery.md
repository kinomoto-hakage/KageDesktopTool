# 11: 显示桌面及 Explorer 重启后恢复操作

Type: implementation
Status: claimed
Labels: ready-for-agent
Approved: 2026-10-02
Plan: ../ticket-plan.md
Spec: ../spec.md
Blocked by: 05, 07
User stories: 62, 65, 66, 68, 69

**实现内容（What to build）：** 在显示桌面和 Explorer 重启后恢复 Folder、托盘、输入和真实拖放；宿主不可用时说明展示状态并允许定位实际内容。

**阻塞关系（Blocked by）：** 05「原生内容查看、多 Folder 展开及不重叠布局」；07「真实文件双向批量移动与同名冲突」

## 上下文与范围

- 桌面 Folder 是头部与下方展示部分，对应存储根目录中的同名真实内容文件夹，沿用用户已验收的原型行为。
- 业务操作经共用的桌面整理 interface，检查实际文件和状态；Windows 会话能力另做真实验收。原型分支为 `codex/prototype/desktop-folder`。

## 验收：显示桌面和 Explorer 重启后恢复可操作的 Folder

- [ ] 人工 `Win+D` 进入／退出后，头部与展示部分实际可见且可以点击、拖动、缩放及操作内容，不仅检查窗口父关系。
- [x] 普通应用覆盖、焦点变化及设置窗口显示符合桌面整理体验，不让 Folder 无故覆盖正常应用。
- [x] Explorer 重启后恢复托盘、桌面挂接及真实拖放能力，不产生重复窗口、重复热键或丢失活动／保留内容状态。
- [x] 宿主不可用时保留内容和配置，说明展示不可用并可打开实际目录；恢复可用后重新挂接。
- [x] Explorer 内部窗口细节只出现在桌面适配器中，其他模块不依赖窗口名称／层次。
- [x] 在当前真实 Windows 会话验收透明绘制、窗口命中、文件拖放和恢复；执行时避免打断未完成文件操作，保留复现与结果记录。

## 来源与交付检查

- 以正式规格的相关用户故事及验收矩阵为依据；本票交付界面入口、实际结果、必要状态和自己的测试。
- 独立演示：在显示桌面和 Explorer 重启后恢复 Folder、托盘、输入和真实拖放；宿主不可用时说明展示状态并允许定位实际内容。

## Comments

- 用户已认可合并方案、阻塞关系及本票验收标准，已正式发布。
- 2026-10-03：用户调用 implement 开始本票；05、07 均为 resolved。沿用已认可的 IDesktopWorkspace 与真实 Windows 会话验收边界；复核基点为 9c68ff91。
- 2026-10-03：用户明确批准模拟输入及 Explorer 重启，确认没有未完成文件操作后执行自动实际会话验收；随后选择暂不做人工 Win+D 验收，保留为待验收。

## Answer

实现已提交，人工验收待完成，`Status: claimed` 保留，不标记为 resolved。

- `IDesktopWorkspace.ReportDesktopAvailabilityAsync` 与 `WorkspaceSnapshot.DesktopAvailable` 提供不持久化的会话展示状态；不会中断或重复文件移动，不混同内容恢复状态。
- 真实复现发现 Explorer 会销毁挂接的子窗口，使旧 HWND 归零。Runtime 依据原稳定标识和已提交状态重建失效 WPF 窗口，重新登记 OLE 接收；复用控制器、原热键、NotifyIcon 和菜单。宿主不可用时隐藏展示，托盘／设置说明状态并提供实际目录入口。
- Explorer 宿主识别、窗口样式／挂接／定位和文件拖放目标识别集中到 `WindowsDesktop`。
- 全套 44 项业务检查通过；Release 构建 0 警告、0 错误。真实自动 Win+D、鼠标点击／拖动／缩放、普通应用覆盖、设置焦点、Explorer 重启、系统托盘双击和恢复后双向 OLE 均通过。04—10 既有 Windows 回归通过；06 一次前台焦点变化失败，单独复测通过。
- 以用户确认的 `9c68ff91` 为基点，两个独立 sub-agent 分别完成 Standards 与 Spec 复核：均为 0 项代码发现，Spec 保留 1 项待人工验收限制。
- 交付与复现记录：[11 桌面会话验收](../../../docs/verification/11-desktop-session-recovery.md)。主日志为忽略目录下 `verification/desktop-recovery-session.txt`，实际屏幕截图已查看。
- 人工隔离入口 `--manual-desktop-check` 已启动供用户核对；用户选择暂不验收，夹具随后从自己的托盘菜单正常退出并清理。后续需亲自完成第一条验收后再关闭本票。
