using Gw2LegendaryTracker.Core.Export;
using Gw2LegendaryTracker.Core.Goals;
using Gw2LegendaryTracker.Core.Settings;
using Gw2LegendaryTracker.Core.State;

namespace Gw2LegendaryTracker.Tests;

public class UserDataSerializerTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    private static AppSettings SampleSettings() => new()
    {
        SelectedLegendaryId = "30704",
        Overlay = new OverlaySettings
        {
            Bounds = new OverlayBounds(10, 20, 300, 230),
            Opacity = 0.6,
            IsExpanded = true,
            IsLocked = true,
            VisibleMaterialCount = 8,
            ShowOnlyMissing = false,
            AutoHideSeconds = 15,
            ToggleHotkey = new HotkeyGesture(HotkeyModifiers.Shift | HotkeyModifiers.Alt, 0x4F),
        },
    };

    private static Goal[] SampleGoals() =>
    [
        new Goal(30704, 1, "eerst deze") { CompletedItemIds = new HashSet<int> { 19672, 19626 } },
        new Goal(30698, 2),
    ];

    [Fact]
    public void Export_then_import_gives_back_the_same_data_in_the_same_order()
    {
        var json = UserDataSerializer.Serialize(SampleSettings(), SampleGoals(), At);

        var result = UserDataSerializer.Parse(json);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Warnings);
        Assert.Equal(SampleSettings(), result.Settings);
        Assert.Equal(SampleGoals(), result.Goals);
        Assert.Equal([30704, 30698], result.Goals.Select(g => g.ItemId));
    }

    [Fact]
    public void The_export_is_deterministic_and_names_its_format()
    {
        var first = UserDataSerializer.Serialize(SampleSettings(), SampleGoals(), At);
        var second = UserDataSerializer.Serialize(SampleSettings(), SampleGoals(), At);

        Assert.Equal(first, second);
        Assert.Contains(UserDataSerializer.FormatName, first, StringComparison.Ordinal);
        Assert.Contains("19626,", first.Replace("\n", string.Empty).Replace(" ", string.Empty), StringComparison.Ordinal);
    }

    [Fact]
    public void The_export_holds_no_secrets()
    {
        var json = UserDataSerializer.Serialize(SampleSettings(), SampleGoals(), At).ToUpperInvariant();

        Assert.DoesNotContain("APIKEY", json, StringComparison.Ordinal);
        Assert.DoesNotContain("TOKEN", json, StringComparison.Ordinal);
        Assert.DoesNotContain("PASSWORD", json, StringComparison.Ordinal);
        Assert.DoesNotContain("BEARER", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("[1,2,3]")]
    [InlineData("{}")]
    [InlineData("""{"format":"something-else","version":1}""")]
    [InlineData("""{"format":"gw2-legendary-tracker-export","version":0}""")]
    [InlineData("""{"format":"gw2-legendary-tracker-export"}""")]
    [InlineData("""{"format":"gw2-legendary-tracker-export","version":1,"goals":[{"itemId":"text"}]}""")]
    public void Files_that_are_not_valid_exports_are_refused_as_a_whole(string? json)
    {
        var result = UserDataSerializer.Parse(json);

        Assert.False(result.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.Empty(result.Goals);
        Assert.Null(result.Settings);
    }

    [Fact]
    public void A_file_from_a_newer_version_is_refused_with_advice_to_update()
    {
        var result = UserDataSerializer.Parse($$"""{"format":"{{UserDataSerializer.FormatName}}","version":{{UserDataSerializer.CurrentVersion + 1}}}""");

        Assert.False(result.Succeeded);
        Assert.Contains("bij", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Oversized_files_are_refused()
    {
        var result = UserDataSerializer.Parse(new string(' ', UserDataSerializer.MaxBytes + 1));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Bad_goals_are_skipped_and_reported_while_good_ones_are_kept()
    {
        var result = UserDataSerializer.Parse($$"""
            {"format":"{{UserDataSerializer.FormatName}}","version":1,"settings":{},
             "goals":[
               {"itemId":5,"quantity":1},
               {"itemId":0},
               {"itemId":-3},
               null,
               {"itemId":5,"quantity":9},
               {"itemId":7,"quantity":-2,"completedItemIds":[3,0,-1,3]}
             ]}
            """);

        Assert.True(result.Succeeded);
        Assert.Equal([5, 7], result.Goals.Select(g => g.ItemId));
        Assert.Equal(1, result.Goals[1].Quantity);
        Assert.Equal([3], result.Goals[1].CompletedItemIds);
        Assert.Equal(4, result.Warnings.Count);
        Assert.Contains(result.Warnings, w => w.Contains("al eerder", StringComparison.Ordinal));
    }

    [Fact]
    public void Too_long_notes_are_shortened_with_a_warning()
    {
        var note = new string('x', GoalService.MaxNoteLength + 10);
        var json = UserDataSerializer.Serialize(new AppSettings(), [new Goal(1, 1, note)], At);

        var result = UserDataSerializer.Parse(json);

        Assert.Equal(GoalService.MaxNoteLength, result.Goals[0].Note.Length);
        Assert.Contains(result.Warnings, w => w.Contains("ingekort", StringComparison.Ordinal));
    }

    [Fact]
    public void The_number_of_goals_is_capped()
    {
        var goals = Enumerable.Range(1, UserDataSerializer.MaxGoals + 5).Select(i => new Goal(i)).ToList();
        var result = UserDataSerializer.Parse(UserDataSerializer.Serialize(new AppSettings(), goals, At));

        Assert.Equal(UserDataSerializer.MaxGoals, result.Goals.Count);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public void Missing_settings_are_reported_and_left_unchanged()
    {
        var result = UserDataSerializer.Parse($$"""{"format":"{{UserDataSerializer.FormatName}}","version":1,"goals":[]}""");

        Assert.True(result.Succeeded);
        Assert.Null(result.Settings);
        Assert.Contains(result.Warnings, w => w.Contains("instellingen", StringComparison.Ordinal));
    }

    [Fact]
    public void Settings_from_a_file_are_made_safe()
    {
        // A plain (not interpolated) literal: the closing braces of the JSON would clash with interpolation.
        var result = UserDataSerializer.Parse("""
            {"format":"gw2-legendary-tracker-export","version":1,"goals":[],
             "settings":{"selectedLegendaryId":"abc",
               "overlay":{"opacity":9,"visibleMaterialCount":999,
                          "bounds":{"left":1,"top":2,"width":-5,"height":100},
                          "toggleHotkey":{"modifiers":0,"virtualKey":76}}}}
            """);

        Assert.True(result.Succeeded);
        var settings = result.Settings!;
        Assert.Null(settings.SelectedLegendaryId);
        Assert.Null(settings.Overlay.Bounds);
        Assert.Equal(OverlaySettings.MaxOpacity, settings.Overlay.Opacity);
        Assert.Equal(OverlaySettings.MaxVisibleMaterials, settings.Overlay.VisibleMaterialCount);
        Assert.Equal(HotkeyGesture.Default, settings.Overlay.ToggleHotkey);
        Assert.Equal(3, result.Warnings.Count);
    }

    [Fact]
    public void Damaged_files_never_throw_and_never_yield_inconsistent_goals()
    {
        var valid = UserDataSerializer.Serialize(SampleSettings(), SampleGoals(), At);
        var random = new Random(20261022);

        for (var round = 0; round < 500; round++)
        {
            var chars = valid.ToCharArray().ToList();
            for (var edits = random.Next(1, 6); edits > 0 && chars.Count > 0; edits--)
            {
                var at = random.Next(chars.Count);
                switch (random.Next(3))
                {
                    case 0: chars.RemoveAt(at); break;
                    case 1: chars[at] = "{}[]\":,0-9a"[random.Next(11)]; break;
                    default: chars.Insert(at, "{}[]\":,xyz9"[random.Next(11)]); break;
                }
            }

            var result = UserDataSerializer.Parse(new string([.. chars]));

            if (result.Succeeded)
            {
                Assert.All(result.Goals, g => Assert.True(g.ItemId > 0 && g.Quantity > 0));
                Assert.Equal(result.Goals.Count, result.Goals.Select(g => g.ItemId).Distinct().Count());
            }
            else
            {
                Assert.Empty(result.Goals);
            }
        }
    }
}

public sealed class UserDataTransferServiceTests : IDisposable
{
    private readonly RecordingSettingsStore _settingsStore = new();
    private readonly FakeGoalStore _goalStore = new();
    private readonly AppState _state;
    private readonly GoalService _goals;
    private readonly UserDataTransferService _transfer;

    public UserDataTransferServiceTests()
    {
        _state = new AppState(_settingsStore, new AppSettings());
        _goals = new GoalService(_goalStore);
        _transfer = new UserDataTransferService(_state, _goals, new ManualTime());
    }

    public void Dispose()
    {
        _goals.Dispose();
        _state.Dispose();
    }

    private static string ExportOf(AppSettings settings, params Goal[] goals) =>
        UserDataSerializer.Serialize(settings, goals, DateTimeOffset.UnixEpoch);

    [Fact]
    public async Task Export_reflects_the_current_goals_and_settings()
    {
        await _goals.AddAsync(30704);
        await _goals.SetNoteAsync(30704, "mijn eerste");
        await _state.UpdateAsync(s => s with { SelectedLegendaryId = "30704" });

        var parsed = UserDataSerializer.Parse(_transfer.Export());

        Assert.Equal("mijn eerste", Assert.Single(parsed.Goals).Note);
        Assert.Equal("30704", parsed.Settings?.SelectedLegendaryId);
    }

    [Fact]
    public async Task Import_replaces_goals_and_settings_and_stores_them()
    {
        await _goals.AddAsync(1);
        var file = ExportOf(new AppSettings { SelectedLegendaryId = "7" }, new Goal(7, 1, "nieuw") { CompletedItemIds = new HashSet<int> { 3 } }, new Goal(8));

        var outcome = await _transfer.ImportAsync(file);

        Assert.True(outcome.Succeeded);
        Assert.Contains("2 doel(en)", outcome.Message, StringComparison.Ordinal);
        Assert.Equal([7, 8], _goals.Goals.Select(g => g.ItemId));
        Assert.Equal([7, 8], _goalStore.Saved.Select(g => g.ItemId));
        Assert.Equal("7", _state.Settings.SelectedLegendaryId);
        Assert.Equal("7", _settingsStore.Saved?.SelectedLegendaryId);
    }

    [Fact]
    public async Task An_invalid_file_changes_nothing()
    {
        await _goals.AddAsync(1);
        var saves = _goalStore.SaveCount;
        var settingsSaves = _settingsStore.SaveCount;

        var outcome = await _transfer.ImportAsync("not an export");

        Assert.False(outcome.Succeeded);
        Assert.Equal([1], _goals.Goals.Select(g => g.ItemId));
        Assert.Equal(saves, _goalStore.SaveCount);
        Assert.Equal(settingsSaves, _settingsStore.SaveCount);
    }

    [Fact]
    public async Task A_file_without_settings_keeps_the_current_settings()
    {
        await _state.UpdateAsync(s => s with { SelectedLegendaryId = "99" });
        var file = $$"""{"format":"{{UserDataSerializer.FormatName}}","version":1,"goals":[{"itemId":5}]}""";

        var outcome = await _transfer.ImportAsync(file);

        Assert.True(outcome.Succeeded);
        Assert.Equal("99", _state.Settings.SelectedLegendaryId);
        Assert.Equal([5], _goals.Goals.Select(g => g.ItemId));
        Assert.NotEmpty(outcome.Warnings);
    }

    [Fact]
    public async Task Warnings_from_the_file_are_passed_on()
    {
        var file = $$"""{"format":"{{UserDataSerializer.FormatName}}","version":1,"settings":{},"goals":[{"itemId":5},{"itemId":0}]}""";

        var outcome = await _transfer.ImportAsync(file);

        Assert.True(outcome.Succeeded);
        Assert.Single(outcome.Warnings);
    }

    [Fact]
    public async Task Export_import_export_is_stable()
    {
        await _goals.AddAsync(30704);
        await _goals.SetCompletedAsync(30704, 19672, true);
        var first = _transfer.Export();

        await _transfer.ImportAsync(first);

        Assert.Equal(first, _transfer.Export());
    }

    [Fact]
    public async Task If_the_settings_cannot_be_stored_the_message_says_the_goals_were_still_imported()
    {
        _settingsStore.Failure = new IOException("schijf vol");
        var file = ExportOf(new AppSettings { SelectedLegendaryId = "7" }, new Goal(7));

        var outcome = await _transfer.ImportAsync(file);

        Assert.False(outcome.Succeeded);
        Assert.Contains("doelen zijn geïmporteerd", outcome.Message, StringComparison.Ordinal);
        Assert.Equal([7], _goals.Goals.Select(g => g.ItemId));
    }

    [Fact]
    public async Task Importing_the_same_data_twice_is_harmless()
    {
        var file = ExportOf(new AppSettings(), new Goal(7));
        await _transfer.ImportAsync(file);
        var saves = _goalStore.SaveCount;

        await _transfer.ImportAsync(file);

        Assert.Equal(saves, _goalStore.SaveCount);
    }

    private sealed class RecordingSettingsStore : ISettingsStore
    {
        public AppSettings? Saved { get; private set; }

        public int SaveCount { get; private set; }

        public Exception? Failure { get; set; }

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(new AppSettings());

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            if (Failure is not null)
            {
                return Task.FromException(Failure);
            }

            Saved = settings;
            SaveCount++;
            return Task.CompletedTask;
        }
    }
}

public sealed class GoalReplaceTests : IDisposable
{
    private readonly FakeGoalStore _store = new();
    private readonly GoalService _service;

    public GoalReplaceTests() => _service = new GoalService(_store);

    public void Dispose() => _service.Dispose();

    [Fact]
    public async Task Replace_stores_before_notifying_and_changes_the_order()
    {
        await _service.AddAsync(1);
        IReadOnlyList<Goal>? storedWhenNotified = null;
        _service.Changed += (_, _) => storedWhenNotified = _store.Saved;

        await _service.ReplaceAsync([new Goal(3), new Goal(2)]);

        Assert.Equal([3, 2], storedWhenNotified?.Select(g => g.ItemId));
        Assert.Equal([3, 2], _service.Goals.Select(g => g.ItemId));
    }

    [Fact]
    public async Task A_failed_replace_keeps_the_old_list()
    {
        await _service.AddAsync(1);
        _store.Failure = new IOException("disk full");

        await Assert.ThrowsAsync<IOException>(() => _service.ReplaceAsync([new Goal(9)]));

        Assert.Equal([1], _service.Goals.Select(g => g.ItemId));
    }
}
