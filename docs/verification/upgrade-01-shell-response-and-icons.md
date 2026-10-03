# 任务 01 追加修正：系统菜单响应与实际图标资源

用户追加要求和接受的菜单展示方式记录于 [功能票 01](../../.scratch/desktop-tool-upgrade/issues/01-content-and-file-interaction.md) 的“追加反馈与修正”部分。原始验收记录保留为旧行为证据，新行为以本记录为准。

## 准确复现与原因

命令：

```powershell
rtk proxy python -c "import subprocess; raise SystemExit(subprocess.run(['src/KageDesktopTool/bin/Release/net10.0-windows/KageDesktopTool.exe','--shell-diagnostics-check']).returncode)"
```

修复前，在用户实际 `MSI Afterburner.lnk` 上测得菜单准备至取消 **4186 ms**、WPF UI 最大停顿 **4159 ms**，可见辅助窗口带 `WS_CAPTION`。旧实现同步 `IShellFolder.GetUIObjectOf`／`IContextMenu.QueryContextMenu` 加载第三方处理器，并激活 1×1 的 WPF ToolWindow；完整菜单构建堵塞 UI，ToolWindow 的系统标题栏产生截图中的红叉。

用户明确接受在资源管理器展示系统菜单。公开 `IExplorerBrowser` 嵌入视图的实际右键探针仍得到传统 `#32768`，所以没有用 WinUI 仿制图冒充 Windows 11 系统菜单。现在使用 `SHOpenFolderAndSelectItems` 定位真实当前选择，后台核对 Explorer 的实际目录及选择集合，确认命中后发送实际右键；菜单完全由 Explorer 拥有，跟随系统设置。Kage 不创建辅助 ToolWindow、不同步构建第三方菜单、不改全局菜单注册表。单次请求互斥，退出取消准备过程，现代菜单切到“显示更多选项”时继续等待传统菜单关闭再刷新。

实际 Windows 11 菜单窗口为 **`Microsoft.UI.Content.PopupWindowSiteBridge`**。测得首次约 **1105 ms**，后续约 **618 ms**；WPF UI 最大停顿分别 **39 ms／46 ms**，无标题栏辅助窗口。这些是本机实测，并非其他机器或扩展组合的固定响应保证。

用户说的“空白”是通用白纸占位图标，而非完全透明图片。初期仅按 alpha 检查图像是否存在无法发现该问题，现已加入真实图案特征检查与 PNG 核对。

- **ChatGPT**：实际是 AppsFolder 包应用链接，普通 `TargetPath` 为空，Shell 目标是有效的 `OpenAI.Codex_2p2nqsd0c76g0!App`。旧系统图像列表在小尺寸返回通用白纸，在 256 px jumbo 画布中又只放了角落的小图案，缩到 96 DIP 后导致尺寸错误。改为解析真实 PIDL／AUMID，查询当前用户注册包和清单，选择与物理像素匹配的 `Square44x44Logo.targetsize-*` 图标资源；无硬编码包版本或 OpenAI 安装路径。
- **Wallpaper Engine**：`.url` 的 `IconFile` 实际存在且指向 Steam 游戏图标，但旧 Shell 缓存返回通用白纸。改为读取 InternetShortcut 明确的 `IconFile`／`IconIndex`，按所需尺寸选择 ICO 帧或模块资源。四档均核对为蓝色镜头图案。

图像缓存按实际路径、修改时间、大小、属性、尺寸及箭头选项区分；短期过期和系统偏好变化会失效。缓存只保存已冻结的有效图像，修改快捷方式后立即重新解析。资源修复仍合成 Shell 返回的非箭头状态覆盖，不修改快捷方式文件或全系统图标设置。

## 默认尺寸与兼容

网格默认大 **48 DIP**，列表默认中 **32 DIP**。新建及缺少新字段的有效旧状态采用新默认值；已经显式保存的尺寸仍按已有值恢复。实际渲染和命中测试相应更新为大网格／中列表，继续验证居中、足够文字空间、相邻项目不重叠及视口伸缩。

## 验证

- 六组内容业务检查与完整 **58 组**业务回归通过；覆盖新默认值、旧状态加载、保存失败、排序、真实副作用及恢复。
- `--content-input-check` 通过：系统菜单单项／多项、Windows 11 现代菜单、“显示更多选项”、原生属性窗口、Explorer 内改名及删除、选择和计数刷新，以及既有真实双击／框选／重排／跨 Folder 移动。
- `--content-layout-check` 通过：48 DIP 网格／32 DIP 列表、图标文字位置、布局、源路径打开、失联和重启恢复。
- `--shell-image-check` 通过：`.url` 显式资源、冻结图像复用、资源改动后失效，实际 ChatGPT／Wallpaper Engine 四档像素和图案。OpenAI 中央透明孔、Wallpaper 蓝色主体与默认白纸明确区分，不只检查非空像素。
- `--shell-diagnostics-check --cancel-only` 通过：准备阶段取消后不再发送右键、不产生菜单。

日志和修复 PNG 位于忽略目录 `.scratch/desktop-folder/verification/`，包括 `shell-response-diagnostics.txt`、`shell-image-check.txt`、`shell-menu-cancel-check.txt`、`actual-shell-icons.json`、`ChatGPT-grid-16/32/48/96.png` 与 `Wallpaper Engine：壁纸引擎-grid-16/32/48/96.png`。环境为 Windows 11 单屏 150% DPI。

平台依据：[Windows 11 菜单集成](https://blogs.windows.com/windowsdeveloper/2021/07/19/extending-the-context-menu-and-share-dialog-in-windows-11/)、[SHOpenFolderAndSelectItems](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shopenfolderandselectitems)、[包查询 API](https://learn.microsoft.com/en-us/windows/win32/appxpkg/functions)、[应用图标 targetsize 资源](https://learn.microsoft.com/en-us/windows/apps/design/iconography/app-icon-construction)。

审查和最终发布验收在完成后补记。
