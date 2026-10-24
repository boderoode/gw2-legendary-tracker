using System.Globalization;
using System.Windows.Input;
using System.Windows.Threading;
using Gw2LegendaryTracker.Core.Crafting;
using Gw2LegendaryTracker.Core.Goals;
using Gw2LegendaryTracker.Core.Gw2;
using Gw2LegendaryTracker.Core.Inventory;
using Gw2LegendaryTracker.Core.Items;
using Gw2LegendaryTracker.Core.Links;
using Gw2LegendaryTracker.Core.Settings;
using Gw2LegendaryTracker.Core.State;
using Gw2LegendaryTracker.Core.Trading;

namespace Gw2LegendaryTracker.App.ViewModels;

public sealed record LegendaryOption(int ItemId, string Name, string? IconUrl);

public sealed record GoalRow(int ItemId, string Name, string? IconUrl, double Progress, string ProgressText);

public sealed record ShortageRow(string Name, string? IconUrl, long Missing, long Needed, long Owned, int ItemId = 0)
{
    public string WikiUrl => ItemLinks.Wiki(Name);

    public string TreasuresUrl => ItemId > 0 ? ItemLinks.Treasures(ItemId) : string.Empty;

    /// <summary>Main window: how much is still needed out of the total.</summary>
    public string Text => string.Create(CultureInfo.CurrentCulture, $"nog {Missing:N0} van {Needed:N0}");

    /// <summary>Overlay: how much is covered out of what is needed, e.g. <c>120 / 250</c>.</summary>
    public string CoverText => string.Create(CultureInfo.CurrentCulture, $"{Math.Max(0, Needed - Missing):N0} / {Needed:N0}");

    /// <summary>Share covered, for the mini bar.</summary>
    public double Ratio => Coverage.Ratio(Math.Max(0, Needed - Missing), Needed);

    public bool IsMissing => Missing > 0;

    /// <summary>Overlay: what is in stock and what is left to get.</summary>
    public string OverlayText => string.Create(CultureInfo.CurrentCulture, $"heb {Owned:N0} · nog {Missing:N0}");
}

/// <summary>One component of the selected goal that can be ticked off by hand.</summary>
public sealed class ChecklistRow : ObservableObject
{
    private readonly Action<int, bool> _onChanged;
    private bool _isDone;

    public ChecklistRow(int itemId, string name, string? iconUrl, string statusText, bool isDone, Action<int, bool> onChanged)
    {
        ItemId = itemId;
        Name = name;
        IconUrl = iconUrl;
        StatusText = statusText;
        _isDone = isDone;
        _onChanged = onChanged;
    }

    public int ItemId { get; }

    public string Name { get; }

    public string? IconUrl { get; }

    public string StatusText { get; }

    public bool IsDone
    {
        get => _isDone;
        set
        {
            if (_isDone != value)
            {
                _isDone = value;
                RaisePropertyChanged(nameof(IsDone));
                _onChanged(ItemId, value);
            }
        }
    }
}

/// <summary>
/// Everything about the user's Legendary goals, shared by the main window and the overlay:
/// the list, the selected goal's progress, next step, shortages and checklist.
/// </summary>
public sealed class GoalsViewModel : ObservableObject, IDisposable
{
    public const string UnbuyableIntro =
        "Dit is niet op de Trading Post te koop en je hebt er nog geen herkomst voor vastgelegd. Zoek het op in de wiki. Kun je het bij een verkoper kopen, leg dat dan vast: de app rekent er dan mee.";

    public const string ProgressExplanation =
        "Een onderdeel telt mee als je er genoeg van hebt, of als je het nu zelf kunt maken met wat je bezit.";

    private const int MaxSuggestions = 8;

    private readonly GoalService _goals;
    private readonly PlanningService _planning;
    private readonly LegendaryCatalog _legendaries;
    private readonly ItemCatalog _items;
    private readonly CurrencyCatalog _currencies;
    private readonly PriceCatalog _prices;
    private readonly InventoryProvider _inventory;
    private readonly AppState _state;
    private readonly HashSet<string> _expandedPaths = [];
    private readonly Dispatcher _dispatcher;

