namespace Gw2LegendaryTracker.Core.Gw2;

/// <summary>A permission the app can use but does not need: without it, <paramref name="Feature"/> works with less.</summary>
/// <param name="Reason">What the permission adds, in Dutch, for the screen.</param>
public sealed record OptionalScope(string Name, string Feature, string Reason);

/// <summary>
/// The API permissions the app asks for. <see cref="Required"/> is the minimum to connect at all; an
/// <see cref="Optional"/> permission only adds something to one feature. Anything beyond these is never requested.
/// </summary>
public static class ApiPermissions
{
    public const string Progression = "progression";

    public const string Unlocks = "unlocks";

    public static IReadOnlyList<string> Required { get; } = ["account", "inventories", "characters", "wallet"];

    public static IReadOnlyList<OptionalScope> Optional { get; } =
    [
        new(Progression, "Masteries", "Hiermee ziet de app welke masteries en achievements je al hebt, zodat stappen vanzelf worden afgevinkt."),
        new(Unlocks, "Fashion Studio", "Hiermee ziet de app welke skins en kleuren je al hebt."),
    ];

    /// <summary>The required permissions that <paramref name="granted"/> lacks. Optional ones are never reported here.</summary>
    public static IReadOnlyList<string> Missing(IEnumerable<string> granted)
    {
        ArgumentNullException.ThrowIfNull(granted);
        var set = granted.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Required.Where(p => !set.Contains(p)).ToList();
    }

    /// <summary>The optional permissions that <paramref name="granted"/> lacks.</summary>
    public static IReadOnlyList<OptionalScope> MissingOptional(IEnumerable<string> granted)
    {
        ArgumentNullException.ThrowIfNull(granted);
        var set = granted.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Optional.Where(p => !set.Contains(p.Name)).ToList();
    }

    public static bool Has(IEnumerable<string> granted, string permission)
    {
        ArgumentNullException.ThrowIfNull(granted);
        return granted.Contains(permission, StringComparer.OrdinalIgnoreCase);
    }
}
