using Gw2LegendaryTracker.Core.Export;
using Gw2LegendaryTracker.Core.Goals;
using Gw2LegendaryTracker.Core.Masteries;
using Gw2LegendaryTracker.Core.Settings;
using Gw2LegendaryTracker.Core.State;
using Gw2LegendaryTracker.Infrastructure.Persistence;

namespace Gw2LegendaryTracker.Tests;

public sealed class SqliteMasteryStoreTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Mastery_tracks_round_trip_with_their_levels_and_fetch_time()
    {
        using var temp = new TempDatabase();
        await temp.Database.MigrateAsync();
        var store = new SqliteMasteryDefinitionStore(temp.Database);

        Assert.Null(await store.GetFetchedUtcAsync());
        await store.SaveAsync([.. MasteryTestData.Definitions.Values], At);

        Assert.Equal(MasteryTestData.Definitions.Values.OrderBy(d => d.Id), await store.LoadAsync());
        Assert.Equal(At, await store.GetFetchedUtcAsync());
    }

    [Fact]
    public async Task Saving_tracks_replaces_the_previous_ones()
    {
        using var temp = new TempDatabase();
        await temp.Database.MigrateAsync();
        var store = new SqliteMasteryDefinitionStore(temp.Database);
        await store.SaveAsync([.. MasteryTestData.Definitions.Values], At);

        var only = new MasteryDefinition(99, "Nieuw", "Sky", 1, [new("Level", 2, 100)]);
        await store.SaveAsync([only], At.AddDays(7));

        Assert.Equal([only], await store.LoadAsync());
    }

    [Fact]
    public async Task Account_progress_and_status_round_trip()
    {
        using var temp = new TempDatabase();
        await temp.Database.MigrateAsync();
        var store = new SqliteMasteryProgressStore(temp.Database);
        var progress = new MasteryAccountProgress(
            At,
            [new AccountMastery(1, 2), new AccountMastery(30, 0)],
            [new RegionPoints("Jade", 3, 4), new RegionPoints("Tyria", 49, 50)],
            [1, 30],
            [new AchievementProgress(6000, true, 3, 3), new AchievementProgress(6001, false, 1, 5)]);

        Assert.Null(await store.LoadAsync());
        Assert.Equal(MasterySyncStatus.Initial, await store.LoadStatusAsync());

        await store.SaveAsync(progress);
        await store.SaveStatusAsync(new MasterySyncStatus(MasterySyncState.Failed, At, "Storing."));

        Assert.Equal(progress, await store.LoadAsync());
        Assert.Equal(new MasterySyncStatus(MasterySyncState.Failed, At, "Storing."), await store.LoadStatusAsync());
    }

    [Fact]
    public async Task Saving_progress_replaces_the_previous_data()
    {
        using var temp = new TempDatabase();
        await temp.Database.MigrateAsync();
        var store = new SqliteMasteryProgressStore(temp.Database);
        await store.SaveAsync(new MasteryAccountProgress(At, [new AccountMastery(1, 2)], [new RegionPoints("Jade", 1, 1)], [1], [new AchievementProgress(5, true, 1, 1)]));

        var later = MasteryAccountProgress.Empty(At.AddHours(1));
        await store.SaveAsync(later);

        Assert.Equal(later, await store.LoadAsync());
    }

    [Fact]
    public async Task The_plan_and_its_choices_round_trip_and_survive_reopening()
    {
        using var temp = new TempDatabase();
        await temp.Database.MigrateAsync();
        var store = new SqliteMasteryPlanStore(temp.Database);
        var plan = MasteryTestData.Plan();

        Assert.Null(await store.LoadAsync());
        await store.ReplaceAsync(plan, new Dictionary<string, bool> { ["s1"] = true, ["s3"] = false }, At);
        await temp.Database.MigrateAsync();

        var loaded = await new SqliteMasteryPlanStore(temp.Database).LoadAsync();
        Assert.NotNull(loaded);
        Assert.Equal(plan, loaded.Plan);
        Assert.Equal(At, loaded.ImportedUtc);
        Assert.Equal(new Dictionary<string, bool> { ["s1"] = true, ["s3"] = false }, loaded.Overrides);
    }

    [Fact]
    public async Task A_choice_can_be_set_changed_and_removed()
    {
        using var temp = new TempDatabase();
        await temp.Database.MigrateAsync();
        var store = new SqliteMasteryPlanStore(temp.Database);
        await store.ReplaceAsync(MasteryTestData.Plan(), new Dictionary<string, bool>(), At);

        await store.SetOverrideAsync("s1", true);
        await store.SetOverrideAsync("s1", false);
        await store.SetOverrideAsync("s2", true);
        await store.SetOverrideAsync("s2", null);

        Assert.Equal(new Dictionary<string, bool> { ["s1"] = false }, (await store.LoadAsync())!.Overrides);
    }

    [Fact]
    public async Task Replacing_the_plan_replaces_all_choices_and_clear_removes_everything()
    {
        using var temp = new TempDatabase();
        await temp.Database.MigrateAsync();
        var store = new SqliteMasteryPlanStore(temp.Database);
        await store.ReplaceAsync(MasteryTestData.Plan(), new Dictionary<string, bool> { ["s1"] = true, ["s2"] = true }, At);

        await store.ReplaceAsync(MasteryTestData.Plan(), new Dictionary<string, bool> { ["s2"] = false }, At.AddDays(1));
        Assert.Equal(new Dictionary<string, bool> { ["s2"] = false }, (await store.LoadAsync())!.Overrides);

        await store.ClearAsync();
        Assert.Null(await store.LoadAsync());
    }

    [Fact]
    public async Task A_version_9_database_is_migrated_without_losing_user_data()
    {
        using var temp = new TempDatabase();
        await using (var connection = await temp.Database.OpenAsync())
        {
            await using var command = connection.CreateCommand();
            command.CommandText = string.Join("\n", Migrations.All.Take(9)) + "\nPRAGMA user_version = 9;";
            await command.ExecuteNonQueryAsync();
        }

        await new SqliteGoalStore(temp.Database).SaveAsync([new Goal(30704, 1, "behouden")]);

        await temp.Database.MigrateAsync();

        Assert.Equal("behouden", Assert.Single(await new SqliteGoalStore(temp.Database).LoadAsync()).Note);
        Assert.Null(await new SqliteMasteryPlanStore(temp.Database).LoadAsync());
        await using (var connection = await temp.Database.OpenAsync())
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version;";
            Assert.Equal(10L, await command.ExecuteScalarAsync());
        }
    }

    [Fact]
    public async Task A_corrupt_stored_plan_is_reported_not_silently_dropped()
    {
        using var temp = new TempDatabase();
        await temp.Database.MigrateAsync();
        await using (var connection = await temp.Database.OpenAsync())
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO mastery_plan (id, plan_json, imported_utc) VALUES (1, 'kapot', '2026-11-07T12:00:00.0000000+00:00');";
            await command.ExecuteNonQueryAsync();
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => new SqliteMasteryPlanStore(temp.Database).LoadAsync());
    }
}

