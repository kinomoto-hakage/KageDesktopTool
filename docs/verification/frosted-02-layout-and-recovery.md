# 毛玻璃 02：四向布局、透明输入与桌面恢复

2026-10-16 完成本票收尾。真实环境为 Windows 10.0.26300、单屏 2560×1600、工作区 2560×1528、150% 缩放。业务检查使用已批准的 `IDesktopWorkspace`，绘制、鼠标、键盘、Shell、OLE 和 Explorer 生命周期均在真实 Windows 会话中串行检查。

## 实现与输入边界

头部和展示部分仍由同一个清晰的 WPF 窗口承载，背景使用 01 选定的独立 HostBackdrop 窗口。两个 HWND 共享 `FolderGeometry` 的可见物理边界；圆角沿用 10 DIP，四向展开没有第二块重复模糊层。位置仍为物理像素，尺寸仍为 DIP，没有新增布局规则或自动移动行为。

100% 透明的首次实测先出现空白头部无法拖动。此时 WPF layered 窗口的 alpha=0 像素让原生背景命中，单纯转发鼠标消息也会被 WPF 的原生来源判断拒绝。最终适配将真实原生输入按 `InputHitTest` 交给共用的头部布局和内容指针入口，后续继续使用正式鼠标捕获；没有用 `RaiseEvent` 注入事件，也没有给着色层偷偷添加 alpha。

- 空白框选、内容右键与头部拖动使用原来的选择、菜单和布局业务路径。原生背景也属于当前 Folder 的输入所有权，外部选择清除和跨 Folder 拖放不会误判它。
- 原生背景注册真实 OLE `IDropTarget`，只接收实际 `CF_HDROP`；文件移动仍由 `FileDrag` 和 Workspace 执行，保留源端效果标志的原有约束。注册、撤销和 OLE 初始化均随背景生命周期配对。
- 原生空白首次点击与有色选中项的后续 WPF 点击共用真实屏幕位置、时间和路径判断，保留双击打开。名称留白的真实悬停继续显示标准全文 ToolTip；离开与按下都按实际物理落点释放悬停捕获。
- 关闭或重建背景时先将前景重挂 Explorer，再释放旧背景；宿主不可用时先隐藏、移出旧背景。定位失败回到原颜色和透明度，释放旧背景，不遗留孤立效果或随背景销毁前景。重试沿用系统通知／轮询入口，不在每次鼠标移动时重建失败资源。

## 真实四向、内容与宿主结果

`02-four-way-input.txt` 的无追踪完整运行通过上／下／左／右头部与横排／竖排八组合，包含实时草稿、完整名称悬停、折叠展开、真实拖动和 Esc、头部分隔线、展示部分外角、网格与列表、全文末端滚动、选择清除、真实双击、保存失败重试及重启恢复。每次输入前都检查实际 HWND 命中，滚动目标还核对项目实例、视口内的图像中心与实际可视树命中。背景与前景的物理边界在四向同时运行的实例中一致。

`02-content-input-session.txt` 完整通过 alpha=0 的内容回归：网格／列表中未选与已选项目的图像／文字双击、真实程序和文档关联打开、子目录导航、Ctrl／Shift 点击和框选、边缘自动滚动、捕获丢失、原生传统菜单、实际属性／改名／删除、内部排序、跨 Folder 移动、滚动条与四档图标。没有用控件事件注入代替系统鼠标和键盘。

`02-ole-session.txt` 通过 Explorer 多选文件、快捷方式和子目录移入，跨 Folder 移动、Explorer 移出、桌面移入移出及落点、冲突处理、Esc 取消和真实 ACL 失败。核对真实内容字节、源消失或保留及目标路径，不仅检查 OLE 效果标志。

`02-desktop-recovery-session.txt` 通过开启毛玻璃时的真实 Explorer 重启、Win+D 往返、普通应用覆盖、托盘与热键保留、重建后的实际折叠／展开、拖动、尺寸调整及 OLE 移入移出。重启前拒绝有文件操作的 Explorer 会话，结束后恢复原目录窗口。`02-Explorer-恢复.png` 留存恢复后的实际画面。

`02-drag-resources.txt` 额外通过正式创建并删除开启毛玻璃的 Folder：旧背景 HWND 被 Windows 销毁，另一个开启实例保留原 Host 和效果，保留的内容字节不变。本轮随机桌面快捷方式经过路径核对后清理。

## 与 03 的真实系统组合及黑底修复

组合检查同时运行四向 Folder，其中一个正式关闭、通过真实开关预览开启。实际关闭 Windows 透明效果后，四个旧背景 HWND 全部销毁，清晰前景保留，样式窗口说明暂停原因。限制期间真实重启 Explorer，新宿主仍按当前系统限制回退，同时保留有效开启草稿和原正式记录；解除限制后恢复真实背景，取消草稿只恢复目标 Folder 的正式关闭值。

