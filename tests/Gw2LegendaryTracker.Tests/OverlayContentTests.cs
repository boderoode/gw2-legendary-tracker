using System.Globalization;
using Gw2LegendaryTracker.Core.Crafting;
using Gw2LegendaryTracker.Core.Goals;
using Gw2LegendaryTracker.Core.Inventory;
using static Gw2LegendaryTracker.Tests.CraftingTestHelpers;

namespace Gw2LegendaryTracker.Tests;

public class PlanTreeTests
{
    private const int A = 1;
    private const int B = 2;
    private const int C = 3;
    private const int D = 4;
    private const int E = 5;
    private const int F = 6;
    private const int G = 7;

    private static IReadOnlyList<PlanTreeNode> Tree(InventoryModel inventory, IReadOnlySet<int>? completed, params Recipe[] recipes) =>
        PlanTree.Build(CraftingPlanner.Plan(new PlanRequest([new CraftGoal(A) { Completed = completed }], inventory, new RecipeBook(recipes)))[0]);

    [Fact]
    public void Nodes_show_whether_they_are_ready_covered_or_missing()
    {
        // A needs B (crafted from E), C and D. E and C are owned; D is not.
        var tree = Tree(
            Inventory([(E, 1), (C, 1)]),
            null,
            Recipe(1, A, 1, (B, 1), (C, 1), (D, 1)),
            Recipe(2, B, 1, (E, 1)));

        Assert.Equal([B, C, D], tree.Select(n => n.Key.Id));
        Assert.Equal([TreeState.Ready, TreeState.Covered, TreeState.Missing], tree.Select(n => n.State));
        Assert.Equal(1, tree[2].Missing);
        Assert.Equal(TreeState.Covered, tree[0].Children[0].State);
    }

