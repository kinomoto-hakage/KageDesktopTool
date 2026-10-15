# 毛玻璃 03：系统暂停、原因与自动恢复

2026-10-14 至 2026-10-15，在 Windows 10.0.26300、单屏 2560×1600、工作区 2560×1528、150% 缩放的真实 Windows 会话检查。本票基于 01 的实际 HostBackdrop 方案；四向、多实例和 Explorer 重建的最终组合验收待 02 合并后继续，本文没有把该组合记为已通过。

## 状态与平台接入

保存的毛玻璃开关仍属于 `FolderRecord`。`FrostedEnvironment` 只存当前 Windows 平台事实，不写入正式配置：透明效果关闭、高对比度开启、省电开启和环境不可用可同时存在，样式窗口列出全部仍成立的原因。只有完整条件允许且原生资源成功建立才显示效果。失去焦点不产生暂停原因。

Windows 透明效果使用 `UISettings.AdvancedEffectsEnabled` 和同实例的变化事件；高对比度使用 `SystemParameters.HighContrast` 与 `StaticPropertyChanged`。Windows 11 Build 26100 起通过 `PowerSettingRegisterNotification` 监听 `GUID_ENERGY_SAVER_STATUS`，DWORD 0 为关闭，1 为标准节能，2 为高度节能，本功能在 1／2 都暂停。旧系统保留 `PowerManager.EnergySaverStatus` 及其事件，避免用旧 Battery Saver 的状态遗漏现代标准节能。注册失败、初值尚未到达或未知状态都回退到“当前环境无法显示毛玻璃”，不假定为关闭；原有三秒轮询重试注册和资源创建。退出解除全部通知并释放 callback 结构内存。

系统通知只投递 UI Dispatcher 的统一刷新。该刷新独立于目录刷新、拖动和内容输入的常规 gate；它保留当前拖动几何，在样式窗口存在时重新取得 `Interaction.Preview`，由业务边界核对草稿是否仍然有效。过期草稿不能在系统变化时重新盖住更新的正式样式。暂停将清晰前景重新挂回 Explorer，再销毁不需要的背景窗口；条件恢复重建效果，失败重试由系统通知／轮询触发，不在每次鼠标移动分配资源。

## TDD 与确定性轨迹

首个切片先观察到环境事实入口／类型缺失的编译失败，接通后验证多条件叠加及部分解除。真实透明效果切片在 `AdvancedEffectsEnabled=False` 时先观察到 Runtime 仍保留效果、缺少原因的失败，再接入实际 Windows 监听。第三个切片先复现“过期关闭草稿在环境变化时保留在 Header 缓存”的失败，再改为从当前有效 `Preview` 刷新。

`--frosted-system-check` 通过正式 Runtime、Folder 和 StyleDialog 观察外部效果与保存结果，环境事实由已批准的 Windows 平台边界提供。完整通过结果在 `03-确定性轨迹.txt`，包括：

- 透明效果、高对比度同时限制；部分解除仍暂停，全部解除自动恢复并清除旧原因。
- 标准／高度节能、能力失效和未知事实都回退；正式启用意图保留。
- 有效关闭草稿、颜色和透明度在后台刷新期间保持；取消按当前环境恢复正式样式。
- 暂停期间一次保存完整样式，恢复后显示最新应用值；关闭草稿在恢复后仍关闭。
- 过期草稿不能覆盖更新的正式样式；暂停状态下重启保留完整正式意图，限制解除后自动恢复。
- 连续 15 次暂停均确认旧背景 HWND 已被 Windows 销毁，前景仍可见，每次恢复都重新生效。本次进程 GDI 57→57、USER 38→38，没有观察到随循环增长。

原有完整 `Kage.Workspace.Checks` 通过，继续覆盖保存故障重试、旧配置默认、逐 Folder 独立样式、真实文件内容、过期会话、显示变化与恢复锁定保护。没有新增保存路径或绕过既有事务。

## 实际 Windows 设置

所有桌面检查串行运行，使用随机隔离工作区；现有正式发布程序没有被本票停止或改写配置。

