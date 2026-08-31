using AutoFlow.Core.Scripting.Actions;
using AutoFlow.Core.Scripting.Conditions;
using AutoFlow.Core.Scripting.Handlers;
using OpenCvSharp;

namespace AutoFlow.Core.Scripting.Rules;

/// <summary>
/// Executes a <see cref="RuleScript"/> as a reactive dispatch loop: each tick, capture a
/// frame, evaluate rules top-to-bottom, run the actions of the first matching rule, then
/// repeat. If no rule matches, idle for <see cref="RuleScript.IdleTickMs"/> and re-check.
/// </summary>
public sealed class RuleLoopInterpreter
{
    private readonly InstructionHandlerRegistry _handlerRegistry;
    private readonly ConditionLibrary? _conditions;
    private readonly ActionBlockLibrary? _blocks;

    public RuleLoopInterpreter(
        InstructionHandlerRegistry handlerRegistry,
        ConditionLibrary? conditions = null,
        ActionBlockLibrary? blocks = null)
    {
        _handlerRegistry = handlerRegistry;
        _conditions = conditions;
        _blocks = blocks;
    }

    public async Task ExecuteAsync(
        RuleScript script,
        ScriptExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        if (script.Rules == null || script.Rules.Count == 0)
        {
            context.Log("规则脚本没有任何规则");
            return;
        }

        context.Log($"开始规则循环: {script.Metadata?.Description ?? "未命名"} ({script.Rules.Count} 条规则)");

        // Resolve every reference before the first frame. A missing condition or action block
        // must fail here, not halfway through a run that has already clicked things.
        var resolved = Resolve(script, context);

        var firedOnce = new HashSet<int>();
        var idleTick = script.IdleTickMs > 0 ? script.IdleTickMs : 500;
        var tickCount = 0;
        var noMatchFrameSaved = false; // Track if we've already saved a no-match frame

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var frame = context.CaptureFrame();
            if (frame == null || frame.Empty())
            {
                await Task.Delay(idleTick, cancellationToken);
                continue;
            }

            tickCount++;

            // Debug: save captured frame based on mode
            if (context.EnableFrameDebug && !string.IsNullOrEmpty(context.FrameDebugPath))
            {
                var shouldSave = context.FrameDebugMode switch
                {
                    0 => tickCount == 1, // First frame only
                    1 => false,          // Matched frames only (checked later)
                    2 => false,          // No-match frames only (checked later)
                    _ => false
                };

                if (shouldSave)
                {
                    SaveDebugFrame(context, frame, tickCount, "full", resolved);
                }
            }

            var matchedIndex = EvaluateRules(resolved, context, frame, firedOnce, tickCount == 1);

            if (matchedIndex < 0)
            {
                // Debug: save no-match frame if mode is 2 (no-match only) - only once per loop session
                if (context.EnableFrameDebug && context.FrameDebugMode == 2 && !string.IsNullOrEmpty(context.FrameDebugPath) && !noMatchFrameSaved)
                {
                    SaveDebugFrame(context, frame, tickCount, "nomatch", resolved);
                    noMatchFrameSaved = true;
                }

                // Log every 10 ticks to show the loop is running
                if (tickCount % 10 == 0)
                {
                    context.Log($"循环 tick {tickCount}: 无规则匹配");
                }
                await Task.Delay(idleTick, cancellationToken);
                continue;
            }

            var rule = script.Rules[matchedIndex];
            context.Log($"Tick {tickCount}: 命中规则 #{matchedIndex} [{rule.Name}]");

            // Debug: save matched frame if mode is 1 (matched only)
            if (context.EnableFrameDebug && context.FrameDebugMode == 1 && !string.IsNullOrEmpty(context.FrameDebugPath))
            {
                SaveDebugFrame(context, frame, tickCount, "matched", resolved, matchedIndex);
            }

            if (rule.TriggerOnce)
            {
                firedOnce.Add(matchedIndex);
            }

            await RunActionsAsync(resolved[matchedIndex], context, cancellationToken);
        }
    }

    /// <summary>
    /// A rule with its condition and action list already resolved: shared references looked up,
    /// action blocks expanded. Built once per run.
    /// </summary>
    private sealed class ResolvedRule
    {
        public required int Index { get; init; }
        public required Rule Rule { get; init; }

        /// <summary>Null for an unconditional catch-all rule, which always matches.</summary>
        public VisualConditionSpec? Condition { get; init; }

        public required List<ExpandedStep> Actions { get; init; }
    }

    private List<ResolvedRule> Resolve(RuleScript script, ScriptExecutionContext context)
    {
        var expander = new ScriptStepExpander(_conditions, _blocks);
        var scriptName = script.Metadata?.Name ?? script.Metadata?.Description ?? "规则脚本";
        var resolved = new List<ResolvedRule>(script.Rules.Count);
        var sharedCount = 0;
        var blockRuleCount = 0;

        for (var i = 0; i < script.Rules.Count; i++)
        {
            var rule = script.Rules[i];
            var label = $"规则 #{i} [{rule.Id ?? rule.Name}]";

            var condition = ResolveCondition(rule, scriptName, label);
            if (rule.UsesSharedCondition)
            {
                sharedCount++;
            }

            List<ExpandedStep> actions;
            if (rule.Steps.Count > 0)
            {
                actions = expander.Expand(rule.Steps, scriptName, label);
                blockRuleCount++;
            }
            else
            {
                actions = rule.Actions
                    .Select((instruction, index) => new ExpandedStep
                    {
                        Instruction = instruction,
                        Condition = null,
                        Origin = $"{label} 动作 {index}"
                    })
                    .ToList();
            }

            resolved.Add(new ResolvedRule
            {
                Index = i,
                Rule = rule,
                Condition = condition,
                Actions = actions
            });
        }

        if (sharedCount > 0 || blockRuleCount > 0)
        {
            context.Log($"  已解析引用: {sharedCount} 条规则使用共享条件, {blockRuleCount} 条规则使用步骤列表");
        }

        WarnOnReferenceSpaceMismatch(script, resolved, context);
        return resolved;
    }

    /// <summary>
    /// Resolves a rule's condition. Precedence: shared reference, then inline fields, then
    /// null for the catch-all case. Returning null is a real outcome here, not a failure —
    /// <c>MultiPlayerGame.rules.json</c> depends on a trailing rule with no condition at all.
    /// </summary>
    private VisualConditionSpec? ResolveCondition(Rule rule, string scriptName, string label)
    {
        if (rule.UsesSharedCondition)
        {
            if (_conditions is null)
            {
                throw new ConditionResolutionException(
                    $"{label} 引用了条件 '{rule.ConditionRef}'，但未提供条件库。");
            }

            return _conditions.Require(rule.ConditionRef!, scriptName, label);
        }

        return string.IsNullOrWhiteSpace(rule.TemplatePath) ? null : ConditionOf(rule);
    }

    /// <summary>
    /// Logs a warning when a shared condition's regions were authored against a different
    /// window size than this script declares. Logged rather than thrown: the match may still
    /// succeed, and refusing to run would be worse than a noisy log (FR-10).
    /// </summary>
    private static void WarnOnReferenceSpaceMismatch(
        RuleScript script, List<ResolvedRule> resolved, ScriptExecutionContext context)
    {
        var reported = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in resolved)
        {
            if (entry.Condition is null || !entry.Rule.UsesSharedCondition)
            {
                continue;
            }

            var check = ReferenceSpaceValidator.Check(
                entry.Condition, script.OuterWidth, script.OuterHeight);

            if (!check.IsOk && reported.Add(entry.Condition.Id))
            {
                context.Log($"  [警告] {check.Message}");
            }
        }
    }

    /// <summary>
    /// Returns the index of the first matching rule, or -1 if none matched.
    /// Rules without a TemplatePath are unconditional (always match) and serve as fallback/catch-all.
    /// </summary>
    private static int EvaluateRules(
        List<ResolvedRule> resolved, ScriptExecutionContext context, Mat frame,
        HashSet<int> firedOnce, bool verbose = false)
    {
        for (var i = 0; i < resolved.Count; i++)
        {
            var entry = resolved[i];
            var rule = entry.Rule;
            if (!rule.Enabled)
            {
                continue;
            }
            if (rule.TriggerOnce && firedOnce.Contains(i))
            {
                continue;
            }

            // Unconditional rule always matches (fallback/catch-all semantics).
            if (entry.Condition is null)
            {
                return i;
            }

            // Conditional rule: check image match
            if (verbose)
            {
                var source = rule.UsesSharedCondition ? $"共享条件 '{rule.ConditionRef}'" : "内联条件";
                context.Log($"  [DEBUG] 尝试匹配规则 #{i} '{rule.Name}', {source}, 模板: {entry.Condition.TemplatePath}");
            }
            if (VisualConditionEvaluator.Evaluate(entry.Condition, frame, context) is not null)
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// Adapts a rule's inline condition fields to a <see cref="VisualConditionSpec"/>.
    ///
    /// Used only for rules that have no <see cref="Rule.ConditionRef"/> yet. Phase 6's migration
    /// rewrites the 13 existing scripts to references and Phase 7 deletes the inline fields;
    /// until then this keeps both interpreters on one evaluation path.
    /// </summary>
    private static VisualConditionSpec ConditionOf(Rule rule) => new()
    {
        Id = rule.Name,
        Name = rule.Name,
        TemplatePath = rule.TemplatePath,
        MatchThreshold = rule.MatchThreshold,
        MeanDiffThreshold = rule.MeanDiffThreshold,
        RegionX1 = rule.RegionX1,
        RegionY1 = rule.RegionY1,
        RegionX2 = rule.RegionX2,
        RegionY2 = rule.RegionY2
    };

    private async Task RunActionsAsync(ResolvedRule entry, ScriptExecutionContext context, CancellationToken cancellationToken)
    {
        for (var i = 0; i < entry.Actions.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var step = entry.Actions[i];
            var action = step.Instruction;
            var handler = _handlerRegistry.GetHandler(action.CommandType);
            if (handler == null)
            {
                context.Log($"  跳过动作 {i}: 无处理器 {action.CommandType}");
                continue;
            }

            // Loop jumps are meaningless inside a rule's action list; ignore them.
            context.JumpToIndex = null;

            // Same contract as the linear interpreter: set right before the handler runs, clear
            // right after, so a leftover cannot bleed into the next action.
            context.CurrentCondition = step.Condition;

            try
            {
                await handler.ExecuteAsync(action, context, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                context.Log($"  动作 {i} ({action.CommandType}) 出错: {ex.Message}");
            }
            finally
            {
                context.CurrentCondition = null;
            }

            if (action.IntervalToNextInstructionMs > 0)
            {
                await Task.Delay(action.IntervalToNextInstructionMs, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Dumps the frame plus each rule's cropped region for diagnosis.
    ///
    /// Takes the resolved rules rather than the raw ones: once a rule uses a shared condition
    /// its inline region fields are empty, so reading them here would silently stop producing
    /// region crops for exactly the rules being debugged.
    /// </summary>
    private static void SaveDebugFrame(ScriptExecutionContext context, Mat frame, int tickCount, string suffix, List<ResolvedRule> rules, int? matchedIndex = null)
    {
        try
        {
            Directory.CreateDirectory(context.FrameDebugPath!);
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            var baseFilename = $"frame_{timestamp}_{suffix}";

            // Save full window capture
            var fullPath = Path.Combine(context.FrameDebugPath!, $"{baseFilename}_full.png");
            frame.SaveImage(fullPath);

            // Save cropped regions for each rule (or only matched rule if specified)
            for (var i = 0; i < rules.Count; i++)
            {
                // If matchedIndex is specified, only save that rule's region
                if (matchedIndex.HasValue && i != matchedIndex.Value)
                {
                    continue;
                }

                var condition = rules[i].Condition;
                if (condition is null || !condition.HasRegion)
                {
                    continue;
                }

                // Same region maths the matcher uses, so the dump shows what was actually searched.
                using var roi = new Mat(frame, VisualConditionEvaluator.ResolveRegion(condition, frame, context));
                var regionPath = Path.Combine(context.FrameDebugPath!, $"{baseFilename}_rule{i}_region.png");
                roi.SaveImage(regionPath);
            }

            if (tickCount == 1)
            {
                var modeDesc = context.FrameDebugMode switch
                {
                    0 => "仅第一帧",
                    1 => "仅匹配帧",
                    2 => "所有帧",
                    _ => "未知模式"
                };
                context.Log($"  [DEBUG] 帧调试已启用 ({modeDesc})，保存到: {context.FrameDebugPath}");
                context.Log($"  [DEBUG] 每帧保存: 完整窗口 + 各规则的裁剪区域");
            }
        }
        catch (Exception ex)
        {
            context.Log($"  [DEBUG] 保存帧失败: {ex.Message}");
        }
    }
}
