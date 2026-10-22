namespace Gw2LegendaryTracker.Core.Gw2;

/// <summary>The minimum set of API permissions the app needs. Anything beyond this is never requested.</summary>
public static class ApiPermissions
{
    public static IReadOnlyList<string> Required { get; } = ["account", "inventories", "characters", "wallet"];

    public static IReadOnlyList<string> Missing(IEnumerable<string> granted)
    {
        ArgumentNullException.ThrowIfNull(granted);
        var set = granted.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Required.Where(p => !set.Contains(p)).ToList();
    }
}
