# AutoFlow 技能系统架构决策

> 本文保留早期决策及实现背景。当前设计及增量实现以文末“后续修订”和复用规范 §19 为准；早期“技能独立维护全部资源”已被修订，不表示新方案仍需复制基础能力。

## 背景

AutoFlow 支持两种自动化流程的表达方式：

1. **规则脚本**（Rule Script）：用户在 GUI 中通过拖拽/配置创建的"若→动作"规则循环
2. **硬编码技能**（Hardcoded Skill）：用 C# 实现的复杂状态机流程

本文档记录"为什么需要技能系统"以及"技能与规则模板的关系"。

---

## 问题：复杂流程无法用规则脚本表达

从 Last Cloudia 的 AHK 脚本（`MultiPlayerCoreProcess.ahk`）迁移到 AutoFlow 时，发现其核心流程包含：

1. **跨轮状态记忆**：`emoji_sent` 全局变量控制"表情只发一次"，`in_room_count` 计数器累计等待时间
2. **条件嵌套**：匹配到"GO"图后，再嵌套检测"未准备"图，根据结果走不同分支
3. **外部参数分支**：`is_room_owner` 参数决定走房主逻辑（等 10 秒后点出发）还是非房主逻辑（等 40 轮后撤退）
4. **循环点击**：`MultiClick(5, 300)` 连点 5 次、每次间隔 300ms，跳过多屏结算
5. **随机选择**：从 4 个表情坐标中随机选一个点击
6. **状态重置**：战斗结束/网络错误时调用 `ResetMultiPlayerState()` 重置状态

这些能力 AutoFlow 的规则脚本**不支持**，且扩展它们需要：
- 给规则脚本加变量系统、条件嵌套语法、动作子例程调用、随机选择语法
- 设计对应的 GUI 编辑界面
- 工作量预估 2-3 周，且最终产物（带变量、嵌套、循环的 DSL）复杂到用户不愿在 GUI 里配置

---

## 决策：采用混合架构（方案 B）

### 方案 A：增强规则引擎（放弃）
扩展规则脚本支持变量、嵌套条件、动作调用等能力。

**放弃原因**：
- 工作量巨大，边际收益递减
- 最终会发明一门新的 DSL，学习曲线陡峭
- 用户已经会写 AHK/C#，"低代码"不应该变成"重新发明编程语言"

### 方案 B：混合架构（采纳）
把流程**分层**：

- **底层核心循环**（如 `MultiPlayerCoreProcess`）用 **C# 硬编码**，作为"技能"暴露给规则脚本
- **上层场景脚本**（如"刷本 10 次后退出"）用规则脚本组合

示例：
```json
{
  "Rules": [
    {"Name": "检测到主界面", "Actions": [{"CommandType": "Click", "PositionX": 370, "PositionY": 500}]},
    {"Name": "检测到房间列表", "Actions": [{"CommandType": "CallSkill", "SkillName": "LastCloudia.MultiPlayerCore", "Parameters": {"is_room_owner": true}}]}
  ]
}
```

**优势**：
- **快**：把 AHK 的 200 行 if-else 直接翻译成 C#，一天完成
- **可维护**：核心流程用强类型语言写，IDE 有自动补全、重构、调试
- **灵活**：上层场景仍然用规则脚本组合，保留"低代码"优势

---

## 决策：技能与规则模板独立维护

### 问题：技能资源是依赖现有模板库，还是独立？

`Documents/AutoFlow/RuleTemplates/LastCloudia/` 中已有 11 个规则模板（Phase 1 生成的单规则 JSON），每个包含一个"若→单次点击"。问：`MultiPlayerCoreSkill` 是否复用这些模板？

### 选项 1：依赖规则模板（放弃）
`MultiPlayerCoreSkill.cs` 运行时加载 `Documents/AutoFlow/RuleTemplates/LastCloudia/*.rules.json`，提取坐标和图片路径。

**放弃原因**：
- **语义混乱**：规则模板的本意是"用户可导入到自己规则脚本的单规则"，而不是"内置技能的配置文件"
- **依赖脆弱**：用户删除/移动模板文件会导致技能挂掉
- **行为不可控**：用户修改模板的坐标/阈值会意外改变技能行为
- **代码复杂**：需要处理模板加载、解析、缺失的错误路径

