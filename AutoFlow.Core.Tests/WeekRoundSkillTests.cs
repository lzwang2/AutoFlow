using AutoFlow.Core.Scripting;
using AutoFlow.Core.Skills;
using AutoFlow.Core.Skills.LastCloudia.SinglePlayerCore;
using AutoFlow.Core.Skills.LastCloudia.WeekRound;
using Xunit;

namespace AutoFlow.Core.Tests;

public sealed class WeekRoundSkillTests
{
    private sealed class RecordingSkill : ISkill
    {
        public string Name => SinglePlayerCoreSkill.SkillName;
        public string Description => "test";
        public List<Dictionary<string, object>?> Calls { get; } = [];
        public Func<int, CancellationToken, Task>? OnCall { get; init; }

        public async Task ExecuteAsync(ScriptExecutionContext context, Dictionary<string, object>? parameters, CancellationToken ct)
        {
            Calls.Add(parameters);
            if (OnCall is not null) await OnCall(Calls.Count, ct);
        }
    }

    private static ScriptExecutionContext Context(List<string>? logs = null, List<string>? progress = null) => new()
    {
        Capture = null!, Input = null!, CoordinateTransform = null!, WindowInfo = null!, TemplateMatch = null!,
        Logger = logs is null ? null : logs.Add,
        ProgressReporter = progress is null ? null : progress.Add
    };

    [Fact]
    public async Task CallsSinglePlayerCoreExactlyFourTimesWithFixedParameters()
    {
        var core = new RecordingSkill();
        var logs = new List<string>();
        var progress = new List<string>();
        await new WeekRoundSkill(core).ExecuteAsync(Context(logs, progress), null, default);

        Assert.Equal(4, core.Calls.Count);
        Assert.All(core.Calls, parameters =>
        {
            Assert.NotNull(parameters);
            Assert.Equal(true, parameters!["again"]);
            Assert.Equal(1, parameters["last_step"]);
            Assert.Equal(2, parameters.Count);
        });
        Assert.Contains("WeekRound: 开始第 1/4 次单人核心调用", logs);
        Assert.Contains("WeekRound: 完成第 4/4 次单人核心调用", logs);
        Assert.Equal(new[] { "第 1/4 次单人核心调用", "第 2/4 次单人核心调用",
            "第 3/4 次单人核心调用", "第 4/4 次单人核心调用" }, progress);
    }

    [Fact]
    public async Task FailureOrCancellationStopsRemainingCalls()
    {
        var failed = new RecordingSkill
        {
            OnCall = (index, _) => index == 2
                ? Task.FromException(new InvalidOperationException("round failed"))
                : Task.CompletedTask
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new WeekRoundSkill(failed).ExecuteAsync(Context(), null, default));
        Assert.Equal(2, failed.Calls.Count);

        using var cancellation = new CancellationTokenSource();
        var cancelled = new RecordingSkill
        {
            OnCall = (index, _) =>
            {
                if (index == 2) cancellation.Cancel();
                return Task.CompletedTask;
            }
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new WeekRoundSkill(cancelled).ExecuteAsync(Context(), null, cancellation.Token));
        Assert.Equal(2, cancelled.Calls.Count);
    }

    [Fact]
    public async Task RejectsParametersBecauseRoundCountIsNotConfigurable()
    {
        var core = new RecordingSkill();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new WeekRoundSkill(core).ExecuteAsync(Context(), new() { ["rounds"] = 5 }, default));
        Assert.Empty(core.Calls);
    }

    [Fact]
    public void BuiltInSkillsAreRegisteredAndWeekRoundExposesCoreDependencies()
    {
        Assert.NotNull(SkillRegistry.Instance.GetSkill(SinglePlayerCoreSkill.SkillName));
        var weekRound = Assert.IsType<WeekRoundSkill>(SkillRegistry.Instance.GetSkill(WeekRoundSkill.SkillName));
        var core = new SinglePlayerCoreSkill();
        Assert.Equal(core.SharedResources, weekRound.SharedResources);
        Assert.Equal(core.SharedConditions, weekRound.SharedConditions);
        Assert.Equal(core.RequiredFiles, weekRound.RequiredFiles);
    }
}
