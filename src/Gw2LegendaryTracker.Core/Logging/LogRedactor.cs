using System.Text.RegularExpressions;

namespace Gw2LegendaryTracker.Core.Logging;

/// <summary>
/// Removes secrets and personal details from text before it is written to a log, so a log can be shared safely:
/// Guild Wars 2 API keys, gw2treasures keys, bearer tokens, key-like URL parameters and the Windows user name in paths.
/// </summary>
public static partial class LogRedactor
{
    public const string SecretMask = "[geheim]";

    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        // Order matters: the long Guild Wars 2 key contains GUID-shaped pieces and must be matched first.
        var result = BearerToken().Replace(text, "Bearer " + SecretMask);
        result = Gw2Key().Replace(result, SecretMask);
        result = Guid().Replace(result, SecretMask);
        result = KeyParameter().Replace(result, m => m.Groups["name"].Value + "=" + SecretMask);
        return UserProfilePath().Replace(result, "%USERPROFILE%");
    }

    [GeneratedRegex(@"Bearer\s+\S+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BearerToken();

    [GeneratedRegex(@"\b[0-9A-Fa-f]{8}(?:-[0-9A-Fa-f]{4}){3}-[0-9A-Fa-f]{20}(?:-[0-9A-Fa-f]{4}){3}-[0-9A-Fa-f]{12}\b", RegexOptions.CultureInvariant)]
    private static partial Regex Gw2Key();

    [GeneratedRegex(@"\b[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\b", RegexOptions.CultureInvariant)]
    private static partial Regex Guid();

    [GeneratedRegex(@"(?<name>access_token|api[_-]?key|token|key|authorization)=[^&\s""']+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex KeyParameter();

    [GeneratedRegex(@"[A-Za-z]:[\\/]+Users[\\/]+[^\\/\s""']+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UserProfilePath();
}
