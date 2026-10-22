using System.Text.RegularExpressions;

namespace Gw2LegendaryTracker.Core.Crafting;

/// <summary>
/// Reads the recipe that the official API puts in the description of many gifts:
/// <c>Made by combining these items in the Mystic Forge: • 250 Vicious Fangs • …</c>.
/// Only that exact phrase counts: weapons also mention the Mystic Forge, but to say what they are used for.
/// </summary>
public static partial class MysticForgeDescription
{
    private const string Marker = "Made by combining these items in the Mystic Forge";

    /// <summary>The listed ingredients, or <c>null</c> when the description does not state how the item is made.</summary>
    public static IReadOnlyList<(int Count, string Name)>? TryParse(string? description)
    {
        if (string.IsNullOrEmpty(description))
        {
            return null;
        }

        var start = description.IndexOf(Marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return null;
        }

        var text = MarkupTag().Replace(description[(start + Marker.Length)..], string.Empty);
        var ingredients = new List<(int, string)>();
        foreach (var line in text.Split('\n'))
        {
            var match = Bullet().Match(line);
            if (match.Success)
            {
                var count = match.Groups["count"].Success ? int.Parse(match.Groups["count"].Value, System.Globalization.CultureInfo.InvariantCulture) : 1;
                ingredients.Add((count, match.Groups["name"].Value.Trim()));
            }
        }

        return ingredients.Count == 0 ? null : ingredients;
    }

    /// <summary>
    /// Makes plural and singular names comparable ("Vicious Fangs" and "Vicious Fang", "Globs of Ectoplasm" and
    /// "Glob of Ectoplasm"). Applied to both sides, so it only has to be consistent, not linguistically perfect.
    /// </summary>
    public static string Normalize(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var words = name.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words.Select(w => w.Length > 3 && w.EndsWith('s') ? w[..^1] : w));
    }

    [GeneratedRegex(@"^\s*[•\-\*]\s*(?:(?<count>\d+)\s+)?(?<name>.+?)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex Bullet();

    [GeneratedRegex("<[^>]*>", RegexOptions.CultureInvariant)]
    private static partial Regex MarkupTag();
}
