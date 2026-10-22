namespace Gw2LegendaryTracker.Core.Crafting;

public sealed record CraftGoal(int ItemId, int Quantity = 1);

/// <summary>One way to make an item, as evaluated for a plan. Shown so the user can see and choose alternatives.</summary>
public sealed record RouteOption(Recipe Recipe, long MissingUnits, long CraftActions, bool IsChosen);

/// <summary>
/// One position in the crafting tree.
/// <para>
/// <see cref="Required"/> = <see cref="FromStock"/> + what must still be crafted or obtained.
/// A node is a leaf (<see cref="Recipe"/> is <c>null</c>) when it is covered by stock, cannot be crafted,
/// or would loop back on itself; only leaves carry <see cref="Missing"/>.
/// </para>
/// </summary>
public sealed record PlanNode(
    MaterialKey Key,
    long Required,
    long FromStock,
    long Missing,
    Recipe? Recipe,
    long Crafts,
    IReadOnlyList<PlanNode> Children,
    IReadOnlyList<RouteOption> Alternatives,
    int NodeCount,
    int SatisfiedCount,
    long MissingUnits,
    long CraftActions)
{
    /// <summary>True when the stock covers this position completely, so nothing below it is needed.</summary>
    public bool IsCoveredByStock => FromStock == Required;

    /// <summary>True when this position is covered, or can be crafted entirely from what is owned.</summary>
    public bool IsSatisfied => IsCoveredByStock || (Recipe is not null && Children.All(c => c.IsSatisfied));
}

/// <param name="IsReady">All ingredients are in stock right now, so this can be crafted immediately.</param>
public sealed record CraftStep(Recipe Recipe, long Times, bool IsReady);

public sealed record MaterialLine(MaterialKey Key, long UsedFromStock, long Missing)
{
    public long Needed => UsedFromStock + Missing;
}

public enum NextStepKind
{
    /// <summary>The goal is already owned.</summary>
    Done,

    /// <summary>Craft <see cref="NextStep.Craft"/>.</summary>
    Craft,

    /// <summary>Obtain <see cref="NextStep.Amount"/> of <see cref="NextStep.Material"/>.</summary>
    Gather,
}

public sealed record NextStep(NextStepKind Kind, CraftStep? Craft = null, MaterialKey? Material = null, long Amount = 0);

public sealed record CraftPlan(
    CraftGoal Goal,
    PlanNode Root,
    IReadOnlyList<MaterialLine> Materials,
    IReadOnlyList<CraftStep> Steps,
    NextStep Next)
{
    /// <summary>Materials still missing, largest gap first.</summary>
    public IEnumerable<MaterialLine> Shortages => Materials.Where(m => m.Missing > 0).OrderByDescending(m => m.Missing);

    /// <summary>
    /// Share of positions in the crafting tree that are satisfied: covered by what you own,
    /// or craftable entirely from what you own. 1.0 means you can finish the goal now.
    /// </summary>
    public double Progress => Root.NodeCount == 0 ? 1 : (double)Root.SatisfiedCount / Root.NodeCount;
}

/// <summary>Raised when a crafting tree is too large to evaluate.</summary>
public sealed class CraftingPlanException : Exception
{
    public CraftingPlanException(string message)
        : base(message)
    {
    }
}
