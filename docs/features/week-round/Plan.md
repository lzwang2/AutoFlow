# Last Cloudia 周回活动实施计划

- 日期：2026-11-22
- 状态：实现与自动化验证完成；真实游戏验收待完成
- 规范来源：[Spec.md](Spec.md)
- 范围：LastCloudia 多功能模块、WeekRound 流程技能、SinglePlayerCore 迁移、资源与自动化验证

## 1. 实施原则

- `WeekRound.json` 只保存一个 `CallSkill(LastCloudia.WeekRound)`；本轮不修改脚本模型、不新增 `Repeat`。
- `LastCloudia.WeekRound` 在 C# 中固定顺序调用 `LastCloudia.SinglePlayerCore` 四次，每次使用 `again=true`、`last_step=1`。
- 单人核心只迁移来源 AHK 的七个显式状态分支和未知状态兜底；不并入多人核心的网络错误等额外分支。
- 已有条件、动作块和 `LastCloudiaCommon` 公共原语必须复用；新增资源进入现有 LastCloudia 模板库。
- 抓帧失败不得进入未知状态点击，并以可取消延迟避免忙循环。
- 自动化验证与真实游戏验证分开记录；没有真实游戏环境时不得把构建通过写成实机通过。

## 2. 阶段安排

| 阶段 | 工作与交付物 | 验证 |
|---|---|---|
| 1. 模块与任务模型 | 将 LastCloudia 专用页从单一多人脚本改为有序模块集合；模块保存稳定 ID、名称、说明、任务类型、路径、加载状态和规则/线性文档；默认选择多人游戏 | 两个模块可独立加载；一个模块失败不影响另一个；选择变更刷新统一入口状态 |
| 2. 预置脚本与技能注册 | 新增 `WeekRound.json`、`LastCloudia.WeekRound` 和 `LastCloudia.SinglePlayerCore` 注册；WeekRound 技能固定调用单人核心四次并记录 1/4–4/4 进度 | 脚本只有一个 CallSkill；流程技能恰好调用四次；参数、取消和失败按顺序传播 |
| 3. 单人核心状态机 | 实现参数校验、持续抓帧循环、七个分支、未知状态兜底、`last_step` 两个退出点和抓帧失败保护 | 分支优先级、返回条件、超过 1000 次仍运行、取消和抓帧失败均可用无真实游戏测试覆盖 |
| 4. 共享资源与动作 | 新增再战、战斗评价条件及三个确认动作块；复用初次奖励、最后结算、准备/体力/结算公共原语；提取多人和单人共同的结算点击流程 | 资源引用闭包在首次输入前校验；缺失条件、动作块或图片给出可定位错误；单人技能无重复坐标定义 |
| 5. 专用页交互 | 功能卡只负责选择；保留一个统一开始、停止、重新加载和编辑入口；F9 启动选中模块；运行中禁止切换 | 默认选中多人游戏；两种任务按类型分派解释器；打开编辑器得到对应规则/线性副本；运行快照不受后续编辑影响 |
| 6. 构建与验证 | 补充 Core/App 测试，执行全量测试、Debug/Release 构建、输出资源检查，并记录未能自动完成的实机项目 | Spec 验收项逐项映射；发布输出包含脚本、条件、动作和图片；文档记录命令与结果 |

## 3. 代码落点

- `AutoFlow.Core/Skills/LastCloudia/SinglePlayerCore/`：单人核心技能及可测试的状态机运行适配。
- `AutoFlow.Core/Skills/LastCloudia/WeekRound/`：四次调用的流程技能。
- `AutoFlow.Core/Skills/LastCloudia/Common/LastCloudiaCommon.cs`：双方共享的战斗结算、安全点击和最后结算流程。
- `AutoFlow.Core/Skills/SkillRegistry.cs`、`SharedScriptResources.cs`：技能注册及间接资源预检。
- `RuleTemplates/LastCloudia/`：`WeekRound.json`、两个条件、三个动作块和两张图片。
- `AutoFlow.App/ViewModels/LastCloudiaViewModel.cs`、`MainViewModel.Workspaces.cs`、`LastCloudiaView.xaml`：模块选择、任务分派和编辑器衔接。
- `AutoFlow.Core.Tests/`、`AutoFlow.App.Tests/`：流程、状态机、资源和页面行为测试。

## 4. 风险与处理

| 风险 | 处理 |
|---|---|
| 顶层脚本只引用 WeekRound，预检漏掉 SinglePlayerCore 的资源 | WeekRound 通过现有技能资源声明暴露同一完整资源闭包，并对顶层 CallSkill 做预检测试 |
| 代码技能互相查找形成注册时递归 | 注册完成后在执行时解析 SinglePlayerCore；测试构造时允许注入替身，不在注册构造器中访问全局注册表 |
| 抓帧失败落入未知状态造成误点 | 抓帧有效性在状态判断之前单独处理；失败只记录、可取消等待并重试 |
| 公共原语等待与动作块等待叠加 | 每个分支只选择一种动作来源；动作映射测试核对坐标和等待归属 |
| 专用页仍假定规则脚本 | 模块任务使用显式联合类型，执行和编辑入口按类型分派，不做对象强制转换 |
| 线性副本打开后误写预置文件 | 复用现有“副本打开、保存时选新位置”语义，并以 App 测试验证预置文件未被绑定为当前保存目标 |

## 5. 验证命令

```powershell
dotnet test AutoFlow.Core.Tests/AutoFlow.Core.Tests.csproj -c Debug -p:Platform=x64
dotnet test AutoFlow.App.Tests/AutoFlow.App.Tests.csproj -c Debug -p:Platform=x64
dotnet build AutoFlow.sln -c Release -p:Platform=x64
```

另行检查构建输出中的 `RuleTemplates/LastCloudia` 和 `Skills/LastCloudia` 资源闭包。真实游戏验收包括队伍准备、体力恢复、战斗结算、再战确认、两个奖励页、最后结算、未知状态恢复和 F10 中断。

## 6. 完成记录要求

实施完成后在本文追加：实际修改范围、测试与构建结果、资源输出检查、未执行的实机项目及已知限制。若实现需要改变 Spec 的用户可见行为，先更新 Spec，不在 Plan 中静默改变设计。

## 7. 实施完成记录（2026-11-22）

- 阶段 1–5 已完成：LastCloudia 页面支持多人游戏和周回活动两张可选功能卡，统一开始/F9 按选择分派规则或线性解释器；运行中选择锁定。
- `WeekRound.json` 只有一个 `CallSkill(LastCloudia.WeekRound)`；流程技能固定调用四次 `SinglePlayerCore`，并把 1/4–4/4 进度同时写入状态和日志。
- 单人核心七个分支、未知状态、参数校验、无上限循环、两个退出位置、取消传播和抓帧失败等待已实现。
- 新增两项共享条件、三个动作块和两张图片；复用现有初次奖励、最后结算和公共代码原语。旧的 `IsFirstClearReward` 及其无调用方技能私有图片已移除，统一使用共享条件。
- Core 测试 339 项通过，7 项既有迁移测试按原设置跳过；App/WPF 测试 20 项通过。Debug 全方案构建 0 警告、0 错误；Release 构建 0 错误，保留第三方捕获项目已有的 1 个 CS0618 警告。
- Release 输出和仓库外临时发布目录均检查到完整 WeekRound 脚本、条件、动作、模板图片和公共技能图片；1320×820 WPF 实际布局快照已人工检查。
- 详细证据见[周回活动验证记录](../../testing/WeekRound_Validation.md)。真实 Last Cloudia 窗口和全部游戏状态本轮无法自动复现，仍须按记录执行实机验收。
