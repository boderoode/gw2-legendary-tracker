using System.Globalization;
using Gw2LegendaryTracker.Core.Acquisition;
using Gw2LegendaryTracker.Core.Crafting;
using Gw2LegendaryTracker.Core.Goals;
using Gw2LegendaryTracker.Core.Links;
using Gw2LegendaryTracker.Infrastructure.Persistence;
using static Gw2LegendaryTracker.Tests.CraftingTestHelpers;

namespace Gw2LegendaryTracker.Tests;

internal static class Offers
{
    public const int Karma = 2;

    public static NewOffer Karmic(int itemId = 19925, long price = 25, string vendor = "Verkoper", string location = "Plaats", int count = 1) =>
        new(itemId, count, [new Ingredient(MaterialKey.Currency(Karma), price)], vendor, location);
}

public sealed class AcquisitionServiceTests : IDisposable
{
    private readonly FakeAcquisitionStore _store = new();
    private readonly AcquisitionService _service;

    public AcquisitionServiceTests() => _service = new AcquisitionService(_store);

    public void Dispose() => _service.Dispose();

    [Fact]
    public async Task A_valid_offer_is_stored_before_anyone_is_told_and_texts_are_trimmed()
    {
        IReadOnlyList<AcquisitionOffer>? storedWhenNotified = null;
        _service.Changed += (_, _) => storedWhenNotified = _store.Saved;

        var error = await _service.AddAsync(Offers.Karmic(vendor: "  Miyani ", location: " Timberline Falls  "));

        Assert.Null(error);
        var offer = Assert.Single(_service.Offers);
        Assert.Equal((1, "Miyani", "Timberline Falls"), (offer.Id, offer.VendorName, offer.Location));
        Assert.Same(_service.Offers, storedWhenNotified);
    }

    [Fact]
    public async Task Ids_grow_and_are_not_taken_over_from_removed_lower_ones()
    {
        await _service.AddAsync(Offers.Karmic(1));
        await _service.AddAsync(Offers.Karmic(2));
        await _service.RemoveAsync(1);

        await _service.AddAsync(Offers.Karmic(3));

        Assert.Equal([2, 3], _service.Offers.Select(o => o.Id));
    }

    public static TheoryData<NewOffer> InvalidOffers() => new()
    {
        Offers.Karmic(itemId: 0),
        Offers.Karmic(count: 0),
        Offers.Karmic(count: AcquisitionService.MaxOutputCount + 1),
        Offers.Karmic(price: 0),
        Offers.Karmic(price: -5),
        Offers.Karmic(price: AcquisitionService.MaxCostAmount + 1),
        Offers.Karmic(vendor: new string('x', AcquisitionService.MaxVendorNameLength + 1)),
        Offers.Karmic(location: new string('x', AcquisitionService.MaxLocationLength + 1)),
        new NewOffer(1, 1, [], "v", "l"),
        new NewOffer(1, 1, [.. Enumerable.Range(1, AcquisitionService.MaxCosts + 1).Select(i => new Ingredient(MaterialKey.Currency(i), 1))], "v", "l"),
        new NewOffer(1, 1, [new Ingredient(MaterialKey.Currency(2), 1), new Ingredient(MaterialKey.Currency(2), 5)], "v", "l"),
        new NewOffer(1, 1, [new Ingredient(MaterialKey.Currency(0), 1)], "v", "l"),
    };

    [Theory]
    [MemberData(nameof(InvalidOffers))]
    public async Task An_invalid_offer_is_refused_with_a_reason_and_nothing_changes(NewOffer offer)
    {
        var notified = false;
        _service.Changed += (_, _) => notified = true;

        var error = await _service.AddAsync(offer);

        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Empty(_service.Offers);
        Assert.Equal(0, _store.SaveCount);
        Assert.False(notified);
    }