| 条件 | 实际操作与事实 | 结果及证据范围 |
| --- | --- | --- |
| 透明效果 | 临时写入实际 `HKCU/.../Themes/Personalize/EnableTransparency` 并广播 `WM_SETTINGCHANGE/ImmersiveColorSet`；独立 `UISettings` 读数验证 True→False→True | Runtime 自动暂停／重建，打开的样式窗口实时说明原因；开关保持选中，前景 HWND 保留。`03-真实系统设置.txt` |
| 高对比度 | `SPI_GETHIGHCONTRAST` 记录原 Flags=126，再通过 `SPI_SETHIGHCONTRAST` 开启和恢复；实际 `SystemParameters.HighContrast` False→True→False | 自动暂停并出现高对比原因，解除后恢复。相同日志记录 API 结果；不是注入的高对比状态 |
| 现代节能 | 实际 Windows“电源和电池”页面，UI Automation 只读识别“始终使用节能模式”控件，系统鼠标物理点击开启／关闭，原 ToggleState=Off | 原生现代通知 0→1（STANDARD）→0；Folder 暂停并显示省电原因，关闭后自动恢复。`03-真实节能模式.txt`、对应开启／恢复 PNG |
| 现代初始通知 | 独立只读原生注册，不创建 Runtime 窗口、不修改设置 | 注册返回 0；真实 Type=0x8013、匹配 GUID、DataLength=4、状态 0。`03-原生节能初始化.txt` |

透明效果和高对比度通过实际 Windows 配置／API 改变，未声称是物理操作设置页；节能模式则使用实际设置页的物理鼠标，未通过 Registry 猜测或注入省电状态。当前接电仍成功触发标准节能，不能把 AC 供电当作不可测理由。HIGH_SAVINGS=2 在当前机器未实际触发，仅有确定性轨迹覆盖；旧 Windows 的 legacy 通知也未在另一台机器实测。

`finally` 恢复原透明设置并确认 `AdvancedEffectsEnabled=True`、高对比度 False；实际节能最终为 0，关闭本轮 Settings 窗口并恢复鼠标位置。首次夹具的同步主题调用曾阻塞 UI；核对原设置后仅结束该隔离检查进程，随后改为异步广播／SPI 调用，正常重跑完成。

主题恢复另有只读核对：当前 `CurrentTheme` 与 Windows 记录的 `Pre-High Contrast Scheme` 均为 `Custom.theme`，该文件最后写入 2026-10-14 22:06:07，早于本票首次系统设置检查；其中 Wallpaper 与当前注册值均为 `%SystemRoot%/system32/oem/wallpaper.png`。Flags=126 与原记录相同，Wallpaper Engine 的 `wallpaper64` 仍运行。未根据截图中的动态画面差异擅改主题、壁纸或重启第三方软件。证据为 `03-主题恢复只读核对.json`；本票最初没有额外保存第三方动态壁纸内部状态，因此不把上述核对扩为其所有状态的逐项证明。

## 截图与最终交接边界

原始日志与图片在本工作树被 Git 忽略的 `.scratch/folder-frosted-background/verification/`，合并交接时保留到集成工作树。真实节能截图清楚显示省电原因和恢复后的清空，但当时 Codex 主窗遮挡桌面 Folder；高对比开启的首张截图拍到 Windows“请稍候”过渡。它们不能单独证明桌面背景像素的视觉变化。夹具已为后续高对比截图加入稳定等待；最终组合检查仍需在可见桌面重新拍摄多个四向 Folder 的稳定暂停／恢复，包含 Explorer 重启期间不绕过系统限制。

未在本票声称完成多屏、混合 DPI、拔屏、其他 Windows 版本、高度节能实机触发或第三方动态壁纸全矩阵验收。平台实际视觉方案的先前依据见 [01 验证记录](frosted-01-style-and-persistence.md)。

可重复命令（后两项会实际临时修改 Windows 设置，须串行并等待 finally 完成）：

```powershell
dotnet build src/KageDesktopTool/KageDesktopTool.csproj -c Release
dotnet run --project tests/Kage.Workspace.Checks/Kage.Workspace.Checks.csproj -c Release
dotnet run --project src/KageDesktopTool/KageDesktopTool.csproj -c Release -- --frosted-system-check
dotnet run --project src/KageDesktopTool/KageDesktopTool.csproj -c Release -- --frosted-system-check --real-settings
dotnet run --project src/KageDesktopTool/KageDesktopTool.csproj -c Release -- --frosted-system-check --real-energy
```

平台依据：[PowerSettingRegisterNotification](https://learn.microsoft.com/en-us/windows/win32/api/powersetting/nf-powersetting-powersettingregisternotification)、[Microsoft 原生现代节能示例](https://github.com/microsoft/Windows-classic-samples/blob/main/Samples/PowerSettingRegisterNotification/cpp/PowerSettingRegisterNotificationSample.cpp)、[Energy Saver 行为及接电启用](https://learn.microsoft.com/en-us/windows-hardware/design/component-guidelines/energy-saver)。
