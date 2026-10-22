using Gw2LegendaryTracker.Core.Crafting;
using Gw2LegendaryTracker.Core.Crafting.Forge;
using Gw2LegendaryTracker.Core.Gw2;

namespace Gw2LegendaryTracker.Tests;

public class ForgeRecipeCatalogTests
{
    private const int Root = 100;
    private const int GiftA = 101;
    private const int GiftB = 102;
    private const int Material = 900;

    private readonly FakeApiClient _official = new();
    private readonly FakeForgeApiClient _forge = new();
    private readonly FakeForgeRecipeStore _store = new();
    private readonly FakeApiKeyStore _keys = new() { Key = "forge-key" };
    private readonly ManualTime _time = new();
    private readonly List<TimeSpan> _delays = [];

    private ForgeRecipeCatalog CreateCatalog() => new(
        _forge,
        _keys,
        _store,
        _official,
        _time,
        TimeSpan.FromMilliseconds(1600),
        (delay, _) =>
        {
            _delays.Add(delay);
            return Task.CompletedTask;
        });

    private static ForgeRecipe Forged(int output, params (int Item, long Count)[] ingredients) =>
        new(output, 1, 1, [.. ingredients.Select(i => new Ingredient(MaterialKey.Item(i.Item), i.Count))]);

    /// <summary>Root needs GiftA and GiftB; GiftA needs 250 of a raw material; GiftB has no forge recipe.</summary>
    private void ServeSmallTree()
    {
        _forge.Recipes[Root] = [Forged(Root, (GiftA, 1), (GiftB, 1))];
        _forge.Recipes[GiftA] = [Forged(GiftA, (Material, 250))];
        _official.Materials = [Material];
    }

    [Fact]
    public async Task Without_a_key_nothing_is_requested()
    {
        _keys.Key = null;

        var result = await CreateCatalog().EnsureAsync(Root, RecipeBook.Empty);

        Assert.True(result.MissingKey);
        Assert.False(result.Completed);
        Assert.Empty(_forge.Requests);
    }

    [Fact]
    public async Task The_whole_tree_below_the_goal_is_fetched_but_raw_materials_are_not()
    {
        ServeSmallTree();

        var result = await CreateCatalog().EnsureAsync(Root, RecipeBook.Empty);

        Assert.True(result.Completed);
        Assert.Equal([Root, GiftA, GiftB], _forge.Requests);
        Assert.Equal(3, result.Fetched);
        Assert.Equal("forge-key", _forge.LastKey);
        Assert.Single(_store.Stored(Root));
        Assert.Single(_store.Stored(GiftA));
        Assert.Empty(_store.Stored(GiftB));
        Assert.True(_store.HasLookup(GiftB), "an item without recipe is remembered too");
    }

    [Fact]
    public async Task Requests_are_spaced_but_the_first_one_does_not_wait()
    {
        ServeSmallTree();

        await CreateCatalog().EnsureAsync(Root, RecipeBook.Empty);

        Assert.Equal(2, _delays.Count);
        Assert.All(_delays, d => Assert.Equal(TimeSpan.FromMilliseconds(1600), d));
    }

    [Fact]
    public async Task A_second_run_within_the_maximum_age_uses_the_cache()
    {
        ServeSmallTree();
        var catalog = CreateCatalog();
        await catalog.EnsureAsync(Root, RecipeBook.Empty);
        _forge.Requests.Clear();
        _time.Advance(TimeSpan.FromDays(3));

        var result = await catalog.EnsureAsync(Root, RecipeBook.Empty);

        Assert.Empty(_forge.Requests);
        Assert.Equal((0, 3), (result.Fetched, result.FromCache));
        Assert.True(result.Completed);
    }

    [Fact]
    public async Task Old_entries_are_fetched_again()
    {
        ServeSmallTree();
        var catalog = CreateCatalog();
        await catalog.EnsureAsync(Root, RecipeBook.Empty);
        _forge.Requests.Clear();
        _time.Advance(ForgeRecipeCatalog.DefaultMaxAge + TimeSpan.FromMinutes(1));

        await catalog.EnsureAsync(Root, RecipeBook.Empty);

        Assert.Equal([Root, GiftA, GiftB], _forge.Requests);
    }

    [Fact]
    public async Task Forcing_a_refresh_ignores_the_cache()
    {
        ServeSmallTree();
        var catalog = CreateCatalog();
        await catalog.EnsureAsync(Root, RecipeBook.Empty);
        _forge.Requests.Clear();

        await catalog.EnsureAsync(Root, RecipeBook.Empty, forceRefresh: true);

        Assert.Equal(3, _forge.Requests.Count);
    }

