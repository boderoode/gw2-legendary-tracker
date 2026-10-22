using Gw2LegendaryTracker.Core.Gw2;
using Gw2LegendaryTracker.Core.Inventory;

namespace Gw2LegendaryTracker.Tests;

public class InventoryModelTests
{
    private static readonly DateTimeOffset Captured = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    private static AccountSnapshot Snapshot(IReadOnlyList<InventoryEntry> items, IReadOnlyList<WalletEntry>? wallet = null) =>
        new("Test.1234", Captured, items, wallet ?? []);

    [Fact]
    public void Same_item_is_summed_over_stacks_and_sources()
    {
        var model = InventoryModel.From(Snapshot(
        [
            new InventoryEntry(10, 250, InventorySource.Materials),
            new InventoryEntry(10, 3, InventorySource.Bank),
            new InventoryEntry(10, 4, InventorySource.Bank),
            new InventoryEntry(10, 5, InventorySource.Character, "Hero One"),
            new InventoryEntry(10, 6, InventorySource.Character, "Hero Two"),
        ]));

        Assert.Equal(268, model.CountOf(10));
    }

    [Fact]
    public void Locations_are_grouped_per_source_and_character()
    {
        var model = InventoryModel.From(Snapshot(
        [
            new InventoryEntry(10, 3, InventorySource.Bank),
            new InventoryEntry(10, 4, InventorySource.Bank),
            new InventoryEntry(10, 5, InventorySource.Character, "Hero Two"),
            new InventoryEntry(10, 6, InventorySource.Character, "Hero One"),
            new InventoryEntry(10, 7, InventorySource.Character, "Hero One"),
            new InventoryEntry(10, 8, InventorySource.Materials),
        ]));

        var stock = model.Find(10);

        Assert.NotNull(stock);
        Assert.Equal(
            [
                new StockLocation(InventorySource.Bank, string.Empty, 7),
                new StockLocation(InventorySource.Materials, string.Empty, 8),
                new StockLocation(InventorySource.Character, "Hero One", 13),
                new StockLocation(InventorySource.Character, "Hero Two", 5),
            ],
            stock.Locations);
    }

    [Fact]
    public void Unknown_item_counts_as_zero_and_has_no_stock()
    {
        var model = InventoryModel.From(Snapshot([new InventoryEntry(10, 1, InventorySource.Bank)]));

        Assert.Equal(0, model.CountOf(999));
        Assert.Null(model.Find(999));
    }

    [Fact]
    public void Empty_stacks_are_ignored()
    {
        var model = InventoryModel.From(Snapshot([new InventoryEntry(10, 0, InventorySource.Bank), new InventoryEntry(11, -2, InventorySource.Bank)]));

        Assert.Empty(model.Items);
    }

    [Fact]
    public void Totals_do_not_overflow_int()
    {
        var model = InventoryModel.From(Snapshot(
        [
            new InventoryEntry(10, int.MaxValue, InventorySource.Bank),
            new InventoryEntry(10, int.MaxValue, InventorySource.Materials),
        ]));

        Assert.Equal(2L * int.MaxValue, model.CountOf(10));
    }

    [Fact]
    public void Items_are_ordered_by_id()
    {
        var model = InventoryModel.From(Snapshot(
        [
            new InventoryEntry(30, 1, InventorySource.Bank),
            new InventoryEntry(10, 1, InventorySource.Bank),
            new InventoryEntry(20, 1, InventorySource.Bank),
        ]));

        Assert.Equal([10, 20, 30], model.Items.Select(i => i.ItemId));
    }

    [Fact]
    public void Wallet_amounts_are_available_and_default_to_zero()
    {
        var model = InventoryModel.From(Snapshot([], [new WalletEntry(1, 123456), new WalletEntry(23, 40)]));

        Assert.Equal(123456, model.CurrencyAmount(1));
        Assert.Equal(40, model.CurrencyAmount(23));
        Assert.Equal(0, model.CurrencyAmount(99));
        Assert.Equal(Captured, model.CapturedUtc);
    }

    [Fact]
    public void Empty_model_has_nothing()
    {
        Assert.Empty(InventoryModel.Empty.Items);
        Assert.Equal(0, InventoryModel.Empty.CountOf(1));
        Assert.Null(InventoryModel.Empty.CapturedUtc);
    }

    [Fact]
    public void Provider_replaces_current_and_notifies()
    {
        var provider = new InventoryProvider();
        InventoryModel? announced = null;
        provider.Changed += (_, model) => announced = model;

        provider.Update(Snapshot([new InventoryEntry(10, 5, InventorySource.Bank)]));

        Assert.Equal(5, provider.Current.CountOf(10));
        Assert.Same(provider.Current, announced);
    }
}
