using System.Net;
using Gw2LegendaryTracker.Core.Cosmetics;
using Gw2LegendaryTracker.Core.Gw2;
using Gw2LegendaryTracker.Infrastructure.Gw2;
using Gw2LegendaryTracker.Infrastructure.Persistence;

namespace Gw2LegendaryTracker.Tests;

internal static class CosmeticsTestData
{
    public static CosmeticSkin Armor(int id, string name, string slot = "Helm", string weight = "Heavy", params string[] races) =>
        new(id, name, $"https://icon/{id}.png", SkinCategory.Armor, slot, weight, "Fine", races, 3, null);

    public static CosmeticSkin Weapon(int id, string name, string type = "Sword") =>
        new(id, name, null, SkinCategory.Weapon, type, null, "Rare", [], 0, "A blade.");

    public static CosmeticSkin Back(int id, string name) =>
        new(id, name, null, SkinCategory.Back, "Back", null, "Basic", [], 0, null);
}

public class SkinSearchTests
{
    private static readonly CosmeticSkin[] Skins =
    [
        CosmeticsTestData.Armor(1, "Zealot Helm", "Helm", "Heavy"),
        CosmeticsTestData.Armor(2, "Apprentice Helm", "Helm", "Light"),
        CosmeticsTestData.Armor(3, "Tailor Hat", "Helm", "Clothing"),
        CosmeticsTestData.Armor(4, "Charr Coat", "Coat", "Heavy", "Charr"),
        CosmeticsTestData.Weapon(5, "Sunrise"),
        CosmeticsTestData.Back(6, "Wings"),
    ];

    private static IEnumerable<int> Ids(SkinQuery query, IReadOnlySet<int>? owned = null) =>
        SkinSearch.Apply(Skins, query, owned).Select(s => s.Id);

    [Fact]
    public void An_empty_query_shows_everything_sorted_by_name()
    {
        Assert.Equal([2, 4, 5, 3, 6, 1], Ids(new SkinQuery()));
    }

    [Fact]
    public void Category_and_slot_narrow_the_list()
    {
        Assert.Equal([5], Ids(new SkinQuery(Category: SkinCategory.Weapon)));
        Assert.Equal([4], Ids(new SkinQuery(Category: SkinCategory.Armor, Slot: "coat")));
    }

    [Fact]
    public void Weight_class_keeps_clothing_and_never_filters_other_categories()
    {
        Assert.Equal([2, 3, 5, 6], Ids(new SkinQuery(WeightClass: "Light")).Order());
    }

    [Fact]
    public void A_racial_skin_only_fits_that_race_but_unrestricted_skins_fit_everyone()
    {
        Assert.DoesNotContain(4, Ids(new SkinQuery(Race: "Human")));
        Assert.Contains(4, Ids(new SkinQuery(Race: "charr")));
        Assert.Contains(1, Ids(new SkinQuery(Race: "Human")));
    }

    [Fact]
    public void Text_matches_any_part_of_the_name_ignoring_case()
    {
        Assert.Equal([2, 1], Ids(new SkinQuery(Text: "  HELM ")));
    }

    [Fact]
    public void Owned_only_hides_what_is_not_unlocked()
    {
        Assert.Equal([1, 6], Ids(new SkinQuery(OwnedOnly: true), new HashSet<int> { 1, 6 }).Order());
    }

    [Fact]
    public void Owned_only_is_ignored_when_the_unlocks_are_unknown()
    {
        Assert.Equal(Skins.Length, Ids(new SkinQuery(OwnedOnly: true), owned: null).Count());
    }
}

public class CharacterLookTests
{
    [Fact]
    public void Changing_a_look_returns_a_new_look_and_leaves_the_old_one_alone()
    {
        var empty = CharacterLook.Default;

        var changed = empty.WithSkin(LookSlot.Helm, 10);

        Assert.Null(empty.Get(LookSlot.Helm).SkinId);
        Assert.Equal(10, changed.Get(LookSlot.Helm).SkinId);
    }

