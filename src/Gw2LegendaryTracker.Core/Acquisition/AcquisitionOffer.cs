using Gw2LegendaryTracker.Core.Crafting;

namespace Gw2LegendaryTracker.Core.Acquisition;

/// <summary>
/// A way to buy an item from a vendor, as recorded by the user: <c>OutputCount</c> pieces for the given costs
/// (usually a currency such as karma). The planner treats it like a recipe, so currencies count against the wallet.
/// </summary>
public sealed record AcquisitionOffer(
    int Id,
    int ItemId,
    int OutputCount,
    IReadOnlyList<Ingredient> Costs,
    string VendorName,
    string Location)
{
    /// <summary>Recipe ids of offers are <c>-(RecipeIdBase + Id)</c>: below file recipes (small) and forge recipes (1,000,000+).</summary>
    public const int RecipeIdBase = 2_000_000;

    // A record compares lists by reference; two offers with the same costs must be equal.
    public bool Equals(AcquisitionOffer? other) =>
        other is not null
        && Id == other.Id
        && ItemId == other.ItemId
        && OutputCount == other.OutputCount
        && VendorName == other.VendorName
        && Location == other.Location
        && Costs.SequenceEqual(other.Costs);

    public override int GetHashCode() => HashCode.Combine(Id, ItemId, OutputCount, VendorName, Location, Costs.Count);

    public Recipe ToRecipe() => new(
        -(RecipeIdBase + Id),
        ItemId,
        OutputCount,
        Costs,
        [],
        RecipeSource.Local,
        Kind: RecipeKind.Vendor,
        Note: string.Join(", ", new[] { VendorName, Location }.Where(t => !string.IsNullOrWhiteSpace(t))));
}

/// <summary>What the user fills in; the service turns it into an <see cref="AcquisitionOffer"/> with an id.</summary>
public sealed record NewOffer(int ItemId, int OutputCount, IReadOnlyList<Ingredient> Costs, string VendorName, string Location);

public interface IAcquisitionStore
{
    Task<IReadOnlyList<AcquisitionOffer>> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Replaces the stored offers atomically, so a failed save keeps the previous list.</summary>
    Task SaveAsync(IReadOnlyList<AcquisitionOffer> offers, CancellationToken cancellationToken = default);
}
