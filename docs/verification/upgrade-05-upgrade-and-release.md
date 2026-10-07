# 升级 05：1.1.0 升级兼容与完整发布验收

日期：2026-10-07。对应 [票据 05](../../.scratch/desktop-tool-upgrade/issues/05-upgrade-and-release.md)，固定审查基点 `6ee9ee53afc34f24bb96422f6284a52f937010d5`。前置 01—04 均为 `resolved`；其各自验收证据保留，本记录补充组合与最终包检查。

## 版本与交付

项目版本统一为 1.1.0；发布脚本和验收默认目录读取项目版本，“关于”显示相同版本。交付位置为 `releases/KageDesktopTool-win-x64-1.1.0/`、相邻 ZIP 和 ZIP SHA256，包内含逐文件 `SHA256SUMS.txt`。Windows x64 包携带 .NET 10、WPF 和 Windows App SDK，不依赖全局 SDK／运行时。

1.0.0 包保留；原 ZIP SHA256 为 `43B66196E1B0904A7F39C41E8BE59A2A914E8CE26D161628FBBF2993FF592E7A`，结束时复核仍一致。早期候选另存于 `releases/候选 1.1.0 …`，用于记录夹具修正、迁移问题和审查过程；正式交付入口仅为上述统一名称的 1.1.0 包。

## 升级与回退

使用独立记录的 1.0.0 磁盘 JSON 字段，在随机隔离目录验证正常状态及有效备份。加载保留稳定标识、根目录、尺寸、头部高度、位置、视图、展开、颜色、透明度、图标方案及保留内容关联；不主动重写有效旧状态。损坏主状态只读加载备份，暂停修改；显式恢复保留损坏文件，再可保存新偏好并重启。

列表默认 32 DIP、网格默认 48 DIP，依据票据 01 后续用户确认，已同步修正主规格和本票的过时默认值。通知默认开启、名称升序、无手动顺序；已有显式偏好保留。

六种旧意图分别验证创建、自启、Folder 改名、保留内容、回收站删除和根迁移；未知归属／外部启动配置不会被认领或覆盖，待核对状态、原因和原 JSON 保留。所有真实内容均为随机夹具，不使用个人文件做移动／删除测试。

另在隔离加载上下文调用保留包内实际 1.0.0 `Kage.Workspace.dll` 的公开 `JsonWorkspaceStore.Read/Save`：核心格式 1、Folder 及保留关联可读取，但旧版回写会丢失通知偏好、内容尺寸／排序／手动顺序及 `PendingContentRename`。这不是完整无损回退。先用新版完成所有恢复，退出并备份状态及内容，再决定回退；恢复升级前状态还需核对期间真实移动／改名／迁移。使用说明已写明步骤及风险。

## 组合检查发现与修复

新增完整包组合流程验证查看尺寸、手动顺序、独立头部调高、样式、图标、通知关闭与 Folder 改名、保留内容、根迁移和重启共同使用。

首次夹具尝试把项目放到其原位置，业务正确返回“顺序未改变”。固定独立修改时间后建立实际重排，随后发现真实缺陷：迁移保存了手动名称顺序，但复制改变 NTFS 文件身份，展示会退回名称顺序。最小公共业务检查连续失败，输出迁移前后身份变化及仍存在的持久顺序，排除状态丢失和重启写入原因。

修复在副作用前先按源身份协调外部改名与同名替换，并随迁移 intent 持久化真实名称顺序；在迁移／回滚目录及字节核对完成后，按这些名称重新绑定当前文件身份，再原子提交状态。平常外部文件替换仍使用严格身份规则。新回归覆盖成功迁移、重启、取消回滚、复制后中断及重启恢复、自动模式保存的最近手动顺序、未刷新就迁移的可靠改名与同名替换。原始完整 EXE 组合流程修复后通过。

## 反馈与故事覆盖

