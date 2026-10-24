using System.Globalization;
using Gw2LegendaryTracker.Core.Crafting;
using Gw2LegendaryTracker.Core.Goals;
using Gw2LegendaryTracker.Core.State;
using static Gw2LegendaryTracker.Tests.CraftingTestHelpers;

namespace Gw2LegendaryTracker.Tests;

public class CoverageTests
{
    [Theory]
    [InlineData(0, 100, 0.0)]
    [InlineData(25, 100, 0.25)]
    [InlineData(100, 100, 1.0)]
    [InlineData(250, 100, 1.0)]
    [InlineData(-5, 100, 0.0)]
    [InlineData(0, 0, 1.0)]
    [InlineData(5, -1, 1.0)]
    public void The_ratio_stays_between_zero_and_one_and_nothing_needed_counts_as_covered(long covered, long needed, double expected)
    {
        Assert.Equal(expected, Coverage.Ratio(covered, needed), 6);
    }

    [Fact]
    public void A_shortage_shows_how_much_is_already_covered()
    {
        var line = new ShortageLine(ItemId: 1, Missing: 130, Needed: 250, Owned: 120);

        Assert.Equal(120, line.Covered);
        Assert.Equal(0.48, line.Ratio, 6);
    }

    [Fact]
    public void Coverage_never_goes_negative_when_more_is_missing_than_needed()
    {
        Assert.Equal(0, new ShortageLine(1, 300, 250, 0).Covered);
        Assert.Equal(0, new MaterialRow(1, 250, 0, 300).Covered);
    }

    [Fact]
    public void A_material_that_is_fully_covered_has_a_full_bar()
    {
        var row = new MaterialRow(ItemId: 1, Needed: 250, Owned: 900, Missing: 0);

        Assert.Equal(250, row.Covered);
        Assert.Equal(1.0, row.Ratio);
    }

    [Fact]
    public void A_wallet_larger_than_the_need_is_fully_covered_and_a_smaller_one_is_a_share()
    {
        Assert.Equal(1.0, new CurrencyRow(2, 6_225, 194_832, 0).Ratio);
        Assert.Equal(0.4, new CurrencyRow(2, 250, 100, 150).Ratio, 6);
        Assert.Equal(0.0, new CurrencyRow(2, 250, 0, 250).Ratio);
    }
}

public class SyncStatusPresenterTests
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
    private static readonly DateTimeOffset At = new(2026, 10, 24, 12, 41, 0, TimeSpan.Zero);

    [Fact]
    public void The_short_text_fits_in_a_corner_for_every_state()
    {
        Assert.Equal("Bijwerken…", SyncStatusPresenter.ShortText(new SyncStatus(SyncPhase.Syncing, At), true, Culture));
        Assert.Equal("Bijwerken mislukt", SyncStatusPresenter.ShortText(new SyncStatus(SyncPhase.Failed, At, "Geen verbinding."), true, Culture));
        Assert.Equal("Bijwerken mislukt", SyncStatusPresenter.ShortText(new SyncStatus(SyncPhase.Failed), true, Culture));
        Assert.StartsWith("Bijgewerkt ", SyncStatusPresenter.ShortText(new SyncStatus(SyncPhase.Succeeded, At), true, Culture), StringComparison.Ordinal);
        Assert.Equal("Geen API-key", SyncStatusPresenter.ShortText(SyncStatus.Initial, false, Culture));
        Assert.Equal("Nog niet bijgewerkt", SyncStatusPresenter.ShortText(SyncStatus.Initial, true, Culture));
    }

    [Fact]
    public void A_failure_is_short_in_the_corner_but_complete_in_the_long_text()
    {
        var failed = new SyncStatus(SyncPhase.Failed, At, "Geen verbinding.");

        Assert.DoesNotContain("verbinding", SyncStatusPresenter.ShortText(failed, true, Culture), StringComparison.Ordinal);
        Assert.Equal("Synchronisatie mislukt: Geen verbinding.", SyncStatusPresenter.LongText(failed, true, Culture));
    }

    [Fact]
    public void The_long_text_keeps_the_wording_the_main_window_always_had()
    {
        Assert.Equal("Synchroniseren…", SyncStatusPresenter.LongText(new SyncStatus(SyncPhase.Syncing), true, Culture));
        Assert.Equal("Synchronisatie mislukt", SyncStatusPresenter.LongText(new SyncStatus(SyncPhase.Failed), true, Culture));
        Assert.StartsWith("Laatst gesynchroniseerd: ", SyncStatusPresenter.LongText(new SyncStatus(SyncPhase.Succeeded, At), true, Culture), StringComparison.Ordinal);
        Assert.Equal("Geen API-key ingesteld", SyncStatusPresenter.LongText(SyncStatus.Initial, false, Culture));
        Assert.Equal("Nog niet gesynchroniseerd", SyncStatusPresenter.LongText(SyncStatus.Initial, true, Culture));
    }

    [Fact]
    public void An_earlier_success_stays_visible_while_the_next_sync_runs_or_when_there_is_no_key_anymore()
    {
        // The last known time wins over "no key": the data on screen is still from that moment.
        var text = SyncStatusPresenter.ShortText(new SyncStatus(SyncPhase.Succeeded, At), hasApiKey: false, Culture);

        Assert.StartsWith("Bijgewerkt ", text, StringComparison.Ordinal);
    }
}

