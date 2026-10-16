# 毛玻璃 01：实时背景与完整样式保存

2026-10-14，在 Windows 10.0.26300、单屏 2560×1600、工作区 2560×1528、150% 缩放的真实交互式会话完成本票检查。本文不把当前单屏结果扩大为其他 Windows 版本或硬件配置的验收。

## 选定方案与前置证据

`FrostedBackdrop` 创建由实际 Explorer 桌面根窗口拥有的原生背景窗口。它使用 `DWMWA_USE_HOSTBACKDROPBRUSH=TRUE`、`Windows.UI.Composition.CreateHostBackdropBrush()` 和 `DesktopWindowTarget(topmost:false)`；现有透明 WPF Folder 作为其子窗口，位于背景 Composition 上方。文字、图标、按钮和内容输入沿用现有 WPF 入口。关闭效果或初始化不可用时使用原来的 Explorer 子窗口和背景颜色、透明度。

背景窗口设置 `WS_EX_TOOLWINDOW`、`WS_EX_NOACTIVATE`，在桌面层级放置；`WM_MOUSEACTIVATE` 返回 `MA_NOACTIVATE`。真实鼠标点击能到达 WPF 前景，同时保持另一应用的前台焦点。实际原生 owner 经 `GetWindow(GW_OWNER)` 和 `GWL_HWNDPARENT` 核对为当前 Progman；`GetParent` 对 WPF 的 overlapped 窗口返回 0，不能用于否定其 owner。

首版统一采用系统 HostBackdropBrush 的固定模糊，不提供强度设置，也不额外施加颜色或材质强度。该 API 没有公开数值模糊半径，本文不编造内核参数。选定画面的 12 DIP 高频蓝白／绿白条纹被明显柔化，仍保留后方颜色；透明度为 100% 时 `Surface.Background` 的 alpha 为 0，仍实际显示模糊。着色与透明度继续由现有 WPF 背景层控制，0% 透明时 alpha 为 255。原生圆角区域直径为 `20 × DisplayScale` 物理像素，对应原有 10 DIP 圆角。

前置验证在实际 Explorer 宿主和随机隔离窗口进行，没有修改系统设置或重启 Explorer。仅顶层 API 返回成功没有作为交付证据；随后验证了真实桌面层级、Win+D 往返、其他应用覆盖、动态背景、移动、真实桌面图标、清晰前景和真实鼠标输入。

| 路线 | 实际结果 | 原始证据 |
| --- | --- | --- |
| Explorer child，layered／非 layered WPF，Accent 0／3／4／5 | API 返回 0；高频条纹保持清晰 | `01-child-*`、`02-inactive-*` |
| child 上创建 HostBackdrop | 属性设置返回 `0x80070006`；画面为黑块 | `03-hostbackdrop-child-*` |
| 顶层初始化 HostBackdrop 后再变为 Explorer child | 能留下模糊纹理，但后方绿变蓝后仍显示旧绿，移动只是移动旧纹理 | `21-*`、`22-*`、`24-*` |
| `GetDC(SHELLDLL_DefView)`＋SRCCOPY | 排除自身 layered 前景，读取动态非 layered 条纹及桌面图标；实际壁纸区域为黑色 | `06-*`、`08-*`、`12-*`、`14-*`、`15-*` |
| WGC Progman／图标及壁纸 child | Progman 取得真实壁纸和图标，但包含自身；child 的 CreateForWindow 返回 E_INVALIDARG | `30-wgc-0-Progman.png`、`probe.log` |
| 顶层设置捕获排除后挂为 child | 窗口／显示器捕获留下黑色孔洞，不能取得实际后方画面 | `31-wgc-affinity-*` |
| own child 加无重定向标志 | WPF 未保留该标志，实际可见前景仍进入捕获 | `32-*`、`probe.log` |
| 桌面拥有的独立背景＋透明 WPF child | 实际实时模糊、清晰前景、蓝绿动态更新、Win+D、其他应用遮挡、失焦、移动、物理点击均成功 | `40-*` 至 `45-*`、`probe.log` |

原始图片、探针源码和本机日志保存在被 Git 忽略的 `.scratch/folder-frosted-background/verification/`。`verification/probe/Probe.csproj` 保留前置探针；`all`、`focus`、`wgc`、`wgc-noredirect`、`desktop-owner` 模式分别覆盖上述路线。正式入口验证使用下列命令，不依赖一次性探针：

```powershell
dotnet run --project tests/Kage.Workspace.Checks/Kage.Workspace.Checks.csproj -c Release
dotnet run --project src/KageDesktopTool/KageDesktopTool.csproj -c Release -- --frosted-input-check
dotnet run --project src/KageDesktopTool/KageDesktopTool.csproj -c Release -- --frosted-input-check --unavailable
dotnet run --project src/KageDesktopTool/KageDesktopTool.csproj -c Release -- --appearance-check
```

