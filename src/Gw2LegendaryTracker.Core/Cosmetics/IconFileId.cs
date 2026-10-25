using System.Globalization;

namespace Gw2LegendaryTracker.Core.Cosmetics;

/// <summary>
/// Icon URLs of the render service end in the file id of the icon inside the game archive, for example
/// <c>https://render.guildwars2.com/file/ABC123/61489.png</c>. That id is a possible anchor to tie API data to archive files.
/// </summary>
public static class IconFileId
{
    public static uint? TryParse(string? iconUrl)
    {
        if (string.IsNullOrWhiteSpace(iconUrl) || !Uri.TryCreate(iconUrl, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var name = Path.GetFileNameWithoutExtension(uri.AbsolutePath);
        return uint.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id != 0 ? id : null;
    }
}
