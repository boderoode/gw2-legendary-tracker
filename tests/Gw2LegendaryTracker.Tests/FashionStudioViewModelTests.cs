using Gw2LegendaryTracker.App.ViewModels;
using Gw2LegendaryTracker.Core.Cosmetics;
using Gw2LegendaryTracker.Core.Gw2;

namespace Gw2LegendaryTracker.Tests;

public class FashionStudioViewModelTests
{
    private sealed class StudioApi : ICosmeticsApiClient
    {
        public List<CosmeticSkin> Skins { get; } =
        [
            new(1, "Heavy Helm", "https://icon/1.png", SkinCategory.Armor, "Helm", "Heavy", "Rare", [], 3, "Strong."),
            new(2, "Light Helm", "https://icon/2.png", SkinCategory.Armor, "Helm", "Light", "Fine", [], 2, null),
            new(3, "Charr Helm", "https://icon/3.png", SkinCategory.Armor, "Helm", "Heavy", "Rare", ["Charr"], 3, null),
            new(4, "Heavy Coat", "https://icon/4.png", SkinCategory.Armor, "Coat", "Heavy", "Rare", [], 4, null),
            new(5, "Sunrise", null, SkinCategory.Weapon, "Sword", null, "Exotic", [], 0, null),
            new(6, "Wings", null, SkinCategory.Back, "Back", null, "Basic", [], 0, null),
        ];

        public List<DyeColor> Colors { get; } =
        [
            new(10, "Sky", 54, 130, 160, "Blue", "Vibrant", "Common"),
            new(11, "Rose", 200, 60, 90, "Red", "Vibrant", "Common"),
        ];

        public IReadOnlySet<int>? OwnedSkins { get; set; } = new HashSet<int> { 1 };

        public IReadOnlySet<int> OwnedDyes { get; set; } = new HashSet<int> { 10 };

        public Gw2ApiException? UnlocksFailure { get; set; }

        public CharacterLook CharacterLook { get; set; } = new CharacterLook("Norn", "Male", "Mesmer").WithSkin(LookSlot.Helm, 2);

        public Task<IReadOnlyList<CosmeticSkin>> GetAllSkinsAsync(IProgress<int>? progress = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CosmeticSkin>>(Skins);

        public Task<IReadOnlyList<DyeColor>> GetAllDyeColorsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DyeColor>>(Colors);

        public Task<IReadOnlySet<int>> GetUnlockedSkinIdsAsync(string apiKey, CancellationToken cancellationToken = default) =>
            UnlocksFailure is null ? Task.FromResult(OwnedSkins!) : Task.FromException<IReadOnlySet<int>>(UnlocksFailure);

        public Task<IReadOnlySet<int>> GetUnlockedDyeIdsAsync(string apiKey, CancellationToken cancellationToken = default) =>
            UnlocksFailure is null ? Task.FromResult(OwnedDyes) : Task.FromException<IReadOnlySet<int>>(UnlocksFailure);

        public Task<IReadOnlyList<string>> GetCharacterNamesAsync(string apiKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(["Mijn Held", "Tweede"]);

        public Task<CharacterLook> GetCharacterLookAsync(string apiKey, string characterName, CancellationToken cancellationToken = default) =>
            Task.FromResult(CharacterLook);
    }

    private sealed class StudioStore : ICosmeticsStore
    {
        public List<CosmeticSkin> Skins { get; } = [];

        public List<DyeColor> Colors { get; } = [];

        public DateTimeOffset? Fetched { get; set; }

        public Dictionary<string, LookPreset> Presets { get; } = [];

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

        public Task<IReadOnlyList<LookPreset>> LoadPresetsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LookPreset>>([.. Presets.Values.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)]);

        public Task SavePresetAsync(LookPreset preset, CancellationToken cancellationToken = default)
        {
            Presets[preset.Id] = preset;
            return Task.CompletedTask;
        }

        public Task DeletePresetAsync(string id, CancellationToken cancellationToken = default)
        {
            Presets.Remove(id);
            return Task.CompletedTask;
        }
    }