    [Fact]
    public async Task Costs_can_mix_currencies_and_items()
    {
        var error = await _service.AddAsync(new NewOffer(5, 1, [new Ingredient(MaterialKey.Currency(2), 100), new Ingredient(MaterialKey.Item(77), 3)], "v", "l"));

        Assert.Null(error);
        Assert.Equal(2, _service.Offers[0].Costs.Count);
    }

    [Fact]
    public async Task A_failed_save_keeps_the_list_and_does_not_notify()
    {
        await _service.AddAsync(Offers.Karmic(1));
        var notified = false;
        _service.Changed += (_, _) => notified = true;
        _store.Failure = new IOException("disk full");

        await Assert.ThrowsAsync<IOException>(() => _service.AddAsync(Offers.Karmic(2)));

        Assert.False(notified);
        Assert.Single(_service.Offers);
    }

    [Fact]
    public async Task The_number_of_offers_is_capped()
    {
        await _service.ReplaceAsync(Enumerable.Range(1, AcquisitionService.MaxOffers).Select(i => Offers.Karmic(i)));

        var error = await _service.AddAsync(Offers.Karmic(999_999));

        Assert.Contains("hooguit", error, StringComparison.Ordinal);
        Assert.Equal(AcquisitionService.MaxOffers, _service.Offers.Count);
    }

    [Fact]
    public async Task Removing_an_unknown_offer_changes_nothing()
    {
        await _service.AddAsync(Offers.Karmic());
        var saves = _store.SaveCount;

        await _service.RemoveAsync(999);

        Assert.Equal(saves, _store.SaveCount);
    }

    [Fact]
    public async Task Replace_renumbers_from_one_and_stores_before_notifying()
    {
        await _service.AddAsync(Offers.Karmic(1));
        IReadOnlyList<AcquisitionOffer>? storedWhenNotified = null;
        _service.Changed += (_, _) => storedWhenNotified = _store.Saved;

        await _service.ReplaceAsync([Offers.Karmic(10), Offers.Karmic(20)]);

        Assert.Equal([1, 2], _service.Offers.Select(o => o.Id));
        Assert.Equal([10, 20], storedWhenNotified?.Select(o => o.ItemId));
    }

    [Fact]
    public async Task Concurrent_additions_get_unique_ids()
    {
        await Task.WhenAll(Enumerable.Range(1, 30).Select(i => _service.AddAsync(Offers.Karmic(i))));

        Assert.Equal(30, _service.Offers.Select(o => o.Id).Distinct().Count());
    }

    [Fact]
    public async Task Load_restores_the_stored_offers_and_notifies()
    {
        _store.Saved = [new AcquisitionOffer(4, 9, 1, [new Ingredient(MaterialKey.Currency(2), 1)], "v", "l")];
        var notified = false;
        _service.Changed += (_, _) => notified = true;

        await _service.LoadAsync();

        Assert.Equal(4, Assert.Single(_service.Offers).Id);
        Assert.True(notified);
    }

    [Theory]
    [InlineData("Miyani", "Timberline Falls", "Miyani, Timberline Falls")]
    [InlineData("Miyani", "", "Miyani")]
    [InlineData("", "Timberline Falls", "Timberline Falls")]
    [InlineData("", "", "")]
    public void An_offer_becomes_a_vendor_recipe_with_a_readable_note(string vendor, string location, string note)
    {
        var offer = new AcquisitionOffer(7, 19925, 5, [new Ingredient(MaterialKey.Currency(2), 125)], vendor, location);

        var recipe = offer.ToRecipe();

        Assert.Equal(-(AcquisitionOffer.RecipeIdBase + 7), recipe.Id);
        Assert.Equal((19925, 5), (recipe.OutputItemId, recipe.OutputCount));
        Assert.Equal(RecipeKind.Vendor, recipe.Kind);
        Assert.Equal(RecipeSource.Local, recipe.Source);
        Assert.Equal(note, recipe.Note);
        Assert.Equal([new Ingredient(MaterialKey.Currency(2), 125)], recipe.Ingredients);
    }
}

