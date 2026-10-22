using Gw2LegendaryTracker.Core.Gw2;

namespace Gw2LegendaryTracker.Tests;

public class ApiKeyTests
{
    [Fact]
    public void Normalize_accepts_valid_key_and_trims_whitespace()
    {
        Assert.True(ApiKeyFormat.TryNormalize($"  {TestData.Key}\r\n", out var key));
        Assert.Equal(TestData.Key, key);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-key")]
    [InlineData("11111111-2222-3333-4444-5555555555")]
    [InlineData("ZZZZZZZZ-2222-3333-4444-55555555555555555555-6666-7777-8888-999999999999")]
    public void Normalize_rejects_invalid_input(string? input)
    {
        Assert.False(ApiKeyFormat.TryNormalize(input, out _));
    }

    [Fact]
    public void Missing_lists_only_absent_permissions_case_insensitively()
    {
        var missing = ApiPermissions.Missing(["Account", "wallet", "guilds"]);

        Assert.Equal(["inventories", "characters"], missing);
    }

    [Fact]
    public void Missing_is_empty_when_all_required_are_granted()
    {
        Assert.Empty(ApiPermissions.Missing([.. ApiPermissions.Required, "tradingpost"]));
    }
}
