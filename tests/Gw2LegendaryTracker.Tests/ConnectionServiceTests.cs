using Gw2LegendaryTracker.Core.Gw2;
using Gw2LegendaryTracker.Core.Sync;

namespace Gw2LegendaryTracker.Tests;

public class ConnectionServiceTests
{
    private readonly FakeApiClient _api = new();
    private readonly FakeApiKeyStore _keys = new();

    private ConnectionService CreateService() => new(_api, _keys);

    [Fact]
    public async Task Valid_key_is_stored_trimmed()
    {
        var result = await CreateService().ConnectAsync($" {TestData.Key} ");

        Assert.True(result.IsConnected);
        Assert.Equal(TestData.Key, _keys.Key);
        Assert.Equal("Test key", result.AccountName);
    }

    [Fact]
    public async Task Malformed_key_is_rejected_without_calling_the_api()
    {
        var result = await CreateService().ConnectAsync("hello");

        Assert.False(result.IsConnected);
        Assert.Null(_keys.Key);
    }

    [Fact]
    public async Task Key_with_missing_permissions_is_not_stored_and_names_them()
    {
        _api.Token = new Gw2TokenInfo("id", "key", ["account", "wallet"]);

        var result = await CreateService().ConnectAsync(TestData.Key);

        Assert.False(result.IsConnected);
        Assert.Null(_keys.Key);
        Assert.Contains("inventories", result.Message, StringComparison.Ordinal);
        Assert.Contains("characters", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Api_error_is_reported_and_key_is_not_stored()
    {
        _api.Failure = new Gw2ApiException(Gw2ApiErrorKind.InvalidKey, "De API-key is ongeldig.");

        var result = await CreateService().ConnectAsync(TestData.Key);

        Assert.False(result.IsConnected);
        Assert.Equal("De API-key is ongeldig.", result.Message);
        Assert.Null(_keys.Key);
    }

    [Fact]
    public async Task Disconnect_removes_the_key()
    {
        var service = CreateService();
        await service.ConnectAsync(TestData.Key);

        await service.DisconnectAsync();

        Assert.False(await service.HasKeyAsync());
    }
}
