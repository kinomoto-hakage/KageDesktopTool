# 12 多显示器及 DPI 变化验收

日期：2026-10-03。对应 [任务 12](../../.scratch/desktop-folder/issues/12-display-dpi-recovery.md)，复核基点为 `984b2eeefc34d0db46616df51bc6a41cb3f60a2a`。

## 实现与边界

沿用已确认的 `IDesktopWorkspace` 测试边界。位置保存为物理屏幕像素，头部及展示部分尺寸保存为 DIP；快照的 `DisplayScale` 给出每个窗口当前显示环境的缩放，不增加持久状态字段。

可见边界按真实工作区域的并集判断。窗口能经过相邻屏幕的接缝，但整个物理矩形必须被工作区覆盖；显示器空洞、错位屏幕留下的空白和任务栏占用区域不能通行。目标显示器优先按物理窗口最大交叠面积选择，并核对换算后的尺寸；高到低 DPI 接缝没有自洽解时，使用头部原点所在屏幕，避免把有效接缝变成阻塞带。多个可行 DPI 选择按缩放及坐标稳定排序，不依赖主屏枚举顺序。拖动按相邻输入的物理像素差扫过路径，更新受阻锚点，保持沿边滑动和立即反向；多屏路径逐物理像素检查，避免大步输入穿过空洞或其他 Folder。

显示区域变化时先保留仍能展示的原位置，再给屏幕外入口寻找空位。容纳不下的 Folder 保留标识、展开状态及实际目录，标记暂未展示；设置提供“刷新展示”和打开内容文件夹，显示空间恢复后周期刷新也会自动恢复。保存恢复位置失败仍发布当前工作区中的安全临时位置，明确报告未保存，并保留上次持久配置以供重试。显示区域／DPI 改变后旧布局输入不能覆盖新环境。

Windows 适配器每次直接通过 `EnumDisplayMonitors`／`GetMonitorInfo` 读取工作区，避免 `Screen` 缓存。控制器处理显示变化消息；`SystemEvents.DisplaySettingsChanged`／`UserPreferenceChanged` 补足 WPF 提前消费的系统设置消息。通知后重新测量字体、原生图标网格及 ListView 行高；文件操作期间等待结束，鼠标输入取消后按新环境恢复。

Explorer 子窗口可能沿用宿主的 DPI；窗口通过 `Surface.LayoutTransform` 补偿目标显示器与 WPF source 的缩放差，并同步根窗口逻辑尺寸及 HWND 物理边界。WPF 的命中和 `PointToScreen` 使用同一变换。每个 DPI 使用 `SystemParametersInfoForDpi` 读取标题字体，在该 DPI 的实际显示器上创建不显示的父 HWND，并用该 DPI 的字体像素和小图标像素实测原生 ListView；注入的 DPI 没有对应实际屏幕时仅验证显式字体／图标像素换算。Shell 在后台按实际路径读取图标及覆盖，必要时从系统图像列表取足够大的原生图像，避免高 DPI 放大低分辨率位图。

平台行为依据：[GetDpiForWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getdpiforwindow)、[GetSystemMetricsForDpi](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getsystemmetricsfordpi)、[SystemParametersInfoForDpi](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-systemparametersinfofordpi)、[SHGetImageList](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shgetimagelist) 和 [SHGetFileInfoW](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shgetfileinfow)。

## 检查结果

