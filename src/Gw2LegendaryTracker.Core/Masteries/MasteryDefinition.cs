namespace Gw2LegendaryTracker.Core.Masteries;

/// <summary>One level of a mastery track from <c>/v2/masteries</c>.</summary>
public sealed record MasteryLevelDefinition(string Name, int PointCost, long ExpCost);

/// <summary>A mastery track from <c>/v2/masteries</c> (public data).</summary>
/// <param name="Region">Exactly the region value the API uses, e.g. <c>Tyria</c> or <c>Maguuma</c>.</param>
public sealed record MasteryDefinition(int Id, string Name, string Region, int Order, IReadOnlyList<MasteryLevelDefinition> Levels)
{
    public bool Equals(MasteryDefinition? other) =>
        other is not null && Id == other.Id && Name == other.Name && Region == other.Region && Order == other.Order
        && Levels.SequenceEqual(other.Levels);

    public override int GetHashCode() => HashCode.Combine(Id, Name, Region, Order, Levels.Count);
}
