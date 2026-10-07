# 升级 03：移入／移出中央进度闪窗修复

日期：2026-10-05。固定基线：`6a80362`。用户普通运行已确认 Folder 操作可在 Windows 通知中显示，但快速移入／移出仍会在屏幕中央闪过弹窗。

## 复现与原因

新增 `--move-progress-check --quick-only`，调用实际 `Runtime.MoveFilesAsync` 与共用 `IDesktopWorkspace`，移动随机目录中的真实小文件。检查移动期间的 `MoveDialog.IsVisible` 变化及实际 Loaded 事件；完成后再等待 750 毫秒，检测迟到显示。

基线实现明确失败于“移入快速完成全程没有中央进度闪窗或迟到显示”，而真实文件移入成功。原因是 `Runtime.MoveFilesAsync` 在任何文件操作开始前无条件 `dialog.Show()`，完成后立即关闭；中央窗口实际是移动进度窗口，Windows 完成通知已经另外发送。

## 修复行为

移动开始时先执行业务操作；持续超过 500 毫秒才显示进度。计时器使用 WPF Background 优先级，让已经排队的完成回调先结束窗口。结束时停止计时器并关闭窗口，快速完成全程不显示中央进度；结果详情与一批一条 Windows 通知继续保留。

同名冲突需要用户选择时立即显示进度及询问，保留正确的窗口 owner。耗时移动仍有取消按钮；取消只停止后续项目，已完成内容保留。等待状态提交时取消也不会把已移动文件伪造为失败或回滚。

## 验证

- 首条真实快速移入检查在旧实现失败，修复后通过。
- 快速移入、移出和拖放取消不触发中央进度的可见性或 Loaded 事件，等待后无迟到显示；真实字节、源消失／取消保留与一次汇总详情均正确。
- 在正式状态存储边界暂停一次提交，实际文件已经移动：开始不立即显示窗口，持续后显示真实进度；真实 UIAutomation 取消按钮可用，取消后禁止重复请求，释放提交后窗口自动关闭且字节保留。
- Release 构建和完整自包含 x64 包生成通过，0 警告、0 错误。
- 完整 63 项共用业务回归通过。
- 真实鼠标／OLE 文件移动回归通过：移入、跨 Folder、移出到 Explorer／桌面、同名保留两份／跳过／取消、拖放 Esc 和实际 ACL 失败继续保持正确。

日志位于忽略目录 `.scratch/desktop-folder/verification/upgrade-03-move-progress.txt`。实际检查是单屏 150% Windows 会话；耗时场景控制的是正式存储边界的提交时间，没有用虚构文件移动替代真实副作用。

## 人工通知证据

用户确认普通运行通知可见，[Windows 通知截图](../../.scratch/desktop-tool-upgrade/references/2026-10-05-notification-visible.png)显示文件移动计数、Folder 布局及展开／折叠结果；[设置详情截图](../../.scratch/desktop-tool-upgrade/references/2026-10-05-notification-details.png)显示操作结果记录。这补充了普通运行的实际显示证据；自动检查进程未找到通知控件的旧记录保留，不能再据此断言普通应用通知不可用。

截图本身没有证明系统通知点击、冷启动激活或 Windows 系统关闭通知。2026-10-07 用户随后明确确认这三个场景已通过人工检查、功能正常，原票据已 resolved，详见 [主验收记录](upgrade-03-settings-style-and-notifications.md)。用户报告的闪窗修复另有可失败的真实移动检查通过证据。

## 复现命令

```powershell
rtk proxy dotnet build src/KageDesktopTool/KageDesktopTool.csproj -c Release --no-restore
rtk proxy src/KageDesktopTool/bin/Release/net10.0-windows10.0.19041.0/win-x64/KageDesktopTool.exe --move-progress-check
rtk proxy dotnet publish src/KageDesktopTool/KageDesktopTool.csproj -c Release --self-contained true --no-restore -o .scratch/desktop-folder/verification/upgrade-03-no-flash
```

新的完整验收包使用独立目录，不覆盖用户正在运行的 `upgrade-03-final`。

## 独立审查

按 `code-review` 技能以 `6a80362` 为固定基线，对实现提交 `02e1e59` 进行两个只读代理独立审查。

### Standards

规范硬违规 0，代码异味判断 0。进度延迟、冲突显示及计时器停止集中在移动窗口；新增检查使用真实工作区和文件夹具，状态存储装饰器只控制验收耗时。

### Spec

发现 0。快速移入／移出不显示中央窗口、耗时才显示、冲突立即询问、取消保留实际结果和一批一次通知均符合本次反馈及原票要求。

两轴各 0 项遗留。闪窗修复的自动回归与原票其他系统通知的人工验收分别记录；后者已由 2026-10-07 用户确认补齐。
