# 桌面 Folder 原型结论

Status: needs-interactive-validation

## 问题

在 Windows 上自绘可调尺寸的桌面 Folder，并使用单击展开浮动面板的方式，是否具有可行的桌面宿主方案？

## 已获得的证据

- .NET 10 / WPF 原型编译通过，无警告。
- Windows 内核版本 `10.0.26300.0` 的当前交互会话中，两个入口窗口的父窗口均为所找到的 `SHELLDLL_DefView`。
- Shell `ToggleDesktop` 切换后的中心点命中检查返回原型入口窗口；截图确认普通 WPF 子窗口实际显示。切换完成后恢复，再退出原型。
- 入口、网格面板和列表面板已生成预览并进行视觉检查。
- 透明分层子窗口虽然可被命中，却没有在实际桌面截图中正常绘制。改用普通 WPF 子窗口后可正常显示。
- 受限执行环境无法找到 Explorer 桌面宿主；在当前交互会话中运行能够找到并挂接。这两种结果不能混为一谈。

## 判断

桌面宿主挂接与普通 WPF 绘制在当前机器上具有可行性，足以继续交互验证。透明背景是尚未解决的技术问题，不能将当前原型作为满足全部需求的正式版本。

`SHELLDLL_DefView` 属于 Explorer 内部窗口结构，此次通过不代表正式支持或跨 Windows 版本兼容保证。

## 尚待验证

- 人工使用 `Win+D` 前后点击入口、展开面板及实际拖拽。
- 原生桌面、资源管理器与面板之间的多选移动、冲突处理和快捷方式拖动。
- 入口位置和两种尺寸调整后的重启恢复。
- 全局快捷键、系统托盘、外部文件变化同步及删除流程。
- Explorer 重启恢复、多显示器、DPI 变化和跨屏移动。
- 桌面背景透明效果及真实 Shell 图标／缩略图。

## 原型来源

- 分支：`codex/prototype/desktop-folder`。
- 项目：`prototypes/DesktopFolderPrototype/`。
- 需求：[requirements.md](requirements.md)。
- 微软参考：[ToggleDesktop](https://learn.microsoft.com/en-us/windows/win32/shell/ishelldispatch4-toggledesktop)、[SetParent](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setparent)。
