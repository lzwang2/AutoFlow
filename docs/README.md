# AutoFlow 文档索引

## 开发流程

从 2026-11-14 起，新增功能和影响现有行为的改动采用文档先行：

1. 调查当前代码和资源，编写或更新功能规范，明确需求、边界、交互与验收标准。
2. 编写实施计划，明确模块职责、实施顺序、验证方式及风险。
3. 与需求方确认设计后再实现；实现中若改变已确认行为，先同步规范。
4. 按验收标准验证，更新文档状态，记录实际结果与未完成项。

小型修复可直接更新相关文档中的行为和验证记录，不要求单独建立一套 Spec / Plan。文档整理不表示历史功能已完成，也不追溯认定历史开发遵循此流程。

规范定义“做什么、做到什么程度”，计划定义“如何落地”。同一功能的计划与规范冲突时，以规范为准；发现代码偏离规范时，应记录差异并确认处理方式。

## 当前功能设计

| 文档 | 用途 | 状态 |
|---|---|---|
| [游戏专用界面规范](features/game-workspaces/Spec.md) | 左侧导航、Last Cloudia 完整功能入口、运行与编辑器衔接 | 已实现，基础验证通过，实机待验收 |
| [游戏专用界面实施计划](features/game-workspaces/Plan.md) | 分阶段改造及验证 | 阶段 1–5 已实现，阶段 6 实机待验收 |
| [Last Cloudia 周回活动迁移规范](features/week-round/Spec.md) | `WeekRound.ahk`、单人核心、共享条件/动作和四次有限调用语义 | 已实现，实机待验收 |
| [Last Cloudia 周回活动实施计划](features/week-round/Plan.md) | 多功能模块、流程技能、单人核心、资源迁移和验证阶段 | 自动化完成，实机待验收 |

## 架构及既有设计资料

| 文档 | 用途 | 状态说明 |
|---|---|---|
| [技能系统架构决策](architecture/Skills_Architecture_Decision.md) | 规则脚本与 C# 技能的职责划分 | 保留原文；包含历史规划，不能据此认定全部已实现 |
| [脚本复用规范](features/script-reuse/Script_Reuse_Spec.md) | 共享条件、动作和脚本模型 | 保留原有版本及状态，尚未逐项核对实现 |
| [脚本复用实施计划](features/script-reuse/Script_Reuse_Plan.md) | 脚本复用改造的阶段安排 | 历史计划，尚未逐项核对完成情况 |

## 测试与历史资料

- [游戏专用界面首版验证记录](testing/GameWorkspaces_Validation.md)：测试结果、布局预览与实机待验收项。

- [MultiPlayerGame 测试说明](testing/MultiPlayerGame_测试说明.md)：历史手工测试资料，部分路径和模型说明已过时。
- [规则模板资源清单](archive/RuleTemplates_Plan.md)：AHK 迁移时的资源规划，归档保留。
- [规则模板目录说明](../RuleTemplates/README.md)：留在资源目录便于就近查看，包含历史格式示例。

## 目录约定

- `architecture/`：跨功能架构决策。
- `features/<feature>/`：同一功能的规范与实施计划。
- `testing/`：测试操作说明与验证记录。
- `archive/`：历史规划和参考资料。

项目根目录保留用户入口 `README.md`；资源目录的 README 就近保留。现有历史文档迁入时保留文件名，新增功能使用 `Spec.md` 和 `Plan.md`。文档中的项目路径默认相对仓库根目录，Markdown 链接相对当前文档。

## 本次整理记录（2026-11-14）

| 原位置 | 新位置 |
|---|---|
| `Skills_Architecture_Decision.md` | `docs/architecture/Skills_Architecture_Decision.md` |
| `Script_Reuse_Spec.md` | `docs/features/script-reuse/Script_Reuse_Spec.md` |
| `Script_Reuse_Plan.md` | `docs/features/script-reuse/Script_Reuse_Plan.md` |
| `RuleTemplates_Plan.md` | `docs/archive/RuleTemplates_Plan.md` |
| `TestScripts/MultiPlayerGame_测试说明.md` | `docs/testing/MultiPlayerGame_测试说明.md` |

此次仅整理和编写文档，未移动脚本、图片或模板，也未修改应用功能。