### 选项 2：独立维护（采纳）
技能的坐标、图片资源独立维护在 `AutoFlow.Core/Skills/` 中。

**优势**：
- **职责清晰**：
  - 规则模板 = 用户可导入/编辑的简单规则（低代码，GUI 配置）
  - 技能 = 内置的复杂状态机流程（硬编码，C# 实现）
- **鲁棒性**：不受用户文件操作影响
- **代码简单**：坐标和逻辑直接硬编码，不需要运行时解析 JSON

**规则模板的新定位**（选项 2 下）：
- **学习案例**：用户打开看"规则是这么写的"
- **快速原型**：单独导入一个模板测试某个弹窗处理
- **自定义扩展**：在技能外面再包一层逻辑（如"刷 10 次后退出"）

---

## 实现方案

### AHK 代码的分层结构

原始 AHK 项目采用三层架构：

```
utils/
  Tools.ahk                      # 底层：通用工具函数
    - IsImageMatch()             # 模板匹配
    - MultiClick()               # 连点
    - LogMessage()               # 日志

  CommonActions.ahk              # 中间层：游戏通用的检测和动作
    - IsConnectionFail()         # 检测网络错误
    - IsBattleEnd()              # 检测战斗结算
    - ClickReady()               # 点击准备按钮
    - ClickAbandonTask()         # 点击放弃任务

  multi_player_core/
    MultiPlayerCoreProcess.ahk   # 上层：多人游戏核心流程
      - 调用 CommonActions 的检测和动作方法
      - 实现完整状态机（emoji_sent, in_room_count）

multi_player_game/
  MultiPlayerGame.ahk            # 顶层：具体场景（找房间 → 调用核心流程）
```

**复用关系**：
- `MultiPlayerGame` 调用 `MultiPlayerCoreProcess()`
- `MultiPlayerCoreProcess` 调用 `CommonActions` 的 `ClickReady()` / `IsConnectionFail()` 等
- `CommonActions` 调用 `Tools` 的 `IsImageMatch()` / `MultiClick()`

---

### C# 实现的分层结构

AutoFlow 已有完善的底层服务（`ScriptExecutionContext`），技能代码直接复用，不重新实现：

```
AutoFlow.Core/Scripting/
  ScriptExecutionContext.cs      # ✅ 底层服务（对应 Tools.ahk）
    - TemplateMatch.TryFindTemplate()  # 模板匹配
    - Input.ClickAt() / Click()        # 点击
    - CoordinateTransform              # 坐标转换
    - CaptureFrame()                   # 截图
    - Log()                            # 日志

AutoFlow.Core/Skills/LastCloudia/
  Common/
    LastCloudiaCommon.cs         # ⚠️ 中间层（对应 CommonActions.ahk）
      - IsConnectionFail()       # 语义检测：调用 context.TemplateMatch
      - ClickReady()             # 语义动作：调用 context.Input
      - MultiClickAsync()        # 封装连点逻辑
    images/                      # 共享图片资源
      connection_fail_area.png
      battle_end_area.png
      ...

  MultiPlayerCore/
    MultiPlayerCoreSkill.cs      # ⚠️ 上层（对应 MultiPlayerCoreProcess.ahk）
      - 实现完整状态机
      - 调用 LastCloudiaCommon 的方法
    MultiPlayerCoreState.cs      # 状态变量封装
    images/                      # 技能特有图片
      prepare_page.png
      emoji.png
      go.png
      ...
```

**关键设计原则**：
1. **底层复用**：技能代码**不重新实现**模板匹配/点击/坐标转换，直接调用 `ScriptExecutionContext` 提供的服务
2. **语义封装**：`LastCloudiaCommon` 把"硬编码的坐标和区域"封装成**有语义的方法名**（如 `IsConnectionFail` 比 `TryFindTemplate(frame, "connection_fail_area.png", region={...})` 可读性高）
3. **资源分层**：共享图片放 `Common/images/`，技能特有图片放技能目录的 `images/`

---

### MultiPlayerCoreSkill 调用 Common 层示例

