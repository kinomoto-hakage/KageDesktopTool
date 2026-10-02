# 04 创建与运行生命周期验收

验收日期：2026-10-02。当前用户 Windows 内核版本 `10.0.26300.0`，.NET SDK `10.0.302`、Windows Desktop Runtime `10.0.10`。对应 [任务 04](../../.scratch/desktop-folder/issues/04-create-lifecycle.md)，主业务入口为 `IDesktopWorkspace`。

## 业务检查

`tests/Kage.Workspace.Checks` 的 11 项行为检查全部通过：

1. 真实目录创建、空初始工作区、稳定标识／名称／坐标重启恢复与实际内容保留。
2. 创建前提交失败不产生无关联目录；创建后最终保存失败保留实际目录及操作记录，重启完成核对。
3. 主状态损坏只读加载备份、禁止悄悄覆盖、明确恢复并保留损坏证据；状态或 Folder 缺少必要字段会进入恢复状态。
4. Windows 非法字符、保留设备名、尾随空格／点及长度校验；同名编号、跳过、取消、大小写语义和同名文件占位。
5. 空间不足保留记录与内容，负坐标和 150% 缩放的显示区域恢复，不重叠位置持久化。
6. 自启实际配置与偏好同步、重复开启、关闭、平台失败和状态失败恢复，以及外部配置差异说明。
7. 保存根目录不可用时说明原因，明确另选后创建；同名占用及检查后出现竞争文件时排他创建不覆盖。
8. 已有根目录离线后明确另选用于新建；旧 Folder 原路径和稳定标识不丢失、不假装迁移完成。
9. 外部目录抢先创建且清理意图保存失败时，不自动认领；用户核对后才明确关联。
10. 自启中断按实际配置恢复，未执行意图不会在启动时擅自注册，未知实际配置保留待核对记录。
11. 不完整归属标识可经明确核对修复，保留原证据与用户内容，内部标识不计入文件数；不同有效归属标识不覆盖。

检查使用真实随机临时目录；只在状态存储及自启平台边界注入失败。测试清理校验绝对路径范围，无个人文件夹作为夹具。

## Windows 会话与发布目录

正式程序以 Release 配置发布到 `.scratch/desktop-folder/verification/发布包 含空格/`，从该位置完成实际会话检查，进程退出码为 0：

- 托盘菜单和真实 `Ctrl+Alt+K` 键盘输入打开创建界面，输入名称后实际目录及同名头部均出现；热键被占用时托盘仍可创建并有说明。
- 多个真实头部均挂接当前 Explorer 桌面宿主，可见且窗口边界不重叠。实际 WPF 背景 Alpha 位于 0 和 255 之间，生成并检查头部渲染预览。
- 设置关闭后托盘及头部继续存在；重复 EXE 进程发送请求后结束，只打开已有设置，不产生额外头部。
- 随机临时 HKCU `Run` 值实际开启、重复开启及关闭；绝对启动目标指向包内可运行 EXE，路径包含空格时仍能启动测试子进程。状态重新加载恢复自启偏好与 Folder 标识／坐标。
- 明确退出销毁头部与控制器窗口，释放热键及托盘，停止后台刷新；内容目录及字节内容保留。测试后临时启动值删除且再次读取为空，热键可以重新注册。

本地日志：`.scratch/desktop-folder/verification/windows-session.txt`；预览：`.scratch/desktop-folder/verification/正式头部.png`。这些是可重新生成的检查产物，不纳入 Git。没有通过注销当前用户触发实际登录事件；本票验证真实启动注册、可运行的发布目标和相应生命周期。显示桌面、Explorer 重启及多屏环境变化的完整验收分别由 11、12 承接；完整发布由 13 承接。

## Standards

只读复核通过，没有未解决的规范发现。备份恢复成功后已清除过期的阻塞提示，自启通知采用独立字段，不按展示文案决定业务分类；不完整标识的恢复流程、原证据保留和 README 描述一致。

## Spec

只读复核通过，没有未解决的规格发现。并发抢占目录不会被自动认领；归属标识完整刷新后原子移入。明确确认可修复已有无效标识并保留证据，其他有效标识会被拒绝。新增检查覆盖稳定标识、用户内容、文件计数及不同归属保护。

最终未解决发现：Standards 0 项；Spec 0 项。

## 复验命令

```powershell
rtk proxy dotnet restore tests/Kage.Workspace.Checks/Kage.Workspace.Checks.csproj --configfile NuGet.Config
rtk proxy dotnet run --project tests/Kage.Workspace.Checks --no-restore
rtk proxy dotnet restore src/KageDesktopTool/KageDesktopTool.csproj --configfile NuGet.Config
rtk proxy dotnet publish src/KageDesktopTool/KageDesktopTool.csproj -c Release --no-restore --output '.scratch/desktop-folder/verification/发布包 含空格'
rtk proxy dotnet '.scratch/desktop-folder/verification/发布包 含空格/KageDesktopTool.dll' --session-check
```

Windows 会话检查应以当前用户交互会话运行，受限沙箱不能用于证明桌面宿主或 HKCU 写入效果。项目构建通过，0 警告、0 错误。
