# Issue tracker：本地 Markdown

本仓库的 issue 和 spec 使用 `.scratch/` 下的 Markdown 文件管理。

## 约定

- 每个 feature 使用一个目录：`.scratch/<feature-slug>/`
- spec 文件为 `.scratch/<feature-slug>/spec.md`
- 每个实现 issue 使用单独文件，路径为 `.scratch/<feature-slug>/issues/<NN>-<slug>.md`。编号从 `01` 开始，不要把多个 ticket 合并到一个文件。
- 每个 issue 文件顶部附近用 `Status:` 行记录状态。若有其他技能定义了状态词汇，应遵循对应的配置。
- 评论和讨论记录追加到文件末尾的 `## Comments` 标题下。

## 发布到 issue tracker

新建 `.scratch/<feature-slug>/` 下的文件；如目录不存在，先创建目录。

## 获取相关 ticket

读取用户提供的文件路径或 issue 编号对应的文件。

## Wayfinder 文件约定

- **Map**：`.scratch/<effort>/map.md`，正文记录 Notes、Decisions-so-far 和 Fog。
- **子 ticket**：`.scratch/<effort>/issues/NN-<slug>.md`，编号从 `01` 开始，问题写在正文中；用 `Type:` 行记录 `research`、`prototype`、`grilling` 或 `task`，用 `Status:` 行记录 `claimed` 或 `resolved`。
- **阻塞关系**：用文件顶部附近的 `Blocked by: NN, NN` 行记录。列出的文件全部标记为 `resolved` 后，该 ticket 才算解除阻塞。
- **可处理队列**：扫描 `.scratch/<effort>/issues/`，按编号优先选择未关闭、无阻塞且未认领的文件。
- **认领**：先把 `Status:` 改为 `claimed` 并保存，再开始工作。
- **完成**：在 `## Answer` 标题下追加答案，将 `Status:` 改为 `resolved`，然后把简要结论及上下文指针追加到 `map.md` 的 Decisions-so-far 部分。
