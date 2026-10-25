using System.Globalization;
using System.Windows.Input;
using Gw2LegendaryTracker.Core.Cosmetics;
using Gw2LegendaryTracker.Core.Gw2;
using Gw2LegendaryTracker.Core.Sync;

namespace Gw2LegendaryTracker.App.ViewModels;

/// <summary>One skin in the browser on the left.</summary>
public sealed record SkinRow(int Id, string Name, string? IconUrl, string Rarity, string Detail, string OwnedText);

/// <summary>One dye channel of a slot. <see cref="Fill"/> is a colour string such as "#3682A0", or "Transparent" for none.</summary>
public sealed record DyeSwatch(int Channel, string Fill, string Tooltip, bool IsSelected)
{
    public int DisplayNumber => Channel + 1;
}

/// <summary>One slot of the look board in the middle.</summary>
public sealed record SlotCard(
    LookSlot Slot,
    string Label,
    string SkinName,
    string? IconUrl,
    bool HasSkin,
    bool IsSelected,
    string? Warning,
    IReadOnlyList<DyeSwatch> Dyes)
{
    public bool HasWarning => Warning is not null;
}

/// <summary>The skin shown large in the middle: what the player is looking at.</summary>
public sealed record SkinDetail(string Name, string? IconUrl, string Facts, string? Description);

public sealed record ColorRow(int Id, string Name, string Hex, string Tooltip);

public sealed record PresetRow(string Id, string Name, string UpdatedText, CharacterLook Look);

/// <summary>
/// The Fashion Studio: browse all cosmetics, put them on a configured character and keep looks as presets.
/// It is a 2D preview: the official API has icons and dye colours, no 3D data.
/// </summary>
public sealed class FashionStudioViewModel : ObservableObject
{
    public const string AllHues = "Alle kleuren";

    private const int MaxPresetNameLength = 60;

    private readonly CosmeticsCatalog _catalog;
    private readonly ICosmeticsApiClient _api;
    private readonly ICosmeticsStore _store;
    private readonly IApiKeyStore _keys;
    private readonly TimeProvider _time;

    private Dictionary<int, CosmeticSkin> _skinsById = [];
    private Dictionary<int, DyeColor> _colorsById = [];
    private IReadOnlySet<int>? _ownedSkins;
    private IReadOnlySet<int>? _ownedDyes;
    private CharacterLook _look = CharacterLook.Default;
    private LookSlot _selectedSlot = LookSlot.Helm;
    private int _selectedChannel;
    private string _searchText = string.Empty;
    private bool _ownedOnly;
    private bool _showAllSkins;
    private string _colorSearchText = string.Empty;
    private bool _colorOwnedOnly;
    private string _selectedHue = AllHues;
    private string _statusText = "Skins laden…";
    private string _ownershipText = string.Empty;
    private string _presetName = string.Empty;
    private string? _selectedCharacter;
    private IReadOnlyList<SkinRow> _visibleSkins = [];
    private IReadOnlyList<ColorRow> _visibleColors = [];
    private IReadOnlyList<SlotCard> _cards = [];
    private IReadOnlyList<PresetRow> _presets = [];
    private IReadOnlyList<string> _hues = [AllHues];
    private IReadOnlyList<string> _characterNames = [];
    private bool _initialized;

    public FashionStudioViewModel(CosmeticsCatalog catalog, ICosmeticsApiClient api, ICosmeticsStore store, IApiKeyStore keys, TimeProvider? time = null)
    {
        _catalog = catalog;
        _api = api;
        _store = store;
        _keys = keys;
        _time = time ?? TimeProvider.System;

        SelectSlotCommand = new RelayCommand<LookSlot>(SelectSlot);
        ClearSlotCommand = new RelayCommand<LookSlot>(ClearSlot);
        ClearSelectedSlotCommand = new RelayCommand(() => ClearSlot(_selectedSlot));
        ApplySkinCommand = new RelayCommand<SkinRow>(ApplySkin);
        SelectChannelCommand = new RelayCommand<int>(SelectChannel);
        ApplyDyeCommand = new RelayCommand<ColorRow>(ApplyDye);
        ClearDyeCommand = new RelayCommand(ClearDye);
        ResetLookCommand = new RelayCommand(ResetLook);
        SavePresetCommand = new RelayCommand(() => _ = SavePresetAsync());
        LoadPresetCommand = new RelayCommand<PresetRow>(LoadPreset);
        DeletePresetCommand = new RelayCommand<PresetRow>(row => _ = DeletePresetAsync(row));
        RefreshCatalogCommand = new RelayCommand(() => _ = RefreshCatalogAsync(force: true));
        LoadUnlocksCommand = new RelayCommand(() => _ = LoadUnlocksAsync());
        LoadCharactersCommand = new RelayCommand(() => _ = LoadCharactersAsync());
        ImportCharacterCommand = new RelayCommand(() => _ = ImportCharacterAsync());
    }

