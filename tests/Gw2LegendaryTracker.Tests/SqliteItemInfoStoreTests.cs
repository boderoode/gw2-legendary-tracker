using Gw2LegendaryTracker.Core.Items;
using Gw2LegendaryTracker.Infrastructure.Persistence;

namespace Gw2LegendaryTracker.Tests;

public class SqliteItemInfoStoreTests
{
    [Fact]
    public async Task Save_then_get_round_trips_including_missing_icon()
    {
        using var temp = new TempDatabase();
        await temp.Database.MigrateAsync();
        var store = new SqliteItemInfoStore(temp.Database);
        var items = new[] { new ItemInfo(1, "Mystic Coin", "https://icon/1.png", "Rare"), new ItemInfo(2, "No Icon", null, "Basic") };

        await store.SaveAsync(items);
        var result = await store.GetAsync([1, 2, 3]);

        Assert.Equal(2, result.Count);
        Assert.Equal(items[0], result[1]);
        Assert.Equal(items[1], result[2]);
    }

    [Fact]
    public async Task Save_updates_existing_items()
    {
        using var temp = new TempDatabase();
        await temp.Database.MigrateAsync();
        var store = new SqliteItemInfoStore(temp.Database);
        await store.SaveAsync([new ItemInfo(1, "Old", null, "Basic")]);

        await store.SaveAsync([new ItemInfo(1, "New", "https://icon", "Exotic")]);

        Assert.Equal(new ItemInfo(1, "New", "https://icon", "Exotic"), (await store.GetAsync([1]))[1]);
    }

    [Fact]
    public async Task Get_handles_more_ids_than_one_batch()
    {
        using var temp = new TempDatabase();
        await temp.Database.MigrateAsync();
        var store = new SqliteItemInfoStore(temp.Database);
        await store.SaveAsync([.. Enumerable.Range(1, 1200).Select(i => new ItemInfo(i, $"Item {i}", null, "Basic"))]);

        var result = await store.GetAsync([.. Enumerable.Range(1, 1200)]);

        Assert.Equal(1200, result.Count);
    }

    [Fact]
    public async Task Get_with_no_ids_returns_empty()
    {
        using var temp = new TempDatabase();
        await temp.Database.MigrateAsync();

        Assert.Empty(await new SqliteItemInfoStore(temp.Database).GetAsync([]));
    }
}
