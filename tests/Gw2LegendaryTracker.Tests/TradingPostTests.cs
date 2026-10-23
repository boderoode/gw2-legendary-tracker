using System.Globalization;
using System.Net;
using Gw2LegendaryTracker.Core.Crafting;
using Gw2LegendaryTracker.Core.Gw2;
using Gw2LegendaryTracker.Core.Trading;
using Gw2LegendaryTracker.Infrastructure.Gw2;
using Gw2LegendaryTracker.Infrastructure.Persistence;
using static Gw2LegendaryTracker.Tests.CraftingTestHelpers;

namespace Gw2LegendaryTracker.Tests;

public class TradingPostPriceTests
{
    [Fact]
    public void Buying_now_costs_the_lowest_offer()
    {
        var price = new TradingPostPrice(1, BuyUnitPrice: 1421, BuyQuantity: 49_138, SellUnitPrice: 1494, SellQuantity: 217_675);

        Assert.True(price.IsPurchasable);
        Assert.Equal(1494, price.InstantBuyPrice);
    }

    [Theory]
    [InlineData(1421, 1494, 1422)]
    [InlineData(1493, 1494, 1494)]
    [InlineData(1494, 1494, 1494)]
    [InlineData(0, 1494, 1494)]
    public void A_buy_order_outbids_by_one_copper_but_never_costs_more_than_buying_now(long best, long offer, long expected)
    {
        Assert.Equal(expected, new TradingPostPrice(1, best, 10, offer, 10).BuyOrderPrice);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(5, 0)]
    public void Nothing_on_offer_means_it_cannot_be_bought(long quantity, long price)
    {
        var item = new TradingPostPrice(1, 50, 10, price, quantity);

        Assert.False(item.IsPurchasable);
        Assert.Equal(0, item.InstantBuyPrice);
        Assert.Equal(0, item.BuyOrderPrice);
    }
}

public class CoinFormatterTests
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    [Theory]
    [InlineData(0, "0c")]
    [InlineData(5, "5c")]
    [InlineData(100, "1s")]
    [InlineData(10_000, "1g")]
    [InlineData(10_001, "1g 1c")]
    [InlineData(1_158_896, "115g 88s 96c")]
    [InlineData(12_345_678, "1,234g 56s 78c")]
    [InlineData(-250, "-2s 50c")]
    public void Copper_is_shown_as_gold_silver_and_copper(long copper, string expected)
    {
        Assert.Equal(expected, CoinFormatter.Format(copper, Culture));
    }

    [Fact]
    public void The_extremes_do_not_throw()
    {
        Assert.NotEmpty(CoinFormatter.Format(long.MaxValue, Culture));
        Assert.StartsWith("-", CoinFormatter.Format(long.MinValue, Culture), StringComparison.Ordinal);
    }
}

public class PriceCatalogTests
{
    private readonly FakeApiClient _api = new();
    private readonly FakePriceStore _store = new();
    private readonly ManualTime _time = new();

    public PriceCatalogTests()
    {
        _api.Prices[1] = new TradingPostPrice(1, 90, 5, 100, 5);
        _api.Prices[2] = new TradingPostPrice(2, 190, 5, 200, 5);
    }

    private PriceCatalog CreateCatalog() => new(_api, _store, _time);

    [Fact]
    public async Task Missing_prices_are_fetched_and_stored_and_unlisted_items_are_remembered()
    {
        var book = await CreateCatalog().EnsureAsync([1, 2, 3]);

        Assert.Equal(100, book.Find(1)?.InstantBuyPrice);
        Assert.Null(book.Find(3));
        Assert.True(_store.Lookups.ContainsKey(3), "an item that is not on the Trading Post is remembered too");
        Assert.Null(_store.Lookups[3].Price);
        Assert.Null(book.Warning);
    }

