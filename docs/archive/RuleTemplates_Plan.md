# Last Cloudia 规则模板资源清单

> 历史规划归档：2026-11-14 从项目根目录迁入。保留原始资源分析及阶段安排，不代表当前模板格式、目录结构或实现完成状态。

本文档记录从 AHK 脚本转换为 AutoFlow 规则模板所需的资源信息。

## 数据源分析

### 坐标定义来源
- **截图区域坐标**: `MultiPlayerCoreProcess.ahk` 行 8-18 (`multi_player_core_coords`)
- **点击坐标**: `MultiPlayerCoreProcess.ahk` 行 37-43
- **通用坐标**: `CommonActions.ahk` 行 5-52

### 图像文件位置
- **多人核心流程**: `D:\docs\github\game-scripts\last_cloudia\ahk\utils\multi_player_core\*.png`
- **通用处理**: `D:\docs\github\game-scripts\last_cloudia\ahk\utils\*.png`

---

## Phase 1: 高优先级模板（8个）

### 1. 无法加入房间 (Unable to Join)

**用途**: 处理创建或加入房间失败的弹窗

**条件（图像匹配）:**
- 模板图像: `utils/multi_player_core/unable_to_join.png`
- 截图区域: `{x1: 285, y1: 320, x2: 440, y2: 340}`
- 匹配阈值: 0.85

**动作序列:**
1. 点击坐标: `(368, 645)` - unable_yes 按钮
   - 执行后延迟: 1000ms

**规则设置:**
- 名称: "无法加入房间"
- 启用: true
- 触发一次: false

---

### 2. 再战确认 (Refight Confirmation)

**用途**: 多人战斗结束后的再战确认

**条件（图像匹配）:**
- 模板图像: `utils/multi_player_core/multi_game_refight.png`
- 截图区域: `{x1: 100, y1: 540, x2: 210, y2: 565}`
- 匹配阈值: 0.85

**动作序列:**
1. 点击坐标: `(470, 295)` - multi_game_refight_yes 按钮
   - 执行后延迟: 1000ms

**规则设置:**
- 名称: "再战确认"
- 启用: true
- 触发一次: false

---

### 3. 初次通关奖励 (First Clear Reward)

**用途**: 首次通关关卡的奖励确认

**条件（图像匹配）:**
- 模板图像: `utils/multi_player_core/first_clear_reward.png`
- 截图区域: `{x1: 182, y1: 207, x2: 283, y2: 232}`
- 匹配阈值: 0.85

**动作序列:**
1. 点击坐标: `(370, 535)` - first_clear_reward_yes 按钮
   - 执行后延迟: 800ms

**规则设置:**
- 名称: "初次通关奖励确认"
- 启用: true
- 触发一次: false

---

### 4. 初次见面奖励 (First Meeting)

**用途**: 首次与其他玩家组队的奖励

**条件（图像匹配）:**
- 模板图像: `utils/multi_player_core/first_meeting.png`
- 截图区域: `{x1: 215, y1: 720, x2: 515, y2: 775}`
- 匹配阈值: 0.85

**动作序列:**
1. 点击坐标: `(370, 750)` - first_meeting_close 按钮
   - 执行后延迟: 800ms

**规则设置:**
- 名称: "初次见面奖励"
- 启用: true
- 触发一次: false

---

### 5. 多人奖励确认 (Multi Clear Reward)

**用途**: 多人游戏通关奖励确认

**条件（图像匹配）:**
- 模板图像: `utils/multi_player_core/multi_clear_reward.png`
- 截图区域: `{x1: 254, y1: 910, x2: 465, y2: 965}`
- 匹配阈值: 0.85

**动作序列:**
1. 点击坐标: `(381, 940)` - multi_clear_reward_yes 按钮
   - 执行后延迟: 800ms

**规则设置:**
- 名称: "多人奖励确认"
- 启用: true
- 触发一次: false

---

### 6. 再战被拒绝 (Refight Denied)

**用途**: 再战请求被拒绝的提示