    public ICommand SelectSlotCommand { get; }

    public ICommand ClearSlotCommand { get; }

    public ICommand ClearSelectedSlotCommand { get; }

    public ICommand ApplySkinCommand { get; }

    public ICommand SelectChannelCommand { get; }

    public ICommand ApplyDyeCommand { get; }

    public ICommand ClearDyeCommand { get; }

    public ICommand ResetLookCommand { get; }

    public ICommand SavePresetCommand { get; }

    public ICommand LoadPresetCommand { get; }

    public ICommand DeletePresetCommand { get; }

    public ICommand RefreshCatalogCommand { get; }

    public ICommand LoadUnlocksCommand { get; }

    public ICommand LoadCharactersCommand { get; }

    public ICommand ImportCharacterCommand { get; }

    // Instance properties on purpose: XAML binds to the view model instance, not to static members.
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Bound from XAML.")]
    public IReadOnlyList<string> Races => CharacterOptions.Races;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Bound from XAML.")]
    public IReadOnlyList<string> Genders => CharacterOptions.Genders;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Bound from XAML.")]
    public IReadOnlyList<string> Professions => CharacterOptions.Professions;

    public IReadOnlyList<SkinRow> VisibleSkins
    {
        get => _visibleSkins;
        private set => Set(ref _visibleSkins, value, nameof(VisibleSkins), nameof(SkinCountText), nameof(HasNoSkins));
    }

    public IReadOnlyList<ColorRow> VisibleColors
    {
        get => _visibleColors;
        private set => Set(ref _visibleColors, value, nameof(VisibleColors));
    }

    public IReadOnlyList<SlotCard> Cards
    {
        get => _cards;
        private set => Set(ref _cards, value, nameof(Cards), nameof(SelectedCard), nameof(Detail), nameof(HasDetail), nameof(HasNoDetail), nameof(HasChannels), nameof(ChannelHint));
    }

    public IReadOnlyList<PresetRow> Presets
    {
        get => _presets;
        private set => Set(ref _presets, value, nameof(Presets), nameof(HasPresets));
    }

    public IReadOnlyList<string> Hues
    {
        get => _hues;
        private set => Set(ref _hues, value, nameof(Hues));
    }

    public IReadOnlyList<string> CharacterNames
    {
        get => _characterNames;
        private set => Set(ref _characterNames, value, nameof(CharacterNames), nameof(HasCharacters));
    }

    public SlotCard? SelectedCard => _cards.FirstOrDefault(c => c.Slot == _selectedSlot);

    public SkinDetail? Detail
    {
        get
        {
            if (_look.Get(_selectedSlot).SkinId is not { } id)
            {
                return null;
            }

            return _skinsById.TryGetValue(id, out var skin)
                ? new SkinDetail(skin.Name, skin.IconUrl, Describe(skin), skin.Description)
                : new SkinDetail($"Onbekende skin (#{id})", null, "Deze skin staat niet in de lijst. Ververs de skins als het spel is bijgewerkt.", null);
        }
    }

    public bool HasDetail => Detail is not null;

    public bool HasNoDetail => Detail is null;

    public bool HasChannels => SelectedCard is { Dyes.Count: > 0 };

    public string ChannelHint => SelectedCard switch
    {
        null or { HasSkin: false } => "Kies eerst een skin voor dit onderdeel om het te kleuren.",
        { Dyes.Count: 0 } => "Deze skin heeft geen kleurvlakken.",
        _ => "Kies een kleurvlak en daarna een kleur.",
    };

    public int SelectedChannel => _selectedChannel;

