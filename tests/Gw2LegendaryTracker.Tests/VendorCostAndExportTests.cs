using System.Globalization;
using Gw2LegendaryTracker.Core.Acquisition;
using Gw2LegendaryTracker.Core.Crafting;
using Gw2LegendaryTracker.Core.Export;
using Gw2LegendaryTracker.Core.Goals;
using Gw2LegendaryTracker.Core.Links;
using Gw2LegendaryTracker.Core.Settings;
using Gw2LegendaryTracker.Core.State;
using Gw2LegendaryTracker.Core.Trading;
using Gw2LegendaryTracker.Infrastructure.Gw2;
using Gw2LegendaryTracker.Infrastructure.Persistence;
using static Gw2LegendaryTracker.Tests.CraftingTestHelpers;

namespace Gw2LegendaryTracker.Tests;

public class VendorCostAnalysisTests
{
    private const int Goal = 1;
    private const int Shard = 19925;
    private const int Karma = Offers.Karma;

    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    private static Recipe VendorRecipe(string vendor = "Miyani", string location = "Timberline Falls") =>
        new AcquisitionOffer(1, Shard, 1, [new Ingredient(MaterialKey.Currency(Karma), 25)], vendor, location).ToRecipe();

    private static CraftPlan Plan(long karma, int needed = 10) =>
        CraftingPlanner.Plan(
            new CraftGoal(Goal),
            Inventory(null, [(Karma, karma)]),
            new RecipeBook([Recipe(10, Goal, 1, (Shard, needed)), VendorRecipe()]));

    private static PriceBook Book(params TradingPostPrice[] prices) => new(prices.ToDictionary(p => p.ItemId), DateTimeOffset.UnixEpoch, null);

    private static string Name(int id) => id == Shard ? "Obsidian Shard" : $"Item{id}";

    private static string CurrencyName(int id) => id == Karma ? "Karma" : $"Currency{id}";

    [Fact]
    public void Items_bought_from_a_vendor_are_listed_with_scaled_costs_and_the_vendor()
    {
        var estimate = CostAnalyzer.Analyze(Plan(karma: 1000), PriceBook.Empty, 0, id => id == Karma ? 1000 : 0);

        var line = Assert.Single(estimate.VendorLines);
        Assert.Equal((Shard, 10L, "Miyani, Timberline Falls"), (line.ItemId, line.Units, line.Note));
        Assert.Equal([new Ingredient(MaterialKey.Currency(Karma), 250)], line.Costs);
        Assert.Null(line.TradingPostTotal);
        Assert.False(estimate.NothingToBuy);
    }

    [Fact]
    public void The_trading_post_price_of_the_same_item_is_shown_as_an_alternative_when_it_is_for_sale()
    {
        var prices = Book(new TradingPostPrice(Shard, 90, 10, 100, 10));

        var line = Assert.Single(CostAnalyzer.Analyze(Plan(1000), prices, 0).VendorLines);

        Assert.Equal(1000, line.TradingPostTotal);
    }

    [Fact]
    public void Currency_needs_are_set_against_the_wallet()
    {
        var enough = CostAnalyzer.Analyze(Plan(karma: 300), PriceBook.Empty, 0, _ => 300);
        var short_ = CostAnalyzer.Analyze(Plan(karma: 100), PriceBook.Empty, 0, _ => 100);

        Assert.Equal(new CurrencyNeed(Karma, 250, 300, 0), Assert.Single(enough.CurrencyNeeds));
        Assert.Equal(new CurrencyNeed(Karma, 250, 100, 150), Assert.Single(short_.CurrencyNeeds));
    }

    [Fact]
    public void A_vendor_item_that_is_already_owned_costs_nothing()
    {
        var plan = CraftingPlanner.Plan(
            new CraftGoal(Goal),
            Inventory([(Shard, 10)], [(Karma, 0)]),
            new RecipeBook([Recipe(10, Goal, 1, (Shard, 10)), VendorRecipe()]));

        var estimate = CostAnalyzer.Analyze(plan, PriceBook.Empty, 0);

        Assert.True(estimate.NothingToBuy);
        Assert.Empty(estimate.VendorLines);
    }