    private IReadOnlyList<LegendaryOption> _options = [];
    private IReadOnlyList<GoalRow> _rows = [];
    private IReadOnlyList<LegendaryOption> _suggestions = [];
    private IReadOnlyList<ShortageRow> _shortages = [];
    private IReadOnlyList<ShortageRow> _overlayShortages = [];
    private IReadOnlyList<ChecklistRow> _checklist = [];
    private List<MaterialRowVm> _allMaterialRows = [];
    private IReadOnlyList<MaterialRowVm> _materialRows = [];
    private IReadOnlyList<ReservationRowVm> _reservationRows = [];
    private IReadOnlyList<CurrencyRowVm> _currencyRows = [];
    private IReadOnlyList<TreeNodeRow> _treeRoots = [];
    private IReadOnlyList<CostRowVm> _costRows = [];
    private IReadOnlyList<string> _suggestionRows = [];
    private string _costHeadline = string.Empty;
    private string _coinText = string.Empty;
    private string? _unbuyableText;
    private IReadOnlyList<string> _vendorLines = [];
    private IReadOnlyList<string> _currencyLines = [];
    private IReadOnlyList<UnbuyableRowVm> _unbuyableRows = [];
    private string _priceStatusText = string.Empty;
    private bool _forcePriceRefresh;
    private string _addSearchText = string.Empty;
    private string _statusText = string.Empty;
    private string? _errorText;
    private string _selectedName = "Geen Legendary geselecteerd";
    private string? _selectedIconUrl;
    private double _selectedProgress;
    private string _selectedProgressText = string.Empty;
    private string _nextStepText = string.Empty;
    private string _selectedPercentText = string.Empty;
    private string _selectedPartsText = string.Empty;
    private string? _nextStepIconUrl;
    private string? _recipeNotice;
    private string _selectedNote = string.Empty;
    private int? _selectedGoalId;
    private int _version;

    public GoalsViewModel(
        GoalService goals,
        PlanningService planning,
        LegendaryCatalog legendaries,
        ItemCatalog items,
        CurrencyCatalog currencies,
        PriceCatalog prices,
        InventoryProvider inventory,
        AppState state,
        Dispatcher dispatcher)
    {
        _goals = goals;
        _planning = planning;
        _legendaries = legendaries;
        _items = items;
        _currencies = currencies;
        _prices = prices;
        _inventory = inventory;
        _state = state;
        _dispatcher = dispatcher;

        _planning.Changed += OnPlansChanged;
        _state.SettingsChanged += OnSettingsChanged;

        AddCommand = new RelayCommand<LegendaryOption>(option => Run(async () =>
        {
            await _goals.AddAsync(option.ItemId);
            AddSearchText = string.Empty;
            SelectedGoalId = option.ItemId;
        }));
        RemoveCommand = new RelayCommand<GoalRow>(row => Run(() => _goals.RemoveAsync(row.ItemId)));
        MoveUpCommand = new RelayCommand<GoalRow>(row => Run(() => _goals.MoveAsync(row.ItemId, -1)));
        MoveDownCommand = new RelayCommand<GoalRow>(row => Run(() => _goals.MoveAsync(row.ItemId, 1)));
        OpenLinkCommand = new RelayCommand<string>(Services.LinkOpener.Open);
        AddSourceCommand = new RelayCommand<UnbuyableRowVm>(row => AddSourceRequested?.Invoke(this, row));
        RefreshPricesCommand = new RelayCommand(() =>
        {
            _forcePriceRefresh = true;
            Rebuild(_planning.Current);
        });
    }

    public ICommand AddCommand { get; }

    public ICommand RemoveCommand { get; }

    public ICommand MoveUpCommand { get; }

    public ICommand MoveDownCommand { get; }

    public ICommand RefreshPricesCommand { get; }

    /// <summary>Opens a web page (wiki or gw2treasures) in the browser; only known, safe links are opened.</summary>
    public ICommand OpenLinkCommand { get; }

    /// <summary>Asks for the form where the user records where an unbuyable item can be bought.</summary>
    public ICommand AddSourceCommand { get; }

