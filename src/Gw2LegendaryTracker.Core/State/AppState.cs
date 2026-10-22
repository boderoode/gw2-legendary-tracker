using Gw2LegendaryTracker.Core.Settings;

namespace Gw2LegendaryTracker.Core.State;

/// <summary>
/// Single source of truth shared by the standalone window and the overlay.
/// Changes are persisted first; observers are notified only after the write succeeded.
/// </summary>
public sealed class AppState : IDisposable
{
    private readonly ISettingsStore _store;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AppSettings _settings;
    private SyncStatus _syncStatus = SyncStatus.Initial;

    public AppState(ISettingsStore store, AppSettings initial)
    {
        _store = store;
        _settings = initial with { Overlay = initial.Overlay.Normalize() };
    }

    public AppSettings Settings => Volatile.Read(ref _settings);

    public SyncStatus SyncStatus => Volatile.Read(ref _syncStatus);

    public event EventHandler<AppSettings>? SettingsChanged;

    public event EventHandler<SyncStatus>? SyncStatusChanged;

    public static async Task<AppState> LoadAsync(ISettingsStore store, CancellationToken cancellationToken = default)
        => new(store, await store.LoadAsync(cancellationToken).ConfigureAwait(false));

    /// <summary>Applies <paramref name="change"/> to the current settings, persists the result, then notifies observers.</summary>
    public async Task UpdateAsync(Func<AppSettings, AppSettings> change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = _settings;
            var next = change(current);
            next = next with { Overlay = next.Overlay.Normalize() };
            if (next == current)
            {
                return;
            }

            await _store.SaveAsync(next, cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _settings, next);
            SettingsChanged?.Invoke(this, next);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    public void ReportSyncStatus(SyncStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        Volatile.Write(ref _syncStatus, status);
        SyncStatusChanged?.Invoke(this, status);
    }
}
