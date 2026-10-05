# 升级 03：设置、Folder 样式与消息通知验收

日期：2026-10-04 至 2026-10-05。对应 [票据 03](../../.scratch/desktop-tool-upgrade/issues/03-settings-style-and-notifications.md)，审查基线为 `542f6f235556c00bd7374064a42bc1b9a2cc1daf`。

## 实现

沿用已批准的 `IDesktopWorkspace` 边界。应用通知偏好缺少字段时默认开启，状态保存成功后才发布新值，保存失败保留原值和原因。旧状态格式版本保持 1，布局、内容映射与恢复意图继续使用已有规则。

操作完成统一进入结果记录，再按消息提示开关决定是否投递 Windows 通知。文件移动按批次列出成功、跳过、取消、失败计数和失败摘要；详情保留源路径、实际路径和状态提交结果。结果历史单独原子替换，最多保留 100 条；历史保存或通知投递失败不会更改业务结果。损坏历史文件保留，当前会话仍可查看新结果。

通知使用 Windows App SDK 1.8.260921001，随自包含包交付。按 [微软的无打包身份 .NET 通知流程](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/app-notifications-dotnet) 注册回调，再注册通知；点击只查看对应结果。真实单实例管道转交结果标识，旧结果超出保留范围时显示说明。通知失败或被系统关闭时不改用完成弹窗。

移动和根目录迁移在进行期间保留进度与取消，完成后自动关闭。名称、同名冲突和删除方式继续询问；输入失败保留可修改表单，样式保存失败保留可重试草稿。新建、改名、删除、设置保存、样式、图标、查看与排序、展开折叠、布局提交和打开项目结果均接入详情。

设置标题为“Kage 桌面工具 · 设置”，六个浅色侧栏分区为通用、桌面 Folder、存储、通知、恢复与状态、关于。原存储、自启、Folder 管理、刷新、备份及待核对关联入口保留。长路径和恢复说明可换行，按钮使用一致间距，存储按钮自适应换行。关闭设置继续后台运行。

“Folder 样式”正文显示当前 Folder 名称，设置及头部入口统一为“样式…”。百分比及完全透明标签位于滑块右侧，使用 Grid 自适应列；去掉实时预览文字与固定空格。预设色、RGB／HEX、调色盘 owner、背景预览和取消／应用行为继续使用已有外观会话。

## 已验证

- 63 项完整业务检查通过，包含新增通知偏好检查：真实 1.0.0 缺字段状态、关闭保存、重启恢复、提交故障、原值保持与重试。已有外观提交、失败恢复、真实目录及文件内容回归保留。
- 自包含 x64 构建通过，警告即错误；通知投影与本地 Windows App Runtime 随产物交付，不安装全局运行时。
- 150% 真实 WPF 设置与样式检查已验证六分区、长路径渲染、未提交选择不被刷新覆盖、开关保存失败恢复、样式失败重试及移动进度自动结束。截图已人工查看。
- 既有真实 Windows 外观、Folder 改名／保留内容／回收站删除、根目录迁移及取消恢复、迁移恢复清单、真实鼠标/OLE 文件移动检查通过。
- 真实原生菜单单项和多项删除均按观察到的原路径状态记录一次结果，详情保留各路径；取消菜单不生成已执行结果。第三方／异步命令只报告 Windows 已返回及当前路径观察，不伪造其最终副作用；尚未确认的删除项目明确列为待核对。
- 实际第二进程通过单实例管道转交对应结果标识，显示原详情而不产生新操作。完整会话检查在释放临时占用后仍遇到既有正式程序占用 `Ctrl+Alt+K`，因此另以 `--instance-only` 完成单实例及通知路由验证；没有关闭正式实例，全局热键成功注册与真实快捷键输入本次未重新验证。

## 未完成的原生通知验收

本机 Windows 返回通知启用，通知提交后可在对应 API 的历史中读到载荷；实际通知中心和 UIAutomation 控件中未找到测试通知，因此真实鼠标点击及其回调未通过。尚不能确定是应用集成还是系统环境问题，不把 API 接收等同于用户可见。

已用旧 Toolkit 与 Windows App SDK 对照，缩到不包含工作区、托盘或设置的一条通知；均未获得可见通知控件。检查进程没有管理员提升，通知服务正在运行；用户表示未关闭或不确定是否限制过通知。Explorer Shell 启动对照也没有取得点击通过证据。未改变系统通知配置、重启 Explorer 或通知服务。

普通 EXE 无检查参数启动、冷启动通知激活及系统关闭应用通知仍需补充验证；投递故障与提示开关抑制已有隔离边界证据，不能替代上述系统验收。票据保持 `claimed`。

日志和截图位于忽略目录 `.scratch/desktop-folder/verification/`，原生失败日志为 `upgrade-03-settings-notifications.txt`；设置／样式截图为 `upgrade-03-settings-*.png`、`upgrade-03-style.png`。本机实际配置为单屏 150%；其他 DPI 和真实多屏未验证。

## 独立审查

固定基线至初次提交 `31c6787` 的两轴审查由独立只读代理完成。

### Standards

明确规范违规 0；两项判断性建议为批次摘要重复计算、`Balloon` 名称与实际副作用不符。已删除进度关闭前的重复完成摘要，统一逐项格式化，并将状态报告入口改名为 `ReportStatus`。

### Spec

两项实现问题为：通知定位后的每次后台刷新重新滚动；原生菜单执行成功没有对应结果反馈。已对结果内容做变化检测，仅选择变化时定位；平台适配器返回已执行命令及真实路径观察，再接入单次结果记录。真实长详情刷新、原生单项／多项删除与取消输入检查通过。

两轴实现问题已修复；原生通知显示、点击及普通启动仍为明确未完成的验收要求，票据没有关闭。

## 复现

```powershell
rtk proxy dotnet run --project tests/Kage.Workspace.Checks --no-restore
rtk proxy dotnet publish src/KageDesktopTool/KageDesktopTool.csproj -c Release -r win-x64 --self-contained true -o .scratch/desktop-folder/verification/upgrade-03-final
rtk proxy .scratch/desktop-folder/verification/upgrade-03-final/KageDesktopTool.exe --settings-notification-check --settings-only
rtk proxy .scratch/desktop-folder/verification/upgrade-03-final/KageDesktopTool.exe --settings-notification-check --notification-only
```

真实会话检查串行执行，原生检查短暂操作鼠标并在结束后恢复。`--settings-only` 明确跳过系统显示／点击，不能据其成功宣称原生通知验收通过。
