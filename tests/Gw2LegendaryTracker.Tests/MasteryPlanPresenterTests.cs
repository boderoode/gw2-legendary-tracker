using System.Globalization;
using System.Text.Json.Nodes;
using Gw2LegendaryTracker.Core.Masteries;

namespace Gw2LegendaryTracker.Tests;

public sealed class MasteryPlanPresenterTests
{
    private static readonly CultureInfo Dutch = CultureInfo.GetCultureInfo("nl-NL");

    private static readonly DateTimeOffset At = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    private static MasteryPlanSummary Summarize(MasteryPlan plan, MasteryAccountProgress? progress = null, Dictionary<string, bool>? overrides = null) =>
        MasteryPlanPresenter.Summarize(
            plan,
            MasteryStatusCalculator.Compute(plan, progress, MasteryTestData.Definitions, overrides ?? []),
            Dutch);

    [Fact]
    public void The_summary_counts_steps_and_time()
    {
        var summary = Summarize(MasteryTestData.Plan(), overrides: new() { ["s1"] = true });

        Assert.Equal(1, summary.DoneCount);
        Assert.Equal(3, summary.TotalCount);
        Assert.Equal("33% · 1/3 stappen", summary.ProgressText);
        Assert.Equal("33%", summary.PercentText);
        Assert.Equal("1/3 stappen", summary.CountText);
        Assert.Equal("Nog ongeveer 1 u, plus 1 stap zonder schatting", summary.RemainingText);
        Assert.Equal(["Jade"], summary.Regions);
        Assert.Equal("1/2 stappen", summary.Phases[0].ProgressText);
        Assert.Equal(0.5, summary.Phases[0].Progress);
    }

    [Fact]
    public void The_step_texts_describe_place_time_and_waiting()
    {
        var summary = Summarize(MasteryTestData.Plan());
        var first = summary.Phases[0].Steps[0];
        var second = summary.Phases[0].Steps[1];

        Assert.Equal("End of Dragons · Seitung Province", first.WhereText);
        Assert.Equal("30 min", first.TimeText);
        Assert.Equal("Beschikbaar", first.StatusText);
        Assert.Equal("Geblokkeerd", second.StatusText);
        Assert.Equal("Wacht op: Skiff level 1", second.WaitingText);
        Assert.Equal("1 u", second.TimeText);
    }

    [Fact]
    public void An_override_is_visible_in_the_status_text()
    {
        var progress = new MasteryAccountProgress(At, [new AccountMastery(30, 0)], [], [], []);

        var summary = Summarize(MasteryTestData.Plan(), progress, new() { ["s1"] = false });

        Assert.Equal("Beschikbaar · handmatig aangepast", summary.Phases[0].Steps[0].StatusText);
    }

    [Fact]
    public void The_next_step_prefers_priority_over_order()
    {
        var node = MasteryTestData.Node();
        MasteryTestData.Step(node, 0, 0)["priority"] = "low";
        MasteryTestData.Step(node, 0, 1)["requires"] = new JsonArray();
        MasteryTestData.Step(node, 0, 1)["priority"] = "high";
        var plan = MasteryPlanSerializer.Parse(node.ToJsonString(), MasteryTestData.Definitions).Plan!;

        var summary = Summarize(plan);

        Assert.Equal("s2", summary.NextStep!.Step.Id);
    }

    [Fact]
    public void The_next_step_follows_the_plan_order_within_a_priority()
    {
        var summary = Summarize(MasteryTestData.Plan(), overrides: new() { ["s1"] = true });

        Assert.Equal("s2", summary.NextStep!.Step.Id);
    }

    [Fact]
    public void When_everything_is_done_there_is_no_next_step()
    {
        var summary = Summarize(MasteryTestData.Plan(), overrides: new() { ["s1"] = true, ["s2"] = true, ["s3"] = true });

        Assert.Null(summary.NextStep);
        Assert.Equal("Alle stappen zijn klaar", summary.RemainingText);
        Assert.Equal("100% · 3/3 stappen", summary.ProgressText);
    }

    [Fact]
    public void The_open_filter_hides_done_steps_and_empty_phases()
    {
        var summary = Summarize(MasteryTestData.Plan(), overrides: new() { ["s1"] = true, ["s2"] = true });

        var filtered = MasteryPlanPresenter.Filter(summary.Phases, onlyOpen: true, region: null);

        var phase = Assert.Single(filtered);
        Assert.Equal("p2", phase.Phase.Id);
        Assert.Equal("1/1 stap", Summarize(MasteryTestData.Plan(), overrides: new() { ["s3"] = true }).Phases[1].ProgressText);
    }

    [Fact]
    public void The_region_filter_keeps_only_that_region()
    {
        var summary = Summarize(MasteryTestData.Plan());

        Assert.Empty(MasteryPlanPresenter.Filter(summary.Phases, onlyOpen: false, region: "Maguuma"));
        Assert.Equal(2, MasteryPlanPresenter.Filter(summary.Phases, onlyOpen: false, region: "jade").Count);
    }

