using System.Globalization;

namespace Gw2LegendaryTracker.Core.State;

/// <summary>Texts for the synchronisation status: a full one for the main window and a short one for the overlay.</summary>
public static class SyncStatusPresenter
{
    public static string LongText(SyncStatus status, bool hasApiKey, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(status);
        culture ??= CultureInfo.CurrentCulture;

        return status switch
        {
            { Phase: SyncPhase.Syncing } => "Synchroniseren…",
            { Phase: SyncPhase.Failed, Error: { } error } => $"Synchronisatie mislukt: {error}",
            { Phase: SyncPhase.Failed } => "Synchronisatie mislukt",
            { LastSuccessUtc: { } at } => $"Laatst gesynchroniseerd: {at.ToLocalTime().ToString("t", culture)}",
            _ when !hasApiKey => "Geen API-key ingesteld",
            _ => "Nog niet gesynchroniseerd",
        };
    }

    /// <summary>One short phrase that fits in a corner of the overlay; the full text belongs in a tooltip.</summary>
    public static string ShortText(SyncStatus status, bool hasApiKey, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(status);
        culture ??= CultureInfo.CurrentCulture;

        return status switch
        {
            { Phase: SyncPhase.Syncing } => "Bijwerken…",
            { Phase: SyncPhase.Failed } => "Bijwerken mislukt",
            { LastSuccessUtc: { } at } => $"Bijgewerkt {at.ToLocalTime().ToString("t", culture)}",
            _ when !hasApiKey => "Geen API-key",
            _ => "Nog niet bijgewerkt",
        };
    }
}
