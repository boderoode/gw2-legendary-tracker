namespace Gw2LegendaryTracker.Core.Cosmetics;

/// <summary>Which skins can go in which slot of the studio.</summary>
public static class SkinSlots
{
    // Aquatic weapons (trident, speargun), bundles and toys have no place in the studio's two weapon slots.
    // The API uses "Spear" for aquatic and land spears alike, so both are offered.
    private static readonly HashSet<string> MainHandTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Axe", "Dagger", "Mace", "Pistol", "Scepter", "Sword", "Greatsword", "Hammer", "LongBow", "Rifle", "ShortBow", "Spear", "Staff",
    };

    private static readonly HashSet<string> OffHandTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Axe", "Dagger", "Mace", "Pistol", "Sword", "Focus", "Shield", "Torch", "Warhorn",
    };

    public static bool Fits(CosmeticSkin skin, LookSlot slot)
    {
        ArgumentNullException.ThrowIfNull(skin);

        return slot switch
        {
            LookSlot.Back => skin.Category == SkinCategory.Back,
            LookSlot.MainHand => skin.Category == SkinCategory.Weapon && MainHandTypes.Contains(skin.Slot),
            LookSlot.OffHand => skin.Category == SkinCategory.Weapon && OffHandTypes.Contains(skin.Slot),
            _ => skin.Category == SkinCategory.Armor && CharacterOptions.SlotOfArmor(skin.Slot) == slot,
        };
    }

    /// <summary>The slot a skin of this kind is meant for in a dialog title, e.g. "Helm" or "Hoofdhand".</summary>
    public static string Label(LookSlot slot) => slot switch
    {
        LookSlot.Helm => "Helm",
        LookSlot.Shoulders => "Schouders",
        LookSlot.Coat => "Borststuk",
        LookSlot.Gloves => "Handschoenen",
        LookSlot.Leggings => "Broek",
        LookSlot.Boots => "Laarzen",
        LookSlot.Back => "Rugstuk",
        LookSlot.MainHand => "Hoofdhand",
        LookSlot.OffHand => "Andere hand",
        _ => slot.ToString(),
    };
}
