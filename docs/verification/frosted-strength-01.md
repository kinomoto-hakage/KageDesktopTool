# 模糊强度 01：实时背景、正式滑块与完整保存

2026-10-16 至 2026-10-17，在 Windows 10.0.26300、单屏 2560×1600、工作区 2560×1528、150% 缩放的真实交互式会话验证。基点为 `5243828`。本文只说明票据 01 的结果；键盘说明、恢复默认和完整系统组合分别由 02、03 交付。

## 路线与生命周期

原 `CreateHostBackdropBrush()` 能取得实时后方画面，但没有公开的可调模糊参数。本次先在同一 Explorer 宿主做实际探针：普通 `CreateBackdropBrush()` 在有重定向的原生背景中读取的是窗口底色；无重定向时虽然能透出后方图案，却连 WPF 子窗口前景也不显示，因此没有将其作为产品路线。

选定路线为 `Windows.Graphics.Capture.CreateForWindow` 捕获真实 Explorer 根窗口，GPU 帧通过 Win2D `CanvasBitmap`、`GaussianBlurEffect`、`CanvasSwapChain` 与 `CreateCompositionSurfaceForSwapChain` 显示在原生背景中。背景继续是 Explorer 拥有的独立顶层窗口，清晰 WPF 前景继续作为它的子窗口，沿用原来的命中、鼠标捕获和 OLE 转发。它们不属于被捕获 Explorer 的子窗口内容树，因此不会递归采入自身。实际 PNG 中前景文字和文件图标清晰，背景持续变化。

同一 Explorer 根窗口共用一个捕获源和 Direct3D／CanvasDevice，各 Folder 有独立的背景 swapchain、位置及强度。帧始终留在 GPU，没有生产代码中的屏幕像素 CPU 回读、静态壁纸文件或定时屏幕截图，也没有设置 `WDA_EXCLUDEFROMCAPTURE`。普通截图中仍能看到 Folder。

按平台合同先请求 Borderless 访问，再设置捕获会话的 `IsBorderRequired=false`。本机完整信任的未打包应用没有弹出许可窗口，真实截图也没有全屏捕获黄边。异步访问尚未就绪时保持既有着色回退，访问完成通知立即通过 Runtime 原环境刷新入口恢复有效草稿；拒绝或故障请求不会永久缓存，后续环境轮询可以重试。捕获源失败时不继续增加新引用，现有实例回退并释放后可重建。关闭效果、删除实例和退出都会移除渲染订阅；最后一个引用释放捕获会话、帧池、GPU 帧及设备。

## 实机基准与映射

探针在固定位置、透明度、缩放及多频蓝白图案下保存原固定效果与 GaussianBlur 参数 0、3、8、16、32、64、96 的蓝／绿实际屏幕图。图案包含 8、24、64、128、256 DIP 周期，避免高频条纹在多个强度下都成为纯色而无法校准。前景在 `SourceInitialized` 中挂接，实际显示红色文字；最早没有显示前景的探针结果未用作前景验收。

在原始 2560×1600 PNG 的 ROI `x=[1600,2200), y=[300,1120)` 内，每 4 像素取样，共 92250 个 RGB 通道样本，排除前景文字与背景边缘。相对原固定效果的 RMSE 如下（通道范围 0–255）：

| 探针 GaussianBlur 参数 | 0 | 3 | 8 | 16 | 32 | 64 | 96 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| RMSE | 85.620 | 71.571 | 50.269 | 27.811 | 2.859 | 30.511 | 40.497 |

32 的平均通道偏差为 0.009。较宽条纹行 `y=400/600/800/1000` 的红通道标准差：原固定效果为 `0 / 15.620 / 71.077 / 96.078`，参数 32 为 `0 / 11.705 / 66.332 / 93.851`，参数 96 为 `0 / 0 / 2.646 / 22`。据此选定 32 作为本机 50% 的经验观感基准，96 作为已实测的 100% 产品上限；没有声称它们是 Windows 固定材质的内部参数或平台 API 上限。

在 150% 缩放下，产品将 0–50% 映射为 `32 × 强度 / 50`，50–100% 映射为 `32 + 64 × (强度−50) / 50`，再按 `目标缩放 / 1.5` 换算；极高缩放统一限制换算系数，使上限不超过效果 API 的 250，保持全范围单调变化。0% 为真实清晰采样，50% 两侧均有实际调整空间。百分比表示相对柔化程度，不是背景 alpha 或固定物理像素半径；其他硬件缩放的完整验收仍归 03。

探针源码、PNG 和日志保留在工作树被忽略的 `.scratch/folder-frosted-strength/verification/probe/`。代表性文件为 `10-GPU-原固定基准.png`、`11-GPU-sigma-0-蓝.png`、`11-GPU-sigma-8-蓝.png`、`11-GPU-sigma-32-蓝.png`、`11-GPU-sigma-64-蓝.png`、`11-GPU-sigma-96-蓝.png` 和对应 `12-GPU-*-绿.png`。最后一轮 859 帧无错误，`BorderRequired=0`，移动后重新采样当前位置。

