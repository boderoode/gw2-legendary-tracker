using System.Data.Common;
using Gw2LegendaryTracker.Core.Gw2;

namespace Gw2LegendaryTracker.Core.Masteries;

/// <summary>
/// Fetches the account's mastery data as part of a sync. It never fails the sync: a missing "progression"
/// permission or a failed fetch only changes <see cref="Status"/>, and the last known data stays available.
/// </summary>
public sealed class MasteryProgressService
{
    private readonly IGw2ApiClient _api;
    private readonly IMasteryProgressStore _store;
    private readonly MasteryCatalog _catalog;
    private readonly Func<IReadOnlyCollection<int>> _achievementIds;
    private readonly TimeProvider _time;
    private MasteryAccountProgress? _progress;
    private MasterySyncStatus _status = MasterySyncStatus.Initial;

    /// <param name="achievementIds">The achievements to ask the API about: those the active plan uses.</param>
    public MasteryProgressService(
        IGw2ApiClient api,
        IMasteryProgressStore store,
        MasteryCatalog catalog,
        Func<IReadOnlyCollection<int>> achievementIds,
        TimeProvider? time = null)
    {
        _api = api;
        _store = store;
        _catalog = catalog;
        _achievementIds = achievementIds;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Raised after <see cref="Progress"/> or <see cref="Status"/> changed; may be raised on any thread.</summary>
    public event EventHandler? Changed;

    /// <summary>The account data from the last successful mastery sync, or <c>null</c>.</summary>
    public MasteryAccountProgress? Progress => Volatile.Read(ref _progress);

    public MasterySyncStatus Status => Volatile.Read(ref _status);

    /// <summary>Loads the stored account data and status, so they are visible while offline.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var progress = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
        var status = await _store.LoadStatusAsync(cancellationToken).ConfigureAwait(false);
        Volatile.Write(ref _progress, progress);
        Volatile.Write(ref _status, status);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Refreshes the mastery tracks when stale and, with the "progression" permission, the account's mastery data.</summary>
    /// <param name="permissions">The permissions of the key, from <c>/v2/tokeninfo</c>.</param>
    public async Task RefreshAsync(string apiKey, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        try
        {
            await _catalog.RefreshIfStaleAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is Gw2ApiException or IOException or InvalidOperationException or DbException)
        {
            // Public data; the stored tracks (if any) stay in use and the next sync tries again.
        }

        var lastSuccess = Status.LastSuccessUtc;
        if (!ApiPermissions.Has(permissions, ApiPermissions.Progression))
        {
            await ReportAsync(new MasterySyncStatus(MasterySyncState.NoPermission, lastSuccess), cancellationToken).ConfigureAwait(false);
            return;
        }

        MasteryAccountProgress progress;
        try
        {
            var masteries = await _api.GetAccountMasteriesAsync(apiKey, cancellationToken).ConfigureAwait(false);
            var points = await _api.GetAccountMasteryPointsAsync(apiKey, cancellationToken).ConfigureAwait(false);
            var ids = _achievementIds();
            var achievements = ids.Count == 0
                ? []
                : await _api.GetAccountAchievementsAsync(apiKey, ids, cancellationToken).ConfigureAwait(false);
            progress = new MasteryAccountProgress(_time.GetUtcNow(), masteries, points.Totals, points.Unlocked, achievements);
        }
        catch (Gw2ApiException ex)
        {
            var state = ex.Kind == Gw2ApiErrorKind.MissingPermissions ? MasterySyncState.NoPermission : MasterySyncState.Failed;
            await ReportAsync(new MasterySyncStatus(state, lastSuccess, ex.Message), cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            await _store.SaveAsync(progress, cancellationToken).ConfigureAwait(false);
            var status = new MasterySyncStatus(MasterySyncState.Succeeded, progress.CapturedUtc);
            await _store.SaveStatusAsync(status, cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _progress, progress);
            Volatile.Write(ref _status, status);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or DbException)
        {
            Volatile.Write(ref _status, new MasterySyncStatus(MasterySyncState.Failed, lastSuccess, "Opslaan van de mastery-gegevens is mislukt."));
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task ReportAsync(MasterySyncStatus status, CancellationToken cancellationToken)
    {
        Volatile.Write(ref _status, status);
        try
        {
            await _store.SaveStatusAsync(status, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or DbException)
        {
            // The status is shown from memory; storing it only matters after a restart.
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