技能的 `ProcessTickAsync` 方法翻译 AHK 的 if-else-if 链，但**不重复实现检测逻辑**，而是调用 `LastCloudiaCommon`：

```csharp
private async Task ProcessTickAsync(
    ScriptExecutionContext context,
    Mat frame,
    bool isRoomOwner,
    CancellationToken cancellationToken)
{
    // 翻译自 AHK line 72-74: prepare_page 分支
    if (IsImageMatch(context, frame, "prepare_page", 300, 850, 430, 880))
    {
        LastCloudiaCommon.ClickReady(context);  // 调用 Common 层
        await Task.Delay(2000, cancellationToken);
        return;
    }

    // 翻译自 AHK line 174-177: connection_fail 分支
    if (LastCloudiaCommon.IsConnectionFail(context, frame))  // 调用 Common 层
    {
        LastCloudiaCommon.ClickConnectionFail(context);      // 调用 Common 层
        _state.Reset();
        return;
    }

    // 翻译自 AHK line 133-138: battle_end 分支
    if (LastCloudiaCommon.IsBattleEnd(context, frame))       // 调用 Common 层
    {
        // 移动到安全位置
        var screenPos = context.ScriptToScreen(708, 500);
        context.Input.MoveTo(screenPos.X, screenPos.Y);

        await Task.Delay(800, cancellationToken);
        await LastCloudiaCommon.MultiClickAsync(context, times: 5, intervalMs: 300, cancellationToken);
        return;
    }

    // ... 其他分支 ...
}
```

**关键点**：
- 技能代码只包含**业务逻辑**（状态机、分支判断、延迟时间）
- 所有检测和动作都**委托给 Common 层或 context**
- 可读性高：`IsConnectionFail` 比 `TryFindTemplate(...)` 语义清晰

---

### 图片资源处理

图片资源按**共享程度**分层存放：

**Common 层图片**（多个技能共享）：
- `Skills/LastCloudia/Common/images/*.png`
- 包括网络错误、战斗结算、准备按钮、放弃任务等**游戏通用元素**

**技能层图片**（技能特有）：
- `Skills/LastCloudia/MultiPlayerCore/images/*.png`
- 包括准备页面、表情界面、GO 按钮等**多人模式特有元素**

在 `AutoFlow.Core.csproj` 中：
```xml
<ItemGroup>
  <Content Include="Skills\LastCloudia\Common\images\*.png" CopyToOutputDirectory="PreserveNewest" />
  <Content Include="Skills\LastCloudia\MultiPlayerCore\images\*.png" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

**运行时路径解析**：
- `LastCloudiaCommon`: 从 `bin/Skills/LastCloudia/Common/images/` 加载
- `MultiPlayerCoreSkill`: 从 `bin/Skills/LastCloudia/MultiPlayerCore/images/` 加载（技能特有图片）和 `../Common/images/` 加载（共享图片，如果需要的话）

---

### 代码复用总结

| 能力 | AHK 实现 | AutoFlow 实现 | 复用方式 |
|------|---------|--------------|---------|
| **模板匹配** | `Tools.ahk: IsImageMatch()` | `ScriptExecutionContext.TemplateMatch` | ✅ 直接调用 context |
| **点击操作** | `Tools.ahk: Click, MouseMove` | `ScriptExecutionContext.Input` | ✅ 直接调用 context |
| **坐标转换** | `PrepareWindow.ahk` | `ScriptExecutionContext.CoordinateTransform` | ✅ 直接调用 context |
| **连点** | `Tools.ahk: MultiClick()` | `LastCloudiaCommon.MultiClickAsync()` | ⚠️ 封装（内部调用 context.Input） |
| **网络错误检测** | `CommonActions.ahk: IsConnectionFail()` | `LastCloudiaCommon.IsConnectionFail()` | ⚠️ 封装（内部调用 context.TemplateMatch） |
| **通用动作** | `CommonActions.ahk: ClickReady()` | `LastCloudiaCommon.ClickReady()` | ⚠️ 封装（内部调用 context.Input） |
| **状态机** | `MultiPlayerCoreProcess.ahk` | `MultiPlayerCoreSkill` | ❌ 业务逻辑，需翻译 |

**设计原则验证**：
- ✅ **不重复造轮子**：底层能力（模板匹配、输入、坐标转换）全部复用 context
- ✅ **语义封装**：Common 层把坐标和区域封装成有意义的方法名
- ✅ **分层清晰**：底层服务 → Common 工具 → 技能业务逻辑

---
```csharp
public enum CommandType
{
    Click,
    WaitImage,
    WaitColor,
    WaitTime,
    Loop,
    Comment,
    CallSkill  // 新增
}