    public event EventHandler<UnbuyableRowVm>? AddSourceRequested;

    public IReadOnlyList<GoalRow> Rows
    {
        get => _rows;
        private set => Set(ref _rows, value, nameof(Rows), nameof(HasGoals), nameof(HasNoGoals));
    }

    public bool HasGoals => _rows.Count > 0;

    public bool HasNoGoals => _rows.Count == 0;

    public IReadOnlyList<LegendaryOption> Suggestions
    {
        get => _suggestions;
        private set => Set(ref _suggestions, value, nameof(Suggestions), nameof(HasSuggestions));
    }

    public bool HasSuggestions => _suggestions.Count > 0;

    public string AddSearchText
    {
        get => _addSearchText;
        set
        {
            _addSearchText = value ?? string.Empty;
            RaisePropertyChanged(nameof(AddSearchText));
            UpdateSuggestions();
        }
    }

    /// <summary>The goal shown in the detail panel and in the overlay.</summary>
    public int? SelectedGoalId
    {
        get => _selectedGoalId;
        set
        {
            // The list reports null while its items are being replaced; that is not the user deselecting.
            if (value is null || _selectedGoalId == value)
            {
                return;
            }

            _selectedGoalId = value;
            RaisePropertyChanged(nameof(SelectedGoalId));
            if (value is { } id)
            {
                Run(() => _state.UpdateAsync(s => s with { SelectedLegendaryId = id.ToString(CultureInfo.InvariantCulture) }));
            }

            Rebuild(_planning.Current);
        }
    }

    public bool HasSelection => _selectedGoalId is not null && _rows.Any(r => r.ItemId == _selectedGoalId);

    public string SelectedName
    {
        get => _selectedName;
        private set => Set(ref _selectedName, value, nameof(SelectedName), nameof(SelectedWikiUrl), nameof(SelectedTreasuresUrl));
    }

    public string? SelectedIconUrl
    {
        get => _selectedIconUrl;
        private set => Set(ref _selectedIconUrl, value, nameof(SelectedIconUrl));
    }

    /// <summary>0 to 1, see <see cref="ProgressExplanation"/> for what it means.</summary>
    public double SelectedProgress
    {
        get => _selectedProgress;
        private set => Set(ref _selectedProgress, value, nameof(SelectedProgress));
    }

    public string SelectedProgressText
    {
        get => _selectedProgressText;
        private set => Set(ref _selectedProgressText, value, nameof(SelectedProgressText));
    }

    public string NextStepText
    {
        get => _nextStepText;
        private set => Set(ref _nextStepText, value, nameof(NextStepText), nameof(HasNextStep));
    }

    public bool HasNextStep => _nextStepText.Length > 0;

    /// <summary>The progress as one number, e.g. <c>42%</c>; the overlay gives it a place of its own.</summary>
    public string SelectedPercentText
    {
        get => _selectedPercentText;
        private set => Set(ref _selectedPercentText, value, nameof(SelectedPercentText), nameof(HasPlanNumbers), nameof(ShowAllCovered));
    }

    /// <summary>The count behind the percentage, e.g. <c>3 van 7 onderdelen klaar</c>.</summary>
    public string SelectedPartsText
    {
        get => _selectedPartsText;
        private set => Set(ref _selectedPartsText, value, nameof(SelectedPartsText));
    }

    /// <summary>Icon of the item the next step is about.</summary>
    public string? NextStepIconUrl
    {
        get => _nextStepIconUrl;
        private set => Set(ref _nextStepIconUrl, value, nameof(NextStepIconUrl));
    }

    /// <summary>True when the plan has numbers to show (not when there is no recipe data or the plan failed).</summary>
    public bool HasPlanNumbers => _selectedPercentText.Length > 0;

    /// <summary>The goal is selected and nothing is missing: the overlay says so instead of showing an empty list.</summary>
    public bool ShowAllCovered => HasSelection && HasPlanNumbers && _overlayShortages.Count == 0;

