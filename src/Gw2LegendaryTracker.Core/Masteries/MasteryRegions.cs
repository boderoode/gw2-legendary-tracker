namespace Gw2LegendaryTracker.Core.Masteries;

/// <summary>
/// The API names one region in two ways: <c>/v2/masteries</c> uses a short code (<c>Maguuma</c>), while
/// <c>/v2/account/mastery/points</c> uses the expansion (<c>Heart of Thorns</c>). Plans use the code; people know the expansion.
/// </summary>
public static class MasteryRegions
{
    // In release order. A region the app does not know yet keeps the name the API gives it.
    private static readonly (string Code, string Expansion)[] Known =
    [
        ("Tyria", "Central Tyria"),
        ("Maguuma", "Heart of Thorns"),
        ("Desert", "Path of Fire"),
        ("Tundra", "Icebrood Saga"),
        ("Jade", "End of Dragons"),
        ("Sky", "Secrets of the Obscure"),
        ("Wild", "Janthir Wilds"),
        ("Magic", "Visions of Eternity"),
    ];

    /// <summary>The name to show for a region code, e.g. "Heart of Thorns" for <c>Maguuma</c>.</summary>
    public static string DisplayName(string region)
    {
        ArgumentNullException.ThrowIfNull(region);
        foreach (var (code, expansion) in Known)
        {
            if (string.Equals(code, region, StringComparison.OrdinalIgnoreCase))
            {
                return expansion;
            }
        }

        return region;
    }

    /// <summary>The region code of <c>/v2/masteries</c> for a region name from <c>/v2/account/mastery/points</c>.</summary>
    public static string CodeOf(string pointsRegion)
    {
        ArgumentNullException.ThrowIfNull(pointsRegion);
        foreach (var (code, expansion) in Known)
        {
            if (string.Equals(expansion, pointsRegion, StringComparison.OrdinalIgnoreCase))
            {
                return code;
            }
        }

        return pointsRegion;
    }
}