## 状态与正式入口

业务 TDD 复用已批准的 `IDesktopWorkspace`／`AppearanceInteraction` seam 与真实隔离 JSON。三个切片分别先观察到缺少强度接口、仅强度变化没有使旧草稿过期、无效 JSON 范围没有锁定恢复的失败，再增加对应实现。CI 的 29 项隔离业务检查通过，覆盖默认 50、开关默认关闭、真实旧 JSON 缺字段、显式 0／100 往返、非法范围及类型、预览与取消、会话结束、完整保存故障重试、过期保护、独立布局／查看变化、重启、有效备份恢复和内容字节不变。

正式样式窗口在开关下显示整数滑块和只读百分比；关闭时置灰但保值，平台不可用但用户已勾选时仍可调节并保存。新检查 `--frosted-strength-check` 通过真实鼠标与系统按键运行正式窗口，不注入 WPF 事件。固定宽边缘图案在 0／25／50／75／100% 下的实际红通道标准差依次为 `127.429 / 123.590 / 119.681 / 111.347 / 102.943`，每档都验证蓝→绿实时变化。正式 PNG 保留五档蓝／绿图、透明着色、清晰文件图标和只读数值。

同一轨迹还验证独立开启的 25% Folder、后台刷新、开关保值、Esc 与 Alt+F4 取消、指定存储故障后的真实应用重试、0% 重启、完全不透明时保存 100% 后重启、提高透明度重新显现模糊，以及平台不可用期间保存与重启。后者使用已批准的平台能力事实，不冒充另一台不支持硬件的实测。

一次连续 12 次全范围鼠标拖动耗时 7092 ms，进程 CPU 844 ms，私有内存从 325.3 降至 270.5 MiB，GDI 248、USER 121；这些是本次夹具观察值，不是帧率门槛或持续 GPU 成本。真实透明头部拖动回归耗时 1226 ms、CPU 953 ms，与既有同类夹具没有观察到明显冻结。截图和测试结束后恢复临时窗口、鼠标与 GPU 资源，未关闭用户正在运行的正式实例。

## 运行记录与边界

检查由 `tools/windows/verify-ci.ps1` 先完成仓库约束、29 项隔离业务、Windows Rebuild（0 警告、0 错误）和执行器契约，再通过 `verify-release.ps1` 在真实桌面串行运行。运行记录保存工作树修改、EXE／DLL／完整包摘要、PID、退出码及逐次证据目录；不能仅凭基点提交判断工作树中二进制内容。

- `20261017T101935-85eeef64`：第一轮完整强度、蓝绿像素、保存／取消／重启与拖动轨迹通过。
- `20261017T102415-becb2caa`：补齐独立已开启 25% Folder 和不可用期间编辑保存的完整强度轨迹通过。
- `20261017T102450-59a3a3d5`：既有真实透明输入、失焦、拖动、Shell 双击、折叠展开及重启通过。
- `20261017T102502-431688dd`：实际透明 Thumb 捕获转移、系统取消模式与释放回归通过。
- `20261017T104126-6ba93afb`：完整真实 OLE 回归通过，包括 Explorer／Folder／桌面双向多选、落点、冲突、取消及真实 ACL 失败保留源。
- `20261017T104149-4805aec9`：完整内容、原生菜单、选择与 Shell 打开回归通过。
- `20261017T104610-6c3a3e49`：最终 CI 产物的完整强度轨迹通过，补齐已保存 75% 的实际折叠／展开；恢复后再次比较用户真实 JSON，全部状态保持一致。最终连续拖动为 7112 ms、CPU 1391 ms、私有内存 286.4→252.6 MiB。

最终 CI 构建目录为 `.scratch/check-runs/build-ef98dfed2c7b4a7b942f2fd290f1a694/`。此前一次在旧桌面批次尚运行时启动的 CI，在执行器契约阶段被会话互斥拒绝；已等待旧批次退出后完整串行复跑通过，没有将该次互斥拒绝记为产品失败或 CI 通过。

OLE 夹具最初把随机源图标留在默认左列，曾误拖遮挡图标的用户“工具”Folder；发现后停止检查，通过核对 PID／标题后的物理鼠标精确恢复 `(0,0)`，实际用户 JSON 比较确认其他状态均未变化。随后只安排随机桌面夹具到真实空白位置，物理源要求命中 `SysListView32`；拖出先滚动到图像并等待实际 HWND 稳定，再建立选择归属，避免前台通知在等待期间清掉夹具多选。失败记录未作通过证据，详细恢复快照与说明随本地验证资料归档。