    [Fact]
    public async Task A_changed_recipe_replaces_the_stored_one()
    {
        ServeSmallTree();
        var catalog = CreateCatalog();
        await catalog.EnsureAsync(Root, RecipeBook.Empty);
        _forge.Recipes[GiftA] = [Forged(GiftA, (Material, 300))];

        await catalog.EnsureAsync(Root, RecipeBook.Empty, forceRefresh: true);

        var stored = Assert.Single(_store.Stored(GiftA));
        Assert.Equal(300, stored.Recipe.Ingredients[0].Count);
    }

    [Fact]
    public async Task Random_outcome_recipes_are_skipped_and_counted()
    {
        _forge.Recipes[Root] = [new ForgeRecipe(Root, 1, 3, [new Ingredient(MaterialKey.Item(Material), 3)]), Forged(Root, (GiftA, 1))];
        _official.Materials = [Material];

        var result = await CreateCatalog().EnsureAsync(Root, RecipeBook.Empty);

        Assert.Equal(1, result.RandomSkipped);
        var stored = Assert.Single(_store.Stored(Root));
        Assert.Equal(GiftA, stored.Recipe.Ingredients[0].Key.Id);
    }

    [Fact]
    public async Task Ingredients_of_known_recipes_are_followed_too()
    {
        // GiftB is crafted from the official recipe of Intermediate (500); Intermediate has a forge recipe of its own.
        _forge.Recipes[Root] = [Forged(Root, (500, 1))];
        _forge.Recipes[600] = [Forged(600, (Material, 1))];
        _official.Materials = [Material];
        var known = new RecipeBook([CraftingTestHelpers.Recipe(1, 500, 1, (600, 1))]);

        await CreateCatalog().EnsureAsync(Root, known);

        Assert.Contains(600, _forge.Requests);
    }

    [Fact]
    public async Task Circular_recipes_end()
    {
        _forge.Recipes[GiftA] = [Forged(GiftA, (GiftB, 1))];
        _forge.Recipes[GiftB] = [Forged(GiftB, (GiftA, 1))];

        var result = await CreateCatalog().EnsureAsync(GiftA, RecipeBook.Empty);

        Assert.True(result.Completed);
        Assert.Equal([GiftA, GiftB], _forge.Requests);
    }

    [Fact]
    public async Task A_service_error_stops_the_run_keeps_what_was_saved_and_does_not_throw()
    {
        ServeSmallTree();
        _forge.Failure = new Gw2ApiException(Gw2ApiErrorKind.RateLimited, "Te veel verzoeken.");
        _forge.FailOnItem = GiftA;

        var result = await CreateCatalog().EnsureAsync(Root, RecipeBook.Empty);

        Assert.Equal("Te veel verzoeken.", result.Error);
        Assert.False(result.Completed);
        Assert.Equal(1, result.Fetched);
        Assert.Single(_store.Stored(Root));
        Assert.False(_store.HasLookup(GiftA), "a failed lookup must not be remembered as 'no recipe'");
    }

    [Fact]
    public async Task A_run_resumes_where_it_stopped()
    {
        ServeSmallTree();
        _forge.Failure = new Gw2ApiException(Gw2ApiErrorKind.Offline, "Geen verbinding.");
        _forge.FailOnItem = GiftA;
        var catalog = CreateCatalog();
        await catalog.EnsureAsync(Root, RecipeBook.Empty);
        _forge.Failure = null;
        _forge.Requests.Clear();

        var result = await catalog.EnsureAsync(Root, RecipeBook.Empty);

        Assert.Equal([GiftA, GiftB], _forge.Requests);
        Assert.True(result.Completed);
    }

    [Fact]
    public async Task Huge_trees_stop_at_the_request_limit_and_say_so()
    {
        _forge.Recipes[Root] = [Forged(Root, [.. Enumerable.Range(1000, 400).Select(i => (i, 1L))])];

        var result = await CreateCatalog().EnsureAsync(Root, RecipeBook.Empty);

        Assert.True(result.Truncated);
        Assert.False(result.Completed);
        Assert.Equal(ForgeRecipeCatalog.MaxRequestsPerRun, _forge.Requests.Count);
    }

    [Fact]
    public async Task Failing_to_read_the_material_list_is_not_fatal()
    {
        ServeSmallTree();
        _official.Materials = [];

        var result = await CreateCatalog().EnsureAsync(Root, RecipeBook.Empty);

        Assert.True(result.Completed);
        Assert.Contains(Material, _forge.Requests);
    }