## 业务与正式入口结果

业务检查通过 `IDesktopWorkspace` 和真实隔离 JSON、内容文件运行。首个切片先观察到新属性／API 缺失，再接通默认、预览和取消；第二个切片先观察到“仅毛玻璃开关发生变化时旧草稿仍能覆盖新设置”的失败，再将该字段加入样式并发保护。完整业务检查通过，覆盖默认关闭、真实旧配置缺省关闭、完整样式一次保存、保存故障重试、逐 Folder 重启恢复、过期会话、独立布局／查看变化保留、显示环境保护和实际内容字节不变。

正式 `StyleDialog` 通过系统鼠标与键盘运行，未注入 WPF 事件。`01-正式输入.txt` 记录以下通过结果：

- 实际开关立即预览，正式记录保持原值；100% 透明仍显示真实模糊，0% 透明完全使用当前颜色。
- 后方实际蓝白条纹切换为绿白条纹，Folder 内的真实屏幕像素 G 通道上升、B 通道下降，证明背景持续更新。
- Esc 取消释放背景并恢复原状态；真实应用按钮经历指定状态存储故障后保留可重试草稿，重试成功。
- 切换其他应用后继续显示效果；真实前景点击保持另一应用的前台焦点。
- 真实鼠标拖动保存最终位置并持续显示效果；真实双击快捷方式由 Windows Shell 启动隔离目标并写入目标标记文件。
- 真实折叠只保留头部；重建程序与工作区后恢复已保存的完整样式及实际内容。

代表性画面：`01-完全透明毛玻璃.png`、`01-完全不透明着色.png`、`01-动态背景绿.png`、`01-失焦持续效果.png`、`01-真实拖动落点.png`、`01-折叠头部.png`、`01-重启恢复.png`。

`01-不可用.txt`／`01-不可用保留意图.png` 使用已批准的 Windows 平台能力事实注入，检查原背景回退、“当前环境无法显示毛玻璃”提示，以及不可用期间保存开启意图、重启恢复。这是不可用轨迹的确定性检查，不是另一台不支持硬件的实机验收。没有将暂时不可用写入正式开关。

已有 `--appearance-check` 全部通过，包括真实调色盘、颜色同步、透明度、取消和托盘图标。该旧夹具原先连续创建两个相邻 Folder 后忽略展开失败，随后访问不存在的可见项目；本次仅通过业务布局入口移开第二个隔离 Folder，并明确断言展开成功，保留产品原地展开规则。

## 基础性能与后续边界

本机一次 18 步真实拖动轨迹墙钟 1308 ms，整个隔离 WPF 进程 CPU 813 ms、私有内存 163.5 MiB、GDI 212、USER 102。背景由系统 Composition 绘制，没有定时屏幕捕获或 CPU 图像模糊。上述数值包含隔离应用、动态图案夹具、图标及输入处理，不能当作持续静置平均值或 GPU 成本；本票未测 GPU 使用。未观察到拖动捕获、Shell 打开或样式输入失败。

四向完整视觉、多实例、Explorer 重建、显示器和 DPI 变化、资源失败恢复及更完整性能比较归 02；系统透明效果、高对比度、省电监听与恢复归 03。当前未验证多屏、混合 DPI、拔屏、HDR、第三方动态壁纸的所有实现或其他 Windows 版本。

02 还需实测 100% 透明时空白头部／展示部分的命中、拖放和选择：透明 layered 前景的空白像素可能命中原生背景窗口，当前证据只覆盖该极值的真实绘制；不得通过给着色层偷偷添加 alpha 来改变 100% 透明语义。运行中定位失败的背景清理、回退重挂与无残留检查也归 02／03。

## 主要平台依据

- [WPF 使用 Composition visual layer](https://learn.microsoft.com/en-us/windows/uwp/composition/using-the-visual-layer-with-wpf)：子 HWND 的 DesktopWindowTarget 与 WPF 前景分层。
- [CreateHostBackdropBrush](https://learn.microsoft.com/en-us/uwp/api/windows.ui.composition.compositor.createhostbackdropbrush)：取得窗口绘制前的后方画面。
- [DWMWINDOWATTRIBUTE](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute)：Windows 11 的 HostBackdrop 属性。
- [GetParent](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getparent)：与 overlapped／popup owner 查询的区别。
- [SetWindowDisplayAffinity](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity)、[BitBlt](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-bitblt)：捕获排除和 layered 捕获的限制；实际结果仍由上述探针确定。