原始运行记录位于工作树 `.scratch/check-runs/<运行 ID>/run.json`，截图和日志在其检查名／尝试编号子目录；清理工作树前由集成流程复制归档。Explorer 重启、真实系统限制全组合、四向布局与 DPI 全矩阵由 03 扩展；未声称已经验证多屏、混合 DPI、拔屏、HDR、其他 Windows 版本或第三方动态壁纸的所有宿主方式。

## 最终审查修复与复验

2026-10-17，集成基点 `4c50f256` 的独立审查发现正式强度夹具的点击和滑块拖动直接通过 `PointToScreen` 加固定延迟发物理输入，尚未遵守共用输入等待约定。修复仅涉及 `FrostedStrengthChecks`：点击通过 `DesktopCheckSession.InputPointAsync(Runtime, Window, FrameworkElement, Point)` 等待布局、手势结束和当前样式窗口的真实 HWND 命中；拖动目标按当前 Track 计算并等待同一窗口命中，按下前最后等待当前 Thumb 位置与命中稳定。既有 Folder 折叠／展开入口已使用对应的 Folder 重载。未修改产品界面、业务语义或增加镜像单元测试。

修复后完整 `verify-ci.ps1` 通过仓库约束、30 项隔离业务检查、Windows Rebuild（0 警告、0 错误）及执行器契约。构建目录为 `.scratch/check-runs/build-dd1cfbeff0c94ea09bf843cfe37cb63f/`，完整日志为 `.scratch/check-runs/review-ci.txt`。首次沙箱运行因映射临时目录中的文件 Move／Replace 被拒绝失败；随后在正常 Windows 用户环境完整复跑，未将沙箱失败当成产品结论。

独立完整包保留在主工作区 `.scratch/manual-review/20261017T153823-4c50f256-385e38fd/`；包内 `manual-review.json` 与运行记录保存基点和修复中的工作树状态。EXE SHA256 为 `D70874D1796AC7EDFF13C5432A193A5BA51CD13804A5466EB923D4079D589C4D`，完整包摘要为 `FBB371F2E7CBCE8B182112B979E1A54C25F950ABE8BB78F914D98A8B5E0BEE8A`。

使用该原包运行 `verify-release.ps1 -OnlyCheck frosted-strength`，运行 ID `20261017T153845-111ea7a3`，检查退出码 0。完整轨迹再次通过五档蓝／绿实时像素、完整事务、重启恢复、独立 Folder、透明度组合、连续 12 次全范围拖动及平台不可用回退；五档红通道标准差仍为 `127.429 / 123.590 / 119.681 / 111.347 / 102.943`。连续拖动因增加命中稳定等待耗时 13082 ms、CPU 1375 ms，私有内存 273.0→243.8 MiB，最终值与可操作性通过。

日志确认临时窗口、鼠标和 GPU 资源恢复完成，检查进程 PID 41500 已退出。原用户 1.1.1 实例 PID 27144、启动时刻 `2026-10-17 14:11:27.4181574 +08:00` 和路径均保持一致。正式状态目录的 `operation-results.json`、`workspace.json`、`workspace.json.bak` 三个文件前后 SHA256 全部一致；其中完整 `workspace.json` 为 `11CCD5B4D4F8666AB15F0E3D4FC6A1FFB6CE58AC291E139BD23813EEC11C0B2B`。前后摘要、实例身份和原 JSON 快照保存在 `.scratch/check-runs/review-safety/`，运行记录及截图位于 `.scratch/check-runs/20261017T153845-111ea7a3/`。本轮仅复验输入夹具修复，既有硬件与系统覆盖边界不变。

## 平台依据

- [CreateHostBackdropBrush](https://learn.microsoft.com/en-us/uwp/api/windows.ui.composition.compositor.createhostbackdropbrush)：窗口绘制前的后方来源与系统控制。
- [CreateBackdropBrush](https://learn.microsoft.com/en-us/uwp/api/windows.ui.composition.compositor.createbackdropbrush)：可用于效果图的来源；实际 Explorer 行为仍以本次实测为准。
- [Microsoft 的 HWND 捕获与 GPU swapchain 示例](https://github.com/microsoft/Windows.UI.Composition-Win32-Samples/blob/master/cpp/ScreenCaptureforHWND/ScreenCaptureforHWND/SimpleCapture.cpp)。
- [CanvasSwapChain](https://microsoft.github.io/Win2D/WinUI3/html/T_Microsoft_Graphics_Canvas_CanvasSwapChain.htm)：GPU 绘制与 Composition swapchain。
- [IsBorderRequired](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.graphicscapturesession.isborderrequired)、[RequestAccessAsync](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.graphicscaptureaccess.requestaccessasync)：无边框请求合同。
- [Microsoft 生成的 Capture ABI](https://github.com/microsoft/windows-rs/blob/master/crates/libs/windows/src/Windows/Graphics/Capture/mod.rs)：19041 目标缺少新接口投影时使用的原生签名；没有提高应用最低目标版本。
