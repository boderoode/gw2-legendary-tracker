using Gw2LegendaryTracker.Core.Crafting;

namespace Gw2LegendaryTracker.Core.Trading;

/// <param name="InstantUnitPrice">Price per piece when buying right now.</param>
/// <param name="BuyOrderUnitPrice">Price per piece with a buy order (cheaper, but you have to wait for it to fill).</param>
public sealed record CostLine(int ItemId, long Quantity, long InstantUnitPrice, long InstantTotal, long BuyOrderUnitPrice, long BuyOrderTotal);

/// <summary>Something that is missing and cannot be bought on the Trading Post (account-bound, a reward, ...).</summary>
public sealed record UnbuyableLine(int ItemId, long Quantity);

/// <summary>Buying <paramref name="Quantity"/> of a craftable item is cheaper than crafting it from what is missing.</summary>
public sealed record BuyInsteadSuggestion(int ItemId, long Quantity, long BuyCost, long CraftCost)
{
    public long Savings => CraftCost - BuyCost;
}

/// <param name="TotalInstant">What it costs to buy every purchasable shortage right now. Unbuyable items are not in it.</param>
/// <param name="TotalBuyOrder">The same via buy orders.</param>
/// <param name="Shortfall">How much more coin is needed than is owned (0 when there is enough).</param>
/// <param name="TotalAfterSuggestions"><paramref name="TotalInstant"/> when every suggestion is followed.</param>
public sealed record CostEstimate(
    IReadOnlyList<CostLine> BuyLines,
    IReadOnlyList<UnbuyableLine> Unbuyable,
    long TotalInstant,
    long TotalBuyOrder,
    long CoinOwned,
    long Shortfall,
    IReadOnlyList<BuyInsteadSuggestion> Suggestions,
    long TotalAfterSuggestions)
{
    public bool NothingToBuy => BuyLines.Count == 0 && Unbuyable.Count == 0;
}

/// <summary>
/// Puts a price on a plan. It is an estimate: prices use the lowest offer, so buying large amounts costs more than
/// shown because the price rises as the cheapest offers sell out; crafting fees and time are not counted.
/// </summary>
public static class CostAnalyzer
{
    /// <summary>The items whose prices are needed to analyse the plan: what is missing, and what is crafted.</summary>
    public static IReadOnlySet<int> RelevantItemIds(CraftPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var ids = plan.Materials.Where(m => m.Key.Kind == MaterialKind.Item && m.Missing > 0).Select(m => m.Key.Id).ToHashSet();
        foreach (var child in plan.Root.Children)
        {
            Collect(child);
        }

        return ids;

        void Collect(PlanNode node)
        {
            if (node.Recipe is not null && node.Key.Kind == MaterialKind.Item)
            {
                ids.Add(node.Key.Id);
            }

            foreach (var child in node.Children)
            {
                Collect(child);
            }
        }
    }

    public static CostEstimate Analyze(CraftPlan plan, PriceBook prices, long coinOwned)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(prices);

        var lines = new List<CostLine>();
        var unbuyable = new List<UnbuyableLine>();
        foreach (var material in plan.Materials.Where(m => m.Key.Kind == MaterialKind.Item && m.Missing > 0))
        {
            var price = prices.Find(material.Key.Id);
            if (price is { IsPurchasable: true })
            {
                lines.Add(new CostLine(
                    material.Key.Id,
                    material.Missing,
                    price.InstantBuyPrice,
                    checked(material.Missing * price.InstantBuyPrice),
                    price.BuyOrderPrice,
                    checked(material.Missing * price.BuyOrderPrice)));
            }
            else
            {
                unbuyable.Add(new UnbuyableLine(material.Key.Id, material.Missing));
            }
        }

        var suggestions = new List<BuyInsteadSuggestion>();
        foreach (var child in plan.Root.Children)
        {
            Suggest(child, prices, suggestions);
        }

        var totalInstant = lines.Sum(l => l.InstantTotal);
        return new CostEstimate(
            [.. lines.OrderByDescending(l => l.InstantTotal).ThenBy(l => l.ItemId)],
            [.. unbuyable.OrderByDescending(u => u.Quantity).ThenBy(u => u.ItemId)],
            totalInstant,
            lines.Sum(l => l.BuyOrderTotal),
            coinOwned,
            Math.Max(0, totalInstant - coinOwned),
            [.. suggestions.OrderByDescending(s => s.Savings).ThenBy(s => s.ItemId)],
            totalInstant - suggestions.Sum(s => s.Savings));
    }

    /// <summary>
    /// Walks down from the top. A crafted part is suggested for purchase when buying it is cheaper than the missing
    /// components below it; the parts below a suggestion are not looked at, so suggestions never overlap.
    /// </summary>
    private static void Suggest(PlanNode node, PriceBook prices, List<BuyInsteadSuggestion> suggestions)
    {
        if (node.IsManual || node.IsCoveredByStock || node.Recipe is null || node.Key.Kind != MaterialKind.Item)
        {
            return;
        }

        var units = node.Required - node.FromStock;
        var price = prices.Find(node.Key.Id);
        if (price is { IsPurchasable: true } && SubtreeCost(node, prices) is { } craftCost)
        {
            var buyCost = checked(units * price.InstantBuyPrice);
            if (buyCost < craftCost)
            {
                suggestions.Add(new BuyInsteadSuggestion(node.Key.Id, units, buyCost, craftCost));
                return;
            }
        }

        foreach (var child in node.Children)
        {
            Suggest(child, prices, suggestions);
        }
    }

    /// <summary>What it costs to buy everything still missing below the node, or <c>null</c> if some of it cannot be bought.</summary>
    private static long? SubtreeCost(PlanNode node, PriceBook prices)
    {
        if (node.IsManual || node.IsCoveredByStock)
        {
            return 0;
        }

        if (node.Recipe is null)
        {
            if (node.Key.Kind != MaterialKind.Item || node.Missing <= 0)
            {
                return node.Missing <= 0 ? 0 : null;
            }

            return prices.Find(node.Key.Id) is { IsPurchasable: true } price ? checked(node.Missing * price.InstantBuyPrice) : null;
        }

        long total = 0;
        foreach (var child in node.Children)
        {
            if (SubtreeCost(child, prices) is not { } cost)
            {
                return null;
            }

            total = checked(total + cost);
        }

        return total;
    }
}
