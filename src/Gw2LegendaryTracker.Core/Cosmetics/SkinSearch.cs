namespace Gw2LegendaryTracker.Core.Cosmetics;

/// <summary>What the browser should show. Every field is optional; an empty query shows everything.</summary>
public sealed record SkinQuery(
    SkinCategory? Category = null,
    string? Slot = null,
    string Text = "",
    string? WeightClass = null,
    string? Race = null,
    bool OwnedOnly = false,
    LookSlot? ForSlot = null);

public static class SkinSearch
{
    /// <summary>
    /// Filters and sorts skins by name. <paramref name="owned"/> may be null when the unlocks are unknown;
    /// then <see cref="SkinQuery.OwnedOnly"/> is ignored instead of hiding everything.
    /// </summary>
    public static IReadOnlyList<CosmeticSkin> Apply(IEnumerable<CosmeticSkin> skins, SkinQuery query, IReadOnlySet<int>? owned)
    {
        ArgumentNullException.ThrowIfNull(skins);
        ArgumentNullException.ThrowIfNull(query);

        var text = query.Text.Trim();
        return [.. skins
            .Where(s => query.Category is null || s.Category == query.Category)
            .Where(s => query.Slot is null || string.Equals(s.Slot, query.Slot, StringComparison.OrdinalIgnoreCase))
            .Where(s => query.ForSlot is null || SkinSlots.Fits(s, query.ForSlot.Value))
            .Where(s => FitsCharacter(s, query.Race, query.WeightClass))
            .Where(s => !query.OwnedOnly || owned is null || owned.Contains(s.Id))
            .Where(s => text.Length == 0 || s.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(s => s.Id)];
    }

    /// <summary>Whether a character of this race and armor weight class can wear the skin. Null means "do not filter on this".</summary>
    public static bool FitsCharacter(CosmeticSkin skin, string? race, string? weightClass) =>
        FitsWeight(skin, weightClass) && FitsRace(skin, race);

    /// <summary>Clothing can be worn by every profession, so it is never filtered by weight class.</summary>
    private static bool FitsWeight(CosmeticSkin skin, string? weightClass) =>
        weightClass is null
        || skin.Category != SkinCategory.Armor
        || skin.WeightClass is null
        || string.Equals(skin.WeightClass, "Clothing", StringComparison.OrdinalIgnoreCase)
        || string.Equals(skin.WeightClass, weightClass, StringComparison.OrdinalIgnoreCase);

    /// <summary>A skin without a race restriction fits everyone.</summary>
    private static bool FitsRace(CosmeticSkin skin, string? race) =>
        race is null
        || skin.Races.Count == 0
        || skin.Races.Contains(race, StringComparer.OrdinalIgnoreCase);
}
