using System.Globalization;
using Gw2LegendaryTracker.Core.Acquisition;
using Gw2LegendaryTracker.Core.Crafting;
using Gw2LegendaryTracker.Core.Gw2;
using Gw2LegendaryTracker.Core.Items;
using Gw2LegendaryTracker.Core.Trading;

namespace Gw2LegendaryTracker.App.ViewModels;

public sealed record CurrencyChoice(int Id, string Name);

/// <summary>The form for recording where an item can be bought: what it costs and who sells it.</summary>
public sealed class OfferEditorViewModel : ObservableObject
{
    private readonly AcquisitionService _service;
    private readonly CurrencyCatalog _currencies;
    private IReadOnlyList<CurrencyChoice> _choices = [];
    private CurrencyChoice? _selected;
    private string _priceText = string.Empty;
    private string _piecesText = "1";
    private string _vendorName = string.Empty;
    private string _location = string.Empty;
    private string? _error;
    private bool _isLoaded;

    public OfferEditorViewModel(AcquisitionService service, CurrencyCatalog currencies, int itemId, string itemName)
    {
        _service = service;
        _currencies = currencies;
        ItemId = itemId;
        ItemName = itemName;
    }

    public int ItemId { get; }

    public string ItemName { get; }

    public IReadOnlyList<CurrencyChoice> Currencies
    {
        get => _choices;
        private set
        {
            _choices = value;
            RaisePropertyChanged(nameof(Currencies));
        }
    }

    public CurrencyChoice? SelectedCurrency
    {
        get => _selected;
        set
        {
            _selected = value;
            RaisePropertyChanged(nameof(SelectedCurrency));
        }
    }

    public string PriceText
    {
        get => _priceText;
        set
        {
            _priceText = value ?? string.Empty;
            RaisePropertyChanged(nameof(PriceText));
        }
    }

    public string PiecesText
    {
        get => _piecesText;
        set
        {
            _piecesText = value ?? string.Empty;
            RaisePropertyChanged(nameof(PiecesText));
        }
    }

    public string VendorName
    {
        get => _vendorName;
        set
        {
            _vendorName = value ?? string.Empty;
            RaisePropertyChanged(nameof(VendorName));
        }
    }

    public string Location
    {
        get => _location;
        set
        {
            _location = value ?? string.Empty;
            RaisePropertyChanged(nameof(Location));
        }
    }

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

    /// <summary>False until the list of currencies is there; saving needs it.</summary>
    public bool CanSave => _isLoaded;

    /// <summary>Loads the currencies to choose from. Coin is left out: its amounts are in copper, which invites mistakes.</summary>
    public async Task LoadAsync()
    {
        try
        {
            var all = await _currencies.AllAsync();
            Currencies = [.. all
                .Where(c => c.Key != CoinFormatter.CoinCurrencyId)
                .Select(c => new CurrencyChoice(c.Key, c.Value))
                .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)];
            SelectedCurrency ??= Currencies.FirstOrDefault(c => string.Equals(c.Name, "Karma", StringComparison.OrdinalIgnoreCase));
            _isLoaded = true;
            Error = null;
        }
        catch (Gw2ApiException ex)
        {
            Error = $"De lijst met betaalmiddelen kon niet worden geladen: {ex.Message}";
        }

        RaisePropertyChanged(nameof(CanSave));
    }

    /// <summary>Stores the offer. Returns <c>true</c> when it was saved; otherwise <see cref="Error"/> says why not.</summary>
    public async Task<bool> SaveAsync()
    {
        if (SelectedCurrency is null)
        {
            Error = "Kies waarmee je betaalt, bijvoorbeeld Karma.";
            return false;
        }

        if (!long.TryParse(PriceText.Trim(), NumberStyles.None, CultureInfo.CurrentCulture, out var price) || price <= 0)
        {
            Error = "Vul de prijs in als een heel getal groter dan 0, bijvoorbeeld 25.";
            return false;
        }

        if (!int.TryParse(PiecesText.Trim(), NumberStyles.None, CultureInfo.CurrentCulture, out var pieces) || pieces <= 0)
        {
            Error = "Vul in hoeveel stuks je voor die prijs krijgt, meestal 1.";
            return false;
        }

        try
        {
            var error = await _service.AddAsync(new NewOffer(
                ItemId,
                pieces,
                [new Ingredient(MaterialKey.Currency(SelectedCurrency.Id), price)],
                VendorName,
                Location));
            Error = error;
            return error is null;
        }
        catch (Exception ex)
        {
            // Storing failed (disk, database): say so instead of closing the form as if it worked.
            Error = $"Het kon niet worden opgeslagen: {ex.Message}";
            return false;
        }
    }
}
