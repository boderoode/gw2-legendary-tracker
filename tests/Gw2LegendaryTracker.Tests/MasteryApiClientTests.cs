using System.Net;
using Gw2LegendaryTracker.Core.Gw2;
using Gw2LegendaryTracker.Core.Masteries;
using Gw2LegendaryTracker.Infrastructure.Gw2;

namespace Gw2LegendaryTracker.Tests;

public sealed class MasteryApiClientTests
{
    private static Gw2ApiClient CreateClient(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://api.guildwars2.com/") });

    [Fact]
    public async Task Masteries_are_public_and_parsed_with_their_levels()
    {
        var handler = new StubHandler(_ => StubHandler.Json("""
            [
              {"id":1,"name":"Exalted Lore","requirement":"x","order":1,"background":"bg","region":"Maguuma",
               "levels":[{"name":"Exalted Markings","description":"d","instruction":"i","icon":"ic","point_cost":1,"exp_cost":254000},
                         {"name":"Exalted Acceptance","point_cost":2,"exp_cost":508000}]},
              {"id":2,"name":"","region":"Maguuma","order":2,"levels":[{"name":"x","point_cost":1,"exp_cost":1}]},
              {"id":3,"name":"No levels","region":"Tyria","order":1,"levels":[]},
              {"id":4,"name":"No region","order":1,"levels":[{"name":"x","point_cost":1,"exp_cost":1}]},
              {"id":5,"name":"Bad cost","region":"Tyria","order":1,"levels":[{"name":"x","point_cost":-1,"exp_cost":1}]},
              null
            ]
            """));

        var masteries = await CreateClient(handler).GetMasteriesAsync();

        var request = Assert.Single(handler.Requests);
        Assert.Null(request.Headers.Authorization);
        Assert.Equal("/v2/masteries?ids=all&lang=en", request.RequestUri!.PathAndQuery);
        var only = Assert.Single(masteries);
        Assert.Equal(
            new MasteryDefinition(1, "Exalted Lore", "Maguuma", 1, [new("Exalted Markings", 1, 254_000), new("Exalted Acceptance", 2, 508_000)]),
            only);
    }

    [Fact]
    public async Task Account_masteries_use_the_key_in_the_header_only()
    {
        var handler = new StubHandler(_ => StubHandler.Json("""[{"id":1,"level":4},{"id":0,"level":1},{"id":7,"level":0}]"""));

        var masteries = await CreateClient(handler).GetAccountMasteriesAsync(TestData.Key);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("/v2/account/masteries", request.RequestUri!.PathAndQuery);
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal(TestData.Key, request.Headers.Authorization?.Parameter);
        Assert.DoesNotContain(TestData.Key, request.RequestUri.ToString(), StringComparison.Ordinal);
        Assert.Equal([new AccountMastery(1, 4), new AccountMastery(7, 0)], masteries);
    }

    [Fact]
    public async Task Mastery_points_are_parsed_per_region()
    {
        var handler = new StubHandler(_ => StubHandler.Json("""
            {"totals":[{"region":"Tyria","spent":49,"earned":50},{"region":"Jade","spent":10,"earned":12},{"spent":1,"earned":1}],
             "unlocked":[1,2,2,30]}
            """));

        var points = await CreateClient(handler).GetAccountMasteryPointsAsync(TestData.Key);

        Assert.Equal("/v2/account/mastery/points", Assert.Single(handler.Requests).RequestUri!.PathAndQuery);
        Assert.Equal([new RegionPoints("Tyria", 49, 50), new RegionPoints("Jade", 10, 12)], points.Totals);
        Assert.Equal([1, 2, 30], points.Unlocked);
    }

    [Fact]
    public async Task Achievements_are_asked_for_in_chunks_of_200()
    {
        var handler = new StubHandler(request =>
            request.RequestUri!.Query.Contains("ids=1,", StringComparison.Ordinal)
                ? StubHandler.Json("""[{"id":1,"current":3,"max":3,"done":true},{"id":2,"current":1,"max":5,"done":false}]""", HttpStatusCode.PartialContent)
                : StubHandler.Json("""{"text":"all ids provided are invalid"}""", HttpStatusCode.NotFound));

        var ids = Enumerable.Range(1, 250).ToList();
        var achievements = await CreateClient(handler).GetAccountAchievementsAsync(TestData.Key, ids);

        Assert.Equal(2, handler.Requests.Count);
        Assert.StartsWith("/v2/account/achievements?ids=1,2,", handler.Requests[0].RequestUri!.PathAndQuery, StringComparison.Ordinal);
        Assert.Equal("/v2/account/achievements?ids=" + string.Join(',', Enumerable.Range(201, 50)), handler.Requests[1].RequestUri!.PathAndQuery);
        Assert.All(handler.Requests, r => Assert.Equal(TestData.Key, r.Headers.Authorization?.Parameter));
        Assert.Equal([new AchievementProgress(1, true, 3, 3), new AchievementProgress(2, false, 1, 5)], achievements);
    }

    [Fact]
    public async Task Without_achievement_ids_nothing_is_asked()
    {
        var handler = new StubHandler(_ => StubHandler.Json("[]"));

        var achievements = await CreateClient(handler).GetAccountAchievementsAsync(TestData.Key, [0, -1]);

        Assert.Empty(achievements);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_missing_progression_scope_is_reported_as_missing_permission()
    {
        var handler = new StubHandler(_ => StubHandler.Json("""{"text":"requires scope progression"}""", HttpStatusCode.Forbidden));

        var error = await Assert.ThrowsAsync<Gw2ApiException>(() => CreateClient(handler).GetAccountMasteriesAsync(TestData.Key));

        Assert.Equal(Gw2ApiErrorKind.MissingPermissions, error.Kind);
    }
}
