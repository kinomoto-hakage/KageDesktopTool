# 统一任务与跨虚拟桌面入口的 Windows 接入边界

核查日期：2026-10-24（Asia/Shanghai）。本报告只核对第一方文档与当前源码，未启动工具、操作任务视图、切换虚拟桌面或编写原型。

## 结论与证据强度

公开 API 提供窗口归属、任务栏项目管理和自定义预览等组成能力，但本轮没有找到承诺将桌面子窗口、辅助对话框及常驻切换入口在当代 Win+Tab／Alt+Tab 中完整合成一个任务的文档合同。不能将用户已确认的产品目标写成“技术上已经验证”。下列“尚未证明”不等于“已证明不可实现”。

| 产品目标 | 本轮能确认的事实 | 尚不能据此承诺的部分 |
| --- | --- | --- |
| 多个 Folder 与便签区合为一个切换入口 | 原生窗口归属及任务代表选择有第一方说明；AppUserModelID 能管理任务栏应用身份 | 同进程、相同 AppUserModelID 或只改 ShowInTaskbar 不证明 Win+Tab 数量正确 |
| 设置与样式等对话框归入同一任务 | WPF Owner 表示窗口所有权，原生 parent 与 owner 有区别 | 当前 Shell 是否替换代表、改变标题／图标或另列任务需要实测 |
| 无常驻任务栏按钮 | 可以管理任务栏项目；TOOLWINDOW 会同时排除任务栏与 Alt+Tab | 隐藏任务栏按钮并始终保留一个切换入口的组合不由单一属性保证 |
| 当前可见面板组合预览 | DWM 公开接口允许应用提供自定义位图及请求刷新 | 当前 Win+Tab 是否采用、跨屏构图、隐藏后旧图清除及对话框代表行为需要实测 |
| 所有虚拟桌面共用同一入口 | IVirtualDesktopManager 可查询及移动指定顶层窗口 | 该公开接口没有“固定到所有桌面”方法，不能由其保证全桌面入口 |
| 任务关闭等同正常退出 | WM_CLOSE 可以由应用处理后再决定是否销毁窗口 | Shell 实际把关闭送到哪个代表、模态状态及保存失败下能否保留入口需要实测 |

上表是以下资料与本地源码的归纳；“代表窗口＋面板排除＋自定义预览”仅是候选方向，不是实现方案已经确定。

## 已证实的第一方事实