public sealed class MasteryPlanServiceTests
{
    private readonly InMemoryPlanStore _store = new();
    private readonly MasteryCatalog _catalog = new(new FakeApiClient(), new FakeMasteryDefinitionStore { Stored = [.. MasteryTestData.Definitions.Values] });
    private readonly MasteryPlanService _service;

    public MasteryPlanServiceTests() => _service = new MasteryPlanService(_store, _catalog, new ManualTime());

    [Fact]
    public async Task A_new_version_of_the_plan_keeps_the_choices_of_steps_that_still_exist()
    {
        await _service.ReplaceAsync(MasteryTestData.Plan());
        await _service.SetOverrideAsync("s1", true);
        await _service.SetOverrideAsync("s3", false);
        var node = MasteryTestData.Node();
        MasteryTestData.Step(node, 1, 0)["id"] = "s3-nieuw";
        MasteryTestData.Step(node, 1, 0)["requires"] = new System.Text.Json.Nodes.JsonArray();

        var preview = _service.Preview(node.ToJsonString());
        await _service.ReplaceAsync(preview.Result.Plan!);

        Assert.True(preview.Succeeded);
        Assert.Equal(2, preview.PhaseCount);
        Assert.Equal(3, preview.StepCount);
        Assert.Equal(1, preview.KeptChoices);
        Assert.Equal(1, preview.DroppedChoices);
        Assert.Equal(new Dictionary<string, bool> { ["s1"] = true }, _service.Overrides);
        Assert.Equal(_service.Overrides, _store.Stored!.Overrides);
    }

    [Fact]
    public void The_preview_changes_nothing()
    {
        var preview = _service.Preview(MasteryTestData.ValidJson);

        Assert.True(preview.Succeeded);
        Assert.Null(_service.Plan);
        Assert.Null(_store.Stored);
    }

    [Fact]
    public void An_invalid_file_gives_a_failed_preview()
    {
        var preview = _service.Preview("geen plan");

        Assert.False(preview.Succeeded);
        Assert.Equal(0, preview.StepCount);
    }

