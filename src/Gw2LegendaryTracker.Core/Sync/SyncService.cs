using Gw2LegendaryTracker.Core.Gw2;
using Gw2LegendaryTracker.Core.Inventory;
using Gw2LegendaryTracker.Core.Masteries;
using Gw2LegendaryTracker.Core.State;

namespace Gw2LegendaryTracker.Core.Sync;

/// <summary>
/// Reads the account from the API and stores the result. Shared by the main window and the overlay,
/// so a sync started in one is never repeated by the other while it is running.
/// </summary>
public sealed class SyncService
{
    private readonly IGw2ApiClient _api;
    private readonly IApiKeyStore _keys;
    private readonly ISnapshotStore _snapshots;
    private readonly AppState _state;
    private readonly InventoryProvider _inventory;
    private readonly TimeProvider _time;
    private readonly MasteryProgressService? _masteries;
    private int _running;

    /// <param name="masteries">Also fetches the mastery data on every sync; its failures never fail the sync itself.</param>
    public SyncService(
        IGw2ApiClient api,
        IApiKeyStore keys,
        ISnapshotStore snapshots,
        AppState state,
        InventoryProvider inventory,
        TimeProvider? time = null,
        MasteryProgressService? masteries = null)
    {
        _api = api;
        _keys = keys;
        _snapshots = snapshots;
        _state = state;
        _inventory = inventory;
        _time = time ?? TimeProvider.System;
        _masteries = masteries;
    }

    /// <summary>Restores the "last synced" time from the stored snapshot so it is visible while offline.</summary>
    public async Task RestoreStatusAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await _snapshots.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (snapshot is not null)
        {
            _inventory.Update(snapshot);
            _state.ReportSyncStatus(new SyncStatus(SyncPhase.Succeeded, snapshot.CapturedUtc));
        }
    }

    /// <summary>
    /// Fetches only the mastery data, e.g. after a new plan was imported that uses other achievements. Unlike
    /// <see cref="SyncAsync"/> it is never skipped: when a sync is busy with the masteries, this waits and then runs again.
    /// Problems are reported in the mastery status; the Legendary sync status is not touched.
    /// </summary>
    public async Task RefreshMasteriesAsync(CancellationToken cancellationToken = default)
    {
        if (_masteries is null)
        {
            return;
        }

        var key = await _keys.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (key is null)
        {
            return;
        }

        Gw2TokenInfo token;
        try
        {
            token = await _api.GetTokenInfoAsync(key, cancellationToken).ConfigureAwait(false);
        }
        catch (Gw2ApiException)
        {
            // Offline or an invalid key: the next full sync reports it.
            return;
        }

        await _masteries.RefreshAsync(key, token.Permissions, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Runs one synchronisation. Returns immediately when one is already running.</summary>
    public async Task SyncAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            return;
        }

        var previous = _state.SyncStatus;
        var lastSuccess = previous.LastSuccessUtc;
        try
        {
            var key = await _keys.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (key is null)
            {
                _state.ReportSyncStatus(new SyncStatus(SyncPhase.Idle, lastSuccess));
                return;
            }

            _state.ReportSyncStatus(new SyncStatus(SyncPhase.Syncing, lastSuccess));

            var token = await _api.GetTokenInfoAsync(key, cancellationToken).ConfigureAwait(false);
            var missing = ApiPermissions.Missing(token.Permissions);
            if (missing.Count > 0)
            {
                throw new Gw2ApiException(Gw2ApiErrorKind.MissingPermissions, $"De API-key mist rechten: {string.Join(", ", missing)}.");
            }

            var snapshot = await _api.GetAccountSnapshotAsync(key, _time.GetUtcNow(), cancellationToken).ConfigureAwait(false);
            await _snapshots.SaveAsync(snapshot, cancellationToken).ConfigureAwait(false);

            // Data is stored before anything observes it; the inventory is replaced before "success" is announced.
            _inventory.Update(snapshot);

            if (_masteries is not null)
            {
                // Reports its own problems (missing permission, failed fetch); the Legendary data above is already safe.
                await _masteries.RefreshAsync(key, token.Permissions, cancellationToken).ConfigureAwait(false);
            }

            _state.ReportSyncStatus(new SyncStatus(SyncPhase.Succeeded, snapshot.CapturedUtc));
        }
        catch (OperationCanceledException)
        {
            _state.ReportSyncStatus(previous);
            throw;
        }
        catch (Gw2ApiException ex)
        {
            _state.ReportSyncStatus(new SyncStatus(SyncPhase.Failed, lastSuccess, ex.Message));
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.Data.Common.DbException)
        {
            // Storing failed after a successful download; the previous snapshot is untouched.
            _state.ReportSyncStatus(new SyncStatus(SyncPhase.Failed, lastSuccess, "Opslaan van de gegevens is mislukt."));
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }
}
