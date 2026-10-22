using Gw2LegendaryTracker.Core.Gw2;
using Gw2LegendaryTracker.Core.State;
using Gw2LegendaryTracker.Core.Sync;

namespace Gw2LegendaryTracker.Tests;

public sealed class SyncServiceTests : IDisposable
{
    private readonly FakeApiClient _api = new();
    private readonly FakeApiKeyStore _keys = new() { Key = TestData.Key };
    private readonly FakeSnapshotStore _snapshots = new();
    private readonly AppState _state = new(new FakeSettingsStore(), new Gw2LegendaryTracker.Core.Settings.AppSettings());

    public void Dispose() => _state.Dispose();

    private SyncService CreateService(FakeSnapshotStore? snapshots = null) =>
        new(_api, _keys, snapshots ?? _snapshots, _state, new FixedTime(new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero)));

    [Fact]
    public async Task Successful_sync_stores_snapshot_and_reports_success()
    {
        await CreateService().SyncAsync();

        Assert.NotNull(_snapshots.Saved);
        Assert.Equal(SyncPhase.Succeeded, _state.SyncStatus.Phase);
        Assert.Equal(new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero), _state.SyncStatus.LastSuccessUtc);
    }

    [Fact]
    public async Task Without_key_status_is_idle_and_api_is_not_called()
    {
        _keys.Key = null;

        await CreateService().SyncAsync();

        Assert.Equal(SyncPhase.Idle, _state.SyncStatus.Phase);
        Assert.Equal(0, _api.SnapshotCalls);
    }

    [Fact]
    public async Task Api_failure_reports_failed_and_keeps_previous_success_time()
    {
        var service = CreateService();
        await service.SyncAsync();
        var firstSuccess = _state.SyncStatus.LastSuccessUtc;
        _api.Failure = new Gw2ApiException(Gw2ApiErrorKind.Offline, "Geen verbinding.");

        await service.SyncAsync();

        Assert.Equal(SyncPhase.Failed, _state.SyncStatus.Phase);
        Assert.Equal("Geen verbinding.", _state.SyncStatus.Error);
        Assert.Equal(firstSuccess, _state.SyncStatus.LastSuccessUtc);
        Assert.NotNull(_snapshots.Saved);
    }

    [Fact]
    public async Task Missing_permissions_fail_without_downloading()
    {
        _api.Token = new Gw2TokenInfo("id", "key", ["account"]);

        await CreateService().SyncAsync();

        Assert.Equal(SyncPhase.Failed, _state.SyncStatus.Phase);
        Assert.Equal(0, _api.SnapshotCalls);
        Assert.Contains("inventories", _state.SyncStatus.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Storage_failure_reports_failed()
    {
        await CreateService(new FakeSnapshotStore { SaveFailure = new IOException("disk full") }).SyncAsync();

        Assert.Equal(SyncPhase.Failed, _state.SyncStatus.Phase);
    }

    [Fact]
    public async Task Overlapping_syncs_run_only_once()
    {
        _api.Gate = new TaskCompletionSource();
        var service = CreateService();

        var first = service.SyncAsync();
        await service.SyncAsync();
        _api.Gate.SetResult();
        await first;

        Assert.Equal(1, _api.SnapshotCalls);
    }

    [Fact]
    public async Task Syncing_can_run_again_after_completion()
    {
        var service = CreateService();

        await service.SyncAsync();
        await service.SyncAsync();

        Assert.Equal(2, _api.SnapshotCalls);
    }

    [Fact]
    public async Task RestoreStatus_shows_last_sync_time_from_stored_snapshot()
    {
        _snapshots.Saved = TestData.Snapshot(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        await CreateService().RestoreStatusAsync();

        Assert.Equal(SyncPhase.Succeeded, _state.SyncStatus.Phase);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), _state.SyncStatus.LastSuccessUtc);
    }

    private sealed class FixedTime : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTime(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