    [Fact]
    public async Task Recent_prices_are_not_asked_for_again()
    {
        var catalog = CreateCatalog();
        await catalog.EnsureAsync([1, 2, 3]);
        _api.PriceRequests.Clear();
        _time.Advance(TimeSpan.FromMinutes(9));

        await catalog.EnsureAsync([1, 2, 3]);

        Assert.Empty(_api.PriceRequests);
    }

    [Fact]
    public async Task Prices_older_than_the_maximum_age_are_fetched_again_and_updated()
    {
        var catalog = CreateCatalog();
        await catalog.EnsureAsync([1]);
        _api.Prices[1] = new TradingPostPrice(1, 90, 5, 150, 5);
        _time.Advance(TimeSpan.FromMinutes(11));

        var book = await catalog.EnsureAsync([1]);

        Assert.Equal(150, book.Find(1)?.InstantBuyPrice);
        Assert.Equal(_time.GetUtcNow(), book.OldestFetchedUtc);
    }

    [Fact]
    public async Task Only_stale_or_unknown_items_are_requested()
    {
        var catalog = CreateCatalog();
        await catalog.EnsureAsync([1]);
        _api.PriceRequests.Clear();
        _time.Advance(TimeSpan.FromMinutes(5));

        await catalog.EnsureAsync([1, 2]);

        Assert.Equal([2], Assert.Single(_api.PriceRequests));
    }

    [Fact]
    public async Task Requests_are_split_into_chunks_of_200_and_duplicates_are_asked_once()
    {
        await CreateCatalog().EnsureAsync([.. Enumerable.Range(1, 450), 5, 5]);

        Assert.Equal([200, 200, 50], _api.PriceRequests.Select(r => r.Count));
    }

    [Fact]
    public async Task Offline_returns_the_last_known_prices_with_a_warning()
    {
        var catalog = CreateCatalog();
        await catalog.EnsureAsync([1]);
        var firstFetch = _time.GetUtcNow();
        _time.Advance(TimeSpan.FromHours(1));
        _api.PriceFailure = new Gw2ApiException(Gw2ApiErrorKind.Offline, "Geen verbinding.");

        var book = await catalog.EnsureAsync([1]);

        Assert.Equal(100, book.Find(1)?.InstantBuyPrice);
        Assert.Equal(firstFetch, book.OldestFetchedUtc);
        Assert.Equal("Geen verbinding.", book.Warning);
    }

    [Fact]
    public async Task Offline_with_nothing_stored_gives_an_empty_book_and_a_warning()
    {
        _api.PriceFailure = new Gw2ApiException(Gw2ApiErrorKind.Offline, "Geen verbinding.");

        var book = await CreateCatalog().EnsureAsync([1, 2]);

        Assert.Empty(book.Prices);
        Assert.Null(book.OldestFetchedUtc);
        Assert.NotNull(book.Warning);
    }

    [Fact]
    public async Task A_failed_request_is_not_remembered_as_not_listed()
    {
        _api.PriceFailure = new Gw2ApiException(Gw2ApiErrorKind.RateLimited, "Te veel verzoeken.");

        await CreateCatalog().EnsureAsync([1]);

        Assert.False(_store.Lookups.ContainsKey(1));
    }

    [Fact]
    public async Task No_items_means_no_request()
    {
        var book = await CreateCatalog().EnsureAsync([]);

        Assert.Same(PriceBook.Empty, book);
        Assert.Empty(_api.PriceRequests);
    }
}

public class CostAnalyzerTests
{
    private const int Goal = 1;
    private const int PartB = 2;
    private const int RawC = 3;
    private const int RawD = 4;
    private const int RawE = 5;

    private static TradingPostPrice Listed(int id, long instant) => new(id, instant - 1, 100, instant, 100);

    private static PriceBook Book(params TradingPostPrice[] prices) =>
        new(prices.ToDictionary(p => p.ItemId), DateTimeOffset.UnixEpoch, null);

    private static CraftPlan Plan(InventoryModelHolder inventory, params Recipe[] recipes) =>
        CraftingPlanner.Plan(new CraftGoal(Goal), inventory.Model, new RecipeBook(recipes));

