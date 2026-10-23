using Gw2LegendaryTracker.Core.Crafting;

namespace Gw2LegendaryTracker.Core.Acquisition;

/// <summary>
/// The user's own vendor offers. Like the goals, every change is stored first and announced afterwards.
/// </summary>
public sealed class AcquisitionService : IDisposable
{
    public const int MaxOffers = 500;
    public const int MaxOutputCount = 10_000;
    public const int MaxCosts = 5;
    public const int MaxVendorNameLength = 100;
    public const int MaxLocationLength = 200;
    public const long MaxCostAmount = 100_000_000;

    private readonly IAcquisitionStore _store;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<AcquisitionOffer> _offers = [];

    public AcquisitionService(IAcquisitionStore store) => _store = store;

    public IReadOnlyList<AcquisitionOffer> Offers => Volatile.Read(ref _offers);

    /// <summary>Raised after the offers changed. May fire on any thread.</summary>
    public event EventHandler<IReadOnlyList<AcquisitionOffer>>? Changed;

    /// <summary>The offers as recipes for the planner.</summary>
    public IReadOnlyList<Recipe> ToRecipes() => [.. Offers.Select(o => o.ToRecipe())];

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var loaded = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
        Volatile.Write(ref _offers, loaded);
        Changed?.Invoke(this, loaded);
    }

    /// <summary>Why the offer cannot be stored, or <c>null</c> when it is fine.</summary>
    public static string? Validate(NewOffer offer)
    {
        ArgumentNullException.ThrowIfNull(offer);

        if (offer.ItemId <= 0)
        {
            return "Kies eerst het item.";
        }

        if (offer.OutputCount is < 1 or > MaxOutputCount)
        {
            return $"Het aantal per aankoop moet tussen 1 en {MaxOutputCount:N0} liggen.";
        }

        if (offer.Costs.Count == 0)
        {
            return "Vul in wat het kost.";
        }

        if (offer.Costs.Count > MaxCosts)
        {
            return $"Een aanbod kan hooguit {MaxCosts} soorten kosten hebben.";
        }

        if (offer.Costs.Any(c => c.Key.Id <= 0 || c.Count <= 0 || c.Count > MaxCostAmount))
        {
            return "Elke prijs moet een aantal groter dan 0 zijn.";
        }

        if (offer.Costs.Select(c => c.Key).Distinct().Count() != offer.Costs.Count)
        {
            return "Dezelfde valuta of hetzelfde item staat dubbel bij de kosten.";
        }

        if (offer.VendorName.Trim().Length > MaxVendorNameLength || offer.Location.Trim().Length > MaxLocationLength)
        {
            return $"De naam van de verkoper mag hooguit {MaxVendorNameLength} tekens zijn en de plaats hooguit {MaxLocationLength}.";
        }

        return null;
    }

    /// <summary>Adds an offer. Returns an error text when it is not valid (nothing is stored then), otherwise <c>null</c>.</summary>
    public async Task<string?> AddAsync(NewOffer offer, CancellationToken cancellationToken = default)
    {
        var error = Validate(offer);
        if (error is not null)
        {
            return error;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = _offers;
            if (current.Count >= MaxOffers)
            {
                return $"Er kunnen hooguit {MaxOffers} aanbiedingen worden bewaard.";
            }

            var added = new AcquisitionOffer(
                (current.Count == 0 ? 0 : current.Max(o => o.Id)) + 1,
                offer.ItemId,
                offer.OutputCount,
                [.. offer.Costs],
                offer.VendorName.Trim(),
                offer.Location.Trim());
            await CommitAsync([.. current, added], cancellationToken).ConfigureAwait(false);
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveAsync(int offerId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var next = _offers.Where(o => o.Id != offerId).ToList();
            if (next.Count != _offers.Count)
            {
                await CommitAsync(next, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Replaces all offers, e.g. after an import. Ids are renumbered; the list is stored before anyone is told.</summary>
    public async Task ReplaceAsync(IEnumerable<NewOffer> offers, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(offers);

        var numbered = offers.Select((o, i) => new AcquisitionOffer(i + 1, o.ItemId, o.OutputCount, [.. o.Costs], o.VendorName.Trim(), o.Location.Trim())).ToList();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await CommitAsync(numbered, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private async Task CommitAsync(IReadOnlyList<AcquisitionOffer> next, CancellationToken cancellationToken)
    {
        await _store.SaveAsync(next, cancellationToken).ConfigureAwait(false);
        Volatile.Write(ref _offers, next);
        Changed?.Invoke(this, next);
    }
}
