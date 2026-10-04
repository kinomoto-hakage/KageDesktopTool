# 任务 01：原生菜单等待时间优化

## 原因与实测反馈

2026-10-04 用户继续报告右键到菜单出现需 3–4 秒，并要求继续未完成的优化。当前基线为 `9947248be210cfdfaa3521f1d7222831e1f1fcea`，保持 Folder 原地显示 Windows 原生传统完整菜单、没有红叉、不经过资源管理器的要求。

旧完整包只读检测实际 MSI Afterburner 快捷方式：首次准备 4092 ms，取得对象 78 ms、构建命令 3839 ms；再次准备 3107 ms，取得对象 2 ms、构建命令 3091 ms。WPF 最大停顿 47 ms。等待集中在 `IContextMenu.QueryContextMenu`，此前把调用移到 STA 线程解决了界面冻结，但没有消除命令准备时间。

隔离比较 `CMF_ASYNCVERBSTATE`、`CMF_ITEMMENU`、`CMF_DONOTPICKDEFAULT` 及无附加标志，重复构建均为约 3.2 秒；复用同一 `IContextMenu` 也约 3.2 秒。复用完整已构建原生 HMENU 的只读探针实际可见时间为 41／22 ms。采样的栈候选涉及 `BaseDataDrivenCommand.GetState` 与 COM 等待，不能据此断言某个具体第三方软件造成全部延迟。探针仅在忽略目录运行，没有注册组件、注入 Explorer、执行用户文件命令或改变扩展设置。

正式真实鼠标反馈命令：

```powershell
rtk proxy python -c "import subprocess; raise SystemExit(subprocess.run(['src/KageDesktopTool/bin/Release/net10.0-windows/KageDesktopTool.exe','--content-input-check','--menu-latency-only']).returncode)"
```

同一正式 Runtime 和随机内容夹具，修复前鼠标停留后右键到菜单可见 **4475 ms**，750 ms 要求准确失败；接入优化后 **444 ms**，包含实际鼠标按下、释放和检查等待。

## 正式策略与边界

- 鼠标停留或选择集合稳定 350 ms 后，在后台提前构建一份当前真实选择的完整原生菜单；不会提前执行命令或显示窗口。
- 提前构建使用独立 STA，与直接右键的 STA 分开。另一图标的慢构建不会排在当前未命中缓存的右键前面。
- 使用实际路径集合、Shift、文件创建／修改时间、大小、属性及剪贴板序号核对；系统偏好改变、目标改变、离开内容区域及 Folder 销毁也失效。准备期间发生变化，显示前核对后重新构建。
- 结果最多保留完成后的 5 秒，超时在所属 STA 释放 HMENU、COM 及 PIDL；只使用一次，已显示的菜单不再复用。动态状态不能无限缓存。
- 仍使用原始 `IContextMenu2/3` 消息转发、系统命令 ID 和 `InvokeCommand`；保留单项／多项、Shift 扩展、重命名业务入口、真实取消与操作后刷新。

这项优化把准备工作提前，**不会使 Shell 自身的 3–4 秒构建变快**。直接右键尚未准备的项目、状态变化或缓存过期后仍可能等待；不能把这类路径标为瞬时响应。没有裁剪原生菜单、禁用用户扩展或修改全系统菜单设置。

## 验收进度

已通过 Release 构建、真实鼠标停留后的延迟检查、原地位置／准备期间 Esc／多选保持／无红叉检查，以及完整内容输入回归。失效检查通过：文件变化后命令构建 3074 ms，过期后 3089 ms，确认重新准备。实际 MSI Afterburner 只读诊断直接准备 3702 ms，提前准备后 10 ms，WPF 最大停顿 47 ms；准备期间取消诊断也退出 0。

完整业务回归、双轴审查及最终完整包验收继续执行，完成后填写最终结果。

证据位于 `.scratch/desktop-folder/verification/`；隔离参数和完整菜单复用探针位于 `.scratch/native-menu-capability/classic-latency/`，仅诊断，不属于发布包。
