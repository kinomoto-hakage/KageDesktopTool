# KageDesktopTool

Windows 桌面整理工具，采用 C#、.NET 10 和 WPF。当前交付 [04 创建与运行生命周期](.scratch/desktop-folder/issues/04-create-lifecycle.md)：真实内容文件夹、透明桌面头部、重启恢复、托盘、`Ctrl+Alt+K`、单实例和用户登录自启。

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
- 头部显示名称及直接文件数。目前点击右侧按钮打开实际内容文件夹；下方内容展示、展开、拖动和尺寸调整由任务 05 实现。
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
```

最后一项需在当前用户的交互式 Windows 会话中运行；它使用随机隔离目录及随机临时自启项，短暂显示测试头部、模拟输入 `Ctrl+Alt+K` 并恢复占用状态，结束后释放资源并清理。不会读写正式工作区或以个人内容作夹具。日志和预览写到 `.scratch/desktop-folder/verification/`；构建及检查产物不纳入 Git。

结构：`src/Kage.Workspace/` 提供共用业务 interface 和真实状态／目录处理；`src/KageDesktopTool/` 提供 WPF、桌面宿主、托盘、热键、单实例及注册表适配器。`prototypes/` 保留已验收的原型。
