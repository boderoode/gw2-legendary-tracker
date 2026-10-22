using Gw2LegendaryTracker.Core.Settings;
using Gw2LegendaryTracker.Infrastructure.Persistence;

namespace Gw2LegendaryTracker.Tests;

public class SqliteSettingsStoreTests
{
    [Fact]
    public async Task Load_returns_defaults_when_nothing_saved()
    {
        using var temp = new TempDatabase();
        await temp.Database.MigrateAsync();

        var settings = await new SqliteSettingsStore(temp.Database).LoadAsync();

        Assert.Equal(new AppSettings(), settings);
    }

    [Fact]
    public async Task Save_then_load_round_trips()
    {
        using var temp = new TempDatabase();
        await temp.Database.MigrateAsync();
        var store = new SqliteSettingsStore(temp.Database);
        var expected = new AppSettings
        {
            SelectedLegendaryId = "gen3-weapon",
            Overlay = new OverlaySettings
            {
                Bounds = new OverlayBounds(10, 20, 300, 200),
                Opacity = 0.5,
                IsLocked = true,
                AutoHideSeconds = 15,
                ToggleHotkey = new HotkeyGesture(HotkeyModifiers.Shift | HotkeyModifiers.Alt, 0x4F),
            },
        };

        await store.SaveAsync(expected);
        var actual = await store.LoadAsync();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task Save_overwrites_previous_value()
    {
        using var temp = new TempDatabase();
        await temp.Database.MigrateAsync();
        var store = new SqliteSettingsStore(temp.Database);

        await store.SaveAsync(new AppSettings { SelectedLegendaryId = "a" });
        await store.SaveAsync(new AppSettings { SelectedLegendaryId = "b" });

        Assert.Equal("b", (await store.LoadAsync()).SelectedLegendaryId);
    }

    [Fact]
    public async Task Load_falls_back_to_defaults_on_corrupt_json()
    {
        using var temp = new TempDatabase();
        await temp.Database.MigrateAsync();
        await using (var connection = await temp.Database.OpenAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO user_setting (key, value) VALUES ('app_settings', '{not json');";
            await command.ExecuteNonQueryAsync();
        }

        var settings = await new SqliteSettingsStore(temp.Database).LoadAsync();

        Assert.Equal(new AppSettings(), settings);
    }

    [Fact]
    public async Task Migrate_is_idempotent_and_keeps_data()
    {
        using var temp = new TempDatabase();
        await temp.Database.MigrateAsync();
        var store = new SqliteSettingsStore(temp.Database);
        await store.SaveAsync(new AppSettings { SelectedLegendaryId = "keep-me" });

        await temp.Database.MigrateAsync();

        Assert.Equal("keep-me", (await store.LoadAsync()).SelectedLegendaryId);
    }
}
