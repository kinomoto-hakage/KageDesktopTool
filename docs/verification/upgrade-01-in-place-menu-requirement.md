# 任务 01：原地系统菜单要求与可行性核验

日期：2026-10-04。本文件保留现代菜单可行性核验的历史记录；用户已接受原地传统菜单，最新修复见 [原地传统菜单验收](upgrade-01-in-place-classic-menu.md)。

## 用户澄清与原方案偏差

用户要求在 Folder 的非图标区域右键时保持现有 Folder 管理菜单；图标右键则在原点击位置展示真实文件选择的 Windows 原生菜单。Windows 11 要跟随系统当前设置使用现代或传统菜单，不允许先打开资源管理器再展示菜单。

上一轮把对 Explorer 展示方式的回复当作最终交互要求，导致交付的图标菜单仍通过 `SHOpenFolderAndSelectItems` 打开内容目录。该方案不满足本次澄清。此前“现代系统窗口出现”的检查只证明菜单来源，无法证明展示位置与窗口副作用正确。

## 准确失败的反馈检查

```powershell
rtk proxy python -c "import subprocess; raise SystemExit(subprocess.run(['src/KageDesktopTool/bin/Release/net10.0-windows/KageDesktopTool.exe','--content-input-check','--menu-placement-only']).returncode)"
```

检查通过正式 Runtime 创建随机临时 Folder 和真实快捷方式，使用实际系统鼠标右键输入，等待系统菜单；然后检查对应实际内容目录没有在 Explorer 中打开，并检查菜单距右键位置不超过 48 个物理像素，允许屏幕边缘避让。取消菜单，并仅关闭本次随机夹具对应的 Explorer 窗口，不执行文件菜单命令。

现有实现实测退出 1：`真实鼠标右键图标后出现系统菜单` 通过，`图标菜单直接在 Folder 中展示，不打开或导航资源管理器到内容目录` 失败。日志位于忽略目录 `.scratch/desktop-folder/verification/content-menu-placement-session.txt`。本检查针对原地与额外窗口要求；现代／传统跟随设置还需要单独验证，不能因本检查将来通过就宣称整项完成。

## 本机系统组件核验

公开 `IContextMenu` 路径能获取真实对象的传统菜单；上一轮公开 `IExplorerBrowser` 嵌入视图的真实输入探针也得到传统 `#32768`，记录保留于 `.scratch/desktop-folder/verification/native-browser-menu-probe.txt`。微软提供的 `IExplorerCommand` 文档描述的是扩展系统菜单，不能据此认定可在独立 WPF 进程展示完整现代菜单。

本次另用随机临时文本文件进行内部组件实验，存放于明确的忽略调试目录 `.scratch/native-menu-capability/`。不注册组件、不更改系统菜单设置、不注入或修改 Explorer 进程。

- 本机注册的 `File Explorer Context Menu` 位于 `Windows.UI.FileExplorer.dll`，文件版本 `10.0.10011.16384`。
- 创建组件的 COM 对象成功；查询公开 `IContextMenu`／2／3、`IExplorerCommand`、`IObjectWithSite` 与 `IShellExtInit` 均返回 `E_NOINTERFACE`。
- 使用与本机 DLL 精确匹配的微软公开调试符号，找到内部 `IContextMenuPresenter` 构型；查询该接口、`Initialize` 与 `PrepareForContextMenu` 成功。
- 两种初始化标志的独立进程探针均没有生成可见现代菜单；`IsContextMenuOpen` 为 0。接口成功返回不能替代可见菜单验收。
- 本机函数中，现代 XAML 菜单初始化会先调用 `IsProcessAnExplorer`。这是对当前二进制的观察，不能推断所有 Windows 11 版本都具有相同内部构型，也不能视为已获得受支持的第三方调用入口。
- 精确核对本机 Shell 服务标识后，桌面的现代菜单服务返回 `REGDB_IIDNOTREG`，其内部接口不能直接跨进程取回。已有 `IContextMenuSite` 仍可取得，实际跨进程调用没有显示可见菜单；公开文档已注明该展示方法从 Windows Server 2003 起不再提供可用保证。
- 一次性进程中另尝试仅修改自身加载副本的宿主检查入口，配合本地隐藏 Shell 视图、实际单文件选择、命令回调、系统菜单配置及真实命令范围。XAML 与扩展工厂初始化成功，等待 10 秒仍只得到 `0,0,0,0` 的空宿主窗口，没有菜单命令。该实验不是产品实现，不修改系统 DLL 文件或 Explorer 进程。一次查询菜单就绪状态使隔离进程访问冲突，其遗留的唯一随机文本夹具已核对路径和内容后清理。

