# 原生桌面 Folder 原型 · 第二版

采用 C#、.NET 10 和 WPF；这是桌面交互实验，尚未完成正式版本的目录迁移和开机自启。

## 启动

先从托盘退出正在运行的旧版，再双击 `bin/DragFix/DesktopFolderPrototype.exe`。也可以双击 `run.cmd`，或在项目根目录从源码运行：

```powershell
rtk dotnet run --project prototypes/DesktopFolderPrototype/DesktopFolderPrototype.csproj
```

从源码运行需要 .NET 10 SDK；编译后的程序需要本机 .NET 10 Windows Desktop Runtime。

## Folder 操作

- 默认灰色半透明，折叠时仅显示名称、内容文件数、倒三角展开按钮。
- 点击倒三角，在头部正下方展开内容；再次点击向上三角折叠。多个 Folder 可以同时展开，点击外部不自动收起。
- 内容区图标来自 Windows Shell，保留文件关联及快捷方式覆盖图标。
- 内容区右上角一键切换列表／网格。
- 拖动头部调整位置；右下角斜三角调整尺寸。折叠时调头部宽高，展开时调共享宽度和内容区高度。
- Folder 不可相互重叠。拖动接触屏幕或其他 Folder 时贴住边界，可沿边滑动，反向后立即跟随鼠标；缩放拒绝冲突的尺寸。展开时优先保持本 Folder 头部位置，将冲突的其他 Folder 放到附近空位，空间不足则取消展开。
- 桌面空间不足时，保留记录和内容并暂时隐藏；释放空间后通过托盘“重新挂接桌面”恢复。
- 右键头部选择“颜色与透明度…”：预设颜色、Windows 调色盘、RGB 三分量和 HEX 输入均可使用；滑块实时改变背景透明度，文字和图标不随之变淡。取消恢复原先外观，应用保存。
- 双击文件或普通子文件夹使用系统默认程序打开。

## 内容移动与删除

- 使用 `Ctrl+Alt+K` 或系统托盘右键菜单创建 Folder。
- 托盘“打开示例数据”可查看 `PROTOTYPE-data/`。从模拟桌面或其他内容目录拖入示例文件，观察其实际移动到 `内容/<Folder 名称>/`。
- Ctrl／Shift 多选后，可拖到其他 Folder、桌面或资源管理器目录，实际移动示例文件。
- 移入仅接受本原型数据目录中的项目，真实个人文件会被拒绝。用户主动拖出示例文件时，文件实际移动到目标位置。
- 保留内容时，在 `PROTOTYPE-data/模拟桌面/` 生成指向原内容目录的快捷方式；正式需求是在实际桌面生成快捷方式。
- 一同删除时，将选中的原型内容文件夹及示例内容放入系统回收站。
- 自动生成的数据、状态和预览留在原型目录，不会创建或改动 `D:\KageFiles\`。

## 图标方案

用户已选定 **D「K 文件夹」**，作为默认应用与托盘图标。托盘“图标方案”菜单还可切换 A「收纳夹」、B「分区格」、C「叠层抽屉」。

`Assets/` 保存四套应用版和托盘版 PNG／ICO；每个 ICO 包含 16、20、24、32、48、64、128、256 像素版本。`图标方案.png` 为比较图。EXE 内置 D 图标；运行时菜单切换托盘及窗口图标，不会改写 EXE 内置图标。

图标由 `IconChoices.cs` 中的几何图形绘制，重新生成：

```powershell
rtk dotnet run --project prototypes/DesktopFolderPrototype/DesktopFolderPrototype.csproj -- --make-icons
```

## 状态与退出

- 沿用第一版的内容目录和位置记录，将旧界面状态升级为新的头部结构及默认灰色样式；不会改动示例文件内容。
- 位置、尺寸、展开状态、查看方式和外观保存在 `PROTOTYPE-state.json`；图标选择保存在 `PROTOTYPE-settings.json`。
- 通过系统托盘菜单退出；再次启动恢复记录并重新消除布局冲突。
- 数据、构建、运行状态与自动预览输出不纳入 Git。

## 已核查与仍待验收

- 编译通过，无警告。
- 当前交互会话中，透明桌面子窗口能实际绘制，背景可透出壁纸，文字和图标保持清晰。
- 补上 Windows 10 兼容声明后，透明分层子窗口正常显示。参见[微软分层窗口文档](https://learn.microsoft.com/en-us/windows/win32/winmsg/using-windows)。
- 已核查两个 Folder 同时展开的实际窗口边界不重叠，以及冲突移动／缩放请求被拒绝。
- 已生成并检查头部、网格、列表、外观设置和四套图标的预览。
- 用户手动拖拽、调色盘、滑块、尺寸恢复及 Explorer 重启、多显示器／DPI 切换仍需交互验收。
- 桌面宿主使用 Explorer 内部窗口，仅作为实验策略，不视为跨 Windows 版本的公开兼容契约。
- 根目录迁移、迁移失败恢复和开机自启开关尚未实现。

## 自动诊断

```powershell
rtk dotnet run --project prototypes/DesktopFolderPrototype/DesktopFolderPrototype.csproj -- --capture
```

生成头部、网格、列表、外观设置预览及 `PROTOTYPE-report.json`，随后退出。增加 `--desktop-probe` 会短暂切换并恢复“显示桌面”，核查入口命中并截取原型入口矩形区域。诊断会恢复原先位置、展开状态和查看方式。

完整需求见[需求记录](../../.scratch/desktop-folder/requirements.md)，实验结论见[原型结论](../../.scratch/desktop-folder/prototype-verdict.md)。

## 拖动回归检查

在项目根目录执行：

```powershell
rtk dotnet build prototypes/DesktopFolderPrototype/DesktopFolderPrototype.csproj --no-restore --output prototypes/DesktopFolderPrototype/bin/DragFix
rtk proxy dotnet prototypes/DesktopFolderPrototype/bin/DragFix/DesktopFolderPrototype.dll --drag-regression
```

增加 `--minimal` 只重放“受阻后反向一像素”的最小轨迹。完整检查覆盖屏幕四边贴边、立即反向、沿边滑动，以及折叠／展开 Folder 的四向碰撞和不重叠；不创建交互窗口，不修改内容文件或用户布局状态。
