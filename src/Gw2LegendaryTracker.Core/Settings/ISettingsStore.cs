namespace Gw2LegendaryTracker.Core.Settings;

public interface ISettingsStore
{
    /// <summary>Loads persisted settings, or defaults when nothing has been saved or the stored data is unreadable.</summary>
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
