using Gw2LegendaryTracker.Core.Gw2;
using Gw2LegendaryTracker.Infrastructure.Persistence;

namespace Gw2LegendaryTracker.Tests;

public class SqliteSnapshotStoreTests
{
    [Fact]
    public async Task Load_returns_null_when_nothing_saved()
    {
        using var temp = new TempDatabase();
        await temp.Database.MigrateAsync();

        Assert.Null(await new SqliteSnapshotStore(temp.Database).LoadAsync());
    }

    [Fact]
    public async Task Save_then_load_round_trips()
    {
        using var temp = new TempDatabase();
        await temp.Database.MigrateAsync();
        var store = new SqliteSnapshotStore(temp.Database);
        var expected = TestData.Snapshot();

        await store.SaveAsync(expected);
        var actual = await store.LoadAsync();

        Assert.NotNull(actual);
        Assert.Equal(expected.AccountName, actual.AccountName);
        Assert.Equal(expected.CapturedUtc, actual.CapturedUtc);
        Assert.Equivalent(expected.Items, actual.Items);
        Assert.Equivalent(expected.Wallet, actual.Wallet);
    }

    [Fact]
    public async Task Save_replaces_the_previous_snapshot()
    {
        using var temp = new TempDatabase();
        await temp.Database.MigrateAsync();
        var store = new SqliteSnapshotStore(temp.Database);
        await store.SaveAsync(TestData.Snapshot());

        var second = new AccountSnapshot("Other.5678", DateTimeOffset.UnixEpoch, [new InventoryEntry(1, 1, InventorySource.Bank)], []);
        await store.SaveAsync(second);
        var actual = await store.LoadAsync();

        Assert.Equal("Other.5678", actual?.AccountName);
        Assert.Single(actual!.Items);
        Assert.Empty(actual.Wallet);
    }
}
