# 升级 02：Folder 位置、展开与尺寸调整验收

日期：2026-10-04。对应 [票据 02](../../.scratch/desktop-tool-upgrade/issues/02-folder-layout-and-sizing.md)，审查基线为 `4b441fb939a8b7090e346ad1a9bac3ad7adec76c`。

## 实现

共用 `IDesktopWorkspace` 作为已批准的验收边界。头部拖动预览允许经过其他 Folder，仅工作区边界和屏幕空洞约束路径。释放与展开共用最近位置搜索，只修改当前 Folder；当前位置可用时保留原位。无可容纳位置时保留已提交布局或折叠，并返回明确原因。后台显示恢复仍沿用原有布局恢复规则。

最近位置按头部左上角锚点的物理像素距离计算。锚点所在工作区优先；其余显示器共同比较距离，平局按纵坐标、横坐标确定。窗口覆盖使用工作区并集、当前整体尺寸、目标 DPI 和既有 12 物理像素间隙。搜索逐纵坐标处理横向可用区间，不使用粗网格；混合 DPI 接缝核对实际度量选择。

展开的 Folder 增加 8 DIP 可命中分隔线，拖动只改变 42—82 DIP 范围内的头部高度，展示部分高度、共享宽度和左侧位置保持不变。分隔线与右下角尺寸把手按目标显示器缩放将系统鼠标像素换算为 DIP。鼠标移动只预览几何，不访问内容目录或写入状态；释放提交、Esc 取消、异常丢失捕获提交最后可用布局。保存失败恢复已提交快照，旧输入继续拒绝覆盖新布局或显示环境。

未新增持久状态字段；已有头部高度、位置、尺寸、展开状态及内容映射继续使用原格式。现有旧状态、备份、隐藏记录与显示恢复检查保留。

## 验证结果

- 完整 62 项业务检查通过，包括新增 4 组升级布局检查：自由穿越与最近释放、底部展开及独立调高、显示器优先／负坐标／空洞／容量不足、最近位置与小工作区独立全像素穷举一致。旧路径碰撞及展开挪动其他 Folder 的断言已替换，边界、间隙、尺寸重启、保存失败和显示恢复回归保留。
- 首条新增检查在基线实现明确失败于“预览可进入占用落点”，实现后通过。受限沙箱中状态原子替换曾返回拒绝访问；正常 Windows 权限下随机隔离夹具通过。
- Release 构建通过，警告即错误，0 警告、0 错误。
- 新增 `--layout-input-check` 在实际 Explorer 宿主上通过系统鼠标输入验证：占用落点预览、直接穿越、最近空位释放、其他入口不变、分隔线命中与独立调高、拖动和分隔线 Esc 取消、丢失系统捕获、保存故障恢复、底部展开及重启。每个真实 HWND 的物理边界与业务记录一致，内容文件保留。
- 本机实际工作区 `(0, 0, 2560, 1528)`、150% 缩放。分隔线截图已查看，头部和内容边界清晰。
- 既有 `--display-dpi-check` 通过实际绘制、输入、显示通知恢复、125%／150%／200% 度量注入和右下角调尺寸；注入未改变系统配置。旧行高断言更新为适配票据 01 的图标档位，小图标列表仍核对原生行高。
- 既有 `--content-layout-check` 通过列表加宽／缩窄、内容布局、捕获丢失和重启；夹具显式将邻居放远，为宽度回归预留空间，适配展开不再挪动邻居的规则。

真实日志和截图保存在忽略目录 `.scratch/desktop-folder/verification/`：`upgrade-02-layout-input.txt`、`upgrade-02-divider.png`、`display-dpi-session.txt`、`content-layout-session.txt`。

## 实际覆盖限制

实际多屏、负坐标屏幕、混合 DPI 硬件跨屏、物理拔屏、切换主屏及系统缩放／分辨率变化未验证。这些场景有业务轨迹与度量注入证据，不能替代硬件会话验收。本次没有改变显示设置、重启 Explorer 或读取正式用户状态；真实输入夹具使用唯一随机临时目录，不注册自启，结束后恢复鼠标与桌面。

## 独立审查

按 `code-review` 技能，以固定基线进行 Standards 与 Spec 两轴独立审查，结果待记录。

## 复现

```powershell
rtk proxy dotnet run --project tests/Kage.Workspace.Checks --no-restore -- 升级布局
rtk proxy dotnet run --project tests/Kage.Workspace.Checks --no-restore
rtk proxy dotnet build src/KageDesktopTool/KageDesktopTool.csproj -c Release --no-restore
rtk proxy dotnet src/KageDesktopTool/bin/Release/net10.0-windows/KageDesktopTool.dll --layout-input-check
rtk proxy dotnet src/KageDesktopTool/bin/Release/net10.0-windows/KageDesktopTool.dll --display-dpi-check
rtk proxy dotnet src/KageDesktopTool/bin/Release/net10.0-windows/KageDesktopTool.dll --content-layout-check
```

真实 Windows 检查串行运行，短暂显示桌面并操作鼠标，需留出输入；在正常交互式会话执行。
