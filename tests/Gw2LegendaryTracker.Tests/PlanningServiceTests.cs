using Gw2LegendaryTracker.Core.Crafting;
using Gw2LegendaryTracker.Core.Goals;
using Gw2LegendaryTracker.Core.Gw2;
using Gw2LegendaryTracker.Core.Inventory;
using static Gw2LegendaryTracker.Tests.CraftingTestHelpers;

namespace Gw2LegendaryTracker.Tests;

public sealed class PlanningServiceTests : IDisposable
{
    private const int Goal = 1;
    private const int Part = 2;
    private const int Raw = 3;

    private readonly GoalService _goals = new(new FakeGoalStore());
    private readonly InventoryProvider _inventory = new();
    private readonly PlanningService _planning;

    public PlanningServiceTests() => _planning = new PlanningService(_goals, _inventory);

    public void Dispose()
    {
        _planning.Dispose();
        _goals.Dispose();
    }

    private static AccountSnapshot Owning(params (int Item, int Count)[] items) =>
        new("Test.1234", DateTimeOffset.UnixEpoch, [.. items.Select(i => new InventoryEntry(i.Item, i.Count, InventorySource.Bank))], []);

    [Fact]
    public async Task Adding_a_goal_produces_a_plan()
    {
        _planning.SetRecipes(new RecipeBook([Recipe(10, Goal, 1, (Raw, 2))]));

        await _goals.AddAsync(Goal);

        var plan = Assert.Single(_planning.Current);
        Assert.True(plan.HasRecipeData);
        Assert.Equal(2, plan.Plan?.Shortages.Single().Missing);
    }

    [Fact]
    public async Task A_new_inventory_updates_the_plan()
    {
        _planning.SetRecipes(new RecipeBook([Recipe(10, Goal, 1, (Raw, 2))]));
        await _goals.AddAsync(Goal);

        _inventory.Update(Owning((Raw, 2)));

        Assert.Equal(1.0, _planning.Current[0].Plan?.Progress);
    }

    [Fact]
    public async Task New_recipes_update_the_plan_and_report_recipe_data()
    {
        await _goals.AddAsync(Goal);
        Assert.False(_planning.Current[0].HasRecipeData);

        _planning.SetRecipes(new RecipeBook([Recipe(10, Goal, 1, (Raw, 1))]));

        Assert.True(_planning.Current[0].HasRecipeData);
    }

    [Fact]
    public async Task A_goal_without_recipe_data_is_a_plain_shortage()
    {
        await _goals.AddAsync(Goal);

        var plan = _planning.Current[0];

        Assert.False(plan.HasRecipeData);
        Assert.Equal(NextStepKind.Gather, plan.Plan?.Next.Kind);
    }

    [Fact]
    public async Task Ticking_a_component_removes_its_requirements()
    {
        _planning.SetRecipes(new RecipeBook([Recipe(10, Goal, 1, (Part, 1)), Recipe(11, Part, 1, (Raw, 5))]));
        await _goals.AddAsync(Goal);
        Assert.Equal(5, _planning.Current[0].Plan?.Shortages.Single().Missing);

        await _goals.SetCompletedAsync(Goal, Part, true);

        var plan = _planning.Current[0].Plan!;
        Assert.Empty(plan.Shortages);
        Assert.Equal(1.0, plan.Progress);
        Assert.Equal(NextStepKind.Craft, plan.Next.Kind);
    }

    [Fact]
    public async Task Goals_share_stock_in_list_order()
    {
        _planning.SetRecipes(new RecipeBook([Recipe(10, 1, 1, (Raw, 2)), Recipe(11, 2, 1, (Raw, 2))]));
        _inventory.Update(Owning((Raw, 3)));
        await _goals.AddAsync(1);
        await _goals.AddAsync(2);

        Assert.Equal(0, _planning.Current[0].Plan?.Shortages.Sum(s => s.Missing));
        Assert.Equal(1, _planning.Current[1].Plan?.Shortages.Sum(s => s.Missing));

        await _goals.MoveAsync(2, -1);

        Assert.Equal(2, _planning.Current[0].Goal.ItemId);
        Assert.Equal(0, _planning.Current[0].Plan?.Shortages.Sum(s => s.Missing));
        Assert.Equal(1, _planning.Current[1].Plan?.Shortages.Sum(s => s.Missing));
    }

    [Fact]
    public async Task Oversized_trees_give_an_error_instead_of_a_plan()
    {
        var huge = Enumerable.Range(0, 60_000).Select(i => (Item: 1000 + i, Count: 1)).ToArray();
        _planning.SetRecipes(new RecipeBook([Recipe(10, Goal, 1, huge)]));

        await _goals.AddAsync(Goal);

        var plan = _planning.Current[0];
        Assert.Null(plan.Plan);
        Assert.False(string.IsNullOrWhiteSpace(plan.Error));
    }

    [Fact]
    public async Task Changes_are_announced()
    {
        var announcements = 0;
        _planning.Changed += (_, _) => announcements++;

        await _goals.AddAsync(Goal);
        _inventory.Update(Owning());
        _planning.SetRecipes(RecipeBook.Empty);

        Assert.Equal(3, announcements);
    }

    [Fact]
    public async Task After_dispose_it_no_longer_reacts()
    {
        _planning.Dispose();

        await _goals.AddAsync(Goal);

        Assert.Empty(_planning.Current);
    }
}
