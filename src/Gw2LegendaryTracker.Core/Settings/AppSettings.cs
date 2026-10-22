namespace Gw2LegendaryTracker.Core.Settings;

public sealed record AppSettings
{
    public OverlaySettings Overlay { get; init; } = new();

    /// <summary>Identifier of the Legendary the user last selected, or <c>null</c> if none.</summary>
    public string? SelectedLegendaryId { get; init; }
}