    // Goal needs 2 B; each B is crafted from 3 C. Nothing owned: 6 C are missing.
    private static readonly Recipe[] TwoPartsFromThree = [Recipe(1, Goal, 1, (PartB, 2)), Recipe(2, PartB, 1, (RawC, 3))];

    [Fact]
    public void Shortages_are_priced_at_the_lowest_offer_and_ordered_by_cost()
    {
        var plan = Plan(Owns(), Recipe(1, Goal, 1, (RawC, 10), (RawD, 2)));
        // Best orders are 90 and 650, offers are 100 and 700: a buy order outbids by 1 copper (91 and 651).
        var prices = Book(new TradingPostPrice(RawC, 90, 100, 100, 100), new TradingPostPrice(RawD, 650, 100, 700, 100));

        var estimate = CostAnalyzer.Analyze(plan, prices, 0);

        // D costs 2 x 700 = 1400, C costs 10 x 100 = 1000: the dearest line comes first.
        Assert.Equal([RawD, RawC], estimate.BuyLines.Select(l => l.ItemId));
        Assert.Equal(new CostLine(RawC, 10, 100, 1000, 91, 910), estimate.BuyLines[1]);
        Assert.Equal(2400, estimate.TotalInstant);
        Assert.Equal(910 + (2 * 651), estimate.TotalBuyOrder);
    }

    [Fact]
    public void Buying_a_part_is_suggested_when_it_is_cheaper_than_making_it()
    {
        // Making 2 B costs 6 C = 600; buying 2 B costs 500.
        var plan = Plan(Owns(), TwoPartsFromThree);

        var estimate = CostAnalyzer.Analyze(plan, Book(Listed(RawC, 100), Listed(PartB, 250)), 0);

        var suggestion = Assert.Single(estimate.Suggestions);
        Assert.Equal(new BuyInsteadSuggestion(PartB, 2, 500, 600), suggestion);
        Assert.Equal(100, suggestion.Savings);
        Assert.Equal(600, estimate.TotalInstant);
        Assert.Equal(500, estimate.TotalAfterSuggestions);
    }

    [Fact]
    public void No_suggestion_when_making_it_is_cheaper_or_equal()
    {
        var plan = Plan(Owns(), TwoPartsFromThree);

        Assert.Empty(CostAnalyzer.Analyze(plan, Book(Listed(RawC, 100), Listed(PartB, 400)), 0).Suggestions);
        Assert.Empty(CostAnalyzer.Analyze(plan, Book(Listed(RawC, 100), Listed(PartB, 300)), 0).Suggestions);
    }

    [Fact]
    public void A_part_is_not_compared_when_something_below_it_cannot_be_bought()
    {
        // C has no listing, so the cost of making B is unknown and no honest comparison exists.
        var plan = Plan(Owns(), TwoPartsFromThree);

        var estimate = CostAnalyzer.Analyze(plan, Book(Listed(PartB, 1)), 0);

        Assert.Empty(estimate.Suggestions);
        Assert.Equal([RawC], estimate.Unbuyable.Select(u => u.ItemId));
    }

    [Fact]
    public void A_part_that_is_not_on_the_trading_post_is_never_suggested()
    {
        var plan = Plan(Owns(), TwoPartsFromThree);

        Assert.Empty(CostAnalyzer.Analyze(plan, Book(Listed(RawC, 100)), 0).Suggestions);
    }

    [Fact]
    public void Suggestions_do_not_overlap_the_top_one_wins()
    {
        // Goal <- B <- C <- E. Buying B is cheapest of all, so nothing below B is suggested as well.
        var recipes = new[] { Recipe(1, Goal, 1, (PartB, 1)), Recipe(2, PartB, 1, (RawC, 1)), Recipe(3, RawC, 1, (RawE, 5)) };
        var plan = Plan(Owns(), recipes);

        var estimate = CostAnalyzer.Analyze(plan, Book(Listed(PartB, 100), Listed(RawC, 300), Listed(RawE, 200)), 0);

        Assert.Equal([PartB], estimate.Suggestions.Select(s => s.ItemId));
    }

