using System.Globalization;
using System.Windows.Threading;
using Gw2LegendaryTracker.Core.Gw2;
using Gw2LegendaryTracker.Core.Inventory;
using Gw2LegendaryTracker.Core.Items;

namespace Gw2LegendaryTracker.App.ViewModels;

/// <summary>One line in the "Wat heb ik?" list.</summary>
public sealed record InventoryRow(int ItemId, string Name, string? IconUrl, long Total, string WhereText)
{
    public string TotalText => Total.ToString("N0", CultureInfo.CurrentCulture);
}

/// <summary>Searchable list of everything the account owns, built from the shared <see cref="InventoryModel"/>.</summary>
public sealed class InventoryViewModel : ObservableObject, IDisposable
{
    private readonly InventoryProvider _provider;
    private readonly ItemCatalog _catalog;
    private readonly Dispatcher _dispatcher;
    private List<InventoryRow> _allRows = [];
    private IReadOnlyList<InventoryRow> _visibleRows = [];
    private string _searchText = string.Empty;
    private string _statusText = EmptyText;
    private int _version;

    private const string EmptyText = "Nog geen gegevens. Verbind je account en synchroniseer om je spullen te zien.";

    public InventoryViewModel(InventoryProvider provider, ItemCatalog catalog, Dispatcher dispatcher)
    {
        _provider = provider;
        _catalog = catalog;
        _dispatcher = dispatcher;
        _provider.Changed += OnInventoryChanged;
    }

    public IReadOnlyList<InventoryRow> VisibleRows
    {
        get => _visibleRows;
        private set
        {
            _visibleRows = value;
            RaisePropertyChanged(nameof(VisibleRows));
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            _searchText = value ?? string.Empty;
            RaisePropertyChanged(nameof(SearchText));
            ApplyFilter();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            _statusText = value;
            RaisePropertyChanged(nameof(StatusText));
        }
    }

    public void Dispose() => _provider.Changed -= OnInventoryChanged;

    internal static string DescribeLocations(IEnumerable<StockLocation> locations) =>
        string.Join(" · ", locations.Select(l => $"{LocationLabel(l)} {l.Count.ToString("N0", CultureInfo.CurrentCulture)}"));

    private static string LocationLabel(StockLocation location) => location.Source switch
    {
        InventorySource.Bank => "Bank",
        InventorySource.Materials => "Materialen",
        InventorySource.SharedInventory => "Gedeelde inventaris",
        _ => location.CharacterName,
    };

    private void OnInventoryChanged(object? sender, InventoryModel model) =>
        _dispatcher.InvokeAsync(() => RefreshAsync(model));

    private async Task RefreshAsync(InventoryModel model)
    {
        var version = ++_version;
        var stocks = model.Items;
        if (stocks.Count == 0)
        {
            _allRows = [];
            StatusText = EmptyText;
            ApplyFilter();
            return;
        }

        StatusText = "Namen en iconen laden…";
        IReadOnlyDictionary<int, ItemInfo> info;
        string? nameError = null;
        try
        {
            info = await _catalog.ResolveAsync(stocks.Select(s => s.ItemId));
        }
        catch (Exception ex)
        {
            // Cache or storage problem: still show the items, just without names.
            info = new Dictionary<int, ItemInfo>();
            nameError = $"Namen konden niet worden geladen: {ex.Message}";
        }

        if (version != _version)
        {
            return; // a newer refresh is already running
        }

        _allRows = stocks
            .Select(s =>
            {
                var known = info.GetValueOrDefault(s.ItemId);
                return new InventoryRow(
                    s.ItemId,
                    known?.Name ?? $"Onbekend item (#{s.ItemId})",
                    known?.IconUrl,
                    s.Total,
                    DescribeLocations(s.Locations));
            })
            .OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        // Names can be missing offline; say so instead of silently showing "Onbekend item".
        StatusText = nameError
            ?? (info.Count < stocks.Count
                ? $"{_allRows.Count:N0} verschillende items. Sommige namen ontbreken; ze worden bij de volgende synchronisatie opgehaald."
                : $"{_allRows.Count:N0} verschillende items");

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var term = _searchText.Trim();
        VisibleRows = term.Length == 0
            ? _allRows
            : _allRows.Where(r => r.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase)).ToList();
    }
}
