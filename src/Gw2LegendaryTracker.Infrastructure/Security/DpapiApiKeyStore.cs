using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Gw2LegendaryTracker.Core.Sync;

namespace Gw2LegendaryTracker.Infrastructure.Security;

/// <summary>
/// Stores the API key encrypted with Windows DPAPI (current-user scope): only this Windows user on this
/// machine can decrypt it. The key never touches the database, settings or logs.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiApiKeyStore : IApiKeyStore
{
    // Binds the ciphertext to this application so other DPAPI users of the same account cannot trivially reuse it.
    private static readonly byte[] Entropy = "Gw2LegendaryTracker.ApiKey.v1"u8.ToArray();

    private readonly string _path;

    public DpapiApiKeyStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
    }

    public async Task<string?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        try
        {
            var protectedBytes = await File.ReadAllBytesAsync(_path, cancellationToken).ConfigureAwait(false);
            var bytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (CryptographicException)
        {
            // Copied from another user/machine or corrupted: treat as "no key" so the user can simply enter it again.
            return null;
        }
    }

    public async Task SaveAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        if (Path.GetDirectoryName(Path.GetFullPath(_path)) is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
        }

        var protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(apiKey), Entropy, DataProtectionScope.CurrentUser);

        // Write to a temp file first so a crash cannot leave a half-written key behind.
        var temp = _path + ".tmp";
        await File.WriteAllBytesAsync(temp, protectedBytes, cancellationToken).ConfigureAwait(false);
        File.Move(temp, _path, overwrite: true);
    }

    public Task DeleteAsync(CancellationToken cancellationToken = default)
    {
        File.Delete(_path);
        return Task.CompletedTask;
    }
}