public sealed class Instruction
{
    // ... existing fields ...

    // CallSkill
    public string? SkillName { get; set; }                      // e.g. "LastCloudia.MultiPlayerCore"
    public Dictionary<string, object>? SkillParameters { get; set; }  // e.g. {"is_room_owner": true}
}
```

### 技能接口设计
```csharp
namespace AutoFlow.Core.Skills;

/// <summary>
/// Base interface for all hardcoded automation skills.
/// Skills are complex state machines implemented in C#, callable from rule scripts.
/// </summary>
public interface ISkill
{
    /// <summary>Skill identifier (e.g. "LastCloudia.MultiPlayerCore")</summary>
    string Name { get; }

    /// <summary>Execute the skill's main loop until completion or cancellation</summary>
    Task ExecuteAsync(ScriptExecutionContext context, Dictionary<string, object>? parameters, CancellationToken cancellationToken);
}

/// <summary>
/// Registry for locating skills by name
/// </summary>
public sealed class SkillRegistry
{
    private readonly Dictionary<string, ISkill> _skills = new();

    public void Register(ISkill skill) => _skills[skill.Name] = skill;
    public ISkill? GetSkill(string name) => _skills.GetValueOrDefault(name);
}
```

### 调用流程
```
用户在规则编辑器里添加 CallSkill 指令
  ↓
保存到 RuleScript.json
  ↓
RuleLoopInterpreter 执行到该规则
  ↓
CallSkillHandler 查询 SkillRegistry
  ↓
找到 MultiPlayerCoreSkill 实例
  ↓
调用其 ExecuteAsync，传入参数（is_room_owner 等）
  ↓
技能内部循环（翻译自 AHK 的 if-else 链）
  ↓
完成后返回规则循环
```

---

## 两个系统的对比

| 维度 | 规则脚本 | 硬编码技能 |
|------|---------|-----------|
| **适用场景** | 简单的"若→动作"反应 | 复杂的状态机流程 |
| **实现方式** | JSON 配置 + GUI 编辑 | C# 硬编码 |
| **用户可编辑** | ✅ GUI 拖拽/配置 | ❌ 需改代码重新编译 |
| **能力上限** | 单条件匹配 + 线性动作序列 | 任意复杂逻辑（变量、嵌套、循环） |
| **示例** | "检测到某个弹窗就点确定" | "多人游戏的完整循环（200 行状态机）" |
| **资源位置** | `Documents/AutoFlow/RuleTemplates/` | `AutoFlow.Core/Skills/` |
| **代码复用** | 导入后各自独立 | Common 层多个技能共享 |

**互补关系**：
- 规则脚本适合**组合简单规则**（如"检测主界面 → 点击多人游戏按钮"）
- 技能适合**封装复杂流程**（如"多人游戏核心循环"）
- 两者可以**混合使用**：规则脚本调用 `CallSkill` 指令执行技能

---

### 新增指令类型：CallSkill

```csharp
public enum CommandType
{
    Click,
    WaitImage,
    WaitColor,
    WaitTime,
    Loop,
    CallSkill,  // 新增
    Comment
}

public sealed class Instruction
{
    // ... existing fields ...

