using AutoFlow.Core.Models;
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

    public RuleLoopInterpreter(InstructionHandlerRegistry handlerRegistry)
    {
        _handlerRegistry = handlerRegistry;
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
                    SaveDebugFrame(context, frame, tickCount, "full", script.Rules);
                }
            }

            var matchedIndex = EvaluateRules(script, context, frame, firedOnce, tickCount == 1);

            if (matchedIndex < 0)
            {
                // Debug: save no-match frame if mode is 2 (no-match only) - only once per loop session
                if (context.EnableFrameDebug && context.FrameDebugMode == 2 && !string.IsNullOrEmpty(context.FrameDebugPath) && !noMatchFrameSaved)
                {
                    SaveDebugFrame(context, frame, tickCount, "nomatch", script.Rules);
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
                SaveDebugFrame(context, frame, tickCount, "matched", script.Rules, matchedIndex);
            }

            if (rule.TriggerOnce)
            {
                firedOnce.Add(matchedIndex);
            }

            await RunActionsAsync(rule, context, cancellationToken);
        }
    }

    /// <summary>
    /// Returns the index of the first matching rule, or -1 if none matched.
    /// Rules without a TemplatePath are unconditional (always match) and serve as fallback/catch-all.
    /// </summary>
    private int EvaluateRules(RuleScript script, ScriptExecutionContext context, Mat frame, HashSet<int> firedOnce, bool verbose = false)
    {
        for (var i = 0; i < script.Rules.Count; i++)
        {
            var rule = script.Rules[i];
            if (!rule.Enabled)
            {
                continue;
            }
            if (rule.TriggerOnce && firedOnce.Contains(i))
            {
                continue;
            }

            // Unconditional rule (no TemplatePath) always matches (fallback/catch-all semantics)
            if (string.IsNullOrWhiteSpace(rule.TemplatePath))
            {
                return i;
            }

            // Conditional rule: check image match
            if (verbose)
            {
                context.Log($"  [DEBUG] 尝试匹配规则 #{i} '{rule.Name}', 模板: {rule.TemplatePath}");
            }
            if (IsConditionMet(rule, context, frame))
            {
                return i;
            }
        }
        return -1;
    }

    private static bool IsConditionMet(Rule rule, ScriptExecutionContext context, Mat frame)
    {
        var options = new TemplateMatchOptions
        {
            Method = TemplateMatchModes.CCoeffNormed,
            UseMask = false
        };

        // Debug: log template path resolution
        if (!File.Exists(rule.TemplatePath))
        {
            context.Log($"  [DEBUG] 模板文件不存在: {rule.TemplatePath}");
            return false;
        }

        // Region-scoped match: crop the frame to the rule's region (converted from the
        // script's reference coordinate space to actual window/frame pixels) before matching.
        if (rule.HasRegion)
        {
            var windowInfo = context.GetWindowInfo();
            var topLeft = context.CoordinateTransform.ScriptToWindow(rule.RegionX1, rule.RegionY1, windowInfo);
            var bottomRight = context.CoordinateTransform.ScriptToWindow(rule.RegionX2, rule.RegionY2, windowInfo);

            var x = Math.Clamp(Math.Min(topLeft.X, bottomRight.X), 0, frame.Width - 1);
            var y = Math.Clamp(Math.Min(topLeft.Y, bottomRight.Y), 0, frame.Height - 1);
            var right = Math.Clamp(Math.Max(topLeft.X, bottomRight.X), x + 1, frame.Width);
            var bottom = Math.Clamp(Math.Max(topLeft.Y, bottomRight.Y), y + 1, frame.Height);

            using var roi = new Mat(frame, new Rect(x, y, right - x, bottom - y));
            var regionMatch = context.TemplateMatch.TryFindTemplate(
                roi, rule.TemplatePath!, options, rule.MatchThreshold, rule.MeanDiffThreshold);
            return regionMatch != null;
        }

        var match = context.TemplateMatch.TryFindTemplate(
            frame, rule.TemplatePath!, options, rule.MatchThreshold, rule.MeanDiffThreshold);
        return match != null;
    }

    private async Task RunActionsAsync(Rule rule, ScriptExecutionContext context, CancellationToken cancellationToken)
    {
        for (var i = 0; i < rule.Actions.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var action = rule.Actions[i];
            var handler = _handlerRegistry.GetHandler(action.CommandType);
            if (handler == null)
            {
                context.Log($"  跳过动作 {i}: 无处理器 {action.CommandType}");
                continue;
            }

            // Loop jumps are meaningless inside a rule's action list; ignore them.
            context.JumpToIndex = null;

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

            if (action.IntervalToNextInstructionMs > 0)
            {
                await Task.Delay(action.IntervalToNextInstructionMs, cancellationToken);
            }
        }
    }

    private static void SaveDebugFrame(ScriptExecutionContext context, Mat frame, int tickCount, string suffix, List<Rule> rules, int? matchedIndex = null)
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
            var windowInfo = context.GetWindowInfo();
            for (var i = 0; i < rules.Count; i++)
            {
                // If matchedIndex is specified, only save that rule's region
                if (matchedIndex.HasValue && i != matchedIndex.Value)
                {
                    continue;
                }

                var rule = rules[i];
                if (!rule.HasRegion || string.IsNullOrWhiteSpace(rule.TemplatePath))
                {
                    continue;
                }

                // Transform and crop region
                var topLeft = context.CoordinateTransform.ScriptToWindow(rule.RegionX1, rule.RegionY1, windowInfo);
                var bottomRight = context.CoordinateTransform.ScriptToWindow(rule.RegionX2, rule.RegionY2, windowInfo);

                var x = Math.Clamp(Math.Min(topLeft.X, bottomRight.X), 0, frame.Width - 1);
                var y = Math.Clamp(Math.Min(topLeft.Y, bottomRight.Y), 0, frame.Height - 1);
                var right = Math.Clamp(Math.Max(topLeft.X, bottomRight.X), x + 1, frame.Width);
                var bottom = Math.Clamp(Math.Max(topLeft.Y, bottomRight.Y), y + 1, frame.Height);

                using var roi = new Mat(frame, new Rect(x, y, right - x, bottom - y));
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
