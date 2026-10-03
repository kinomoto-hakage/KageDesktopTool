# 13 正式发布与整体验收

日期：2026-10-03。对应 [任务 13](../../.scratch/desktop-folder/issues/13-publish-and-accept.md)，实施和两轴审查基点为 `90b35014797f32e628296a527aaf58a98417bd79`。

## 交付

最终解压目录为 `releases/KageDesktopTool-win-x64-1.0.0/`，压缩包为相邻的 `KageDesktopTool-win-x64-1.0.0.zip`。ZIP 相邻有 `.zip.sha256`，包内有逐文件 `SHA256SUMS.txt` 和中文 `使用说明.md`。发布文件、候选包、隔离夹具及日志均由 `.gitignore` 排除，不将运行时二进制提交进源码库。

最终 ZIP 为 73,943,522 字节（约 70.5 MiB），SHA256：`43B66196E1B0904A7F39C41E8BE59A2A914E8CE26D161628FBBF2993FF592E7A`。实际解压后 483 个清单文件逐项 SHA256 一致，含空格解压目录内 EXE 直接启动并装入该目录的运行时。

发布配置 `WindowsX64` 固定 Release、win-x64、自包含及 apphost，保留 WPF、Shell COM 和反射依赖，不裁剪、不合并单文件。实际装入的 `System.Private.CoreLib.dll` 位于包内；不依赖开发机全局 .NET。EXE 的内置图标逐像素与 D 资源一致，四套应用／托盘资源可加载。日常使用只需完整解压后双击 EXE；维护者复现发布需 .NET 10 SDK 和 NuGet 可访问。

