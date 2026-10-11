# Folder 样式、尺寸与 InternetShortcut 人工反馈验收

日期：2026-10-11。对应本地票据 04／05，用户最新确认：只有拖动位置松手时寻找空位；展开、方向、样式滑块与鼠标尺寸调整全部取消自动挪位。

## 原因与修复

- 闪烁：三秒刷新先用正式记录更新 HWND，然后重新应用样式草稿；真实窗口消息捕获到 9 次瞬时方向回切。刷新现在直接采用当前有效草稿，修复后两个完整刷新周期内回切次数为 0，真实目录新增内容仍可显示。
- 下／右分隔线：旧实现只增加厚度，保留头部原点，内侧分隔线没有跟随鼠标。现在固定外缘，按实际像素边界变化调整头部原点，分隔线与鼠标同向、展示宽高保持不变。四向真实输入在 150% 下误差最多 1 像素；业务检查覆盖 100%／125%／150%／200% 的外缘和取整。
- 样式厚度：上下为头部高度 42–82 DIP，左右为头部宽度 64–360 DIP；滑块右侧显示当前数值，两种厚度独立保留。高度加入正式草稿、预览、保存、取消、保存失败重试和过期校验。
- 受阻处理：尺寸、展开及样式入口只检查当前位置；受阻保持最后有效尺寸、折叠或有效草稿并说明原因。`NearestPlacement.Find` 仅在位置拖动提交时使用；真实占用落点释放仍可寻找最近空位。
- InternetShortcut：显示名称原本只隐藏 `.lnk`。现在 `.url` 同样隐藏显示后缀，保留真实扩展名、链接字节和显式图标资源。改名窗口编辑快捷方式显示名称并保留扩展名，拒绝空名称及末尾空格／句点。普通文件和同后缀目录不隐藏名称。

## 最终结果

- Release 构建：0 警告、0 错误。
- 完整业务回归：76 项全部通过；覆盖屏幕及邻居尺寸阻挡、四向分隔线、原地展开／样式、混合 DPI 轨迹、样式事务、真实内容类型、迁移和恢复。
- `--style-layout-feedback-check`：周期预览、真实鼠标分隔线、滑块数值、屏幕边界尺寸、方向／滑块／展开受阻、URL 实际移入和真实改名全部通过。
- `--four-way-input-check`：八种方向／名称组合、展开折叠、位置拖动、分隔线、外角、完整名称与裁剪、选择清除、保存故障及受阻方向提示通过。
- `--layout-input-check`：真实占用落点释放、原地展开受阻、分隔线、Esc、捕获丢失、保存故障及重启恢复通过。旧夹具的尺寸检查改为先由用户位置拖动到可展开位置，不再依赖自动挪位。
- `--four-way-restart-check`：两次实际窗口构建、四向样式、物理边界、内容及折叠／展开恢复通过。首次复验被前台 Codex 窗口遮挡，截图明确证明输入没有命中夹具；重新显露桌面后通过。
- `--shell-image-check`：隔离 URL／LNK 显式资源、尺寸与缓存回归通过；只读核对实际 Wallpaper Engine 在 16／32／48／96 DIP 下均显示蓝色主体图案。用户实际文件仍有 `.url`，没有修改用户文件。
- 变更复核及 `git diff --check` 通过；没有遗留临时诊断日志或新增测试专用业务入口。

日志与截图保留在 `.scratch/folder-grid-selection-and-header/verification/`；布局和图标资源日志位于 `.scratch/desktop-folder/verification/`。上下／左右样式窗口截图已视觉核对滑块、数值和应用按钮布局。临时夹具均使用唯一随机目录并核对范围后清理，不读取正式配置；实际图标检查只读读取用户相关文件。

## 实际环境与限制

实际输入与渲染覆盖 Windows、单屏 2560×1600、150% 缩放，工作区 2560×1528。多屏、混合 DPI、拔屏、主屏和系统缩放／分辨率切换未做真实硬件验收；相关业务轨迹通过不代表硬件覆盖。

## 复现命令

```powershell
dotnet build src/KageDesktopTool -c Release --no-restore --disable-build-servers
dotnet run --project tests/Kage.Workspace.Checks -c Release --no-build --no-restore
& 'src/KageDesktopTool/bin/Release/net10.0-windows10.0.19041.0/win-x64/KageDesktopTool.exe' --style-layout-feedback-check
& 'src/KageDesktopTool/bin/Release/net10.0-windows10.0.19041.0/win-x64/KageDesktopTool.exe' --four-way-input-check
& 'src/KageDesktopTool/bin/Release/net10.0-windows10.0.19041.0/win-x64/KageDesktopTool.exe' --layout-input-check
& 'src/KageDesktopTool/bin/Release/net10.0-windows10.0.19041.0/win-x64/KageDesktopTool.exe' --four-way-restart-check
& 'src/KageDesktopTool/bin/Release/net10.0-windows10.0.19041.0/win-x64/KageDesktopTool.exe' --shell-image-check
```

Windows 检查必须在真实交互式用户会话串行执行，避免其他窗口抢焦点。沙箱中临时状态文件的原子替换被拒绝不作为产品失败；本次使用提升权限的隔离检查完成验收。Shell 图标检查包含只读实际用户文件检查，复现环境缺少相应应用快捷方式时该部分无法覆盖。
