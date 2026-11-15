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

    public static void Validate(RuleScript script, ConditionLibrary conditions,
        ActionBlockLibrary blocks, string appDirectory)
    {
        if (script.Schema != "autoflow/rule-script" || script.FormatVersion != ScriptFormatVersion.Current ||
            script.Rules is not { Count: > 0 } || script.OuterWidth <= 0 || script.OuterHeight <= 0)
            throw new InvalidOperationException("脚本没有规则或窗口尺寸无效。");
        var expander = new ScriptStepExpander(conditions, blocks);
        foreach (var rule in script.Rules)
        {
            if (rule is null) throw new InvalidOperationException("脚本含空规则。");
            if (rule.UsesSharedCondition)
                CheckImage(conditions.Require(rule.ConditionRef!, "多人游戏", rule.Name));
            if (rule.Steps is null || rule.Actions is null) throw new InvalidOperationException($"规则 {rule.Name} 的动作列表为空值。");
            var actions = rule.Steps.Count > 0
                ? expander.Expand(rule.Steps, "多人游戏", rule.Name)
                : rule.Actions.Select(i => new ExpandedStep { Instruction = i, Origin = rule.Name }).ToList();
            foreach (var action in actions)
            {
                if (action.Condition is not null) CheckImage(action.Condition);
                var instruction = action.Instruction;
                if (instruction is null) throw new InvalidOperationException($"规则 {rule.Name} 含空动作。");
                if (instruction.CommandType != CommandType.CallSkill) continue;
                if (string.IsNullOrEmpty(instruction.SkillName) ||
                    SkillRegistry.Instance.GetSkill(instruction.SkillName) is null)
                    throw new InvalidOperationException($"未注册技能: {instruction.SkillName} ({action.Origin})");
                if (instruction.SkillName == "LastCloudia.MultiPlayerCore") CheckSkillImages(appDirectory);
            }
        }
    }

    private static void CheckImage(VisualConditionSpec condition)
    {
        if (string.IsNullOrWhiteSpace(condition.EffectiveTemplatePath) || !File.Exists(condition.EffectiveTemplatePath))
            throw new FileNotFoundException($"条件 {condition.Id} 的图片缺失: {condition.EffectiveTemplatePath}");
    }

    private static void CheckSkillImages(string appDirectory)
    {
        var groups = new Dictionary<string, string[]>
        {
            ["Common"] = ["abandon_task_area", "battle_end_area", "battle_last_result", "first_clear_reward_area",
                "network_error_area", "server_connection_fail_multiplayer", "server_connection_fail_retreat",
                "server_connection_fail_return", "stamina_recover_area", "team_prepare_area"],
            ["MultiPlayerCore"] = ["emoji", "first_clear_reward", "first_meeting", "go", "multi_clear_reward",
                "multi_game_refight", "not_prepared", "prepare_page", "refight_denied", "unable_to_join"]
        };
        foreach (var (group, names) in groups)
            foreach (var name in names)
            {
                var path = Path.Combine(appDirectory, "Skills", "LastCloudia", group, "images", name + ".png");
                if (!File.Exists(path)) throw new FileNotFoundException($"技能图片缺失: {path}");
            }
    }
}