    // CallSkill
    public string? SkillName { get; set; }                      // e.g. "LastCloudia.MultiPlayerCore"
    public Dictionary<string, object>? SkillParameters { get; set; }  // e.g. {"is_room_owner": true}
}
```

**CallSkillHandler** 查询 `SkillRegistry`，找到对应技能并调用 `ExecuteAsync`。

---

## 未来扩展

### Phase 1（当前）：基础技能系统
- 实现 `ISkill` 接口、`SkillRegistry`、`CallSkillHandler`
- 翻译 `MultiPlayerCoreProcess.ahk` → `MultiPlayerCoreSkill.cs`

### Phase 2：技能参数化
- GUI 支持编辑 `CallSkill` 指令的 `Parameters`
- 技能暴露参数文档（如"is_room_owner: 是否为房主"）

### Phase 3：技能组合
- 技能可以调用其他技能（如 `MultiPlayerCore` 内部调用 `CommonErrorHandler`）
- 规则脚本可以等待技能返回值（如"刷了几次"）

### Phase 4：第三方技能
- 用户可以写自己的技能 DLL，放到 `Skills/` 目录让 AutoFlow 加载
- 类似 Vim 插件系统

---

## 决策总结

1. **采用混合架构**：规则脚本（低代码）+ 硬编码技能（复杂流程）
2. **技能独立维护**：不依赖规则模板库，资源和代码都在 `AutoFlow.Core/Skills/<GameName>/<SkillName>/`
3. **按游戏分组**：`Skills/LastCloudia/MultiPlayerCore/`，便于同一游戏的多个技能共享资源
4. **规则模板保留**：作为"用户可导入的简单规则库"，与技能系统互补

---

## 相关文件

- 原始 AHK 实现：`D:\docs\github\game-scripts\last_cloudia\ahk\utils\multi_player_core\MultiPlayerCoreProcess.ahk`
- 规则模板（Phase 1 产物）：`Documents\AutoFlow\RuleTemplates\LastCloudia\*.rules.json`
- 技能实现（Phase 2 目标）：`AutoFlow.Core\Skills\LastCloudia\MultiPlayerCore\MultiPlayerCoreSkill.cs`
- 迁移计划：[规则模板资源清单](../archive/RuleTemplates_Plan.md)


## 后续修订：复杂流程独立，基础能力共享（2026-11-15）

原“技能与规则模板独立维护”的决策需要收窄：独立维护适用于复杂流程编排，不再适用于已经在规则模板中存在的基础视觉条件、图片和简单动作。

规则文件保留，由规则解释器加载。`MultiPlayerCoreSkill` 直接按 ID 使用共享条件和动作，不从规则文件中寻找它们，也不依赖规则顺序或兜底规则。它通过本次运行的条件库和动作库快照，复用规则模板的基础能力；规则解释器和技能共用同一个动作序列执行器。

共享动作以现有模板语义为准。例如模板的 `Click` 加 `IntervalToNextInstructionMs = 800` 表示点击后等待 800ms。技能删除原有多余的鼠标移动和点击前等待，不建立额外兼容层。

技能继续独立维护房间状态机、等待计数、随机表情、战斗结算连点和状态重置。这些内容没有对应的简单模板动作，不应为了复用而扩展规则脚本。


### 复用定义澄清

“共享”表示共享文件是参数的唯一来源，不是在技能 C# 中重新写一套等价参数。以初次通关奖励为例：保留现有条件文件，将规则中的 Click 定义移入 `LastCloudia.ConfirmFirstClearReward` 动作块；原子规则通过 `BlockRef` 引用，技能通过同一 ID 调用。代码仅维护 ID 引用和流程，不重复存储该分支的区域、图片、坐标和等待时间。

条件文件定义如何识别画面，动作文件定义点击和等待，规则文件保留条件与动作的关系。技能直接使用前两类文件的库快照，规则解释器通过规则文件引用它们。详细模型及迁移示例以[复用规范 §19](../features/script-reuse/Script_Reuse_Spec.md#19-版本-06-补充结论)为准。本修订优先于本文早期的资源独立维护描述，11 个重复分支均已迁移；多人奖励确认实机通过，其余场景待实机验证。


本轮已实现范围：11 个重复分支通过共享条件和动作 ID 复用，技能使用 `ISkill.SharedResources` 声明依赖供预检；删除无调用者的重复 Common 方法和 11 张相同图片，保留专用流程及其资源。错误确认成功后重置多人状态，取消不视为确认成功。实施及验证证据见[复用计划 §9.8](../features/script-reuse/Script_Reuse_Plan.md#98-其余-10-个分支迁移完成2026-11-15)。
