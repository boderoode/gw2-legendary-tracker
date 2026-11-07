namespace Gw2LegendaryTracker.Core.Settings;

public sealed record AppSettings
{
    public OverlaySettings Overlay { get; init; } = new();

    /// <summary>Identifier of the Legendary the user last selected, or <c>null</c> if none.</summary>
    public string? SelectedLegendaryId { get; init; }

    /// <summary>The main tab the user last opened (see <see cref="MainTabs"/>), or <c>null</c> for the first one.</summary>
    public string? SelectedMainTab { get; init; }
}

/// <summary>The main tabs of the window, by the name stored in <see cref="AppSettings.SelectedMainTab"/>.</summary>
public static class MainTabs
{
    public const string Legendaries = "legendaries";

    public const string Masteries = "masteries";

    public const string Account = "account";

    /// <summary>In the order the window shows them.</summary>
    public static IReadOnlyList<string> All { get; } = [Legendaries, Masteries, Account];

    /// <summary>The position of a stored tab; an unknown or missing name gives the first tab.</summary>
    public static int IndexOf(string? name) => name is null ? 0 : Math.Max(0, All.ToList().IndexOf(name));
}