    private readonly StudioApi _api = new();
    private readonly StudioStore _store = new();
    private readonly FakeApiKeyStore _keys = new() { Key = TestData.Key };

    private async Task<FashionStudioViewModel> CreateAsync()
    {
        var vm = new FashionStudioViewModel(new CosmeticsCatalog(_api, _store), _api, _store, _keys);
        await vm.InitializeAsync();
        return vm;
    }

    private static SkinRow Row(FashionStudioViewModel vm, int id) => vm.VisibleSkins.Single(r => r.Id == id);

    private static SlotCard Card(FashionStudioViewModel vm, LookSlot slot) => vm.Cards.Single(c => c.Slot == slot);

    [Fact]
    public async Task The_studio_starts_with_an_empty_board_and_the_helms_that_fit_the_default_character()
    {
        var vm = await CreateAsync();

        Assert.Equal(Enum.GetValues<LookSlot>().Length, vm.Cards.Count);
        Assert.All(vm.Cards, c => Assert.False(c.HasSkin));
        Assert.Equal(["Heavy Helm"], vm.VisibleSkins.Select(r => r.Name));
        Assert.True(Card(vm, LookSlot.Helm).IsSelected);
    }

    [Fact]
    public async Task Showing_all_skins_includes_the_ones_that_do_not_fit()
    {
        var vm = await CreateAsync();

        vm.ShowAllSkins = true;

        Assert.Equal(["Charr Helm", "Heavy Helm", "Light Helm"], vm.VisibleSkins.Select(r => r.Name));
    }

