using System.Globalization;
using System.Text.Json.Nodes;
using Gw2LegendaryTracker.Core.Export;
using Gw2LegendaryTracker.Core.Gw2;
using Gw2LegendaryTracker.Core.Inventory;
using Gw2LegendaryTracker.Core.Masteries;
using Gw2LegendaryTracker.Core.Settings;
using Gw2LegendaryTracker.Core.State;
using Gw2LegendaryTracker.Core.Sync;
using Gw2LegendaryTracker.Infrastructure.Persistence;

namespace Gw2LegendaryTracker.Tests;

/// <summary>Edge cases: huge estimates, quick ticks, plans near the size limit, permissions that disappear, overlapping refreshes.</summary>
public sealed class MasteryRobustnessTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void An_unrealistic_estimate_is_left_out_and_the_summary_never_overflows()
    {
        var node = MasteryTestData.Node();
        MasteryTestData.Step(node, 0, 0)["estimatedMinutes"] = 2_000_000_000;
        MasteryTestData.Step(node, 0, 1)["estimatedMinutes"] = MasteryPlanSerializer.MaxEstimatedMinutes;
        MasteryTestData.Step(node, 1, 0)["estimatedMinutes"] = MasteryPlanSerializer.MaxEstimatedMinutes;

        var result = MasteryPlanSerializer.Parse(node.ToJsonString(), MasteryTestData.Definitions);
        var plan = result.Plan!;
        var summary = MasteryPlanPresenter.Summarize(
            plan,
            MasteryStatusCalculator.Compute(plan, null, MasteryTestData.Definitions, new Dictionary<string, bool>()),
            CultureInfo.GetCultureInfo("nl-NL"));

        Assert.Null(plan.Phases[0].Steps[0].EstimatedMinutes);
        Assert.Contains(result.Warnings, w => w.Contains("onrealistisch hoge", StringComparison.Ordinal));
        Assert.StartsWith("Nog ongeveer 2.000 u", summary.RemainingText, StringComparison.Ordinal);
    }

    [Fact]
    public void A_plan_that_would_grow_past_1_MB_when_stored_is_refused()
    {
        // Minimal steps just under 1 MB as sent; the app writes every other field out as well (about 230 bytes per step).
        var steps = new JsonArray();
        for (var i = 0; i < MasteryPlanSerializer.MaxSteps; i++)
        {
            steps.Add(new JsonObject
            {
                ["id"] = $"s{i}",
                ["title"] = "t",
                ["description"] = new string('d', MasteryPlanSerializer.MaxLongText),
                ["notes"] = new string('n', 1_270),
            });
        }

        var node = new JsonObject
        {
            ["format"] = MasteryPlanSerializer.FormatName,
            ["version"] = 1,
            ["phases"] = new JsonArray(new JsonObject { ["id"] = "p", ["title"] = "p", ["steps"] = steps }),
        };
        var text = node.ToJsonString();

        var result = MasteryPlanSerializer.Parse(text, MasteryTestData.Definitions);

        Assert.True(System.Text.Encoding.UTF8.GetByteCount(text) <= MasteryPlanSerializer.MaxBytes);
        Assert.False(result.Succeeded);
        Assert.Contains("zodra de app het opslaat", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_accepted_plan_can_be_exported_and_imported_again()
    {
        var steps = new JsonArray();
        for (var i = 0; i < MasteryPlanSerializer.MaxSteps; i++)
        {
            steps.Add(new JsonObject { ["id"] = $"s{i}", ["title"] = "Stap met een é", ["description"] = new string('é', 1_200) });
        }

        var node = new JsonObject
        {
            ["format"] = MasteryPlanSerializer.FormatName,
            ["version"] = 1,
            ["phases"] = new JsonArray(new JsonObject { ["id"] = "p", ["title"] = "p", ["steps"] = steps }),
        };

        // Sent the way an AI writes it: accents as is (about 0.75 MB), not escaped.
        var text = node.ToJsonString(new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        var parsed = MasteryPlanSerializer.Parse(text, MasteryTestData.Definitions);
        Assert.True(parsed.Succeeded, parsed.Error);
        var plan = parsed.Plan!;

        var exported = MasteryPlanSerializer.Serialize(plan);
        var userData = UserDataSerializer.Serialize(new AppSettings(), [], At, masteryPlan: new MasteryPlanBackup(plan, new Dictionary<string, bool>()));

        Assert.Equal(plan, MasteryPlanSerializer.Parse(exported, MasteryTestData.Definitions).Plan);
        Assert.Equal(plan, UserDataSerializer.Parse(userData, MasteryTestData.Definitions).MasteryPlan?.Plan);
    }

    [Fact]
    public async Task A_stored_plan_is_read_back_whatever_its_size()
    {
        using var temp = new TempDatabase();
        await temp.Database.MigrateAsync();
        var store = new SqliteMasteryPlanStore(temp.Database);
        var steps = Enumerable.Range(0, MasteryPlanSerializer.MaxSteps)
            .Select(i => new MasteryStep($"s{i}", "t", new string('d', MasteryPlanSerializer.MaxLongText), MasteryStepType.Other, null, null, null, null, null, null, null, MasteryPriority.Normal, [], [], new string('n', MasteryPlanSerializer.MaxLongText)))
            .ToList();
        var big = new MasteryPlan("Groot", null, string.Empty, string.Empty, [], [new MasteryPhase("p", "p", string.Empty, steps)]);

        await store.ReplaceAsync(big, new Dictionary<string, bool>(), At);

        Assert.Equal(big, (await store.LoadAsync())!.Plan);
    }

    [Fact]
    public async Task Quick_ticks_one_after_the_other_are_all_kept()
    {
        var store = new SlowPlanStore();
        using var service = new MasteryPlanService(store, new MasteryCatalog(new FakeApiClient(), new FakeMasteryDefinitionStore()), new ManualTime());
        await service.ReplaceAsync(MasteryTestData.Plan());
        store.Slow = new TaskCompletionSource();

        var first = service.SetOverrideAsync("s1", true);
        var second = service.SetOverrideAsync("s2", true);
        store.Slow.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(new Dictionary<string, bool> { ["s1"] = true, ["s2"] = true }, service.Overrides);
        Assert.Equal(service.Overrides, store.Choices);
    }

    [Fact]
    public async Task Replacing_the_plan_announces_it_for_both_ways_of_replacing()
    {
        using var service = new MasteryPlanService(new SlowPlanStore(), new MasteryCatalog(new FakeApiClient(), new FakeMasteryDefinitionStore()), new ManualTime());
        var replaced = 0;
        service.PlanReplaced += (_, _) => replaced++;

        await service.ReplaceAsync(MasteryTestData.Plan());
        await service.ReplaceAsync(MasteryTestData.Plan(), new Dictionary<string, bool> { ["s1"] = true });
        await service.SetOverrideAsync("s2", true);

        Assert.Equal(2, replaced);
    }

    [Fact]
    public async Task Without_progression_old_account_data_is_not_used()
    {
        var store = new FakeMasteryProgressStore { Saved = new MasteryAccountProgress(At, [new AccountMastery(30, 1)], [], [], []) };
        var api = new FakeApiClient();
        using var service = new MasteryProgressService(api, store, new MasteryCatalog(api, new FakeMasteryDefinitionStore()), () => [], new ManualTime());
        await service.LoadAsync();
        Assert.NotNull(service.Progress);

        await service.RefreshAsync(TestData.Key, [.. ApiPermissions.Required]);

        Assert.Equal(MasterySyncState.NoPermission, service.Status.State);
        Assert.Null(service.Progress);
        Assert.DoesNotContain("\"trainedLevels\": 2", MasteryPromptBuilder.Build(service.Progress, MasteryTestData.Definitions), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Overlapping_catalog_refreshes_download_the_tracks_once()
    {
        var api = new FakeApiClient { Masteries = [.. MasteryTestData.Definitions.Values], MasteryDefinitionGate = new TaskCompletionSource() };
        var catalog = new MasteryCatalog(api, new FakeMasteryDefinitionStore(), new ManualTime());

        var first = catalog.RefreshIfStaleAsync();
        var second = catalog.RefreshIfStaleAsync();
        api.MasteryDefinitionGate.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(1, api.MasteryDefinitionCalls);
        Assert.NotNull(catalog.Definitions);
    }

    [Fact]
    public async Task A_mastery_refresh_asks_for_the_achievements_of_the_plan_that_is_active_now()
    {
        var api = new FakeApiClient { Token = new Gw2TokenInfo("id", "key", [.. ApiPermissions.Required, ApiPermissions.Progression]) };
        IReadOnlyCollection<int> planAchievements = [1];
        using var masteries = new MasteryProgressService(api, new FakeMasteryProgressStore(), new MasteryCatalog(api, new FakeMasteryDefinitionStore()), () => planAchievements, new ManualTime());
        using var state = new AppState(new FakeSettingsStore(), new AppSettings());
        var sync = new SyncService(api, new FakeApiKeyStore { Key = TestData.Key }, new FakeSnapshotStore(), state, new InventoryProvider(), new ManualTime(), masteries);

        planAchievements = [6000, 6001];
        await sync.RefreshMasteriesAsync();

        Assert.Equal([6000, 6001], Assert.Single(api.AchievementRequests));
        Assert.Equal(0, api.SnapshotCalls);
        Assert.Equal(SyncPhase.Idle, state.SyncStatus.Phase);
    }

    [Fact]
    public async Task A_mastery_refresh_without_a_key_does_nothing()
    {
        var api = new FakeApiClient();
        using var masteries = new MasteryProgressService(api, new FakeMasteryProgressStore(), new MasteryCatalog(api, new FakeMasteryDefinitionStore()), () => [1], new ManualTime());
        using var state = new AppState(new FakeSettingsStore(), new AppSettings());
        var sync = new SyncService(api, new FakeApiKeyStore(), new FakeSnapshotStore(), state, new InventoryProvider(), new ManualTime(), masteries);

        await sync.RefreshMasteriesAsync();

        Assert.Equal(0, api.AccountMasteryCalls);
        Assert.Equal(MasterySyncState.Never, masteries.Status.State);
    }

    /// <summary>A plan store whose writes can be held back, to let two changes overlap.</summary>
    private sealed class SlowPlanStore : IMasteryPlanStore
    {
        private MasteryPlan? _plan;

        public TaskCompletionSource? Slow { get; set; }

        public Dictionary<string, bool> Choices { get; private set; } = [];

        public Task<StoredMasteryPlan?> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_plan is null ? null : new StoredMasteryPlan(_plan, Choices, At));

        public Task ReplaceAsync(MasteryPlan plan, IReadOnlyDictionary<string, bool> manualChoices, DateTimeOffset importedUtc, CancellationToken cancellationToken = default)
        {
            _plan = plan;
            Choices = new Dictionary<string, bool>(manualChoices);
            return Task.CompletedTask;
        }

        public async Task SetOverrideAsync(string stepId, bool? done, CancellationToken cancellationToken = default)
        {
            if (Slow is not null)
            {
                await Slow.Task;
            }

            if (done is { } value)
            {
                Choices[stepId] = value;
            }
            else
            {
                Choices.Remove(stepId);
            }
        }

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            _plan = null;
            Choices = [];
            return Task.CompletedTask;
        }
    }
}
