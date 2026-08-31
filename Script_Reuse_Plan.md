# AutoFlow 脚本复用与模板实现计划

## 1. 文档状态

- 状态：Implementation Plan
- 适用范围：AutoFlow 脚本复用与模板库落地
- 版本：0.3
- 目标读者：实现开发者、架构维护者、脚本编辑器开发者
- 对应规范：[Script_Reuse_Spec.md](Script_Reuse_Spec.md) v0.5

### 1.1 与 Spec 的分工

本文档**不定义字段，也不定义行为规范**：

- 对象有哪些字段 → 见 Spec §11
- 要满足什么行为 → 见 Spec §5（FR / NFR）
- 什么算做完 → 见 Spec §15（AC）

本文档只定义：实现顺序、模块边界、优先级、每阶段的交付物与自检项。任何一处与 Spec 冲突，以 Spec 为准。

---

## 2. 目标

本文档用于支撑 [Script_Reuse_Spec.md](Script_Reuse_Spec.md) 中已经确立的架构设计，明确实现顺序、模块边界、优先级与验收条件。它不是设计说明，而是工程执行计划。

目标包括：

- 先落地共享对象模型，再改解释器
- 统一视觉判定对象，避免重复定义 `TemplatePath` 与阈值
- 让线性脚本和规则脚本都能引用共享的 `VisualConditionSpec` 和 `ActionBlock`
- 保持两套脚本执行语义不混淆
- 现有 13 份脚本一次性迁移到新格式，验证等价后删除旧格式支持（不做向后兼容，见 Spec FR-11）
- 线性脚本编辑器功能完整保留（目前无存量脚本文件，但功能不裁）

### 2.1 本轮不做

与 Spec §3.3 一致，以下内容不在本轮范围，计划中不安排工作项：

| 项 | 原因 |
|---|---|
| 技能层（`AutoFlow.Core/Skills/`）整合 | 既有决策，见 [Skills_Architecture_Decision.md](Skills_Architecture_Decision.md)。NFR-01 只约束脚本层 |
| 编辑器拖放交互 | 本轮复用通过"选择/引用"完成 |
| `Rule.Priority` 字段 | 列表顺序即优先级，见 Spec §11.4 |
| 重构 `Instruction` 为 `Type + Parameters` | 见 Spec §11.2，改为映射层方案 |
| 颜色条件、复合条件（AND/OR/NOT） | 只留扩展空间 |

---

## 3. 总体原则

### 3.1 先模型，后解释器

必须先定义以下模型（字段见 Spec §11）：

- `VisualConditionSpec`（Spec §11.1）
- `ActionSpec`（Spec §11.2）
- `ActionBlock`（Spec §11.3）
- `Rule`（Spec §11.4）
- `ScriptDocument`（Spec §11.5）

然后再改 UI、模板服务、解释器绑定。

### 3.2 共享对象，不共享执行语义

- 共享：判断对象、动作对象、模板对象、动作片段
- 不共享：线性脚本的顺序语义、规则循环的条件驱动语义

### 3.3 语义对齐

在实现中要坚持以下名称语义：

- `IfImage`：规则循环中的条件节点
- `WaitImage`：线性脚本中的条件型等待动作
- `VisualConditionSpec`：真正的可复用判定对象
- `ActionBlock`：真正的可复用动作片段

关于 `IfImage` 的落地方式：它是**概念名**，描述"规则的条件节点"这一角色。本轮**不新增 `IfImage` 枚举值、不改 `CommandType`**。规则的条件由 `Rule.ConditionRef` 承载，不是由一个叫 `IfImage` 的动作类型承载。文档中出现 `IfImage` 时，指的就是 `Rule.ConditionRef` 这个位置。

### 3.4 每阶段可独立编译运行

任何一个 Phase 结束时，仓库必须处于可编译、现有脚本可运行的状态。不允许出现"改完 Phase 2 才能编译"的中间态。

---

## 4. 实施顺序

### Phase 0：梳理现状与边界

#### 目标

确认现有脚本模型和模板层的职责边界，避免在执行器中提前把对象混合。

#### 工作项

- 确认线性脚本执行器职责（`ScriptInterpreter`）
- 确认规则循环执行器职责（`RuleLoopInterpreter`）
- 确认 `Rule.TemplatePath` + 阈值 + `RegionX1..Y2` 这组内联字段，语义上等价于一个匿名 `VisualConditionSpec`
- 明确 `VisualConditionSpec` 是共享底座，而非脚本类型本身
- 清点需要迁移的磁盘资产（见下表）

#### 需要迁移的资产清点

| 位置 | 数量 | 现状 |
|---|---|---|
| `RuleTemplates/LastCloudia/*.rules.json` | 11 | 内联条件，`FormatVersion = 1` |
| `TestScripts/*.rules.json` | 2 | 内联条件；`test.rules.json` 含机器相关绝对路径 |
| 用户自行另存的脚本（任意目录） | 0（当前） | 通过"打开脚本"对话框可存到任意位置，但目前不存在这类文件 |

迁移工具只处理上表中的 13 份。若日后出现工具覆盖不到的 v1 文件，按 FR-11 会在加载时被明确拒绝并提示迁移方式——这是设计选择，不是缺陷：宁可报错要求用户重新迁移，也不静默按 v2 解析。