**条件（图像匹配）:**
- 模板图像: `utils/multi_player_core/refight_denied.png`
- 截图区域: `{x1: 315, y1: 320, x2: 415, y2: 345}`
- 匹配阈值: 0.85

**动作序列:**
1. 点击坐标: `(355, 650)` - refight_denied_yes 按钮
   - 执行后延迟: 800ms

**规则设置:**
- 名称: "再战被拒绝"
- 启用: true
- 触发一次: false

---

### 7. 网络错误 - 无法连接伺服器 (Connection Fail)

**用途**: 处理网络连接失败弹窗

**条件（图像匹配）:**
- 模板图像: `utils/connection_fail_area.png`
- 截图区域: `{x1: 220, y1: 370, x2: 520, y2: 420}`
- 匹配阈值: 0.85

**动作序列:**
1. 点击坐标: `(370, 660)` - connection_fail_button
   - 执行后延迟: 1000ms

**规则设置:**
- 名称: "网络错误-无法连接伺服器"
- 启用: true
- 触发一次: false

---

### 8. 网络错误 - 无法连接伺服器2 (Connection Fail Type 2)

**用途**: 处理第二种网络连接失败弹窗样式

**条件（图像匹配）:**
- 模板图像: `utils/connection_fail_area2.png`
- 截图区域: `{x1: 250, y1: 320, x2: 470, y2: 370}`
- 匹配阈值: 0.85

**动作序列:**
1. 点击坐标: `(370, 700)` - connection_fail_button2
   - 执行后延迟: 1000ms

**规则设置:**
- 名称: "网络错误-无法连接伺服器2"
- 启用: true
- 触发一次: false

---

### 9. 网络错误 - 无法连接多人游戏伺服器 (Multi Connection Fail)

**用途**: 多人游戏伺服器连接失败

**条件（图像匹配）:**
- 模板图像: `utils/multi_connection_fail_area.png`
- 截图区域: `{x1: 275, y1: 290, x2: 455, y2: 340}`
- 匹配阈值: 0.85

**动作序列:**
1. 点击坐标: `(365, 750)` - multi_connection_fail_button
   - 执行后延迟: 1000ms

**规则设置:**
- 名称: "网络错误-无法连接多人游戏伺服器"
- 启用: true
- 触发一次: false

---

### 10. 网络错误 - 发生了网络错误 (Network Error)

**用途**: 通用网络错误提示

**条件（图像匹配）:**
- 模板图像: `utils/network_error_area.png`
- 截图区域: `{x1: 285, y1: 430, x2: 435, y2: 455}`
- 匹配阈值: 0.85

**动作序列:**
1. 点击坐标: `(365, 640)` - network_error_button
   - 执行后延迟: 1000ms

**规则设置:**
- 名称: "网络错误-发生了网络错误"
- 启用: true
- 触发一次: false

---

### 11. 放弃任务 (Abandon Task)

**用途**: 房间解散时的放弃任务确认

**条件（图像匹配）:**
- 模板图像: `utils/abandon_task_area.png`
- 截图区域: `{x1: 310, y1: 320, x2: 420, y2: 350}`
- 匹配阈值: 0.85

**动作序列:**
1. 点击坐标: `(368, 645)` - abandon_task_button
   - 执行后延迟: 1000ms

**规则设置:**
- 名称: "放弃任务确认"
- 启用: true
- 触发一次: false

---

## Phase 2: 中优先级模板（待功能支持）

### 队伍准备页面 (Prepare Page)
- **需要功能**: 展开 ClickReady() 坐标或支持调用动作函数
- 模板图像: `utils/multi_player_core/prepare_page.png`
- 区域: `{x1: 300, y1: 850, x2: 430, y2: 880}`
- 动作: 点击准备按钮 `(375, 865)` → 等待 2000ms

### 战斗结算 (Battle End)
- **需要功能**: 支持循环点击或 MultiClick 操作
- 模板图像: `utils/battle_end_area.png`
- 区域: `{x1: 169, y1: 205, x2: 292, y2: 270}`
- 动作: 移动到安全位置 `(708, 500)` → 多次点击跳过

