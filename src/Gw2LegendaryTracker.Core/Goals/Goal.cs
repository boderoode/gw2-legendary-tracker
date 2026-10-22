namespace Gw2LegendaryTracker.Core.Goals;

/// <summary>A Legendary the user is working towards. The list order is the priority: earlier goals get stock first.</summary>
public sealed record Goal(int ItemId, int Quantity = 1, string Note = "")
{
    /// <summary>Items ticked off by hand for this goal.</summary>
    public IReadOnlySet<int> CompletedItemIds { get; init; } = new HashSet<int>();

    public bool Equals(Goal? other) =>
        other is not null
        && ItemId == other.ItemId
        && Quantity == other.Quantity
        && Note == other.Note
        && CompletedItemIds.SetEquals(other.CompletedItemIds);

    public override int GetHashCode() => HashCode.Combine(ItemId, Quantity, Note);
}
