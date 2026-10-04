# Windows 快捷方式原生菜单三秒等待：根因与修复

## 结论

本机根因是 **AISTONE GLOBAL LIMITED 的 PCAppStoreExt 现代卸载菜单组件**，不是 KageDesktopTool 的菜单代码。它给 `.lnk` 注册的 `PCAppStoreUninstall` 命令在查询状态时等待约 3000 ms，Windows 现代与传统菜单都会访问该动态命令；因此桌面、Explorer 和 Kage 共同遇到延迟。

机器是 Windows 11 26H2、26300.9457。不能从这台机器的扩展缺陷推断所有 Windows 11 都必然有三秒延迟。微软 [GetState 文档](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-iexplorercommand-getstate) 提供快速查询与 E_PENDING 机制；本组件在 `fOkToBeSlow=false` 时仍实测 3006 ms，`true` 时 3000 ms。

组件身份：包 `PCAppStoreExt_1.0.0.0_x64__4ptzsrs9z9qm8`，CLSID `{24615B2E-CF96-48DA-B77B-B2602608A357}`，清单注册类型 `.lnk`。独立探针不引用 Kage、不调用 Invoke。其他关联现代命令（更新、评分、QQ、记事本、Code、PDF）GetState 实测约 0–5 ms；旧式 PCAppStore 卸载扩展初始化／菜单构建约 0–3 ms。

修复仅撤销**当前用户这个独立现代菜单稀疏包**的注册；保留 PCAppStore 主软件、旧式强力卸载、其他包以及 Windows 现代／传统菜单偏好。现代菜单不再显示该组件的“强力卸载此软件”，仍可通过“显示更多选项”的传统入口使用该功能。原 DLL 没有修改，此为精确隔离故障组件，组件代码的永久修复需要供应商改进。

## 因果与真实输入验证

先用独立 `IShellFolder/IContextMenu` 构建菜单：实际 MSI 快捷方式 3986–4800 ms，普通 EXE 611 ms，普通文件 417 ms。逐项独立测量定位到现代卸载组件，而非猜测 NVIDIA、同步软件或 Windows 本身。

旧 Shell 阻止值的 A/B/A：原状态 4018 ms → 临时阻止 685 ms → 恢复 3663 ms，传统命令集合相同。但真实现代 Explorer 不受该旧式阻止值控制，即使刷新 Explorer 仍慢；已精确撤回该值，不能把它当作最终系统修复。Explorer 刷新前保存唯一用户目录，刷新后重开，不终止其他程序。

采用受支持的包注册撤销后，真实 Explorer 首次验证现代菜单 660 ms、传统 192 ms。最终工具恢复组件后独立菜单又回到 4003 ms；再次应用修复后真实结果：

| 场景 | 修复前 | 修复后 |
| --- | ---: | ---: |
| Explorer 现代菜单完成加载 | 4247 ms，六个加载占位 | 695 ms |
| Explorer “更多选项”传统菜单 | 3193 ms | 193 ms |
| 桌面现代菜单完成加载 | 3482 ms，六个加载占位 | 249 ms |
| 桌面“更多选项”传统菜单 | 3155 ms | 131 ms |

现代菜单短暂异步加载仍可能出现，例如 Explorer 首次约 0.7 秒内完成；不能声称所有加载占位永远为零。最终桌面样本峰值占位为 0。真实鼠标检查只创建唯一副本，不执行其目标，只关闭自身创建的 Explorer 窗口，副本删除前核对原字节哈希。用户原快捷方式没有修改。

## 难度与恢复

根因定位为中等难度：经典处理器计时正常，真正问题藏在包注册的现代 GetState 路径。隔离修复难度较低；可靠恢复为中等难度，该组件是 sparse package，必须用原签名 MSIX **及原 ExternalLocation** 恢复，不能仅按普通包安装。此恢复路径已实际运行成功；组件持久设置也有备份，零字节加密漫游锁由 Windows 重建。

工具：`tools/windows/Fix-PcAppStoreMenu.ps1`。执行前校验包名／family、非 framework、可移除状态，备份原安装包与组件设置，验证安装包哈希；不操作任意用户指定包。当前修复状态为 Apply。

```powershell
rtk proxy powershell -NoProfile -File tools/windows/Fix-PcAppStoreMenu.ps1 -Mode Status
rtk proxy powershell -NoProfile -File tools/windows/Fix-PcAppStoreMenu.ps1 -Mode Restore
rtk proxy powershell -NoProfile -File tools/windows/Fix-PcAppStoreMenu.ps1 -Mode Apply
```

备份在 `.scratch/windows-shell-latency/final-backup-v2/`，不提交 Git；诊断／截图／原始包副本在同 effort 的忽略目录。将来供应商重新注册此插件时，需重新验证组件是否已修复，不能假定此隔离设置永久阻止供应商安装。

## 项目检查

Kage 产品菜单逻辑没有因这个系统修复而裁剪命令。既有菜单失效检查原先要求重新构建必须大于 1000 ms，在系统修复后不成立；改为使用实际 QueryContextMenu 的高精度计时，确认确实重新构建，不以系统保持缓慢作为成功条件。正式 Runtime 原地／无红叉／准备取消和缓存失效检查通过，文件、系统失效和过期后实际重建为 64–67 ms。实际 MSI 快捷方式首次准备 **651 ms**，重复 **8 ms**，WPF 最大停顿 **46 ms**。

该系统设置已作用于现有 Kage，可继续使用原试用包，不需要为了生效更换 EXE。后续仅诊断计时精度变化，不改变文件菜单调用逻辑。