- 最终完整 52 项业务检查通过。新增 8 组覆盖负坐标相邻跨屏及重启、空洞与错位工作区、混合 DPI 跨屏与调尺寸、恢复保存失败、旧 DPI 输入、拔屏／分辨率／容量不足与恢复、优先保留剩余屏位置、四种混合 DPI 双向和上下跨屏。首次完整 51 项通过后，两轴审查发现高到低 DPI 接缝阻塞，已增加失败复现并修复，随后复跑完整 52 项及真实显示／内容布局检查。
- Release 构建通过，警告即错误，0 警告、0 错误。
- 当前实际 Windows 工作区为 `(0, 0, 2560, 1528)`，缩放 150%，HWND 和 WPF DPI 均为 144。实际绘制、原生字体／图标／行高、列表加宽零左侧偏移、真实鼠标内容选择、头部拖动十五物理像素、调尺寸和位置保存通过。
- 实际控制器接收显示变化消息，活动输入结束，原生度量缓存失效，内容重新排列；恢复后受阻立即反向一物理像素和输入命中通过。
- 在同一实际会话注入 125%／150%／200% 显示度量，网格和列表的 HWND 边界、原生图标分辨率、字体、行高及 WPF 物理命中一致。注入 200% 时目标 DPI 为 192，HWND／WPF 仍为实际 144，显式补偿得到正确尺寸。这验证适配器换算，不代表实际切换了系统缩放。
- 既有 05 原生内容布局、06 外观及 07 真实 Explorer OLE 拖入／内容拖出 Windows 会话回归通过。真实截图已查看，透明背景、头部、列表左侧及选择高亮绘制正常。

日志与截图保存在忽略目录 `.scratch/desktop-folder/verification/`：`display-dpi-session.txt`、`12-实际屏幕-1.png`，以及既有内容／外观检查日志。

## 实际覆盖限制

当前只有一台可用显示器。实际多屏跨屏、负坐标屏幕、混合 DPI、物理拔屏、切换主屏和实际系统缩放／分辨率变化均**未验证**。这些布局场景已有记录的业务轨迹检查，但业务检查及度量注入不能代替相应硬件和系统配置下的实际绘制／输入验收。

本次没有更改系统显示配置，也没有重启 Explorer。真实检查使用随机临时内容和状态目录，不读取正式工作区、不注册自启；恢复鼠标位置，并成对执行 Win+D。

## 独立复核

按 `code-review` 技能对基点 `984b2ee` 至实现 `37b4a7b` 及修复 `88caa1e` 的差异进行两轴独立审查。

### Standards

规范硬违规 0，baseline smell 0。首次额外发现 1 项正确性问题：高 DPI 向低 DPI 跨屏时最大交叠选择没有自洽解；已通过共用业务边界复现，修复并补齐双向／上下检查。复审确认正确性遗留 0；原生控件在目标屏创建隐藏父 HWND 的职责和资源释放合理。

### Spec

首次发现 1 项错误实现：混合 DPI 接缝阻断相邻屏幕间移动。修复后的回退仍经过完整工作区覆盖和碰撞检查，不放开空洞及不重叠约束。最终缺失／部分需求 0、范围扩张 0、错误实现 0，实际硬件覆盖限制已按票据要求记录。

最终 Standards 0 项遗留；Spec 0 项遗留。复核只采用实际完成的主代理运行证据，未把受限沙箱中的文件保存失败当作产品失败。

## 复现

```powershell
rtk proxy dotnet run --project tests/Kage.Workspace.Checks --no-restore -- 显示环境
rtk proxy dotnet run --project tests/Kage.Workspace.Checks --no-restore
rtk proxy dotnet build src/KageDesktopTool/KageDesktopTool.csproj -c Release --no-restore
rtk proxy dotnet src/KageDesktopTool/bin/Release/net10.0-windows/KageDesktopTool.dll --display-dpi-check
rtk proxy dotnet src/KageDesktopTool/bin/Release/net10.0-windows/KageDesktopTool.dll --content-layout-check
rtk proxy dotnet src/KageDesktopTool/bin/Release/net10.0-windows/KageDesktopTool.dll --appearance-check
rtk proxy dotnet src/KageDesktopTool/bin/Release/net10.0-windows/KageDesktopTool.dll --file-move-check
```

`--display-dpi-check` 会临时显示桌面及操作鼠标，运行时应留出输入。它逐屏记录实际配置，度量注入保留“系统配置未改变”标识；退出时核对随机前缀和绝对路径后清理夹具。
