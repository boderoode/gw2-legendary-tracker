using System.Runtime.Versioning;
using System.Text;
using Gw2LegendaryTracker.Infrastructure.Security;

namespace Gw2LegendaryTracker.Tests;

/// <summary>Marks a test that needs Windows DPAPI; it is skipped (and visible as skipped) elsewhere.</summary>
internal sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Requires Windows DPAPI.";
        }
    }
}

[SupportedOSPlatform("windows")]
public sealed class DpapiApiKeyStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "gw2lt-dpapi-" + Guid.NewGuid().ToString("N"));

    private string KeyPath => Path.Combine(_directory, "apikey.bin");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [WindowsFact]
    public async Task Key_round_trips_and_is_not_stored_as_plain_text()
    {
        var store = new DpapiApiKeyStore(KeyPath);

        await store.SaveAsync(TestData.Key);

        Assert.Equal(TestData.Key, await store.LoadAsync());
        var raw = await File.ReadAllBytesAsync(KeyPath);
        Assert.DoesNotContain(TestData.Key, Encoding.UTF8.GetString(raw), StringComparison.Ordinal);
        Assert.False(File.Exists(KeyPath + ".tmp"));
    }

    [WindowsFact]
    public async Task Load_returns_null_when_missing_or_corrupt()
    {
        var store = new DpapiApiKeyStore(KeyPath);
        Assert.Null(await store.LoadAsync());

        Directory.CreateDirectory(_directory);
        await File.WriteAllBytesAsync(KeyPath, [1, 2, 3, 4]);
        Assert.Null(await store.LoadAsync());
    }

    [WindowsFact]
    public async Task Delete_removes_the_key_and_is_safe_to_repeat()
    {
        var store = new DpapiApiKeyStore(KeyPath);
        await store.SaveAsync(TestData.Key);

        await store.DeleteAsync();
        await store.DeleteAsync();

        Assert.Null(await store.LoadAsync());
    }
}
