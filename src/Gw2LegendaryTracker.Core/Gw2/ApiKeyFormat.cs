using System.Text.RegularExpressions;

namespace Gw2LegendaryTracker.Core.Gw2;

public static partial class ApiKeyFormat
{
    /// <summary>Trims the input and checks it has the shape of an ArenaNet key (8-4-4-4-20-4-4-4-12 hex digits).</summary>
    public static bool TryNormalize(string? input, out string key)
    {
        key = input?.Trim() ?? string.Empty;
        return KeyPattern().IsMatch(key);
    }

    [GeneratedRegex("^[0-9A-Fa-f]{8}(?:-[0-9A-Fa-f]{4}){3}-[0-9A-Fa-f]{20}(?:-[0-9A-Fa-f]{4}){3}-[0-9A-Fa-f]{12}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();
}
