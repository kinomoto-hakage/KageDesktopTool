# 11 显示桌面及 Explorer 重启恢复验收

日期：2026-10-03。对应 [任务 11](../../.scratch/desktop-folder/issues/11-desktop-session-recovery.md)。

## 实现与边界

`IDesktopWorkspace.ReportDesktopAvailabilityAsync` 报告当前会话是否能够展示桌面 Folder。`WorkspaceSnapshot.DesktopAvailable` 与内容恢复状态分开，报告不写入配置、不重排布局、不执行文件恢复；重启默认等待新的宿主核对。报告通过共用操作队列串行执行，不能打断正在等待冲突选择的文件移动，也不会在宿主恢复时重复移动文件。

`WindowsDesktop` 统一识别 Explorer 桌面宿主、调整子窗口样式及父关系、换算宿主坐标、识别拖放实际目录。其他产品模块不依赖 Explorer 窗口类名或内部层次。挂接前显式调整 WS_POPUP／WS_CHILD，依据 [Microsoft SetParent 文档](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setparent)。

真实会话复现证实：Explorer 退出会销毁本程序挂接的子窗口，旧 Folder 的 HWND 变成 0。Runtime 按原稳定标识和已提交快照重建失效的 WPF 窗口，重新登记 OLE 接收；有效窗口继续使用原对象。控制器及热键保持原注册，托盘恢复复用 NotifyIcon 和菜单。`TaskbarCreated` 通知后重新添加托盘图标，依据 [Microsoft 任务栏文档](https://learn.microsoft.com/en-us/windows/win32/shell/taskbar)。周期刷新补充检测宿主失效与恢复；文件移动、迁移和鼠标捕获期间等待操作结束再挂接。

宿主不可用时隐藏头部和展示部分，不改动活动／保留内容及配置；托盘说明展示状态，设置仍提供打开实际目录的入口。宿主恢复后通知展示已恢复。

## 自动检查结果

- 全套 44 项共用接口检查通过；新增两组分别核对不可用／恢复时实际路径、展开布局、字节及配置保留，以及待冲突选择的文件移动不会被会话报告打断或重复执行。
- Release 构建通过，警告即错误，0 警告、0 错误。
- 当前真实 Windows 会话通过：注入实际 Win+D 按键进入／退出；头部与展示部分 HWND 实际命中；真实鼠标展开／折叠、拖动及缩放保存；普通应用覆盖 Folder；独立设置获得前台焦点。
- 当前 Explorer 实际退出／启动通过：不可用说明和设置入口；活动布局、保留内容字节与链接关联保持；恢复无重复窗口；原控制器和热键保持；系统通知区／隐藏图标区域中真实托盘按钮可双击打开唯一设置。
- 恢复后真实 Explorer OLE 拖入和展示部分 OLE 拖出通过，分别核对源消失和目标字节，未凭全局效果标志推断成功。
- 04 生命周期、05 内容布局、06 外观、07 真实多选 OLE、08 Folder 操作、09 根迁移、10 中断恢复的既有 Windows 会话回归通过。06 首次受前台焦点变化影响失败，单独复测通过；未修改其实现或降低检查要求。
- 实际屏幕截图已查看，确认透明背景、头部及展开区域正常绘制。日志与截图保存在忽略目录 `.scratch/desktop-folder/verification/`，主要文件为 `desktop-recovery-session.txt`、`11-WinD-进入.png`、`11-Explorer-恢复.png`。

## 人工验收

人工隔离入口 `--manual-desktop-check` 创建“人工验收 A/B”和 txt 内容，不读取正式工作区，不注册自启；托盘退出后清理随机夹具。亲自执行 Win+D 进入／退出，核对头部、内容、展开／折叠、拖动、缩放及内容操作。

2026-10-03：用户在先前暂缓验收后确认“人工 Win+D 检查通过”。任务中的人工进入／退出、头部和展示部分可见及可操作验收项据此标记为通过；全部验收完成，任务状态更新为 `resolved`。人工结果依据用户确认记录，自动检查结果见上节。隔离夹具已通过自己的托盘退出菜单正常关闭并清理。

## 独立复核

按用户确认的基点 `9c68ff91` 对实现提交 `b40ab5a` 执行两轴独立复核。

- Standards：14 个改动文件逐项复核；仓库标准违规 0，值得报告的十二项 baseline smell 0。Explorer 细节收于桌面适配器，隔离检查沿用既有模式。
- Spec：代码缺失、范围扩张和错误实现均为 0 项；复核时保留的 1 项人工 Win+D 验收限制，已由用户在 2026-10-03 确认通过后解除。

## 复现

```powershell
rtk proxy dotnet run --project tests/Kage.Workspace.Checks --no-restore
rtk proxy dotnet build src/KageDesktopTool/KageDesktopTool.csproj -c Release --no-restore
rtk proxy dotnet src/KageDesktopTool/bin/Release/net10.0-windows/KageDesktopTool.dll --desktop-recovery-check
rtk proxy dotnet src/KageDesktopTool/bin/Release/net10.0-windows/KageDesktopTool.dll --manual-desktop-check
```

真实恢复检查会操作鼠标及 Win+D，并重启当前交互会话桌面宿主所属的 Explorer 进程。执行前应结束复制／移动／删除；检查本工具没有未完成操作，并拒绝 Explorer 文件进度或对话框。记录并恢复可识别的目录及系统位置窗口。夹具及桌面快捷方式均使用随机命名，清理前检查绝对路径和随机前缀。

本次自动审批最初拒绝会话级副作用；用户随后明确允许执行，并确认没有未完成文件操作，才继续真实验收。
