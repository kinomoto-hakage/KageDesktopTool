# 01：Folder 网格与完整图标名称

对应票据：[01-grid-and-full-names](../../.scratch/folder-grid-selection-and-header/issues/01-grid-and-full-names.md)。实施基线：`b78accf9841333caeae72abddcc4567a148279d2`。

## 实现

- 四档采用目标 DPI 的系统标题字体，分别计算格子宽高及图标间距；普通标签保留三行和末尾省略。行高向上对齐物理像素，避免 150% 舍入后第三行放不下；列表仍使用原生 ListView 行高。
- 当前选中项的完整名称放在固定格子的 Canvas 中，随整个内容滚动，由 ScrollContentPresenter 和所属视口裁剪。增加的是内容末端，图标格子和 Folder 几何不变。
- 网格预留滚动条宽度，避免全文第一次产生滚动范围时减少列数。列表保留自动滚动条。
- 名称沿用原 ListBoxItem 的命中路由、选择集合、原生菜单和拖动。单选／多选最后操作项及键盘定位共用一个临时全文对象，不写入配置。
- 普通身份刷新保留当前项；可靠改名更新其路径，删除或取消清理全文。必要可见性调整只发生在当前项变化时；滚轮、滑块及重复点击全文保留用户偏移，收起只夹到有效末端。
- 多选的操作次序只保存在当前会话，Ctrl 取消当前项后恢复剩余选中项中的最后操作项，可靠改名同步迁移这份次序；不会按排序末项回退，也不持久化选择。

## 验证

测试边界沿用已批准规格的 `IDesktopWorkspace` 与真实 Windows 会话。没有直接设置选择或注入 WPF 事件作为本票验收。`tdd` 的回归先后证实四档布局、未实现的全文展开、临界列宽以及三行实际高度在对应修正前失败、修正后通过。

- Release 编译：零警告、零错误。
- 工作区完整套件：66 项通过。内容视图 6 项单独通过，涵盖四档合法值、独立列表／网格设置、排序、旧字段缺省、保存失败及重启恢复。
- `--content-input-check --grid-names-only`：真实文件和 .lnk、系统鼠标与键盘，覆盖四档布局、单选与 Ctrl 多选、End 键、覆盖文字点击／双击／原生右键、末行全文滚轮和滑块、取消／换项的偏移保持、普通刷新、可靠改名、删除、下方项目命中恢复、全文区域多选跨 Folder 真移动及临界列宽。
- `--content-input-check`：完整既有输入回归通过，包含 Ctrl／Shift 点击与框选、自动滚动、捕获丢失、各类双击、单项／多项原生菜单、真实改名／删除、内部重排、跨 Folder 实际移动、列表／网格各四档、滑块与轨道。
- `--content-layout-check`：通过真实桌面宿主、多个展开窗口、列表加宽／缩窄、实际窗口布局、内容变化和重启恢复。

日志及 PNG 在忽略目录 `.scratch/desktop-folder/verification/`：`content-grid-names-session.txt`、`grid-names-16/32/48/96.png`、`grid-names-collapsed.png`、`grid-names-expanded.png`、`grid-names-last-bottom.png`、`content-input-session.txt`、`content-layout-session.txt`。已目视检查全文展开、末端、视口裁剪及四档截图。

## 已有回归限制

以下两项在本次 EXE 和从实施基线独立编译的 EXE 下出现相同失败，不能记为通过，也没有修改其产品行为或验收标准：

- `--display-dpi-check`：当前实际 150% 的图像、文字与鼠标命中通过；随后注入 125% 时 HWND 为 `540×462`，预期为 `450×385`，边界断言失败。对照日志 `grid-baseline-display-dpi.txt`。后续注入检查未完成。
- `--file-move-check`：Explorer 多选移入及 Folder 间实际移动通过；随后等待 Explorer 前台命中超时，尚未开始拖出 Explorer。对照日志 `grid-current-file-move.txt`、`grid-baseline-file-move.txt`。本票完整内容输入及全文专项的跨 Folder 实际移动通过；本次外部拖出及该检查后续桌面／冲突／取消流程未完成。

实际环境为 Windows、单屏、150%。硬件多屏、混合 DPI、拔屏、主屏及系统缩放／分辨率切换未实际验证；历史结果和注入度量不替代本次硬件验收。票据 02、03 尚未实现，因此外部统一清除及四向视口组合验收由后续票据负责。

## 复验命令

当前环境未安装 `rtk`，直接使用 `dotnet`。真实 UI 检查串行运行，使用唯一临时夹具并只清理所属路径。

```powershell
dotnet build src/KageDesktopTool/KageDesktopTool.csproj -c Release --no-restore --disable-build-servers
dotnet run --project tests/Kage.Workspace.Checks --no-restore --disable-build-servers
python -c "import subprocess; raise SystemExit(subprocess.run(['src/KageDesktopTool/bin/Release/net10.0-windows10.0.19041.0/win-x64/KageDesktopTool.exe','--content-input-check','--grid-names-only']).returncode)"
```

将最后命令中的检查参数换为 `--content-input-check`、`--content-layout-check`、`--display-dpi-check` 或 `--file-move-check` 可复验相应流程。

## 审查

按 `code-review` 对照固定基线并行审查：Standards 未发现硬性违规或需要处理的 smell；Spec 补审发现 1 条 P2（Ctrl 取消当前项后错误地按排序末项回退）。已用真实四步输入先复现失败，再通过临时操作次序修正；修正复审结果在票据收尾时记录。