### 表情包界面 (Emoji)
- **需要功能**: 支持随机坐标选择
- 模板图像: `utils/multi_player_core/emoji.png`
- 区域: `{x1: 481, y1: 849, x2: 606, y2: 956}`
- 动作: 随机选择表情坐标并点击
- 触发一次: true

---

## 实现计划

### Step 1: 准备图像文件
将 AHK 工程中的图像复制到 AutoFlow 模板目录：
```
源: D:\docs\github\game-scripts\last_cloudia\ahk\utils\
目标: [我的文档]\AutoFlow\RuleTemplates\LastCloudia\images\
```

### Step 2: 生成规则模板 JSON
为每个规则创建 `.rules.json` 文件，包含：
- Metadata（名称、描述、作者）
- Rule 定义（条件 + 动作）

### Step 3: 测试验证
在 AutoFlow 中导入模板，验证：
- 图像匹配是否正确
- 动作执行是否符合预期
- 坐标是否准确（750×1040 客户区基准）

---

## 注意事项

1. **坐标系统（重要）**: AHK 脚本中的坐标**不是** 1920×1080 基准。
   `utils/PrepareWindow.ahk` 先把游戏窗口调整为 `750×1040`（`WinMove`，外框尺寸），
   之后 `Tools.ahk` 的 `IsImageMatch` 通过 `ImagePutBuffer({window: hWnd, crop: [...]})`
   截取，crop 是**客户区相对坐标**。全部 AHK 文件中坐标最大值为 X=708 / Y=1000，
   与 750×1040 一致。

   因此这些坐标与截图都活在 AHK 的 750×1040 客户区空间里：
   - 模板图片**不能**在 1920×1080 下使用。OpenCV 模板匹配不具备尺度不变性，
     在 750px 宽下截取的图无法匹配同一元素放大 2.56 倍后的样子。
   - 750×1040 的宽高比是 0.72，1920×1080 是 1.78 —— 两者不是彼此的缩放，
     无法通过换算坐标解决。

   AutoFlow 已支持此场景（见 `PrepareTargetWindowForRun`）：
   - 工具栏勾选"运行前调整窗口尺寸"，外框填 `750×1040`（与 `WinMove` 语义一致）；
   - "参考分辨率"填脚本坐标所在空间，随脚本保存在 `RuleScript.ReferenceWidth/Height`，
     打开脚本时自动恢复；
   - 外框尺寸含边框与标题栏，客户区更小。点"立即调整"后用"← 用实测客户区"
     把实测值写入参考分辨率，不要凭假设填写。
   - AHK 坐标**原样照抄**，不做任何换算。
2. **图像路径**: 模板 JSON 中使用相对路径，便于迁移
3. **匹配阈值**: 初始使用 0.85，可根据实际情况调整
4. **动作延迟**: 保持与 AHK 脚本一致的延迟时间
5. **规则优先级**: 在实际使用时，网络错误类规则应放在列表前面（更高优先级）

---

## 下一步

- [x] 创建图像目录结构（`[我的文档]\AutoFlow\RuleTemplates\LastCloudia\images\`）
- [x] 复制所需图像文件（11 张 PNG）
- [x] 生成 11 个规则模板 JSON 文件（每个模板 = 一条可复用的"若→动作"规则）
- [ ] 在 AutoFlow 中测试导入（需运行 GUI + 目标窗口）
- [ ] 调优匹配参数

### 延迟语义说明

AHK 原始流程是 `MouseMove → Sleep(delay) → Click`（延迟在点击**之前**）。
AutoFlow 的 Click 动作先点击，`IntervalToNextInstructionMs` 是点击**之后**的延迟。
由于每条模板都是单动作规则、且规则循环每轮重新扫描，把延迟放在点击之后
等效于点击后的冷却时间，效果一致，故模板中延迟值原样沿用 AHK 的数值。
