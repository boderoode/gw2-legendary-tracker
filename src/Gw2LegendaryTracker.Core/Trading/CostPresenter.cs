using System.Globalization;

namespace Gw2LegendaryTracker.Core.Trading;

public sealed record CostLineView(int ItemId, long Quantity, string UnitText, string TotalText);

/// <param name="CoinText">Empty when there is nothing to pay for.</param>
/// <param name="UnbuyableText"><c>null</c> when everything that is missing can be bought.</param>
public sealed record CostSummary(
    string Headline,
    string CoinText,
    IReadOnlyList<CostLineView> Lines,
    IReadOnlyList<string> Suggestions,
    string? UnbuyableText);

/// <summary>Turns a cost estimate into the texts the desktop app shows. Free of UI so it can be tested.</summary>
public static class CostPresenter
{
    public const string Disclaimer =
        "Dit is een schatting op basis van de laagste verkoopprijs. Bij grote aantallen stijgt de prijs, omdat de goedkoopste aanbiedingen opraken. Kosten en tijd van het craften zijn niet meegerekend.";

    private const int MaxUnbuyableNamed = 5;

    public static CostSummary Summarize(CostEstimate estimate, Func<int, string> itemName, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(estimate);
        ArgumentNullException.ThrowIfNull(itemName);
        culture ??= CultureInfo.CurrentCulture;

        string Coins(long copper) => CoinFormatter.Format(copper, culture);

        if (estimate.NothingToBuy)
        {
            return new CostSummary("Je hoeft niets meer te kopen.", string.Empty, [], [], null);
        }

        var headline = estimate.BuyLines.Count == 0
            ? "Wat je mist is niet op de Trading Post te koop."
            : $"Alles kopen wat je mist: {Coins(estimate.TotalInstant)} direct, of {Coins(estimate.TotalBuyOrder)} met kooporders.";

        var coin = estimate.BuyLines.Count == 0 ? string.Empty
            : estimate.Shortfall == 0 ? $"Je hebt genoeg goud ({Coins(estimate.CoinOwned)})."
            : $"Je hebt {Coins(estimate.CoinOwned)} en komt {Coins(estimate.Shortfall)} tekort.";

        var lines = estimate.BuyLines
            .Select(l => new CostLineView(l.ItemId, l.Quantity, $"{Coins(l.InstantUnitPrice)} per stuk", Coins(l.InstantTotal)))
            .ToList();

        var suggestions = estimate.Suggestions
            .Select(s => string.Create(
                culture,
                $"Koop {s.Quantity:N0}× {itemName(s.ItemId)} voor {Coins(s.BuyCost)} in plaats van te maken voor {Coins(s.CraftCost)}: dat bespaart {Coins(s.Savings)}."))
            .ToList();
        if (suggestions.Count > 1)
        {
            suggestions.Add($"Volg je alle tips, dan kost alles samen {Coins(estimate.TotalAfterSuggestions)} in plaats van {Coins(estimate.TotalInstant)}.");
        }

        return new CostSummary(headline, coin, lines, suggestions, DescribeUnbuyable(estimate, itemName, culture));
    }

    /// <summary>How current the prices are, and a warning when fresh prices could not be fetched.</summary>
    public static string PriceStatus(PriceBook book, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(book);
        culture ??= CultureInfo.CurrentCulture;

        if (book.OldestFetchedUtc is not { } oldest)
        {
            return book.Warning is null
                ? "Er zijn geen prijzen beschikbaar."
                : $"Prijzen konden niet worden opgehaald: {book.Warning}";
        }

        var text = $"Prijzen van {oldest.ToLocalTime().ToString("t", culture)}.";
        return book.Warning is null
            ? text
            : $"{text} Verversen is mislukt ({book.Warning}); dit zijn de laatst bekende prijzen.";
    }

    private static string? DescribeUnbuyable(CostEstimate estimate, Func<int, string> itemName, CultureInfo culture)
    {
        if (estimate.Unbuyable.Count == 0)
        {
            return null;
        }

        var named = estimate.Unbuyable.Take(MaxUnbuyableNamed)
            .Select(u => string.Create(culture, $"{u.Quantity:N0}× {itemName(u.ItemId)}"));
        var more = estimate.Unbuyable.Count - MaxUnbuyableNamed;
        return $"Niet te koop op de Trading Post: {string.Join(", ", named)}"
            + (more > 0 ? string.Create(culture, $" en nog {more} andere") : string.Empty)
            + ". Die moet je verzamelen of op een andere manier krijgen.";
    }
}
