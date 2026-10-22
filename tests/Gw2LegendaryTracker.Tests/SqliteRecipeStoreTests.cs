using Gw2LegendaryTracker.Core.Crafting;
using Gw2LegendaryTracker.Infrastructure.Persistence;
using static Gw2LegendaryTracker.Tests.CraftingTestHelpers;

namespace Gw2LegendaryTracker.Tests;

public class SqliteRecipeStoreTests
{
    [Fact]
    public async Task Save_then_load_round_trips_including_ingredient_order()
    {
        using var temp = new TempDatabase();
        await temp.Database.MigrateAsync();
        var store = new SqliteRecipeStore(temp.Database);
        var recipes = new[]
        {
            Recipe(1, 100, 3, (9, 2), (4, 1), (7, 5)) with { Disciplines = ["Weaponsmith", "Huntsman"] },
            Recipe(2, 200, 1, (4, 1)) with { RequiresGuildUpgrades = true },
        };

        await store.SaveAsync(recipes);
        var loaded = await store.LoadAllAsync();

        Assert.Equal(2, loaded.Count);
        Assert.Equal([9, 4, 7], loaded[0].Ingredients.Select(i => i.Key.Id));
        Assert.Equal(recipes[0].Ingredients, loaded[0].Ingredients);
        Assert.Equal(["Weaponsmith", "Huntsman"], loaded[0].Disciplines);
        Assert.Equal(3, loaded[0].OutputCount);
        Assert.False(loaded[0].RequiresGuildUpgrades);
        Assert.True(loaded[1].RequiresGuildUpgrades);
        Assert.Empty(loaded[1].Disciplines);
    }

    [Fact]
    public async Task Known_ids_include_everything_stored()
    {
        using var temp = new TempDatabase();
        await temp.Database.MigrateAsync();
        var store = new SqliteRecipeStore(temp.Database);

        await store.SaveAsync([Recipe(5, 1, 1, (2, 1)), Recipe(8, 1, 1, (2, 1))]);

        Assert.Equal([5, 8], (await store.GetKnownIdsAsync()).Order());
    }

    [Fact]
    public async Task Saving_a_recipe_again_replaces_its_ingredients()
    {
        using var temp = new TempDatabase();
        await temp.Database.MigrateAsync();
        var store = new SqliteRecipeStore(temp.Database);
        await store.SaveAsync([Recipe(1, 100, 1, (2, 1), (3, 1))]);

        await store.SaveAsync([Recipe(1, 100, 1, (4, 9))]);

        var recipe = Assert.Single(await store.LoadAllAsync());
        Assert.Equal([new Ingredient(MaterialKey.Item(4), 9)], recipe.Ingredients);
    }

    [Fact]
    public async Task Empty_store_loads_nothing_and_empty_save_is_a_no_op()
    {
        using var temp = new TempDatabase();
        await temp.Database.MigrateAsync();
        var store = new SqliteRecipeStore(temp.Database);

        await store.SaveAsync([]);

        Assert.Empty(await store.LoadAllAsync());
        Assert.Empty(await store.GetKnownIdsAsync());
    }

    [Fact]
    public async Task Stored_recipes_can_drive_a_plan_end_to_end()
    {
        using var temp = new TempDatabase();
        await temp.Database.MigrateAsync();
        var store = new SqliteRecipeStore(temp.Database);
        await store.SaveAsync([Recipe(1, 10, 1, (20, 2)), Recipe(2, 20, 1, (30, 3))]);
        var book = new RecipeBook(await store.LoadAllAsync());

        var plan = CraftingPlanner.Plan(new CraftGoal(10), Inventory([(30, 6)]), book);

        Assert.Equal(1.0, plan.Progress);
        Assert.Equal(NextStepKind.Craft, plan.Next.Kind);
        Assert.Equal(2, plan.Next.Craft?.Recipe.Id);
    }
}