public class OverlaySummaryTests
{
    private const int A = 1;
    private const int B = 2;
    private const int C = 3;

    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    private static string Name(int id) => $"Item{id}";

    private static GoalSummary Summary(CraftPlan plan) => GoalPresenter.Summarize(new GoalPlan(new Goal(A), plan, true, null), Name, Culture);

    [Fact]
    public void The_percentage_and_the_count_are_separate_so_the_screen_can_give_the_number_its_own_place()
    {
        // Positions: A, B, C. Only C is owned: 1 of 3.
        var plan = CraftingPlanner.Plan(new CraftGoal(A), Inventory([(C, 1)]), new RecipeBook([Recipe(1, A, 1, (B, 1), (C, 1))]));

        var summary = Summary(plan);

        Assert.Equal(1.0 / 3, summary.Progress, 6);
        Assert.Equal(plan.Progress.ToString("P0", Culture), summary.PercentText);
        Assert.Equal("1 van 3 onderdelen klaar", summary.PartsText);
        Assert.Equal($"{summary.PercentText} · {summary.PartsText}", summary.ProgressText);
    }

    [Fact]
    public void A_plan_that_could_not_be_made_has_no_numbers()
    {
        var summary = GoalPresenter.Summarize(new GoalPlan(new Goal(A), null, true, "Te groot."), Name, Culture);

        Assert.Empty(summary.PercentText);
        Assert.Empty(summary.PartsText);
        Assert.Null(summary.NextItemId);
    }

    [Fact]
    public void The_next_step_names_the_item_to_make_so_its_icon_can_be_shown()
    {
        var plan = CraftingPlanner.Plan(new CraftGoal(A), Inventory([(B, 1)]), new RecipeBook([Recipe(1, A, 1, (B, 1))]));

        Assert.Equal(A, Summary(plan).NextItemId);
    }

    [Fact]
    public void The_next_step_names_the_item_to_gather()
    {
        var plan = CraftingPlanner.Plan(new CraftGoal(A), Inventory(), new RecipeBook([Recipe(1, A, 1, (B, 5))]));

        Assert.Equal(B, Summary(plan).NextItemId);
    }

    [Fact]
    public void A_currency_or_a_finished_goal_has_no_item_icon()
    {
        var currencyPlan = CraftingPlanner.Plan(
            new CraftGoal(A),
            Inventory(),
            new RecipeBook([WithCurrency(Recipe(1, A, 1, (B, 1)), currencyId: 2, count: 25)]));
        var owned = CraftingPlanner.Plan(new CraftGoal(A), Inventory([(A, 1)]), RecipeBook.Empty);

        Assert.Equal(B, Summary(currencyPlan).NextItemId);
        Assert.Null(Summary(owned).NextItemId);
    }
}