    /// <summary>Shown instead of numbers when the plan cannot be trusted, e.g. no recipe data for this Legendary.</summary>
    public string? RecipeNotice
    {
        get => _recipeNotice;
        private set => Set(ref _recipeNotice, value, nameof(RecipeNotice), nameof(HasRecipeNotice));
    }

    public bool HasRecipeNotice => _recipeNotice is not null;

    public IReadOnlyList<ShortageRow> Shortages
    {
        get => _shortages;
        private set => Set(ref _shortages, value, nameof(Shortages));
    }

    /// <summary>The biggest shortages, limited to the number of materials the user wants to see in the overlay.</summary>
    public IReadOnlyList<ShortageRow> OverlayShortages
    {
        get => _overlayShortages;
        private set => Set(ref _overlayShortages, value, nameof(OverlayShortages), nameof(HasOverlayShortages), nameof(ShowAllCovered));
    }

    public bool HasOverlayShortages => _overlayShortages.Count > 0;

    public IReadOnlyList<ChecklistRow> Checklist
    {
        get => _checklist;
        private set => Set(ref _checklist, value, nameof(Checklist), nameof(HasChecklist));
    }

    public bool HasChecklist => _checklist.Count > 0;

    /// <summary>Expanded overlay: the materials of the selected goal, all of them or only what is missing (a setting).</summary>
    public IReadOnlyList<MaterialRowVm> MaterialRows
    {
        get => _materialRows;
        private set => Set(ref _materialRows, value, nameof(MaterialRows), nameof(HasMaterialRows), nameof(MaterialsHint));
    }

    public bool HasMaterialRows => _materialRows.Count > 0;

    /// <summary>Explains an empty material list instead of leaving a blank space.</summary>
    public string MaterialsHint =>
        _materialRows.Count > 0 ? string.Empty
        : _allMaterialRows.Count > 0 ? "Je hebt alle materialen die nodig zijn."
        : string.Empty;

    public IReadOnlyList<ReservationRowVm> ReservationRows
    {
        get => _reservationRows;
        private set => Set(ref _reservationRows, value, nameof(ReservationRows), nameof(HasReservations));
    }

    public bool HasReservations => _reservationRows.Count > 0;

    public IReadOnlyList<CurrencyRowVm> CurrencyRows
    {
        get => _currencyRows;
        private set => Set(ref _currencyRows, value, nameof(CurrencyRows), nameof(HasCurrencies));
    }

    public bool HasCurrencies => _currencyRows.Count > 0;

    /// <summary>The crafting tree below the selected goal.</summary>
    public IReadOnlyList<TreeNodeRow> TreeRoots
    {
        get => _treeRoots;
        private set => Set(ref _treeRoots, value, nameof(TreeRoots), nameof(HasTree));
    }

    public bool HasTree => _treeRoots.Count > 0;

    /// <summary>What the missing, purchasable materials cost on the Trading Post (desktop app only).</summary>
    public string CostHeadline
    {
        get => _costHeadline;
        private set => Set(ref _costHeadline, value, nameof(CostHeadline), nameof(HasCosts));
    }

    public bool HasCosts => _costHeadline.Length > 0;

    public string CoinText
    {
        get => _coinText;
        private set => Set(ref _coinText, value, nameof(CoinText), nameof(HasCoinText));
    }

    public bool HasCoinText => _coinText.Length > 0;

    public IReadOnlyList<CostRowVm> CostRows
    {
        get => _costRows;
        private set => Set(ref _costRows, value, nameof(CostRows), nameof(HasCostRows));
    }

    public bool HasCostRows => _costRows.Count > 0;

    /// <summary>Crafted parts that are cheaper to buy than to make, with what each saves.</summary>
    public IReadOnlyList<string> SuggestionRows
    {
        get => _suggestionRows;
        private set => Set(ref _suggestionRows, value, nameof(SuggestionRows), nameof(HasSuggestionRows));
    }

    public bool HasSuggestionRows => _suggestionRows.Count > 0;

    public string? UnbuyableText
    {
        get => _unbuyableText;
        private set => Set(ref _unbuyableText, value, nameof(UnbuyableText));
    }

    public bool HasUnbuyable => _unbuyableRows.Count > 0;