    [Fact]
    public void When_the_top_part_is_dearer_a_part_below_it_can_still_be_suggested()
    {
        var recipes = new[] { Recipe(1, Goal, 1, (PartB, 1)), Recipe(2, PartB, 1, (RawC, 1)), Recipe(3, RawC, 1, (RawE, 5)) };
        var plan = Plan(Owns(), recipes);

        // B (900) is dearer than C+... = 5 E (500). C (300) is cheaper than 5 E (500).
        var estimate = CostAnalyzer.Analyze(plan, Book(Listed(PartB, 900), Listed(RawC, 300), Listed(RawE, 100)), 0);

        Assert.Equal([RawC], estimate.Suggestions.Select(s => s.ItemId));
        Assert.Equal(200, estimate.Suggestions[0].Savings);
    }

    [Fact]
    public void Missing_items_without_a_listing_are_reported_separately_and_not_counted()
    {
        var plan = Plan(Owns(), Recipe(1, Goal, 1, (RawC, 4), (RawD, 1)));

        var estimate = CostAnalyzer.Analyze(plan, Book(Listed(RawC, 100)), 0);

        Assert.Equal(400, estimate.TotalInstant);
        Assert.Equal(new UnbuyableLine(RawD, 1), Assert.Single(estimate.Unbuyable));
    }

    [Fact]
    public void An_item_with_no_offers_counts_as_not_for_sale()
    {
        var plan = Plan(Owns(), Recipe(1, Goal, 1, (RawC, 4)));
        var soldOut = new TradingPostPrice(RawC, 50, 10, 100, 0);

        var estimate = CostAnalyzer.Analyze(plan, Book(soldOut), 0);

        Assert.Empty(estimate.BuyLines);
        Assert.Single(estimate.Unbuyable);
    }

    [Fact]
    public void Nothing_missing_means_nothing_to_buy()
    {
        var plan = Plan(Owns((RawC, 6)), TwoPartsFromThree);

        var estimate = CostAnalyzer.Analyze(plan, Book(Listed(RawC, 100), Listed(PartB, 1)), 0);

        Assert.True(estimate.NothingToBuy);
        Assert.Equal(0, estimate.TotalInstant);
        Assert.Empty(estimate.Suggestions);
    }

    [Fact]
    public void Owned_stock_is_not_bought_again()
    {
        var plan = Plan(Owns((RawC, 2)), Recipe(1, Goal, 1, (RawC, 10)));

        var estimate = CostAnalyzer.Analyze(plan, Book(Listed(RawC, 100)), 0);

        Assert.Equal(8, Assert.Single(estimate.BuyLines).Quantity);
        Assert.Equal(800, estimate.TotalInstant);
    }

    [Fact]
    public void Parts_ticked_off_by_hand_cost_nothing_and_are_not_suggested()
    {
        var plan = CraftingPlanner.Plan(
            new CraftGoal(Goal) { Completed = new HashSet<int> { PartB } },
            Owns().Model,
            new RecipeBook(TwoPartsFromThree));

        var estimate = CostAnalyzer.Analyze(plan, Book(Listed(RawC, 100), Listed(PartB, 1)), 0);

        Assert.True(estimate.NothingToBuy);
        Assert.Empty(estimate.Suggestions);
    }

    [Theory]
    [InlineData(1000, 0)]
    [InlineData(600, 0)]
    [InlineData(200, 400)]
    [InlineData(0, 600)]
    public void The_shortfall_is_what_the_wallet_lacks_for_buying_everything_now(long coin, long shortfall)
    {
        var plan = Plan(Owns(), Recipe(1, Goal, 1, (RawC, 6)));

        var estimate = CostAnalyzer.Analyze(plan, Book(Listed(RawC, 100)), coin);

        Assert.Equal(shortfall, estimate.Shortfall);
        Assert.Equal(coin, estimate.CoinOwned);
    }

