using System.Globalization;
using Gw2LegendaryTracker.Core.Crafting;

namespace Gw2LegendaryTracker.Core.Goals;

public enum TreeState
{
    /// <summary>The stock covers it.</summary>
    Covered,

    /// <summary>Ticked off by hand.</summary>
    Manual,

    /// <summary>Can be crafted right now: every ingredient is in stock.</summary>
    Ready,

    /// <summary>Can be crafted once the parts below it are made; nothing is missing.</summary>
    Waiting,

    /// <summary>Something below it is still missing.</summary>
    Blocked,

    /// <summary>Cannot be crafted and is not (completely) in stock.</summary>
    Missing,
}

/// <param name="Path">Identifies the position in the tree (stable between recalculations), e.g. to remember what was expanded.</param>
public sealed record PlanTreeNode(
    MaterialKey Key,
    long Required,
    long Missing,
    TreeState State,
    string Path,
    IReadOnlyList<PlanTreeNode> Children)
{
    public string StatusText(CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        return State switch
        {
            TreeState.Covered => "Heb je",
            TreeState.Manual => "Zelf afgevinkt",
            TreeState.Ready => "Nu te maken",
            TreeState.Waiting => "Te maken na de onderdelen hieronder",
            TreeState.Blocked => "Nog niet te maken: onderdelen ontbreken",
            _ => string.Create(culture, $"Nog {Missing:N0} nodig"),
        };
    }
}

/// <summary>The crafting tree of a plan, below the goal itself, as plain data for the screens.</summary>
public static class PlanTree
{
    public static IReadOnlyList<PlanTreeNode> Build(CraftPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return [.. plan.Root.Children.Select((child, index) => Convert(child, string.Empty, index))];
    }

    private static PlanTreeNode Convert(PlanNode node, string parentPath, int index)
    {
        var path = $"{parentPath}/{node.Key.Kind}{node.Key.Id}#{index}";
        var children = node.Children.Select((child, i) => Convert(child, path, i)).ToList();
        return new PlanTreeNode(node.Key, node.Required, node.Missing, StateOf(node), path, children);
    }

    private static TreeState StateOf(PlanNode node)
    {
        if (node.IsManual)
        {
            return TreeState.Manual;
        }

        if (node.IsCoveredByStock)
        {
            return TreeState.Covered;
        }

        if (node.Recipe is null)
        {
            return TreeState.Missing;
        }

        if (node.Children.All(c => c.IsCoveredByStock))
        {
            return TreeState.Ready;
        }

        return node.IsSatisfied ? TreeState.Waiting : TreeState.Blocked;
    }
}
