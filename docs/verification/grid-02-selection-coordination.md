# 02：Folder 统一选择协调

对应票据：[02-selection-coordination](../../.scratch/folder-grid-selection-and-header/issues/02-selection-coordination.md)。实施基线：`e2f7ff03934430801d1d0cf6d292cc5359576ed3`。

## 实现

- 应用层的 `SelectionCoordinator` 统一当前选择归属，各 Folder 注册／关闭时注销。不把选择写入工作区状态，不新增测试专用业务 API。
- 正式窗口的 Preview 输入先协调归属：图标及当前滚动条保留所属 Folder，普通空白与任意头部清除全部选择；当前 Ctrl／Shift 框选仍沿用开始时的集合。
- 清除同时重置鼠标及 WPF 键盘连选锚点、框选初始集合和完整名称操作次序；通过原名称观察路径收起全文及额外滚动范围。`ContentListBox` 使用正式的 protected `AnchorItem`，首次 Shift 导航由 WPF 定位新目标，再从该目标建立范围；鼠标与键盘同步锚点。普通刷新仍按可靠身份保留有效选择，显式清除后的重建不恢复集合。
- 每个 Runtime 安装一组 Windows 鼠标及前台监听，退出时卸载。鼠标回调只判定真实 HWND 并排队，始终调用 `CallNextHookEx`；前台通知在应用线程处理。排队通知带选择代次，并在执行时复核前台，避免旧通知覆盖后续操作。
- 原生菜单只按宿主、所属菜单线程和拥有者窗口识别；内部重命名另有明确窗口归属。菜单结束时仅在仍拥有前台且请求未因外部操作取消时交还来源桌面，避免隐藏菜单宿主激活上一个应用。外部清除同时取消尚在准备的菜单请求。
- 文件拖动沿用开始时的真实路径快照；可见选择清除不修改已经构建的 OLE 数据和移动来源。

Windows 监听约束依据 Microsoft 的 [LowLevelMouseProc 文档](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelmouseproc) 和 [SetWinEventHook 文档](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwineventhook)。WPF 锚点依据 [官方 ListBox 源码](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationFramework/System/Windows/Controls/ListBox.cs) 的 protected 入口；不使用反射。

## 验证过程

沿用规格已批准的真实 Windows 内容输入边界，按 `tdd` 先复现跨 Folder、头部和桌面点击的选择残留，再逐步接入正式输入处理。额外真实组合检查复现菜单到重命名的前台交接误清除，并在同一路径复验修正。不以直接设置选择、调用清除方法或注入 WPF 事件作为新增功能验收。

测试使用随机隔离工作区、实际内容文件、桌面快捷方式和独立应用进程；检查窗口命中、系统输入接收、选择与全文，以及文件源消失和目标字节。桌面／焦点／菜单检查串行执行，仅清理属于当前夹具的路径。

桌面原点击的接收通过 Windows GUI 线程实际焦点／捕获、桌面项目真实选中和快捷方式打开标记核对。在当前会话，空白点击会让 `SysListView32` 取得焦点，但 Windows 自身保留已选桌面项目；临时关闭本票系统监听的独立对照同样如此。启用监听后原焦点与原生 Shell 选择数相同，只有 Folder 选择由 1 变为 0。因此不把“Windows 桌面必须取消自己的选择”作为本票额外要求。对照日志为 `selection-desktop-enabled.txt` 和 `selection-desktop-baseline.txt`；对照开关、临时探针及全部 `[DEBUG-selection]` 代码已删除。

## 复验命令

当前环境没有 `rtk`，直接使用 `dotnet`。构建带 `--disable-build-servers`。

```powershell
dotnet build src/KageDesktopTool/KageDesktopTool.csproj -c Release --no-restore --disable-build-servers
dotnet run --project tests/Kage.Workspace.Checks --no-restore --disable-build-servers
$check = Start-Process -FilePath 'src/KageDesktopTool/bin/Release/net10.0-windows10.0.19041.0/win-x64/KageDesktopTool.exe' -ArgumentList '--content-input-check','--selection-only' -WindowStyle Hidden -PassThru
$check.WaitForExit()
exit $check.ExitCode
```

真实输入证据位于忽略目录 `.scratch/desktop-folder/verification/` 的 `selection-coordination-session.txt` 与 `selection-cleared.png`；其他回归使用各自的既有日志名。

## 平台范围

当前实际环境：Windows `10.0.26300.0`，单屏工作区 `2560×1528`，缩放 `150%`。硬件多屏、混合 DPI、拔屏、主屏及系统缩放／分辨率切换未实际验证。票据 03 尚未实现，四向头部与侧栏组合由后续票据负责。