    [Fact]
    public void The_items_that_need_a_price_are_the_shortages_and_the_crafted_parts()
    {
        var plan = Plan(Owns(), TwoPartsFromThree);

        var ids = CostAnalyzer.RelevantItemIds(plan);

        Assert.Equal([PartB, RawC], ids.Order());
    }

    [Fact]
    public void A_goal_with_no_recipe_prices_the_goal_itself()
    {
        var plan = Plan(Owns());

        var estimate = CostAnalyzer.Analyze(plan, Book(Listed(Goal, 5_000_000)), 0);

        Assert.Equal(5_000_000, estimate.TotalInstant);
    }

    private static InventoryModelHolder Owns(params (int Item, int Count)[] items) => new(Inventory(items));

    // Small wrapper so the helper signatures above stay readable.
    private sealed record InventoryModelHolder(Core.Inventory.InventoryModel Model);
}

public class TradingPostInfrastructureTests
{
    private static ForgeApiClientTestsHelper Client(HttpMessageHandler handler) => new(handler);

    private sealed class ForgeApiClientTestsHelper
    {
        public ForgeApiClientTestsHelper(HttpMessageHandler handler) =>
            Api = new Gw2ApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.guildwars2.com/") });

        public Gw2ApiClient Api { get; }
    }

    [Fact]
    public async Task Prices_are_read_from_the_public_endpoint_without_credentials_and_mapped()
    {
        var handler = new StubHandler(_ => StubHandler.Json("""
            [{"id":19721,"whitelisted":true,"buys":{"quantity":1024435,"unit_price":1602},"sells":{"quantity":2294899,"unit_price":1723}},
             {"id":29185,"whitelisted":false,"buys":{"quantity":4855,"unit_price":1009100},"sells":{"quantity":178,"unit_price":1158896}}]
            """, HttpStatusCode.PartialContent));

        var prices = await Client(handler).Api.GetTradingPostPricesAsync([19721, 29185, 19672]);

        var request = Assert.Single(handler.Requests);
        Assert.Null(request.Headers.Authorization);
        Assert.Equal("/v2/commerce/prices?ids=19721,29185,19672", request.RequestUri!.PathAndQuery);
        Assert.Equal(new TradingPostPrice(19721, 1602, 1_024_435, 1723, 2_294_899), prices[0]);
        Assert.Equal(1_158_896, prices[1].InstantBuyPrice);
    }

    [Fact]
    public async Task Missing_sides_become_zero_so_an_empty_market_is_not_purchasable()
    {
        var handler = new StubHandler(_ => StubHandler.Json("""[{"id":7,"buys":{"quantity":0,"unit_price":0}}]"""));

        var price = Assert.Single(await Client(handler).Api.GetTradingPostPricesAsync([7]));

        Assert.False(price.IsPurchasable);
        Assert.Equal(0, price.SellUnitPrice);
    }

