# 原生桌面 Folder 临时原型

这是回答桌面集成问题的实验代码，不是正式版本。采用 C#、.NET 10 和 WPF，未引入第三方依赖。

## 要回答的问题

自绘 Folder 能否在真正的 Windows 桌面上显示、展开面板，并在“显示桌面”后继续接收操作？

这是原生桌面技术实验，使用实际 Windows 窗口；网页无法回答其桌面宿主问题，因此没有采用网页布局变体。

## 启动

双击 `run.cmd`，或者在项目根目录执行：

```powershell
rtk dotnet run --project prototypes/DesktopFolderPrototype/DesktopFolderPrototype.csproj
```

本机需要 .NET 10 SDK。已经编译好的入口是 `bin/Debug/net10.0-windows/DesktopFolderPrototype.exe`，无需再次构建即可双击运行。

## 操作

- 两个示例 Folder 显示在桌面上；拖动标题调整位置，拖动入口右下角调整尺寸。
- 单击入口中的文件名区域，展开浮动内容面板；点击外部收起。
- 面板右上角切换列表／网格；调整窗口边缘可改变面板尺寸。
- 右键入口可重命名、调整背景颜色和透明度数值，或删除。
- 使用 `Ctrl+Alt+K`，或右下角系统托盘菜单创建 Folder。
- 托盘菜单“打开示例数据”打开原型数据目录。
- 从 `PROTOTYPE-data/模拟桌面/` 拖入示例文件，观察文件实际移动到 `PROTOTYPE-data/内容/<名称>/`。
- 在面板中使用 Ctrl／Shift 多选示例文件，拖到其他 Folder、桌面或资源管理器中的目录；这会实际移动示例文件。
- 双击文件或普通子文件夹使用系统默认程序打开。
- 退出使用系统托盘菜单；下次启动恢复入口位置、尺寸、颜色和查看方式。

## 数据范围

所有自动生成的数据都位于本项目目录的 `PROTOTYPE-data/` 中。原型不会创建或改动 `D:\KageFiles\`。

移入操作仅接受原型数据目录中的示例项目；真实个人文件会被拒绝。用户主动将示例文件拖出到桌面或其他目录时，示例文件会真实移动到所选位置。

原型选择“保留内容”时，在 `PROTOTYPE-data/模拟桌面/` 创建快捷方式，保留对应内容目录。正式版本按需求在实际桌面创建快捷方式。选择“一同删除”时，对选中的原型内容目录执行系统回收站操作。

持久状态保存在 `PROTOTYPE-state.json`；该文件以及构建、数据和自动预览输出均不纳入 Git。

## 已核查与限制

- 编译通过，无警告。
- 当前 Windows 会话中，两个入口成功成为 `SHELLDLL_DefView` 的子窗口。
- 使用 Shell 的 `ToggleDesktop` 执行“显示桌面”，入口仍可被鼠标命中；随后切回并退出。该诊断不等于完整键盘交互验收。
- 列表和网格均能渲染，预览图已经检查。
- **透明分层 WPF 子窗口未正常绘制；普通 WPF 子窗口可以显示。** 当前“背景透明度”只改变颜色混合效果，不能透出桌面壁纸，正式版本需要另行解决。
- 当前入口预览使用文件名和通用符号占位，尚未加载真实 Shell 图标与内容缩略图。
- 使用 Explorer 内部窗口是实验策略，不保证系统更新后的兼容性。
- 未实现根目录修改／迁移、开机自启开关，以及正式版本所需的迁移恢复机制。
- 真实手势拖拽、快捷键、菜单、尺寸拖动、Explorer 重启和多显示器／DPI 切换仍需在交互会话中逐项验收。

## 自动诊断

```powershell
rtk dotnet run --project prototypes/DesktopFolderPrototype/DesktopFolderPrototype.csproj -- --capture
```

生成入口、列表和网格预览，以及 `PROTOTYPE-report.json`，随后退出。

增加 `--desktop-probe` 会短暂切换并恢复“显示桌面”，核查入口命中情况，只有命中入口时才截取其矩形区域。

正式需求见 [需求记录](../../.scratch/desktop-folder/requirements.md)，实验结论见 [原型结论](../../.scratch/desktop-folder/prototype-verdict.md)。
