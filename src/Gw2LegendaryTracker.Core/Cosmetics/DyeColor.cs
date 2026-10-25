using System.Globalization;

namespace Gw2LegendaryTracker.Core.Cosmetics;

/// <summary>A dye colour. The RGB value is the cloth rendering, which is what the game shows on most armor.</summary>
public sealed record DyeColor(int Id, string Name, int Red, int Green, int Blue, string? Hue, string? Material, string? Rarity)
{
    public string Hex => string.Create(CultureInfo.InvariantCulture, $"#{Red:X2}{Green:X2}{Blue:X2}");
}