    [Fact]
    public async Task Only_unknown_items_gives_an_empty_result_and_no_ids_means_no_request()
    {
        var handler = new StubHandler(_ => StubHandler.Json("""{"text":"all ids provided are invalid"}""", HttpStatusCode.NotFound));
        var api = Client(handler).Api;

        Assert.Empty(await api.GetTradingPostPricesAsync([19672]));
        Assert.Empty(await api.GetTradingPostPricesAsync([]));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task More_than_200_ids_are_rejected()
    {
        var api = Client(new StubHandler(_ => StubHandler.Json("[]"))).Api;

        await Assert.ThrowsAsync<ArgumentException>(() => api.GetTradingPostPricesAsync([.. Enumerable.Range(1, 201)]));
    }

    [Fact]
    public async Task The_store_keeps_prices_and_not_listed_items_and_updates_them()
    {
        using var temp = new TempDatabase();
        await temp.Database.MigrateAsync();
        var store = new SqlitePriceStore(temp.Database);
        var at = new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

        await store.SaveAsync([new PriceLookup(1, new TradingPostPrice(1, 90, 5, 100, 7), at), new PriceLookup(2, null, at)]);
        await store.SaveAsync([new PriceLookup(1, new TradingPostPrice(1, 95, 6, 120, 8), at.AddMinutes(10))]);
        var loaded = await store.GetAsync([1, 2, 3]);

        Assert.Equal(2, loaded.Count);
        Assert.Equal(new TradingPostPrice(1, 95, 6, 120, 8), loaded[1].Price);
        Assert.Equal(at.AddMinutes(10), loaded[1].FetchedUtc);
        Assert.Null(loaded[2].Price);
        Assert.False(loaded.ContainsKey(3));
    }

    [Fact]
    public async Task The_store_handles_more_ids_than_one_batch_and_none_at_all()
    {
        using var temp = new TempDatabase();
        await temp.Database.MigrateAsync();
        var store = new SqlitePriceStore(temp.Database);
        var at = DateTimeOffset.UnixEpoch;
        await store.SaveAsync([.. Enumerable.Range(1, 1200).Select(i => new PriceLookup(i, new TradingPostPrice(i, 1, 1, 2, 1), at))]);

        Assert.Equal(1200, (await store.GetAsync([.. Enumerable.Range(1, 1200)])).Count);
        Assert.Empty(await store.GetAsync([]));
        await store.SaveAsync([]);
    }
}

public class CostPresenterTests
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    private static string Name(int id) => $"Item{id}";

    private static CostEstimate Estimate(
        CostLine[]? lines = null,
        UnbuyableLine[]? unbuyable = null,
        BuyInsteadSuggestion[]? suggestions = null,
        long coin = 0)
    {
        lines ??= [];
        var total = lines.Sum(l => l.InstantTotal);
        var savings = (suggestions ?? []).Sum(s => s.Savings);
        return new CostEstimate(lines, unbuyable ?? [], total, lines.Sum(l => l.BuyOrderTotal), coin, Math.Max(0, total - coin), suggestions ?? [], total - savings);
    }

    [Fact]
    public void Nothing_to_buy_says_so_without_numbers()
    {
        var summary = CostPresenter.Summarize(Estimate(), Name, Culture);

        Assert.Equal("Je hoeft niets meer te kopen.", summary.Headline);
        Assert.Empty(summary.CoinText);
        Assert.Empty(summary.Lines);
        Assert.Null(summary.UnbuyableText);
    }

    [Fact]
    public void The_headline_gives_both_prices_and_lines_show_unit_and_total()
    {
        var estimate = Estimate([new CostLine(5, 250, 1723, 430_750, 1603, 400_750)], coin: 1_000_000);

        var summary = CostPresenter.Summarize(estimate, Name, Culture);

        Assert.Equal("Alles kopen wat je mist: 43g 7s 50c direct, of 40g 7s 50c met kooporders.", summary.Headline);
        var line = Assert.Single(summary.Lines);
        Assert.Equal(("17s 23c per stuk", "43g 7s 50c"), (line.UnitText, line.TotalText));
        Assert.Equal(250, line.Quantity);
    }

    [Theory]
    [InlineData(1_000_000, "Je hebt genoeg goud (100g).")]
    [InlineData(430_750, "Je hebt genoeg goud (43g 7s 50c).")]
    [InlineData(400_000, "Je hebt 40g en komt 3g 7s 50c tekort.")]
    [InlineData(0, "Je hebt 0c en komt 43g 7s 50c tekort.")]
    public void The_wallet_is_compared_with_the_cost(long coin, string expected)
    {
        var estimate = Estimate([new CostLine(5, 250, 1723, 430_750, 1603, 400_750)], coin: coin);

        Assert.Equal(expected, CostPresenter.Summarize(estimate, Name, Culture).CoinText);
    }

