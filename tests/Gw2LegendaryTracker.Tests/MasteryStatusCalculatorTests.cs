using Gw2LegendaryTracker.Core.Masteries;

namespace Gw2LegendaryTracker.Tests;

public sealed class MasteryStatusCalculatorTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    private static readonly Dictionary<string, bool> NoOverrides = [];

    private static MasteryAccountProgress Progress(int? skiffLevel = null, bool achievementDone = false) => new(
        At,
        skiffLevel is { } level ? [new AccountMastery(30, level)] : [],
        [],
        [],
        [new AchievementProgress(6000, achievementDone, achievementDone ? 1 : 0, 1)]);

    private static IReadOnlyDictionary<string, StepStatus> Compute(MasteryAccountProgress? progress, IReadOnlyDictionary<string, bool>? overrides = null) =>
        MasteryStatusCalculator.Compute(MasteryTestData.Plan(), progress, MasteryTestData.Definitions, overrides ?? NoOverrides);

    [Fact]
    public void A_trained_mastery_level_is_done_automatically()
    {
        var statuses = Compute(Progress(skiffLevel: 0));

        Assert.Equal(MasteryStepState.DoneAuto, statuses["s1"].State);
        Assert.True(statuses["s1"].IsTracked);
        Assert.False(statuses["s1"].IsOverridden);
        Assert.Equal(MasteryStepState.Available, statuses["s2"].State);
    }

    [Fact]
    public void An_untrained_mastery_level_is_available()
    {
        var statuses = Compute(Progress());

        Assert.Equal(MasteryStepState.Available, statuses["s1"].State);
        Assert.True(statuses["s1"].IsTracked);
    }

    [Fact]
    public void A_completed_achievement_is_done_automatically()
    {
        var statuses = Compute(Progress(achievementDone: true));

        Assert.Equal(MasteryStepState.DoneAuto, statuses["s3"].State);
    }

    [Fact]
    public void Without_account_data_nothing_is_tracked()
    {
        var statuses = Compute(progress: null);

        Assert.All(statuses.Values, s => Assert.False(s.IsTracked));
        Assert.Equal(MasteryStepState.Available, statuses["s1"].State);
    }

    [Fact]
    public void A_manual_tick_makes_a_step_done()
    {
        var statuses = Compute(Progress(), new Dictionary<string, bool> { ["s2"] = true });

        Assert.Equal(MasteryStepState.DoneManual, statuses["s2"].State);
        Assert.False(statuses["s2"].IsOverridden); // s2 is not tracked, so a tick is not an override
    }

    [Fact]
    public void A_manual_tick_overrides_an_open_tracked_step()
    {
        var statuses = Compute(Progress(), new Dictionary<string, bool> { ["s1"] = true });

        Assert.Equal(MasteryStepState.DoneManual, statuses["s1"].State);
        Assert.True(statuses["s1"].IsOverridden);
        Assert.False(statuses["s1"].AutoDone);
    }

    [Fact]
    public void A_manual_choice_can_reopen_an_automatically_done_step()
    {
        var statuses = Compute(Progress(skiffLevel: 1), new Dictionary<string, bool> { ["s1"] = false });

        Assert.Equal(MasteryStepState.Available, statuses["s1"].State);
        Assert.True(statuses["s1"].IsOverridden);
        Assert.True(statuses["s1"].AutoDone);
        Assert.Equal(MasteryStepState.Blocked, statuses["s2"].State);
    }

    [Fact]
    public void A_step_waits_for_its_required_steps()
    {
        var statuses = Compute(Progress());

        Assert.Equal(MasteryStepState.Blocked, statuses["s2"].State);
        Assert.Equal(["s1"], statuses["s2"].WaitingFor);
        Assert.Equal(MasteryStepState.Blocked, statuses["s3"].State);
        Assert.Equal(["s2"], statuses["s3"].WaitingFor);
    }

    [Fact]
    public void A_done_step_is_never_blocked()
    {
        var statuses = Compute(Progress(achievementDone: true));

        Assert.Equal(MasteryStepState.DoneAuto, statuses["s3"].State);
        Assert.Empty(statuses["s3"].WaitingFor);
    }

    [Fact]
    public void An_unknown_mastery_track_is_not_tracked()
    {
        var statuses = MasteryStatusCalculator.Compute(MasteryTestData.Plan(), Progress(skiffLevel: 1), definitions: null, NoOverrides);

        Assert.False(statuses["s1"].IsTracked);
        Assert.Equal(MasteryStepState.Available, statuses["s1"].State);
    }
}
