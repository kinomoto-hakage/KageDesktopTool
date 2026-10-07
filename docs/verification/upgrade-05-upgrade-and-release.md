# 升级 05：1.1.0 升级兼容与完整发布验收

日期：2026-10-07。对应 [票据 05](../../.scratch/desktop-tool-upgrade/issues/05-upgrade-and-release.md)，固定审查基点 `6ee9ee53afc34f24bb96422f6284a52f937010d5`。前置 01—04 均为 `resolved`；其各自验收证据保留，本记录补充组合与最终包检查。

## 版本与交付

项目版本统一为 1.1.0；发布脚本和验收默认目录读取项目版本，“关于”显示相同版本。交付位置为 `releases/KageDesktopTool-win-x64-1.1.0/`、相邻 ZIP 和 ZIP SHA256，包内含逐文件 `SHA256SUMS.txt`。Windows x64 包携带 .NET 10、WPF 和 Windows App SDK，不依赖全局 SDK／运行时。

最终交付代码为 `18bf37fd11bef4735f5ccb8321caf653ec3b269a`。ZIP SHA256：`F06A89AD7D58603E8283EAF64169A34412CE5E9D0FDCA5DD4389D56AB9EB2EDA`。解压后的 799 个清单文件全部一致，没有清单外文件；含空格目录中的 EXE 成功加载包内 CoreLib、WPF、通知运行时和图标资源。

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

最终 66 项完整业务回归通过，退出码 0，日志为 `upgrade-05-business.txt`。包括独立旧 JSON、备份恢复、六类旧意图、迁移顺序及所有既有创建、冲突、批量移动、改名、两种删除、跨卷、取消、提交失败和中断恢复。受限沙箱的原子文件替换曾返回 Access denied；相同随机夹具在当前用户完整会话通过，不把该沙箱失败判为产品失败。

最终构建成功，没有警告或错误。首轮构建被用户正在运行的开发输出 DLL 锁定；发布脚本改为独立构建目录，保留实例及旧包，之后构建通过。

| 最终 EXE 检查 | 实际结果与证据 |
| --- | --- |
| publish、release-workflow、session | 通过；自包含资源、组合流程、随机真实自启、热键及单实例。最终包已再次补核组合／会话 |
| content-input | 最终 EXE 直接执行 `--content-input-check`，退出码 0；全套打开、菜单、框选、修饰键、重排、移动、滚动及八种视图尺寸通过，日志 `content-input-session.txt` |
| shell-image、content-layout、layout-input | 通过；真实 Shell 图像、窗口布局、输入及几何恢复。布局曾在捕获丢失后的新会话等待超时，独立重跑通过，原因未确定 |
| appearance | 同一正式 EXE 的完整检查通过，含原生调色盘 owner／前台。两次前台焦点失败后做只读跟踪；Hidden、Normal 启动均通过，未据差异断言启动标志是根因。证据 `upgrade-05-appearance-probe.json`、`upgrade-05-appearance-Hidden.txt`、`upgrade-05-appearance-Normal.txt` |
| settings-notification | 设置、样式、结果详情、关闭及故障恢复通过；真实通知身份、投递及系统历史通过。原生显示／点击自动检查失败，详见下一段 |
| move-progress、tray-session、file-move、folder-action | 通过；快速／耗时移动、真实托盘四方案、双向 OLE、冲突、保留内容及整目录回收 |
| root-migration、migration-recovery、display-dpi | 通过；真实设置迁移、取消、恢复清单及当前单屏 150% 几何与输入 |
| desktop-recovery | 通过；成对真实 Win+D、Explorer 重启、桌面／控制器／托盘恢复，恢复后真实双向 OLE 字节、源消失与保留关联一致 |

本票未改通知投递、点击路由或 Windows App SDK 版本。通知可见、点击、冷启动及 Windows 关闭通知采用 [票据 03 的用户人工通过记录](upgrade-03-settings-style-and-notifications.md)；本次正式包另确认投递设置为 Enabled、通知载荷进入系统历史。自动控件检查仍出现“未找到通知”以及最后一次 `ElementNotAvailableException`，因此自动原生显示／点击没有通过，也没有拿 API 接受冒充可见点击。保留完整失败日志 `upgrade-03-settings-notifications.txt`；该项与既有人工验收分别记录。

