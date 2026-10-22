using Gw2LegendaryTracker.Core.Crafting;
using static Gw2LegendaryTracker.Tests.CraftingTestHelpers;

namespace Gw2LegendaryTracker.Tests;

public class RecipeBookTests
{
    [Fact]
    public void Recipes_are_grouped_by_output_and_ordered_by_id()
    {
        var book = new RecipeBook([Recipe(9, 1, 1, (2, 1)), Recipe(3, 1, 1, (3, 1)), Recipe(5, 7, 1, (2, 1))]);

        Assert.Equal([3, 9], book.RecipesFor(1).Select(r => r.Id));
        Assert.Equal(2, book.OutputCount);
        Assert.Empty(book.RecipesFor(404));
    }

    [Fact]
    public void Recipes_needing_guild_upgrades_are_never_used()
    {
        var book = new RecipeBook([Recipe(1, 1, 1, (2, 1)) with { RequiresGuildUpgrades = true }]);

        Assert.Empty(book.RecipesFor(1));
    }

    [Fact]
    public void Unusable_and_duplicate_recipes_are_ignored()
    {
        var book = new RecipeBook(
        [
            Recipe(1, 1, 0, (2, 1)),
            Recipe(2, 1, 1),
            Recipe(3, 1, 1, (2, 1)),
            Recipe(3, 1, 1, (9, 9)),
        ]);

        var only = Assert.Single(book.RecipesFor(1));
        Assert.Equal(2, only.Ingredients[0].Key.Id);
    }
}

public class LocalRecipeLoaderTests
{
    private const string Valid = """
        { "recipes": [
          { "id": -1, "output": { "itemId": 100, "count": 1 },
            "ingredients": [ { "itemId": 5, "count": 2 }, { "currencyId": 23, "count": 250 } ],
            "disciplines": ["Mystic Forge"] }
        ] }
        """;

    [Fact]
    public void Valid_recipe_with_item_and_currency_is_loaded()
    {
        var result = LocalRecipeLoader.Parse(Valid);

        Assert.Empty(result.Errors);
        var recipe = Assert.Single(result.Recipes);
        Assert.Equal(-1, recipe.Id);
        Assert.Equal(RecipeSource.Local, recipe.Source);
        Assert.Equal(100, recipe.OutputItemId);
        Assert.Equal([new Ingredient(MaterialKey.Item(5), 2), new Ingredient(MaterialKey.Currency(23), 250)], recipe.Ingredients);
        Assert.Equal(["Mystic Forge"], recipe.Disciplines);
    }

    [Theory]
    [InlineData("""{ "recipes": [ { "id": 5, "output": { "itemId": 1, "count": 1 }, "ingredients": [ { "itemId": 2, "count": 1 } ] } ] }""", "negatief")]
    [InlineData("""{ "recipes": [ { "id": -1, "output": { "itemId": 0, "count": 1 }, "ingredients": [ { "itemId": 2, "count": 1 } ] } ] }""", "output")]
    [InlineData("""{ "recipes": [ { "id": -1, "output": { "itemId": 1, "count": 1 }, "ingredients": [] } ] }""", "ingrediënt")]
    [InlineData("""{ "recipes": [ { "id": -1, "output": { "itemId": 1, "count": 1 }, "ingredients": [ { "itemId": 2, "count": 0 } ] } ] }""", "count")]
    [InlineData("""{ "recipes": [ { "id": -1, "output": { "itemId": 1, "count": 1 }, "ingredients": [ { "itemId": 2, "currencyId": 3, "count": 1 } ] } ] }""", "precies één")]
    [InlineData("""{ "recipes": [ { "id": -1, "output": { "itemId": 1, "count": 1 }, "ingredients": [ { "count": 1 } ] } ] }""", "precies één")]
    [InlineData("""{ "recipes": [ null ] }""", "leeg")]
    public void Invalid_recipe_is_skipped_with_a_readable_reason(string json, string expectedFragment)
    {
        var result = LocalRecipeLoader.Parse(json);

        Assert.Empty(result.Recipes);
        var error = Assert.Single(result.Errors);
        Assert.StartsWith("Recept #1:", error, StringComparison.Ordinal);
        Assert.Contains(expectedFragment, error, StringComparison.Ordinal);
    }

