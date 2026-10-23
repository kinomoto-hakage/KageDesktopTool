# Folder 桌面升级：历史版本与发布来源核查

核查时间：2026-10-23 20:17（Asia/Shanghai）。这是规划研究资料，不是正式升级日志。研究范围为当前 `origin` 对应的第一方仓库 `kinomoto-hakage/KageDesktopTool`；不修改产品、版本号或已发布 Release。

## 结论与证据规则

可以可靠补全三个**确认已发布**的版本：1.1.0、1.1.1、1.1.3。它们都有公开正式 Release、对应 tag、固定源码提交及已上传的 ZIP／SHA256 附件。当前 Release 集合与全部 tag 引用中没有 1.1.2 或更早版本；这只证明本次查询未发现，不能推断它们从未存在。不能按版本号补造缺失版本。[Release 集合第一页](https://api.github.com/repos/kinomoto-hakage/KageDesktopTool/releases?per_page=100&page=1)、[空的第二页](https://api.github.com/repos/kinomoto-hakage/KageDesktopTool/releases?per_page=100&page=2)、[tag 引用集合](https://api.github.com/repos/kinomoto-hakage/KageDesktopTool/git/matching-refs/tags/)。

- **强证据／确认事实**：第一方 Release 的发布状态与时间、GitHub tag 对象、固定提交文件、附件 API 元数据；可用于历史条目。
- **中等证据／提交推断**：固定提交或比较证明源码变化，但不能单凭提交证明正式包已包含、已发布或已实机通过。
- **待核实**：未取得第一方记录、未下载核验的包内字节、尚未推送的本地提交。保留限制，不转成确定的发布事实。

Release API 和 tag 引用会随远端维护变化；本文记录核查时快照。下文源码和比较链接尽量固定到完整 SHA。三个 Release 当时均为 `draft=false`、`prerelease=false`、`immutable=false`；官方发布文案也不是不可变档案。[第一方 Release 集合](https://api.github.com/repos/kinomoto-hakage/KageDesktopTool/releases?per_page=100&page=1)。

## 已发布版本与源码对应

| 版本 | 正式发布时间（上海） | tag 对应提交 | 源码 Version | 状态与证据 |
| --- | --- | --- | --- | --- |
| 1.1.0 | 2026-10-08 21:01:41 | `18bf37fd11bef4735f5ccb8321caf653ec3b269a` | `1.1.0` | 正式发布；强证据：[Release](https://github.com/kinomoto-hakage/KageDesktopTool/releases/tag/v1.1.0)、[固定项目文件](https://github.com/kinomoto-hakage/KageDesktopTool/blob/18bf37fd11bef4735f5ccb8321caf653ec3b269a/src/KageDesktopTool/KageDesktopTool.csproj) |
| 1.1.1 | 2026-10-11 18:50:45 | `ea67728b78726f1481905991849577b196cc050f` | `1.1.1` | 正式发布；强证据：[Release](https://github.com/kinomoto-hakage/KageDesktopTool/releases/tag/v1.1.1)、[固定项目文件](https://github.com/kinomoto-hakage/KageDesktopTool/blob/ea67728b78726f1481905991849577b196cc050f/src/KageDesktopTool/KageDesktopTool.csproj) |
| 1.1.3 | 2026-10-17 16:44:03 | `9426ed204e8844098c71658e779caa07ba49a280` | `1.1.3` | 正式发布；强证据：[Release](https://github.com/kinomoto-hakage/KageDesktopTool/releases/tag/v1.1.3)、[固定项目文件](https://github.com/kinomoto-hakage/KageDesktopTool/blob/9426ed204e8844098c71658e779caa07ba49a280/src/KageDesktopTool/KageDesktopTool.csproj) |

本地三个 tag 的最终 commit 与远端一致。1.1.0 是直接指向 commit 的轻量 tag；1.1.1 的 annotated tag 对象为 `ee217bc9ffb8ed34b497874405c66bc4e5f3b791`，剥离后为 `ea67728…`；1.1.3 的对象为 `a0e2a7b3ba2238561f9cfcad9e5111e6ce25a5f6`，剥离后为 `9426ed2…`。后两者 API 签名验证状态为 `verified=false`／`unsigned`。这里的“确认对应”来自第一方 API 和 SHA 比对，不宣称已通过签名认证。[全部 tag 引用](https://api.github.com/repos/kinomoto-hakage/KageDesktopTool/git/matching-refs/tags/)、[1.1.1 tag 对象](https://api.github.com/repos/kinomoto-hakage/KageDesktopTool/git/tags/ee217bc9ffb8ed34b497874405c66bc4e5f3b791)、[1.1.3 tag 对象](https://api.github.com/repos/kinomoto-hakage/KageDesktopTool/git/tags/a0e2a7b3ba2238561f9cfcad9e5111e6ce25a5f6)。

### 1.1.0：可回填的首个已核实历史条目

官方文案支持记录：多个独立展开／折叠的 Folder，拖动后寻找附近空位；桌面、资源管理器和 Folder 之间批量移动及同名处理；网格／列表、四档图标、多选、框选及自动／手动排序；原生传统文件菜单、颜色与透明度、四套托盘图标；布局偏好持久化、通知、自启、根目录迁移和中断恢复。这是该发布版的功能清单，不能进一步声称每项均首次出现于 1.1.0，也不能虚构前一版本的差异。[1.1.0 Release](https://github.com/kinomoto-hakage/KageDesktopTool/releases/tag/v1.1.0)。

Release 的 `target_commitish` 直接是 `18bf37f…`，正文也指定该完整构建来源；源码 Version 相符。提交时间为 10 月 7 日，正式发布为 10 月 8 日；升级日志应使用正式发布时间，不能把 commit 日期当发布日期。发布说明只声称 Windows 11 x64、单屏 150% 验证，多屏／混合 DPI 未验证。[Release API](https://api.github.com/repos/kinomoto-hakage/KageDesktopTool/releases/406832195)、[固定提交](https://github.com/kinomoto-hakage/KageDesktopTool/commit/18bf37fd11bef4735f5ccb8321caf653ec3b269a)。

### 1.1.1：展示、选择、四向头部与布局修复

可以回填：四档网格及三行名称省略、选中长名称全文滚动；跨 Folder、桌面和应用切换的选择清除；四向头部与横／竖名称、按方位显示头部宽高；样式未应用时的周期闪烁及下／右分隔线方向修复；尺寸／展开／方向／样式受阻时保持有效布局，位置拖动释放时才寻找空位；`.url`、`.lnk` 隐藏后缀且改名保留实际扩展名、目标和图标。上述内容同时存在于线上 Release 与该版本固定发布说明，证据强。[1.1.1 Release](https://github.com/kinomoto-hakage/KageDesktopTool/releases/tag/v1.1.1)、[历史发布说明](https://github.com/kinomoto-hakage/KageDesktopTool/blob/ea67728b78726f1481905991849577b196cc050f/docs/release-notes/v1.1.1.md)、[固定版本差异](https://github.com/kinomoto-hakage/KageDesktopTool/compare/18bf37fd11bef4735f5ccb8321caf653ec3b269a...ea67728b78726f1481905991849577b196cc050f)。

官方记录声称 76 项业务回归、发布包与真实输入／窗口重建／启动及人工复查通过；本文只转述历史验收，未重跑。覆盖仍限单屏 150%，未验证实际多屏／混合 DPI。本次用户报告的展开／缩放白底及“毛玻璃背景”文字闪烁不能因旧日志有“闪烁修复”而认定已经修好，两者触发范围和代码阶段不同。[固定发布说明](https://github.com/kinomoto-hakage/KageDesktopTool/blob/ea67728b78726f1481905991849577b196cc050f/docs/release-notes/v1.1.1.md)。

### 1.1.3：逐 Folder 毛玻璃及强度控制

可以回填：逐 Folder 实时毛玻璃开关；0–100% 强度、默认 50%，键盘微调、悬浮说明、恢复默认；与其他样式一起预览／应用／取消及重启恢复；系统透明、高对比度、省电与桌面环境受限时显示原因并保留设置，条件恢复后自动恢复；兼容既有四向头部、文件整理、原生菜单、多选及拖放。[1.1.3 Release](https://github.com/kinomoto-hakage/KageDesktopTool/releases/tag/v1.1.3)、[固定发布说明](https://github.com/kinomoto-hakage/KageDesktopTool/blob/9426ed204e8844098c71658e779caa07ba49a280/docs/release-notes/v1.1.3.md)、[固定版本差异](https://github.com/kinomoto-hakage/KageDesktopTool/compare/ea67728b78726f1481905991849577b196cc050f...9426ed204e8844098c71658e779caa07ba49a280)。

必须保留其验收边界：该正式包通过打包、30 项隔离业务及核心毛玻璃交互／普通启动，但 `style-layout-feedback`、`frosted-four-way` 两项交互检查未通过，完整桌面分组没有全量完成。此前合并前通过记录不能冒充正式包全部复跑通过。其他 Windows、真实多屏、混合 DPI、HDR 及全部第三方壁纸宿主方式未实测。[固定正式包验收](https://github.com/kinomoto-hakage/KageDesktopTool/blob/9426ed204e8844098c71658e779caa07ba49a280/docs/verification/release-1.1.3.md)。

## 附件与一致性核查

| 正式版本 | ZIP 大小（字节） | GitHub 返回的 ZIP SHA256 | ZIP 来源 |
| --- | --- | --- | --- |
| 1.1.0 | 125756743 | `f06a89ad7d58603e8283eaf64169a34412ce5e9d0fdca5dd4389d56ab9eb2eda` | [固定版本下载](https://github.com/kinomoto-hakage/KageDesktopTool/releases/download/v1.1.0/KageDesktopTool-win-x64-1.1.0.zip) |
| 1.1.1 | 129523355 | `bb891cb5110d02442cef061f7c26bfa2c6333f0076e1e21ffd929bcd57617702` | [固定版本下载](https://github.com/kinomoto-hakage/KageDesktopTool/releases/download/v1.1.1/KageDesktopTool-win-x64-1.1.1.zip) |
| 1.1.3 | 130546096 | `98e6f7e17c2e8dc6e590f27a257589691de09a488a393808f0bbf091af980ee7` | [固定版本下载](https://github.com/kinomoto-hakage/KageDesktopTool/releases/download/v1.1.3/KageDesktopTool-win-x64-1.1.3.zip) |

三个版本均恰有 ZIP 与相邻 `.zip.sha256` 两个资产，状态均为 `uploaded`。上表来自 [1.1.0 API](https://api.github.com/repos/kinomoto-hakage/KageDesktopTool/releases/406832195)、[1.1.1 API](https://api.github.com/repos/kinomoto-hakage/KageDesktopTool/releases/409605270)、[1.1.3 API](https://api.github.com/repos/kinomoto-hakage/KageDesktopTool/releases/415682221)。1.1.0 正文 SHA 与 API 一致；1.1.3 固定验收中的大小／SHA 与 API 一致。`.zip.sha256` 自身的 asset digest 是该校验文本文件的摘要，不能当 ZIP 摘要使用。

未重新下载约 126–131 MB 的三个包，也未重算其字节、核验包内 EXE 或重跑桌面检查。因此本次强证据是线上发布元数据和固定历史记录，不能声称本次已独立验证包体与源码逐文件一致；当前没有发现 tag／Release／源码 Version 不一致。[1.1.3 固定包验收](https://github.com/kinomoto-hakage/KageDesktopTool/blob/9426ed204e8844098c71658e779caa07ba49a280/docs/verification/release-1.1.3.md)。

## 本地文件缺失与未发布变更

1. **历史日志可以恢复。** 当前工作区 `docs/release-notes/` 只保留 `v1.1.3.md`，并不代表 1.1.1 从未有日志：其文件仍存在于固定历史提交。1.1.3 验收明确记录清理旧票据、日志、验收副本并保留 Git 历史。1.1.0 可用线上官方 Release 与固定源码补全，不能把当前文档倒填为旧版。[1.1.1 历史日志](https://github.com/kinomoto-hakage/KageDesktopTool/blob/ea67728b78726f1481905991849577b196cc050f/docs/release-notes/v1.1.1.md)、[1.1.3 清理说明](https://github.com/kinomoto-hakage/KageDesktopTool/blob/9426ed204e8844098c71658e779caa07ba49a280/docs/verification/release-1.1.3.md)、[1.1.0 官方日志](https://github.com/kinomoto-hakage/KageDesktopTool/releases/tag/v1.1.0)。
2. **远端 main 不等于 tag。** 核查时远端 main 是 `06ae917790501eb599a9bd8600cfd8853b5f8a77`，其比 1.1.3 tag 多一个文档／忽略规则提交，只改 `.gitignore` 与 `README.md`，没有源码 Version 升级。这不是一个额外正式版本。1.1.1 与 1.1.3 Release 的 `target_commitish=main` 只是创建参数，实际版本固定点应以剥离后的 tag 为准。[远端 main 引用](https://api.github.com/repos/kinomoto-hakage/KageDesktopTool/git/ref/heads/main)、[固定文档提交](https://github.com/kinomoto-hakage/KageDesktopTool/commit/06ae917790501eb599a9bd8600cfd8853b5f8a77)、[固定差异](https://github.com/kinomoto-hakage/KageDesktopTool/compare/9426ed204e8844098c71658e779caa07ba49a280...06ae917790501eb599a9bd8600cfd8853b5f8a77)。
3. **本地新功能尚未证明正式发布。** 主工作区 HEAD 为 `8f389cbc16bfbe49431be14703dfaac06d686f53`（2026-10-20），是远端 main 的后代，比它多 68 个提交；比 1.1.3 tag 多 69 个提交。只读证据命令：`git rev-list --count 06ae917790501eb599a9bd8600cfd8853b5f8a77..8f389cbc16bfbe49431be14703dfaac06d686f53`；`git diff --stat 9426ed204e8844098c71658e779caa07ba49a280 8f389cbc16bfbe49431be14703dfaac06d686f53`。这些本地历史未由本研究推送；不提供冒充在线可访问的固定 URL。发布集合核查只确认上述三个版本，未提供这些后续改动的正式发布证据。[当前官方发布集合](https://api.github.com/repos/kinomoto-hakage/KageDesktopTool/releases?per_page=100&page=1)。
4. **未发布候选应单列。** 本地固定 HEAD 的代码与验收资料包含统一原生宿主／失败恢复／终结、统一有效样式与布局显示、真实输入检查和 Windows 验证恢复工具。可在“未发布”候选区按面向用户的行为再整理；不能将其写进已发布 1.1.3 的增量，也不能只因本地 csproj 仍为 1.1.3 就认定它们在正式 ZIP 内。本地证据：`git show 8f389cbc16bfbe49431be14703dfaac06d686f53:docs/verification/folder-native-host-final.md`、同提交的 `docs/verification/folder-effective-display-final.md`、`docs/verification/desktop-check-gestures-final.md`、`docs/verification/windows-verification-retro.md` 与 `src/KageDesktopTool/KageDesktopTool.csproj`；证据强度为源码／历史验收，中等，不是正式发布证明。

## 交给后续决策的依据

历史升级日志的数据范围可从以上三个已核实正式版本起步，按正式发布时间倒序；本地后续改动另列“未发布”，发布日期留空。先保留版本、日期、用户可感知变化、对应 Release 链接与证据备注；界面入口、日志折叠方式、是否显示完整验收限制及未来维护流程仍由相关 HITL 决策决定，本研究不替代这些选择。

仓库访问入口确认为 [GitHub 远程仓库](https://github.com/kinomoto-hakage/KageDesktopTool)，与 `git remote -v` 的 origin 一致。研究分支以已在线核对的 `06ae917790501eb599a9bd8600cfd8853b5f8a77` 为基点，只增加本文件；不携带本地尚未推送的产品提交、不创建 PR、不合并。

## 核查覆盖与限制

已读取：本地三个 tag、各自项目 Version、Git 历史和发布说明；GitHub Releases 集合第一页与空第二页、全部 tag 引用、两个 annotated tag 对象、远端 main 引用；固定 1.1.1 发布说明与 1.1.3 验收。所有远程来源均通过 GitHub 插件直接读取第一方 GitHub API／文件，没有使用二手文章。

未核实：已删除或从未公开的旧 Release、无 tag 的旧安装包、私有发布渠道、三个下载包的当前字节、尚未推送本地 commit 的在线可访问性。没有历史事实可支持 1.1.2、1.0.x 的正式条目，也未给当前闪烁报告下诊断结论。
