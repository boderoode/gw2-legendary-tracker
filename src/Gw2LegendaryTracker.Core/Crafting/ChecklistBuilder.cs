namespace Gw2LegendaryTracker.Core.Crafting;

/// <summary>One line the user can tick off by hand.</summary>
/// <param name="Missing">What is still missing of this item (0 when it is crafted, covered or marked done).</param>
public sealed record ChecklistEntry(int ItemId, long Required, long Missing, bool IsCraftable, bool IsDone);

/// <summary>Lists the components of a plan that make sense to tick off by hand.</summary>
public static class ChecklistBuilder
{
    /// <summary>
    /// Every component below the goal that is crafted, still missing, or already marked done, once per item,
    /// in tree order. A marked item stays listed so the mark can be undone.
    /// </summary>
    public static IReadOnlyList<ChecklistEntry> Build(CraftPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var entries = new List<ChecklistEntry>();
        var seen = new HashSet<int>();

        foreach (var child in plan.Root.Children)
        {
            Walk(child);
        }

        return entries;

        void Walk(PlanNode node)
        {
            if (node.Key.Kind == MaterialKind.Item && (node.Recipe is not null || node.Missing > 0 || node.IsManual) && seen.Add(node.Key.Id))
            {
                entries.Add(new ChecklistEntry(node.Key.Id, node.Required, node.Missing, node.Recipe is not null, node.IsManual));
            }

            foreach (var child in node.Children)
            {
                Walk(child);
            }
        }
    }
}