注意：`我的文档/AutoFlow/` **不含模板资产**，只存 `window-profiles.json`（`WindowTargetProfileService`）。模板库根实际是 `<AppBase>/RuleTemplates`，且当前不可由用户配置。详见 Spec §6.4 的目录辨析。

#### 顺带修掉的既有缺陷

以下是清点过程中确认存在的问题，与本轮改造直接相关，建议在 Phase 0 或对应 Phase 一并修掉：

1. `RuleTemplateService` 写出两种不同的 `Schema` 值——[RuleTemplateService.cs:40](AutoFlow.Core/Scripting/Rules/RuleTemplateService.cs#L40) 和 [:138](AutoFlow.Core/Scripting/Rules/RuleTemplateService.cs#L138) 写 `https://autoflow.dev/rule-template.schema.json`，[:105](AutoFlow.Core/Scripting/Rules/RuleTemplateService.cs#L105) 写 `autoflow/rule-script`。引入 `FormatVersion = 2` 时必须先统一，否则版本判定不可靠
2. [RuleTemplateService.cs:306-316](AutoFlow.Core/Scripting/Rules/RuleTemplateService.cs#L306-L316) 的 `FilesAreEqual` 只比较文件长度就返回 `true`，注释也承认"只比较大小，不比较内容"。共享图片库会放大这个问题：两张同样大小但内容不同的图会被判定为同一张，静默跳过复制
3. [TestScripts/test.rules.json:12](TestScripts/test.rules.json#L12) 的 `TemplatePath` 是写死到 `bin/x64/Debug/` 的绝对路径，不可分享。改为相对路径 `LastCloudia/images/network_error_area.png`（走第 2 级回退），具体见 Phase 6「路径改写规则」
4. `RuleTemplates/README.md` 已过期：仍描述 `ReferenceWidth`/`ReferenceHeight` 字段名，且列出的部分文件名与实际不符

#### 交付物

- 架构边界清单
- 共享对象命名表
- `IfImage` / `WaitImage` / `VisualConditionSpec` 的术语对齐说明
- 待迁移资产清单
- 上述 4 项缺陷的修复或明确的延后记录

#### 完成记录

Phase 0 已完成，4 项缺陷全部修掉：

1. `Schema` 统一——三处保存入口不再各自硬编码，改为沿用 `RuleScript` 默认值 `autoflow/rule-script`（磁盘上 13 份文件与 `MainViewModel` 的格式嗅探用的都是这个值，另两处是错的）
2. `FilesAreEqual` 改为长度相同时逐块比较内容，附 `ReadFully` 处理短读
3. `TestScripts/test.rules.json` 绝对路径 → `LastCloudia/images/network_error_area.png`
4. `RuleTemplates/README.md` 修正：3 个不存在的文件名替换为实际的 `server_connection_fail_*`，`ReferenceWidth/Height` → `OuterWidth/Height`，并说明两类相对路径的区别

另建立测试项目 `AutoFlow.Core.Tests`（xUnit，走中央包管理），含 `RuleScriptAssetTests` 共 35 个基线测试，固化上述结论作为 Phase 6 迁移等价性的对照基准：模板数量、`Schema` 值、`OuterWidth/Height` 非零、`TemplatePath` 为 `LastCloudia/images/` 前缀且文件真实存在、无绝对路径。仓库根通过向上查找 `AutoFlow.sln` 定位，不硬编码路径。

全解决方案编译通过，35/35 测试通过。

---

### Phase 1：抽离共享条件模型

对应 Spec：FR-01、FR-09、FR-10、AC-01、AC-07、AC-09

#### 目标

把视觉判定从脚本文本中抽离为单一可复用对象。

#### 工作项

1. 新增 `VisualConditionSpec`，字段按 **Spec §11.1**（不在此处复述，避免两份漂移）
2. 支持 JSON 序列化/反序列化
3. 实现参照空间校验：脚本 `OuterWidth/OuterHeight` 与条件 `ReferenceOuterWidth/Height` 不一致时报错或告警（FR-10）
4. 允许 `WaitImage` 和 `Rule.ConditionRef` 都引用同一个条件 `Id`

#### 设计要求

- 视觉条件对象必须可被规则模板和线性脚本共享
- 不允许 `TemplatePath` 直接散落在多个脚本对象中
- **超时不进入条件对象**（FR-09）——`WaitTimeoutMs` 留在调用点
- 引用用 `Id`，不用 `Name`；`Name` 可改而不破坏引用
- 允许后续扩展颜色判断、区域判断等类型

#### 自检项

- 可从 JSON 读取一条图像条件
- 规则脚本和线性脚本都能引用同一条件 ID
- `TemplatePath` 与阈值参数只定义一次
- 同一条件被两处 `WaitImage` 以不同超时引用，无需复制定义
- 参照空间不匹配时能被发现，而不是静默按错坐标匹配

#### 完成记录

Phase 1 已完成，新增 4 个文件于 `AutoFlow.Core/Scripting/Conditions/`：

| 文件 | 职责 |
|---|---|
| `VisualConditionSpec.cs` | 字段按 Spec §11.1；`HasRegion` 精确复用 `Rule.HasRegion` 的四条件逻辑；默认值与 `Rule` 一致（0.85 / 30.0 / -1），否则迁移会静默改行为 |
| `ReferenceSpaceValidator.cs` | FR-10 校验，三态结果 `Ok` / `Unspecified` / `Mismatch`；不做自动缩放，只报错 |
| `TemplatePathResolver.cs` | 从 `RuleScriptService.Load` **原样抽出**的两级回退，供条件库与规则脚本共用 |
| `ConditionLibrary.cs` | 一个条件一个文件（`*.condition.json`）；`Require()` 的错误含脚本名 + 规则名 + 缺失 Id（AC-08） |

`RuleScriptService.Load` 已改为委托 `TemplatePathResolver`，消除两份路径解析逻辑——留着两份正是本轮要消除的那类重复。回退顺序未改：改顺序会静默改变现有脚本解析到哪张图。

**遗留项 1「条件库粒度」已定**：一个条件一个文件。与现有一文件一模板的组织一致，且单个条件改动时 diff 清晰。

测试增至 67 个（新增 32 个），全部通过；解决方案编译无新增警告。

一处值得记的实现细节：`Path.Combine` 会保留输入里的 `/`，所以解析结果混用 `\` 和 `/`。这与改造前一致（现有脚本的 `LastCloudia/images/x.png` 一直如此，Windows 正常识别），因此测试比较 `Path.GetFullPath` 规范化后的路径，不比较原始字符串。

---

### Phase 2：抽离原子动作与动作片段模型

对应 Spec：FR-04、FR-05、AC-02、AC-05

#### 目标

让动作层具备复用能力，而非每个脚本都重复编写相同步骤。

#### 工作项

1. 新增 `ActionSpec`（字段见 Spec §11.2）
2. 新增 `ActionBlock`（字段见 Spec §11.3）
3. **实现 `ActionSpec` ↔ `Instruction` 双向映射**（Spec §11.2）：
   - `Instruction` 保持原样，7 个 handler 与 ViewModel 不动
   - `ActionSpec.Type` 复用现有 `CommandType` 枚举
   - `ActionSpec.Parameters` 键名与 `Instruction` 属性名一一对应
   - 映射失败报明确错误，不静默丢字段
4. 支持动作块在规则动作列表和线性动作列表中引用
5. 允许动作引用共享条件对象（`Parameters` 中带 `conditionRef`）

#### 设计要求

- 原子动作范围见 Spec §9.1
- `WaitImage`、`WaitColor`、`IfImage` 视为条件型使用方式，不得当作纯原子动作
- 动作块是固定步骤序列，本轮不参数化
- 动作块可嵌入规则和线性步骤中

#### 自检项

- 可以从模板库中**选择并插入**一个动作片段（非拖放）
- 规则动作和线性脚本都能使用该动作片段
- 动作片段可以被序列化与恢复
- `Instruction` → `ActionSpec` → `Instruction` 往返无字段丢失

#### 完成记录

Phase 2 已完成，新增 4 个文件于 `AutoFlow.Core/Scripting/Actions/`：

| 文件 | 职责 |
|---|---|
| `ActionSpec.cs` | 单个原子动作的序列化形式；`Parameters` 键名与 `Instruction` 属性名一一对应 |
| `ActionSpecMapper.cs` | `ActionSpec` ↔ `Instruction` 双向映射；每个 `CommandType` 只携带语义相关的字段，防止 `ActionSpec` 退化成另一份字段汤 |
| `ActionBlock.cs` | 有序动作序列；`ConditionRefs` 存 Id 列表而非对象副本（NFR-01），用于导入时前置校验；附带 `ActionBlockRef` 用于在步骤列表中引用块 |
| `ActionBlockLibrary.cs` | 加载 `*.actionblock.json`，展开混有内联动作与块引用的步骤列表为平铺 `Instruction` 序列 |

**映射关键细节**

`ActionSpecMapper` 对每个 `CommandType` 只保留语义有效的字段。例如 `Click` 携带 `PositionX/Y/MouseButton/ClickCount`，但不携带 `TemplatePath` ——虽然 `Instruction` 有这个字段，但对 `Click` 无意义。这样做是刻意的：让 `ActionSpec` 变清爽是引入它的理由，否则它就成了第二个字段汤。

字段映射表集中维护在 `FieldsByType`。测试覆盖两项强制约束：

1. 枚举的每个成员都有映射（新增 `CommandType` 忘了加映射会立即炸）
2. 映射表里的每个字段名在 `Instruction` 上都存在（防重命名漂移）

JSON 往返的类型退化靠 `Coerce` 处理：`int` → `long`、`enum` → `string` 或数字、嵌套 `Dictionary<string, object>` → `JObject`。最后这个一度打算绕开，但 `SkillParameters` 就是那个类型，JSON 往返必定产生 `JObject`，回避不了。现在显式处理，测试覆盖了 `CallSkill` 的 JSON 往返。

**遗留项的隐含决策**

`ActionBlock` 的 `ConditionRefs` 字段类型是 `List<string>`，不是 `List<VisualConditionSpec>`——测试 `ConditionRefs_StoreIdsNotCopies` 明确断言这点。这固化了 Spec 的设计：存副本会把复用退化成拷贝，违背 NFR-01。

测试从 67 → 92（新增 25 个），全部通过。

---

### Phase 3：建立模板库分层

对应 Spec：FR-06、FR-07、§6.4、AC-04、AC-08

#### 目标

将模板服务分层，不再把所有内容塞进一个模板库。

#### 目录结构

按 **Spec §6.4**，不在此处另定一套。

#### 工作项

1. 在 `<Game>/` 下建立 `Rules/`、`Conditions/`、`Actions/`、`Scripts/` 四个平级子目录及扩展名约定（`*.rules.json` / `*.condition.json` / `*.actionblock.json` / `*.json`）
   - `<Game>/images/` **位置不动**，避免改写已有 `TemplatePath` 字符串
   - 现有 `<Game>/*.rules.json` 挪进 `Rules/`，实际挪动动作由 Phase 6 的迁移工具执行；本 Phase 只需让加载与保存逻辑认新路径
   - 需同步改 `MainViewModel` 的模板保存路径（现为 `Path.Combine(GetTemplateDirectory(), SelectedGame, name + ".rules.json")`）与模板枚举逻辑，补上 `Rules` 一层
   - 保留按游戏分目录的维度，`SelectedGame` 相关逻辑不变
2. 新增模板索引：名称、类型、描述、分类、文件路径、`Id`
3. 实现条件 `Id` 解析器：
   - 图片路径沿用现有两级回退策略（[RuleScriptService.cs:26-70](AutoFlow.Core/Scripting/Rules/RuleScriptService.cs#L26-L70)），不另发明，也不改基准——改基准会静默改变现有脚本的解析结果
   - 共享条件的 `TemplatePath` 一律写成 `<Game>/images/<文件名>`，只依赖第 2 级回退，从而不受条件文件移动影响（见 Spec §6.4）。这与现有 11 份模板的写法一致，迁移时可原样搬运
   - 解析失败时报出「哪个脚本的哪条规则引用了哪个不存在的 Id」（AC-08）
   - 保存时若遇绝对路径给出告警
4. 允许条件模板与动作模板在 UI 中独立展示
5. 创建规则模板加载逻辑与线性脚本模板加载逻辑

#### 自检项

- 视觉模板和动作模板可分开浏览
- 规则模板不会和线性脚本模板混淆
- 模板文件可以按对象类型解析
- 缺失条件 `Id` 的错误信息包含脚本名、规则名、缺失 Id

#### 完成记录

工作项 3（条件 `Id` 解析器）在 Phase 1 已随 `ConditionLibrary.Require` 与 `TemplatePathResolver` 落地，本 Phase 只补了其中最后一条「保存时若遇绝对路径给出告警」。工作项 4 的**数据侧**在此完成（索引能按类型筛选），XAML 呈现留给 Phase 8，与「编辑器层落地」放在一起做。

新增 4 个文件于 `AutoFlow.Core/Scripting/`：

| 文件 | 职责 |
|---|---|
| `TemplateLayout.cs` | 目录结构与扩展名的**单一来源**；`TemplateKind` 枚举 + `ClassifyFile` / `GameOf` |
| `TemplateFileNames.cs` | 文件名清洗与 `Id` → 文件名约定，四个写入方共用 |
| `TemplateIndex.cs` | 扫描四类模板产出统一可筛选列表（`TemplateIndexEntry`） |
| `ScriptTemplateLibrary.cs` | 线性脚本模板库，`<Game>/Scripts/*.json` |

**扩展名判定顺序是有坑的**

`.rules.json` 同时以 `.json` 结尾，先判线性脚本就会把 11 份规则模板全部误分类。`ClassifyFile` 因此先判 `.rules.json`；且裸 `*.json` 只在确实位于 `Scripts/` 目录下时才算线性脚本，否则模板树里任何一个杂散 json 都会被收进索引。两条都有测试钉住。

**迁移期间同时扫两个位置**

`TemplateIndex.ScanRules` 与 `RuleTemplateService.ListTemplates` 既扫 `<Game>/Rules/` 也扫 `<Game>/`。Phase 6 的迁移工具执行挪动之前，规则文件还躺在 `<Game>/` 下；只认新路径会让模板库在两步之间显示为空。

**顺带修掉的一处不一致**

`MainViewModel.SaveRuleAsTemplate` 原先自己拼一遍保存路径（`Path.Combine(GetTemplateDirectory(), SelectedGame, name + ".rules.json")`）来做提示。改成 `Rules/` 之后这个拼接就会指向错误位置——提示的路径和实际写入位置不一致。现在 `SaveAsTemplate` 返回真实路径，UI 直接用，这类漂移不会再发生。

`ConditionLibrary.Save` / `ActionBlockLibrary.Save` 也改为返回写入路径，并新增 `SourceFileOf(id)`，为 Phase 8 的「定位到文件」做准备。

**绝对路径：告警但仍写盘**

`ConditionLibrary.Save` 遇到绝对 `TemplatePath` 时输出告警而不是抛异常。拒绝保存会丢掉用户当次编辑，代价比留一个只在本机有效的路径更大——告警足以让问题暴露。

测试从 92 → 135（新增 43 个），全部通过。

---

### Phase 4：改造线性脚本解释器

对应 Spec：FR-02、§12.1、AC-03

#### 目标

让线性脚本支持共享条件和动作块引用，而不改变其顺序执行语义。

#### 工作项

1. 解析 `ActionSpec` / `ActionBlockRef`
2. 支持 `WaitImage(conditionRef, timeoutMs)` 等条件型动作
3. 支持 `ActionBlock` 展开
4. 保持脚本顺序推进逻辑不变
5. **统一区域裁剪语义**：现有 `WaitImageHandler` 对整帧匹配，而规则会裁剪到区域。接入带区域的共享条件后，`WaitImage` 必须遵守条件里的区域定义
6. **加版本闸门**：`ScriptDocumentService` 加载时校验 `FormatVersion == 2`，否则抛出含文件路径与实际版本号的错误（FR-11）。这一项不可省——`FormatVersion` 目前在 [ScriptDocument.cs:11](AutoFlow.Core/Scripting/ScriptDocument.cs#L11) 只写不读，代码里没有任何按版本分支的逻辑，若不加闸门，v1 文件会被静默当作 v2 解析，内联字段被丢弃、区域坐标退化为默认值

#### 设计要求

- 线性脚本仍然是顺序执行
- 运行逻辑依赖抽象对象，而不是 UI 绑定状态
- `conditionRef` 解析失败时给出明确错误
- 超时来自调用点，不来自条件对象

#### 自检项

- 线性脚本能执行共享条件
- 线性脚本能复用动作片段
- 不影响原有顺序执行行为
- 带区域的条件在 `WaitImage` 中确实按区域裁剪匹配

#### 完成记录（已完成）

**新增文件**

| 文件 | 职责 |
|---|---|
| `Scripting/Conditions/VisualConditionEvaluator.cs` | 图像判定的唯一实现，两个解释器共用 |
| `Scripting/ScriptFormatVersion.cs` | 版本闸门，`Current = 2` |
| `Scripting/ScriptStep.cs` | `ActionSpec` / `ActionBlockRef` 二选一的步骤联合体 |
| `Scripting/ScriptStepExpander.cs` | 把 `Steps` 展开成 `Instruction + 已解析条件` |

**改造文件**：`ScriptDocument`（加 `FormatVersion` / `Id` / `Name` / `Steps`）、`ScriptExecutionContext`（加 `CurrentCondition`）、`WaitImageHandler`、`ScriptInterpreter`、`ScriptDocumentService`、`RuleLoopInterpreter`、`MainViewModel`。

**区域语义统一（工作项 5）是本阶段风险最高的一处。** 改造前 `WaitImageHandler` 一律整帧匹配，而 `RuleLoopInterpreter` 会裁剪到区域——同一个共享条件在两个解释器里行为不同。裁剪逻辑原样搬进 `VisualConditionEvaluator`，两边都调它。关键细节：区域匹配后必须把 ROI 偏移加回去（`roiRect.X + m.X`），否则每个带区域的 `ClickAfterMatch` 都会点到窗口左上角附近。内联 `WaitImage` 仍是整帧匹配，因为临时构造的条件对象区域字段保持 -1。

**条件的传递方式**：解析好的条件挂在 `ScriptExecutionContext.CurrentCondition` 上，调 handler 前赋值、`finally` 里清空（残留会静默作用到下一条指令）。没有加到 `Instruction` 上——§11.2 要求该 DTO 不变，且它本来也没有区域字段可放。

**编辑器空白问题就地修掉**：`OpenScript` 原来只读 `document.Instructions`，`Steps` 形态的脚本会显示为空。改成先经 `ScriptStepExpander` 展开。

**决策：规则脚本的版本闸门从 Phase 5 移到 Phase 6。** 已确认现存 13 个脚本全部声明 `"FormatVersion": 1`。若在 Phase 5 就把 `RuleScript.FormatVersion` 默认值提到 2 并加闸门，迁移工具尚未存在，这 13 个脚本会全部加载失败，违反 §3.4「每个阶段结束时脚本可运行」。因此 `ScriptFormatVersion` 按通用组件实现，本阶段只对 `ScriptDocument` 生效（线性脚本零存量数据，加闸门无代价），默认值提升与规则侧闸门与 Phase 6 的迁移改写同批落地。该理由已写成 `RuleScript.FormatVersion` 的文档注释，避免被误读为遗漏。

测试数：135 → 174。

---

### Phase 5：改造规则循环解释器

对应 Spec：FR-03、§12.2、AC-03

#### 目标

让规则循环使用统一的共享判定对象，同时保留条件驱动语义。

#### 工作项

1. 让规则条件使用 `VisualConditionSpec` 引用
2. 把规则条件节点（概念上的 `IfImage`）绑定到共享条件对象——即 `Rule.ConditionRef`，不新增枚举值
3. `Rule` **原地修改**：加 `Id` / `Description` / `ConditionRef`，暂留内联条件字段，待 Phase 6 迁移验证通过后删除（Spec §11.4）
4. 允许响应动作列表中引用 `ActionBlock`
5. 保持"每一帧扫描 + 命中执行"的运行模式
6. ~~加版本闸门~~ → **已移至 Phase 6**。规则侧的 `FormatVersion` 默认值提升与加载校验必须和迁移改写同批落地，否则现存 13 个 v1 脚本会在本阶段结束时全部无法加载（详见 Phase 4 完成记录）

#### 设计要求

- `Rule = ConditionRef + ActionSequence`
- 规则循环仍然按状态判定执行
- **规则求值顺序 = 列表顺序，首个命中即执行，语义不变**（不引入 `Priority`）
- `TriggerOnce` 与无条件 catch-all 规则的现有语义不变。catch-all 由 `ConditionRef` 为空表达，`MultiPlayerGame.rules.json` 依赖它

#### 自检项

- 规则脚本可以从共享条件模板中读取判定对象
- 规则判断不再依赖重复的 `TemplatePath` 复制
- 规则命中后执行对应动作序列
- 列表顺序决定的命中优先级与改造前完全一致

#### 完成记录（已完成）

**`Rule` 原地修改**：加 `Id`、`ConditionRef`、`UsesSharedCondition`、`Steps`；内联条件字段暂留并标注「Phase 6 迁移改写、Phase 7 删除」。

**动作块引用用 `List<ScriptStep>` 而不是并列的 `List<ActionBlockRef>`。** 一开始写成了 `Actions` + `ActionBlockRefs` 两个列表，但那样两者的交错顺序没有定义——是先跑完所有直接动作，还是按各自下标穿插？改用线性侧已有的 `ScriptStep` 联合体，一个列表天然表达唯一顺序，也少一个概念。`Steps` 非空时权威，否则用 `Actions`。

**引用在循环开始前一次性解析**（新增私有 `ResolvedRule` + `Resolve`）。改造前 `IsConditionMet` 每帧为每条规则新建一个 `VisualConditionSpec`；现在每次运行只解析一次。更重要的是缺失的条件或动作块会在第一帧之前就报错，而不是在已经点过东西的运行中途。

**catch-all 语义收敛到 `Condition is null`。** 改造前判据是 `string.IsNullOrWhiteSpace(rule.TemplatePath)`，迁移后引用型规则的 `TemplatePath` 为空，会被误判成兜底规则而永远命中——那会让 `MultiPlayerGame.rules.json` 之外的每个脚本都在第一条规则上卡死。`ResolveCondition` 的优先级是：共享引用 → 内联字段 → null（兜底）。空白 `ConditionRef` 等同于没有引用，否则会拿空串去查库然后报缺失。

**帧调试改用解析后的条件。** `SaveDebugFrame` 原来读 `rule.RegionX1..Y2`，迁移后这些字段为空，区域裁剪图会静默消失——恰好是最需要调试的那些规则。现在改调 `VisualConditionEvaluator.ResolveRegion`，与实际匹配用的是同一份区域计算。

**`CurrentCondition` 契约与线性侧一致**：调 handler 前赋值、`finally` 清空。

`ScriptStepExpander` 提取出 `Expand(steps, scriptName, ownerLabel)` 重载，规则与文档共用同一份展开逻辑，错误信息前缀分别是「规则 #2 [撤退]」和「脚本 'X'」。

测试数：174 → 184，含一条锁定现存 13 个脚本在无 `Id`/`ConditionRef`/`Steps` 字段时照旧可读的回归测试。

---

### Phase 6：一次性迁移

对应 Spec：FR-11、§6.4、AC-06

> 这个 Phase 必须在解释器改造（Phase 4/5）**之后**：迁移工具的输出格式依赖新模型定稿，而验证迁移结果又依赖新解释器能实际跑起来。

#### 目标

把现有 13 份内联条件脚本一次性转换为 `FormatVersion = 2` 的引用式脚本，并证明等价。

#### 待迁移清单

| 文件 | 份数 | 目标位置 |
|---|---|---|
| `RuleTemplates/LastCloudia/*.rules.json` | 11 | `RuleTemplates/LastCloudia/Rules/` |
| `TestScripts/MultiPlayerGame.rules.json` | 1 | 原地（非模板库） |
| `TestScripts/test.rules.json` | 1 | 原地（非模板库） |

线性脚本（`Schema: autoflow/script`）**待迁移份数为 0**——全仓库不存在此类文件。线性侧只有代码改造，没有数据迁移。

`publish/RuleTemplates/` 下的 11 份是构建产物、未纳入版本控制，不迁移，重新发布即覆盖。

#### 工作项

1. 实现迁移工具（一次性命令行工具或 `AutoFlow.Core.Tests` 里的可执行用例即可，不必进主 UI）：
   - 逐条规则抽取内联条件 → 写入 `<Game>/Conditions/*.condition.json`，`Id` 按下方「条件 Id 命名规则」生成
   - 用 `ReferenceOuterWidth/Height` 记录参照空间，取值来自所属脚本的 `OuterWidth/OuterHeight`（FR-10）
   - 规则改写为 `ConditionRef`，`FormatVersion` 置 2
   - 内容相同的条件**合并为一个**条件文件（这才是 NFR-01 的实际收益，需报告合并了多少份）
   - 按下方「路径改写规则」处理 `TemplatePath`
2. 实现结构等价性校验（AC-06 第一条），迁移工具跑完自动执行，13 份全过才算通过
3. 执行迁移并提交
4. 人工运行等价性验证（AC-06 第二条），至少覆盖 `unable_to_join`、`network_error`、`multi_game_refight`
5. **规则侧版本闸门（自 Phase 5 移入）**：把 `RuleScript.FormatVersion` 默认值改为 `ScriptFormatVersion.Current`，并在 `RuleScriptService.Load` 中调 `ScriptFormatVersion.Validate`。**必须与第 3 项同一次提交**——闸门先落地会让 13 个 v1 脚本全部加载失败，迁移先落地则 v1 文件会被静默当作 v2 解析。`ScriptFormatVersion` 组件已在 Phase 4 建好，本项只是接线

#### 条件 `Id` 命名规则

现有规则名是中文（「无法加入房间」「服务器连接失败-撤退并返回」），不适合直接做 `Id`。**取图片文件名转 PascalCase**，前缀游戏名：

```
LastCloudia/images/unable_to_join.png  →  LastCloudia.UnableToJoin
LastCloudia/images/refight_denied.png  →  LastCloudia.RefightDenied
```

两处例外要人工指定，不能机械去后缀：

| 图片 | 机械转换结果 | 应采用 |
|---|---|---|
| `abandon_task_area.png` | `AbandonTaskArea` | `LastCloudia.AbandonTask` |
| `network_error_area.png` | `NetworkErrorArea` | `LastCloudia.NetworkError` |

`_area` 是截图时的区域标注习惯，不属于语义。规则名分别是「放弃任务确认」和「网络错误-发生了网络错误」，`Area` 出现在 `Id` 里会误导。

原规则的中文名迁移到条件的 `Name` 字段（供 UI 显示），`Id` 只用于引用。

`TestScripts/` 两份脚本不进条件库，其条件保持匿名内联抽取到脚本同目录，或直接引用已有的 `LastCloudia.*` 条件——`test.rules.json` 用的就是 `network_error_area.png`，应改为引用 `LastCloudia.NetworkError`，这本身就是一次去重。

#### 路径改写规则

清点确认存在三类 `TemplatePath`，处理方式不同：

| 类型 | 实例 | 处理 |
|---|---|---|
| 第 2 级回退（`<TemplateRoot>` 基准） | `LastCloudia/images/unable_to_join.png`（11 份模板全部如此） | **原样保留**。`images/` 不挪位置，`.rules.json` 挪进 `Rules/` 不影响第 2 级解析 |
| 第 1 级回退（同目录基准） | `images/welcome.png`、`images/no_room.png`（`MultiPlayerGame.rules.json`，图片实际在 `TestScripts/images/`） | **原样保留**。该脚本迁移时原地不动，第 1 级仍然成立 |
| 绝对路径 | `TestScripts/test.rules.json:12` 写死到 `bin/x64/Debug/...` | **改为相对路径** `LastCloudia/images/network_error_area.png`，走第 2 级回退 |
| 空 / null | `MultiPlayerGame.rules.json` 的「其他情况-调用多人核心流程」 | `ConditionRef` 留空，catch-all 语义不变 |

绝对路径那条之所以能直接改：它指向的就是 `RuleTemplates/LastCloudia/images/network_error_area.png` 在构建输出里的副本，与 11 份模板引用的是同一张图。改成相对路径后行为不变，且脚本变得可分享。

迁移工具遇到上述四类之外的路径形态应报错停下，不要猜。

#### 自检项

- 13 份全部迁移为 `FormatVersion = 2`
- 结构等价性校验 13/13 通过
- 条件去重有实际效果（合并份数 > 0）；`test.rules.json` 与 `network_error.rules.json` 应指向同一个 `LastCloudia.NetworkError`
- 全仓库不再有绝对路径 `TemplatePath`
- 生成的条件 `Id` 中不含 `Area` 后缀
- 三份关键脚本人工实跑与迁移前一致
- 迁移前的原始文件保留在 git 历史中可回溯

---

### Phase 7：删除旧格式

对应 Spec：FR-11、NFR-01

> 必须在 Phase 6 的两类等价性验证**都通过之后**执行。这是收尾清理，不可与迁移合并——迁移出问题时需要旧字段仍在以便对照。

#### 工作项

1. 从 `Rule` 删除内联条件字段：`TemplatePath`、`RegionX1..Y2`、`MatchThreshold`、`MeanDiffThreshold`、`HasRegion`
2. 从 `RuleLoopInterpreter.EvaluateRules` 删除内联条件求值分支，只保留 `ConditionRef` 解析与空引用 catch-all 两条路径
3. 清理 `RuleViewModel` / `MainViewModel` / `MainWindow.xaml` 中绑定到已删字段的成员
4. 删除 `publish/RuleTemplates/` 下的旧格式产物，重新发布

#### 自检项

- 解决方案编译无警告残留（无未使用字段、无死代码）
- 全部 AC 复测通过
- 搜索 `TemplatePath` 只在 `VisualConditionSpec` 及其解析器中出现

---

### Phase 8：编辑器层落地

对应 Spec：FR-06、AC-01、AC-02

#### 目标

让用户可以在界面中直接声明共享对象，而不感知底层存储结构。

> **线性脚本编辑器的功能必须完整保留。** 目前磁盘上没有任何线性脚本文件（待迁移份数为 0），但「线性脚本编辑器」Tab（[MainWindow.xaml:159](AutoFlow.App/Views/MainWindow.xaml#L159)）及其新建 / 打开 / 保存 / 运行链路都要继续可用。"没有存量脚本"只意味着无需迁移数据，不意味着可以裁掉功能。

#### 工作项

1. 线性脚本编辑器：
   - 展示步骤列表
   - 支持从动作模板库**选择插入**
   - 支持为条件型动作**选择**共享条件对象
   - 保留现有全部操作：新建、打开、保存、另存、运行、指令增删与上移下移
2. 规则编辑器：
   - 展示条件和动作
   - 支持为规则条件**选择**共享条件对象
   - 支持动作片段引用
3. 模板库 UI：
   - 视觉条件模板
   - 动作模板
   - 规则模板
   - 线性模板
4. 参照空间不匹配时在 UI 上给出可见提示（AC-07）

#### 说明

复用交互一律用「选择 / 引用」（对话框或下拉），**不引入拖放**。现有编辑器是列表 + 增删 + 上移下移按钮，没有拖放基础设施，本轮不新建。

#### 自检项

- 用户能在两种编辑器中复用同一条件对象
- 用户能在两种编辑器中复用同一动作块
- UI 不混淆脚本语义

---

### Phase 9：测试与回归

对应 Spec：NFR-03、全部 AC

#### 前置工作

测试项目已在 Phase 0 建立：`AutoFlow.Core.Tests`（xUnit 2.9.3，依赖走 `Directory.Packages.props` 中央包管理，已加入 `AutoFlow.sln`），现有 `RuleScriptAssetTests` 35 个基线测试。

仍需遵守：对象层测试不得依赖 UI 或真实窗口；需要图像的用例使用固定测试图片；定位仓库资产用向上查找 `AutoFlow.sln` 的方式，不硬编码绝对路径。

#### 测试范围

- JSON 序列化/反序列化
- 条件对象引用解析
- 动作块展开
- 线性脚本执行
- 规则循环执行
- 模板库加载
- 版本闸门与迁移等价性

#### 建议测试项

1. `VisualConditionSpec` 读取和写回
2. `ActionBlock` 作为规则动作执行
3. `ActionBlock` 作为线性脚本步骤执行
4. `WaitImage` 与规则条件引用同一条件对象
5. 缺失条件文件时输出清晰错误
6. **规则求值顺序稳定**：列表顺序决定命中，与改造前一致
7. `Instruction` ↔ `ActionSpec` 往返映射无损
8. **`FormatVersion = 1` 文件被拒绝加载**，错误信息含文件路径与实际版本号（FR-11）
9. **迁移结构等价性**：13 份脚本迁移前后判定内容与动作序列逐项相等（AC-06）——用 git 历史中的 v1 原文件作为对照输入
10. 参照空间不匹配时报错（AC-07）
11. 同一条件以不同超时被引用（AC-09）
12. `ConditionRef` 为空的 catch-all 规则仍然无条件命中

---

## 5. 优先级建议

### P0：必须先做

- `VisualConditionSpec`（Phase 1）
- `ActionSpec` / `ActionBlock` 及其与 `Instruction` 的映射（Phase 2）
- 模板库分层与 `Id` 解析（Phase 3）

### P1：第二批

- 线性脚本解释器改造 + 版本闸门（Phase 4）
- 规则循环解释器改造 + 版本闸门（Phase 5）
- 一次性迁移与等价性验证（Phase 6）

### P2：第三批

- 删除旧格式（Phase 7）
- UI 集成（Phase 8）
- 复杂条件扩展
- 更高级的模板管理能力

> Phase 6 迁移虽属 P1，但**在它通过之前不要开始 Phase 7**。放弃向后兼容意味着旧路径不再是安全网，迁移的正确性完全靠 Phase 6 的等价性验证兜住；旧字段要留到验证通过为止，以便出问题时对照。

---

## 6. 关键依赖顺序

1. 先设计统一对象模型（Phase 1-2）
2. 再设计模板库接口与 `Id` 解析（Phase 3）
3. 再改解释器，同时加版本闸门（Phase 4-5）
4. 再做一次性迁移并验证等价（Phase 6）
5. 验证通过后删除旧格式（Phase 7）
6. 再改编辑器 UI（Phase 8）
7. 最后做回归测试与稳定性修正（Phase 9）

第 1-2 步在前，是为了避免"先改执行器、再拆对象"的典型设计错误。

第 4 步在第 3 步之后，理由有两条：迁移工具的输出格式依赖新模型定稿；验证迁移结果需要新解释器能实际跑起来。

第 5 步单独成步而不与第 4 步合并，是因为迁移出问题时需要旧字段仍在以便对照——这是放弃向后兼容后唯一的回退依据。

---

## 7. 遗留待定项

以下问题 Spec 与本计划都未给出结论，实现到对应 Phase 前需要定：

1. **条件被删除时的引用处理**：级联报错、标记失效，还是禁止删除仍被引用的条件
2. **迁移工具的存放位置**：一次性工具做完即删，还是保留在仓库里备用

已定项（原在此列，结论已并入对应章节）：

- 条件 `Id` 命名规则 → Phase 6「条件 `Id` 命名规则」
- `ScriptDocument` 与 `Rule` 是否原地修改 → Spec §11.4 / §11.5（原地改）
- 条件库粒度 → Phase 1 完成记录（一个条件一个文件）

---

## 8. 最终落地结论

最稳妥的路线不是把两种脚本编辑器合并，而是：

- 让线性脚本的 `WaitImage` 和规则的条件节点共享同一 `VisualConditionSpec`
- 让规则和线性脚本都复用 `ActionBlock`
- 继续保留两种脚本的运行语义
- 用一次性迁移 + 等价性验证替代兼容层，收尾时清除旧格式

这正是 [Script_Reuse_Spec.md](Script_Reuse_Spec.md) 里"统一复用对象、保留语义差异"的核心实现路径。

