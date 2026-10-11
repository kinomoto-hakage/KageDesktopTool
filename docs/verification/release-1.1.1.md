# 1.1.1 发布与仓库清理验收

日期：2026-10-11。用户确认上一轮人工复查通过，并授权清理本仓库、只留下最终版本、升级到 V1.1.1、制作发布包、提交一次并推送 GitHub。

## 清理范围

- 删除开发构建的 `bin/obj`、临时基线副本、临时检查脚本、日志与截图。初始核对的清理目标包含 2347 个文件；运行中的旧程序占用文件在用户确认退出后补清理。
- 删除本地旧版 1.1.0 发布目录、ZIP 和校验文件，以及本次验收产生的构建缓存和临时输出。最终 `releases/` 只保留 1.1.1 完整目录、ZIP 和 `.zip.sha256`。
- 保留正式源码、必要回归源码、维护脚本、已完成票据和验收结论。历史 Git 提交和 GitHub 1.1.0 Release 保留，不改写历史。
- 保留系统菜单修复唯一还原资料 `.scratch/windows-shell-latency/final-backup-v2/`，这是维护脚本的真实恢复依赖。
- 发布及源码运行脚本直接使用 .NET／PowerShell，删除失效的 `rtk` 调用和原型目录说明；发布临时构建和 ZIP 随机解压副本验收后自动清理。
- 发布检查入口加入四向输入、四向重建及人工反馈检查，摘要按项目版本命名。首页和使用说明更新到 1.1.1，保留既有用户说明结构。

## 包与验证

- 包名：`KageDesktopTool-win-x64-1.1.1.zip`。
- Windows x64 自包含、Release，无外部 .NET 运行时依赖，无测试源码／脚本或 PDB 调试符号随包发布。
- EXE 文件版本：`1.1.1.0`；ProductVersion 主版本：`1.1.1`。
- ZIP：129523355 字节。
- 包中 799 个程序文件加 `SHA256SUMS.txt`，合计 800 个文件；ZIP 随机解压到含空格路径后逐文件校验全部一致，包内运行时和正式图标加载成功。
- ZIP SHA256：`BB891CB5110D02442CEF061F7C26BFA2C6333F0076E1E21FFD929BCD57617702`。
- 76 项业务回归全部通过。
- 最终包 `style-layout-feedback`、`four-way-restart` 真实输入／窗口检查通过，覆盖周期预览、四向分隔线、滑块及数值、受阻布局、URL 实际移入／改名与图标。
- 最终 EXE 无检查参数普通启动通过：读取实际根目录和全部 Folder、重复启动转交设置、关闭设置继续运行、真实托盘退出正常。
- 普通启动前后实际工作区与备份 SHA256、内容路径／文件属性和自启注册项保持不变。
- 首次外层 ZIP 调用误把未设置的 `$LASTEXITCODE` 当成失败；ZIP 内部检查已通过，使用正确直接脚本调用复验通过。该误报不属于产品失败。
- 用户此前已经确认人工复查通过；实际硬件覆盖 Windows、单屏 150%。实际多屏、混合 DPI、拔屏、主屏及系统缩放／分辨率切换不作本次已覆盖声明。

验收日志和截图在结论记录后按用户要求清理，复现脚本会重新生成隔离产物。构建与发布均使用真实用户环境；沙箱 GitHub 账号路径导致的无效登录在真实用户环境已核对正常。

## 发布入口

- [GitHub Release v1.1.1](https://github.com/kinomoto-hakage/KageDesktopTool/releases/tag/v1.1.1)
- [完整 ZIP](https://github.com/kinomoto-hakage/KageDesktopTool/releases/download/v1.1.1/KageDesktopTool-win-x64-1.1.1.zip)
- [SHA256](https://github.com/kinomoto-hakage/KageDesktopTool/releases/download/v1.1.1/KageDesktopTool-win-x64-1.1.1.zip.sha256)

版本清理及发布准备通过一次本地提交完成；远端保留 `main`，发布标签固定在该提交。发布附件与本地 ZIP 摘要一致。
