using Gw2LegendaryTracker.Core.Crafting;
using Gw2LegendaryTracker.Core.Gw2;
using Gw2LegendaryTracker.Core.Inventory;

namespace Gw2LegendaryTracker.Tests;

internal static class CraftingTestHelpers
{
    /// <summary>An inventory owning the given (item id, count) pairs, optionally with wallet amounts (currency id, value).</summary>
    public static InventoryModel Inventory((int Item, int Count)[]? items = null, (int Currency, long Value)[]? wallet = null) =>
        InventoryModel.From(new AccountSnapshot(
            "Test.1234",
            DateTimeOffset.UnixEpoch,
            [.. (items ?? []).Select(i => new InventoryEntry(i.Item, i.Count, InventorySource.Bank))],
            [.. (wallet ?? []).Select(w => new WalletEntry(w.Currency, w.Value))]));

    /// <summary>A recipe with item ingredients only.</summary>
    public static Recipe Recipe(int id, int output, int outputCount, params (int Item, int Count)[] ingredients) =>
        new(id, output, outputCount, [.. ingredients.Select(i => new Ingredient(MaterialKey.Item(i.Item), i.Count))], []);

    public static Recipe WithCurrency(Recipe recipe, int currencyId, long count) =>
        recipe with { Ingredients = [.. recipe.Ingredients, new Ingredient(MaterialKey.Currency(currencyId), count)] };

    public static CraftPlan Plan(int goal, InventoryModel inventory, params Recipe[] recipes) =>
        CraftingPlanner.Plan(new CraftGoal(goal), inventory, new RecipeBook(recipes));

    public static long Missing(CraftPlan plan, MaterialKey key) => plan.Materials.FirstOrDefault(m => m.Key == key)?.Missing ?? 0;

    public static long Used(CraftPlan plan, MaterialKey key) => plan.Materials.FirstOrDefault(m => m.Key == key)?.UsedFromStock ?? 0;
}
