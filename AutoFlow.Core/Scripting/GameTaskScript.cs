using AutoFlow.Core.Scripting.Actions;
using AutoFlow.Core.Scripting.Conditions;
using AutoFlow.Core.Scripting.Instructions;
using AutoFlow.Core.Scripting.Rules;
using AutoFlow.Core.Skills;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AutoFlow.Core.Scripting;

/// <summary>Loads a complete built-in task without changing editor or window state.</summary>
public static class GameTaskScript
{
    public static T Copy<T>(T value) =>
        JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(value))!;

    public static RuleScript Load(string path)
    {
        var json = JObject.Parse(File.ReadAllText(path));
        if ((string?)json["Schema"] != "autoflow/rule-script" ||
            (int?)json["FormatVersion"] != ScriptFormatVersion.Current ||
            json["Rules"] is not JArray { Count: > 0 } ||
            (int?)json["OuterWidth"] is not > 0 || (int?)json["OuterHeight"] is not > 0)
            throw new InvalidOperationException($"预置脚本格式、规则或窗口尺寸无效: {path}");
        return json.ToObject<RuleScript>() ?? throw new InvalidOperationException($"脚本为空: {path}");
    }

    public static ScriptDocument LoadLinear(string path)
    {
        var json = JObject.Parse(File.ReadAllText(path));
        var hasSteps = json["Steps"] is JArray { Count: > 0 };
        var hasInstructions = json["Instructions"] is JArray { Count: > 0 };
        if ((string?)json["Schema"] != "autoflow/script" ||
            (int?)json["FormatVersion"] != ScriptFormatVersion.Current ||
            (!hasSteps && !hasInstructions) ||
            (int?)json["OuterWidth"] is not > 0 || (int?)json["OuterHeight"] is not > 0)
            throw new InvalidOperationException($"预置线性脚本格式、步骤或窗口尺寸无效: {path}");

        var document = new ScriptDocumentService().Load(path);
        return document;
    }

    public static void Validate(RuleScript script, ConditionLibrary conditions,
        ActionBlockLibrary blocks, string appDirectory)
    {
        if (script.Schema != "autoflow/rule-script" || script.FormatVersion != ScriptFormatVersion.Current ||
            script.Rules is not { Count: > 0 } || script.OuterWidth <= 0 || script.OuterHeight <= 0)
            throw new InvalidOperationException("脚本没有规则或窗口尺寸无效。");
        var expander = new ScriptStepExpander(conditions, blocks);
        var shared = new SharedScriptResources(conditions, blocks, Handlers.InstructionHandlerRegistry.CreateDefault());
        var allActions = new List<ExpandedStep>();
        foreach (var rule in script.Rules)
        {
            if (rule is null) throw new InvalidOperationException("脚本含空规则。");
            if (rule.UsesSharedCondition)
                CheckImage(conditions.Require(rule.ConditionRef!, "多人游戏", rule.Name));
            if (rule.Steps is null || rule.Actions is null) throw new InvalidOperationException($"规则 {rule.Name} 的动作列表为空值。");
            var actions = rule.Steps.Count > 0
                ? expander.Expand(rule.Steps, "多人游戏", rule.Name)
                : rule.Actions.Select(i => new ExpandedStep { Instruction = i, Origin = rule.Name }).ToList();
            allActions.AddRange(actions);
            foreach (var action in actions)
            {
                if (action.Condition is not null) CheckImage(action.Condition);
                var instruction = action.Instruction;
                if (instruction is null) throw new InvalidOperationException($"规则 {rule.Name} 含空动作。");
                if (instruction.CommandType != CommandType.CallSkill) continue;
                if (string.IsNullOrEmpty(instruction.SkillName) ||
                    SkillRegistry.Instance.GetSkill(instruction.SkillName) is null)
                    throw new InvalidOperationException($"未注册技能: {instruction.SkillName} ({action.Origin})");
            }
        }
        shared.ValidateSkillDependencies(allActions, appDirectory);
    }

    public static void Validate(ScriptDocument script, ConditionLibrary conditions,
        ActionBlockLibrary blocks, string appDirectory)
    {
        if (script.Schema != "autoflow/script" || script.FormatVersion != ScriptFormatVersion.Current ||
            (script.Steps is not { Count: > 0 } && script.Instructions is not { Count: > 0 }) ||
            script.OuterWidth <= 0 || script.OuterHeight <= 0)
            throw new InvalidOperationException("线性脚本没有步骤或窗口尺寸无效。");

        var steps = new ScriptStepExpander(conditions, blocks).Expand(script, script.Name ?? "周回活动");
        foreach (var step in steps)
        {
            if (step.Condition is not null) CheckImage(step.Condition);
            if (step.Instruction.CommandType != CommandType.CallSkill) continue;
            if (string.IsNullOrWhiteSpace(step.Instruction.SkillName) ||
                SkillRegistry.Instance.GetSkill(step.Instruction.SkillName) is null)
                throw new InvalidOperationException($"未注册技能: {step.Instruction.SkillName} ({step.Origin})");
        }

        new SharedScriptResources(conditions, blocks, Handlers.InstructionHandlerRegistry.CreateDefault())
            .ValidateSkillDependencies(steps, appDirectory);
    }

    private static void CheckImage(VisualConditionSpec condition)
    {
        if (string.IsNullOrWhiteSpace(condition.EffectiveTemplatePath) || !File.Exists(condition.EffectiveTemplatePath))
            throw new FileNotFoundException($"条件 {condition.Id} 的图片缺失: {condition.EffectiveTemplatePath}");
    }

}
