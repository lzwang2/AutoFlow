# AutoFlow 规则模板库

> 本文保留早期模板用法，目录和示例尚未按 v2 格式全面复核。当前模型参见[脚本复用规范](../docs/features/script-reuse/Script_Reuse_Spec.md)，全部文档见[文档索引](../docs/README.md)。

## 目录结构

```
RuleTemplates/
└── LastCloudia/           Last Cloudia 游戏的可复用规则模板
    ├── Rules/            规则模板和完整脚本（含 MultiPlayerGame.rules.json）
    ├── Conditions/       共享条件
    ├── Actions/          共享动作块
    ├── Scripts/          线性脚本
    └── images/            模板引用的图像资源（11 张 PNG）
```

`LastCloudia/Rules/MultiPlayerGame.rules.json` 是多人游戏完整脚本，也是专用面板的唯一预置脚本源。构建后随模板库复制到可执行文件旁的同名目录；面板与模板浏览器使用同一份文件。下列原子模板仍可独立复用。

## 规则模板列表（Last Cloudia）

每个模板封装一个"检测→处理"的原子行为，可以在规则编辑器中加载并复用。

| 文件名 | 说明 | 图像 |
|--------|------|------|
| `abandon_task.rules.json` | 检测"放弃任务"弹窗并点击确认 | `abandon_task_area.png` |
| `server_connection_fail_multiplayer.rules.json` | 服务器连接失败-多人游戏 | `server_connection_fail_multiplayer.png` |
| `server_connection_fail_retreat.rules.json` | 服务器连接失败-撤退并返回 | `server_connection_fail_retreat.png` |
| `server_connection_fail_return.rules.json` | 服务器连接失败-直接返回 | `server_connection_fail_return.png` |
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
  "Schema": "autoflow/rule-script",
  "FormatVersion": 1,
  "OuterWidth": 750,
  "OuterHeight": 1040,
  "Rules": [
    // ... 从 network_error.rules.json 复制的规则
    // ... 从 unable_to_join.rules.json 复制的规则
    // ... 你自己的业务规则
  ]
}
```

`OuterWidth` / `OuterHeight` 是目标窗口的**外框**尺寸（对应 AHK `WinMove` 的语义），不是客户区尺寸。应用会据此调整窗口，再实测客户区来建立坐标映射。

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
1. 将新模板和图像放到对应游戏子目录（如 `LastCloudia/`），图像放 `LastCloudia/images/`
2. 图像路径写成 `LastCloudia/images/xxx.png`（含游戏名），以 `RuleTemplates/` 为基准解析。不要写成 `images/xxx.png`——那是「以脚本自身所在目录为基准」的写法，只适用于自带私有图片的脚本
3. 不要写绝对路径，那样模板无法分享
4. 确认 `OuterWidth`/`OuterHeight` 已设置（750×1040）
5. 提交到 Git，让其他用户也能使用