    [Fact]
    public void A_ticked_component_is_shown_as_done_by_hand()
    {
        var tree = Tree(Inventory(), new HashSet<int> { D }, Recipe(1, A, 1, (D, 5)));

        Assert.Equal(TreeState.Manual, Assert.Single(tree).State);
        Assert.Equal("Zelf afgevinkt", tree[0].StatusText(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void A_part_that_waits_for_lower_crafts_is_told_apart_from_one_that_is_blocked()
    {
        // B needs F, F needs G.
        var recipes = new[] { Recipe(1, A, 1, (B, 1)), Recipe(2, B, 1, (F, 1)), Recipe(3, F, 1, (G, 1)) };

        var waiting = Tree(Inventory([(G, 1)]), null, recipes);
        var blocked = Tree(Inventory(), null, recipes);

        Assert.Equal(TreeState.Waiting, waiting[0].State);
        Assert.Equal(TreeState.Ready, waiting[0].Children[0].State);
        Assert.Equal(TreeState.Blocked, blocked[0].State);
        Assert.Equal(TreeState.Blocked, blocked[0].Children[0].State);
        Assert.Equal(TreeState.Missing, blocked[0].Children[0].Children[0].State);
    }

    [Fact]
    public void Status_texts_are_plain_language_and_name_the_missing_amount()
    {
        var tree = Tree(Inventory(), null, Recipe(1, A, 1, (B, 1500)));

        Assert.Equal("Nog 1,500 nodig", tree[0].StatusText(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Paths_are_unique_and_do_not_change_between_calculations()
    {
        var recipes = new[] { Recipe(1, A, 1, (B, 1), (C, 1)), Recipe(2, B, 1, (C, 1)) };

        var first = Flatten(Tree(Inventory(), null, recipes)).ToList();
        var second = Flatten(Tree(Inventory(), null, recipes)).ToList();

        Assert.Equal(first, second);
        Assert.Equal(first.Count, first.Distinct().Count());
    }

    [Fact]
    public void The_goal_itself_is_not_part_of_the_tree()
    {
        var tree = Tree(Inventory(), null, Recipe(1, A, 1, (B, 1)));

        Assert.DoesNotContain(Flatten(tree), path => path.Contains("Item1#", StringComparison.Ordinal) && !path.Contains("Item2", StringComparison.Ordinal));
        Assert.Single(tree);
    }

    private static IEnumerable<string> Flatten(IEnumerable<PlanTreeNode> nodes) =>
        nodes.SelectMany(n => new[] { n.Path }.Concat(Flatten(n.Children)));
}

public class MaterialOverviewTests
{
    private const int Goal1 = 10;
    private const int Goal2 = 20;
    private const int Goal3 = 30;
    private const int X = 1;
    private const int Y = 2;
    private const int Wallet = 99;

    private static IReadOnlyList<GoalPlan> Plans(InventoryModel inventory, params (int Goal, Recipe Recipe)[] goals)
    {
        var book = new RecipeBook(goals.Select(g => g.Recipe));
        var plans = CraftingPlanner.Plan(new PlanRequest([.. goals.Select(g => new CraftGoal(g.Goal))], inventory, book));
        return [.. goals.Select((g, i) => new GoalPlan(new Goal(g.Goal), plans[i], true, null))];
    }

    [Fact]
    public void Materials_list_the_largest_gaps_first_and_show_the_total_owned()
    {
        var inventory = Inventory([(X, 5), (Y, 0)]);
        var plans = Plans(inventory, (Goal1, Recipe(1, Goal1, 1, (X, 5), (Y, 9), (3, 2))));

        var result = MaterialOverview.Build(plans, 0, inventory);

        Assert.Equal([Y, 3, X], result.Materials.Select(m => m.ItemId));
        Assert.Equal(new MaterialRow(Y, 9, 0, 9), result.Materials[0]);
        Assert.Equal(new MaterialRow(X, 5, 5, 0), result.Materials[2]);
    }

    [Fact]
    public void Stock_taken_by_a_goal_with_higher_priority_is_reported_as_reserved()
    {
        var inventory = Inventory([(X, 3)]);
        var plans = Plans(inventory, (Goal1, Recipe(1, Goal1, 1, (X, 2))), (Goal2, Recipe(2, Goal2, 1, (X, 2))));

        var first = MaterialOverview.Build(plans, 0, inventory);
        var second = MaterialOverview.Build(plans, 1, inventory);

        Assert.Empty(first.Reservations);
        var line = Assert.Single(second.Reservations);
        Assert.Equal((MaterialKey.Item(X), 2L, Goal1), (line.Key, line.Amount, line.ForGoalItemId));

        // The second goal still sees the total owned, but is short by what the first one holds.
        var row = Assert.Single(second.Materials);
        Assert.Equal(new MaterialRow(X, 2, 3, 1), row);
    }

    [Fact]
    public void Only_materials_the_selected_goal_needs_are_listed_as_reserved()
    {
        var inventory = Inventory([(X, 5), (Y, 5)]);
        var plans = Plans(inventory, (Goal1, Recipe(1, Goal1, 1, (X, 1), (Y, 1))), (Goal2, Recipe(2, Goal2, 1, (X, 1))));

        var result = MaterialOverview.Build(plans, 1, inventory);

        Assert.Equal([MaterialKey.Item(X)], result.Reservations.Select(r => r.Key));
    }

    [Fact]
    public void Reservations_name_every_earlier_goal_in_priority_order()
    {
        var inventory = Inventory([(X, 10)]);
        var plans = Plans(
            inventory,
            (Goal1, Recipe(1, Goal1, 1, (X, 3))),
            (Goal2, Recipe(2, Goal2, 1, (X, 4))),
            (Goal3, Recipe(3, Goal3, 1, (X, 1))));

        var result = MaterialOverview.Build(plans, 2, inventory);

        Assert.Equal([(Goal1, 3L), (Goal2, 4L)], result.Reservations.Select(r => (r.ForGoalItemId, r.Amount)));
    }

    [Fact]
    public void Currencies_are_listed_with_the_wallet_amount()
    {
        var inventory = Inventory(null, [(Wallet, 100)]);
        var plans = Plans(inventory, (Goal1, WithCurrency(Recipe(1, Goal1, 1, (X, 1)), Wallet, 250)));

        var result = MaterialOverview.Build(plans, 0, inventory);

        Assert.Equal(new CurrencyRow(Wallet, 250, 100, 150), Assert.Single(result.Currencies));
        Assert.DoesNotContain(result.Materials, m => m.ItemId == Wallet);
    }

    [Fact]
    public void An_unusable_selection_gives_an_empty_overview()
    {
        var inventory = Inventory();
        var broken = new List<GoalPlan> { new(new Goal(Goal1), null, true, "Te groot.") };

        Assert.Empty(MaterialOverview.Build(broken, 0, inventory).Materials);
        Assert.Empty(MaterialOverview.Build(broken, 5, inventory).Materials);
        Assert.Empty(MaterialOverview.Build([], -1, inventory).Materials);
    }

    [Fact]
    public void Shortages_carry_the_amount_owned()
    {
        var inventory = Inventory([(X, 120)]);
        var plan = Plans(inventory, (Goal1, Recipe(1, Goal1, 1, (X, 250))))[0];

        var line = Assert.Single(GoalPresenter.Summarize(plan, id => $"#{id}", CultureInfo.InvariantCulture, inventory).Shortages);

        Assert.Equal(new ShortageLine(X, 130, 250, 120), line);
    }
}

public class OverlaySettingsDefaultsTests
{
    private static readonly System.Text.Json.JsonSerializerOptions Web = new(System.Text.Json.JsonSerializerDefaults.Web);

    [Fact]
    public void Old_stored_settings_without_the_new_filter_default_to_showing_only_missing()
    {
        var restored = System.Text.Json.JsonSerializer.Deserialize<Gw2LegendaryTracker.Core.Settings.OverlaySettings>(
            """{"opacity":0.7,"visibleMaterialCount":3}""",
            Web);

        Assert.NotNull(restored);
        Assert.True(restored.ShowOnlyMissing);
        Assert.Equal(3, restored.VisibleMaterialCount);
    }
}

public class CurrencyCatalogTests
{
    private readonly FakeApiClient _api = new();

    public CurrencyCatalogTests()
    {
        _api.Currencies[1] = "Coin";
        _api.Currencies[2] = "Karma";
    }

    [Fact]
    public async Task Names_are_resolved_and_remembered_for_the_session()
    {
        var catalog = new Gw2LegendaryTracker.Core.Items.CurrencyCatalog(_api);

        var first = await catalog.ResolveAsync([1, 2]);
        var second = await catalog.ResolveAsync([2, 1]);

        Assert.Equal("Karma", first[2]);
        Assert.Equal(first, second);
        Assert.Equal(1, _api.CurrencyRequests);
    }

    [Fact]
    public async Task Only_unknown_ids_are_requested_and_unresolvable_ones_are_left_out()
    {
        var catalog = new Gw2LegendaryTracker.Core.Items.CurrencyCatalog(_api);
        await catalog.ResolveAsync([1]);

        var result = await catalog.ResolveAsync([1, 2, 999]);

        Assert.Equal([1, 2], result.Keys.Order());
        Assert.Equal(2, _api.CurrencyRequests);
    }

    [Fact]
    public async Task Offline_returns_what_is_known_without_throwing()
    {
        var catalog = new Gw2LegendaryTracker.Core.Items.CurrencyCatalog(_api);
        await catalog.ResolveAsync([1]);
        _api.CurrencyFailure = new Gw2LegendaryTracker.Core.Gw2.Gw2ApiException(Gw2LegendaryTracker.Core.Gw2.Gw2ApiErrorKind.Offline, "offline");

        var result = await catalog.ResolveAsync([1, 2]);

        Assert.Equal([1], result.Keys);
    }
}