1. **任务栏身份不是任务合并合同。** AppUserModelID 用于将进程、文件与窗口归到同一应用，文档讨论任务栏按钮及 Jump List。相同身份并不在该文档中承诺 Win+Tab 只保留一个窗口任务。[Application User Model IDs](https://learn.microsoft.com/en-us/windows/win32/shell/appids)
2. **TOOLWINDOW 不适合直接充当需要显示的统一入口。** 扩展样式文档明确其不出现在任务栏或 Alt+Tab。APPWINDOW 强制可见顶层窗口显示在任务栏；因此这两个样式不能单独同时满足“切换列表有一个、任务栏始终零按钮”。[Extended Window Styles](https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles)
3. **WPF 属性只有有限保证。** ShowInTaskbar 管理任务栏按钮，设为 true 也会出现在 Alt+Tab；文档未说 false 必然排除所有任务切换入口，更没有为本项目的 Explorer 子窗口保证现代 Win+Tab 行为。[Window.ShowInTaskbar](https://learn.microsoft.com/en-us/dotnet/api/system.windows.window.showintaskbar?view=windowsdesktop-10.0)
4. **任务栏删除可单独请求，切换列表结果仍未知。** ITaskbarList.DeleteTab 的文档合同是删除指定窗口的任务栏项，没有承诺同时保留 Alt+Tab 或 Win+Tab。把其作为候选需要验证 Shell 重建、重复展示及激活后的稳定性。[ITaskbarList.DeleteTab](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-itaskbarlist-deletetab)
5. **历史 Alt+Tab 算法只能解释概念。** Raymond Chen 介绍过从 root owner、last active popup 选择窗口代表的逻辑，同时明确算法是可变实现细节，文章针对 Classic Alt+Tab；不可拿它当作 Windows 11 任务视图规范。[Which windows appear in the Alt+Tab list?](https://devblogs.microsoft.com/oldnewthing/20071008-00/?p=24863/)
6. **所有权与桌面父子关系须分别处理。** WPF Owner 指窗口所有者；GetAncestor 区分 parent 链与包含 owner 的根链。SetParent 不自行改 WS_CHILD／WS_POPUP，且跨不同 DPI awareness 模式可能产生失败或重置。这些事实不能证明 Explorer 接入在所有 Windows 版本受稳定支持。[Window.Owner](https://learn.microsoft.com/en-us/dotnet/api/system.windows.window.owner?view=windowsdesktop-10.0)、[GetAncestor](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getancestor)、[SetParent](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setparent)
7. **自定义缩略图与 Peek 有公开接口。** DwmSetIconicThumbnail 为本进程窗口／tab 提供 32-bit 位图，需配置 FORCE_ICONIC_REPRESENTATION 和 HAS_ICONIC_BITMAP；通过失效请求更新，但缓存满时 DWM 不一定请求新图。应用必须管理位图生命周期。[DwmSetIconicThumbnail](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmseticonicthumbnail)
8. **预览不是同一个画面合同。** DwmSetIconicLivePreviewBitmap 描述任务栏悬浮或 Alt+Tab 缩略图获得焦点时的全尺寸 Peek，位图不大于窗口／框架客户区；这与小缩略图分别处理。该文档没有承诺当代 Win+Tab 一律调用相同预览路径。[DwmSetIconicLivePreviewBitmap](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmseticoniclivepreviewbitmap)
9. **公开虚拟桌面接口边界明确且较窄。** IVirtualDesktopManager 仅列出 GetWindowDesktopId、IsWindowOnCurrentVirtualDesktop、MoveWindowToDesktop；其中没有创建／枚举桌面、切换通知或固定到所有桌面的方法。这只能说明该接口不提供这些方法，不能证明其他办法不可能。官方还要求避免程序自动把用户切到另一桌面。[IVirtualDesktopManager](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ivirtualdesktopmanager)
10. **系统设置影响“计数范围”。** Windows 允许 Alt+Tab 展示当前桌面或全部桌面的窗口，任务栏也有独立范围设置；每个虚拟桌面建立一个代表窗口的候选方向因此必须检查“全部桌面”模式下是否重复。本轮不修改这些系统设置。[Configure Multiple Desktops in Windows](https://support.microsoft.com/en-au/windows/experience/configure-multiple-desktops-in-windows)
11. **移动／关闭虚拟桌面不能等同删除内容。** 官方说明 Task View 可移动窗口到其他桌面；关闭虚拟桌面会将应用移到其他可用桌面，不是退出应用。[How to Multitask in Windows](https://support.microsoft.com/en-us/windows/how-to-multitask-in-windows-b4fa0333-98f8-ef43-e25c-06d4fb1d6960)
12. **退出与激活都有应用侧责任。** 应用可处理 WM_CLOSE 后才销毁窗口；SetForegroundWindow 受前台权限等系统规则限制。因此“正常退出保护”及“返回桌面聚焦最近面板”都需要真实 Shell 触发验证。[WM_CLOSE](https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-close)、[SetForegroundWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setforegroundwindow)

## 当前源码只读事实

核查工作区 HEAD：`eeb9247e2a377dada20e3d19e0374b9327e7ea64`。下列源码均无相对 HEAD 的未提交修改；文档与规划已有未提交改动未带入研究分支。这是本地未发布源码上下文，不宣称它与远端 main 相同。

| 入口 | 观察 |
| --- | --- |
| `src/KageDesktopTool/FolderHeader.cs:65–72` | 每个 Folder 有带自身名称的 Title，ShowInTaskbar=false、ShowActivated=false；这不构成当前 Shell 列表的观察证据 |
| `src/KageDesktopTool/Runtime.cs:116–117` | 1×1 的控制器窗口设置 ShowInTaskbar=false、WindowStyle.ToolWindow 并取得 HWND；不是现成的已验证统一入口 |
| `src/KageDesktopTool/WindowsDesktop.cs:31–40` | 普通接入把窗口改成 WS_CHILD 后 SetParent 到 Explorer 宿主 |
| `src/KageDesktopTool/NativeFolderHost.cs:98–105` | 毛玻璃路径再把前景挂到背景窗口；不能把某个 HWND 的静态样式等同所有路径的任务表现 |
| `src/KageDesktopTool/FrostedBackdrop.cs:50` | 背景原生窗口创建使用 `0x08000080`（NOACTIVATE＋TOOLWINDOW）和 POPUP 参数，之后还有接入流程；此处只记创建参数 |
| `src/KageDesktopTool/Runtime.cs:505–516,542` | 有独立“新建 Folder”模态窗口及 Application.Shutdown 调用；新增统一退出前须重新检查对话框和各生命周期路径 |

用户报告的“每个 Folder 各占一个任务”仍是有效需求背景，但本研究没有启动现场复现，未定位究竟是哪条窗口路径进入列表。也未把规划中的便签区当成已存在的受测窗口。

## 待交互决策处理的边界

- **“常驻任务栏按钮”有两种含义**：不自动固定到任务栏，或运行时也绝不出现按钮。两者需要的技术约束不同；应请用户明确，不替其扩大第一轮选择。
- **跨虚拟桌面固定方式尚未获证明**：官方公开 IVirtualDesktopManager 没有固定方法。本轮没有核实某个未公开 COM 接口的版本兼容性，也不建议把博客里的内部 GUID 当成稳定合同。若候选需要系统“在所有桌面上显示”设置，应由后续决策明确是否接受人工配置，不能暗中执行。
- **全桌面入口与对话框优先存在交叉情景**：对话框留在另一虚拟桌面时，是移动对话框到当前桌面、让用户切过去，还是采用其他方式，需要先查证候选能力再决定，不自动跨桌面抢焦点。
- **预览应独立合成应用可见状态**是需求方向，不是本次实现建议。全屏截图会夹入其他应用，旧缓存可能留下已隐藏便签正文；只有原生核对正确后才可声称符合“隐藏即不预览”。
- 第一轮选择保持有效；如果真实验证发现条件无法同时满足，应回到交互票呈现限制和替代选项，不自行缩减为任务栏分组、零入口或多个重复入口。

## 实施前必须取得的最小真实 Windows 证据

以下是后续候选接入验证门槛，当前全部未执行；不是本次建原型或修改产品的授权。

| 场景 | 必须观察的结果 |
| --- | --- |
| 0／1／多个 Folder，普通与毛玻璃路径，便签固定／收纳 | Win+Tab 和 Alt+Tab 分别恰好一个入口；任务栏按已确认含义核对 |
| Folder 全体隐藏、所有面板暂不可见 | 仍有统一入口，预览不泄露隐藏正文；选择入口不自行改变显隐 |
| 设置、样式、原生颜色／文件选择及错误窗口 | 单一入口的标题／图标／代表与激活行为符合合同，关闭目标和保存保护明确 |
| 输入／粘贴后立即从任务视图关闭，注入保存失败 | 正常退出保存；失败保留程序和输入，不误报成功或删除内容 |
| 两个虚拟桌面，系统 Alt+Tab 当前／全部桌面两种范围 | 每个使用情景无重复／失踪入口，切换不改变内容，激活不意外跳桌面 |
| 新建、关闭虚拟桌面，移动任务及固定选择 | 不新建第二套数据、不丢窗口／布局、不把桌面关闭当退出 |
| 双屏不同 DPI，跨屏面板、断开／接回显示器 | 组合预览无异位，单入口计数及正常布局恢复正确 |
| Explorer 重启、锁屏恢复、程序重启及接入失败 | 无永久幽灵／重复入口；托盘和保存数据可恢复，失败明确 |
| 进入／退出任务视图前后编辑、拖放、缩放及收纳计时 | 输入与操作保护符合各既有合同，不因系统切换吞输入或丢草稿 |
| 快速隐藏／删除／收纳后立即再看缩略图和 Peek | 不复用已经不应展示的内容；小图、Peek、Win+Tab 各自观察 |

记录确切 Windows build、程序／包摘要、窗口父／owner／样式与 Shell 实际列表、原生输入和截图；按仓库 Windows 验证流程串行执行并完成系统恢复。多屏硬件缺失、系统版本未覆盖或无法验证的候选必须列为限制。

## 研究发布边界

研究分支：`research/folder-task-view-20261024`，基于远端 main `06ae917790501eb599a9bd8600cfd8853b5f8a77`，只增加本报告；不包含当前未发布产品历史，不创建 PR、不合并主线。固定研究提交由研究票回填。本报告解答第一方资料与当前源码能证明的边界，不能替代用户剩余交互选择、原生接入验证或产品验收。