    [Fact]
    public async Task The_preview_checks_against_the_known_tracks()
    {
        await _catalog.LoadAsync();
        var node = MasteryTestData.Node();
        MasteryTestData.Step(node, 0, 0)["masteryId"] = 999;

        var preview = _service.Preview(node.ToJsonString());

        Assert.Null(preview.Result.Plan!.Phases[0].Steps[0].MasteryId);
    }

    [Fact]
    public async Task A_choice_for_an_unknown_step_is_ignored()
    {
        await _service.ReplaceAsync(MasteryTestData.Plan());

        await _service.SetOverrideAsync("bestaat-niet", true);

        Assert.Empty(_service.Overrides);
    }

    [Fact]
    public async Task Changes_are_announced_and_the_achievement_ids_follow_the_plan()
    {
        var changed = 0;
        _service.Changed += (_, _) => changed++;

        await _service.ReplaceAsync(MasteryTestData.Plan());
        await _service.SetOverrideAsync("s2", true);
        await _service.SetOverrideAsync("s2", null);

        Assert.Equal(3, changed);
        Assert.Equal([6000], _service.AchievementIds);
        Assert.Empty(_service.Overrides);
    }

    [Fact]
    public async Task Export_writes_the_plan_in_its_own_format_and_clear_removes_it()
    {
        Assert.Null(_service.Export());
        await _service.ReplaceAsync(MasteryTestData.Plan());

        var exported = _service.Export();
        await _service.ClearAsync();

        Assert.Equal(MasteryTestData.Plan(), MasteryPlanSerializer.Parse(exported, MasteryTestData.Definitions).Plan);
        Assert.Null(_service.Plan);
        Assert.Null(_store.Stored);
    }

    [Fact]
    public async Task The_stored_plan_is_loaded_at_start_up()
    {
        _store.Stored = new StoredMasteryPlan(MasteryTestData.Plan(), new Dictionary<string, bool> { ["s2"] = true }, DateTimeOffset.UnixEpoch);

        await _service.LoadAsync();

        Assert.Equal(MasteryTestData.Plan(), _service.Plan);
        Assert.True(_service.Overrides["s2"]);
        Assert.Equal(DateTimeOffset.UnixEpoch, _service.ImportedUtc);
    }

    private sealed class InMemoryPlanStore : IMasteryPlanStore
    {
        public StoredMasteryPlan? Stored { get; set; }

        public Task<StoredMasteryPlan?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Stored);

        public Task ReplaceAsync(MasteryPlan plan, IReadOnlyDictionary<string, bool> manualChoices, DateTimeOffset importedUtc, CancellationToken cancellationToken = default)
        {
            Stored = new StoredMasteryPlan(plan, new Dictionary<string, bool>(manualChoices), importedUtc);
            return Task.CompletedTask;
        }

        public Task SetOverrideAsync(string stepId, bool? done, CancellationToken cancellationToken = default)
        {
            var choices = new Dictionary<string, bool>(Stored!.Overrides);
            if (done is { } value)
            {
                choices[stepId] = value;
            }
            else
            {
                choices.Remove(stepId);
            }

            Stored = Stored with { Overrides = choices };
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            Stored = null;
            return Task.CompletedTask;
        }
    }
}

public sealed class MasteryExportTests : IDisposable
{
    private static readonly DateTimeOffset At = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly AppState _state = new(new FakeSettingsStore(), new AppSettings());

    public void Dispose() => _state.Dispose();

    private static MasteryPlanBackup Backup() => new(MasteryTestData.Plan(), new Dictionary<string, bool> { ["s1"] = true, ["s3"] = false });

    [Fact]
    public void A_version_2_export_carries_the_plan_and_its_choices()
    {
        var json = UserDataSerializer.Serialize(new AppSettings(), [], At, masteryPlan: Backup());

        var result = UserDataSerializer.Parse(json, MasteryTestData.Definitions);

        Assert.True(result.Succeeded, result.Error);
        Assert.Empty(result.Warnings);
        Assert.Contains("\"version\": 2", json, StringComparison.Ordinal);
        Assert.Contains("\"format\": \"gw2-mastery-plan\"", json, StringComparison.Ordinal);
        Assert.Equal(Backup().Plan, result.MasteryPlan!.Plan);
        Assert.Equal(Backup().ManualChoices, result.MasteryPlan.ManualChoices);
    }

    [Fact]
    public void A_version_1_export_is_still_read_and_keeps_the_current_plan()
    {
        var result = UserDataSerializer.Parse($$"""{"format":"{{UserDataSerializer.FormatName}}","version":1,"settings":{},"goals":[{"itemId":5}]}""");

        Assert.True(result.Succeeded, result.Error);
        Assert.Null(result.MasteryPlan);
        Assert.Single(result.Goals);
    }

