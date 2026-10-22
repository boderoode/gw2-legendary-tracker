using Gw2LegendaryTracker.Core.Crafting;
using Gw2LegendaryTracker.Core.Inventory;

namespace Gw2LegendaryTracker.Core.Goals;

/// <param name="Needed">What this goal uses of it in total: taken from stock plus still missing.</param>
/// <param name="Owned">What the account owns of it in total, also what other goals will use.</param>
/// <param name="Missing">What this goal still lacks after stock was shared in goal order.</param>
public sealed record MaterialRow(int ItemId, long Needed, long Owned, long Missing);

/// <summary>An amount of something the selected goal needs that a goal with higher priority already uses up.</summary>
public sealed record ReservationLine(MaterialKey Key, long Amount, int ForGoalItemId);

public sealed record CurrencyRow(int CurrencyId, long Needed, long Owned, long Missing);

public sealed record MaterialOverviewResult(
    IReadOnlyList<MaterialRow> Materials,
    IReadOnlyList<ReservationLine> Reservations,
    IReadOnlyList<CurrencyRow> Currencies);

/// <summary>The full material picture of one goal: everything it needs, what other goals hold, and currencies.</summary>
public static class MaterialOverview
{
    /// <param name="plans">All goal plans in priority order.</param>
    /// <param name="selectedIndex">The goal to describe.</param>
    public static MaterialOverviewResult Build(IReadOnlyList<GoalPlan> plans, int selectedIndex, InventoryModel inventory)
    {
        ArgumentNullException.ThrowIfNull(plans);
        ArgumentNullException.ThrowIfNull(inventory);
        if (selectedIndex < 0 || selectedIndex >= plans.Count || plans[selectedIndex].Plan is not { } plan)
        {
            return new MaterialOverviewResult([], [], []);
        }

        // Missing first (largest gap first), then what is merely covered.
        var materials = plan.Materials
            .Where(m => m.Key.Kind == MaterialKind.Item)
            .OrderByDescending(m => m.Missing)
            .ThenBy(m => m.Key.Id)
            .Select(m => new MaterialRow(m.Key.Id, m.Needed, inventory.CountOf(m.Key.Id), m.Missing))
            .ToList();

        var currencies = plan.Materials
            .Where(m => m.Key.Kind == MaterialKind.Currency)
            .OrderBy(m => m.Key.Id)
            .Select(m => new CurrencyRow(m.Key.Id, m.Needed, inventory.CurrencyAmount(m.Key.Id), m.Missing))
            .ToList();

        // Goals earlier in the list were planned first and took their share of the stock.
        var needed = plan.Materials.Select(m => m.Key).ToHashSet();
        var reservations = new List<ReservationLine>();
        for (var earlier = 0; earlier < selectedIndex; earlier++)
        {
            if (plans[earlier].Plan is not { } other)
            {
                continue;
            }

            reservations.AddRange(other.Materials
                .Where(m => m.UsedFromStock > 0 && needed.Contains(m.Key))
                .OrderBy(m => m.Key.Kind)
                .ThenBy(m => m.Key.Id)
                .Select(m => new ReservationLine(m.Key, m.UsedFromStock, plans[earlier].Goal.ItemId)));
        }

        return new MaterialOverviewResult(materials, reservations, currencies);
    }
}
