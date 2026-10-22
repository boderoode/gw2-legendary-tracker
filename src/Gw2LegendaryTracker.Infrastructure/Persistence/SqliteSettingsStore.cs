using System.Text.Json;
using Gw2LegendaryTracker.Core.Settings;

namespace Gw2LegendaryTracker.Infrastructure.Persistence;

public sealed class SqliteSettingsStore : ISettingsStore
{
    private const string Key = "app_settings";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SqliteDatabase _database;

    public SqliteSettingsStore(SqliteDatabase database) => _database = database;

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM user_setting WHERE key = $key;";
        command.Parameters.AddWithValue("$key", Key);

        if (await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not string json)
        {
            return new AppSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (JsonException)
        {
            // Corrupt or incompatible settings must never prevent the app from starting.
            return new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO user_setting (key, value) VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        command.Parameters.AddWithValue("$key", Key);
        command.Parameters.AddWithValue("$value", JsonSerializer.Serialize(settings, JsonOptions));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