public class VendorPlanningTests
{
    private const int Goal = 1;
    private const int Shard = 19925;
    private const int Karma = Offers.Karma;

    private static Recipe VendorRecipe(int pieces = 1, long karma = 25) =>
        new AcquisitionOffer(1, Shard, pieces, [new Ingredient(MaterialKey.Currency(Karma), karma)], "Miyani", "Timberline Falls").ToRecipe();

    private static CraftPlan Plan(Gw2LegendaryTracker.Core.Inventory.InventoryModel inventory) =>
        CraftingPlanner.Plan(new CraftGoal(Goal), inventory, new RecipeBook([Recipe(10, Goal, 1, (Shard, 10)), VendorRecipe()]));

    [Fact]
    public void A_vendor_offer_turns_a_shortage_of_an_item_into_a_currency_cost()
    {
        var plan = Plan(Inventory(null, [(Karma, 300)]));

        Assert.Empty(plan.Shortages);
        Assert.Equal(250, Used(plan, MaterialKey.Currency(Karma)));
        Assert.Equal(NextStepKind.Craft, plan.Next.Kind);
        Assert.Equal(RecipeKind.Vendor, plan.Next.Craft?.Recipe.Kind);
        Assert.Equal(10, plan.Next.Craft?.Times);
    }

    [Fact]
    public void Too_little_karma_is_a_currency_shortage_and_the_next_step_is_to_gather_it()
    {
        var plan = Plan(Inventory(null, [(Karma, 100)]));

        Assert.Equal(150, Missing(plan, MaterialKey.Currency(Karma)));
        Assert.Equal(NextStepKind.Gather, plan.Next.Kind);
        Assert.Equal(MaterialKey.Currency(Karma), plan.Next.Material);
    }

    [Fact]
    public void The_next_step_says_to_buy_from_a_vendor_and_names_the_vendor()
    {
        var plan = Plan(Inventory(null, [(Karma, 300)]));
        var goalPlan = new GoalPlan(new Goal(Goal), plan, true, null);

        var summary = GoalPresenter.Summarize(
            goalPlan,
            id => id == Shard ? "Obsidian Shard" : $"#{id}",
            CultureInfo.InvariantCulture,
            currencyName: id => id == Karma ? "Karma" : $"#{id}");

        Assert.Equal("Koop nu: 10× Obsidian Shard (bij een verkoper: Miyani, Timberline Falls)", summary.NextStepText);
    }

    [Fact]
    public void Gathering_a_currency_uses_its_name()
    {
        var plan = Plan(Inventory(null, [(Karma, 100)]));
        var goalPlan = new GoalPlan(new Goal(Goal), plan, true, null);

        var summary = GoalPresenter.Summarize(goalPlan, id => $"#{id}", CultureInfo.InvariantCulture, currencyName: _ => "Karma");

        Assert.Equal("Verzamel: 150× Karma", summary.NextStepText);
    }

    [Fact]
    public void A_vendor_that_sells_stacks_buys_enough_stacks()
    {
        var book = new RecipeBook([Recipe(10, Goal, 1, (Shard, 10)), VendorRecipe(pieces: 4, karma: 100)]);

        var plan = CraftingPlanner.Plan(new CraftGoal(Goal), Inventory(null, [(Karma, 1000)]), book);

        // 10 pieces from stacks of 4 means 3 purchases (12 pieces), 300 karma, and 2 pieces left over.
        Assert.Equal(300, Used(plan, MaterialKey.Currency(Karma)));
        Assert.Equal(3, plan.Root.Children[0].Crafts);
    }

    [Fact]
    public void An_item_owned_is_not_bought_again()
    {
        var plan = Plan(Inventory([(Shard, 10)], [(Karma, 0)]));

        Assert.Equal(0, Used(plan, MaterialKey.Currency(Karma)));
        Assert.Equal(NextStepKind.Craft, plan.Next.Kind);
        Assert.Equal(Goal, plan.Next.Craft?.Recipe.OutputItemId);
    }
}
