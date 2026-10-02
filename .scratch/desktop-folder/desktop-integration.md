# 桌面集成事实核查

## 结论

- 当前需求包含自定义尺寸、颜色、透明度和浮动列表／网格面板。基于微软公开的文件夹自定义及 Shell handler 能力，完整界面需要工具自行绘制；这属于依据接口能力作出的技术判断。
- 没有找到微软公开文档保证 `WorkerW` 窗口或 Explorer 私有消息可作为任意 WPF 窗口的稳定桌面宿主。`SetParent` 是公开 API，但不能由此推导 Explorer 内部窗口结构是公开扩展契约。
- 普通无边框窗口不能自动满足“显示桌面”时仍可操作的要求。应先验证桌面集成，再确定生产实现。
- `RegisterHotKey` 可注册全局快捷键；`Ctrl+Alt+K` 被其他程序占用时需要处理注册失败。
- 通知区域图标和普通任务栏应用按钮使用不同的菜单机制，产品含义尚待用户澄清。

## 原型验证项

- `Win+D` 进入和退出显示桌面后，Folder 的可见性、可操作性和窗口层次。
- 普通应用覆盖、面板展开／收起和键盘焦点。
- 文件从原生桌面拖入、从面板拖出时的数据传递及实际移动。
- Explorer 重启后的桌面展示和通知区域图标恢复。
- 多显示器、混合 DPI 和运行中调整缩放。

## 微软来源

- [文件夹自定义](https://learn.microsoft.com/en-us/windows/win32/shell/how-to-customize-folders-with-desktop-ini)
- [Shell extension handlers](https://learn.microsoft.com/en-us/windows/win32/shell/handlers)
- [SetParent 及 DPI 限制](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setparent)
- [操作桌面内部窗口的兼容性实例](https://devblogs.microsoft.com/oldnewthing/20211122-00/?p=105948)
- [Win+D 的窗口层次行为](https://devblogs.microsoft.com/oldnewthing/20241021-00/?p=110393)
- [RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey)
- [通知区域机制](https://learn.microsoft.com/en-us/windows/win32/shell/notification-area)
- [普通任务栏按钮的 Jump List](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-icustomdestinationlist)
- [Explorer 重启后的 TaskbarCreated 通知](https://learn.microsoft.com/en-us/windows/win32/shell/taskbar)
- [High DPI 指南](https://learn.microsoft.com/en-us/windows/win32/hidpi/high-dpi-desktop-application-development-on-windows)
