# 06 外观及图标验收

验收日期：2026-10-02。对应 [任务 06](../../.scratch/desktop-folder/issues/06-appearance-and-icons.md)。界面和业务检查共用 `IDesktopWorkspace`，外观草稿通过该接口打开、应用和取消；颜色及透明度预览独立于已保存的工作区快照。

## 业务结果

- 默认灰色 `#666666`，背景不透明度 `.68`。HEX 支持可选 `#` 的六位色值并规范为大写；RGB 三分量限定为 0–255 的整数；各输入同步同一颜色。无效值保留上次有效预览，返回具体原因并禁止应用。
- 透明度滑块仅修改背景画刷 alpha。预览不保存、不枚举目录、不更改内容路径或布局位置；后台刷新重新绘制后仍恢复当前草稿预览。
- 取消、Esc 或关闭窗口丢弃草稿并恢复打开前外观；应用先保存，成功后才更新工作区快照。保存失败保留原状态和草稿，允许重试或取消。
- 应用只更新目标 Folder 的颜色及透明度，保留编辑期间已提交的展开和查看方式。其他 Folder 外观独立，重启完整恢复。过期草稿不覆盖其他会话已提交的外观；已取消的草稿不能提交。
- 默认 D「K 文件夹」，A／B／C／D 均可保存及重启恢复；无效选择或保存失败保留原方案。托盘切换成功后更新托盘、选中标记及已打开窗口，EXE 内置图标继续使用 D。

## 真实 Windows 会话

`--appearance-check` 使用随机临时目录，不访问正式工作区或用户内容。受限沙箱无法访问 Explorer 桌面宿主，真实会话检查在当前用户交互式 Windows 会话执行。

- 检查真实 HEX／RGB 输入同步、无效输入提示和应用按钮禁用、调色盘颜色更新。
- 拖动滑块至 100% 透明时背景 alpha 为 0，至 75% 时 alpha 为 64；窗口、内容、文件名及 Shell 图标保持不透明，实际 HWND 边界和布局记录不变。
- Windows 原生调色盘显式使用外观窗口为 owner，父窗口在模态期间禁用且调色盘拥有前台焦点；取消调色盘保持当前草稿。外观窗口关闭恢复原值，应用保存后关闭。
- 四套图标逐一切换，核对托盘选中标记及所有打开窗口使用相同图标；重新创建运行时恢复保存的 B 图标和各 Folder 独立外观，真实内容字节保持原值。
- 包内 EXE 图标与 D 图标资源逐像素比较。四套应用 PNG 嵌入程序集，ICO 随包复制，不依赖运行时访问原型目录。

日志及预览位于 `.scratch/desktop-folder/verification/`：`appearance-session.txt`、`06-外观设置.png`、`06-完全透明背景.png`、`06-半透明预览.png`。已目视核对真实 WPF 预览；完全透明背景下文字与 Shell 图标仍清晰。屏幕截图可能被其他前台窗口遮挡，因此不作为本票透明合成的证据；验收使用真实桌面 HWND 及 WPF 背景 alpha、前景图标和文本检查。所有测试窗口和临时目录在结束后清理，可重新生成的产物不纳入 Git。

## 复验

```powershell
rtk proxy dotnet run --project tests/Kage.Workspace.Checks --no-restore
rtk proxy dotnet build src/KageDesktopTool/KageDesktopTool.csproj --no-restore -t:Rebuild
rtk proxy dotnet src/KageDesktopTool/bin/Debug/net10.0-windows/KageDesktopTool.dll --appearance-check
rtk proxy dotnet build src/KageDesktopTool/KageDesktopTool.csproj -c Release --no-restore -t:Rebuild
rtk proxy dotnet publish src/KageDesktopTool/KageDesktopTool.csproj -c Release --no-build --no-restore --output '.scratch/desktop-folder/verification/发布包 含空格'
rtk proxy dotnet '.scratch/desktop-folder/verification/发布包 含空格/KageDesktopTool.dll' --appearance-check
rtk proxy dotnet '.scratch/desktop-folder/verification/发布包 含空格/KageDesktopTool.dll' --content-layout-check
rtk proxy dotnet '.scratch/desktop-folder/verification/发布包 含空格/KageDesktopTool.dll' --session-check
```

已有构建缓存可能遗漏新增 WPF 资源，本次采用完整重建核对资源进入 Debug／Release 程序集。最终发布由任务 13 继续交付。

结果：全套 21 项业务检查通过；Debug／Release 构建均 0 警告、0 错误。含空格路径 Release 包的 06 外观、05 内容布局、04 生命周期真实 Windows 检查均退出码 0。移除被前台窗口遮挡的截图步骤后，最终 Release 外观检查再次通过。

## Standards

独立只读复核 0 项发现。中文、领域术语、issue 状态和模块职责符合仓库约定，未发现具有实际影响的代码气味。

## Spec

独立只读复核 0 项发现，未发现遗漏、错误实现或超出本票的功能扩展。颜色同步及非法输入、取消和保存失败、独立背景 alpha、图标持久化及内置 D 均与本票一致。代理进行了静态复核；上述真实 Windows 验收由主代理运行。

最终未解决发现：Standards 0 项；Spec 0 项。
