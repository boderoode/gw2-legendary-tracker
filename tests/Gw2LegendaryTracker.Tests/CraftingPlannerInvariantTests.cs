using Gw2LegendaryTracker.Core.Crafting;
using static Gw2LegendaryTracker.Tests.CraftingTestHelpers;

namespace Gw2LegendaryTracker.Tests;

/// <summary>
/// Properties that must hold for every plan, checked on many random (acyclic) recipe books.
/// A fixed seed keeps failures reproducible.
/// </summary>
public class CraftingPlannerInvariantTests
{
    [Fact]
    public void Every_plan_satisfies_the_conservation_rules()
    {
        var random = new Random(20261022);

        for (var round = 0; round < 300; round++)
        {
            var itemCount = random.Next(4, 14);
            var recipes = RandomRecipes(random, itemCount);
            var owned = Enumerable.Range(1, itemCount + 2)
                .Where(_ => random.Next(3) == 0)
                .Select(item => (item, random.Next(1, 10)))
                .ToArray();
            var inventory = Inventory(owned);
            var book = new RecipeBook(recipes);

            var plan = CraftingPlanner.Plan(new CraftGoal(1, random.Next(1, 4)), inventory, book);

            VerifyNode(plan.Root, $"round {round}");
            Assert.InRange(plan.Progress, 0.0, 1.0);
            Assert.All(plan.Materials, m => Assert.True(m.UsedFromStock >= 0 && m.Missing >= 0, $"round {round}: negative amount"));

            // A plan has no shortage exactly when the goal is covered or fully craftable from stock.
            Assert.Equal(!plan.Shortages.Any(), plan.Root.IsSatisfied);

            // Items that cannot be crafted are only ever taken from real stock.
            foreach (var line in plan.Materials.Where(m => m.Key.Kind == MaterialKind.Item && book.RecipesFor(m.Key.Id).Count == 0))
            {
                Assert.True(line.UsedFromStock <= inventory.CountOf(line.Key.Id), $"round {round}: used more {line.Key.Id} than owned");
            }
        }
    }

    private static List<Recipe> RandomRecipes(Random random, int itemCount)
    {
        // Ingredients always have a higher id than the output, so there are no loops.
        var recipes = new List<Recipe>();
        var recipeId = 1;
        for (var item = 1; item <= itemCount; item++)
        {
            for (var variant = random.Next(0, 3); variant > 0; variant--)
            {
                var ingredients = Enumerable.Range(0, random.Next(1, 4))
                    .Select(_ => (Item: random.Next(item + 1, itemCount + 3), Count: random.Next(1, 5)))
                    .DistinctBy(i => i.Item)
                    .ToArray();
                recipes.Add(Recipe(recipeId++, item, random.Next(1, 4), ingredients));
            }
        }

        return recipes;
    }

    private static void VerifyNode(PlanNode node, string context)
    {
        Assert.True(node.Required > 0, $"{context}: non-positive requirement");
        Assert.InRange(node.FromStock, 0, node.Required);
        Assert.True(node.Missing >= 0, $"{context}: negative missing");

        var deficit = node.Required - node.FromStock;
        if (node.Recipe is null)
        {
            // A leaf is covered, or has exactly its deficit missing.
            Assert.Equal(deficit, node.Missing);
            Assert.Empty(node.Children);
            return;
        }

        var recipe = node.Recipe;
        Assert.Equal(0, node.Missing);

        // Crafts are enough to cover the deficit, and not one more than needed.
        Assert.True(node.Crafts * recipe.OutputCount >= deficit, $"{context}: too few crafts");
        Assert.True((node.Crafts - 1) * recipe.OutputCount < deficit, $"{context}: too many crafts");

        Assert.Equal(recipe.Ingredients.Count, node.Children.Count);
        for (var i = 0; i < node.Children.Count; i++)
        {
            Assert.Equal(recipe.Ingredients[i].Key, node.Children[i].Key);
            Assert.Equal(recipe.Ingredients[i].Count * node.Crafts, node.Children[i].Required);
            VerifyNode(node.Children[i], context);
        }
    }
}