    [Fact]
    public void An_unreadable_plan_is_skipped_with_a_warning_and_the_rest_is_imported()
    {
        var file = $$$"""
            {"format":"{{{UserDataSerializer.FormatName}}}","version":2,"settings":{},"goals":[{"itemId":5}],
             "masteryPlan":{"plan":{"format":"gw2-mastery-plan","version":1,"phases":[]},"manualChoices":[]}}
            """;

        var result = UserDataSerializer.Parse(file);

        Assert.True(result.Succeeded, result.Error);
        Assert.Null(result.MasteryPlan);
        Assert.Single(result.Goals);
        Assert.Contains(result.Warnings, w => w.Contains("masteryplan in het bestand is overgeslagen", StringComparison.Ordinal));
    }

    [Fact]
    public void Choices_for_unknown_steps_are_skipped_with_a_warning()
    {
        var json = UserDataSerializer.Serialize(new AppSettings(), [], At, masteryPlan: Backup())
            .Replace("\"stepId\": \"s3\"", "\"stepId\": \"weg\"", StringComparison.Ordinal);

        var result = UserDataSerializer.Parse(json, MasteryTestData.Definitions);

        Assert.Equal(new Dictionary<string, bool> { ["s1"] = true }, result.MasteryPlan!.ManualChoices);
        Assert.Contains(result.Warnings, w => w.Contains("afvinking", StringComparison.Ordinal));
    }

    [Fact]
    public void The_export_with_a_plan_holds_no_secrets()
    {
        var json = UserDataSerializer.Serialize(new AppSettings(), [], At, masteryPlan: Backup()).ToUpperInvariant();

        Assert.DoesNotContain("APIKEY", json, StringComparison.Ordinal);
        Assert.DoesNotContain("BEARER", json, StringComparison.Ordinal);
        Assert.DoesNotContain(TestData.Key.ToUpperInvariant(), json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_transfer_service_exports_and_imports_the_plan()
    {
        var catalog = new MasteryCatalog(new FakeApiClient(), new FakeMasteryDefinitionStore { Stored = [.. MasteryTestData.Definitions.Values] });
        await catalog.LoadAsync();
        using var goals = new GoalService(new FakeGoalStore());
        var source = new MasteryPlanService(new MemoryPlanStore(), catalog, new ManualTime());
        await source.ReplaceAsync(MasteryTestData.Plan());
        await source.SetOverrideAsync("s2", true);
        var exported = new UserDataTransferService(_state, goals, new ManualTime(), masteryPlans: source, masteryCatalog: catalog).Export();

        var target = new MasteryPlanService(new MemoryPlanStore(), catalog, new ManualTime());
        var outcome = await new UserDataTransferService(_state, goals, new ManualTime(), masteryPlans: target, masteryCatalog: catalog).ImportAsync(exported);

        Assert.True(outcome.Succeeded, outcome.Message);
        Assert.Contains("je masteryplan", outcome.Message, StringComparison.Ordinal);
        Assert.Equal(MasteryTestData.Plan(), target.Plan);
        Assert.Equal(new Dictionary<string, bool> { ["s2"] = true }, target.Overrides);
    }

    [Fact]
    public async Task Importing_a_file_without_a_plan_keeps_the_current_plan()
    {
        var catalog = new MasteryCatalog(new FakeApiClient(), new FakeMasteryDefinitionStore());
        using var goals = new GoalService(new FakeGoalStore());
        var plans = new MasteryPlanService(new MemoryPlanStore(), catalog, new ManualTime());
        await plans.ReplaceAsync(MasteryTestData.Plan());
        var transfer = new UserDataTransferService(_state, goals, new ManualTime(), masteryPlans: plans, masteryCatalog: catalog);

        var outcome = await transfer.ImportAsync(UserDataSerializer.Serialize(new AppSettings(), [], At));

        Assert.True(outcome.Succeeded, outcome.Message);
        Assert.Equal(MasteryTestData.Plan(), plans.Plan);
    }

    private sealed class MemoryPlanStore : IMasteryPlanStore
    {
        private StoredMasteryPlan? _stored;

        public Task<StoredMasteryPlan?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(_stored);

        public Task ReplaceAsync(MasteryPlan plan, IReadOnlyDictionary<string, bool> manualChoices, DateTimeOffset importedUtc, CancellationToken cancellationToken = default)
        {
            _stored = new StoredMasteryPlan(plan, manualChoices, importedUtc);
            return Task.CompletedTask;
        }

        public Task SetOverrideAsync(string stepId, bool? done, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            _stored = null;
            return Task.CompletedTask;
        }
    }
}