发布方式及运行时更新行为依据 [Microsoft 自包含发布文档](https://learn.microsoft.com/en-us/dotnet/core/deploying/#publish-as-self-contained)。自包含运行时随包固定，运行时修补需重新发布并更换完整包。`publish.ps1` 拒绝覆盖已有发布位置，运行包内依赖检查后才生成清单和 ZIP。

正式状态仍为 `%LOCALAPPDATA%\KageDesktopTool\workspace.json`，内容按保存的存储根目录关联；程序包、状态和内容独立。程序普通启动分支保持实际用户状态路径，不创建示例或导入原型。升级说明要求先关闭旧包自启并退出，再在最终位置启动、核对现有状态并重新应用自启开关。

## 组合检查与回归

最终版本目录串行运行 `verify-release.ps1`，全部 11 项 ExitCode=0。当前系统实际记录为 Windows NT `10.0.26300.0`。Release 发布通过，警告按错误处理，无警告／错误。

- 完整 52 项业务检查通过，使用共用 `IDesktopWorkspace` 及既定状态／文件系统／Shell 故障边界。真实跨本地磁盘、整目录回收、十二阶段进程中断、显示环境及 DPI 轨迹回归均通过。
- 包内 `--release-workflow-check` 从随机隔离初次启动开始，依次创建两个 Folder、批量移入文件／真实链接／子目录、Folder 间冲突编号、批量移出、列表及展开、保存外观／图标、改名、保留内容、迁移活动及保留目录与链接、应用当前包自启、重启读取设置，再注入移动后失败／恢复失败并重启核对、恢复旧根目录。实际文件字节、源消失、快捷方式目标和暂停修改一致，随机自启项退出时清理。
- 现有 `session` 检查在发布 EXE 下覆盖热键占用与恢复、托盘创建、包内重复进程转交设置、关闭设置继续运行、窗口释放、真实随机 HKCU 自启开关及内容保留。`folder-action` 覆盖真实用户桌面链接与整目录原生回收；`root-migration` 和 `migration-recovery` 覆盖真实设置入口、当场取消、定位及重试恢复。
- `content-layout`、`appearance` 和 `file-move` 检查真实多 Folder 展开、原生网格／列表、内容刷新、外观预览和取消、调色盘 owner／焦点、图标资源、真实桌面／Explorer 多选 OLE、冲突、取消和 ACL 失败。`desktop-recovery` 检查 Win+D 进出、实际鼠标输入、带保护的 Explorer 重启、托盘恢复及恢复后的双向 OLE。
- `display-dpi` 记录当前实际单屏 `(0, 0, 2560, 1528)`，150%／144 DPI，检查原生尺寸、选择、拖动、调尺寸及显示通知；125%／150%／200% 注入仅验证换算和命中。

实际多屏、负坐标屏幕、混合 DPI、物理拔屏、主屏及系统缩放／分辨率切换**未验证**。业务轨迹和注入度量不代表这些硬件配置已验收。ARM64、32 位 Windows 和其他系统版本亦未验收。

## 普通启动与实际用户状态

最终 EXE **不带检查参数**普通启动后，再次普通启动同一 EXE，第二进程正常结束，设置窗口属于第一实例。通过 UI Automation 核对根目录输入和全部已有 Folder 行来自实际用户保存状态；关闭设置后第一进程继续运行，再从真实托盘菜单“退出”正常结束。实际设置截图已查看，根目录、既有两个 Folder、可用入口及显示状态正确。

普通启动前后，实际状态与备份、原型设置／状态／报告／预览及原型数据文件的 SHA256 完全一致；实际内容的路径、长度及修改时间相同，正式 HKCU 自启项相同。该检查不移动个人内容、不切换正式自启；发布位置的自启开启／关闭另以随机启动项验证。没有自动生成示例、修改原型或覆盖实际用户偏好。

## 验收时序修正

首次发布会话中，Explorer 前台切换后立即核对拖出目标失败，单独复跑通过。检查现在等待鼠标位置实际命中指定 Explorer HWND／子窗口，再执行原有实际目录断言；没有修改目标识别或文件移动实现。

后续调色盘前台焦点检查连续两次失败：owner 正确，前台 HWND 始终为 `2493898`。只读定位确认其所属为 Explorer 的“系统托盘溢出窗口”，是前次托盘验收遗留的弹层。检查现在模拟用户点击外观窗口标题，确认 owner 真正取得前台，再打开原生调色盘；原有 owner／焦点断言通过，检查退出恢复鼠标位置。没有通过强行设置调色盘前台或放宽断言绕过失败。

早期 SDK／NuGet 检查受到沙箱缓存和 Windows 服务权限限制，下载和实际会话检查在当前用户完整会话运行。不能据此将受限沙箱错误当作产品错误。

## 复现与证据

在仓库根目录运行：

```powershell
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File publish.ps1 -OutputDirectory releases/KageDesktopTool-win-x64-1.0.0
rtk proxy dotnet run --project tests/Kage.Workspace.Checks --no-restore
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File verify-release.ps1
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File verify-user-launch.ps1
```

已存在的发布目录需改用新的 `releases` 子目录。后两项需当前用户交互式 Windows 会话，串行操作实际鼠标；Explorer 检查拒绝存在未完成文件操作的会话，并恢复先前可定位的 Explorer 窗口。普通启动检查要求已有实际用户状态、没有待恢复意图，且现有正式实例已退出。

日志在忽略目录 `.scratch/desktop-folder/verification/`：`13-release-summary.txt`、`publish-package.txt`、`release-workflow.txt`、`windows-session.txt`、各既有 Windows 会话检查日志，以及最终 ZIP 解压校验和普通用户启动证据。文件系统／恢复注入仅作用于随机夹具；正式工作区不会被用作移动或删除的夹具。

最终 ZIP 证据为 `13-archive.txt` 和 `13-extracted-package.txt`；普通启动证据为 `13-user-launch.txt` 与 `13-实际用户设置.png`。解压检查日志覆盖通用 `publish-package.txt`，而最终版本程序路径及退出码仍完整保存在 `13-release-summary.txt`。
