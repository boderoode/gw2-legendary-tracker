using Gw2LegendaryTracker.Core.Crafting;
using static Gw2LegendaryTracker.Tests.CraftingTestHelpers;

namespace Gw2LegendaryTracker.Tests;

public class CraftingPlannerTests
{
    private const int A = 1;
    private const int B = 2;
    private const int C = 3;
    private const int D = 4;
    private const int E = 5;

    private static MaterialKey Item(int id) => MaterialKey.Item(id);

    [Fact]
    public void Owned_goal_is_done_without_any_crafting()
    {
        var plan = Plan(A, Inventory([(A, 1)]), Recipe(100, A, 1, (B, 1)));

        Assert.Equal(NextStepKind.Done, plan.Next.Kind);
        Assert.Equal(1.0, plan.Progress);
        Assert.Empty(plan.Steps);
        Assert.Empty(plan.Shortages);
    }

    [Fact]
    public void All_ingredients_owned_makes_the_craft_ready()
    {
        var plan = Plan(A, Inventory([(B, 2), (C, 1)]), Recipe(100, A, 1, (B, 2), (C, 1)));

        var step = Assert.Single(plan.Steps);
        Assert.True(step.IsReady);
        Assert.Equal(1, step.Times);
        Assert.Equal(NextStepKind.Craft, plan.Next.Kind);
        Assert.Equal(100, plan.Next.Craft?.Recipe.Id);
        Assert.Equal(1.0, plan.Progress);
        Assert.Equal(2, Used(plan, Item(B)));
        Assert.Empty(plan.Shortages);
    }

    [Fact]
    public void Partly_owned_ingredient_is_reported_as_shortage_and_next_step_is_to_gather_it()
    {
        var plan = Plan(A, Inventory([(B, 1), (C, 1)]), Recipe(100, A, 1, (B, 2), (C, 1)));

        Assert.Equal(1, Missing(plan, Item(B)));
        Assert.Equal(1, Used(plan, Item(B)));
        Assert.Equal(NextStepKind.Gather, plan.Next.Kind);
        Assert.Equal(Item(B), plan.Next.Material);
        Assert.Equal(1, plan.Next.Amount);
        Assert.False(Assert.Single(plan.Steps).IsReady);

        // 3 positions (A, B, C); only C is satisfied.
        Assert.Equal(1.0 / 3, plan.Progress, 6);
    }

    [Fact]
    public void Intermediate_items_are_crafted_recursively()
    {
        // A needs 2 B; each B needs 3 C. Owning 4 C leaves 2 of the 6 C missing.
        var plan = Plan(A, Inventory([(C, 4)]), Recipe(100, A, 1, (B, 2)), Recipe(101, B, 1, (C, 3)));

        Assert.Equal(2, Missing(plan, Item(C)));
        Assert.Equal(4, Used(plan, Item(C)));
        Assert.Equal([101L, 100L], plan.Steps.Select(s => (long)s.Recipe.Id));
        Assert.Equal(2, plan.Steps[0].Times);
        Assert.Equal(0.0, plan.Progress);
    }

    [Fact]
    public void Recipe_output_count_rounds_crafts_up_and_surplus_is_reused()
    {
        // B comes 2 per craft from 3 C. A needs 3 B plus a D, and D needs another B.
        // 3 B means 2 crafts (4 B); the spare B covers D's B, so 2 crafts (6 C) suffice instead of 3 crafts (9 C).
        var plan = Plan(
            A,
            Inventory(),
            Recipe(100, A, 1, (B, 3), (D, 1)),
            Recipe(101, B, 2, (C, 3)),
            Recipe(102, D, 1, (B, 1)));

        Assert.Equal(2, plan.Root.Children[0].Crafts);
        Assert.Equal(6, Missing(plan, Item(C)));
        Assert.Equal(1, Used(plan, Item(B)));
    }

    [Fact]
    public void Fewest_missing_units_wins_between_routes()
    {
        var recipes = new[] { Recipe(1, A, 1, (B, 5)), Recipe(2, A, 1, (C, 1)) };

        var ownsB = Plan(A, Inventory([(B, 5)]), recipes);
        var ownsNothing = Plan(A, Inventory(), recipes);

        Assert.Equal(1, ownsB.Root.Recipe?.Id);
        Assert.Equal(2, ownsNothing.Root.Recipe?.Id);
        Assert.Equal([true, false], ownsB.Root.Alternatives.Select(o => o.IsChosen));
        Assert.Equal([0L, 1L], ownsB.Root.Alternatives.Select(o => o.MissingUnits));
    }