正式 ZIP 解压到 `releases/解压验收 含空格 1.1.0 a0ad7e85951b43c680422ebc8eb54163/KageDesktopTool-win-x64-1.1.0/` 后，无检查参数普通启动成功；重复启动转交第一实例的设置，读取实际根目录及全部已保存 Folder。关闭设置继续运行，从真实托盘正常退出。实际工作区与备份、原型受保护文件 SHA256 不变，实际内容路径／属性及正式自启项不变；操作历史按正常查看行为追加。截图已查看，日志 `upgrade-05-user-launch.txt`，截图 `upgrade-05-实际用户设置.png`。

实际机器安装有 SDK；通过运行时配置、自包含资源和实际 CoreLib 加载位置核对包内依赖，未另用无 SDK 新机器验收。换包流程的含空格路径、状态兼容及自启命令使用最终 EXE 已核对；正式用户自启项没有在测试中改写，换位置后需按使用说明重新应用自启选择。

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

按 `code-review` 分别由独立只读代理执行 Standards 与 Spec 两轴审查，最终代码累计范围为基点至 `18bf37f`：`rtk git diff 6ee9ee53afc34f24bb96422f6284a52f937010d5...18bf37fd11bef4735f5ccb8321caf653ec3b269a`。

### Standards

最终 0 项可行动问题。中文开发文本、RTK、领域术语及共用业务层约定一致；有界命中等待保留实际 HWND 条件及失败证据。未发现硬违规或值得报告的 baseline smell。

### Spec

最终 0 项遗留问题。迁移排序 P2 已补失败回归并修复；输入等待、Shell 显露和物理命中核对保留所有原行为断言，专门桌面恢复检查仍使用真实 Win+D。审查是静态复核，实际 Windows 结果独立列在上文。

最终 Standards 0 项、Spec 0 项；原生通知自动点击与未实测硬件范围如实保留，不合并为自动检查通过。

对关闭记录 `c9108f4` 的最后复核，Spec 无遗留；Standards 指出报告中的 diff 示例缺少 RTK 前缀，已补齐。此次修正仅涉及文档，不改变最终包。

首轮审查范围为基点至 `9d72b07`。Standards 0 项可行动问题；Spec 1 项 P2：迁移前尚未刷新时，直接忽略身份重绑会丢失可靠外部改名位置或认领同名替换。已先建立失败回归，再补源身份协调及 intent 提交，回归转绿，待累计复审。

本轮桌面恢复检查首次因任务栏投影的其他应用下载进度而停止；只读 UIAutomation 确认进度位于 `Shell_TrayWnd`，实际 Explorer 文件窗口无进度。已将安全检查范围排除桌面／任务栏宿主，仍拒绝独立 Explorer 对话框或操作进度，并在错误中保留窗口名称及类名。普通启动检查另修正为按 PID 和设置窗口名共同查找，避免把 Shell 图标辅助窗口误认成设置；操作历史允许追加，受保护工作区及备份必须保持不变。

内容输入初次候选通过，最终重复检查两次停在菜单取消后的框选。检查原先无条件 Win+D 会把已显示的桌面再次遮住；补真实命中核对后，框选通过，但又发现后续取消菜单有同样切换，且 Ctrl 选择期间补发 Win+D 会组合其他系统快捷键。统一为每次操作前核对实际 HWND，必要时使用真实 Shell 的显示桌面动作显露夹具；专门桌面恢复检查仍使用真实 Win+D。原框选、Ctrl／Shift、拖动、原生菜单及八种视图尺寸断言均保留，修正后的完整内容输入检查通过。

后续封装检查遇到显露桌面后固定 350 毫秒仍未命中的一次失败。改为最多等待 2 秒，逐次核对实际 HWND；仍不命中就停止并保存全屏截图与期望／实际 HWND，不放宽行为断言。独立完整真实输入检查再次通过。

最终包随后通过包装脚本启动时仍有一次初始命中失败；`content-hit-failure.png` 明确显示 Codex 窗口覆盖目标点，检查在输入前停止。直接运行同一正式 EXE 的 `--content-input-check` 后，全套检查退出码 0；记录实际覆盖与直接运行结果，不把包装脚本的失败改写为通过，也未确定其与启动方式的因果关系。
