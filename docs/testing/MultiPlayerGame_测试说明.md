# MultiPlayerGame 测试说明

> 历史测试资料：2026-11-14 从 `TestScripts/` 迁入，以下保留原始说明，尚未逐项复核。当前脚本使用 v2 `ConditionRef`，不能再以 `TemplatePath: null` 解释兜底规则；图片按共享条件库解析，不能假定都在脚本旁的 `images/`。脚本尺寸为窗口外框尺寸，客户区以运行时实测为准。旧的目录、时间估计及操作步骤仅作参考。
>
> 当前脚本：[MultiPlayerGame.rules.json](../../RuleTemplates/LastCloudia/Rules/MultiPlayerGame.rules.json)。后续专用面板验收以[游戏专用界面规范](../features/game-workspaces/Spec.md)为准。

## 功能说明

这是从 `D:\docs\github\game-scripts\last_cloudia\ahk\multi_player_game\MultiPlayerGame.ahk` 翻译的完整多人游戏流程。

**逻辑**：
1. 如果检测到房间列表（`welcome.png`）→ 点击第一个房间 → 进入多人核心流程
2. 如果检测到"没有房间"（`no_room.png`）→ 点击"更新列表" → 等待 6 秒
3. 否则（已在房间内/战斗中）→ 调用 `MultiPlayerCoreSkill` 处理准备/战斗/结算/错误

## 前置准备

### 1. 准备图像文件

需要在 `D:\docs\github\AutoFlow\TestScripts\images\` 目录下准备 2 张图片（从 AHK 脚本目录复制）：

```bash
# 从 AHK 脚本目录复制
cp /d/docs/github/game-scripts/last_cloudia/ahk/multi_player_game/welcome.png \
   D:/docs/github/AutoFlow/TestScripts/images/

cp /d/docs/github/game-scripts/last_cloudia/ahk/multi_player_game/no_room.png \
   D:/docs/github/AutoFlow/TestScripts/images/
```

### 2. MultiPlayerCore 技能的图像

技能所需的图像已在编译时自动复制到：
- `AutoFlow.App/bin/Debug/.../Skills/LastCloudia/Common/images/` (10 张)
- `AutoFlow.App/bin/Debug/.../Skills/LastCloudia/MultiPlayerCore/images/` (10 张)

这些图像来自 `AutoFlow.Core/Skills/LastCloudia/` 目录，需要确认是否已从 AHK 项目复制过来。

### 3. 检查图像是否齐全

```bash
# 检查 TestScripts 的图像（外层规则用）
ls -1 D:/docs/github/AutoFlow/TestScripts/images/*.png

# 检查技能图像（构建输出目录）
ls -1 D:/docs/github/AutoFlow/AutoFlow.App/bin/Debug/net8.0-windows10.0.22621.0/Skills/LastCloudia/Common/images/*.png
ls -1 D:/docs/github/AutoFlow/AutoFlow.App/bin/Debug/net8.0-windows10.0.22621.0/Skills/LastCloudia/MultiPlayerCore/images/*.png
```

## 测试步骤

1. **启动游戏**：打开 Last Cloudia 游戏窗口

2. **启动 AutoFlow**：
   - 切换到"规则循环"模式（界面上的模式切换按钮）
   - 点击"打开脚本" → 选择 `MultiPlayerGame.rules.json`

3. **配置窗口**：
   - 在"目标窗口"下拉框选择 Last Cloudia 游戏窗口
   - **勾选**"运行前调整窗口尺寸"
   - 设置外框尺寸（根据你的 DPI 计算，保证客户区是 750×1040）
   - 点击"立即调整"测试窗口调整是否正确
   - 确认"实测客户区"显示为 `750×1040`

4. **运行脚本**：
   - 点击 "▶ 运行 (F9)" 或按 F9 键
   - 观察日志输出和游戏窗口行为

5. **停止脚本**：
   - 点击 "■ 停止 (F10)" 或按 F10 键

## 预期行为

### 场景 1：在房间列表页
- 检测到 `welcome` → 日志显示"命中规则 #0 [找到房间-点击第一个]"
- 移动鼠标到第一个房间位置 (344, 329) → 等 800ms → 点击 → 等 1000ms
- 进入房间后，下一轮会命中规则 #2（无条件），调用 MultiPlayerCoreSkill

### 场景 2：没有房间
- 检测到 `no_room` → 日志显示"命中规则 #1 [没有房间-更新列表]"
- 移动鼠标到"更新列表"按钮 (647, 955) → 等 800ms → 点击 → 等 6000ms
- 刷新后，继续循环检测

### 场景 3：已在房间内/战斗中/结算中
- 无图像匹配 → 命中规则 #2（无条件兜底）
- 日志显示"命中规则 #2 [其他情况-调用多人核心流程]"
- 日志显示"MultiPlayerCore 启动 (is_room_owner=false)"
- 进入 MultiPlayerCoreSkill 的状态机（准备/出发/战斗/结算/错误处理）

## 调试技巧

### 如果规则不触发
1. 检查图像路径是否正确（相对于 JSON 文件的 `images/` 子目录）
2. 检查区域坐标是否准确（可以用"预览区域"功能测试）
3. 降低 `MatchThreshold`（从 0.85 改为 0.75）

### 如果 CallSkill 报错
1. 检查日志中是否有"未找到技能: LastCloudia.MultiPlayerCore"
2. 确认 `SkillRegistry` 已注册该技能（应该在 Core 构建时自动注册）
3. 检查技能的图像文件是否在输出目录

### 如果点击位置不对
1. 确认窗口客户区确实是 750×1040（用"实测客户区"确认）
2. 确认游戏分辨率设置（游戏内设置可能影响 UI 布局）
3. 可以临时修改 JSON 中的 PositionX/Y 测试

## 参数说明

### 修改房主模式
如果你是房主（创建房间），修改第 3 条规则的参数：
```json
"SkillParameters": {
  "is_room_owner": true
}
```

房主逻辑：进入房间后等待 10 秒（凑人）再点出发。

非房主逻辑：累计等待 40 轮（约 80 秒），超时后点准备再点返回（撤退）。

## 技术细节

- **参考分辨率**：750×1040（AHK 脚本的客户区坐标空间）
- **模板匹配**：TM_CCOEFF_NORMED，阈值 0.85
- **规则匹配顺序**：从上到下，首个匹配的规则执行（if-else-if 语义）
- **无条件规则**：`TemplatePath: null` 的规则总是匹配（兜底）
- **技能调用**：通过 `CallSkill` 指令调用 C# 实现的复杂状态机
