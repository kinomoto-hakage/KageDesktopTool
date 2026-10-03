# 升级 01：内容展示与真实文件交互

需求：[功能票 01](../../.scratch/desktop-tool-upgrade/issues/01-content-and-file-interaction.md)。验收使用正式 `IDesktopWorkspace` 与真实 Windows 桌面随机夹具，不更改用户工作区状态、自启或已有桌面文件。

## 双击复现与修复

基线为保留的 `releases/KageDesktopTool-win-x64-1.0.0/KageDesktopTool.exe`。命令：

```powershell
rtk proxy python tests/physical_content_probe.py releases/KageDesktopTool-win-x64-1.0.0/KageDesktopTool.exe
```

脚本启动发布 EXE 的独立 `--manual-desktop-check` 夹具，创建会写入标记的真实 `.cmd`，以 Win+D 显示桌面，再经过 Windows 鼠标输入双击第一格图像。先核对进程归属、实际窗口边界与落点，按 150% DPI 换算。1.0.0 结果为 `marker_exists: false`；仅修复 Preview 路由后的同一脚本为 `true`。

根因：旧 `FileDrag.Send` 在已选项目的 Preview 按下事件设置 `Handled`；双击的第二次按下已处于选中状态，原 `MouseDoubleClick` 处理无法收到事件。现由 `ContentPointerInput` 统一处理点击、双击、框选及起拖，以当前命中项目打开；双击不启动拖动。旧 `ContentLayoutChecks` 的事件注入已删除，该检查仅验收 Shell 路径打开，真实输入由新检查覆盖。

## 实现边界与状态

- `SetContentViewAsync` 保存查看方式、列表／网格独立尺寸、排序键及方向；`ReorderContentsAsync` 在最新实际内容上验证路径、保留可见相对顺序并提交手动顺序。原位释放不写状态。
- `CustomOrder` 保存实际名称与可取得的文件身份。可靠改名保留位置；无法取得身份的外部改名按新增处理。刷新协调新增／删除，一次原子保存；目录不可读时不清空顺序。保存失败保留已提交设置。
- `RenameContentAsync` 在真实改名前持久化 `PendingContentRename`，副作用后按文件身份协调最终状态；最后保存失败进入恢复状态，重启确认源／目标后恢复手动位置，不认领外部文件。
- 旧状态缺字段时：名称升序、无手动顺序、列表 16 DIP、网格 32 DIP，保留原查看方式、Folder 标识及几何。状态版本仍为 1。旧程序会忽略新偏好；存在新版未完成内容改名时应先由新版完成恢复，再回退程序。
- 原生菜单通过 `IShellFolder.GetUIObjectOf` 取得实际选择的 `IContextMenu`，转发 `IContextMenu2/3` 消息以支持动态／自绘扩展。临时顶层菜单所有者承接键盘输入，解决 Explorer 子 HWND 无法成为前台菜单所有者的问题。Shell 的重命名命令由宿主提供名称输入，再通过业务接口提交。
- Shell 图像列表只省略与系统 `SIID_LINK` 对应的覆盖，保留实际文件关联、自定义图标及其他返回的覆盖；不修改注册表或全局箭头配置。Windows 每次只返回一个主覆盖，本票不重新合成 Shell 未返回的状态图层。
- ListBox 使用 DIP 像素滚动，选框起点保存在内容坐标中；自动滚动后仍正确命中视口外项目。鼠标捕获、原生菜单和 OLE 拖动期间暂停周期刷新。

平台实现依据：[Microsoft IContextMenu](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-icontextmenu)、[GetOverlayImage](https://learn.microsoft.com/en-us/windows/win32/api/commoncontrols/nf-commoncontrols-iimagelist-getoverlayimage)、[系统覆盖图标枚举](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/ne-shellapi-shstockiconid)。

## 检查命令与证据

```powershell
rtk proxy dotnet run --project tests/Kage.Workspace.Checks --no-restore -- 内容视图
rtk proxy dotnet build src/KageDesktopTool/KageDesktopTool.csproj -c Release --no-restore
rtk proxy python -c "import subprocess; raise SystemExit(subprocess.run(['src/KageDesktopTool/bin/Release/net10.0-windows/KageDesktopTool.exe','--content-input-check']).returncode)"
```

新增业务组覆盖：自然数字及中文名称、目录优先、日期同键稳定顺序、快捷方式自身大小、偏好独立保存／失败／重启、原位释放、过期路径、多选手动顺序、外部增删改名、内容改名事务中断、有效旧状态缺字段的默认值。

Windows 输入组通过系统鼠标／键盘输入驱动实际 WPF 窗口，核对快捷方式标记、普通程序、默认文本关联窗口、Explorer 实际目录、原生菜单与已安装 Code／7-Zip 处理器、真实改名／删除及选择刷新、多选内部重排、跨 Folder 移动、滚动和八种尺寸。捕获丢失使用 Windows `ReleaseCapture`，不通过 WPF 事件注入。

运行日志、基线 EXE 校验值及 PNG 位于忽略目录 `.scratch/desktop-folder/verification/`：`physical-1.0.0-result.json`、`physical-fixed-result.json`、`content-input-session.txt`、`content-grid-16/32/48/96.png`、`content-list-16/32/48/96.png`。实际测试环境为 Windows 11、单屏、150% DPI；硬件多屏与其他 DPI 不冒充已实测。

完整回归、发布目录验收及代码审查结论在完成后补记。
