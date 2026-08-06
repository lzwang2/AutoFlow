# AutoFlow 规则模板库

## 目录结构

```
RuleTemplates/
└── LastCloudia/           Last Cloudia 游戏的可复用规则模板
    ├── *.rules.json       11 个规则模板文件
    └── images/            模板引用的图像资源（11 张 PNG）
```

## 规则模板列表（Last Cloudia）

每个模板封装一个"检测→处理"的原子行为，可以在规则编辑器中加载并复用。

| 文件名 | 说明 | 图像 |
|--------|------|------|
| `abandon_task.rules.json` | 检测"放弃任务"弹窗并点击确认 | `abandon_task_area.png` |
| `connection_fail.rules.json` | 检测"无法连接到伺服器"（类型1）并点击确认 | `connection_fail_area.png` |
| `connection_fail2.rules.json` | 检测"无法连接到伺服器"（类型2）并点击确认 | `connection_fail_area2.png` |
| `multi_connection_fail.rules.json` | 检测"无法连接多人游戏伺服器"并点击确认 | `multi_connection_fail_area.png` |
| `network_error.rules.json` | 检测"发生了网络错误"并点击确认 | `network_error_area.png` |
| `unable_to_join.rules.json` | 检测"无法创建或加入房间"并点击确认 | `unable_to_join.png` |
| `first_clear_reward.rules.json` | 检测"初次通关奖励"并点击确认 | `first_clear_reward.png` |
| `first_meeting.rules.json` | 检测"初次见面奖励"并点击确认 | `first_meeting.png` |
| `multi_clear_reward.rules.json` | 检测"多人奖励"并点击确认 | `multi_clear_reward.png` |
| `multi_game_refight.rules.json` | 检测"再战确认"并点击确认 | `multi_game_refight.png` |
| `refight_denied.rules.json` | 检测"再战被拒绝"并点击确认 | `refight_denied.png` |

## 使用方式

### 方法 1：在编辑器中加载模板

1. 打开 AutoFlow 规则编辑器（规则循环模式）
2. 点击"加载模板"按钮（如果已实现）
3. 选择对应的 `.rules.json` 文件
4. 规则会被插入到当前规则列表中

### 方法 2：手动复制到脚本中

1. 用文本编辑器打开模板文件
2. 复制 `Rules` 数组中的规则对象
3. 粘贴到你的脚本 JSON 的 `Rules` 数组中

### 方法 3：组合多个模板

将多个模板的规则合并到一个完整的脚本中，例如：

```json
{
  "ReferenceWidth": 750,
  "ReferenceHeight": 1040,
  "Rules": [
    // ... 从 connection_fail.rules.json 复制的规则
    // ... 从 network_error.rules.json 复制的规则
    // ... 你自己的业务规则
  ]
}
```

## 参考分辨率

所有 Last Cloudia 模板使用 **750×1040** 客户区坐标空间（对应 AHK 脚本的坐标系统）。

运行前需要：
1. 勾选"运行前调整窗口尺寸"
2. 调整窗口客户区到 750×1040

## 图像路径

模板中的图像路径相对于模板文件：
- 模板文件：`RuleTemplates/LastCloudia/xxx.rules.json`
- 图像资源：`RuleTemplates/LastCloudia/images/xxx.png`
- JSON 中写作：`"TemplatePath": "images/xxx.png"`

如果将模板复制到其他位置，需要同时复制 `images/` 目录。

## 与 Skills 的关系

规则模板库和 C# Skills（`AutoFlow.Core/Skills/LastCloudia/`）是**两套独立的资源**：

- **规则模板**：用户可见、可编辑的 JSON 规则，适合简单的"图像检测→点击"场景
- **C# Skills**：硬编码的复杂状态机，适合需要跨 tick 状态、嵌套逻辑、随机选择的场景

两者的图像资源是各自维护的（虽然有部分重叠），这样每层都是自包含的。

## 维护说明

如果从 AHK 脚本新增/更新模板：
1. 将新模板和图像放到对应游戏子目录（如 `LastCloudia/`）
2. 确保图像路径使用相对路径 `images/xxx.png`
3. 确认 `ReferenceWidth`/`ReferenceHeight` 已设置（750×1040）
4. 提交到 Git，让其他用户也能使用