微软符号源：`https://msdl.microsoft.com/download/symbols/windows.ui.fileexplorer.pdb/12675DEE5B620E857BA4B90DF80828FD1/windows.ui.fileexplorer.pdb`。符号、隔离实验代码和反汇编仅用于此次核验，没有加入正式产品或发布包。

## 当前结论与未完成项

当前已验证的公开方式可原地展示原生传统菜单；尚未验证出同时满足“原地展示”与“跟随系统设置的 Windows 11 现代菜单”的可用实现。不能将打开 Explorer、固定传统菜单或自绘现代外观标为满足完整要求。

本轮仅加入准确失败的输入检查并更正验收状态，产品菜单路径尚未替换，没有发布新包。继续实现前需要验证实际可用的现代菜单宿主方案；任何需求降级必须由用户决定。

用户已明确选择保持现代菜单要求并继续验证，不接受先交付固定传统菜单。

## 已构建但尚未获准运行的桌面桥接实验

在忽略的调试目录构建了 `broker.cpp` 原生 COM DLL 与 `BrokerProof.cs` 客户端。桥接采用唯一实验 CLSID `f7f764a8-4540-4574-84e9-0138db3702a8`，拟在当前用户范围临时注册，并由已有 Explorer 桌面服务加载组件。DLL 只记录执行进程及线程，不展示菜单、不执行任何文件命令；诊断输出限定在 DLL 所在目录且文件名必须属于 `broker-proof-` 命名空间。客户端结束清理本次注册，拒绝覆盖已有 CLSID。

编译器从 Zig 官方站点下载，只解压在工作区；版本 `0.17.0`，归档 SHA-256 `b5663f69581dcf391293fbf16c06cb80d81d806545ce618b4d0bab7f0eb8c428` 与官方索引匹配。没有安装到系统或修改 PATH。该编译器仅用于验证，不作为产品依赖。

实际 `--broker-proof` 运行被自动审批拒绝，尚未执行、尚未创建注册项。拒绝理由：临时注册 COM 类并让 Explorer 加载本地原生 DLL 会跨进程影响桌面 Shell，即使计划清理，崩溃或加载失败仍可能留下注册项或扰乱 Explorer；用户未明确授权此类验证。已只读核验实验 CLSID 不存在。

继续此路线前需要用户明确批准这项具体实验。批准只解决是否允许验证的问题，并不代表现代菜单功能已完成；仍必须验证真正可见的原生命令、目标选择、原地位置、系统设置及生命周期。

较安全的替代验证 `--broker-self-check` 已通过：DLL 只在探针自身进程加载，不注册组件或调用 Explorer；核对 PID、正常反序列化、拒绝构建菜单及文件命令、拒绝目录穿越／奇数字节／超界长度，并在释放全部 COM 引用后允许模块卸载。两项目及桥接 DLL 编译无警告、无错误。该通过不能替代 Explorer 桥接或现代菜单验收。

### Standards

固定点 `1951fc1ee10159ecf35d0ead81b8a22421cbaeb4`，检查点 `ae6c570`；另只读审查待审批的实验源码。规范轴未发现明确规范违例或可行动 heuristic；默认接口查询与桥接注册分支的注释歧义已澄清，剩余发现 0。

### Spec

初审指出 COM 释放异常可能跳过注册清理、检查已有注册只覆盖 HKCU。已修正为 HKCR 合并视图及 HKCU 初查，逐项记录 COM 释放异常，注册删除与原始接口表释放采用独立 `finally`；仍有 COM 引用时保留原始表至进程退出，避免提前释放。规格轴复核剩余发现 0。审查结论仅涉及检查点状态与诊断实验范围，完整原地现代菜单仍未完成。

依据：[IContextMenu](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-icontextmenu)、[微软 Windows 11 菜单扩展说明](https://blogs.windows.com/windowsdeveloper/2021/07/19/extending-the-context-menu-and-share-dialog-in-windows-11/)、[IContextMenuSite](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-icontextmenusite)。可见行为结论来自本机隔离实验，公开资料并未保证独立进程可以展示完整现代菜单。
