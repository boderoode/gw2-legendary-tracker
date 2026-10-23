using System.Globalization;
using System.Windows.Input;
using System.Windows.Threading;
using Gw2LegendaryTracker.Core.Acquisition;
using Gw2LegendaryTracker.Core.Crafting;
using Gw2LegendaryTracker.Core.Items;
using Gw2LegendaryTracker.Core.Trading;

namespace Gw2LegendaryTracker.App.ViewModels;

/// <summary>One of the user's own vendor offers, as shown in the list.</summary>
public sealed record OfferRowVm(int Id, string Name, string? IconUrl, string Text);

/// <summary>The user's own vendor offers: the list, removing, and creating the form for a new one.</summary>
public sealed class AcquisitionsViewModel : ObservableObject, IDisposable
{
    private readonly AcquisitionService _service;
    private readonly CurrencyCatalog _currencies;
    private readonly ItemCatalog _items;
    private readonly Dispatcher _dispatcher;
    private IReadOnlyList<OfferRowVm> _rows = [];
    private string? _error;
    private int _version;

    public AcquisitionsViewModel(AcquisitionService service, CurrencyCatalog currencies, ItemCatalog items, string customDataFolder, Dispatcher dispatcher)
    {
        _service = service;
        _currencies = currencies;
        _items = items;
        CustomDataFolder = customDataFolder;
        _dispatcher = dispatcher;

        _service.Changed += OnChanged;
        RemoveCommand = new RelayCommand<OfferRowVm>(row => _ = RemoveAsync(row.Id));
    }

    public ICommand RemoveCommand { get; }

    /// <summary>The folder where the user can drop their own recipe files (also for vendor offers with several costs).</summary>
    public string CustomDataFolder { get; }

    public IReadOnlyList<OfferRowVm> Rows
    {
        get => _rows;
        private set
        {
            _rows = value;
            RaisePropertyChanged(nameof(Rows));
            RaisePropertyChanged(nameof(HasRows));
            RaisePropertyChanged(nameof(HasNoRows));
        }
    }

    public bool HasRows => _rows.Count > 0;

    public bool HasNoRows => _rows.Count == 0;

    public string? Error
    {
        get => _error;
        private set
        {
            _error = value;
            RaisePropertyChanged(nameof(Error));
            RaisePropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => _error is not null;

    public OfferEditorViewModel CreateEditor(int itemId, string itemName) => new(_service, _currencies, itemId, itemName);

    public void Dispose() => _service.Changed -= OnChanged;

    private void OnChanged(object? sender, IReadOnlyList<AcquisitionOffer> offers) =>
        _dispatcher.InvokeAsync(() => RebuildAsync(offers));

    private async Task RemoveAsync(int offerId)
    {
        try
        {
            await _service.RemoveAsync(offerId);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Het kon niet worden verwijderd: {ex.Message}";
        }
    }

    private async Task RebuildAsync(IReadOnlyList<AcquisitionOffer> offers)
    {
        var version = ++_version;
        if (offers.Count == 0)
        {
            Rows = [];
            return;
        }

        IReadOnlyDictionary<int, ItemInfo> items;
        IReadOnlyDictionary<int, string> currencies;
        try
        {
            items = await _items.ResolveAsync(offers.Select(o => o.ItemId).Concat(offers.SelectMany(o => o.Costs).Where(c => c.Key.Kind == MaterialKind.Item).Select(c => c.Key.Id)));
            currencies = await _currencies.ResolveAsync(offers.SelectMany(o => o.Costs).Where(c => c.Key.Kind == MaterialKind.Currency).Select(c => c.Key.Id));
        }
        catch (Exception)
        {
            // Names are a convenience; the offers themselves must still be listed.
            items = new Dictionary<int, ItemInfo>();
            currencies = new Dictionary<int, string>();
        }

        if (version != _version)
        {
            return;
        }

        string ItemName(int id) => items.TryGetValue(id, out var item) ? item.Name : $"item #{id.ToString(CultureInfo.InvariantCulture)}";

        Rows = [.. offers.Select(o =>
        {
            var costs = string.Join(" + ", o.Costs.Select(c => c.Key.Kind == MaterialKind.Currency
                ? string.Create(CultureInfo.CurrentCulture, $"{c.Count:N0} {(currencies.TryGetValue(c.Key.Id, out var name) ? name : $"currency #{c.Key.Id}")}")
                : string.Create(CultureInfo.CurrentCulture, $"{c.Count:N0}× {ItemName(c.Key.Id)}")));
            var seller = string.Join(", ", new[] { o.VendorName, o.Location }.Where(t => !string.IsNullOrWhiteSpace(t)));
            var text = string.Create(CultureInfo.CurrentCulture, $"{o.OutputCount:N0}× voor {costs}{(seller.Length > 0 ? $" bij {seller}" : string.Empty)}");
            return new OfferRowVm(o.Id, ItemName(o.ItemId), items.TryGetValue(o.ItemId, out var info) ? info.IconUrl : null, text);
        })];
    }
}
