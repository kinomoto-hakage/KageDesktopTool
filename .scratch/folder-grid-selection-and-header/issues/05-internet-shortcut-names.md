# InternetShortcut 显示名称与改名保留图标

Type: task
Status: resolved
Blocked by: None
Created: 2026-10-11

## 用户反馈与交付范围

Wallpaper Engine 的 `.url` 移入后标签显示后缀；删除磁盘后缀会破坏 Windows 类型识别并显示默认图标。

- Folder 内 `.url` 与 `.lnk` 快捷方式隐藏标签后缀，普通文件与同后缀目录保持原名称。
- 磁盘实际扩展名与链接内容保留，图标沿用实际快捷方式路径和显式图标资源。
- Folder 的改名窗口编辑显示名称，提交时保留快捷方式扩展名，避免再次误删类型。

## 验收

- [x] 实际移入 `.url` 后显示无后缀名称，磁盘仍为 `.url`，字节与图标正确。
- [x] 真实改名输入、保存及后续图标读取保持扩展名和链接内容。
- [x] 大小写后缀、普通文件、同后缀目录与既有 `.lnk` 显示回归通过。
- [x] 用户实际 Wallpaper Engine 图标只读核对通过。

## Answer

`.url` 与 `.lnk` 共享显示后缀隐藏规则，改名窗口仅编辑快捷方式显示名称并保留磁盘扩展名；不修改 URL 链接内容或图标资源。真实移入／改名和图标字节检查通过，实际 Wallpaper Engine 蓝色图案在四档尺寸下只读验收通过。

记录见 [人工反馈验收](../../../docs/verification/grid-04-style-layout-and-shortcuts.md)。用户当前实际文件仍为 `.url`，本次没有修改用户内容文件。

## Comments

- 2026-10-11：用户补充反馈直接授权修复；现有显示规则仅隐藏 `.lnk`，现有图标入口已支持 `.url` 显式资源。本机实际 Wallpaper Engine 文件仍保留 `.url`，无需改动用户文件。
- 2026-10-11：实现、真实输入／文件结果、图标资源及业务回归完成，标记 resolved。