    [Fact]
    public void Another_skin_keeps_the_dyes_of_the_slot()
    {
        var look = CharacterLook.Default.WithSkin(LookSlot.Coat, 1).WithDye(LookSlot.Coat, 2, 77).WithSkin(LookSlot.Coat, 2);

        Assert.Equal(new SlotLook(2, null, null, 77, null), look.Get(LookSlot.Coat));
    }

    [Fact]
    public void A_slot_without_skin_and_dyes_is_removed_by_clearing_it()
    {
        var look = CharacterLook.Default.WithSkin(LookSlot.Boots, 1).ClearSlot(LookSlot.Boots);

        Assert.Empty(look.Slots);
        Assert.Equal(CharacterLook.Default, look);
    }

    [Fact]
    public void Looks_compare_by_content()
    {
        var a = CharacterLook.Default.WithSkin(LookSlot.Helm, 1).WithDye(LookSlot.Helm, 0, 5);
        var b = CharacterLook.Default.WithDye(LookSlot.Helm, 0, 5).WithSkin(LookSlot.Helm, 1);

        Assert.Equal(a, b);
        Assert.NotEqual(a, b.WithDye(LookSlot.Helm, 1, 6));
        Assert.NotEqual(a, a.WithCharacter("Norn", "Male", "Warrior"));
    }

    [Fact]
    public void Dye_channels_outside_the_four_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CharacterLook.Default.WithDye(LookSlot.Helm, 4, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => default(SlotLook).GetDye(-1));
    }

    [Fact]
    public void A_look_survives_a_round_trip_through_json()
    {
        var look = new CharacterLook("Sylvari", "Female", "Mesmer")
            .WithSkin(LookSlot.Helm, 12).WithDye(LookSlot.Helm, 0, 3).WithDye(LookSlot.Helm, 3, 9)
            .WithSkin(LookSlot.MainHand, 99);

        var restored = LookJson.Deserialize(LookJson.Serialize(look));

        Assert.Equal(look, restored);
    }

    [Fact]
    public void Unknown_slots_are_ignored_and_garbage_is_not_a_look()
    {
        var restored = LookJson.Deserialize("""{"race":"Human","gender":"Male","profession":"Thief","slots":[{"slot":"Tail","skin":1},{"slot":"Boots","skin":2}]}""");

        Assert.NotNull(restored);
        Assert.Equal(2, restored.Get(LookSlot.Boots).SkinId);
        Assert.Single(restored.Slots);
        Assert.Null(LookJson.Deserialize("not json"));
        Assert.Null(LookJson.Deserialize("""{"race":"","gender":"Male","profession":"Thief"}"""));
        Assert.Null(LookJson.Deserialize("null"));
    }

    [Theory]
    [InlineData("Guardian", "Heavy")]
    [InlineData("Revenant", "Heavy")]
    [InlineData("Ranger", "Medium")]
    [InlineData("Mesmer", "Light")]
    [InlineData("Something else", null)]
    public void Each_profession_wears_one_weight_class(string profession, string? expected) =>
        Assert.Equal(expected, CharacterOptions.WeightClassOf(profession));

    [Fact]
    public void Every_profession_is_known_to_the_weight_class_table()
    {
        Assert.All(CharacterOptions.Professions, p => Assert.NotNull(CharacterOptions.WeightClassOf(p)));
    }

    [Theory]
    [InlineData("Helm", LookSlot.Helm)]
    [InlineData("Backpack", LookSlot.Back)]
    [InlineData("WeaponA1", LookSlot.MainHand)]
    [InlineData("WeaponA2", LookSlot.OffHand)]
    public void Equipment_slots_map_to_studio_slots(string equipmentSlot, LookSlot expected) =>
        Assert.Equal(expected, CharacterOptions.SlotOfEquipment(equipmentSlot));

    [Theory]
    [InlineData("HelmAquatic")]
    [InlineData("WeaponB1")]
    [InlineData("Amulet")]
    public void Slots_the_studio_does_not_cover_map_to_nothing(string equipmentSlot) =>
        Assert.Null(CharacterOptions.SlotOfEquipment(equipmentSlot));
}

