namespace Gw2LegendaryTracker.Core.Trading;

/// <summary>
/// Trading Post prices of one item, in copper. In the API, <c>sells</c> are the offers you can buy from and
/// <c>buys</c> are the orders you would sell to; the names are from the seller's point of view.
/// </summary>
public sealed record TradingPostPrice(int ItemId, long BuyUnitPrice, long BuyQuantity, long SellUnitPrice, long SellQuantity)
{
    /// <summary>True when something is on offer, so the item can be bought now.</summary>
    public bool IsPurchasable => SellQuantity > 0 && SellUnitPrice > 0;

    /// <summary>What you pay when you buy right now: the lowest offer. 0 when nothing is on offer.</summary>
    public long InstantBuyPrice => IsPurchasable ? SellUnitPrice : 0;

    /// <summary>
    /// What you pay with a buy order that outbids the best one by 1 copper. An order at or above the lowest offer fills
    /// at once at the offer price, so it never costs more than <see cref="InstantBuyPrice"/>. 0 when nothing is on offer.
    /// </summary>
    public long BuyOrderPrice => !IsPurchasable ? 0 : BuyUnitPrice > 0 ? Math.Min(BuyUnitPrice + 1, SellUnitPrice) : SellUnitPrice;
}

/// <summary>The outcome of asking for an item: its price, or <c>null</c> when it is not on the Trading Post.</summary>
public sealed record PriceLookup(int ItemId, TradingPostPrice? Price, DateTimeOffset FetchedUtc);

/// <param name="OldestFetchedUtc">When the oldest price in the book was fetched, to tell the user how current it is.</param>
/// <param name="Warning">Set when fresh prices could not be fetched and older ones are shown.</param>
public sealed record PriceBook(IReadOnlyDictionary<int, TradingPostPrice> Prices, DateTimeOffset? OldestFetchedUtc, string? Warning)
{
    public static PriceBook Empty { get; } = new(new Dictionary<int, TradingPostPrice>(), null, null);

    public TradingPostPrice? Find(int itemId) => Prices.GetValueOrDefault(itemId);
}