    public string SelectedChannelText => string.Create(CultureInfo.CurrentCulture, $"Gekozen: kleurvlak {_selectedChannel + 1}");

    public string SearchText
    {
        get => _searchText;
        set
        {
            _searchText = value ?? string.Empty;
            RaisePropertyChanged(nameof(SearchText));
            RebuildSkins();
        }
    }

    public bool OwnedOnly
    {
        get => _ownedOnly;
        set
        {
            _ownedOnly = value;
            RaisePropertyChanged(nameof(OwnedOnly));
            RebuildSkins();
        }
    }

    public bool ShowAllSkins
    {
        get => _showAllSkins;
        set
        {
            _showAllSkins = value;
            RaisePropertyChanged(nameof(ShowAllSkins));
            RebuildSkins();
        }
    }

    public string ColorSearchText
    {
        get => _colorSearchText;
        set
        {
            _colorSearchText = value ?? string.Empty;
            RaisePropertyChanged(nameof(ColorSearchText));
            RebuildColors();
        }
    }

    public bool ColorOwnedOnly
    {
        get => _colorOwnedOnly;
        set
        {
            _colorOwnedOnly = value;
            RaisePropertyChanged(nameof(ColorOwnedOnly));
            RebuildColors();
        }
    }

    public string SelectedHue
    {
        get => _selectedHue;
        set
        {
            _selectedHue = string.IsNullOrEmpty(value) ? AllHues : value;
            RaisePropertyChanged(nameof(SelectedHue));
            RebuildColors();
        }
    }

    public string SelectedRace
    {
        get => _look.Race;
        set => ChangeCharacter(value, _look.Gender, _look.Profession);
    }

    public string SelectedGender
    {
        get => _look.Gender;
        set => ChangeCharacter(_look.Race, value, _look.Profession);
    }

    public string SelectedProfession
    {
        get => _look.Profession;
        set => ChangeCharacter(_look.Race, _look.Gender, value);
    }

    public string PresetName
    {
        get => _presetName;
        set
        {
            _presetName = value ?? string.Empty;
            RaisePropertyChanged(nameof(PresetName));
        }
    }

    public string? SelectedCharacter
    {
        get => _selectedCharacter;
        set
        {
            _selectedCharacter = value;
            RaisePropertyChanged(nameof(SelectedCharacter));
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value, nameof(StatusText));
    }

    public string OwnershipText
    {
        get => _ownershipText;
        private set => Set(ref _ownershipText, value, nameof(OwnershipText), nameof(HasOwnershipText));
    }

    public bool HasOwnershipText => _ownershipText.Length > 0;

    public bool HasPresets => _presets.Count > 0;

    public bool HasCharacters => _characterNames.Count > 0;

    public bool HasNoSkins => _visibleSkins.Count == 0;

    public string SkinCountText => string.Create(CultureInfo.CurrentCulture, $"{_visibleSkins.Count:N0} skins");

    public string SlotTitle => SkinSlots.Label(_selectedSlot);

