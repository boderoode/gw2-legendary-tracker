namespace Gw2LegendaryTracker.Core.Links;

/// <summary>Web pages that tell more about an item, for the "look it up" buttons.</summary>
public static class ItemLinks
{
    private static readonly string[] AllowedHosts = ["wiki.guildwars2.com", "en.gw2treasures.com"];

    /// <summary>
    /// The Guild Wars 2 wiki. The search goes straight to the page when the name matches a page title, and shows
    /// search results otherwise. Names are matched, not ids, so items that share a name may need a second click.
    /// </summary>
    public static string Wiki(string itemName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemName);
        return "https://wiki.guildwars2.com/wiki/Special:Search?search=" + Uri.EscapeDataString(itemName.Trim());
    }

    /// <summary>gw2treasures: the page of exactly this item id.</summary>
    public static string Treasures(int itemId) =>
        itemId > 0
            ? "https://en.gw2treasures.com/item/" + itemId.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : throw new ArgumentOutOfRangeException(nameof(itemId));

    /// <summary>True only for https links to the sites above, so a link can never start something else.</summary>
    public static bool IsSafe(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && AllowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);
}