| 反馈／故事 | 前置证据 | 本票组合与回归 |
| --- | --- | --- |
| 1—4、9；故事 1—24、38、47、49、51—53 | [内容交互](upgrade-01-content-and-file-interaction.md)、[原地完整菜单](upgrade-01-in-place-classic-menu.md)、[响应优化](upgrade-01-menu-latency.md)、[桌面落点](upgrade-01-desktop-drop-position.md) | 最终 EXE 的内容输入、图标、布局、跨 Folder／桌面／Explorer 移动；迁移前后保存手动顺序 |
| 7、11；故事 36—37、40—44、47—48 | [Folder 布局](upgrade-02-folder-layout-and-sizing.md) | 最终 EXE 自由拖动、最近放置、底部展开、分隔线调高及 DPI 检查；组合中重启恢复几何 |
| 5、6、8；故事 25—35、47、50 | [设置与通知](upgrade-03-settings-style-and-notifications.md)、[快速移动进度](upgrade-03-move-progress-no-flash.md) | 最终 EXE 设置、样式、消息提示与结果详情；组合重启恢复通知开关 |
| 10；故事 39、54 | [托盘图标](upgrade-04-tray-icon-design.md) | 最终 EXE 包内资源及四方案真实托盘切换／恢复 |
| 全部；故事 45—46 | 本记录、使用说明、升级检查 | 旧状态／备份／恢复意图、完整包、ZIP 解压、含空格路径及普通启动 |

消息提示关闭或 Windows 屏蔽时，从“设置 → 通知”查看最近结果及路径，从“恢复与状态”核对未完成操作；不恢复完成弹窗，不重复执行文件操作。

## 本次检查记录

完整业务回归和最终 EXE 的串行 Windows 验收正在执行，以下结果在结束后按实际日志补齐。未执行或失败的项目不会记为通过。

日志及截图位于忽略目录 `.scratch/desktop-folder/verification/`。运行时需当前用户交互式 Windows 会话；真实输入检查串行运行。既有正式实例正常托盘退出前确认无待恢复意图，并将用户状态备份到 `upgrade-05-state-before-a562c2913b1b4dc59a16024b804b589b/`。

本机实际 Windows 11 x64、单屏 150%。实际多屏、负坐标屏幕、混合 DPI、拔屏、切换主屏、系统缩放／分辨率变化、ARM64、32 位 Windows 和其他系统未验收；业务轨迹和注入度量不作为硬件实测。

## 复现

```powershell
rtk proxy dotnet run --project tests/Kage.Workspace.Checks --no-restore
rtk proxy dotnet run --project tests/Kage.Workspace.Checks --no-restore -- --rollback-package releases/KageDesktopTool-win-x64-1.0.0
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File publish.ps1
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File verify-release.ps1
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File verify-archive.ps1
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File verify-user-launch.ps1
```

已有发布目录会被拒绝覆盖；复现发布需另传新的 `-OutputDirectory releases/<新目录>`。单项 Windows 重跑使用 `verify-release.ps1 -OnlyCheck <名称>`，不会将未执行的其他检查记为通过。

## 独立审查

按 `code-review` 分别执行 Standards 与 Spec 两轴审查，固定基点如上；审查结论及问题处理在验收完成后补齐。

首轮审查范围为基点至 `9d72b07`。Standards 0 项可行动问题；Spec 1 项 P2：迁移前尚未刷新时，直接忽略身份重绑会丢失可靠外部改名位置或认领同名替换。已先建立失败回归，再补源身份协调及 intent 提交，回归转绿，待累计复审。

本轮桌面恢复检查首次因任务栏投影的其他应用下载进度而停止；只读 UIAutomation 确认进度位于 `Shell_TrayWnd`，实际 Explorer 文件窗口无进度。已将安全检查范围排除桌面／任务栏宿主，仍拒绝独立 Explorer 对话框或操作进度，并在错误中保留窗口名称及类名。普通启动检查另修正为按 PID 和设置窗口名共同查找，避免把 Shell 图标辅助窗口误认成设置；操作历史允许追加，受保护工作区及备份必须保持不变。

内容输入初次候选通过，最终重复检查两次停在菜单取消后的框选。检查原先无条件 Win+D 会把已显示的桌面再次遮住；补真实命中核对后，框选通过，但又发现后续取消菜单有同样切换，且 Ctrl 选择期间补发 Win+D 会组合其他系统快捷键。统一为每次操作前核对实际 HWND，必要时使用真实 Shell 的显示桌面动作显露夹具；专门桌面恢复检查仍使用真实 Win+D。原框选、Ctrl／Shift、拖动、原生菜单及八种视图尺寸断言均保留，修正后的完整内容输入检查通过。

后续封装检查遇到显露桌面后固定 350 毫秒仍未命中的一次失败。改为最多等待 2 秒，逐次核对实际 HWND；仍不命中就停止并保存全屏截图与期望／实际 HWND，不放宽行为断言。独立完整真实输入检查再次通过。