    [Fact]
    public void The_headline_says_the_trading_post_is_not_needed_when_everything_comes_from_vendors()
    {
        var estimate = CostAnalyzer.Analyze(Plan(1000), PriceBook.Empty, 0, _ => 1000);

        var summary = CostPresenter.Summarize(estimate, Name, Culture, CurrencyName);

        Assert.Equal("Op de Trading Post hoef je niets te kopen.", summary.Headline);
        Assert.Empty(summary.CoinText);
    }

    [Fact]
    public void A_vendor_line_names_the_item_the_vendor_the_price_and_the_trading_post_alternative()
    {
        var prices = Book(new TradingPostPrice(Shard, 90, 10, 100, 10));
        var estimate = CostAnalyzer.Analyze(Plan(1000), prices, 0, _ => 1000);

        var text = Assert.Single(CostPresenter.Summarize(estimate, Name, Culture, CurrencyName).VendorLines);

        // 10 pieces at 100 copper on the Trading Post is 10 silver.
        Assert.Equal("10× Obsidian Shard: koop bij een verkoper (Miyani, Timberline Falls) voor 250 Karma. Op de Trading Post kost hetzelfde 10s.", text);
    }

    [Fact]
    public void A_vendor_line_without_a_known_vendor_or_alternative_stays_short()
    {
        var offer = new AcquisitionOffer(1, Shard, 1, [new Ingredient(MaterialKey.Currency(Karma), 25)], string.Empty, string.Empty).ToRecipe();
        var plan = CraftingPlanner.Plan(new CraftGoal(Goal), Inventory(null, [(Karma, 1000)]), new RecipeBook([Recipe(10, Goal, 1, (Shard, 2)), offer]));

        var text = Assert.Single(CostPresenter.Summarize(CostAnalyzer.Analyze(plan, PriceBook.Empty, 0), Name, Culture, CurrencyName).VendorLines);

        Assert.Equal("2× Obsidian Shard: koop bij een verkoper voor 50 Karma.", text);
    }