public class CosmeticsCatalogTests
{
    private sealed class FakeCosmeticsApi : ICosmeticsApiClient
    {
        public List<CosmeticSkin> Skins { get; } = [CosmeticsTestData.Armor(1, "A")];

        public List<DyeColor> Colors { get; } = [new(1, "Red", 255, 0, 0, null, null, null)];

        public Gw2ApiException? Failure { get; set; }

        public int SkinCalls { get; private set; }

        public int ColorCalls { get; private set; }

        public Task<IReadOnlyList<CosmeticSkin>> GetAllSkinsAsync(IProgress<int>? progress = null, CancellationToken cancellationToken = default)
        {
            SkinCalls++;
            return Failure is null ? Task.FromResult<IReadOnlyList<CosmeticSkin>>(Skins) : Task.FromException<IReadOnlyList<CosmeticSkin>>(Failure);
        }

        public Task<IReadOnlyList<DyeColor>> GetAllDyeColorsAsync(CancellationToken cancellationToken = default)
        {
            ColorCalls++;
            return Failure is null ? Task.FromResult<IReadOnlyList<DyeColor>>(Colors) : Task.FromException<IReadOnlyList<DyeColor>>(Failure);
        }

        public Task<IReadOnlySet<int>> GetUnlockedSkinIdsAsync(string apiKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlySet<int>> GetUnlockedDyeIdsAsync(string apiKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<string>> GetCharacterNamesAsync(string apiKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<CharacterLook> GetCharacterLookAsync(string apiKey, string characterName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeCosmeticsStore : ICosmeticsStore
    {
        public List<CosmeticSkin> Skins { get; } = [];

        public List<DyeColor> Colors { get; } = [];

        public DateTimeOffset? Fetched { get; set; }

        public Task<IReadOnlyList<CosmeticSkin>> LoadSkinsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CosmeticSkin>>([.. Skins]);

        public Task SaveSkinsAsync(IReadOnlyList<CosmeticSkin> skins, DateTimeOffset fetchedUtc, CancellationToken cancellationToken = default)
        {
            Skins.Clear();
            Skins.AddRange(skins);
            Fetched = fetchedUtc;
            return Task.CompletedTask;
        }

        public Task<DateTimeOffset?> GetSkinsFetchedUtcAsync(CancellationToken cancellationToken = default) => Task.FromResult(Fetched);

        public Task<IReadOnlyList<DyeColor>> LoadColorsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DyeColor>>([.. Colors]);

        public Task SaveColorsAsync(IReadOnlyList<DyeColor> colors, CancellationToken cancellationToken = default)
        {
            Colors.Clear();
            Colors.AddRange(colors);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<LookPreset>> LoadPresetsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task SavePresetAsync(LookPreset preset, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task DeletePresetAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeCosmeticsApi _api = new();
    private readonly FakeCosmeticsStore _store = new();

    private CosmeticsCatalog Create() => new(_api, _store, new FixedTime(Now));

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public async Task Without_a_cache_everything_is_fetched_and_stored_before_use()
    {
        var catalog = Create();

        var result = await catalog.RefreshAsync();

        Assert.Equal(CatalogRefreshResult.Updated, result);
        Assert.Single(catalog.Skins);
        Assert.Single(catalog.Colors);
        Assert.Single(_store.Skins);
        Assert.Equal(Now, _store.Fetched);
    }

    [Fact]
    public async Task A_recent_cache_is_used_without_calling_the_api()
    {
        _store.Skins.Add(CosmeticsTestData.Armor(9, "Cached"));
        _store.Colors.Add(new DyeColor(9, "Cached", 1, 2, 3, null, null, null));
        _store.Fetched = Now - TimeSpan.FromDays(2);
        var catalog = Create();
        await catalog.LoadCacheAsync();

        var result = await catalog.RefreshAsync();

        Assert.Equal(CatalogRefreshResult.UpToDate, result);
        Assert.Equal(0, _api.SkinCalls);
        Assert.Equal("Cached", Assert.Single(catalog.Skins).Name);
    }

    [Fact]
    public async Task An_old_cache_is_refreshed()
    {
        _store.Skins.Add(CosmeticsTestData.Armor(9, "Old"));
        _store.Colors.Add(new DyeColor(9, "Old", 1, 2, 3, null, null, null));
        _store.Fetched = Now - CosmeticsCatalog.MaxAge - TimeSpan.FromMinutes(1);
        var catalog = Create();
        await catalog.LoadCacheAsync();

        var result = await catalog.RefreshAsync();

        Assert.Equal(CatalogRefreshResult.Updated, result);
        Assert.Equal("A", Assert.Single(catalog.Skins).Name);
    }

    [Fact]
    public async Task Force_refreshes_even_a_recent_cache()
    {
        _store.Skins.Add(CosmeticsTestData.Armor(9, "Cached"));
        _store.Colors.Add(new DyeColor(9, "Cached", 1, 2, 3, null, null, null));
        _store.Fetched = Now;
        var catalog = Create();
        await catalog.LoadCacheAsync();

        await catalog.RefreshAsync(force: true);

        Assert.Equal(1, _api.SkinCalls);
    }

    [Fact]
    public async Task When_the_api_fails_the_old_cache_stays_in_use_and_is_not_overwritten()
    {
        _store.Skins.Add(CosmeticsTestData.Armor(9, "Cached"));
        _store.Colors.Add(new DyeColor(9, "Cached", 1, 2, 3, null, null, null));
        _store.Fetched = Now - TimeSpan.FromDays(30);
        _api.Failure = new Gw2ApiException(Gw2ApiErrorKind.Offline, "offline");
        var catalog = Create();
        await catalog.LoadCacheAsync();

        var result = await catalog.RefreshAsync();

        Assert.Equal(CatalogRefreshResult.Offline, result);
        Assert.Equal("Cached", Assert.Single(catalog.Skins).Name);
        Assert.Equal("Cached", Assert.Single(_store.Skins).Name);
        Assert.Equal(Now - TimeSpan.FromDays(30), _store.Fetched);
    }
}

public class CosmeticsApiClientTests
{
    private static Gw2ApiClient CreateClient(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://api.guildwars2.com/") });

    private const string ArmorSkin = """
        {"id":1,"name":"Zealot Helm","type":"Armor","icon":"https://icon/1.png","rarity":"Rare","restrictions":["Human","Norn"],
         "details":{"type":"Helm","weight_class":"Heavy","dye_slots":{"default":[{"color_id":5,"material":"cloth"},null,{"color_id":6,"material":"metal"},null]}}}
        """;

    [Fact]
    public async Task Skins_are_read_in_chunks_with_slot_weight_races_and_dye_slots()
    {
        var ids = Enumerable.Range(1, 450).ToList();
        var handler = new StubHandler(request =>
        {
            var query = request.RequestUri!.Query;
            if (query.Length == 0)
            {
                return StubHandler.Json($"[{string.Join(',', ids)}]");
            }

            return StubHandler.Json($"[{ArmorSkin}]");
        });
        var progress = new List<int>();

        var skins = await CreateClient(handler).GetAllSkinsAsync(new Progress<int>(progress.Add));

        Assert.Equal(1 + 3, handler.Requests.Count);
        Assert.All(handler.Requests.Skip(1), r => Assert.Contains("lang=en", r.RequestUri!.Query, StringComparison.Ordinal));
        Assert.Equal(3, skins.Count);
        var skin = skins[0];
        Assert.Equal((1, "Zealot Helm", SkinCategory.Armor, "Helm", "Heavy", 2), (skin.Id, skin.Name, skin.Category, skin.Slot, skin.WeightClass, skin.DyeSlotCount));
        Assert.Equal(["Human", "Norn"], skin.Races);
    }

    [Fact]
    public async Task Skins_that_cannot_be_shown_are_left_out()
    {
        var json = "[" + ArmorSkin + """
            ,
              {"id":2,"name":"Wings","type":"Back","icon":"https://icon/2.png"},
              {"id":3,"name":"Pick","type":"Gathering","details":{"type":"Mining"}},
              {"id":4,"name":"Mystery","type":"Spaceship","details":{"type":"X"}},
              {"id":5,"name":"","type":"Weapon","details":{"type":"Sword"}},
              {"id":6,"name":"No slot","type":"Weapon"}
            ]
            """;
        var handler = new StubHandler(request => StubHandler.Json(request.RequestUri!.Query.Length == 0 ? "[1,2,3,4,5,6]" : json));

        var skins = await CreateClient(handler).GetAllSkinsAsync();

        Assert.Equal([1, 2, 3], skins.Select(s => s.Id));
        Assert.Equal("Back", skins[1].Slot);
        Assert.Equal("Mining", skins[2].Slot);
        Assert.Equal("Basic", skins[1].Rarity);
        Assert.Empty(skins[1].Races);
    }

    [Fact]
    public async Task Colors_use_the_cloth_rendering_and_fall_back_to_the_base_colour()
    {
        var handler = new StubHandler(_ => StubHandler.Json("""
            [
              {"id":10,"name":"Sky","base_rgb":[128,26,26],"cloth":{"rgb":[54,130,160]},"categories":["Blue","Vibrant","Common"]},
              {"id":11,"name":"Plain","base_rgb":[1,2,3]},
              {"id":12,"name":"Broken","base_rgb":[1,2]}
            ]
            """));

        var colors = await CreateClient(handler).GetAllDyeColorsAsync();

        Assert.Equal(2, colors.Count);
        Assert.Equal(new DyeColor(10, "Sky", 54, 130, 160, "Blue", "Vibrant", "Common"), colors[0]);
        Assert.Equal("#010203", colors[1].Hex);
        Assert.Null(colors[1].Hue);
    }

    [Fact]
    public async Task Unlocks_are_read_with_the_key_in_the_header_only()
    {
        var handler = new StubHandler(request => StubHandler.Json(request.RequestUri!.AbsolutePath.EndsWith("skins", StringComparison.Ordinal) ? "[1,2,2]" : "[7]"));
        var client = CreateClient(handler);

        var skins = await client.GetUnlockedSkinIdsAsync(TestData.Key);
        var dyes = await client.GetUnlockedDyeIdsAsync(TestData.Key);

        Assert.Equal([1, 2], skins.Order());
        Assert.Equal([7], dyes);
        Assert.All(handler.Requests, r =>
        {
            Assert.Equal(TestData.Key, r.Headers.Authorization?.Parameter);
            Assert.DoesNotContain(TestData.Key, r.RequestUri!.ToString(), StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task A_key_without_the_unlocks_permission_gives_a_missing_permissions_error()
    {
        var client = CreateClient(new StubHandler(_ => StubHandler.Json("""{"text":"requires scope unlocks"}""", HttpStatusCode.Forbidden)));

        var ex = await Assert.ThrowsAsync<Gw2ApiException>(() => client.GetUnlockedSkinIdsAsync(TestData.Key));

        Assert.Equal(Gw2ApiErrorKind.MissingPermissions, ex.Kind);
    }

    [Fact]
    public async Task A_character_look_reads_race_dyes_and_only_the_slots_the_studio_covers()
    {
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath.EndsWith("core", StringComparison.Ordinal)
            ? StubHandler.Json("""{"name":"Mijn Held","race":"Asura","gender":"Female","profession":"Elementalist"}""")
            : StubHandler.Json("""
                {"equipment":[
                  {"id":1,"slot":"Helm","skin":100,"dyes":[5,null,7]},
                  {"id":2,"slot":"Coat","dyes":[1,2]},
                  {"id":3,"slot":"Backpack","skin":200,"dyes":null},
                  {"id":4,"slot":"WeaponA1","skin":300},
                  {"id":5,"slot":"HelmAquatic","skin":400},
                  {"id":6,"slot":"Amulet"}
                ]}
                """));

        var look = await CreateClient(handler).GetCharacterLookAsync(TestData.Key, "Mijn Held");

        Assert.Equal(("Asura", "Female", "Elementalist"), (look.Race, look.Gender, look.Profession));
        Assert.Equal(new SlotLook(100, 5, null, 7, null), look.Get(LookSlot.Helm));
        Assert.Equal(200, look.Get(LookSlot.Back).SkinId);
        Assert.Equal(300, look.Get(LookSlot.MainHand).SkinId);
        Assert.Null(look.Get(LookSlot.Coat).SkinId);
        Assert.Equal(3, look.Slots.Count);
        Assert.All(handler.Requests, r => Assert.Contains("Mijn%20Held", r.RequestUri!.AbsoluteUri, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Character_names_are_listed()
    {
        var client = CreateClient(new StubHandler(_ => StubHandler.Json("""["A","B C"]""")));

        Assert.Equal(["A", "B C"], await client.GetCharacterNamesAsync(TestData.Key));
    }
}

public class SqliteCosmeticsStoreTests
{
    [Fact]
    public async Task Skins_round_trip_including_races_and_missing_fields()
    {
        using var temp = new TempDatabase();
        await temp.Database.MigrateAsync();
        var store = new SqliteCosmeticsStore(temp.Database);
        var skins = new[]
        {
            CosmeticsTestData.Armor(1, "Racial", "Coat", "Light", "Asura", "Charr"),
            CosmeticsTestData.Weapon(2, "Sunrise"),
            CosmeticsTestData.Back(3, "Wings"),
        };

        await store.SaveSkinsAsync(skins, new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero));
        var loaded = (await store.LoadSkinsAsync()).OrderBy(s => s.Id).ToList();

        Assert.Equal(skins, loaded);
        Assert.Equal(new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero), await store.GetSkinsFetchedUtcAsync());
    }

    [Fact]
    public async Task Saving_skins_replaces_the_previous_list()
    {
        using var temp = new TempDatabase();
        await temp.Database.MigrateAsync();
        var store = new SqliteCosmeticsStore(temp.Database);
        await store.SaveSkinsAsync([CosmeticsTestData.Armor(1, "Old"), CosmeticsTestData.Armor(2, "Gone")], DateTimeOffset.UnixEpoch);

        await store.SaveSkinsAsync([CosmeticsTestData.Armor(1, "New")], DateTimeOffset.UnixEpoch.AddDays(1));

        Assert.Equal("New", Assert.Single(await store.LoadSkinsAsync()).Name);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddDays(1), await store.GetSkinsFetchedUtcAsync());
    }

    [Fact]
    public async Task Without_a_cache_there_is_no_fetch_time()
    {
        using var temp = new TempDatabase();
        await temp.Database.MigrateAsync();

        Assert.Null(await new SqliteCosmeticsStore(temp.Database).GetSkinsFetchedUtcAsync());
    }

    [Fact]
    public async Task Colors_round_trip_and_are_replaced()
    {
        using var temp = new TempDatabase();
        await temp.Database.MigrateAsync();
        var store = new SqliteCosmeticsStore(temp.Database);
        var sky = new DyeColor(10, "Sky", 54, 130, 160, "Blue", "Vibrant", "Common");

        await store.SaveColorsAsync([sky, new DyeColor(11, "Plain", 1, 2, 3, null, null, null)]);
        await store.SaveColorsAsync([sky]);

        Assert.Equal(sky, Assert.Single(await store.LoadColorsAsync()));
    }

    [Fact]
    public async Task Presets_are_saved_overwritten_listed_by_name_and_deleted()
    {
        using var temp = new TempDatabase();
        await temp.Database.MigrateAsync();
        var store = new SqliteCosmeticsStore(temp.Database);
        var at = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        var look = CharacterLook.Default.WithSkin(LookSlot.Helm, 5).WithDye(LookSlot.Helm, 0, 9);

        await store.SavePresetAsync(new LookPreset("b", "beta", look, at));
        await store.SavePresetAsync(new LookPreset("a", "Alpha", CharacterLook.Default, at));
        await store.SavePresetAsync(new LookPreset("b", "Beta 2", look, at.AddDays(1)));
        var presets = await store.LoadPresetsAsync();

        Assert.Equal(["Alpha", "Beta 2"], presets.Select(p => p.Name));
        Assert.Equal(look, presets[1].Look);
        Assert.Equal(at.AddDays(1), presets[1].UpdatedUtc);

        await store.DeletePresetAsync("a");
        Assert.Equal("b", Assert.Single(await store.LoadPresetsAsync()).Id);
    }

    [Fact]
    public async Task A_corrupt_preset_is_skipped_without_hiding_the_others()
    {
        using var temp = new TempDatabase();
        await temp.Database.MigrateAsync();
        var store = new SqliteCosmeticsStore(temp.Database);
        await store.SavePresetAsync(new LookPreset("ok", "Fine", CharacterLook.Default, DateTimeOffset.UnixEpoch));
        await using (var connection = await temp.Database.OpenAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO look_preset (id, name, look_json, updated_utc) VALUES ('bad', 'Broken', 'not json', '2026-01-01T00:00:00.0000000+00:00');";
            await command.ExecuteNonQueryAsync();
        }

        Assert.Equal("ok", Assert.Single(await store.LoadPresetsAsync()).Id);
    }
}

public class SkinSlotsTests
{
    [Theory]
    [InlineData("Helm", LookSlot.Helm, true)]
    [InlineData("Helm", LookSlot.Coat, false)]
    [InlineData("HelmAquatic", LookSlot.Helm, false)]
    [InlineData("Boots", LookSlot.Boots, true)]
    public void Armor_goes_in_its_own_slot_only(string armorSlot, LookSlot slot, bool expected) =>
        Assert.Equal(expected, SkinSlots.Fits(CosmeticsTestData.Armor(1, "x", armorSlot), slot));

    [Theory]
    [InlineData("Greatsword", true, false)]
    [InlineData("Scepter", true, false)]
    [InlineData("Shield", false, true)]
    [InlineData("Torch", false, true)]
    [InlineData("Sword", true, true)]
    [InlineData("Trident", false, false)]
    [InlineData("LargeBundle", false, false)]
    public void Weapons_fit_the_hand_they_can_be_held_in(string type, bool main, bool off)
    {
        var skin = CosmeticsTestData.Weapon(1, "x", type);

        Assert.Equal(main, SkinSlots.Fits(skin, LookSlot.MainHand));
        Assert.Equal(off, SkinSlots.Fits(skin, LookSlot.OffHand));
        Assert.False(SkinSlots.Fits(skin, LookSlot.Back));
        Assert.False(SkinSlots.Fits(skin, LookSlot.Helm));
    }

    [Fact]
    public void Back_items_only_fit_the_back_slot()
    {
        var wings = CosmeticsTestData.Back(1, "Wings");

        Assert.True(SkinSlots.Fits(wings, LookSlot.Back));
        Assert.False(SkinSlots.Fits(wings, LookSlot.MainHand));
    }

    [Fact]
    public void Every_slot_has_a_label()
    {
        var labels = Enum.GetValues<LookSlot>().Select(SkinSlots.Label).ToList();

        Assert.All(labels, label => Assert.False(string.IsNullOrWhiteSpace(label)));
        Assert.Equal(labels.Count, labels.Distinct().Count());
    }

    [Fact]
    public void A_query_for_a_slot_keeps_only_skins_that_fit_the_character()
    {
        CosmeticSkin[] skins =
        [
            CosmeticsTestData.Armor(1, "Heavy helm", "Helm", "Heavy"),
            CosmeticsTestData.Armor(2, "Light helm", "Helm", "Light"),
            CosmeticsTestData.Armor(3, "Light coat", "Coat", "Light"),
            CosmeticsTestData.Armor(4, "Charr helm", "Helm", "Heavy", "Charr"),
        ];

        var result = SkinSearch.Apply(skins, new SkinQuery(ForSlot: LookSlot.Helm, WeightClass: "Heavy", Race: "Human"), null);

        Assert.Equal([1], result.Select(s => s.Id));
    }
}
