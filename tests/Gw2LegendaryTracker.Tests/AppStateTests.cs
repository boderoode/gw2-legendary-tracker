using Gw2LegendaryTracker.Core.Settings;
using Gw2LegendaryTracker.Core.State;

namespace Gw2LegendaryTracker.Tests;

public class AppStateTests
{
    [Fact]
    public async Task Update_persists_before_notifying_observers()
    {
        var store = new RecordingStore();
        using var state = new AppState(store, new AppSettings());
        AppSettings? savedAtNotification = null;
        state.SettingsChanged += (_, _) => savedAtNotification = store.Saved;

        await state.UpdateAsync(s => s with { SelectedLegendaryId = "x" });

        Assert.Equal("x", savedAtNotification?.SelectedLegendaryId);
        Assert.Equal("x", state.Settings.SelectedLegendaryId);
    }

    [Fact]
    public async Task Update_does_not_notify_or_apply_when_save_fails()
    {
        var store = new RecordingStore { Failure = new IOException("disk full") };
        using var state = new AppState(store, new AppSettings());
        var notified = false;
        state.SettingsChanged += (_, _) => notified = true;

        await Assert.ThrowsAsync<IOException>(() => state.UpdateAsync(s => s with { SelectedLegendaryId = "x" }));

        Assert.False(notified);
        Assert.Null(state.Settings.SelectedLegendaryId);
    }

    [Fact]
    public async Task Update_skips_save_and_notification_when_nothing_changed()
    {
        var store = new RecordingStore();
        using var state = new AppState(store, new AppSettings());
        var notified = false;
        state.SettingsChanged += (_, _) => notified = true;

        await state.UpdateAsync(s => s);

        Assert.Equal(0, store.SaveCount);
        Assert.False(notified);
    }

    [Fact]
    public async Task Update_normalizes_overlay_settings()
    {
        using var state = new AppState(new RecordingStore(), new AppSettings());

        await state.UpdateAsync(s => s with { Overlay = s.Overlay with { Opacity = 9 } });

        Assert.Equal(OverlaySettings.MaxOpacity, state.Settings.Overlay.Opacity);
    }

    [Fact]
    public async Task Concurrent_updates_are_not_lost()
    {
        using var state = new AppState(new RecordingStore(), new AppSettings());

        await Task.WhenAll(Enumerable.Range(1, 20).Select(_ =>
            state.UpdateAsync(s => s with { Overlay = s.Overlay with { VisibleMaterialCount = Math.Min(s.Overlay.VisibleMaterialCount + 1, 20) } })));

        Assert.Equal(20, state.Settings.Overlay.VisibleMaterialCount);
    }

    [Fact]
    public void ReportSyncStatus_notifies_observers()
    {
        using var state = new AppState(new RecordingStore(), new AppSettings());
        SyncStatus? seen = null;
        state.SyncStatusChanged += (_, s) => seen = s;

        var status = new SyncStatus(SyncPhase.Failed, Error: "offline");
        state.ReportSyncStatus(status);

        Assert.Equal(status, seen);
        Assert.Equal(status, state.SyncStatus);
    }

    private sealed class RecordingStore : ISettingsStore
    {
        public AppSettings? Saved { get; private set; }

        public int SaveCount { get; private set; }

        public Exception? Failure { get; init; }

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
