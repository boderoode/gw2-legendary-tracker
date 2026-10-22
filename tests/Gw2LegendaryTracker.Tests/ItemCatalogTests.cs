using Gw2LegendaryTracker.Core.Gw2;
using Gw2LegendaryTracker.Core.Items;

namespace Gw2LegendaryTracker.Tests;

public class ItemCatalogTests
{
    private readonly FakeApiClient _api = new();
    private readonly FakeItemInfoStore _store = new();

    private ItemCatalog CreateCatalog() => new(_api, _store);

    [Fact]
    public async Task Fetches_unknown_items_and_caches_them()
    {
        var result = await CreateCatalog().ResolveAsync([1, 2]);

        Assert.Equal("Item 1", result[1].Name);
        Assert.Equal(2, _store.Items.Count);
    }

    [Fact]
    public async Task Cached_items_are_not_requested_again()
    {
        _store.Items[1] = new ItemInfo(1, "Cached", null, "Basic");

        var result = await CreateCatalog().ResolveAsync([1, 2]);

        Assert.Equal("Cached", result[1].Name);
        Assert.Equal([2], Assert.Single(_api.ItemRequests));
    }

    [Fact]
    public async Task Nothing_is_requested_when_everything_is_cached()
    {
        _store.Items[1] = new ItemInfo(1, "Cached", null, "Basic");

        await CreateCatalog().ResolveAsync([1, 1]);

        Assert.Empty(_api.ItemRequests);
    }

    [Fact]
    public async Task Requests_are_split_into_chunks_of_200()
    {
        await CreateCatalog().ResolveAsync(Enumerable.Range(1, 450));

        Assert.Equal([200, 200, 50], _api.ItemRequests.Select(r => r.Count));
    }

    [Fact]
    public async Task Duplicate_ids_are_requested_once()
    {
        await CreateCatalog().ResolveAsync([5, 5, 5]);

        Assert.Equal([5], Assert.Single(_api.ItemRequests));
    }

    [Fact]
    public async Task Offline_returns_the_cached_part_without_throwing()
    {
        _store.Items[1] = new ItemInfo(1, "Cached", null, "Basic");
        _api.ItemFailure = new Gw2ApiException(Gw2ApiErrorKind.Offline, "Geen verbinding.");

        var result = await CreateCatalog().ResolveAsync([1, 2]);

        Assert.Equal(["Cached"], result.Values.Select(i => i.Name));
        Assert.Equal(0, _store.SaveCalls);
    }
}
