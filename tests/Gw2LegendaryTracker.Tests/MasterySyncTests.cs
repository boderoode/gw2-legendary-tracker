using Gw2LegendaryTracker.Core.Gw2;
using Gw2LegendaryTracker.Core.Inventory;
using Gw2LegendaryTracker.Core.Masteries;
using Gw2LegendaryTracker.Core.Settings;
using Gw2LegendaryTracker.Core.State;
using Gw2LegendaryTracker.Core.Sync;

namespace Gw2LegendaryTracker.Tests;

public sealed class ApiPermissionsTests
{
    [Fact]
    public void Progression_and_unlocks_are_optional_not_required()
    {
        Assert.DoesNotContain(ApiPermissions.Progression, ApiPermissions.Required);
        Assert.DoesNotContain(ApiPermissions.Unlocks, ApiPermissions.Required);
        Assert.Contains(ApiPermissions.Optional, p => p.Name == ApiPermissions.Progression && p.Feature == "Masteries");
        Assert.Contains(ApiPermissions.Optional, p => p.Name == ApiPermissions.Unlocks);
    }

    [Fact]
    public void An_existing_key_without_progression_misses_nothing_required()
    {
        string[] granted = ["account", "inventories", "characters", "wallet"];

        Assert.Empty(ApiPermissions.Missing(granted));
        Assert.Equal([ApiPermissions.Progression, ApiPermissions.Unlocks], ApiPermissions.MissingOptional(granted).Select(p => p.Name));
        Assert.False(ApiPermissions.Has(granted, ApiPermissions.Progression));
    }

    [Fact]
    public void Permissions_are_compared_case_insensitively()
    {
        string[] granted = ["ACCOUNT", "Inventories", "characters", "wallet", "Progression"];

        Assert.Empty(ApiPermissions.Missing(granted));
        Assert.True(ApiPermissions.Has(granted, ApiPermissions.Progression));
        Assert.Equal([ApiPermissions.Unlocks], ApiPermissions.MissingOptional(granted).Select(p => p.Name));
    }

