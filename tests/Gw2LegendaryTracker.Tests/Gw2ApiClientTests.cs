using System.Net;
using Gw2LegendaryTracker.Core.Crafting;
using Gw2LegendaryTracker.Core.Gw2;
using Gw2LegendaryTracker.Core.Items;
using Gw2LegendaryTracker.Infrastructure.Gw2;

namespace Gw2LegendaryTracker.Tests;

public class Gw2ApiClientTests
{
    private static Gw2ApiClient CreateClient(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://api.guildwars2.com/") });

    [Fact]
    public async Task TokenInfo_sends_key_only_in_authorization_header()
    {
        var handler = new StubHandler(_ => StubHandler.Json("""{"id":"abc","name":"My key","permissions":["account","wallet"]}"""));

        var info = await CreateClient(handler).GetTokenInfoAsync(TestData.Key);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal(TestData.Key, request.Headers.Authorization?.Parameter);
        Assert.DoesNotContain(TestData.Key, request.RequestUri!.ToString(), StringComparison.Ordinal);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(["account", "wallet"], info.Permissions);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, """{"text":"invalid key"}""", Gw2ApiErrorKind.InvalidKey)]
    [InlineData(HttpStatusCode.Unauthorized, """{"text":"Invalid access token"}""", Gw2ApiErrorKind.InvalidKey)]
    [InlineData(HttpStatusCode.Forbidden, """{"text":"requires scope inventories"}""", Gw2ApiErrorKind.MissingPermissions)]
    [InlineData(HttpStatusCode.TooManyRequests, "{}", Gw2ApiErrorKind.RateLimited)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "not json", Gw2ApiErrorKind.ServerError)]
    [InlineData(HttpStatusCode.NotFound, "{}", Gw2ApiErrorKind.Unexpected)]
    public async Task Http_errors_map_to_friendly_error_kinds(HttpStatusCode status, string body, Gw2ApiErrorKind expected)
    {
        var client = CreateClient(new StubHandler(_ => StubHandler.Json(body, status)));

        var ex = await Assert.ThrowsAsync<Gw2ApiException>(() => client.GetTokenInfoAsync(TestData.Key));

        Assert.Equal(expected, ex.Kind);
        Assert.DoesNotContain(TestData.Key, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Network_failure_maps_to_offline()
    {
        var client = CreateClient(new ThrowingHandler(new HttpRequestException("no route")));

        var ex = await Assert.ThrowsAsync<Gw2ApiException>(() => client.GetTokenInfoAsync(TestData.Key));

        Assert.Equal(Gw2ApiErrorKind.Offline, ex.Kind);
    }

    [Fact]
    public async Task Timeout_maps_to_offline_but_caller_cancellation_propagates()
    {
        var timeout = CreateClient(new ThrowingHandler(new TaskCanceledException("timeout")));
        var ex = await Assert.ThrowsAsync<Gw2ApiException>(() => timeout.GetTokenInfoAsync(TestData.Key));
        Assert.Equal(Gw2ApiErrorKind.Offline, ex.Kind);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var cancelled = CreateClient(new ThrowingHandler(new TaskCanceledException("cancelled", null, cts.Token)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled.GetTokenInfoAsync(TestData.Key, cts.Token));
    }

    [Fact]
    public async Task Malformed_json_maps_to_unexpected()
    {
        var client = CreateClient(new StubHandler(_ => StubHandler.Json("<html>")));

        var ex = await Assert.ThrowsAsync<Gw2ApiException>(() => client.GetTokenInfoAsync(TestData.Key));

        Assert.Equal(Gw2ApiErrorKind.Unexpected, ex.Kind);
    }

    [Fact]
    public async Task Snapshot_combines_all_sources_and_skips_empty_slots()
    {
        var handler = new StubHandler(request => StubHandler.Json(request.RequestUri!.PathAndQuery switch
        {
            "/v2/account" => """{"name":"Test.1234"}""",
            "/v2/account/bank" => """[null,{"id":100,"count":2},{"id":101,"count":0}]""",
            "/v2/account/materials" => """[{"id":19721,"category":5,"count":250}]""",
            "/v2/account/inventory" => """[{"id":200,"count":1},null]""",
            "/v2/account/wallet" => """[{"id":1,"value":99999},{"id":23,"value":7}]""",
            "/v2/characters?ids=all" => """[{"name":"Hero One","bags":[null,{"inventory":[null,{"id":300,"count":5}]}]},{"name":"Hero Two","bags":[]}]""",
            var other => throw new InvalidOperationException($"Unexpected request {other}"),
        }));
        var at = new DateTimeOffset(2026, 5, 6, 7, 8, 9, TimeSpan.Zero);

        var snapshot = await CreateClient(handler).GetAccountSnapshotAsync(TestData.Key, at);

        Assert.Equal("Test.1234", snapshot.AccountName);
        Assert.Equal(at, snapshot.CapturedUtc);
        Assert.Equal(
            [
                new InventoryEntry(100, 2, InventorySource.Bank),
                new InventoryEntry(19721, 250, InventorySource.Materials),
                new InventoryEntry(200, 1, InventorySource.SharedInventory),
                new InventoryEntry(300, 5, InventorySource.Character, "Hero One"),
            ],
            snapshot.Items);
        Assert.Equal([new WalletEntry(1, 99999), new WalletEntry(23, 7)], snapshot.Wallet);
    }

    [Fact]
    public async Task Items_are_requested_without_credentials_and_mapped()
    {
        var handler = new StubHandler(_ => StubHandler.Json("""[{"id":1,"name":"Mystic Coin","icon":"https://i/1.png","rarity":"Rare"},{"id":2,"name":"","icon":null}]"""));

        var items = await CreateClient(handler).GetItemsAsync([1, 2]);

        var request = Assert.Single(handler.Requests);
        Assert.Null(request.Headers.Authorization);
        Assert.Equal("/v2/items?ids=1,2&lang=en", request.RequestUri!.PathAndQuery);
        Assert.Equal([new ItemInfo(1, "Mystic Coin", "https://i/1.png", "Rare")], items);
    }

    [Fact]
    public async Task Items_not_found_is_an_empty_result()
    {
        var client = CreateClient(new StubHandler(_ => StubHandler.Json("""{"text":"all ids provided are invalid"}""", HttpStatusCode.NotFound)));

        Assert.Empty(await client.GetItemsAsync([999999999]));
    }

    [Fact]
    public async Task Items_partial_content_is_accepted()
    {
        var client = CreateClient(new StubHandler(_ => StubHandler.Json("""[{"id":1,"name":"Only One","rarity":"Basic"}]""", HttpStatusCode.PartialContent)));

        Assert.Single(await client.GetItemsAsync([1, 999999999]));
    }

    [Fact]
    public async Task Items_rejects_more_than_200_ids_and_skips_the_call_for_none()
    {
        var handler = new StubHandler(_ => StubHandler.Json("[]"));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<ArgumentException>(() => client.GetItemsAsync([.. Enumerable.Range(1, 201)]));
        Assert.Empty(await client.GetItemsAsync([]));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Recipe_ids_come_from_the_public_endpoint_without_credentials()
    {
        var handler = new StubHandler(_ => StubHandler.Json("[1,2,3]"));

        var ids = await CreateClient(handler).GetRecipeIdsAsync();

        var request = Assert.Single(handler.Requests);
        Assert.Null(request.Headers.Authorization);
        Assert.Equal("/v2/recipes", request.RequestUri!.PathAndQuery);
        Assert.Equal([1, 2, 3], ids);
    }

    [Fact]
    public async Task Recipes_map_snake_case_fields_and_flag_guild_recipes()
    {
        var handler = new StubHandler(_ => StubHandler.Json("""
            [
              {"id":1,"type":"Weapon","output_item_id":100,"output_item_count":3,"time_to_craft_ms":1000,
               "disciplines":["Weaponsmith"],"min_rating":400,"flags":["AutoLearned"],
               "ingredients":[{"item_id":10,"count":2},{"item_id":11,"count":1}],"guild_ingredients":[],"chat_link":"x"},
              {"id":2,"type":"GuildDecoration","output_item_id":200,"output_item_count":1,
               "disciplines":[],"ingredients":[{"item_id":10,"count":1}],"guild_ingredients":[{"upgrade_id":5,"count":1}]}
            ]
            """));

        var recipes = await CreateClient(handler).GetRecipesAsync([1, 2]);

        Assert.Equal("/v2/recipes?ids=1,2", Assert.Single(handler.Requests).RequestUri!.PathAndQuery);
        var plain = recipes[0];
        Assert.Equal((1, 100, 3), (plain.Id, plain.OutputItemId, plain.OutputCount));
        Assert.Equal([new Ingredient(MaterialKey.Item(10), 2), new Ingredient(MaterialKey.Item(11), 1)], plain.Ingredients);
        Assert.Equal(["Weaponsmith"], plain.Disciplines);
        Assert.False(plain.RequiresGuildUpgrades);
        Assert.True(recipes[1].RequiresGuildUpgrades);
    }

    [Fact]
    public async Task Recipes_that_cannot_be_interpreted_completely_are_dropped()
    {
        var handler = new StubHandler(_ => StubHandler.Json("""
            [
              {"id":1,"output_item_id":100,"output_item_count":1,"ingredients":[{"item_id":10,"count":1}]},
              {"id":2,"output_item_id":100,"output_item_count":1,"ingredients":[{"count":1}]},
              {"id":3,"output_item_id":100,"output_item_count":1,"ingredients":[{"item_id":10,"count":0}]},
              {"id":4,"output_item_id":100,"output_item_count":0,"ingredients":[{"item_id":10,"count":1}]},
              {"id":5,"output_item_id":100,"output_item_count":1,"ingredients":[]}
            ]
            """));

        var recipes = await CreateClient(handler).GetRecipesAsync([1, 2, 3, 4, 5]);

        Assert.Equal([1], recipes.Select(r => r.Id));
    }

    [Fact]
    public async Task Recipes_rejects_more_than_200_ids_and_skips_the_call_for_none()
    {
        var handler = new StubHandler(_ => StubHandler.Json("[]"));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<ArgumentException>(() => client.GetRecipesAsync([.. Enumerable.Range(1, 201)]));
        Assert.Empty(await client.GetRecipesAsync([]));
        Assert.Empty(handler.Requests);
    }
}