    [Fact]
    public async Task Progress_is_reported_per_item()
    {
        ServeSmallTree();
        var reports = new List<ForgeProgress>();

        await CreateCatalog().EnsureAsync(Root, RecipeBook.Empty, progress: new SynchronousProgress<ForgeProgress>(reports.Add));

        Assert.Equal(3, reports.Count);
        Assert.Equal(3, reports[^1].Visited);
        Assert.Equal(0, reports[^1].Queued);
    }

    // ---- verification against the official description

    private void ServeVerifiableGift(string? description, params (int Item, long Count, string Name)[] ingredients)
    {
        _forge.Recipes[GiftA] = [Forged(GiftA, [.. ingredients.Select(i => (i.Item, i.Count))])];
        var names = new Dictionary<int, string> { [GiftA] = "Gift of A" };
        foreach (var (item, _, name) in ingredients)
        {
            names[item] = name;
        }

        _official.KnownItems = names;
        _official.ItemDescriptions = description is null ? [] : new Dictionary<int, string> { [GiftA] = description };
        _official.Materials = [.. ingredients.Select(i => i.Item)];
    }

    [Fact]
    public async Task A_recipe_that_matches_the_official_description_is_marked_verified()
    {
        ServeVerifiableGift(
            "Made by combining these items in the Mystic Forge:\n• 250 Vicious Fangs\n• 3 Ancient Bones",
            (1, 250, "Vicious Fang"),
            (2, 3, "Ancient Bone"));

        var result = await CreateCatalog().EnsureAsync(GiftA, RecipeBook.Empty);

        Assert.Empty(result.Rejected);
        Assert.True(Assert.Single(_store.Stored(GiftA)).Verified);
    }

    [Fact]
    public async Task A_recipe_that_contradicts_the_description_is_rejected_and_not_stored()
    {
        ServeVerifiableGift(
            "Made by combining these items in the Mystic Forge:\n• 250 Vicious Fangs\n• 3 Ancient Bones",
            (1, 200, "Vicious Fang"),
            (2, 3, "Ancient Bone"));

        var result = await CreateCatalog().EnsureAsync(GiftA, RecipeBook.Empty);

        var message = Assert.Single(result.Rejected);
        Assert.Contains("Gift of A", message, StringComparison.Ordinal);
        Assert.Empty(_store.Stored(GiftA));
        Assert.True(_store.HasLookup(GiftA));
    }

    [Fact]
    public async Task Without_a_description_to_compare_the_recipe_is_kept_as_single_source()
    {
        ServeVerifiableGift(null, (1, 250, "Vicious Fang"));

        var result = await CreateCatalog().EnsureAsync(GiftA, RecipeBook.Empty);

        Assert.Empty(result.Rejected);
        Assert.False(Assert.Single(_store.Stored(GiftA)).Verified);
        Assert.Contains(Assert.Single(_store.Stored(GiftA)).Recipe.Id, (await _store.LoadAllAsync()).SingleSourceRecipeIds);
    }

    [Fact]
    public async Task With_alternatives_one_match_is_enough_and_the_others_stay_single_source()
    {
        ServeVerifiableGift(
            "Made by combining these items in the Mystic Forge:\n• 250 Vicious Fangs",
            (1, 250, "Vicious Fang"));
        _forge.Recipes[GiftA].Add(Forged(GiftA, (1, 9)));

        var result = await CreateCatalog().EnsureAsync(GiftA, RecipeBook.Empty);

        Assert.Empty(result.Rejected);
        Assert.Equal([true, false], _store.Stored(GiftA).Select(s => s.Verified));
    }

    [Fact]
    public async Task When_the_official_lookup_fails_recipes_are_kept_unverified()
    {
        ServeVerifiableGift("Made by combining these items in the Mystic Forge:\n• 250 Vicious Fangs", (1, 250, "Vicious Fang"));
        _official.ItemFailure = new Gw2ApiException(Gw2ApiErrorKind.Offline, "offline");

        var result = await CreateCatalog().EnsureAsync(GiftA, RecipeBook.Empty);

        Assert.Empty(result.Rejected);
        Assert.False(Assert.Single(_store.Stored(GiftA)).Verified);
    }
}

/// <summary>Reports on the calling thread, so tests can assert right after the call.</summary>
internal sealed class SynchronousProgress<T> : IProgress<T>
{
    private readonly Action<T> _handler;

    public SynchronousProgress(Action<T> handler) => _handler = handler;

    public void Report(T value) => _handler(value);
}
