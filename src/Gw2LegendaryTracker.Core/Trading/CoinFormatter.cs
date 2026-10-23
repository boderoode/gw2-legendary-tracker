using System.Globalization;

namespace Gw2LegendaryTracker.Core.Trading;

/// <summary>Formats copper as gold, silver and copper, the way the game shows it: <c>115g 88s 96c</c>.</summary>
public static class CoinFormatter
{
    /// <summary>The wallet currency "Coin"; its value is in copper.</summary>
    public const int CoinCurrencyId = 1;

    public static string Format(long copper, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;

        var sign = copper < 0 ? "-" : string.Empty;
        var value = copper == long.MinValue ? long.MaxValue : Math.Abs(copper);
        var gold = value / 10_000;
        var silver = value / 100 % 100;
        var rest = value % 100;

        var parts = new List<string>(3);
        if (gold > 0)
        {
            parts.Add(string.Create(culture, $"{gold:N0}g"));
        }

        if (silver > 0)
        {
            parts.Add(string.Create(culture, $"{silver}s"));
        }

        if (rest > 0 || parts.Count == 0)
        {
            parts.Add(string.Create(culture, $"{rest}c"));
        }

        return sign + string.Join(' ', parts);
    }
}