    [Fact]
    public void Equal_missing_units_prefer_fewer_crafts_then_lower_recipe_id()
    {
        // Route 1 needs Y, which must itself be crafted from B (2 crafts); route 2 uses B directly (1 craft).
        var viaCraft = Plan(A, Inventory(), Recipe(1, A, 1, (D, 1)), Recipe(2, A, 1, (B, 1)), Recipe(3, D, 1, (B, 1)));
        Assert.Equal(2, viaCraft.Root.Recipe?.Id);

        var tie = Plan(A, Inventory(), Recipe(7, A, 1, (B, 1)), Recipe(8, A, 1, (C, 1)));
        Assert.Equal(7, tie.Root.Recipe?.Id);
    }

    [Fact]
    public void Route_override_forces_a_recipe_even_when_another_is_better()
    {
        var book = new RecipeBook([Recipe(1, A, 1, (B, 5)), Recipe(2, A, 1, (C, 1))]);

        var forced = CraftingPlanner.Plan(new PlanRequest([new CraftGoal(A)], Inventory([(C, 1)]), book)
        {
            RouteOverrides = new Dictionary<int, int> { [A] = 1 },
        })[0];

        Assert.Equal(1, forced.Root.Recipe?.Id);
        Assert.Equal(5, Missing(forced, Item(B)));
    }

    [Fact]
    public void Override_for_a_recipe_that_does_not_make_the_item_is_ignored()
    {
        var book = new RecipeBook([Recipe(1, A, 1, (B, 1)), Recipe(9, D, 1, (C, 1))]);

        var plan = CraftingPlanner.Plan(new PlanRequest([new CraftGoal(A)], Inventory(), book)
        {
            RouteOverrides = new Dictionary<int, int> { [A] = 9 },
        })[0];

        Assert.Equal(1, plan.Root.Recipe?.Id);
    }

    [Fact]
    public void Circular_recipes_terminate_and_report_the_loop_as_missing()
    {
        var plan = Plan(A, Inventory(), Recipe(1, A, 1, (B, 1)), Recipe(2, B, 1, (A, 1)));

        Assert.Equal(1, Missing(plan, Item(A)));
        Assert.Equal(NextStepKind.Gather, plan.Next.Kind);
    }

    [Fact]
    public void Currencies_come_from_the_wallet()
    {
        var recipe = WithCurrency(Recipe(1, A, 1, (B, 2)), currencyId: 23, count: 250);

        var enough = Plan(A, Inventory([(B, 2)], [(23, 300)]), recipe);
        var short_ = Plan(A, Inventory([(B, 2)], [(23, 100)]), recipe);

        Assert.True(enough.Steps[0].IsReady);
        Assert.Equal(250, Used(enough, MaterialKey.Currency(23)));
        Assert.Equal(150, Missing(short_, MaterialKey.Currency(23)));
        Assert.False(short_.Steps[0].IsReady);
    }

    [Fact]
    public void Reserved_amounts_are_not_available()
    {
        var book = new RecipeBook([Recipe(1, A, 1, (B, 2))]);

        var plan = CraftingPlanner.Plan(new PlanRequest([new CraftGoal(A)], Inventory([(B, 2)]), book)
        {
            Reserved = new Dictionary<MaterialKey, long> { [Item(B)] = 1 },
        })[0];

        Assert.Equal(1, Missing(plan, Item(B)));
        Assert.Equal(1, Used(plan, Item(B)));
    }

    [Fact]
    public void Reservation_larger_than_stock_leaves_nothing_available()
    {
        var book = new RecipeBook([Recipe(1, A, 1, (B, 1))]);

        var plan = CraftingPlanner.Plan(new PlanRequest([new CraftGoal(A)], Inventory([(B, 1)]), book)
        {
            Reserved = new Dictionary<MaterialKey, long> { [Item(B)] = 50 },
        })[0];

        Assert.Equal(1, Missing(plan, Item(B)));
        Assert.Equal(0, Used(plan, Item(B)));
    }