    [Fact]
    public void One_bad_recipe_does_not_discard_the_others()
    {
        var result = LocalRecipeLoader.Parse("""
            { "recipes": [
              { "id": -1, "output": { "itemId": 1, "count": 1 }, "ingredients": [ { "itemId": 2, "count": 1 } ] },
              { "id": -2, "output": { "itemId": 0, "count": 1 }, "ingredients": [ { "itemId": 2, "count": 1 } ] },
              { "id": -3, "output": { "itemId": 3, "count": 1 }, "ingredients": [ { "itemId": 2, "count": 1 } ] }
            ] }
            """);

        Assert.Equal([-1, -3], result.Recipes.Select(r => r.Id));
        Assert.StartsWith("Recept #2:", Assert.Single(result.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public void Duplicate_ids_are_reported()
    {
        var result = LocalRecipeLoader.Parse("""
            { "recipes": [
              { "id": -1, "output": { "itemId": 1, "count": 1 }, "ingredients": [ { "itemId": 2, "count": 1 } ] },
              { "id": -1, "output": { "itemId": 3, "count": 1 }, "ingredients": [ { "itemId": 2, "count": 1 } ] }
            ] }
            """);

        Assert.Single(result.Recipes);
        Assert.Contains("dubbel", Assert.Single(result.Errors), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{ "recipes": null }""")]
    public void Broken_files_give_one_error_and_no_recipes(string json)
    {
        var result = LocalRecipeLoader.Parse(json);

        Assert.Empty(result.Recipes);
        Assert.Single(result.Errors);
    }
}

public class RecipeCatalogTests
{
    private readonly FakeApiClient _api = new();
    private readonly FakeRecipeStore _store = new();

    private RecipeCatalog CreateCatalog() => new(_api, _store);

    private void Serve(IEnumerable<int> ids)
    {
        foreach (var id in ids)
        {
            _api.Recipes[id] = Recipe(id, id + 10_000, 1, (1, 1));
            _api.RecipeIds.Add(id);
        }
    }

    [Fact]
    public async Task Refresh_downloads_unknown_recipes_in_chunks_of_200_and_stores_them()
    {
        Serve(Enumerable.Range(1, 450));

        var added = await CreateCatalog().RefreshAsync();

        Assert.Equal(450, added);
        Assert.Equal(450, _store.Recipes.Count);
        Assert.Equal([200, 200, 50], _api.RecipeRequests.Select(r => r.Count));
    }

    [Fact]
    public async Task Refresh_only_fetches_recipes_that_are_not_cached()
    {
        Serve(Enumerable.Range(1, 10));
        await CreateCatalog().RefreshAsync();
        _api.RecipeRequests.Clear();
        Serve([11, 12]);

        var added = await CreateCatalog().RefreshAsync();

        Assert.Equal(2, added);
        Assert.Equal([11, 12], Assert.Single(_api.RecipeRequests));
    }

    [Fact]
    public async Task Refresh_makes_no_requests_when_everything_is_cached()
    {
        Serve([1, 2]);
        await CreateCatalog().RefreshAsync();
        _api.RecipeRequests.Clear();

        Assert.Equal(0, await CreateCatalog().RefreshAsync());
        Assert.Empty(_api.RecipeRequests);
    }

    [Fact]
    public async Task Book_combines_cached_and_local_recipes_as_alternatives()
    {
        _store.Recipes[5] = Recipe(5, 100, 1, (1, 1));
        var local = Recipe(-1, 100, 1, (2, 1)) with { Source = RecipeSource.Local };

        var book = await CreateCatalog().LoadBookAsync([local]);

        Assert.Equal([-1, 5], book.RecipesFor(100).Select(r => r.Id));
    }
}
