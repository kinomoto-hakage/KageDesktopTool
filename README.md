# KageDesktopTool

Windows 桌面整理工具，采用 C#、.NET 10 和 WPF。已交付创建与运行生命周期、原生内容、不重叠布局及外观图标，本轮增加 [07 真实文件移动和冲突处理](.scratch/desktop-folder/issues/07-file-moves-and-conflicts.md)：桌面、资源管理器及多个 Folder 之间的双向多选移动、同名选择、取消和逐项结果。

## 运行

双击正式程序 `KageDesktopTool.exe`。本轮的可运行检查包位于 `.scratch/desktop-folder/verification/发布包 含空格/`，需要 .NET 10 Windows Desktop Runtime。最终完整发布包由任务 13 交付。

源码运行需要 .NET 10 SDK，可以双击根目录 `run.cmd`，或执行：

```powershell
rtk proxy dotnet restore src/KageDesktopTool/KageDesktopTool.csproj --configfile NuGet.Config
rtk proxy dotnet run --project src/KageDesktopTool/KageDesktopTool.csproj --no-restore
```

首次运行没有示例文件和示例 Folder。默认存储根目录为 `D:\KageFiles\`，创建时实际检查并建立目录；不可用时在设置中明确选择其他目录并保存，不会自行改用其他位置。

## 使用

- 托盘右键“新建 Folder”或 `Ctrl+Alt+K` 输入名称，创建同名真实内容文件夹及灰色半透明头部。名称遵守 Windows 规则；同名时选择保留两份自动编号、跳过或取消。
- 头部显示名称及直接文件数，快捷方式计为文件，普通子文件夹仍在展示部分显示。点击右侧按钮独立展开／折叠，各 Folder 可以同时展开。
- 展示部分一键切换系统尺寸的网格／列表，使用实际路径的 Windows Shell 图标和覆盖；双击文件使用默认程序，子文件夹使用资源管理器。后台每 3 秒核对目录和图标，外部新增、删除及重命名自动更新；目录失联时保留记录并显示说明。
- 从实际桌面或资源管理器多选拖到 Folder 的头部／展示部分，实际移动任意格式文件、快捷方式及普通子目录。展示部分支持 Ctrl／Shift 多选，再拖到另一个 Folder、桌面空白处或资源管理器的实际目录内容区／普通子目录项目。快捷方式只移动 `.lnk` 自身。
- 每批移动显示逐项成功、跳过、取消及失败和实际位置。同名时选择保留两份自动编号、跳过或取消；“取消后续项目”及拖动中的 Esc 均保留已完成项目。文件占用、权限不足、不可用目标和非法自身／子目录移动明确失败。跨盘目录完整复制并核对字节后才删除源；中途失败保留两边已有内容。
- 拖出只用原生 OLE 预览目标，释放时由共用业务接口执行一次移动；接收拖入也不让外部源凭全局效果标志删除源文件。未能确认实际目录的导航树、虚拟 Shell 位置或其他应用窗口会报告无效目标。重解析路径暂不支持移动，需选择实际目录。操作期间可取消后续项目，结束后两边项目、计数和图标刷新。
- 拖动头部移动 Folder，贴屏幕或其他 Folder 边缘可沿边滑动，受阻后可以立即反向。右下角调整尺寸：折叠时调整头部宽高，展开时调整共享宽度及内容区高度，左侧保持固定。展开优先保留本头部位置并安排冲突 Folder；放不下或尺寸会重叠时保留原布局。
- 位置、尺寸、展开和查看方式在操作结束后保存，重启恢复。内容加载和图标读取在后台进行，鼠标移动只预览几何布局。
- 头部右键“外观…”或设置中对应 Folder 的“外观…”打开外观窗口。预设色块、Windows 调色盘、RGB 三分量和 HEX 输入同步同一颜色；无效输入显示原因并禁止应用。背景透明度滑块实时预览，文字及文件图标保持不透明；取消、Esc 或关闭窗口恢复打开前设置，应用成功后独立保存各 Folder 外观，保存失败可重试或取消。
- 托盘“图标方案”切换 A 收纳夹、B 分区格、C 叠层抽屉、D K 文件夹。成功保存后更新托盘及已打开窗口，重启使用保存的选择；默认 D，EXE 内置发布图标始终为已选 D。
- 托盘“设置”选择存储根目录、应用自启选择、刷新展示或查看异常说明。关闭设置后继续运行；托盘“退出”释放窗口、热键及后台刷新，保留内容。
- 设置中的自启开关需点击“应用自启选择”。只有注册结果和偏好保存都成功才显示成功；失败说明实际结果并尝试恢复原配置。启动目标采用当前包内 EXE 的绝对路径，支持含空格的发布目录。
- 重复启动打开已有实例的设置；热键被占用时托盘仍可创建，并显示原因。
- 空间不足时保留 Folder 记录和真实目录，在设置中显示暂未展示；释放空间后“刷新展示”恢复。
- 已有可用根目录的整体更换需使用任务 09 的迁移功能。已有根目录不可用时，可以明确另选目录用于新建；旧 Folder 保留其原路径关联并显示说明，不将其伪装成已迁移的内容。

## 状态及恢复

状态保存到 `%LOCALAPPDATA%\KageDesktopTool\workspace.json`，与程序包及内容目录分开。稳定标识、名称、头部坐标和外观使用带格式版本的状态；提交先刷新临时文件，再原子替换并保留 `.bak` 最近有效状态。创建和自启操作在产生副作用前记录意图，重启先核对实际目录／启动项后恢复。

状态损坏时暂停修改，只读展示可用备份及原因，不当作空工作区覆盖。确认“恢复状态备份”后才恢复写入，同时保留损坏文件证据。目录已创建但最终状态保存失败时会返回待恢复及实际路径；重启不会重新创建或丢弃原内容。

每个新内容文件夹保留隐藏的 `.kage-folder-id` 归属标识，它不是示例内容，也不计入头部文件数。标识先完整刷新临时文件，再原子移入最终名称。重启恢复未完成创建时核对标识，避免将外部程序抢先创建的同名目录误认为创建成功。标识缺失或不完整时保留待核对记录；核对实际路径和内容后，可在设置中明确关联待核对目录。不完整标识修复前会另存证据，不覆盖其他有效归属标识或用户内容。内部临时标识和证据同样不计入文件数。

## 验证

业务检查与 UI 共用 `IDesktopWorkspace`，使用随机隔离目录；只在状态存储和 Windows 自启边界注入故障。无需外部测试包。

```powershell
rtk proxy dotnet restore tests/Kage.Workspace.Checks/Kage.Workspace.Checks.csproj --configfile NuGet.Config
rtk proxy dotnet run --project tests/Kage.Workspace.Checks --no-restore
rtk proxy dotnet build src/KageDesktopTool/KageDesktopTool.csproj --no-restore
rtk proxy dotnet src/KageDesktopTool/bin/Debug/net10.0-windows/KageDesktopTool.dll --session-check
rtk proxy dotnet src/KageDesktopTool/bin/Debug/net10.0-windows/KageDesktopTool.dll --content-layout-check
rtk proxy dotnet src/KageDesktopTool/bin/Debug/net10.0-windows/KageDesktopTool.dll --appearance-check
rtk proxy dotnet src/KageDesktopTool/bin/Debug/net10.0-windows/KageDesktopTool.dll --file-move-check
```

最后四项需在当前用户的交互式 Windows 会话中运行。`--session-check` 使用随机隔离目录及随机临时自启项，短暂显示测试头部、模拟输入 `Ctrl+Alt+K` 并恢复占用状态；`--content-layout-check` 核对实际 WPF 网格／列表、真实窗口边界、捕获释放、双击打开及外部变化；`--appearance-check` 检查真实外观输入、透明背景、调色盘焦点、图标切换及重启恢复；`--file-move-check` 使用真实鼠标/OLE、实际桌面随机命名夹具、隔离 Explorer 目录和真实冲突对话框，核对字节、源消失、取消及权限失败，短暂显露桌面后恢复窗口。结束后释放资源、关闭夹具 Explorer 并恢复 ACL，不读写正式工作区或以个人内容作夹具。日志和预览写到 `.scratch/desktop-folder/verification/`；构建及检查产物不纳入 Git。详见 [05 验收记录](docs/verification/05-content-and-layout.md)、[06 验收记录](docs/verification/06-appearance-and-icons.md) 和 [07 验收记录](docs/verification/07-file-moves-and-conflicts.md)。

结构：`src/Kage.Workspace/` 提供共用业务 interface 和真实状态／目录处理；`src/KageDesktopTool/` 提供 WPF、桌面宿主、托盘、热键、单实例及注册表适配器。`prototypes/` 保留已验收的原型。