    [Fact]
    public void Costs_in_items_and_in_coin_are_written_out()
    {
        var offer = new AcquisitionOffer(
            1,
            Shard,
            1,
            [new Ingredient(MaterialKey.Currency(CoinFormatter.CoinCurrencyId), 12_345), new Ingredient(MaterialKey.Item(77), 3)],
            "Handelaar",
            string.Empty).ToRecipe();
        var plan = CraftingPlanner.Plan(new CraftGoal(Goal), Inventory(), new RecipeBook([Recipe(10, Goal, 1, (Shard, 1)), offer]));

        var text = Assert.Single(CostPresenter.Summarize(CostAnalyzer.Analyze(plan, PriceBook.Empty, 0), Name, Culture, CurrencyName).VendorLines);

        Assert.Contains("1g 23s 45c + 3× Item77", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Currency_lines_tell_whether_there_is_enough_or_how_much_is_missing()
    {
        var enough = CostPresenter.Summarize(CostAnalyzer.Analyze(Plan(300), PriceBook.Empty, 0, _ => 300), Name, Culture, CurrencyName);
        var short_ = CostPresenter.Summarize(CostAnalyzer.Analyze(Plan(100), PriceBook.Empty, 0, _ => 100), Name, Culture, CurrencyName);

        Assert.Equal("Karma: je hebt genoeg (300 van 250).", Assert.Single(enough.CurrencyLines));
        Assert.Equal("Karma: je hebt 100 van 250, nog 150 nodig.", Assert.Single(short_.CurrencyLines));
    }

    [Fact]
    public void An_unknown_currency_falls_back_to_its_id_and_coin_is_shown_as_gold_silver_copper()
    {
        var offer = new AcquisitionOffer(1, Shard, 1, [new Ingredient(MaterialKey.Currency(CoinFormatter.CoinCurrencyId), 20_000)], "v", "l").ToRecipe();
        var plan = CraftingPlanner.Plan(new CraftGoal(Goal), Inventory(null, [(CoinFormatter.CoinCurrencyId, 5_000)]), new RecipeBook([Recipe(10, Goal, 1, (Shard, 1)), offer]));

        var withoutNames = CostPresenter.Summarize(CostAnalyzer.Analyze(plan, PriceBook.Empty, 5_000, _ => 5_000), Name, Culture);

        Assert.Equal("currency #1: je hebt 50s van 2g, nog 1g 50s nodig.", Assert.Single(withoutNames.CurrencyLines));
    }
}

public sealed class AcquisitionExportTests : IDisposable
{
    private readonly FakeAcquisitionStore _acquisitionStore = new();
    private readonly FakeGoalStore _goalStore = new();
    private readonly AppState _state = new(new FakeSettingsStore(), new AppSettings());
    private readonly GoalService _goals;
    private readonly AcquisitionService _acquisitions;
    private readonly UserDataTransferService _transfer;

    public AcquisitionExportTests()
    {
        _goals = new GoalService(_goalStore);
        _acquisitions = new AcquisitionService(_acquisitionStore);
        _transfer = new UserDataTransferService(_state, _goals, new ManualTime(), _acquisitions);
    }

    public void Dispose()
    {
        _acquisitions.Dispose();
        _goals.Dispose();
        _state.Dispose();
    }

    [Fact]
    public async Task Offers_survive_export_and_import_including_mixed_costs()
    {
        await _acquisitions.AddAsync(new NewOffer(19925, 5, [new Ingredient(MaterialKey.Currency(2), 125), new Ingredient(MaterialKey.Item(77), 3)], "Miyani", "Timberline Falls"));
        var file = _transfer.Export();
        await _acquisitions.ReplaceAsync([]);

        var outcome = await _transfer.ImportAsync(file);

        Assert.True(outcome.Succeeded);
        Assert.Contains("1 eigen herkomstgegeven(s)", outcome.Message, StringComparison.Ordinal);
        var offer = Assert.Single(_acquisitions.Offers);
        Assert.Equal((19925, 5, "Miyani", "Timberline Falls"), (offer.ItemId, offer.OutputCount, offer.VendorName, offer.Location));
        Assert.Equal([new Ingredient(MaterialKey.Currency(2), 125), new Ingredient(MaterialKey.Item(77), 3)], offer.Costs);
    }

    [Fact]
    public async Task Export_import_export_is_stable_with_offers()
    {
        await _acquisitions.AddAsync(Offers.Karmic());
        var first = _transfer.Export();

        await _transfer.ImportAsync(first);

        Assert.Equal(first, _transfer.Export());
    }

    [Fact]
    public async Task A_file_from_before_offers_existed_keeps_the_current_offers()
    {
        await _acquisitions.AddAsync(Offers.Karmic());
        var file = $$"""{"format":"{{UserDataSerializer.FormatName}}","version":1,"goals":[]}""";

        var outcome = await _transfer.ImportAsync(file);

        Assert.True(outcome.Succeeded);
        Assert.Single(_acquisitions.Offers);
    }

    [Fact]
    public async Task A_file_with_an_empty_list_clears_the_offers()
    {
        await _acquisitions.AddAsync(Offers.Karmic());
        var file = $$"""{"format":"{{UserDataSerializer.FormatName}}","version":1,"goals":[],"acquisitions":[]}""";

        await _transfer.ImportAsync(file);

        Assert.Empty(_acquisitions.Offers);
    }

    [Fact]
    public void Invalid_offers_in_a_file_are_skipped_and_reported_and_the_rest_is_kept()
    {
        var result = UserDataSerializer.Parse($$"""
            {"format":"{{UserDataSerializer.FormatName}}","version":1,"goals":[],
             "acquisitions":[
               {"itemId":5,"outputCount":1,"costs":[{"kind":"currency","id":2,"count":25}],"vendorName":"A","location":"B"},
               {"itemId":0,"outputCount":1,"costs":[{"kind":"currency","id":2,"count":25}]},
               {"itemId":6,"outputCount":1,"costs":[]},
               {"itemId":7,"outputCount":1,"costs":[{"kind":"currency","id":2,"count":-1}]},
               null
             ]}
            """);

        Assert.True(result.Succeeded);
        Assert.Equal([5], result.Acquisitions!.Select(o => o.ItemId));
        Assert.Equal(4, result.Warnings.Count(w => w.StartsWith("Herkomst #", StringComparison.Ordinal)));
    }

    [Fact]
    public void Offers_in_a_file_are_capped()
    {
        var offers = Enumerable.Range(1, AcquisitionService.MaxOffers + 3)
            .Select(i => new AcquisitionOffer(i, i, 1, [new Ingredient(MaterialKey.Currency(2), 1)], string.Empty, string.Empty))
            .ToList();
        var json = UserDataSerializer.Serialize(new AppSettings(), [], DateTimeOffset.UnixEpoch, offers);

        var result = UserDataSerializer.Parse(json);

        Assert.Equal(AcquisitionService.MaxOffers, result.Acquisitions!.Count);
        Assert.Single(result.Warnings, w => w.Contains("hooguit", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Without_an_acquisition_service_offers_in_a_file_are_ignored_and_exports_have_none()
    {
        var plain = new UserDataTransferService(_state, _goals, new ManualTime());
        var file = UserDataSerializer.Serialize(new AppSettings(), [], DateTimeOffset.UnixEpoch, [new AcquisitionOffer(1, 5, 1, [new Ingredient(MaterialKey.Currency(2), 1)], "v", "l")]);

        var outcome = await plain.ImportAsync(file);

        Assert.True(outcome.Succeeded);
        Assert.DoesNotContain("herkomst", outcome.Message, StringComparison.Ordinal);
    }
}

public class VendorFileAndLinkTests
{
    [Fact]
    public void A_recipe_with_a_vendor_entry_is_a_vendor_offer_and_one_without_is_a_forge_recipe()
    {
        var result = LocalRecipeLoader.Parse("""
            { "recipes": [
              { "id": -1, "vendor": { "name": "Miyani", "location": "Timberline Falls" },
                "output": { "itemId": 19925, "count": 1, "name": "Obsidian Shard" },
                "ingredients": [ { "currencyId": 2, "count": 25, "name": "Karma" } ] },
              { "id": -2, "output": { "itemId": 1, "count": 1, "name": "Gift" },
                "ingredients": [ { "itemId": 2, "count": 1, "name": "Part" } ] }
            ] }
            """);

        Assert.Empty(result.Errors);
        Assert.Equal(RecipeKind.Vendor, result.Recipes[0].Kind);
        Assert.Equal("Miyani, Timberline Falls", result.Recipes[0].Note);
        Assert.Equal(RecipeKind.MysticForge, result.Recipes[1].Kind);
        Assert.Null(result.Recipes[1].Note);
        Assert.Equal("Karma", result.ExpectedCurrencyNames?[2]);
    }

    [Fact]
    public void An_overlong_vendor_text_is_refused_with_a_reason()
    {
        var result = LocalRecipeLoader.Parse($$"""
            { "recipes": [ { "id": -1, "vendor": { "name": "{{new string('x', 300)}}" },
              "output": { "itemId": 1, "count": 1 }, "ingredients": [ { "currencyId": 2, "count": 1 } ] } ] }
            """);

        Assert.Empty(result.Recipes);
        Assert.Contains("verkoper", Assert.Single(result.Errors), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Obsidian Shard", "https://wiki.guildwars2.com/wiki/Special:Search?search=Obsidian%20Shard")]
    [InlineData("Philosopher's Stone", "https://wiki.guildwars2.com/wiki/Special:Search?search=Philosopher%27s%20Stone")]
    [InlineData("  Gift of Might ", "https://wiki.guildwars2.com/wiki/Special:Search?search=Gift%20of%20Might")]
    [InlineData("A&B=C", "https://wiki.guildwars2.com/wiki/Special:Search?search=A%26B%3DC")]
    public void The_wiki_link_searches_for_the_name_and_escapes_it(string name, string expected)
    {
        var url = ItemLinks.Wiki(name);

        Assert.Equal(expected, url);
        Assert.True(ItemLinks.IsSafe(url));
    }

    [Fact]
    public void The_gw2treasures_link_points_at_exactly_this_item()
    {
        Assert.Equal("https://en.gw2treasures.com/item/19925", ItemLinks.Treasures(19925));
        Assert.True(ItemLinks.IsSafe(ItemLinks.Treasures(19925)));
        Assert.Throws<ArgumentOutOfRangeException>(() => ItemLinks.Treasures(0));
        Assert.Throws<ArgumentException>(() => ItemLinks.Wiki(" "));
    }

    [Theory]
    [InlineData("http://wiki.guildwars2.com/wiki/X")]
    [InlineData("https://wiki.guildwars2.com.evil.example/wiki/X")]
    [InlineData("https://evil.example/wiki.guildwars2.com")]
    [InlineData("https://user@evil.example/")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    [InlineData("ftp://wiki.guildwars2.com/x")]
    [InlineData("wiki.guildwars2.com")]
    [InlineData("")]
    [InlineData(null)]
    public void Only_https_links_to_the_two_known_sites_are_considered_safe(string? url)
    {
        Assert.False(ItemLinks.IsSafe(url));
    }
}

public class AcquisitionInfrastructureTests
{
    [Fact]
    public async Task Offers_round_trip_with_the_order_and_kind_of_their_costs()
    {
        using var temp = new TempDatabase();
        await temp.Database.MigrateAsync();
        var store = new SqliteAcquisitionStore(temp.Database);
        var offers = new[]
        {
            new AcquisitionOffer(1, 19925, 1, [new Ingredient(MaterialKey.Currency(2), 25)], "Miyani", "Timberline Falls"),
            new AcquisitionOffer(3, 5, 10, [new Ingredient(MaterialKey.Item(77), 3), new Ingredient(MaterialKey.Currency(23), 9), new Ingredient(MaterialKey.Currency(2), 1)], string.Empty, string.Empty),
        };

        await store.SaveAsync(offers);
        var loaded = await store.LoadAsync();

        Assert.Equal(offers, loaded);
    }

    [Fact]
    public async Task Saving_replaces_the_previous_offers_and_their_costs()
    {
        using var temp = new TempDatabase();
        await temp.Database.MigrateAsync();
        var store = new SqliteAcquisitionStore(temp.Database);
        await store.SaveAsync([new AcquisitionOffer(1, 1, 1, [new Ingredient(MaterialKey.Currency(2), 5), new Ingredient(MaterialKey.Currency(3), 5)], "a", "b")]);

        await store.SaveAsync([new AcquisitionOffer(1, 2, 1, [new Ingredient(MaterialKey.Currency(4), 1)], "c", "d")]);

        var offer = Assert.Single(await store.LoadAsync());
        Assert.Equal([new Ingredient(MaterialKey.Currency(4), 1)], offer.Costs);
    }

    [Fact]
    public async Task An_empty_store_loads_nothing_and_a_provider_book_includes_the_offers()
    {
        using var temp = new TempDatabase();
        await temp.Database.MigrateAsync();
        var store = new SqliteAcquisitionStore(temp.Database);
        Assert.Empty(await store.LoadAsync());

        using var service = new AcquisitionService(store);
        await service.AddAsync(Offers.Karmic(19925, 25, "Miyani", "Timberline Falls"));
        var api = new FakeApiClient();
        var provider = new RecipeBookProvider(
            new RecipeCatalog(api, new FakeRecipeStore()),
            new Gw2LegendaryTracker.Core.Crafting.Forge.ForgeRecipeCatalog(new FakeForgeApiClient(), new FakeApiKeyStore(), new FakeForgeRecipeStore(), api),
            _ => Task.FromResult(new LocalRecipes([], new HashSet<int>(), [])),
            service);

        var result = await provider.BuildAsync();

        var recipe = Assert.Single(result.Book.RecipesFor(19925));
        Assert.Equal(RecipeKind.Vendor, recipe.Kind);
        Assert.Equal(0, result.SingleSourceCount);
    }

    [Fact]
    public async Task All_currency_names_come_from_the_public_endpoint()
    {
        var handler = new StubHandler(_ => StubHandler.Json("""[{"id":1,"name":"Coin"},{"id":2,"name":"Karma"},{"id":3,"name":""}]"""));
        var api = new Gw2ApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.guildwars2.com/") });

        var names = await api.GetAllCurrencyNamesAsync();

        Assert.Equal("/v2/currencies?ids=all&lang=en", Assert.Single(handler.Requests).RequestUri!.PathAndQuery);
        Assert.Equal(["Coin", "Karma"], names.Values.Order());
    }

    [Fact]
    public async Task The_currency_catalog_loads_the_whole_list_once_and_remembers_it()
    {
        var api = new FakeApiClient();
        api.Currencies[2] = "Karma";
        api.Currencies[23] = "Spirit Shard";
        var catalog = new Gw2LegendaryTracker.Core.Items.CurrencyCatalog(api);

        var first = await catalog.AllAsync();
        api.Currencies[99] = "Added later";
        var second = await catalog.AllAsync();

        Assert.Equal(["Karma", "Spirit Shard"], first.Values.Order());
        Assert.Equal(first, second);
        Assert.Equal("Karma", (await catalog.ResolveAsync([2]))[2]);
    }
}
