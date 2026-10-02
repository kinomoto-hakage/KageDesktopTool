# 07 真实文件移动及同名冲突验收

日期：2026-10-02。对应 [任务 07](../../.scratch/desktop-folder/issues/07-file-moves-and-conflicts.md)。

## 实现与结果口径

`IDesktopWorkspace.MoveAsync` 接收固定多选集合、Folder／明确目录／实际桌面目标、异步冲突选择、逐项进度及取消令牌。批量在后台串行执行，返回逐项 `MoveItemResult` 和独立的 `StateCommit`，不把文件移动失败与状态提交失败混在一起。取消停止后续项目，已经完成的移动保留；状态提交失败仍按实际目录刷新，重启只核对内容，不重复执行移动。

同名文件与 `.lnk` 在扩展名前编号，目录在完整名称后编号。不设置覆盖标志；编号目标采用排他移动／创建，竞争同名不会被覆盖。普通子目录不转为桌面 Folder；内部归属标识及托管内容文件夹不能作为普通内容项目移动。

同盘使用原生排他移动；跨盘文件允许 Windows 复制后删除，并额外核对源是否消失、目标是否存在。跨盘目录先完整复制及刷新文件，再对每个源与目标核对 SHA-256，并持有同一排他删除句柄删除已复制文件；不递归删除复制期间新出现的源内容。失败／取消保留两边已有内容，结果列出源和实际目标。重解析路径暂不支持，明确报告失败。

原生 OLE 负责拖放预览。展示部分拖出在释放时识别鼠标所指 Folder、桌面空白处或 Explorer 的实际目录内容区／普通子目录，取消原生 Drop 交付后由共用接口执行一次。拖入也由本程序移动，返回效果为 None，防止外部源因全局 Move 标志删除跳过或失败项目。结果不根据全局拖放效果伪造成功或目标路径。桌面通过 Windows `DesktopDirectory` 获取，本机验收桌面已重定向到 D 盘。

## 检查

- 全套 25 项共用接口检查通过；新增四组涵盖任意文件字节、`.lnk` 文件、普通子目录、双向批量、Folder 间移动、同名编号／跳过／取消、已完成保留、占用、无效目标、自身／子目录、状态提交故障、重启核对及 C／D 跨盘。真实 ACL 拒绝源删除和跨盘子目录复制失败均不报告移动成功，并恢复隔离权限后清理。
- 真实 Windows 会话使用实际鼠标/OLE 和随机命名夹具：Explorer 多选拖入、Folder 间多选、拖出 Explorer 内容区、实际桌面多选拖入／拖出均核对路径、字节和源消失；真实 `.lnk` 的指向文件保持原位。
- 真实同名对话框依次选择保留两份、跳过及取消，核对五个项目的逐项结果与未移动源；原生拖出 Esc 取消保持源。真实目标 ACL 拒绝写入，返回逐项失败并保留源字节。
- 构建启用警告即错误。Release 会话回归和规范／规格复核结果记录在任务 Answer；日志位于忽略目录 `.scratch/desktop-folder/verification/`。

```powershell
rtk proxy dotnet run --project tests/Kage.Workspace.Checks --no-restore
rtk proxy dotnet build src/KageDesktopTool/KageDesktopTool.csproj -c Release --no-restore
rtk proxy dotnet src/KageDesktopTool/bin/Release/net10.0-windows/KageDesktopTool.dll --file-move-check
```

会话检查先临时显露桌面，结束后恢复被最小化的窗口、关闭夹具 Explorer、恢复 ACL 并清理随机夹具。桌面夹具的源／目标均验证绝对路径及随机前缀；不会清理个人内容。
