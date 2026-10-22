using Gw2LegendaryTracker.Core.Crafting;
using static Gw2LegendaryTracker.Tests.CraftingTestHelpers;

namespace Gw2LegendaryTracker.Tests;

public class ManualCompletionTests
{
    private const int A = 1;
    private const int B = 2;
    private const int C = 3;
    private const int D = 4;

    private static CraftPlan Plan(IReadOnlyCollection<int> completed, Gw2LegendaryTracker.Core.Inventory.InventoryModel inventory, params Recipe[] recipes) =>
        CraftingPlanner.Plan(new PlanRequest([new CraftGoal(A) { Completed = completed.ToHashSet() }], inventory, new RecipeBook(recipes)))[0];

    [Fact]
    public void Ticked_component_counts_as_owned_and_its_ingredients_are_not_needed()
    {
        var plan = Plan([B], Inventory(), Recipe(1, A, 1, (B, 1), (C, 1)), Recipe(2, B, 1, (D, 9)));

        Assert.DoesNotContain(plan.Shortages, s => s.Key.Id == D);
        Assert.Equal(1, plan.Shortages.Single().Missing);
        Assert.True(plan.Root.Children[0].IsManual);
        Assert.True(plan.Root.Children[0].IsCoveredByStock);
    }

    [Fact]
    public void Ticking_does_not_use_up_real_stock()
    {
        var plan = Plan([B], Inventory([(B, 5)]), Recipe(1, A, 1, (B, 1)));

        Assert.Equal(0, Used(plan, MaterialKey.Item(B)));
    }

    [Fact]
    public void Ticking_the_goal_itself_has_no_effect()
    {
        var plan = Plan([A], Inventory(), Recipe(1, A, 1, (B, 1)));

        Assert.False(plan.Root.IsManual);
        Assert.Equal(1, Missing(plan, MaterialKey.Item(B)));
    }

    [Fact]
    public void Ticked_components_count_towards_progress_with_their_hidden_positions()
    {
        // Positions: A, B, D (below B), C = 4. Ticking B satisfies B and D.
        var plan = Plan([B], Inventory(), Recipe(1, A, 1, (B, 1), (C, 1)), Recipe(2, B, 1, (D, 1)));

        Assert.Equal(0.5, plan.Progress, 6);
    }

    [Fact]
    public void Ticking_all_components_makes_the_goal_craftable_now()
    {
        var plan = Plan([B, C], Inventory(), Recipe(1, A, 1, (B, 1), (C, 1)));

        Assert.Equal(NextStepKind.Craft, plan.Next.Kind);
        Assert.Equal(1.0, plan.Progress);
    }

    [Fact]
    public void Ticking_a_leaf_that_cannot_be_crafted_clears_its_shortage()
    {
        var plan = Plan([C], Inventory(), Recipe(1, A, 1, (C, 5)));

        Assert.Empty(plan.Shortages);
    }

    [Fact]
    public void Checklist_lists_crafted_and_missing_components_once_each_and_keeps_ticked_ones()
    {
        // C is needed twice (once for B, once for A), so two are owned.
        var plan = Plan([D], Inventory([(C, 2)]), Recipe(1, A, 1, (B, 1), (C, 1), (D, 1)), Recipe(2, B, 1, (C, 1), (D, 1)));

        var entries = ChecklistBuilder.Build(plan);

        // B is crafted; C is owned (not listed); D is ticked and stays listed.
        Assert.Equal([B, D], entries.Select(e => e.ItemId));
        Assert.True(entries[0].IsCraftable);
        Assert.False(entries[0].IsDone);
        Assert.True(entries[1].IsDone);
    }

    [Fact]
    public void Checklist_includes_missing_leaves_and_is_empty_when_everything_is_owned()
    {
        var missing = Plan([], Inventory(), Recipe(1, A, 1, (B, 3)));
        Assert.Equal([B], ChecklistBuilder.Build(missing).Select(e => e.ItemId));
        Assert.Equal(3, ChecklistBuilder.Build(missing)[0].Missing);

        var owned = Plan([], Inventory([(B, 3)]), Recipe(1, A, 1, (B, 3)));
        Assert.Empty(ChecklistBuilder.Build(owned));
    }

    [Fact]
    public void Checklist_never_lists_the_goal_itself()
    {
        var plan = Plan([], Inventory(), Recipe(1, A, 1, (B, 1)));

        Assert.DoesNotContain(ChecklistBuilder.Build(plan), e => e.ItemId == A);
    }
}