    [Fact]
    public async Task A_key_without_progression_connects_and_mentions_it()
    {
        var api = new FakeApiClient { Token = new Gw2TokenInfo("id", "key", ["account", "inventories", "characters", "wallet", "unlocks"]) };
        var keys = new FakeApiKeyStore();

        var result = await new ConnectionService(api, keys).ConnectAsync(TestData.Key);

        Assert.True(result.IsConnected);
        Assert.Equal(TestData.Key, keys.Key);
        Assert.Contains("'progression' (Masteries)", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_key_with_all_optional_permissions_gets_the_plain_message()
    {
        var api = new FakeApiClient { Token = new Gw2TokenInfo("id", "key", [.. ApiPermissions.Required, "progression", "unlocks"]) };

        var result = await new ConnectionService(api, new FakeApiKeyStore()).ConnectAsync(TestData.Key);

        Assert.Equal("Verbonden met je Guild Wars 2-account.", result.Message);
    }
}

public sealed class MasteryCatalogTests
{
    private readonly FakeApiClient _api = new() { Masteries = [.. MasteryTestData.Definitions.Values] };
    private readonly FakeMasteryDefinitionStore _store = new();
    private readonly ManualTime _time = new();

    private MasteryCatalog Create() => new(_api, _store, _time);

    [Fact]
    public async Task Without_a_stored_copy_the_tracks_are_fetched_and_stored()
    {
        var catalog = Create();

        await catalog.RefreshIfStaleAsync();

        Assert.Equal(1, _api.MasteryDefinitionCalls);
        Assert.Equal(3, _store.Stored.Count);
        Assert.Equal(_time.GetUtcNow(), _store.FetchedUtc);
        Assert.Equal("Skiff", catalog.Definitions![30].Name);
    }

    [Fact]
    public async Task A_fresh_copy_is_used_without_asking_the_api()
    {
        _store.Stored = [.. MasteryTestData.Definitions.Values];
        _store.FetchedUtc = _time.GetUtcNow().AddDays(-6);
        var catalog = Create();

        await catalog.RefreshIfStaleAsync();

        Assert.Equal(0, _api.MasteryDefinitionCalls);
        Assert.NotNull(catalog.Definitions);
    }

    [Fact]
    public async Task A_stale_copy_is_refreshed()
    {
        _store.FetchedUtc = _time.GetUtcNow().AddDays(-8);

        await Create().RefreshIfStaleAsync();

        Assert.Equal(1, _api.MasteryDefinitionCalls);
    }

    [Fact]
    public async Task An_empty_answer_keeps_the_stored_tracks()
    {
        _store.Stored = [.. MasteryTestData.Definitions.Values];
        _api.Masteries = [];
        var catalog = Create();

        await catalog.RefreshAsync();

        Assert.Equal(3, _store.Stored.Count);
        Assert.Equal(3, catalog.Definitions!.Count);
    }
}

public sealed class MasteryProgressSyncTests : IDisposable
{
    private static readonly string[] AllPermissions = [.. ApiPermissions.Required, ApiPermissions.Progression];

    private readonly FakeApiClient _api = new()
    {
        Token = new Gw2TokenInfo("id", "key", AllPermissions),
        Masteries = [.. MasteryTestData.Definitions.Values],
        AccountMasteries = [new AccountMastery(30, 1)],
        MasteryPoints = new AccountMasteryPoints([new RegionPoints("Jade", 3, 4)], [30]),
        Achievements = [new AchievementProgress(6000, true, 1, 1), new AchievementProgress(7000, true, 1, 1)],
    };

    private readonly FakeMasteryProgressStore _progressStore = new();
    private readonly ManualTime _time = new();
    private readonly AppState _state = new(new FakeSettingsStore(), new AppSettings());
    private readonly MasteryProgressService _masteries;

    public MasteryProgressSyncTests()
    {
        var catalog = new MasteryCatalog(_api, new FakeMasteryDefinitionStore(), _time);
        _masteries = new MasteryProgressService(_api, _progressStore, catalog, () => [6000], _time);
    }

    public void Dispose() => _state.Dispose();

    private SyncService CreateSync() =>
        new(_api, new FakeApiKeyStore { Key = TestData.Key }, new FakeSnapshotStore(), _state, new InventoryProvider(), _time, _masteries);

    [Fact]
    public async Task A_sync_also_stores_the_mastery_data()
    {
        await CreateSync().SyncAsync();

        Assert.Equal(SyncPhase.Succeeded, _state.SyncStatus.Phase);
        Assert.Equal(MasterySyncState.Succeeded, _masteries.Status.State);
        var saved = _progressStore.Saved!;
        Assert.Equal([new AccountMastery(30, 1)], saved.Masteries);
        Assert.Equal([new RegionPoints("Jade", 3, 4)], saved.Points);
        Assert.Equal([30], saved.Unlocked);
        Assert.Equal([new AchievementProgress(6000, true, 1, 1)], saved.Achievements);
        Assert.Equal([6000], Assert.Single(_api.AchievementRequests));
        Assert.Equal(_time.GetUtcNow(), _masteries.Status.LastSuccessUtc);
        Assert.Same(saved, _masteries.Progress);
    }

    [Fact]
    public async Task Without_progression_the_sync_succeeds_and_no_mastery_data_is_asked()
    {
        _api.Token = new Gw2TokenInfo("id", "key", [.. ApiPermissions.Required]);

        await CreateSync().SyncAsync();

        Assert.Equal(SyncPhase.Succeeded, _state.SyncStatus.Phase);
        Assert.Equal(MasterySyncState.NoPermission, _masteries.Status.State);
        Assert.Equal(0, _api.AccountMasteryCalls);
        Assert.Null(_progressStore.Saved);
        Assert.Equal(MasterySyncState.NoPermission, _progressStore.Status.State);
    }

    [Fact]
    public async Task A_failed_mastery_fetch_does_not_fail_the_sync_and_keeps_the_old_data()
    {
        var sync = CreateSync();
        await sync.SyncAsync();
        var first = _progressStore.Saved;
        _api.MasteryFailure = new Gw2ApiException(Gw2ApiErrorKind.ServerError, "Storing.");

        await sync.SyncAsync();

        Assert.Equal(SyncPhase.Succeeded, _state.SyncStatus.Phase);
        Assert.Equal(MasterySyncState.Failed, _masteries.Status.State);
        Assert.Equal("Storing.", _masteries.Status.Error);
        Assert.Same(first, _masteries.Progress);
        Assert.Equal(first!.CapturedUtc, _masteries.Status.LastSuccessUtc);
    }

    [Fact]
    public async Task A_revoked_progression_scope_is_reported_as_no_permission()
    {
        _api.MasteryFailure = new Gw2ApiException(Gw2ApiErrorKind.MissingPermissions, "De API-key mist een benodigd recht.");

        await CreateSync().SyncAsync();

        Assert.Equal(SyncPhase.Succeeded, _state.SyncStatus.Phase);
        Assert.Equal(MasterySyncState.NoPermission, _masteries.Status.State);
    }

    [Fact]
    public async Task Storing_the_mastery_data_failing_does_not_fail_the_sync()
    {
        _progressStore.SaveFailure = new IOException("schijf vol");

        await CreateSync().SyncAsync();

        Assert.Equal(SyncPhase.Succeeded, _state.SyncStatus.Phase);
        Assert.Equal(MasterySyncState.Failed, _masteries.Status.State);
        Assert.Null(_masteries.Progress);
    }

    [Fact]
    public async Task Offline_mastery_tracks_do_not_stop_the_account_data()
    {
        _api.MasteryDefinitionFailure = new Gw2ApiException(Gw2ApiErrorKind.Offline, "offline");

        await CreateSync().SyncAsync();

        Assert.Equal(MasterySyncState.Succeeded, _masteries.Status.State);
    }

    [Fact]
    public async Task Without_achievements_in_the_plan_none_are_asked()
    {
        var masteries = new MasteryProgressService(_api, _progressStore, new MasteryCatalog(_api, new FakeMasteryDefinitionStore(), _time), () => [], _time);

        await masteries.RefreshAsync(TestData.Key, AllPermissions);

        Assert.Empty(_api.AchievementRequests);
        Assert.Empty(_progressStore.Saved!.Achievements);
    }

    [Fact]
    public async Task The_stored_data_and_status_are_loaded_at_start_up()
    {
        _progressStore.Saved = MasteryAccountProgress.Empty(_time.GetUtcNow());
        _progressStore.Status = new MasterySyncStatus(MasterySyncState.Succeeded, _time.GetUtcNow());
        var changed = 0;
        _masteries.Changed += (_, _) => changed++;

        await _masteries.LoadAsync();

        Assert.Same(_progressStore.Saved, _masteries.Progress);
        Assert.Equal(MasterySyncState.Succeeded, _masteries.Status.State);
        Assert.Equal(1, changed);
    }
}
