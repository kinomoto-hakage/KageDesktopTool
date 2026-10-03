# 09 更换根目录、整体迁移及当场失败恢复验收

验收日期：2026-10-03。对应 [任务 09](../../.scratch/desktop-folder/issues/09-change-root-and-rollback.md)。

## 用户入口及事务次序

设置中的“选择目录…”及“迁移并保存目录”经共用 `IDesktopWorkspace.MigrateRootAsync` 迁移全部活动内容目录和 `RetainedFolders`，更新工具保留内容快捷方式。相同根目录不操作；先检查目录可用性、实际路径关系、归属和全部目标同名冲突，不覆盖、合并或跳过冲突目录。已删除的展示入口不会复活。迁移结果窗口显示逐项原／新路径和完成数量，支持“取消迁移并恢复”；关闭进行中的窗口或托盘退出会先请求取消并等待结果。

共用业务信号量覆盖清单预检、文件操作、快捷方式更新、配置提交及当场恢复，其他修改排队；桌面布局／外观草稿和 UI 文件操作在此期间暂停。文件与状态处理运行在后台，不阻塞进度窗口。单个原生文件复制完成后检查取消，停止后续内容并开始恢复。

旧根目录离线时仍保留任务 04 的明确另选路径：“离线时另选新建目录”沿用 `SelectRootAsync`，只更换后续新建位置并保留既有内容关联；可用旧根目录不能通过该入口绕过迁移。

仅所有目录、关联快捷方式和最终配置提交均完成才报告成功并切换正常根目录。每个目录采用排他创建、完整复制、SHA-256 字节及 NTFS 命名流核对，再逐文件核对并移除对应源；同盘也使用相同复制路径，不以同盘改名冒充跨盘支持。普通跨盘文件移动复用提取的目录复制适配器，并保留原有逐项结果语义。

## 日志及恢复边界

`WorkspaceState.PendingRootMigration` 保存操作标识、旧／新根目录，以及每项稳定标识、原／新内容路径、关联快捷方式路径、执行／恢复阶段和错误。阶段在文件或链接效果之前原子提交；更新链接使用原目标和稳定归属核对，原子替换工具生成的 `.lnk`。关联快捷方式的原／新目标分别为该项 `SourcePath`／`DestinationPath`。日志校验要求清单与全部正常活动／保留映射一致，拒绝未知标识、非法阶段、冲突未完成操作及重复目标。

失败或取消停止继续迁移，逆序尝试恢复内容和快捷方式。跨盘部分副本或源部分移除时只补回缺失内容，验证一致后清理对应副本；同名不同字节、重解析路径和其他归属不覆盖。旧正常配置保留；恢复不完整或恢复状态提交失败返回 `RecoveryRequired`，保留日志、两边实际路径、阶段和错误，暂停修改。快照在原目录已移走时定位可确认归属的实际新路径，设置中的异常说明也列出保留目录及链接。

重启发现迁移日志后只读保留并暂停修改，供任务 10 核对真实目录和链接后接入启动恢复。本票不自动重做或提交中断迁移。

## 检查及证据

新增七组公共业务接口检查采用真实随机临时目录、实际 Shell `.lnk` 及真实字节。只在指定目录传输、快捷方式和状态保存边界注入故障，覆盖完整迁移、保留入口不复活、取消前／中／最终提交前、独占修改次序、实际移动后异常、恢复失败路径、同名和非法路径、意图／最终提交保存失败、非法日志、快捷方式实际更新后异常及原目标恢复。

独立规格审查发现只读文件复制后刷新及副本清理会失败；先用真实只读文件复现，再将复制／持久刷新集中到 `CopyAndFlush`，仅暂时解除本次目标的只读属性后恢复。已核对副本对应的只读源使用排他删除句柄的 `FileDispositionInfoEx`，允许删除只读文件且不改变其属性；依据 [FILE_INFO_BY_HANDLE_CLASS](https://learn.microsoft.com/en-us/windows/win32/api/minwinbase/ne-minwinbase-file_info_by_handle_class) 和 [删除标志定义](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntddk/ns-ntddk-_file_disposition_information_ex)。新增检查确认成功迁移和提交前取消恢复均保持字节与只读属性。规范审查提出的重复字节核对也已提取共用函数；最终两轴均 0 项遗留。

真实跨盘检查在 C: 临时源与 D: 仓库下随机验证目标间迁移，检查嵌套字节、文件与目录 NTFS 命名流、源实际消失；源文件占用导致部分复制失败时核对旧根目录和完整恢复。该项已实际验证，无替代同盘检查。

`--root-migration-check` 在实际用户 Windows 会话使用真实设置按钮、Explorer 桌面头部、进度和取消按钮。仅第二目录使用可控等待适配器固定取消时机，第一目录及快捷方式效果均为真实操作。检查成功、1 / 2 进度、暂停布局／外观及当场取消恢复。日志为 `.scratch/desktop-folder/verification/root-migration-session.txt`，预览为 `09-迁移成功.png`、`09-取消恢复.png`。随机内容、隔离链接、窗口及资源已清理。

```powershell
rtk proxy dotnet run --project tests/Kage.Workspace.Checks --no-restore
rtk proxy dotnet build src/KageDesktopTool/KageDesktopTool.csproj -c Release --no-restore
rtk proxy dotnet src/KageDesktopTool/bin/Release/net10.0-windows/KageDesktopTool.dll --root-migration-check
```

最终完整 37 项业务检查通过，Release 构建为 0 警告／0 错误。新增 Windows 根迁移检查及既有 04 生命周期、05 布局、06 外观、07 文件移动、08 Folder 操作检查通过。两项会话检查曾重叠导致改名窗口焦点断言失败，串行重跑通过；最终结果以串行验收为准。完整发布包仍由任务 13 交付。
