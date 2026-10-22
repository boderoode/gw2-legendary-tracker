using Gw2LegendaryTracker.Core.Crafting;
using Gw2LegendaryTracker.Core.Items;

namespace Gw2LegendaryTracker.Tests;

public class MysticForgeDescriptionTests
{
    // Texts as returned by the official API.
    private const string GiftOfMight =
        "A gift used to create legendary weapons.\n\nMade by combining these items in the Mystic Forge:\n• 250 Vicious Fangs\n• 250 Armored Scales\n• 250 Vicious Claws\n• 250 Ancient Bones";

    private const string TwilightWeapon =
        "<c=@flavor>This weapon is used to craft the legendary greatsword Eternity by combining it in the Mystic Forge with:\n• Sunrise\n• 5 Piles of Crystalline Dust\n• 10 Philosopher's Stones</c>";

    [Fact]
    public void Gift_description_yields_its_ingredients()
    {
        var parsed = MysticForgeDescription.TryParse(GiftOfMight);

        Assert.Equal(
            [(250, "Vicious Fangs"), (250, "Armored Scales"), (250, "Vicious Claws"), (250, "Ancient Bones")],
            parsed);
    }

    [Fact]
    public void A_weapon_that_is_only_used_in_the_forge_is_not_read_as_a_recipe()
    {
        Assert.Null(MysticForgeDescription.TryParse(TwilightWeapon));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Refined from Copper Ore and Tin.")]
    [InlineData("Made by combining these items in the Mystic Forge:")]
    public void Descriptions_without_a_listed_recipe_give_nothing(string? description)
    {
        Assert.Null(MysticForgeDescription.TryParse(description));
    }

    [Fact]
    public void A_bullet_without_a_number_counts_as_one_and_markup_is_ignored()
    {
        var parsed = MysticForgeDescription.TryParse("Made by combining these items in the Mystic Forge:\n• <c=@x>Gift of Fortune</c>\n• 3 Mystic Coins");

        Assert.Equal([(1, "Gift of Fortune"), (3, "Mystic Coins")], parsed);
    }

    [Theory]
    [InlineData("Vicious Fangs", "Vicious Fang")]
    [InlineData("Globs of Ectoplasm", "Glob of Ectoplasm")]
    [InlineData("Piles of Crystalline Dust", "Pile of Crystalline Dust")]
    [InlineData("Gift of Might", "gift of might")]
    public void Plural_and_singular_names_compare_equal(string a, string b)
    {
        Assert.Equal(MysticForgeDescription.Normalize(a), MysticForgeDescription.Normalize(b));
    }

    [Fact]
    public void Different_names_stay_different()
    {
        Assert.NotEqual(MysticForgeDescription.Normalize("Large Fang"), MysticForgeDescription.Normalize("Vicious Fang"));
    }
}

public class RecipePackDescriptionCheckTests
{
    private readonly FakeApiClient _api = new();

    private static ItemInfo Item(int id, string name, string? description = null) => new(id, name, null, "Basic", description);

    private FakeApiClient WithItems(params ItemInfo[] items)
    {
        _api.KnownItems = items.ToDictionary(i => i.Id, i => i.Name);
        _api.ItemDescriptions = items.Where(i => i.Description is not null).ToDictionary(i => i.Id, i => i.Description!);
        return _api;
    }

    private static LocalRecipeLoadResult Pack(string ingredients) => LocalRecipeLoader.Parse($$"""
        { "recipes": [ { "id": -1, "output": { "itemId": 19672, "count": 1, "name": "Gift of Might" },
                         "ingredients": [ {{ingredients}} ] } ] }
        """);

    private const string Description =
        "Made by combining these items in the Mystic Forge:\n• 250 Vicious Fangs\n• 250 Ancient Bones";

    [Fact]
    public async Task A_recipe_matching_the_official_description_is_marked_verified()
    {
        var api = WithItems(Item(19672, "Gift of Might", Description), Item(24357, "Vicious Fang"), Item(24358, "Ancient Bone"));
        var pack = Pack("""{ "itemId": 24357, "count": 250, "name": "Vicious Fang" }, { "itemId": 24358, "count": 250, "name": "Ancient Bone" }""");

        var result = await RecipePackVerifier.VerifyAsync(pack, api);

        Assert.True(result.IsClean);
        Assert.Equal([-1], result.DescriptionVerifiedRecipeIds);
    }

    [Fact]
    public async Task A_wrong_quantity_is_a_problem()
    {
        var api = WithItems(Item(19672, "Gift of Might", Description), Item(24357, "Vicious Fang"), Item(24358, "Ancient Bone"));
        var pack = Pack("""{ "itemId": 24357, "count": 200, "name": "Vicious Fang" }, { "itemId": 24358, "count": 250, "name": "Ancient Bone" }""");

        var result = await RecipePackVerifier.VerifyAsync(pack, api);

        Assert.False(result.IsClean);
        Assert.Contains("Gift of Might", Assert.Single(result.Problems), StringComparison.Ordinal);
        Assert.Empty(result.DescriptionVerifiedRecipeIds);
    }

    [Fact]
    public async Task A_missing_ingredient_is_a_problem()
    {
        var api = WithItems(Item(19672, "Gift of Might", Description), Item(24357, "Vicious Fang"));
        var pack = Pack("""{ "itemId": 24357, "count": 250, "name": "Vicious Fang" }""");

        Assert.False((await RecipePackVerifier.VerifyAsync(pack, api)).IsClean);
    }

    [Fact]
    public async Task Items_whose_description_states_no_recipe_are_left_unverified_without_a_problem()
    {
        var api = WithItems(Item(19672, "Gift of Might", "Some flavour text."), Item(24357, "Vicious Fang"));
        var pack = Pack("""{ "itemId": 24357, "count": 250, "name": "Vicious Fang" }""");

        var result = await RecipePackVerifier.VerifyAsync(pack, api);

        Assert.True(result.IsClean);
        Assert.Empty(result.DescriptionVerifiedRecipeIds);
    }

    [Fact]
    public async Task With_several_recipes_for_one_item_one_match_is_enough()
    {
        var api = WithItems(Item(19672, "Gift of Might", Description), Item(24357, "Vicious Fang"), Item(24358, "Ancient Bone"), Item(9, "Other"));
        var pack = LocalRecipeLoader.Parse("""
            { "recipes": [
              { "id": -1, "output": { "itemId": 19672, "count": 1, "name": "Gift of Might" }, "ingredients": [ { "itemId": 9, "count": 1, "name": "Other" } ] },
              { "id": -2, "output": { "itemId": 19672, "count": 1, "name": "Gift of Might" },
                "ingredients": [ { "itemId": 24357, "count": 250, "name": "Vicious Fang" }, { "itemId": 24358, "count": 250, "name": "Ancient Bone" } ] }
            ] }
            """);

        var result = await RecipePackVerifier.VerifyAsync(pack, api);

        Assert.True(result.IsClean);
        Assert.Equal([-2], result.DescriptionVerifiedRecipeIds);
    }

    [Fact]
    public async Task Recipes_with_currencies_cannot_be_compared_with_the_description()
    {
        var api = WithItems(Item(19672, "Gift of Might", Description), Item(24357, "Vicious Fang"), Item(24358, "Ancient Bone"));
        var pack = Pack("""{ "itemId": 24357, "count": 250, "name": "Vicious Fang" }, { "itemId": 24358, "count": 250, "name": "Ancient Bone" }, { "currencyId": 2, "count": 1 }""");

        var result = await RecipePackVerifier.VerifyAsync(pack, api);

        Assert.False(result.IsClean);
    }
}
