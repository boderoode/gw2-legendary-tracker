namespace Gw2LegendaryTracker.Core.Cosmetics;

public enum SkinCategory
{
    Armor,
    Weapon,
    Back,
    Gathering,
}

/// <summary>A wardrobe skin from the official API. Only metadata: the API has no 3D data, just a 2D icon.</summary>
public sealed record CosmeticSkin(
    int Id,
    string Name,
    string? IconUrl,
    SkinCategory Category,
    string Slot,
    string? WeightClass,
    string Rarity,
    IReadOnlyList<string> Races,
    int DyeSlotCount,
    string? Description)
{
    public bool Equals(CosmeticSkin? other) =>
        other is not null
        && Id == other.Id
        && Name == other.Name
        && IconUrl == other.IconUrl
        && Category == other.Category
        && Slot == other.Slot
        && WeightClass == other.WeightClass
        && Rarity == other.Rarity
        && DyeSlotCount == other.DyeSlotCount
        && Description == other.Description
        && Races.SequenceEqual(other.Races);

    public override int GetHashCode() => HashCode.Combine(Id, Name, Category, Slot);
}
