# 05 原生内容及不重叠布局验收

验收日期：2026-10-02。对应 [任务 05](../../.scratch/desktop-folder/issues/05-content-and-layout.md)。业务检查通过 UI 共用的 `IDesktopWorkspace`，布局输入通过该接口创建 `LayoutInteraction`，鼠标轨迹与尺寸预览只操作内存快照，结束后异步提交；目录与图标均在后台读取。

## 业务结果

- 真实目录展示直接文件、快捷方式和普通子文件夹；隐藏归属标识不展示也不计数。文件数包含快捷方式，不包含子目录。枚举失败返回已取得的实际项目及说明，不复用旧项目冒充成功，也不重新创建失联目录。
- 多个 Folder 独立展开；展开保留本头部位置，为冲突 Folder 搜索附近空位。空间不足时不提交任何新布局。折叠不改变其他 Folder 的展开状态。
- 屏幕四边和折叠／展开 Folder 四向接触轨迹覆盖大步输入不穿越、受阻后反向一像素、沿边滑动和结束后忽略输入。对角输入使用 swept AABB 检查真实直线，接触后消除法向位移并继续切向扫描。预览不提交持久状态。
- 折叠调整头部宽高，展开调整共享宽度及内容高度，位置保持固定；会越界或重叠的尺寸被拒绝。结束输入才保存，重启恢复位置、尺寸、展开及查看方式；过期输入不会覆盖更新后的布局。
- 继承 04 的空间不足保留记录、目录及设置刷新恢复入口。持久化可选的暂未展示标记，恢复时优先保留可见布局，再为隐藏记录寻找空位；兼容原状态文件。发布快照只使用已提交几何，不再次重排。当前拖动约束在所在显示器工作区域，多屏跨屏及 DPI 环境变化的完整处理由 12 承接。
- 输入或展开保存失败时返回实际失败，保留此前布局和用户内容；重启恢复上次有效状态。

## 真实 Windows 会话结果

`--content-layout-check` 在当前用户交互式会话中运行，退出码 0。受限沙箱无法访问 Explorer 桌面宿主，不能用于证明真实窗口结果；本次实际会话确认所有窗口宿主有效且可见。

- 真实短名、长名、文本、图片、`.lnk` 和子目录均显示实际 Shell 图标；快捷方式使用覆盖和其配置的图标，无通用占位图。
- 对实际 WPF 排列结果检查系统网格宽高、每个格子的图标和文件名中心、标题字体、换行及项间距。选择高亮下渲染网格及列表预览。
- 对本机原生 ListView 测得的行高检查列表行及相邻行距；宽度依次调整至 460、260、480、300 DIP，图标和文件名的左侧偏移均小于 0.6 DIP，右侧随视口伸缩，行宽保持自动值。
- 两个真实桌面窗口同时展开，使用 HWND 边界检查窗口均在工作区域内且不重叠；屏幕边界受阻后实际窗口立即反向一个物理像素。真实鼠标捕获释放结束输入，后续回放不再改变位置。
- 调用正式双击处理入口，隔离 `.lnk` 经默认 Shell 打开测试子进程并生成正确目标标记；普通子文件夹在资源管理器打开实际路径，检查后只关闭该随机夹具路径的窗口。
- 正式 3 秒后台刷新反映外部新增、删除和重命名，移除失效项目及图标；目录失联时清空项目、计数显示未知，保留 Folder 和说明，不生成空目录。重新加载状态保持布局和查看方式。

日志：`.scratch/desktop-folder/verification/content-layout-session.txt`。预览：`05-网格.png`、`05-列表.png`、`05-列表恢复.png`，已目视检查。这些可重新生成的产物不纳入 Git。

## 复验

```powershell
rtk proxy dotnet run --project tests/Kage.Workspace.Checks --no-restore
rtk proxy dotnet build src/KageDesktopTool/KageDesktopTool.csproj --no-restore
rtk proxy dotnet src/KageDesktopTool/bin/Debug/net10.0-windows/KageDesktopTool.dll --content-layout-check
rtk proxy dotnet publish src/KageDesktopTool/KageDesktopTool.csproj -c Release --no-restore --output '.scratch/desktop-folder/verification/发布包 含空格'
rtk proxy dotnet '.scratch/desktop-folder/verification/发布包 含空格/KageDesktopTool.dll' --content-layout-check
rtk proxy dotnet '.scratch/desktop-folder/verification/发布包 含空格/KageDesktopTool.dll' --session-check
```

业务全套 18 项通过；正式项目构建 0 警告、0 错误。更新后的含空格 Release 检查包中，05 内容／布局及 04 生命周期真实 Windows 检查均返回退出码 0。文件移动、外观编辑、显示桌面／Explorer 重启、多屏变化及最终发布由对应后续任务交付。

## Standards

独立只读复核后未解决发现 0 项。中文、领域术语、issue 和源码布局符合约定。采纳后台生命周期观察，在每个图标读取开始前检查关闭及刷新代次，不继续读取失效批次；已进入 Shell 的一次读取不能强制中断，返回后不写入已关闭控件。

## Spec

初次复核发现 2 项 P1，均已修复并用主要业务 interface 回归：对角大步输入此前可能绕过直线路径上的障碍；隐藏记录恢复此前可能挤动刚展开的头部，造成保存与展示坐标不一致。新增轨迹和真实目录检查分别先复现失败，再验证修复、正常刷新及重启一致性。独立复核修复后未解决发现 0 项。

最终未解决发现：Standards 0 项；Spec 0 项。
