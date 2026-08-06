# AutoFlow

基于图像识别的游戏自动化工具，采用**规则脚本**（低代码）+ **硬编码技能**（复杂流程）的混合架构。项目源于将 Last Cloudia 的 AutoHotkey 脚本迁移到 C#/.NET 的需求。

## 特性

- **规则脚本引擎**：通过 GUI 配置"若匹配到某画面 → 执行动作"的规则循环，无需编码
- **线性指令脚本**：顺序执行的点击、等待、循环指令流
- **窗口捕获与坐标转换**：自动测量目标窗口客户区，将脚本参考坐标映射到实际屏幕坐标
- **技能系统**：用 C# 实现带状态记忆、条件嵌套、参数分支的复杂流程，作为 `CallSkill` 指令暴露给规则脚本
- **模板匹配**：基于 OpenCV 的图像匹配，支持区域限定和阈值/均值差双重判定
- **帧调试模式**：捕获匹配失败的帧，便于排查模板和坐标问题

## 项目结构

```
AutoFlow/
├── AutoFlow.App/           WPF 桌面应用（GUI）
│   ├── ViewModels/         MVVM 视图模型
│   └── Views/              XAML 界面
├── AutoFlow.Core/          核心库
│   ├── Capture/            窗口捕获、坐标转换
│   ├── Scripting/          脚本引擎（规则脚本 + 线性指令）
│   │   ├── Handlers/       指令处理器（Click/Wait/Loop/CallSkill 等）
│   │   └── Rules/          规则脚本模型与循环解释器
│   ├── Skills/             硬编码技能（按游戏分组）
│   │   └── LastCloudia/    Last Cloudia 的技能实现
│   └── Vision/             模板匹配
├── Fischless.GameCapture/  游戏画面捕获（BitBlt / WGC）
├── Fischless.WindowsInput/ 鼠标键盘模拟
├── RuleTemplates/          可导入的规则模板库
└── TestScripts/            测试脚本
```

## 架构设计

### 混合架构

复杂的游戏流程（跨轮状态记忆、条件嵌套、参数分支、循环点击、随机选择）难以用规则脚本表达。项目采用分层设计：

- **底层核心循环**用 C# 硬编码为"技能"，通过 `CallSkill` 指令调用
- **上层场景脚本**用规则脚本组合（如"刷本直到无房间"）

规则脚本调用技能示例：

```json
{
  "Rules": [
    {
      "Name": "检测到房间列表",
      "Actions": [
        {
          "CommandType": "CallSkill",
          "SkillName": "LastCloudia.MultiPlayerCore",
          "Parameters": { "is_room_owner": true }
        }
      ]
    }
  ]
}
```

详见 [Skills_Architecture_Decision.md](Skills_Architecture_Decision.md)。

### 坐标系与窗口尺寸

脚本记录**目标窗口外框尺寸**（`OuterWidth`/`OuterHeight`），加载脚本时自动：

1. 按外框尺寸调整目标窗口
2. 测量实际客户区
3. 以实测客户区作为坐标转换基准

由于模板匹配不是尺度不变的，运行前窗口必须调整到脚本设计时的尺寸。客户区尺寸因系统边框/标题栏/DPI 而异，因此由工具实测而非写死在脚本中。

## 构建与运行

需要 .NET 8 SDK（Windows）。

```bash
# 构建
dotnet build AutoFlow.App/AutoFlow.App.csproj -c Release

# 运行
dotnet run --project AutoFlow.App
```

## 使用流程

1. 启动应用，选择目标游戏窗口
2. 打开规则脚本（`.rules.json`）→ 自动调整窗口并测量客户区
3. 点击运行 → 目标窗口自动激活到前台 → 开始规则循环
4. 匹配失败时可开启帧调试模式排查模板/坐标问题

## 许可证

见 [LICENSE.txt](LICENSE.txt)。

第三方组件 Fischless.GameCapture / Fischless.WindowsInput / Fischless.HotkeyCapture 保留各自的许可证。
