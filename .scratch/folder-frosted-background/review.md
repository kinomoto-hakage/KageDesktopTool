# 毛玻璃实现双轴审查

日期：2026-10-16。集成分支：`codex/folder-frosted-background`。

固定基线为 `68bf4d28aaf414bf325f97a623634a278b7419ba`，初次审查快照为 `53f9981`，修复快照为 `ca6ba42`。审查使用 `git diff 68bf4d2...HEAD`，代码规范与规格由两个独立代理并行核对。

## Standards

硬性规范违反：0。判断性发现：1，最高 P3，已修复。

possible Duplicated Code：`FolderHeader.BackdropInput` 与 `ContentPointerInput.PointerDown` 分别维护双击时间／坐标和判定，度量来源也不同。已移除前者的重复状态、判定及两项 Interop；原生入口仅传原始点击信息，共用 `PointerDown` 统一识别。原审查代理只读复核后关闭此项，未发现相关新增问题。

## Spec

发现：1，最高 P2，已修复。

规格故事 56 要求毛玻璃不改变拖动等既有操作；生命周期要求暂停及宿主丢失时释放资源。透明原生 Thumb 只处理鼠标松开，遗漏 `WM_CAPTURECHANGED`／`WM_CANCELMODE`，导致其他窗口夺取捕获或背景销毁后布局会话残留。已统一清理原生输入状态，退出原生消息栈后结束布局，并防止普通 `ReleaseCapture` 重入造成二次提交。原审查代理核对代码和真实 red→green 日志后关闭此项，未发现相关新增问题。

## 修复验证与交付边界

- `--frosted-capture-check`：物理按住透明 Thumb，实际捕获转移、暂停销毁背景、系统取消模式和正常松开四条路径均通过；每次单次提交，后续展开及样式入口可用，内容字节不变。
- `--frosted-content-check --double-click-only`：网格／列表、未选／已选、图像／文字八组合真实双击及 Shell 启动通过。
- `--frosted-system-check` 与 Release 构建通过，构建零警告、零错误。完整业务与其余真实桌面验收见 [02 记录](../../docs/verification/frosted-02-layout-and-recovery.md)。
- 原始修复日志归档于 `.scratch/folder-frosted-background/verification/ticket-02/review-*.txt`，由 Git 忽略；多屏、混合 DPI 等未覆盖硬件边界保留在各验证记录中。

Standards：1 项，最高 P3，已修复，剩余 0；Spec：1 项，最高 P2，已修复，剩余 0。
