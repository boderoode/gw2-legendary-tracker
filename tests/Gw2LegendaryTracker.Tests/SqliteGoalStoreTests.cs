using Gw2LegendaryTracker.Core.Goals;
using Gw2LegendaryTracker.Infrastructure.Persistence;

namespace Gw2LegendaryTracker.Tests;

public class SqliteGoalStoreTests
{
    [Fact]
    public async Task Goals_round_trip_with_order_note_and_ticks()
    {
        using var temp = new TempDatabase();
        await temp.Database.MigrateAsync();
        var store = new SqliteGoalStore(temp.Database);
        var goals = new[]
        {
            new Goal(30, 1, "second in line") { CompletedItemIds = new HashSet<int> { 5, 6 } },
            new Goal(10, 2),
            new Goal(20),
        };

        await store.SaveAsync(goals);
        var loaded = await store.LoadAsync();

        Assert.Equal([30, 10, 20], loaded.Select(g => g.ItemId));
        Assert.Equal(goals, loaded);
        Assert.Equal(2, loaded[1].Quantity);
    }

    [Fact]
    public async Task Saving_replaces_the_previous_list_including_ticks()
    {
        using var temp = new TempDatabase();
        await temp.Database.MigrateAsync();
        var store = new SqliteGoalStore(temp.Database);
        await store.SaveAsync([new Goal(1) { CompletedItemIds = new HashSet<int> { 5 } }, new Goal(2)]);

        await store.SaveAsync([new Goal(2)]);

        var loaded = Assert.Single(await store.LoadAsync());
        Assert.Equal(2, loaded.ItemId);
        Assert.Empty(loaded.CompletedItemIds);
    }

    [Fact]
    public async Task Empty_store_loads_an_empty_list()
    {
        using var temp = new TempDatabase();
        await temp.Database.MigrateAsync();

        Assert.Empty(await new SqliteGoalStore(temp.Database).LoadAsync());
    }

    [Fact]
    public async Task Goals_survive_reopening_the_database()
    {
        using var temp = new TempDatabase();
        await temp.Database.MigrateAsync();
        await new SqliteGoalStore(temp.Database).SaveAsync([new Goal(4, 1, "keep")]);

        await temp.Database.MigrateAsync();

        Assert.Equal("keep", Assert.Single(await new SqliteGoalStore(temp.Database).LoadAsync()).Note);
    }

    [Fact]
    public async Task Legendary_list_round_trips_is_replaced_and_remembers_when_it_was_fetched()
    {
        using var temp = new TempDatabase();
        await temp.Database.MigrateAsync();
        var store = new SqliteLegendaryStore(temp.Database);
        var first = new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero);
        var second = first.AddDays(1);
        Assert.Null(await store.GetFetchedUtcAsync());

        await store.SaveAsync([5, 3, 4, 3], first);
        Assert.Equal([3, 4, 5], await store.LoadAsync());
        Assert.Equal(first, await store.GetFetchedUtcAsync());

        await store.SaveAsync([9], second);
        Assert.Equal([9], await store.LoadAsync());
        Assert.Equal(second, await store.GetFetchedUtcAsync());
    }
}