    /// <summary>Loads the cache, shows it at once, then refreshes in the background when it is old. Safe to call more than once.</summary>
    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        try
        {
            await _catalog.LoadCacheAsync();
            RebuildAll();
            await LoadPresetsAsync();
            await RefreshCatalogAsync(force: false);
            await LoadUnlocksAsync(silent: true);
        }
        catch (Exception ex)
        {
            // A broken cache must not close the studio: say what happened and let the user retry.
            StatusText = $"De lijst met skins kon niet worden geladen: {ex.Message}";
        }
    }

    internal static string Describe(CosmeticSkin skin)
    {
        var parts = new List<string>();
        switch (skin.Category)
        {
            case SkinCategory.Armor:
                parts.Add(string.IsNullOrEmpty(skin.WeightClass) ? "Pantser" : skin.WeightClass);
                break;
            case SkinCategory.Weapon:
                parts.Add(skin.Slot);
                break;
            case SkinCategory.Back:
                parts.Add("Rugstuk");
                break;
            case SkinCategory.Gathering:
                parts.Add(skin.Slot);
                break;
        }

        parts.Add(skin.Rarity);
        if (skin.Races.Count > 0)
        {
            parts.Add("alleen " + string.Join(", ", skin.Races));
        }

        if (skin.DyeSlotCount > 0)
        {
            parts.Add($"{skin.DyeSlotCount} kleurvlakken");
        }

        return string.Join(" · ", parts);
    }

    private void SelectSlot(LookSlot slot)
    {
        _selectedSlot = slot;
        _selectedChannel = 0;
        RaisePropertyChanged(nameof(SlotTitle));
        RaisePropertyChanged(nameof(SelectedChannel));
        RaisePropertyChanged(nameof(SelectedChannelText));
        RebuildCards();
        RebuildSkins();
    }

    private void ClearSlot(LookSlot slot)
    {
        SetLook(_look.ClearSlot(slot));
        StatusText = $"{SkinSlots.Label(slot)} is leeggemaakt.";
    }

    private void ApplySkin(SkinRow row)
    {
        SetLook(_look.WithSkin(_selectedSlot, row.Id));
        StatusText = $"{row.Name} staat nu op {SkinSlots.Label(_selectedSlot).ToLower(CultureInfo.CurrentCulture)}.";
    }

    private void SelectChannel(int channel)
    {
        _selectedChannel = channel;
        RaisePropertyChanged(nameof(SelectedChannel));
        RaisePropertyChanged(nameof(SelectedChannelText));
        RebuildCards();
    }

    private void ApplyDye(ColorRow row)
    {
        if (SelectedCard is not { HasSkin: true, Dyes.Count: > 0 } card || _selectedChannel >= card.Dyes.Count)
        {
            StatusText = ChannelHint;
            return;
        }

        SetLook(_look.WithDye(_selectedSlot, _selectedChannel, row.Id));
        StatusText = $"{row.Name} op kleurvlak {_selectedChannel + 1} van {SkinSlots.Label(_selectedSlot).ToLower(CultureInfo.CurrentCulture)}.";
    }

    private void ClearDye()
    {
        if (!HasChannels)
        {
            return;
        }

        SetLook(_look.WithDye(_selectedSlot, _selectedChannel, null));
        StatusText = $"Kleurvlak {_selectedChannel + 1} is leeggemaakt.";
    }

    private void ResetLook()
    {
        SetLook(new CharacterLook(_look.Race, _look.Gender, _look.Profession));
        StatusText = "Alle onderdelen zijn leeggemaakt.";
    }

    private void ChangeCharacter(string race, string gender, string profession)
    {
        if (race == _look.Race && gender == _look.Gender && profession == _look.Profession)
        {
            return;
        }

        SetLook(_look.WithCharacter(race, gender, profession));
        RaisePropertyChanged(nameof(SelectedRace));
        RaisePropertyChanged(nameof(SelectedGender));
        RaisePropertyChanged(nameof(SelectedProfession));
    }

    private void SetLook(CharacterLook look)
    {
        _look = look;
        RebuildCards();
        RebuildSkins();
    }

    private void RebuildAll()
    {
        _skinsById = _catalog.Skins.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First());
        _colorsById = _catalog.Colors.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First());
        Hues = [AllHues, .. _catalog.Colors.Select(c => c.Hue).OfType<string>().Distinct().Order()];
        if (!Hues.Contains(_selectedHue))
        {
            _selectedHue = AllHues;
            RaisePropertyChanged(nameof(SelectedHue));
        }

        RebuildCards();
        RebuildSkins();
        RebuildColors();
        StatusText = _catalog.Skins.Count == 0
            ? "Er zijn nog geen skins. Verbind met internet en kies 'Skins verversen'."
            : $"{_catalog.Skins.Count:N0} skins en {_catalog.Colors.Count:N0} kleuren beschikbaar.";
    }

    private void RebuildCards()
    {
        var weight = CharacterOptions.WeightClassOf(_look.Profession);
        Cards = [.. Enum.GetValues<LookSlot>().Select(slot => BuildCard(slot, weight))];
    }

    private SlotCard BuildCard(LookSlot slot, string? weight)
    {
        var look = _look.Get(slot);
        CosmeticSkin? skin = null;
        if (look.SkinId is { } id)
        {
            _skinsById.TryGetValue(id, out skin);
        }

        var name = look.SkinId is null ? "Leeg" : skin?.Name ?? $"Onbekende skin (#{look.SkinId})";
        var warning = skin is not null && !SkinSearch.FitsCharacter(skin, _look.Race, weight)
            ? $"Past niet bij {_look.Race} of {_look.Profession}."
            : null;

        var isSelected = slot == _selectedSlot;
        var channels = skin is null ? 0 : Math.Min(skin.DyeSlotCount, SlotLook.DyeChannels);
        var dyes = new List<DyeSwatch>(channels);
        for (var channel = 0; channel < channels; channel++)
        {
            var color = look.GetDye(channel) is { } colorId && _colorsById.TryGetValue(colorId, out var found) ? found : null;
            dyes.Add(new DyeSwatch(
                channel,
                color?.Hex ?? "Transparent",
                color is null ? $"Kleurvlak {channel + 1}: geen kleur" : $"Kleurvlak {channel + 1}: {color.Name}",
                isSelected && channel == _selectedChannel));
        }

        return new SlotCard(slot, SkinSlots.Label(slot), name, skin?.IconUrl, look.SkinId is not null, isSelected, warning, dyes);
    }

    private void RebuildSkins()
    {
        var weight = _showAllSkins ? null : CharacterOptions.WeightClassOf(_look.Profession);
        var query = new SkinQuery(
            Text: _searchText,
            WeightClass: weight,
            Race: _showAllSkins ? null : _look.Race,
            OwnedOnly: _ownedOnly,
            ForSlot: _selectedSlot);

        VisibleSkins = [.. SkinSearch.Apply(_catalog.Skins, query, _ownedSkins).Select(ToRow)];
    }

    private SkinRow ToRow(CosmeticSkin skin) => new(
        skin.Id,
        skin.Name,
        skin.IconUrl,
        skin.Rarity,
        Describe(skin),
        _ownedSkins is null ? string.Empty : _ownedSkins.Contains(skin.Id) ? "Ontgrendeld" : "Nog niet ontgrendeld");

    private void RebuildColors()
    {
        var text = _colorSearchText.Trim();
        VisibleColors = [.. _catalog.Colors
            .Where(c => _selectedHue == AllHues || c.Hue == _selectedHue)
            .Where(c => !_colorOwnedOnly || _ownedDyes is null || _ownedDyes.Contains(c.Id))
            .Where(c => text.Length == 0 || c.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase))
            .Select(c => new ColorRow(c.Id, c.Name, c.Hex, DescribeColor(c)))];
    }

    private string DescribeColor(DyeColor color)
    {
        var owned = _ownedDyes is null ? string.Empty : _ownedDyes.Contains(color.Id) ? " (ontgrendeld)" : " (nog niet ontgrendeld)";
        return color.Name + owned;
    }

    internal async Task RefreshCatalogAsync(bool force)
    {
        StatusText = force ? "Skins verversen…" : StatusText;
        var progress = new Progress<int>(count => StatusText = string.Create(CultureInfo.CurrentCulture, $"Skins ophalen… {count:N0} van de lijst geladen"));
        try
        {
            var result = await _catalog.RefreshAsync(force, progress);
            RebuildAll();
            if (result == CatalogRefreshResult.Offline)
            {
                StatusText = _catalog.Skins.Count == 0
                    ? "Geen verbinding met Guild Wars 2 en nog geen opgeslagen skins."
                    : $"Geen verbinding met Guild Wars 2: de opgeslagen lijst van {_catalog.Skins.Count:N0} skins wordt gebruikt.";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Skins verversen mislukt: {ex.Message}";
        }
    }

    internal async Task LoadUnlocksAsync(bool silent = false)
    {
        try
        {
            var key = await _keys.LoadAsync();
            if (key is null)
            {
                OwnershipText = "Verbind je account in het hoofdvenster om te zien wat je al hebt.";
                return;
            }

            _ownedSkins = await _api.GetUnlockedSkinIdsAsync(key);
            _ownedDyes = await _api.GetUnlockedDyeIdsAsync(key);
            OwnershipText = string.Empty;
            RebuildSkins();
            RebuildColors();
            if (!silent)
            {
                StatusText = $"Je hebt {_ownedSkins.Count:N0} skins en {_ownedDyes.Count:N0} kleuren ontgrendeld.";
            }
        }
        catch (Gw2ApiException ex) when (ex.Kind == Gw2ApiErrorKind.MissingPermissions)
        {
            _ownedSkins = null;
            _ownedDyes = null;
            OwnershipText = "Om te zien welke skins en kleuren je hebt, mist je API-key het recht 'unlocks'. Je kunt alles gewoon bekijken; maak een nieuwe key met dat recht om de markering te zien.";
        }
        catch (Gw2ApiException ex)
        {
            OwnershipText = ex.Message;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Nothing here may escape: this runs without anyone awaiting it.
            OwnershipText = $"Ontgrendelingen konden niet worden geladen: {ex.Message}";
        }
    }

    internal async Task LoadCharactersAsync()
    {
        try
        {
            var key = await _keys.LoadAsync();
            if (key is null)
            {
                StatusText = "Verbind je account in het hoofdvenster om je karakters te laden.";
                return;
            }

            CharacterNames = await _api.GetCharacterNamesAsync(key);
            SelectedCharacter ??= CharacterNames.Count > 0 ? CharacterNames[0] : null;
            StatusText = CharacterNames.Count == 0 ? "Er zijn geen karakters gevonden." : $"{CharacterNames.Count} karakters geladen.";
        }
        catch (Gw2ApiException ex)
        {
            StatusText = ex.Message;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            StatusText = $"Karakters konden niet worden geladen: {ex.Message}";
        }
    }

    internal async Task ImportCharacterAsync()
    {
        if (SelectedCharacter is not { } name)
        {
            StatusText = "Laad eerst je karakters en kies er één.";
            return;
        }

        try
        {
            var key = await _keys.LoadAsync();
            if (key is null)
            {
                StatusText = "Verbind je account in het hoofdvenster om je karakters te laden.";
                return;
            }

            var look = await _api.GetCharacterLookAsync(key, name);
            SetLook(look);
            RaisePropertyChanged(nameof(SelectedRace));
            RaisePropertyChanged(nameof(SelectedGender));
            RaisePropertyChanged(nameof(SelectedProfession));
            StatusText = $"De look van {name} is geladen. Onderdelen met de standaardskin van hun item zijn leeg gelaten, want de API noemt die niet.";
        }
        catch (Gw2ApiException ex)
        {
            StatusText = ex.Message;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            StatusText = $"De look kon niet worden geladen: {ex.Message}";
        }
    }

    private async Task LoadPresetsAsync()
    {
        var presets = await _store.LoadPresetsAsync();
        Presets = [.. presets.Select(p => new PresetRow(p.Id, p.Name, p.UpdatedUtc.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture), p.Look))];
    }

    internal async Task SavePresetAsync()
    {
        var name = _presetName.Trim();
        if (name.Length == 0)
        {
            StatusText = "Geef je look een naam om hem op te slaan.";
            return;
        }

        if (name.Length > MaxPresetNameLength)
        {
            name = name[..MaxPresetNameLength];
        }

        // The same name replaces the old preset: players expect "save" to update, not to pile up copies.
        var existing = _presets.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        try
        {
            await _store.SavePresetAsync(new LookPreset(existing?.Id ?? Guid.NewGuid().ToString("N"), name, _look, _time.GetUtcNow()));
            await LoadPresetsAsync();
            StatusText = existing is null ? $"Look '{name}' is opgeslagen." : $"Look '{name}' is bijgewerkt.";
        }
        catch (Exception ex)
        {
            StatusText = $"Opslaan mislukt: {ex.Message}";
        }
    }

    private void LoadPreset(PresetRow row)
    {
        SetLook(row.Look);
        RaisePropertyChanged(nameof(SelectedRace));
        RaisePropertyChanged(nameof(SelectedGender));
        RaisePropertyChanged(nameof(SelectedProfession));
        PresetName = row.Name;
        StatusText = $"Look '{row.Name}' is geladen.";
    }

    internal async Task DeletePresetAsync(PresetRow row)
    {
        try
        {
            await _store.DeletePresetAsync(row.Id);
            await LoadPresetsAsync();
            StatusText = $"Look '{row.Name}' is verwijderd.";
        }
        catch (Exception ex)
        {
            StatusText = $"Verwijderen mislukt: {ex.Message}";
        }
    }

    private void Set<T>(ref T field, T value, string name, params string[] alsoRaise)
    {
        field = value;
        RaisePropertyChanged(name);
        foreach (var other in alsoRaise)
        {
            RaisePropertyChanged(other);
        }
    }
}
