# 桌面 Folder 原型结论

Status: needs-interactive-validation

## 问题

在 Windows 上自绘头部及其下方的内容展示区，支持多个 Folder 同时展开而不重叠，并保持灰色半透明背景，是否具有可行的桌面宿主方案？

## 已获得的证据

- .NET 10 / WPF 原型编译通过，无警告。
- Windows 内核版本 `10.0.26300.0` 的当前交互会话中，两个入口窗口的父窗口均为所找到的 `SHELLDLL_DefView`。
- Shell `ToggleDesktop` 切换后的中心点命中检查返回原型入口窗口；截图确认普通 WPF 子窗口实际显示。切换完成后恢复，再退出原型。
- 入口、网格面板和列表面板已生成预览并进行视觉检查。
- 透明分层子窗口虽然可被命中，却没有在实际桌面截图中正常绘制。改用普通 WPF 子窗口后可正常显示。
- 受限执行环境无法找到 Explorer 桌面宿主；在当前交互会话中运行能够找到并挂接。这两种结果不能混为一谈。

## 判断

第一版确认了桌面宿主挂接及普通 WPF 绘制的可行性；第二版已补充透明分层子窗口所需的 Windows 10 兼容声明，实测背景可透出桌面壁纸，文字和文件图标保持清晰。第一版的透明绘制限制已解除。

`SHELLDLL_DefView` 属于 Explorer 内部窗口结构，此次通过不代表正式支持或跨 Windows 版本兼容保证。

## 第二版交互与验证

- Folder 改为头部与其下方展示部分，折叠只保留头部，多个 Folder 可同时展开。
- 默认灰色半透明；使用 Windows Shell 文件图标；新增调色盘、RGB／HEX 输入和实时透明度滑块。
- 创建、位置恢复、移动、缩放和展开采用统一的不重叠布局规则。
- 自动诊断通过实际原生窗口边界检查两个 Folder 同时展开时不重叠；检查重叠移动／缩放请求被拒绝。
- 四套应用／托盘图标已导出多尺寸 ICO 和 PNG；用户选定 D「K 文件夹」为默认方案。
- 已检查头部、网格、列表、外观对话框、图标比较图和桌面实际透明绘制截图。

## 尚待验证

- 人工使用 `Win+D` 前后点击入口、展开面板及实际拖拽。
- 原生桌面、资源管理器与面板之间的多选移动、冲突处理和快捷方式拖动。
- 入口位置和两种尺寸调整后的重启恢复。
- 全局快捷键、系统托盘、外部文件变化同步及删除流程。
- Explorer 重启恢复、多显示器、DPI 变化和跨屏移动。
- 颜色／透明度控件的完整人工操作、Windows 图标与资源管理器在更多文件类型上的一致性。

## 原型来源

- 分支：`codex/prototype/desktop-folder`。
- 项目：`prototypes/DesktopFolderPrototype/`。
- 需求：[requirements.md](requirements.md)。
- 微软参考：[ToggleDesktop](https://learn.microsoft.com/en-us/windows/win32/shell/ishelldispatch4-toggledesktop)、[SetParent](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setparent)、[分层子窗口与兼容声明](https://learn.microsoft.com/en-us/windows/win32/winmsg/using-windows)、[SHGetFileInfo](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shgetfileinfow)。
