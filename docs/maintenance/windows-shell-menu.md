# 本机系统菜单恢复

本机曾因 PCAppStoreExt 的现代卸载菜单组件出现快捷方式右键约三秒等待，已通过撤销当前用户的该独立稀疏包注册解决。PCAppStore 主软件与传统菜单扩展保留。这是本机系统修复，不是 KageDesktopTool 的运行依赖。

维护工具为 `tools/windows/Fix-PcAppStoreMenu.ps1`，唯一还原备份位于 `local-backups/windows-shell-latency/final-backup-v2/`，不提交 Git，也不进入发布包。V1.1.3 整理时从 `.scratch/` 完整迁移，迁移前后逐文件 SHA256 一致。

```powershell
powershell -NoProfile -File tools/windows/Fix-PcAppStoreMenu.ps1 -Mode Status
powershell -NoProfile -File tools/windows/Fix-PcAppStoreMenu.ps1 -Mode Restore
powershell -NoProfile -File tools/windows/Fix-PcAppStoreMenu.ps1 -Mode Apply
```

`Status` 只查询，`Restore` 恢复原组件及其设置，`Apply` 重新隔离已确认的故障组件。工具校验包身份、版本、发布者和逐文件摘要；主软件位置改变、备份损坏或版本不一致时停止。恢复须保留原签名 MSIX、`state.json`、`user-data/` 及原 ExternalLocation，不要只保留安装包。持久设置恢复中断后使用 `Restore` 继续。

此次仓库整理只迁移备份并核对摘要，没有执行 `Apply` 或 `Restore`。更早的诊断与真实恢复记录见 Git 历史中的 `docs/verification/windows-lnk-menu-delay.md`。
