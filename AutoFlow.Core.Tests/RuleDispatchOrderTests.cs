using AutoFlow.Core.Capture;
using AutoFlow.Core.Input;
using AutoFlow.Core.Scripting;
using AutoFlow.Core.Scripting.Handlers;
using AutoFlow.Core.Scripting.Instructions;
using AutoFlow.Core.Scripting.Rules;
using AutoFlow.Core.Vision;
using Fischless.GameCapture;
using Fischless.WindowsInput;
using OpenCvSharp;
using Xunit;

namespace AutoFlow.Core.Tests;

/// <summary>
/// Interpreter-level dispatch tests (Plan 建议测试项 6 和 12): list order decides which rule
/// fires, and a rule with no <c>ConditionRef</c> still matches unconditionally.
///
/// <para>Every rule here is a catch-all, which is what makes the test runnable without a real
/// window: a null condition returns from <c>EvaluateRules</c> before any template match or
/// coordinate transform, so <see cref="WindowInfoService"/> is never consulted. Region-scoped
/// matching needs a live window and stays in the manual pass.</para>
/// </summary>
public class RuleDispatchOrderTests
{
    /// <summary>Returns a fixed black frame — never inspected, since no rule has a condition.</summary>
    private sealed class StubCapture : IGameCapture
    {
        public bool IsCapturing => true;
        public void Start(nint hWnd, Dictionary<string, object>? settings = null) { }
        public Mat? Capture() => new(64, 64, MatType.CV_8UC3, Scalar.Black);
        public void Stop() { }
        public void Dispose() { }
    }

    /// <summary>
    /// Throws instead of returning a no-op simulator: a dispatch test that reaches real input
    /// has stopped testing dispatch, and should fail loudly rather than silently pass.
    /// </summary>
    private sealed class UnusedInputSimulator : IInputSimulator
    {
        public IKeyboardSimulator Keyboard => throw new NotSupportedException("调度测试不应触发键盘输入");
        public IMouseSimulator Mouse => throw new NotSupportedException("调度测试不应触发鼠标输入");
        public IInputDeviceStateAdaptor InputDeviceState => throw new NotSupportedException("调度测试不应查询输入设备");
    }

    /// <summary>Stands in for the Comment handler and records which rule's action ran.</summary>
    private sealed class RecordingHandler : IInstructionHandler
    {
        private readonly Action<string> _record;
        public RecordingHandler(Action<string> record) => _record = record;

        public CommandType CommandType => CommandType.Comment;

        public Task ExecuteAsync(Instruction instruction, ScriptExecutionContext context, CancellationToken cancellationToken)
        {
            _record(instruction.CommentContent ?? string.Empty);
            return Task.CompletedTask;
        }
    }

    /// <summary>A catch-all rule whose single action records <paramref name="name"/>.</summary>
    private static Rule CatchAll(string name, bool enabled = true, bool triggerOnce = false) => new()
    {
        Name = name,
        Enabled = enabled,
        TriggerOnce = triggerOnce,
        ConditionRef = null,
        Actions =
        [
            new Instruction
            {
                CommandType = CommandType.Comment,
                CommentContent = name,
                IntervalToNextInstructionMs = 0
            }
        ]
    };

    /// <summary>
    /// Runs the dispatch loop until <paramref name="expectedHits"/> actions have fired, then
    /// cancels, and returns the rule names in the order they fired.
    /// </summary>
    private static async Task<List<string>> RunAsync(RuleScript script, int expectedHits)
    {
        var hits = new List<string>();
        using var cts = new CancellationTokenSource();

        // Backstop: if dispatch never fires, the test must fail on an assertion rather than
        // hang the suite on an infinite loop.
        cts.CancelAfter(TimeSpan.FromSeconds(10));

        var registry = new InstructionHandlerRegistry();
        registry.Register(new RecordingHandler(name =>
        {
            hits.Add(name);
            if (hits.Count >= expectedHits) cts.Cancel();
        }));

        using var capture = new StubCapture();
        var context = new ScriptExecutionContext
        {
            Capture = capture,
            Input = new InputService(new UnusedInputSimulator()),
            CoordinateTransform = new CoordinateTransformService(),
            WindowInfo = new WindowInfoService(),
            TemplateMatch = TemplateMatchService.Instance
        };

        // No condition library on purpose: an unconditional rule must not need one (项 12).
        var interpreter = new RuleLoopInterpreter(registry);

        try
        {
            await interpreter.ExecuteAsync(script, context, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected: the loop only ends by cancellation.
        }

        return hits;
    }

    [Fact]
    public async Task UnconditionalRuleMatchesWithoutAnyConditionLibrary()
    {
        // 项 12：ConditionRef 为空的 catch-all 仍然无条件命中。
        var script = new RuleScript
        {
            IdleTickMs = 1,
            Rules = [CatchAll("兜底")]
        };

        var hits = await RunAsync(script, expectedHits: 1);

        Assert.Equal(["兜底"], hits);
    }

    [Fact]
    public async Task FirstEnabledRuleInListOrderWins()
    {
        // 项 6：命中由列表顺序决定，而不是由名称、Id 或任何排序规则决定。
        var script = new RuleScript
        {
            IdleTickMs = 1,
            Rules = [CatchAll("第一"), CatchAll("第二"), CatchAll("第三")]
        };

        var hits = await RunAsync(script, expectedHits: 3);

        // 每个 tick 都命中同一条：后面的规则永远轮不到。
        Assert.Equal(["第一", "第一", "第一"], hits);
    }

    [Fact]
    public async Task ReorderingRulesChangesTheHit()
    {
        // 同一组规则换个顺序就换命中对象——证明上一个测试测的是顺序，不是巧合。
        var script = new RuleScript
        {
            IdleTickMs = 1,
            Rules = [CatchAll("第三"), CatchAll("第二"), CatchAll("第一")]
        };

        var hits = await RunAsync(script, expectedHits: 1);

        Assert.Equal(["第三"], hits);
    }

    [Fact]
    public async Task DisabledRuleIsSkippedWithoutShiftingTheRest()
    {
        var script = new RuleScript
        {
            IdleTickMs = 1,
            Rules = [CatchAll("停用", enabled: false), CatchAll("启用")]
        };

        var hits = await RunAsync(script, expectedHits: 1);

        Assert.Equal(["启用"], hits);
    }

    [Fact]
    public async Task TriggerOnceRuleYieldsToTheNextRuleAfterFiring()
    {
        // TriggerOnce 命中后退出候选，顺序语义交给下一条——两者必须同时成立。
        var script = new RuleScript
        {
            IdleTickMs = 1,
            Rules = [CatchAll("只触发一次", triggerOnce: true), CatchAll("常驻")]
        };

        var hits = await RunAsync(script, expectedHits: 3);

        Assert.Equal(["只触发一次", "常驻", "常驻"], hits);
    }
}
