using System.Globalization;
using Gw2LegendaryTracker.Core.Crafting;
using Gw2LegendaryTracker.Core.Goals;
using static Gw2LegendaryTracker.Tests.CraftingTestHelpers;

namespace Gw2LegendaryTracker.Tests;

public class GoalPresenterTests
{
    private const int A = 1;
    private const int B = 2;
    private const int C = 3;

    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    private static string Name(int id) => id switch { A => "Goal", B => "Part", C => "Raw", _ => $"#{id}" };

    private static GoalPlan Summaryable(CraftPlan plan, bool hasRecipeData = true, string? error = null) =>
        new(new Goal(A), plan, hasRecipeData, error);

    [Fact]
    public void An_owned_goal_says_so()
    {
        var plan = CraftingPlanner.Plan(new CraftGoal(A), Inventory([(A, 1)]), RecipeBook.Empty);

        var summary = GoalPresenter.Summarize(Summaryable(plan), Name, Culture);

        Assert.Equal("Je hebt deze Legendary al.", summary.NextStepText);
        Assert.Equal(1.0, summary.Progress);
    }

    [Fact]
    public void Crafting_names_the_discipline_for_api_recipes_and_the_forge_for_local_ones()
    {
        var api = Recipe(1, A, 1, (C, 1)) with { Disciplines = ["Weaponsmith", "Huntsman"] };
        var local = Recipe(-1, A, 1, (C, 1)) with { Source = RecipeSource.Local, Kind = RecipeKind.MysticForge };
        var inv = Inventory([(C, 1)]);

        var fromApi = GoalPresenter.Summarize(Summaryable(CraftingPlanner.Plan(new CraftGoal(A), inv, new RecipeBook([api]))), Name, Culture);
        var fromForge = GoalPresenter.Summarize(Summaryable(CraftingPlanner.Plan(new CraftGoal(A), inv, new RecipeBook([local]))), Name, Culture);

        Assert.Equal("Maak nu: 1× Goal (bij Weaponsmith of Huntsman)", fromApi.NextStepText);
        Assert.Equal("Maak nu: 1× Goal (in de Mystic Forge)", fromForge.NextStepText);
    }

    [Fact]
    public void Gathering_names_the_missing_item_and_amount()
    {
        var plan = CraftingPlanner.Plan(new CraftGoal(A), Inventory(), new RecipeBook([Recipe(1, A, 1, (C, 1500))]));

        var summary = GoalPresenter.Summarize(Summaryable(plan), Name, Culture);

        Assert.Equal("Verzamel: 1,500× Raw", summary.NextStepText);
        var shortage = Assert.Single(summary.Shortages);
        Assert.Equal((C, 1500, 1500), (shortage.ItemId, shortage.Missing, shortage.Needed));
    }

    [Fact]
    public void Progress_text_states_the_counts_behind_the_percentage()
    {
        // Positions: A, C. Nothing owned.
        var plan = CraftingPlanner.Plan(new CraftGoal(A), Inventory(), new RecipeBook([Recipe(1, A, 1, (C, 1))]));

        var summary = GoalPresenter.Summarize(Summaryable(plan), Name, Culture);

        Assert.EndsWith("0 van 2 onderdelen klaar", summary.ProgressText, StringComparison.Ordinal);
    }

    [Fact]
    public void A_goal_without_recipe_data_carries_a_notice()
    {
        var plan = CraftingPlanner.Plan(new CraftGoal(A), Inventory(), RecipeBook.Empty);

        var summary = GoalPresenter.Summarize(Summaryable(plan, hasRecipeData: false), Name, Culture);

        Assert.Equal(GoalPresenter.NoRecipeDataNotice, summary.Notice);
    }

    [Fact]
    public void A_plan_that_could_not_be_made_shows_its_error_and_no_numbers()
    {
        var summary = GoalPresenter.Summarize(new GoalPlan(new Goal(A), null, true, "Te groot."), Name, Culture);

        Assert.Equal("Te groot.", summary.Notice);
        Assert.Empty(summary.Shortages);
        Assert.Empty(summary.Checklist);
        Assert.Equal(0, summary.Progress);
    }

    [Fact]
    public void Checklist_status_distinguishes_missing_craftable_and_ticked()
    {
        var recipes = new RecipeBook([Recipe(1, A, 1, (B, 1), (C, 4)), Recipe(2, B, 1, (C, 1))]);
        var plan = CraftingPlanner.Plan(new CraftGoal(A) { Completed = new HashSet<int> { C } }, Inventory(), recipes);

        var lines = GoalPresenter.Summarize(Summaryable(plan), Name, Culture).Checklist;

        Assert.Equal([B, C], lines.Select(l => l.ItemId));
        Assert.Equal("Zelf afgevinkt", lines.Single(l => l.ItemId == C).StatusText);
        Assert.True(lines.Single(l => l.ItemId == C).IsDone);
    }

    [Fact]
    public void Referenced_ids_cover_everything_that_needs_a_name()
    {
        var plan = CraftingPlanner.Plan(new CraftGoal(A), Inventory([(C, 1)]), new RecipeBook([Recipe(1, A, 1, (B, 1), (C, 1)), Recipe(2, B, 1, (C, 1))]));

        var ids = GoalPresenter.ReferencedItemIds(Summaryable(plan));

        Assert.All(new[] { A, B, C }, id => Assert.Contains(id, ids));
    }

    [Theory]
    [InlineData(0, "Maak nu: 1× Goal")]
    [InlineData(1, "Maak nu: 1× Goal (bij Leatherworker)")]
    [InlineData(3, "Maak nu: 1× Goal (bij Leatherworker of Armorsmith of Tailor)")]
    [InlineData(7, "Maak nu: 1× Goal (bij o.a. Leatherworker, Armorsmith, Tailor en andere disciplines)")]
    public void A_long_list_of_disciplines_is_shortened(int count, string expected)
    {
        string[] all = ["Leatherworker", "Armorsmith", "Tailor", "Artificer", "Weaponsmith", "Scribe", "Huntsman"];
        var recipe = Recipe(1, A, 1, (C, 1)) with { Disciplines = [.. all.Take(count)] };

        var plan = CraftingPlanner.Plan(new CraftGoal(A), Inventory([(C, 1)]), new RecipeBook([recipe]));

        Assert.Equal(expected, GoalPresenter.Summarize(Summaryable(plan), Name, Culture).NextStepText);
    }
}