    public IReadOnlyList<UnbuyableRowVm> UnbuyableRows
    {
        get => _unbuyableRows;
        private set => Set(ref _unbuyableRows, value, nameof(UnbuyableRows), nameof(HasUnbuyable));
    }

    /// <summary>Items that are bought from a vendor, with the price and the Trading Post alternative.</summary>
    public IReadOnlyList<string> VendorLines
    {
        get => _vendorLines;
        private set => Set(ref _vendorLines, value, nameof(VendorLines), nameof(HasVendorLines));
    }

    public bool HasVendorLines => _vendorLines.Count > 0;

    /// <summary>The currencies the plan needs (karma, ...) set against the wallet.</summary>
    public IReadOnlyList<string> CurrencyLines
    {
        get => _currencyLines;
        private set => Set(ref _currencyLines, value, nameof(CurrencyLines), nameof(HasCurrencyLines));
    }

    public bool HasCurrencyLines => _currencyLines.Count > 0;

    public string SelectedWikiUrl => _selectedGoalId is null || SelectedName.Length == 0 ? string.Empty : ItemLinks.Wiki(SelectedName);

    public string SelectedTreasuresUrl => _selectedGoalId is { } id ? ItemLinks.Treasures(id) : string.Empty;

    public string PriceStatusText
    {
        get => _priceStatusText;
        private set => Set(ref _priceStatusText, value, nameof(PriceStatusText));
    }

    /// <summary>What the estimate leaves out, shown with the numbers so they are not taken for exact.</summary>
    public string CostDisclaimer { get; } = CostPresenter.Disclaimer;

    public string SelectedNote
    {
        get => _selectedNote;
        set
        {
            var note = value ?? string.Empty;
            if (_selectedNote == note)
            {
                return;
            }

            _selectedNote = note;
            RaisePropertyChanged(nameof(SelectedNote));
            if (_selectedGoalId is { } id)
            {
                Run(() => _goals.SetNoteAsync(id, note));
            }
        }
    }

    /// <summary>Progress of loading recipes and the Legendary list, shown so a first start does not look frozen.</summary>
    public string StatusText
    {
        get => _statusText;
        set => Set(ref _statusText, value, nameof(StatusText));
    }

    public string? ErrorText
    {
        get => _errorText;
        private set => Set(ref _errorText, value, nameof(ErrorText), nameof(HasError));
    }

    public bool HasError => _errorText is not null;