    [Fact]
    public void The_region_overview_shows_points_and_tracks()
    {
        var progress = new MasteryAccountProgress(At, [new AccountMastery(30, 1)], [new RegionPoints("Jade", 12, 20)], [30], []);

        var regions = MasteryPlanPresenter.Regions(progress, MasteryTestData.Definitions, Dutch);

        Assert.Equal(["Maguuma", "Jade"], regions.Select(r => r.Region));
        Assert.Equal(["Heart of Thorns", "End of Dragons"], regions.Select(r => r.DisplayName));
        var jade = regions[1];
        Assert.Equal(20, jade.Earned);
        Assert.Equal(12, jade.Spent);
        Assert.Equal("20 punten verdiend, 12 uitgegeven", jade.PointsText);
        Assert.Equal(["Skiff 2/2", "Jade Bot 0/1"], jade.Tracks.Select(t => $"{t.Name} {t.Text}"));
        Assert.Equal("Nog geen punten", regions[0].PointsText);
    }

    [Fact]
    public void Without_account_data_the_points_are_unknown()
    {
        var regions = MasteryPlanPresenter.Regions(progress: null, MasteryTestData.Definitions, Dutch);

        Assert.All(regions, r => Assert.Equal("Punten onbekend", r.PointsText));
        Assert.All(regions.SelectMany(r => r.Tracks), t => Assert.Equal(0, t.Trained));
    }

    [Fact]
    public void Without_priorities_the_current_phase_is_the_first_with_open_steps()
    {
        Assert.Equal("p1", MasteryPlanPresenter.CurrentPhase(Summarize(MasteryTestData.Plan()))!.Phase.Id);
        Assert.Equal("p2", MasteryPlanPresenter.CurrentPhase(Summarize(MasteryTestData.Plan(), overrides: new() { ["s1"] = true, ["s2"] = true }))!.Phase.Id);
    }

    [Fact]
    public void A_later_phase_does_not_become_current_while_an_earlier_one_has_open_steps()
    {
        var summary = Summarize(MasteryTestData.Plan(), overrides: new() { ["s1"] = true, ["s3"] = true });

        Assert.Equal("p1", MasteryPlanPresenter.CurrentPhase(summary)!.Phase.Id);
    }

    [Fact]
    public void Reopening_an_automatically_done_step_makes_its_phase_current_again()
    {
        var progress = new MasteryAccountProgress(At, [new AccountMastery(30, 1)], [], [], []);
        var done = Summarize(MasteryTestData.Plan(), progress, new() { ["s2"] = true, ["s3"] = true });
        var reopened = Summarize(MasteryTestData.Plan(), progress, new() { ["s1"] = false, ["s2"] = true, ["s3"] = true });

        Assert.Null(MasteryPlanPresenter.CurrentPhase(done));
        Assert.Equal("p1", MasteryPlanPresenter.CurrentPhase(reopened)!.Phase.Id);
    }

    [Fact]
    public void The_current_phase_is_the_phase_of_the_next_step()
    {
        // A high-priority step in a later phase is the next step; the overlay must not show another phase beside it.
        var node = MasteryTestData.Node();
        MasteryTestData.Step(node, 0, 0)["priority"] = "low";
        MasteryTestData.Step(node, 1, 0)["requires"] = new JsonArray();
        MasteryTestData.Step(node, 1, 0)["priority"] = "high";
        var plan = MasteryPlanSerializer.Parse(node.ToJsonString(), MasteryTestData.Definitions).Plan!;

        var summary = Summarize(plan);

        Assert.Equal("s3", summary.NextStep!.Step.Id);
        Assert.Equal("p2", MasteryPlanPresenter.CurrentPhase(summary)!.Phase.Id);
    }

    [Fact]
    public void When_everything_is_done_there_is_no_current_phase()
    {
        var summary = Summarize(MasteryTestData.Plan(), overrides: new() { ["s1"] = true, ["s2"] = true, ["s3"] = true });

        Assert.Null(MasteryPlanPresenter.CurrentPhase(summary));
    }

    [Fact]
    public void The_overlay_lists_open_steps_first_and_done_steps_last()
    {
        var summary = Summarize(MasteryTestData.Plan(), overrides: new() { ["s1"] = true });

        Assert.Equal(["s2", "s1"], MasteryPlanPresenter.OverlayOrder(summary.Phases[0]));
        Assert.Equal(["s1", "s2"], MasteryPlanPresenter.OverlayOrder(Summarize(MasteryTestData.Plan()).Phases[0]));
    }

    [Theory]
    [InlineData(0, "0 min")]
    [InlineData(45, "45 min")]
    [InlineData(60, "1 u")]
    [InlineData(200, "3 u 20 min")]
    public void Minutes_are_formatted(int minutes, string expected) =>
        Assert.Equal(expected, MasteryPlanPresenter.FormatMinutes(minutes, Dutch));
}