    [Fact]
    public void Earlier_goals_have_priority_over_later_ones()
    {
        var book = new RecipeBook([Recipe(1, A, 1, (B, 2)), Recipe(2, D, 1, (B, 2))]);

        var plans = CraftingPlanner.Plan(new PlanRequest([new CraftGoal(A), new CraftGoal(D)], Inventory([(B, 3)]), book));

        Assert.Equal(0, Missing(plans[0], Item(B)));
        Assert.Equal(1.0, plans[0].Progress);
        Assert.Equal(1, Missing(plans[1], Item(B)));
        Assert.Equal(1, Used(plans[1], Item(B)));
    }

    [Fact]
    public void Goal_quantity_multiplies_the_requirements()
    {
        var book = new RecipeBook([Recipe(1, A, 1, (B, 2))]);

        var plan = CraftingPlanner.Plan(new CraftGoal(A, 3), Inventory([(B, 4)]), book);

        Assert.Equal(3, plan.Root.Crafts);
        Assert.Equal(2, Missing(plan, Item(B)));
    }

    [Fact]
    public void Non_positive_goal_quantity_is_rejected()
    {
        var request = new PlanRequest([new CraftGoal(A, 0)], Inventory(), RecipeBook.Empty);

        Assert.Throws<ArgumentOutOfRangeException>(() => CraftingPlanner.Plan(request));
    }

    [Fact]
    public void Oversized_trees_are_rejected_instead_of_hanging()
    {
        var book = new RecipeBook([Recipe(1, A, 1, (B, 1), (C, 1), (D, 1), (E, 1))]);
        var request = new PlanRequest([new CraftGoal(A)], Inventory(), book) { MaxNodes = 3 };

        Assert.Throws<CraftingPlanException>(() => CraftingPlanner.Plan(request));
    }

    [Fact]
    public void Progress_counts_hidden_positions_below_owned_items_as_satisfied()
    {
        // A needs B and E. B (owned) would be crafted from C and D, so it stands for 3 positions.
        // Positions: A, B, C, D, E = 5. Satisfied: B's 3. A is not (E is missing), E is not.
        var plan = Plan(A, Inventory([(B, 1)]), Recipe(1, A, 1, (B, 1), (E, 1)), Recipe(2, B, 1, (C, 1), (D, 1)));

        Assert.Equal(0.6, plan.Progress, 6);
    }

    [Fact]
    public void Next_step_is_the_deepest_craft_that_is_ready()
    {
        // A needs B and C; B needs D. D and C are owned, so B can be crafted, A cannot yet.
        var plan = Plan(A, Inventory([(D, 1), (C, 1)]), Recipe(1, A, 1, (B, 1), (C, 1)), Recipe(2, B, 1, (D, 1)));

        Assert.Equal([2, 1], plan.Steps.Select(s => s.Recipe.Id));
        Assert.Equal([true, false], plan.Steps.Select(s => s.IsReady));
        Assert.Equal(NextStepKind.Craft, plan.Next.Kind);
        Assert.Equal(2, plan.Next.Craft?.Recipe.Id);
        Assert.Equal(1.0, plan.Progress);
    }

    [Fact]
    public void Next_step_moves_up_the_tree_once_the_intermediate_is_owned()
    {
        var plan = Plan(A, Inventory([(B, 1), (C, 1)]), Recipe(1, A, 1, (B, 1), (C, 1)), Recipe(2, B, 1, (D, 1)));

        Assert.Equal(1, plan.Next.Craft?.Recipe.Id);
    }

    [Fact]
    public void Item_without_recipe_and_not_owned_is_a_plain_shortage()
    {
        var plan = Plan(A, Inventory(), Recipe(9, B, 1, (C, 1)));

        Assert.Equal(1, Missing(plan, Item(A)));
        Assert.Equal(NextStepKind.Gather, plan.Next.Kind);
        Assert.Equal(0.0, plan.Progress);
    }

    [Fact]
    public void Plans_are_deterministic()
    {
        var book = new RecipeBook([Recipe(1, A, 1, (B, 2), (C, 1)), Recipe(2, A, 1, (D, 1)), Recipe(3, B, 3, (E, 1))]);
        var inventory = Inventory([(E, 1)]);

        var first = CraftingPlanner.Plan(new CraftGoal(A, 2), inventory, book);
        var second = CraftingPlanner.Plan(new CraftGoal(A, 2), inventory, book);

        Assert.Equal(first.Materials, second.Materials);
        Assert.Equal(first.Next, second.Next);
        Assert.Equal(first.Progress, second.Progress);
    }
}
