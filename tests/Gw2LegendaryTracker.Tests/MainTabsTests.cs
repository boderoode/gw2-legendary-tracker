using Gw2LegendaryTracker.Core.Export;
using Gw2LegendaryTracker.Core.Goals;
using Gw2LegendaryTracker.Core.Settings;
using Gw2LegendaryTracker.Core.State;
using Gw2LegendaryTracker.Infrastructure.Persistence;

namespace Gw2LegendaryTracker.Tests;

public sealed class MainTabsTests
{
    [Theory]
    [InlineData(null, 0)]
    [InlineData("legendaries", 0)]
    [InlineData("masteries", 1)]
    [InlineData("account", 2)]
    [InlineData("bestaat-niet", 0)]
    [InlineData("Masteries", 0)]
    public void A_stored_tab_name_maps_to_its_position(string? name, int expected) =>
        Assert.Equal(expected, MainTabs.IndexOf(name));

    [Fact]
    public async Task The_chosen_tab_is_stored_with_the_settings()
    {
        using var temp = new TempDatabase();
        await temp.Database.MigrateAsync();
        var store = new SqliteSettingsStore(temp.Database);

        await store.SaveAsync(new AppSettings { SelectedMainTab = MainTabs.Masteries });

        Assert.Equal(MainTabs.Masteries, (await store.LoadAsync()).SelectedMainTab);
    }

    [Fact]
    public async Task Importing_user_data_keeps_the_open_tab()
    {
        using var state = new AppState(new FakeSettingsStore(), new AppSettings { SelectedMainTab = MainTabs.Account });
        using var goals = new GoalService(new FakeGoalStore());
        var transfer = new UserDataTransferService(state, goals, new ManualTime());
        var file = UserDataSerializer.Serialize(new AppSettings { SelectedMainTab = MainTabs.Masteries, SelectedLegendaryId = "30704" }, [], DateTimeOffset.UnixEpoch);

        var outcome = await transfer.ImportAsync(file);

        Assert.True(outcome.Succeeded, outcome.Message);
        Assert.Equal(MainTabs.Account, state.Settings.SelectedMainTab);
        Assert.Equal("30704", state.Settings.SelectedLegendaryId);
    }
}
