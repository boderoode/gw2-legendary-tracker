using Gw2LegendaryTracker.Core.Crafting;
using Gw2LegendaryTracker.Core.Goals;

namespace Gw2LegendaryTracker.Tests;

public class RecipePackLoaderTests
{
    private static string Pack(string ingredients, string output = """{ "itemId": 19672, "count": 1, "name": "Gift of Might" }""", string extra = "") => $$"""
        { "recipes": [ { "id": -1, {{extra}} "output": {{output}}, "ingredients": [ {{ingredients}} ] } ] }
        """;

    [Fact]
    public void Names_are_collected_for_verification()
    {
        var result = LocalRecipeLoader.Parse(Pack("""
            { "itemId": 24357, "count": 250, "name": "Vicious Fang" },
            { "itemId": 24358, "count": 250, "name": "Ancient Bone" },
            { "currencyId": 23, "count": 5 }
            """));

        Assert.Empty(result.Errors);
        Assert.Equal("Gift of Might", result.ExpectedItemNames[19672]);
        Assert.Equal("Vicious Fang", result.ExpectedItemNames[24357]);
        Assert.Equal(3, result.ExpectedItemNames.Count);
    }

    [Fact]
    public void The_same_item_listed_twice_as_ingredient_is_rejected()
    {
        // The mistake in the original example: 24358 appeared twice, once meant for another material.
        var result = LocalRecipeLoader.Parse(Pack("""
            { "itemId": 24358, "count": 250, "name": "Ancient Bone" },
            { "itemId": 24358, "count": 250, "name": "Armored Scale" }
            """));

        Assert.Empty(result.Recipes);
        Assert.Contains("twee keer", Assert.Single(result.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public void An_id_with_two_names_across_recipes_is_rejected()
    {
        var result = LocalRecipeLoader.Parse("""
            { "recipes": [
              { "id": -1, "output": { "itemId": 1, "count": 1, "name": "A" }, "ingredients": [ { "itemId": 9, "count": 1, "name": "Copper Ore" } ] },
              { "id": -2, "output": { "itemId": 2, "count": 1, "name": "B" }, "ingredients": [ { "itemId": 9, "count": 1, "name": "Iron Ore" } ] }
            ] }
            """);

        Assert.Equal([-1], result.Recipes.Select(r => r.Id));
        Assert.Contains("eerder in het bestand", Assert.Single(result.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public void An_output_item_cannot_be_named_differently_than_an_ingredient_elsewhere()
    {
        var result = LocalRecipeLoader.Parse("""
            { "recipes": [
              { "id": -1, "output": { "itemId": 1, "count": 1, "name": "A" }, "ingredients": [ { "itemId": 9, "count": 1, "name": "Gift" } ] },
              { "id": -2, "output": { "itemId": 9, "count": 1, "name": "Something Else" }, "ingredients": [ { "itemId": 5, "count": 1, "name": "X" } ] }
            ] }
            """);

        Assert.Single(result.Recipes);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void Unreviewed_recipes_are_reported()
    {
        var result = LocalRecipeLoader.Parse(Pack("""{ "itemId": 5, "count": 1, "name": "X" }""", extra: "\"status\": \"unreviewed\","));

        Assert.Equal([-1], result.UnreviewedRecipeIds);
    }

    [Fact]
    public void Missing_names_are_allowed_by_the_loader_but_left_to_the_verifier()
    {
        var result = LocalRecipeLoader.Parse(Pack("""{ "itemId": 5, "count": 1 }""", output: """{ "itemId": 6, "count": 1 }"""));

        Assert.Empty(result.Errors);
        Assert.Empty(result.ExpectedItemNames);
    }
}

public class RecipePackVerifierTests
{
    private readonly FakeApiClient _api = new()
    {
        KnownItems = new Dictionary<int, string>
        {
            [19672] = "Gift of Might",
            [24357] = "Vicious Fang",
            [24358] = "Ancient Bone",
            [24351] = "Vicious Claw",
            [24289] = "Armored Scale",
            [24356] = "Large Fang",
        },
    };

    private static LocalRecipeLoadResult Load(string ingredients, string status = "reviewed") => LocalRecipeLoader.Parse($$"""
        { "recipes": [ { "id": -1, "status": "{{status}}", "output": { "itemId": 19672, "count": 1, "name": "Gift of Might" },
                         "ingredients": [ {{ingredients}} ] } ] }
        """);

    [Fact]
    public async Task Correct_ids_and_names_verify_cleanly()
    {
        var pack = Load("""
            { "itemId": 24357, "count": 250, "name": "Vicious Fang" },
            { "itemId": 24289, "count": 250, "name": "Armored Scale" },
            { "itemId": 24351, "count": 250, "name": "Vicious Claw" },
            { "itemId": 24358, "count": 250, "name": "Ancient Bone" }
            """);

        var result = await RecipePackVerifier.VerifyAsync(pack, _api);

        Assert.True(result.IsClean);
        Assert.Empty(result.Notes);
        Assert.Equal(5, result.ItemsChecked);
    }

    [Fact]
    public async Task A_wrong_id_for_a_real_item_is_caught()
    {
        // 24356 is Large Fang, not Vicious Claw: exactly the mistake in the original example.
        var pack = Load("""
            { "itemId": 24357, "count": 250, "name": "Vicious Fang" },
            { "itemId": 24356, "count": 250, "name": "Vicious Claw" }
            """);

        var result = await RecipePackVerifier.VerifyAsync(pack, _api);

        Assert.False(result.IsClean);
        var problem = Assert.Single(result.Problems);
        Assert.Contains("24356", problem, StringComparison.Ordinal);
        Assert.Contains("Large Fang", problem, StringComparison.Ordinal);
        Assert.Contains("Vicious Claw", problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_id_that_does_not_exist_is_caught()
    {
        var pack = Load("""{ "itemId": 99999999, "count": 1, "name": "Imaginary Thing" }""");

        var result = await RecipePackVerifier.VerifyAsync(pack, _api);

        Assert.Contains(result.Problems, p => p.Contains("bestaat niet", StringComparison.Ordinal) && p.Contains("99999999", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Items_without_a_name_cannot_be_verified()
    {
        var pack = LocalRecipeLoader.Parse("""
            { "recipes": [ { "id": -1, "output": { "itemId": 19672, "count": 1, "name": "Gift of Might" },
                             "ingredients": [ { "itemId": 24357, "count": 1 } ] } ] }
            """);

        var result = await RecipePackVerifier.VerifyAsync(pack, _api);

        Assert.Contains(result.Problems, p => p.Contains("24357", StringComparison.Ordinal) && p.Contains("geen naam", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Name_comparison_ignores_case_and_unreviewed_recipes_are_notes_not_problems()
    {
        var pack = Load("""{ "itemId": 24357, "count": 1, "name": "vicious fang" }""", status: "unreviewed");

        var result = await RecipePackVerifier.VerifyAsync(pack, _api);

        Assert.True(result.IsClean);
        Assert.Single(result.Notes);
    }

    [Fact]
    public async Task Currencies_are_checked_by_id_and_name_like_items()
    {
        _api.Currencies[23] = "Spirit Shard";
        var pack = Load("""
            { "itemId": 24357, "count": 1, "name": "Vicious Fang" },
            { "currencyId": 23, "count": 5, "name": "Spirit Shard" }
            """);

        var result = await RecipePackVerifier.VerifyAsync(pack, _api);

        Assert.True(result.IsClean);
    }

    [Fact]
    public async Task A_currency_without_a_name_cannot_be_verified()
    {
        _api.Currencies[23] = "Spirit Shard";

        var result = await RecipePackVerifier.VerifyAsync(Load("""{ "currencyId": 23, "count": 5 }"""), _api);

        Assert.Contains(result.Problems, p => p.Contains("Valuta 23", StringComparison.Ordinal) && p.Contains("geen naam", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_wrong_currency_id_is_caught()
    {
        // A mistyped id that is another currency: karma is 2, laurels are 3.
        _api.Currencies[2] = "Karma";
        _api.Currencies[3] = "Laurel";

        var result = await RecipePackVerifier.VerifyAsync(Load("""{ "currencyId": 3, "count": 25, "name": "Karma" }"""), _api);

        var problem = Assert.Single(result.Problems);
        Assert.Contains("Laurel", problem, StringComparison.Ordinal);
        Assert.Contains("Karma", problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_currency_that_does_not_exist_is_caught()
    {
        var result = await RecipePackVerifier.VerifyAsync(Load("""{ "currencyId": 9999, "count": 1, "name": "Imaginary" }"""), _api);

        Assert.Contains(result.Problems, p => p.Contains("bestaat niet", StringComparison.Ordinal) && p.Contains("9999", StringComparison.Ordinal));
    }
}
