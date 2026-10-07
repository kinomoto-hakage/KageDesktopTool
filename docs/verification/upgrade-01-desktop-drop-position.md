# 桌面移出落点与快捷方式箭头修复

日期：2026-10-07。对应本地升级票 01 的追加反馈：从 Folder 移出到桌面后放在鼠标指定位置，移除 Folder 内部分图标残留的快捷方式箭头。

## 复现与原因

在修复前的实现中扩展已有真实 Windows 检查：

- `KageDesktopTool.exe --file-move-check`：通过实际鼠标／OLE 将随机文件、真实快捷方式与目录移入 Folder 再拖到桌面中部。文件移动与字节核对成功，新增“移出后的首个桌面图标位于鼠标释放位置附近，而不是最左空位”断言准确失败，退出 1。
- `KageDesktopTool.exe --shell-image-check`：临时 `.url` 指定本包实际 ICO 资源；将输出像素与独立解码的原始 ICO 对比，新增无箭头断言准确失败，退出 1。

移动链路只保存目标目录，未保存释放位置；业务移动后 Windows 按默认空位安排新图标。图标模块原本只将 `.lnk` 识别为需要隐藏箭头的快捷方式，遗漏了 InternetShortcut `.url`，其箭头仍被作为 Shell 覆盖合成。

## 修正行为

- 释放鼠标时固定一份物理屏幕坐标，用同一坐标解析目标并安排图标。跨 Folder、Explorer、内部重排和 Esc 的现有分流保持。
- 文件仍只通过 `IDesktopWorkspace` 移动一次。桌面定位仅使用本批 `Success` 项的 `ActualPath`，因此冲突保留两份后的实际名称也可定位；跳过、失败和取消项不参与。
- 通过桌面 `IShellFolder` 解析相对名称取得实际子 PIDL，等待 `IFolderView.GetItemPosition` 能识别这些项目，再通过 `SelectAndPositionItems` 提交屏幕落点。直接解析完整路径得到的 PIDL 并非桌面视图中的子项目，实测不能定位，最终使用相对名称。
- 连续移入再移出同一文件时，旧的目录更新曾覆盖新位置。最终先在后台通过 `SHChangeNotify(UPDATEDIR, PATHW | FLUSH)` 交付目录变更，再等待真实项目并定位；UI 线程继续响应。
- 多选按桌面图标间距安排在落点附近的不同网格，屏幕边缘向内展开。保留 Windows 网格吸附；不修改系统自动排列偏好。若系统开启自动排列、桌面视图消失或定位失败，文件结果保持真实成功，原因加入同一批结果详情，不重复移动。
- 文件移动结束即停止尚未显示的进度定时器，避免仅因 Shell 位置更新迟到而闪出中央进度窗口；已经显示的耗时进度正常收尾。
- `.lnk` 与 `.url` 共用现有系统快捷方式覆盖识别，只去掉快捷方式箭头，保留自定义图标和其他适用状态覆盖。无全系统图标或注册表修改。

接口依据：[Microsoft 桌面图标定位说明](https://devblogs.microsoft.com/oldnewthing/20211122-00/?p=105948)、[IFolderView.SelectAndPositionItems](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifolderview-selectandpositionitems)、[屏幕坐标转换标志](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/ne-shobjidl_core-_svsif)、[Shell 变更通知及交付标志](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shchangenotify)。

## 验证

最终完整自包含包：`.scratch/desktop-folder/verification/upgrade-01-drop-position/`。Release 发布 0 警告／0 错误，以下检查全部通过：

- 完整 63 项业务检查退出 0。
- 修复 Shell 更新竞态后，完整包 `--file-move-check` 连续三次退出 0；最后一次进一步加入“后续 Shell 目录更新仍保持鼠标落点”和“桌面定位随同批移动仅记录一次结果”，均通过。既有 Explorer／Folder／桌面多选、冲突保留两份、跳过、批量取消、Esc 和真实 ACL 失败均通过。
- 完整包 `--move-progress-check` 退出 0，快速移入／移出不闪窗、耗时进度可取消及实际字节保留通过。
- 完整包 `--shell-image-check` 退出 0，临时 `.lnk` 与 `.url` 在 16／32／48／96 像素下与原始 ICO 像素一致，无箭头；资源变更缓存、中文 ANSI 路径及实际 ChatGPT／Wallpaper 图标四档检查通过。
- 完整包 `--publish-check` 退出 0，运行时和资源完整；`git diff --check` 通过。

两轴独立只读审查：需求审查 0 项；规范审查首次发现 1 项票据评论插入顺序问题，已移到既有 Comments 记录末尾，等待最终复核。代码 smell 0 项。

首次完整包实测：多选释放点 `(1580,764)`，批次首个图标真实边界 `(1495,740,115,127)`；随后同一文件重新移入，单项释放点 `(1880,964)`，图标边界 `(1840,887,115,127)`。多选项目在附近的不同网格，两个落点均未回到最左空位。

检查使用随机临时内容目录及桌面项目，只清理已核对归属的夹具。桌面落点以 Windows UIAutomation 返回的真实图标边界核对，拖动使用实际鼠标输入，不用事件注入替代。实测环境为当前 Windows 单屏 150% DPI；真实多屏及混合 DPI 落点尚未验收。系统自动排列开启时的真实系统交互未修改或实测，代码保留该系统约束并记录原因。