组合视觉检查曾发现高对比度解除后，`FrostedVisible` 和 HWND 挂接均为真，但四块背景实际全为 RGB `(26,42,51)`，没有跟随后方画面变化。Explorer 后的恢复画面仍有正常渐变，所以不能把该失败归为 Explorer 恢复，也不能用对象存在宣布视觉恢复。隔离诊断中，仅延后重新建立背景就使真实蓝／绿图案重新进入四个背景，WPF 前景未重建。

正式修复在 `VisualStyle` 主题通知后合并一次 1 秒、`ContextIdle` 优先级的背景重建；有布局／内容输入、文件拖动或宿主尚未恢复时继续延期。重建重新读取当前系统事实和有效草稿，保留 `SupportsFrosted` 限制。定时器随 Runtime 释放。1 秒是本机实测的通知合并安排，不是 Windows 对 HostBackdrop 就绪时间的 API 保证。[WM_THEMECHANGED](https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-themechanged) 和 [.NET SystemEvents 源码](https://github.com/dotnet/runtime/blob/main/src/libraries/Microsoft.Win32.SystemEvents/src/Microsoft/Win32/SystemEvents.cs)提供主题通知依据；真实材质恢复由下面的屏幕像素证明。

最终 `02-03-combined.txt` 自动恢复通过，四向真实蓝→绿像素分别为 `(99,115,154)→(99,137,124)`、约 `(102,118,169)→(102,149,127)`、`(103,119,169)→(103,149,128)`、约 `(102,118,169)→(102,149,127)`。正式验证没有手动重绑通过分支。四向暂停／恢复截图均要求实际无遮挡连续稳定后再采集，避免 Explorer 重开窗口或 Windows 过渡画面盖住目标。

真实透明、高对比设置的 `finally` 最终恢复为透明注册值 1、实际透明 `True`、高对比 `False`、现代节能 0。03 的完整确定性多限制、草稿、重启和 15 次资源释放轨迹在本票合并后再次通过；实际节能操作的证据见 [03 验证记录](frosted-03-system-fallback.md)。

## CPU、GPU 与资源观察

四个 Folder 后放置真实高频蓝白／绿白窗口，截取明、暗和动态画面。背景内条纹明显柔化、前景文字与原生图标清晰、四向交界连续，圆角外条纹保持清晰。原始图为 `02-四向蓝白背景.png`、`02-四向绿白实时背景.png`、`02-四向明背景.png`、`02-四向暗背景.png`。

以下为短时实际观察，包含隔离应用、图案夹具、Shell／WPF 及正常桌面负载，不是独占硬件基准或性能承诺。静置和动态图案来自同一轮，拖动为后续独立短轮，不能把跨轮差异当作因果收益。GPU 列是相应进程所有有效 GPU Engine 计数器值合计后的均值，不能当作整卡利用率；每阶段约 4—5 个采样时刻。

| 场景 | 应用 CPU／墙钟（ms） | 应用 GPU Engine 合计均值 | DWM GPU Engine 合计均值 |
| --- | --- | --- | --- |
| 开启，静置 | 875／6235 | 0.28% | 3.55% |
| 关闭，静置 | 16／6229 | 0.07% | 2.74% |
| 开启，动态背景 | 609／6220 | 1.40% | 5.20% |
| 关闭，动态背景 | 234／6227 | 0.83% | 3.24% |
| 开启，四段真实拖动（两轮往返） | 2359／6206 | 0.61% | 4.83% |
| 关闭，同样拖动 | 656／6239 | 0.59% | 4.07% |

静置／动态图案一轮的应用独占显存约 46.5 MiB，共享显存约 3.0—5.3 MiB；DWM 也保留原始独占／共享显存记录。拖动对照保持相同 alpha=0 和四次 50 物理像素位移，选择实际绘制的名称像素，每一步都核对正式落点；没有将穿透到桌面的动作当作关闭效果的拖动样本。采样分别保存在 `02-gpu-raw.csv`／`02-gpu-summary.csv`、`02-gpu-drag-raw.csv`／`02-gpu-drag-summary.csv`，仅保留 Counter Status=0 的有效实例样本。

四实例完成 30 轮逐个关闭／开启：预热后 handles/GDI/USER 为 `809/151/63`，10 轮 `816/138/65`，20 轮和 30 轮均为 `816/138/65`，最终仍为相同值；私有内存 116→132 MiB，后两组均为 132 MiB。没有观察到随轮次持续增长。关闭一个实例不改变其他实例的 HWND 和效果。上述数值不是所有 Windows、驱动或长时运行的无泄漏保证。

## 夹具修订、复现与未覆盖范围

没有为了新背景更改原地展开、布局冲突或选择语义。旧输入／OLE 夹具先通过业务布局给第二个 Folder 预留展开空间并断言展开成功；旧 DPI 夹具曾在鼠标内容输入尚未结束时注入度量，Render 正确受 gate 阻止，关闭效果也能复现旧矩形。等待完整松开与布局 idle 后，125%、150%、200% 注入及跨 DPI 预览、显示通知恢复均通过。注入只验证换算路径，不代表实际切换系统 DPI。

主要复现入口：

```powershell
dotnet run --project tests/Kage.Workspace.Checks/Kage.Workspace.Checks.csproj -c Release
dotnet build src/KageDesktopTool/KageDesktopTool.csproj -c Release
dotnet run --project src/KageDesktopTool/KageDesktopTool.csproj -c Release -- --frosted-four-way-check
dotnet run --project src/KageDesktopTool/KageDesktopTool.csproj -c Release -- --frosted-content-check
dotnet run --project src/KageDesktopTool/KageDesktopTool.csproj -c Release -- --frosted-ole-check
dotnet run --project src/KageDesktopTool/KageDesktopTool.csproj -c Release -- --frosted-dpi-check
dotnet run --project src/KageDesktopTool/KageDesktopTool.csproj -c Release -- --frosted-desktop-recovery-check
dotnet run --project src/KageDesktopTool/KageDesktopTool.csproj -c Release -- --frosted-combined-check
dotnet run --project src/KageDesktopTool/KageDesktopTool.csproj -c Release -- --frosted-lifecycle-check
```

宿主恢复和系统组合检查会真实重启 Explorer 或临时切换 Windows 设置，必须串行等待 `finally` 完成。`--frosted-lifecycle-check --drag-only` 只补拖动开关对照与删除资源释放，不重复 30 轮。原始日志、PNG 和采样脚本在工作树被 Git 忽略的 `.scratch/folder-frosted-background/verification/`；合并归档位于集成工作树相应 `verification/ticket-02/`，保留 01／03 的独立证据。

实际多屏、负坐标硬件、混合 DPI、拔屏、主屏切换、系统缩放／分辨率切换、HDR、其他 Windows／GPU 版本及第三方动态壁纸的完整行为没有实机覆盖。业务检查覆盖四向负坐标、空洞和混合 DPI 轨迹，当前真实绘制证明只限单屏 150% 与上述明确区分的注入度量。动态图案确实持续更新，不把后台存在 Wallpaper Engine 进程扩大为该第三方软件的完整验收。

## 最终双轴审查修复

2026-10-16 对集成提交 `53f9981` 完成 Standards／Spec 两轴审查后，集中修复以下发现：

- Standards 没有硬性违反，提出一项 P3 判断性重复：原生入口和 `ContentPointerInput.PointerDown` 各自维护双击时间／坐标，还使用不同的度量。现由共用 `PointerDown` 统一识别；原生入口只传 `WM_LBUTTONDOWN`／`WM_LBUTTONDBLCLK` 的原始点击信息，删除重复字段和两个未再使用的 Win32 Interop。
- Spec 提出一项 P2：原生 Thumb 捕获仅在鼠标松开时结束，没有处理 `WM_CAPTURECHANGED`／`WM_CANCELMODE`。真实按住完全透明分隔线、移动后通过实际 `SetCapture` 转移到控制器，首次明确复现 `activeInteraction` 不结束，证据为 `review-native-capture-red.txt`。

修复接通这两个系统消息，并在背景释放时清理原生 Thumb、按钮和右键状态。结束布局先取走原会话、清理本地状态，再退出当前 Win32 消息栈提交该会话，避免 `DestroyWindow` 的同步重入递归刷新，也不会把稍后的会话当作旧会话提交。普通松开仍在 `ReleaseCapture` 之前取走状态，产生的同步丢捕获消息不会重复提交。正常右键松开也先保留当前目标，再释放捕获，避免清理覆盖该次菜单选择。

最终针对性检查均退出 0，Release 构建为 0 警告／0 错误：

1. `--frosted-capture-check`：真实物理按下透明 Thumb，确认 Windows 捕获确实属于原生背景，再依次执行实际捕获转移、暂停销毁背景、`EnableWindow` 产生系统取消模式和正常鼠标松开。每次都结束布局、保存当前预览，并通过正式反馈确认只提交一次；后续真实折叠／展开、样式入口和内容字节继续正常。`review-native-capture.txt` 保留最终通过轨迹。
2. `--frosted-content-check --double-click-only`：网格／列表、未选／已选、图像／文字八种真实双击及 Shell 启动通过，见 `review-double-click.txt`。它验证用户操作结果，不要求某个固定像素在不同主题下必然变成特定 HWND。
3. `--frosted-system-check`：原有系统限制、草稿、重启及资源释放轨迹通过，见 `review-system-fallback.txt`。捕获专项中的省电事实由已批准的平台入口提供，但销毁背景和丢捕获是实际 Windows 行为；本轮没有重新切换真实 Windows 节能设置。

这轮只运行与审查修复有关的输入和暂停回归，没有重复无关的 30 轮或性能采样。额外按钮与固定像素切换的探索没有纳入通过范围，也没有为夹具前提更改产品 alpha 或放宽命中断言。
