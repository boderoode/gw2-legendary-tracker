using System.Net;
using Gw2LegendaryTracker.Core.Crafting;
using Gw2LegendaryTracker.Core.Crafting.Forge;
using Gw2LegendaryTracker.Core.Goals;
using Gw2LegendaryTracker.Core.Gw2;
using Gw2LegendaryTracker.Infrastructure.Gw2;
using Gw2LegendaryTracker.Infrastructure.Persistence;
using static Gw2LegendaryTracker.Tests.CraftingTestHelpers;

namespace Gw2LegendaryTracker.Tests;

public class ForgeApiClientTests
{
    private const string Key = "d1d1d1d1-2222-3333-4444-555555555555";

    private static ForgeApiClient CreateClient(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://api.gw2treasures.com/") });

    [Fact]
    public async Task Recipes_are_requested_with_the_key_only_in_the_header_and_mapped()
    {
        var handler = new StubHandler(_ => StubHandler.Json("""
            [{"_gw2treasures_recipe_id":"abc","output_item_id":30704,"output_item_count_min":1,"output_item_count_max":1,
              "ingredients":[{"item_id":29185,"count":1},{"item_id":19674,"count":2}]}]
            """));

        var recipes = await CreateClient(handler).GetRecipesAsync(30704, Key);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("/items/30704/mystic-forge", request.RequestUri!.PathAndQuery);
        Assert.Equal(Key, request.Headers.Authorization?.Parameter);
        Assert.DoesNotContain(Key, request.RequestUri.ToString(), StringComparison.Ordinal);
        var recipe = Assert.Single(recipes);
        Assert.Equal(30704, recipe.OutputItemId);
        Assert.False(recipe.IsRandom);
        Assert.Equal([new Ingredient(MaterialKey.Item(29185), 1), new Ingredient(MaterialKey.Item(19674), 2)], recipe.Ingredients);
    }

    [Fact]
    public async Task An_item_without_recipes_gives_an_empty_list()
    {
        var recipes = await CreateClient(new StubHandler(_ => StubHandler.Json("[]"))).GetRecipesAsync(29185, Key);

        Assert.Empty(recipes);
    }

    [Fact]
    public async Task Random_outcomes_are_flagged()
    {
        var handler = new StubHandler(_ => StubHandler.Json("""[{"output_item_id":1,"output_item_count_min":1,"output_item_count_max":4,"ingredients":[{"item_id":2,"count":3}]}]"""));

        Assert.True(Assert.Single(await CreateClient(handler).GetRecipesAsync(1, Key)).IsRandom);
    }

    [Fact]
    public async Task Recipes_that_cannot_be_interpreted_completely_are_dropped()
    {
        var handler = new StubHandler(_ => StubHandler.Json("""
            [
              {"output_item_id":1,"output_item_count_min":1,"output_item_count_max":1,"ingredients":[{"item_id":2,"count":1}]},
              {"output_item_id":1,"output_item_count_min":1,"output_item_count_max":1,"ingredients":[{"count":1}]},
              {"output_item_id":1,"output_item_count_min":1,"output_item_count_max":1,"ingredients":[{"item_id":2,"count":0}]},
              {"output_item_id":1,"output_item_count_min":0,"output_item_count_max":0,"ingredients":[{"item_id":2,"count":1}]},
              {"output_item_id":1,"output_item_count_min":3,"output_item_count_max":1,"ingredients":[{"item_id":2,"count":1}]},
              {"output_item_id":1,"output_item_count_min":1,"output_item_count_max":1,"ingredients":[]}
            ]
            """));

        Assert.Single(await CreateClient(handler).GetRecipesAsync(1, Key));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, Gw2ApiErrorKind.InvalidKey)]
    [InlineData(HttpStatusCode.Forbidden, Gw2ApiErrorKind.MissingPermissions)]
    [InlineData(HttpStatusCode.TooManyRequests, Gw2ApiErrorKind.RateLimited)]
    [InlineData(HttpStatusCode.BadGateway, Gw2ApiErrorKind.ServerError)]
    [InlineData(HttpStatusCode.NotFound, Gw2ApiErrorKind.Unexpected)]
    public async Task Http_errors_map_to_friendly_error_kinds_without_leaking_the_key(HttpStatusCode status, Gw2ApiErrorKind expected)
    {
        var client = CreateClient(new StubHandler(_ => StubHandler.Json("""{"error":401,"text":"Invalid API key"}""", status)));

        var ex = await Assert.ThrowsAsync<Gw2ApiException>(() => client.GetRecipesAsync(1, Key));

        Assert.Equal(expected, ex.Kind);
        Assert.DoesNotContain(Key, ex.Message, StringComparison.Ordinal);
        Assert.Contains("gw2treasures", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Network_failure_and_timeout_map_to_offline()
    {
        var offline = CreateClient(new ThrowingHandler(new HttpRequestException("no route")));
        var timeout = CreateClient(new ThrowingHandler(new TaskCanceledException("timeout")));

        Assert.Equal(Gw2ApiErrorKind.Offline, (await Assert.ThrowsAsync<Gw2ApiException>(() => offline.GetRecipesAsync(1, Key))).Kind);
        Assert.Equal(Gw2ApiErrorKind.Offline, (await Assert.ThrowsAsync<Gw2ApiException>(() => timeout.GetRecipesAsync(1, Key))).Kind);
    }

    [Fact]
    public async Task Malformed_json_maps_to_unexpected()
    {
        var client = CreateClient(new StubHandler(_ => StubHandler.Json("<html>")));

        Assert.Equal(Gw2ApiErrorKind.Unexpected, (await Assert.ThrowsAsync<Gw2ApiException>(() => client.GetRecipesAsync(1, Key))).Kind);
    }

    [Fact]
    public async Task A_blank_key_is_rejected_before_any_request()
    {
        var handler = new StubHandler(_ => StubHandler.Json("[]"));

        await Assert.ThrowsAnyAsync<ArgumentException>(() => CreateClient(handler).GetRecipesAsync(1, " "));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Raw_material_ids_come_from_the_official_endpoint_without_duplicates()
    {
        var handler = new StubHandler(_ => StubHandler.Json("""[{"id":5,"name":"Gem","items":[1,2,3]},{"id":6,"name":"Ore","items":[3,4]}]"""));
        var client = new Gw2ApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.guildwars2.com/") });

        var ids = await client.GetMaterialItemIdsAsync();

        Assert.Equal("/v2/materials?ids=all", Assert.Single(handler.Requests).RequestUri!.PathAndQuery);
        Assert.Equal([1, 2, 3, 4], ids.Order());
    }
}

public class SqliteForgeRecipeStoreTests
{
    private static readonly DateTimeOffset At = new(2026, 2, 3, 4, 5, 6, TimeSpan.Zero);

    private static ForgeRecipeDraft Draft(int count, bool verified, params (int Item, long Count)[] ingredients) =>
        new(count, [.. ingredients.Select(i => new Ingredient(MaterialKey.Item(i.Item), i.Count))], verified);

    [Fact]
    public async Task Recipes_round_trip_with_negative_ids_below_the_base_and_keep_ingredient_order()
    {
        using var temp = new TempDatabase();
        await temp.Database.MigrateAsync();
        var store = new SqliteForgeRecipeStore(temp.Database);

        await store.SaveAsync(100, [Draft(1, true, (9, 2), (4, 1), (7, 5))], At);
        var recipes = await store.GetRecipesForAsync(100);

        var recipe = Assert.Single(recipes);
        Assert.True(recipe.Id <= -IForgeRecipeStore.IdBase);
        Assert.Equal(RecipeSource.Local, recipe.Source);
        Assert.Equal(100, recipe.OutputItemId);
        Assert.Equal([9, 4, 7], recipe.Ingredients.Select(i => i.Key.Id));
        Assert.Equal([2L, 1L, 5L], recipe.Ingredients.Select(i => i.Count));
    }

    [Fact]
    public async Task Lookups_are_remembered_also_when_the_item_has_no_recipe()
    {
        using var temp = new TempDatabase();
        await temp.Database.MigrateAsync();
        var store = new SqliteForgeRecipeStore(temp.Database);
        Assert.Null(await store.GetFetchedUtcAsync(55));

        await store.SaveAsync(55, [], At);

        Assert.Equal(At, await store.GetFetchedUtcAsync(55));
        Assert.Empty(await store.GetRecipesForAsync(55));
    }

    [Fact]
    public async Task Saving_again_replaces_recipes_and_ingredients_of_that_item_only()
    {
        using var temp = new TempDatabase();
        await temp.Database.MigrateAsync();
        var store = new SqliteForgeRecipeStore(temp.Database);
        await store.SaveAsync(100, [Draft(1, true, (1, 1), (2, 2))], At);
        await store.SaveAsync(200, [Draft(1, true, (3, 3))], At);

        await store.SaveAsync(100, [Draft(2, false, (8, 8))], At.AddDays(1));

        var changed = Assert.Single(await store.GetRecipesForAsync(100));
        Assert.Equal(2, changed.OutputCount);
        Assert.Equal([new Ingredient(MaterialKey.Item(8), 8)], changed.Ingredients);
        Assert.Equal(At.AddDays(1), await store.GetFetchedUtcAsync(100));
        Assert.Single(await store.GetRecipesForAsync(200));
    }

    [Fact]
    public async Task Load_all_returns_everything_and_flags_single_source_recipes()
    {
        using var temp = new TempDatabase();
        await temp.Database.MigrateAsync();
        var store = new SqliteForgeRecipeStore(temp.Database);
        await store.SaveAsync(100, [Draft(1, true, (1, 1))], At);
        await store.SaveAsync(200, [Draft(1, false, (2, 1))], At);

        var all = await store.LoadAllAsync();

        Assert.Equal(2, all.Recipes.Count);
        var single = Assert.Single(all.SingleSourceRecipeIds);
        Assert.Equal(200, all.Recipes.Single(r => r.Id == single).OutputItemId);
    }

    [Fact]
    public async Task Recipe_ids_stay_unique_after_a_replace()
    {
        using var temp = new TempDatabase();
        await temp.Database.MigrateAsync();
        var store = new SqliteForgeRecipeStore(temp.Database);
        await store.SaveAsync(100, [Draft(1, true, (1, 1))], At);
        var firstId = Assert.Single(await store.GetRecipesForAsync(100)).Id;

        await store.SaveAsync(100, [Draft(1, true, (1, 1))], At.AddDays(1));
        var secondId = Assert.Single(await store.GetRecipesForAsync(100)).Id;

        Assert.NotEqual(firstId, secondId);
    }
}

public class ForgeConnectionServiceTests
{
    /// <summary>Synthetic key with the real shape. Never use a real key in tests.</summary>
    private const string Key = "aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee";

    private readonly FakeForgeApiClient _forge = new();
    private readonly FakeApiKeyStore _keys = new();

    [Fact]
    public async Task An_accepted_key_is_stored_in_its_normal_form()
    {
        var result = await new ForgeConnectionService(_forge, _keys).ConnectAsync($"  {Key.ToUpperInvariant()}  ");

        Assert.True(result.IsConnected);
        Assert.Equal(Key, _keys.Key);
        Assert.Equal(Key, _forge.LastKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a key")]
    [InlineData("aaaaaaaa-bbbb-4ccc-8ddd")]
    public async Task A_malformed_key_is_rejected_without_a_request(string? input)
    {
        var result = await new ForgeConnectionService(_forge, _keys).ConnectAsync(input);

        Assert.False(result.IsConnected);
        Assert.Empty(_forge.Requests);
        Assert.Null(_keys.Key);
    }

    [Fact]
    public async Task A_key_the_service_refuses_is_not_stored_and_the_reason_is_shown()
    {
        _forge.Failure = new Gw2ApiException(Gw2ApiErrorKind.InvalidKey, "De gw2treasures-key is ongeldig.");

        var result = await new ForgeConnectionService(_forge, _keys).ConnectAsync(Key);

        Assert.False(result.IsConnected);
        Assert.Equal("De gw2treasures-key is ongeldig.", result.Message);
        Assert.Null(_keys.Key);
    }

    [Fact]
    public async Task Disconnect_removes_the_key()
    {
        var service = new ForgeConnectionService(_forge, _keys);
        await service.ConnectAsync(Key);

        await service.DisconnectAsync();

        Assert.False(await service.HasKeyAsync());
    }
}

public class LegendaryCatalogRefreshTests
{
    private readonly FakeApiClient _api = new();
    private readonly FakeLegendaryStore _store = new();
    private readonly ManualTime _time = new();

    private LegendaryCatalog CreateCatalog() => new(_api, _store, _time);

    [Fact]
    public async Task Refresh_stores_the_list_and_when_it_was_fetched()
    {
        _api.LegendaryIds = [3, 1, 2];

        var ids = await CreateCatalog().RefreshAsync();

        Assert.Equal([3, 1, 2], ids);
        Assert.Equal(_time.GetUtcNow(), _store.FetchedUtc);
    }

    [Fact]
    public async Task An_empty_answer_never_replaces_a_stored_list()
    {
        _store.Ids.AddRange([7, 8]);
        _api.LegendaryIds = [];

        Assert.Equal([7, 8], await CreateCatalog().RefreshAsync());
        Assert.Equal([7, 8], _store.Ids);
    }

    [Fact]
    public async Task The_list_is_fetched_once_per_day()
    {
        _api.LegendaryIds = [1, 2];
        var catalog = CreateCatalog();
        await catalog.RefreshIfStaleAsync();

        _api.LegendaryIds = [1, 2, 3];
        _time.Advance(TimeSpan.FromHours(23));
        var within = await catalog.RefreshIfStaleAsync();
        _time.Advance(TimeSpan.FromHours(2));
        var after = await catalog.RefreshIfStaleAsync();

        Assert.Equal([1, 2], within);
        Assert.Equal([1, 2, 3], after);
    }

    [Fact]
    public async Task A_failed_daily_refresh_leaves_the_stored_list_available()
    {
        _store.Ids.AddRange([7, 8]);
        _store.FetchedUtc = _time.GetUtcNow() - TimeSpan.FromDays(2);
        var failing = new FailingLegendaryApi();

        await Assert.ThrowsAsync<Gw2ApiException>(() => new LegendaryCatalog(failing, _store, _time).RefreshIfStaleAsync());

        Assert.Equal([7, 8], await new LegendaryCatalog(_api, _store, _time).LoadIdsAsync());
    }

    private sealed class FailingLegendaryApi : IGw2ApiClient
    {
        private static Gw2ApiException Offline() => new(Gw2ApiErrorKind.Offline, "offline");

        public Task<IReadOnlyList<int>> GetLegendaryItemIdsAsync(CancellationToken cancellationToken = default) => throw Offline();

        public Task<Gw2TokenInfo> GetTokenInfoAsync(string apiKey, CancellationToken cancellationToken = default) => throw Offline();

        public Task<AccountSnapshot> GetAccountSnapshotAsync(string apiKey, DateTimeOffset capturedUtc, CancellationToken cancellationToken = default) => throw Offline();

        public Task<IReadOnlyList<Core.Items.ItemInfo>> GetItemsAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default) => throw Offline();

        public Task<IReadOnlyList<int>> GetRecipeIdsAsync(CancellationToken cancellationToken = default) => throw Offline();

        public Task<IReadOnlyList<Recipe>> GetRecipesAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default) => throw Offline();

        public Task<IReadOnlyList<int>> GetMaterialItemIdsAsync(CancellationToken cancellationToken = default) => throw Offline();

        public Task<IReadOnlyDictionary<int, string>> GetCurrencyNamesAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default) => throw Offline();
    }
}

public class RecipeBookMergeTests
{
    [Fact]
    public void The_same_recipe_from_two_sources_is_listed_once_keeping_the_lowest_id()
    {
        var fresh = Recipe(-1_000_001, 10, 1, (2, 5), (3, 1));
        var shipped = Recipe(-1, 10, 1, (3, 1), (2, 5));

        var book = new RecipeBook([shipped, fresh]);

        var only = Assert.Single(book.RecipesFor(10));
        Assert.Equal(-1_000_001, only.Id);
        Assert.True(book.Contains(-1_000_001));
        Assert.False(book.Contains(-1));
    }

    [Fact]
    public void Recipes_that_differ_in_quantity_or_output_stay_separate_alternatives()
    {
        var book = new RecipeBook([Recipe(1, 10, 1, (2, 5)), Recipe(2, 10, 1, (2, 6)), Recipe(3, 10, 2, (2, 5))]);

        Assert.Equal([1, 2, 3], book.RecipesFor(10).Select(r => r.Id));
    }

    [Fact]
    public async Task The_provider_merges_official_downloaded_and_local_recipes_and_counts_single_source_ones()
    {
        var api = new FakeApiClient();
        var apiStore = new FakeRecipeStore();
        apiStore.Recipes[5] = Recipe(5, 100, 1, (1, 1));
        var forgeStore = new FakeForgeRecipeStore();
        await forgeStore.SaveAsync(200, [new ForgeRecipeDraft(1, [new Ingredient(MaterialKey.Item(2), 1)], false)], DateTimeOffset.UnixEpoch);
        var forge = new ForgeRecipeCatalog(new FakeForgeApiClient(), new FakeApiKeyStore(), forgeStore, api);
        var local = new LocalRecipes(
            [Recipe(-1, 300, 1, (3, 1)), Recipe(-2, 400, 1, (4, 1))],
            new HashSet<int> { -2 },
            ["a.json: een probleem"]);

        var result = await new RecipeBookProvider(new RecipeCatalog(api, apiStore), forge, _ => Task.FromResult(local)).BuildAsync();

        Assert.All([100, 200, 300, 400], output => Assert.Single(result.Book.RecipesFor(output)));
        Assert.Equal(2, result.SingleSourceCount);
        Assert.Equal(["a.json: een probleem"], result.LocalErrors);
    }

    [Fact]
    public async Task A_local_recipe_replaced_by_an_identical_downloaded_one_is_not_counted_as_single_source()
    {
        var api = new FakeApiClient();
        var forgeStore = new FakeForgeRecipeStore();
        await forgeStore.SaveAsync(300, [new ForgeRecipeDraft(1, [new Ingredient(MaterialKey.Item(3), 1)], true)], DateTimeOffset.UnixEpoch);
        var forge = new ForgeRecipeCatalog(new FakeForgeApiClient(), new FakeApiKeyStore(), forgeStore, api);
        var local = new LocalRecipes([Recipe(-1, 300, 1, (3, 1))], new HashSet<int> { -1 }, []);

        var result = await new RecipeBookProvider(new RecipeCatalog(api, new FakeRecipeStore()), forge, _ => Task.FromResult(local)).BuildAsync();

        Assert.Single(result.Book.RecipesFor(300));
        Assert.Equal(0, result.SingleSourceCount);
    }
}