    [Fact]
    public void Only_unbuyable_items_say_that_nothing_can_be_bought()
    {
        var summary = CostPresenter.Summarize(Estimate(unbuyable: [new UnbuyableLine(9, 1)]), Name, Culture);

        Assert.Equal("Wat je mist is niet op de Trading Post te koop.", summary.Headline);
        Assert.Empty(summary.CoinText);
        Assert.Contains("1× Item9", summary.UnbuyableText, StringComparison.Ordinal);
    }

    [Fact]
    public void A_long_list_of_unbuyable_items_is_shortened()
    {
        var unbuyable = Enumerable.Range(1, 8).Select(i => new UnbuyableLine(i, 10 - i)).ToArray();

        var text = CostPresenter.Summarize(Estimate(unbuyable: unbuyable), Name, Culture).UnbuyableText;

        Assert.Contains("en nog 3 andere", text, StringComparison.Ordinal);
        Assert.Contains("Item1", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Item8", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_suggestion_names_what_to_buy_what_it_costs_and_what_it_saves()
    {
        var estimate = Estimate(
            [new CostLine(3, 6, 100, 600, 99, 594)],
            suggestions: [new BuyInsteadSuggestion(2, 2, 500, 600)]);

        var summary = CostPresenter.Summarize(estimate, Name, Culture);

        Assert.Equal("Koop 2× Item2 voor 5s in plaats van te maken voor 6s: dat bespaart 1s.", Assert.Single(summary.Suggestions));
    }

    [Fact]
    public void Several_suggestions_get_a_total_after_following_them_all()
    {
        var estimate = Estimate(
            [new CostLine(3, 10, 1000, 10_000, 900, 9_000)],
            suggestions: [new BuyInsteadSuggestion(2, 1, 400, 900), new BuyInsteadSuggestion(4, 1, 100, 300)]);

        var suggestions = CostPresenter.Summarize(estimate, Name, Culture).Suggestions;

        Assert.Equal(3, suggestions.Count);
        // Savings are 500 + 200 = 700 copper: 10,000 becomes 9,300 copper.
        Assert.Equal("Volg je alle tips, dan kost alles samen 93s in plaats van 1g.", suggestions[2]);
    }

    [Fact]
    public void The_disclaimer_is_honest_about_what_the_estimate_leaves_out()
    {
        Assert.Contains("laagste verkoopprijs", CostPresenter.Disclaimer, StringComparison.Ordinal);
        Assert.Contains("stijgt", CostPresenter.Disclaimer, StringComparison.Ordinal);
        Assert.Contains("niet meegerekend", CostPresenter.Disclaimer, StringComparison.Ordinal);
    }

    [Fact]
    public void The_price_status_tells_how_current_the_prices_are()
    {
        var fresh = new PriceBook(new Dictionary<int, TradingPostPrice>(), DateTimeOffset.UtcNow, null);
        var offline = new PriceBook(new Dictionary<int, TradingPostPrice>(), DateTimeOffset.UtcNow, "Geen verbinding.");
        var nothing = new PriceBook(new Dictionary<int, TradingPostPrice>(), null, "Geen verbinding.");

        Assert.StartsWith("Prijzen van ", CostPresenter.PriceStatus(fresh, Culture), StringComparison.Ordinal);
        Assert.DoesNotContain("mislukt", CostPresenter.PriceStatus(fresh, Culture), StringComparison.Ordinal);
        Assert.Contains("laatst bekende prijzen", CostPresenter.PriceStatus(offline, Culture), StringComparison.Ordinal);
        Assert.Contains("Geen verbinding.", CostPresenter.PriceStatus(nothing, Culture), StringComparison.Ordinal);
        Assert.Contains("geen prijzen", CostPresenter.PriceStatus(new PriceBook(new Dictionary<int, TradingPostPrice>(), null, null), Culture), StringComparison.Ordinal);
    }
}