    [Fact]
    public async Task Picking_a_skin_puts_it_on_the_selected_slot_and_says_so()
    {
        var vm = await CreateAsync();

        vm.ApplySkinCommand.Execute(Row(vm, 1));

        var card = Card(vm, LookSlot.Helm);
        Assert.Equal("Heavy Helm", card.SkinName);
        Assert.True(card.HasSkin);
        Assert.Equal(3, card.Dyes.Count);
        Assert.Equal("Heavy Helm", vm.Detail?.Name);
        Assert.Contains("Heavy Helm", vm.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Selecting_another_slot_changes_the_browser()
    {
        var vm = await CreateAsync();

        vm.SelectSlotCommand.Execute(LookSlot.Coat);

        Assert.Equal(["Heavy Coat"], vm.VisibleSkins.Select(r => r.Name));
        Assert.True(Card(vm, LookSlot.Coat).IsSelected);
        Assert.False(Card(vm, LookSlot.Helm).IsSelected);

        vm.SelectSlotCommand.Execute(LookSlot.Back);
        Assert.Equal(["Wings"], vm.VisibleSkins.Select(r => r.Name));
        vm.SelectSlotCommand.Execute(LookSlot.MainHand);
        Assert.Equal(["Sunrise"], vm.VisibleSkins.Select(r => r.Name));
    }

    [Fact]
    public async Task A_dye_needs_a_skin_with_colour_channels()
    {
        var vm = await CreateAsync();
        var sky = vm.VisibleColors.Single(c => c.Id == 10);

        vm.ApplyDyeCommand.Execute(sky);

        Assert.False(Card(vm, LookSlot.Helm).HasSkin);
        Assert.Equal(vm.ChannelHint, vm.StatusText);
        Assert.False(vm.HasChannels);

        vm.SelectSlotCommand.Execute(LookSlot.Back);
        vm.ShowAllSkins = true;
        vm.ApplySkinCommand.Execute(Row(vm, 6));
        vm.ApplyDyeCommand.Execute(sky);

        Assert.Equal("Deze skin heeft geen kleurvlakken.", vm.ChannelHint);
        Assert.Empty(Card(vm, LookSlot.Back).Dyes);
    }

    [Fact]
    public async Task A_dye_goes_on_the_selected_channel_and_shows_its_colour()
    {
        var vm = await CreateAsync();
        vm.ApplySkinCommand.Execute(Row(vm, 1));

        vm.SelectChannelCommand.Execute(2);
        vm.ApplyDyeCommand.Execute(vm.VisibleColors.Single(c => c.Id == 10));

        var dyes = Card(vm, LookSlot.Helm).Dyes;
        Assert.Equal(["Transparent", "Transparent", "#3682A0"], dyes.Select(d => d.Fill));
        Assert.True(dyes[2].IsSelected);
        Assert.False(dyes[0].IsSelected);
        Assert.Contains("Sky", dyes[2].Tooltip, StringComparison.Ordinal);
        Assert.Equal("Gekozen: kleurvlak 3", vm.SelectedChannelText);

        vm.ClearDyeCommand.Execute(null);
        Assert.Equal("Transparent", Card(vm, LookSlot.Helm).Dyes[2].Fill);
    }

    [Fact]
    public async Task Another_skin_keeps_the_colours()
    {
        var vm = await CreateAsync();
        vm.ShowAllSkins = true;
        vm.ApplySkinCommand.Execute(Row(vm, 1));
        vm.ApplyDyeCommand.Execute(vm.VisibleColors.Single(c => c.Id == 11));

        vm.ApplySkinCommand.Execute(Row(vm, 2));

        Assert.Equal("Light Helm", Card(vm, LookSlot.Helm).SkinName);
        Assert.Equal("#C83C5A", Card(vm, LookSlot.Helm).Dyes[0].Fill);
    }

    [Fact]
    public async Task A_skin_that_does_not_fit_the_character_gets_a_warning()
    {
        var vm = await CreateAsync();
        vm.ApplySkinCommand.Execute(Row(vm, 1));
        Assert.False(Card(vm, LookSlot.Helm).HasWarning);

        vm.SelectedProfession = "Mesmer";

        Assert.True(Card(vm, LookSlot.Helm).HasWarning);
        Assert.Contains("Mesmer", Card(vm, LookSlot.Helm).Warning, StringComparison.Ordinal);
        Assert.Equal(["Light Helm"], vm.VisibleSkins.Select(r => r.Name));
    }

    [Fact]
    public async Task A_racial_skin_fits_only_its_race()
    {
        var vm = await CreateAsync();
        vm.SelectedRace = "Charr";

        Assert.Equal(["Charr Helm", "Heavy Helm"], vm.VisibleSkins.Select(r => r.Name));
    }

    [Fact]
    public async Task Clearing_a_slot_and_resetting_the_look_keep_the_character()
    {
        var vm = await CreateAsync();
        vm.SelectedRace = "Asura";
        vm.ApplySkinCommand.Execute(Row(vm, 1));
        vm.SelectSlotCommand.Execute(LookSlot.Coat);
        vm.ApplySkinCommand.Execute(Row(vm, 4));

        vm.ClearSlotCommand.Execute(LookSlot.Coat);
        Assert.False(Card(vm, LookSlot.Coat).HasSkin);
        Assert.True(Card(vm, LookSlot.Helm).HasSkin);

        vm.ResetLookCommand.Execute(null);
        Assert.All(vm.Cards, c => Assert.False(c.HasSkin));
        Assert.Equal("Asura", vm.SelectedRace);
    }

    [Fact]
    public async Task Unlocks_mark_what_the_account_has_and_can_filter_the_list()
    {
        var vm = await CreateAsync();
        vm.ShowAllSkins = true;

        Assert.Equal("Ontgrendeld", Row(vm, 1).OwnedText);
        Assert.Equal("Nog niet ontgrendeld", Row(vm, 2).OwnedText);
        Assert.False(vm.HasOwnershipText);

        vm.OwnedOnly = true;
        Assert.Equal(["Heavy Helm"], vm.VisibleSkins.Select(r => r.Name));

        vm.ColorOwnedOnly = true;
        Assert.Equal(["Sky"], vm.VisibleColors.Select(c => c.Name));
    }

    [Fact]
    public async Task Without_the_unlocks_permission_everything_is_shown_and_the_user_is_told_why()
    {
        _api.UnlocksFailure = new Gw2ApiException(Gw2ApiErrorKind.MissingPermissions, "scope");

        var vm = await CreateAsync();
        vm.ShowAllSkins = true;
        vm.OwnedOnly = true;

        Assert.True(vm.HasOwnershipText);
        Assert.Contains("unlocks", vm.OwnershipText, StringComparison.Ordinal);
        Assert.Equal(3, vm.VisibleSkins.Count);
        Assert.All(vm.VisibleSkins, r => Assert.Equal(string.Empty, r.OwnedText));
    }

    [Fact]
    public async Task Without_an_account_the_studio_still_works_and_asks_to_connect()
    {
        _keys.Key = null;

        var vm = await CreateAsync();

        Assert.Contains("Verbind", vm.OwnershipText, StringComparison.Ordinal);
        Assert.NotEmpty(vm.VisibleSkins);
    }

    [Fact]
    public async Task Other_api_errors_during_unlocks_are_shown_and_do_not_escape()
    {
        _api.UnlocksFailure = new Gw2ApiException(Gw2ApiErrorKind.Offline, "Geen verbinding.");

        var vm = await CreateAsync();

        Assert.Equal("Geen verbinding.", vm.OwnershipText);
    }

    [Fact]
    public async Task A_look_needs_a_name_before_it_can_be_saved()
    {
        var vm = await CreateAsync();

        await vm.SavePresetAsync();

        Assert.Empty(_store.Presets);
        Assert.Contains("naam", vm.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Saving_under_the_same_name_updates_instead_of_adding_a_copy()
    {
        var vm = await CreateAsync();
        vm.ApplySkinCommand.Execute(Row(vm, 1));
        vm.PresetName = "  Gala ";
        await vm.SavePresetAsync();
        vm.SelectSlotCommand.Execute(LookSlot.Coat);
        vm.ApplySkinCommand.Execute(Row(vm, 4));
        vm.PresetName = "gala";

        await vm.SavePresetAsync();

        var preset = Assert.Single(_store.Presets.Values);
        Assert.Equal("gala", preset.Name);
        Assert.Equal(4, preset.Look.Get(LookSlot.Coat).SkinId);
        Assert.Single(vm.Presets);
        Assert.True(vm.HasPresets);
    }

    [Fact]
    public async Task Loading_a_preset_restores_the_character_and_the_slots()
    {
        var vm = await CreateAsync();
        vm.SelectedRace = "Norn";
        vm.SelectedProfession = "Mesmer";
        vm.SelectSlotCommand.Execute(LookSlot.Helm);
        vm.ApplySkinCommand.Execute(Row(vm, 2));
        vm.PresetName = "Illusie";
        await vm.SavePresetAsync();

        vm.SelectedRace = "Human";
        vm.SelectedProfession = "Guardian";
        vm.ResetLookCommand.Execute(null);
        vm.LoadPresetCommand.Execute(vm.Presets[0]);

        Assert.Equal(("Norn", "Mesmer"), (vm.SelectedRace, vm.SelectedProfession));
        Assert.Equal("Light Helm", Card(vm, LookSlot.Helm).SkinName);
    }

    [Fact]
    public async Task A_preset_can_be_deleted()
    {
        var vm = await CreateAsync();
        vm.PresetName = "Tijdelijk";
        await vm.SavePresetAsync();

        await vm.DeletePresetAsync(vm.Presets[0]);

        Assert.Empty(_store.Presets);
        Assert.False(vm.HasPresets);
    }

    [Fact]
    public async Task The_look_of_a_character_replaces_the_board_and_the_character_settings()
    {
        var vm = await CreateAsync();
        await vm.LoadCharactersAsync();
        Assert.Equal(["Mijn Held", "Tweede"], vm.CharacterNames);
        Assert.Equal("Mijn Held", vm.SelectedCharacter);

        await vm.ImportCharacterAsync();

        Assert.Equal(("Norn", "Male", "Mesmer"), (vm.SelectedRace, vm.SelectedGender, vm.SelectedProfession));
        Assert.Equal("Light Helm", Card(vm, LookSlot.Helm).SkinName);
    }

    [Fact]
    public async Task Importing_without_a_chosen_character_explains_what_to_do()
    {
        var vm = await CreateAsync();

        await vm.ImportCharacterAsync();

        Assert.Contains("karakters", vm.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_skin_that_is_not_in_the_list_is_shown_as_unknown_instead_of_hidden()
    {
        _api.CharacterLook = CharacterLook.Default.WithSkin(LookSlot.Boots, 99999);
        var vm = await CreateAsync();
        await vm.LoadCharactersAsync();

        await vm.ImportCharacterAsync();

        Assert.Equal("Onbekende skin (#99999)", Card(vm, LookSlot.Boots).SkinName);

        vm.SelectSlotCommand.Execute(LookSlot.Boots);
        Assert.Equal("Onbekende skin (#99999)", vm.Detail?.Name);
    }

    [Fact]
    public async Task The_colour_list_can_be_filtered_by_group_and_name()
    {
        var vm = await CreateAsync();
        Assert.Equal([FashionStudioViewModel.AllHues, "Blue", "Red"], vm.Hues);

        vm.SelectedHue = "Red";
        Assert.Equal(["Rose"], vm.VisibleColors.Select(c => c.Name));

        vm.SelectedHue = FashionStudioViewModel.AllHues;
        vm.ColorSearchText = "sk";
        Assert.Equal(["Sky"], vm.VisibleColors.Select(c => c.Name));
    }

    [Fact]
    public async Task Typing_in_the_search_box_narrows_the_skins_and_reports_the_count()
    {
        var vm = await CreateAsync();
        vm.ShowAllSkins = true;

        vm.SearchText = "light";

        Assert.Equal(["Light Helm"], vm.VisibleSkins.Select(r => r.Name));
        Assert.Contains("1", vm.SkinCountText, StringComparison.Ordinal);

        vm.SearchText = "zzz";
        Assert.True(vm.HasNoSkins);
    }

    [Fact]
    public async Task Describing_a_skin_names_its_weight_rarity_races_and_colour_channels()
    {
        var vm = await CreateAsync();
        vm.ShowAllSkins = true;

        Assert.Equal("Heavy · Rare · alleen Charr · 3 kleurvlakken", Row(vm, 3).Detail);
    }

    [Fact]
    public async Task Without_a_cache_and_without_internet_the_studio_says_so()
    {
        var vm = new FashionStudioViewModel(
            new CosmeticsCatalog(new OfflineApi(), _store),
            _api,
            _store,
            _keys);

        await vm.InitializeAsync();

        Assert.Contains("Geen verbinding", vm.StatusText, StringComparison.Ordinal);
        Assert.Empty(vm.VisibleSkins);
    }

    private sealed class OfflineApi : ICosmeticsApiClient
    {
        private static readonly Gw2ApiException Offline = new(Gw2ApiErrorKind.Offline, "offline");

        public Task<IReadOnlyList<CosmeticSkin>> GetAllSkinsAsync(IProgress<int>? progress = null, CancellationToken cancellationToken = default) => Task.FromException<IReadOnlyList<CosmeticSkin>>(Offline);

        public Task<IReadOnlyList<DyeColor>> GetAllDyeColorsAsync(CancellationToken cancellationToken = default) => Task.FromException<IReadOnlyList<DyeColor>>(Offline);

        public Task<IReadOnlySet<int>> GetUnlockedSkinIdsAsync(string apiKey, CancellationToken cancellationToken = default) => Task.FromException<IReadOnlySet<int>>(Offline);

        public Task<IReadOnlySet<int>> GetUnlockedDyeIdsAsync(string apiKey, CancellationToken cancellationToken = default) => Task.FromException<IReadOnlySet<int>>(Offline);

        public Task<IReadOnlyList<string>> GetCharacterNamesAsync(string apiKey, CancellationToken cancellationToken = default) => Task.FromException<IReadOnlyList<string>>(Offline);

        public Task<CharacterLook> GetCharacterLookAsync(string apiKey, string characterName, CancellationToken cancellationToken = default) => Task.FromException<CharacterLook>(Offline);
    }
}