    /// <summary>Loads the stored goals and Legendary list; refreshes the list in the background.</summary>
    public async Task InitializeAsync()
    {
        await _goals.LoadAsync();
        var selected = _state.Settings.SelectedLegendaryId;
        if (int.TryParse(selected, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
        {
            _selectedGoalId = id;
        }

        await ReloadOptionsAsync(await _legendaries.LoadIdsAsync());

        try
        {
            // The list changes rarely, so it is fetched at most once a day.
            await ReloadOptionsAsync(await _legendaries.RefreshIfStaleAsync());
        }
        catch (Gw2ApiException)
        {
            // Offline: the stored list is already shown.
        }
    }

    public void Dispose()
    {
        _planning.Changed -= OnPlansChanged;
        _state.SettingsChanged -= OnSettingsChanged;
    }

    private async Task ReloadOptionsAsync(IReadOnlyList<int> ids)
    {
        if (ids.Count == 0)
        {
            return;
        }

        var info = await _items.ResolveAsync(ids);
        _options = [.. ids.Where(info.ContainsKey).Select(i => new LegendaryOption(i, info[i].Name, info[i].IconUrl)).OrderBy(o => o.Name, StringComparer.CurrentCultureIgnoreCase)];
        UpdateSuggestions();
    }

    private void UpdateSuggestions()
    {
        var term = _addSearchText.Trim();
        if (term.Length == 0)
        {
            Suggestions = [];
            return;
        }

        var taken = _goals.Goals.Select(g => g.ItemId).ToHashSet();
        Suggestions = [.. _options
            .Where(o => !taken.Contains(o.ItemId) && o.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase))
            .Take(MaxSuggestions)];
    }

    private void OnPlansChanged(object? sender, IReadOnlyList<GoalPlan> plans) =>
        _dispatcher.InvokeAsync(() =>
        {
            UpdateSuggestions();
            Rebuild(plans);
        });

    private void OnSettingsChanged(object? sender, AppSettings settings) =>
        _dispatcher.InvokeAsync(() =>
        {
            OverlayShortages = [.. _shortages.Take(settings.Overlay.VisibleMaterialCount)];
            ApplyMaterialFilter(settings.Overlay.ShowOnlyMissing);
        });

    private void ApplyMaterialFilter(bool onlyMissing) =>
        MaterialRows = onlyMissing ? [.. _allMaterialRows.Where(r => r.IsMissing)] : _allMaterialRows;

    private void OnNodeExpandedChanged(string path, bool isExpanded)
    {
        _ = isExpanded ? _expandedPaths.Add(path) : _expandedPaths.Remove(path);
    }

    private void Rebuild(IReadOnlyList<GoalPlan> plans) => _ = RebuildAsync(plans);

    private async Task RebuildAsync(IReadOnlyList<GoalPlan> plans)
    {
        var version = ++_version;

        var selected = plans.FirstOrDefault(p => p.Goal.ItemId == _selectedGoalId) ?? (plans.Count > 0 ? plans[0] : null);
        var ids = plans.Select(p => p.Goal.ItemId).ToHashSet();
        if (selected is not null)
        {
            ids.UnionWith(GoalPresenter.ReferencedItemIds(selected));
        }

        var overview = MaterialOverview.Build(plans, IndexOf(plans, selected), _inventory.Current);
        var tree = selected?.Plan is { } selectedPlan ? PlanTree.Build(selectedPlan) : [];
        ids.UnionWith(Flatten(tree).Where(n => n.Key.Kind == MaterialKind.Item).Select(n => n.Key.Id));
        var currencyIds = overview.Currencies.Select(c => c.CurrencyId)
            .Concat(Flatten(tree).Where(n => n.Key.Kind == MaterialKind.Currency).Select(n => n.Key.Id))
            .ToHashSet();

        IReadOnlyDictionary<int, ItemInfo> info;
        try
        {
            info = await _items.ResolveAsync(ids);
        }
        catch (Exception)
        {
            // Names are a convenience; the numbers must still show if the item cache is unavailable.
            info = new Dictionary<int, ItemInfo>();
        }

        var currencyNames = await _currencies.ResolveAsync(currencyIds);

        if (version != _version)
        {
            return;
        }

        string Name(int id) => info.TryGetValue(id, out var item) ? item.Name : $"item #{id.ToString(CultureInfo.InvariantCulture)}";
        string? Icon(int id) => info.TryGetValue(id, out var item) ? item.IconUrl : null;
        string CurrencyName(int id) => currencyNames.TryGetValue(id, out var currency) ? currency : $"currency #{id.ToString(CultureInfo.InvariantCulture)}";
        string KeyName(MaterialKey key) => key.Kind == MaterialKind.Item ? Name(key.Id) : CurrencyName(key.Id);
        string? KeyIcon(MaterialKey key) => key.Kind == MaterialKind.Item ? Icon(key.Id) : null;

        Rows = [.. plans.Select(p => new GoalRow(
            p.Goal.ItemId,
            Name(p.Goal.ItemId),
            Icon(p.Goal.ItemId),
            p.Plan?.Progress ?? 0,
            p.Plan is null ? "–" : p.Plan.Progress.ToString("P0", CultureInfo.CurrentCulture)))];

        if (selected is null)
        {
            ShowNoSelection();
            return;
        }

        _selectedGoalId = selected.Goal.ItemId;
        RaisePropertyChanged(nameof(SelectedGoalId));
        RaisePropertyChanged(nameof(HasSelection));
        SelectedName = Name(selected.Goal.ItemId);
        SelectedIconUrl = Icon(selected.Goal.ItemId);
        if (_selectedNote != selected.Goal.Note)
        {
            _selectedNote = selected.Goal.Note;
            RaisePropertyChanged(nameof(SelectedNote));
        }

        ShowPlan(selected, Name, Icon, new PlanDetails(overview, tree, KeyName, KeyIcon, CurrencyName));
        _ = UpdateCostsAsync(selected, version, Name, Icon, CurrencyName);
    }

    /// <summary>
    /// Prices arrive after the rest of the screen is shown: they may need a request, and the screen should not wait for it.
    /// </summary>
    private async Task UpdateCostsAsync(GoalPlan goalPlan, int version, Func<int, string> name, Func<int, string?> icon, Func<int, string> currencyName)
    {
        if (goalPlan.Plan is not { } plan)
        {
            ClearCosts();
            return;
        }

        var ids = CostAnalyzer.RelevantItemIds(plan);
        if (ids.Count == 0)
        {
            ApplyCosts(plan, PriceBook.Empty, name, icon, currencyName);
            return;
        }

        PriceStatusText = "Prijzen ophalen…";
        var maxAge = _forcePriceRefresh ? TimeSpan.Zero : (TimeSpan?)null;
        _forcePriceRefresh = false;

        PriceBook book;
        try
        {
            book = await _prices.EnsureAsync(ids, maxAge);
        }
        catch (Exception ex)
        {
            // The price cache is unavailable; everything else keeps working.
            PriceStatusText = $"Prijzen konden niet worden geladen: {ex.Message}";
            return;
        }

        if (version != _version)
        {
            return; // a newer calculation is already running
        }

        ApplyCosts(plan, book, name, icon, currencyName);
        PriceStatusText = CostPresenter.PriceStatus(book);
    }

    private void ApplyCosts(CraftPlan plan, PriceBook book, Func<int, string> name, Func<int, string?> icon, Func<int, string> currencyName)
    {
        var inventory = _inventory.Current;
        var estimate = CostAnalyzer.Analyze(plan, book, inventory.CurrencyAmount(CoinFormatter.CoinCurrencyId), inventory.CurrencyAmount);
        var summary = CostPresenter.Summarize(estimate, name, currencyName: currencyName);

        CostHeadline = summary.Headline;
        CoinText = summary.CoinText;
        CostRows = [.. summary.Lines.Select(l => new CostRowVm(name(l.ItemId), icon(l.ItemId), l.Quantity, l.UnitText, l.TotalText))];
        SuggestionRows = summary.Suggestions;
        VendorLines = summary.VendorLines;
        CurrencyLines = summary.CurrencyLines;
        UnbuyableRows = [.. estimate.Unbuyable.Select(u => new UnbuyableRowVm(u.ItemId, name(u.ItemId), icon(u.ItemId), u.Quantity))];
        UnbuyableText = UnbuyableRows.Count > 0 ? UnbuyableIntro : null;
    }

    private static int IndexOf(IReadOnlyList<GoalPlan> plans, GoalPlan? plan)
    {
        for (var i = 0; plan is not null && i < plans.Count; i++)
        {
            if (ReferenceEquals(plans[i], plan))
            {
                return i;
            }
        }

        return -1;
    }

    private static IEnumerable<PlanTreeNode> Flatten(IEnumerable<PlanTreeNode> nodes) =>
        nodes.SelectMany(n => new[] { n }.Concat(Flatten(n.Children)));

    private sealed record PlanDetails(
        MaterialOverviewResult Overview,
        IReadOnlyList<PlanTreeNode> Tree,
        Func<MaterialKey, string> KeyName,
        Func<MaterialKey, string?> KeyIcon,
        Func<int, string> CurrencyName);

    private void ShowNoSelection()
    {
        RaisePropertyChanged(nameof(HasSelection));
        SelectedName = "Geen Legendary geselecteerd";
        SelectedIconUrl = null;
        SelectedProgress = 0;
        SelectedProgressText = string.Empty;
        SelectedPercentText = string.Empty;
        SelectedPartsText = string.Empty;
        NextStepIconUrl = null;
        NextStepText = "Kies hieronder een Legendary om te beginnen.";
        RecipeNotice = null;
        Shortages = [];
        OverlayShortages = [];
        Checklist = [];
        ClearDetails();
    }

    private void ClearCosts()
    {
        CostHeadline = string.Empty;
        CoinText = string.Empty;
        CostRows = [];
        SuggestionRows = [];
        UnbuyableText = null;
        UnbuyableRows = [];
        VendorLines = [];
        CurrencyLines = [];
        PriceStatusText = string.Empty;
    }

    private void ClearDetails()
    {
        ClearCosts();
        _allMaterialRows = [];
        MaterialRows = [];
        ReservationRows = [];
        CurrencyRows = [];
        TreeRoots = [];
    }

    private void ShowPlan(GoalPlan goalPlan, Func<int, string> name, Func<int, string?> icon, PlanDetails details)
    {
        var summary = GoalPresenter.Summarize(goalPlan, name, inventory: _inventory.Current, currencyName: details.CurrencyName);

        SelectedProgress = summary.Progress;
        SelectedProgressText = summary.ProgressText;
        SelectedPercentText = summary.PercentText;
        SelectedPartsText = summary.PartsText;
        NextStepText = summary.NextStepText;
        NextStepIconUrl = summary.NextItemId is { } nextId ? icon(nextId) : null;
        RecipeNotice = summary.Notice;
        RaisePropertyChanged(nameof(HasPlanNumbers));
        RaisePropertyChanged(nameof(ShowAllCovered));

        Shortages = [.. summary.Shortages.Select(l => new ShortageRow(name(l.ItemId), icon(l.ItemId), l.Missing, l.Needed, l.Owned, l.ItemId))];
        OverlayShortages = [.. Shortages.Take(_state.Settings.Overlay.VisibleMaterialCount)];
        Checklist = [.. summary.Checklist.Select(l => new ChecklistRow(l.ItemId, name(l.ItemId), icon(l.ItemId), l.StatusText, l.IsDone, OnChecklistToggled))];

        _allMaterialRows = [.. details.Overview.Materials.Select(m => new MaterialRowVm(name(m.ItemId), icon(m.ItemId), m.Needed, m.Owned, m.Missing))];
        ApplyMaterialFilter(_state.Settings.Overlay.ShowOnlyMissing);

        ReservationRows = [.. details.Overview.Reservations.Select(r => new ReservationRowVm(string.Create(
            CultureInfo.CurrentCulture,
            $"{r.Amount:N0}× {details.KeyName(r.Key)} staat gereserveerd voor {name(r.ForGoalItemId)} (hogere voorrang)")))];
        CurrencyRows = [.. details.Overview.Currencies.Select(c => new CurrencyRowVm(details.CurrencyName(c.CurrencyId), c.Needed, c.Owned, c.Missing))];
        TreeRoots = [.. details.Tree.Select(node => ToRow(node, details))];
    }

    private TreeNodeRow ToRow(PlanTreeNode node, PlanDetails details) => new(
        node.Path,
        details.KeyName(node.Key),
        details.KeyIcon(node.Key),
        node.Required,
        node.StatusText(),
        node.State,
        [.. node.Children.Select(child => ToRow(child, details))],
        _expandedPaths.Contains(node.Path),
        OnNodeExpandedChanged);

    private void OnChecklistToggled(int componentItemId, bool isDone)
    {
        if (_selectedGoalId is { } goalId)
        {
            Run(() => _goals.SetCompletedAsync(goalId, componentItemId, isDone));
        }
    }

    private void Set<T>(ref T field, T value, params string[] names)
    {
        field = value;
        foreach (var name in names)
        {
            RaisePropertyChanged(name);
        }
    }

    /// <summary>Runs a store operation from a command; failures are shown instead of being lost.</summary>
    private void Run(Func<Task> operation) => _ = RunAsync(operation);

    private async Task RunAsync(Func<Task> operation)
    {
        try
        {
            await operation();
            await _dispatcher.InvokeAsync(() => ErrorText = null);
        }
        catch (Exception ex)
        {
            await _dispatcher.InvokeAsync(() => ErrorText = $"Dat kon niet worden opgeslagen: {ex.Message}");
        }
    }
}
