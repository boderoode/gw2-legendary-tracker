using System.Globalization;
using Gw2LegendaryTracker.Core.Items;
using Microsoft.Data.Sqlite;

namespace Gw2LegendaryTracker.Infrastructure.Persistence;

public sealed class SqliteItemInfoStore : IItemInfoStore
{
    /// <summary>Stays far below SQLite's bound-parameter limit.</summary>
    private const int LookupBatchSize = 500;

    private readonly SqliteDatabase _database;
    private readonly TimeProvider _time;

    public SqliteItemInfoStore(SqliteDatabase database, TimeProvider? time = null)
    {
        _database = database;
        _time = time ?? TimeProvider.System;
    }

    public async Task<IReadOnlyDictionary<int, ItemInfo>> GetAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var result = new Dictionary<int, ItemInfo>();
        if (ids.Count == 0)
        {
            return result;
        }

        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        foreach (var batch in ids.Distinct().Chunk(LookupBatchSize))
        {
            await using var command = connection.CreateCommand();
            var names = new string[batch.Length];
            for (var i = 0; i < batch.Length; i++)
            {
                names[i] = "$p" + i.ToString(CultureInfo.InvariantCulture);
                command.Parameters.AddWithValue(names[i], batch[i]);
            }

            // Only generated parameter names are concatenated; values are always bound.
            command.CommandText = $"SELECT item_id, name, icon_url, rarity FROM item_info WHERE item_id IN ({string.Join(',', names)});";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var item = new ItemInfo(
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.GetString(3));
                result[item.Id] = item;
            }
        }

        return result;
    }

    public async Task SaveAsync(IReadOnlyCollection<ItemInfo> items, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            return;
        }

        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO item_info (item_id, name, icon_url, rarity, fetched_utc) VALUES ($id, $name, $icon, $rarity, $fetched)
            ON CONFLICT(item_id) DO UPDATE SET
                name = excluded.name, icon_url = excluded.icon_url, rarity = excluded.rarity, fetched_utc = excluded.fetched_utc;
            """;
        var id = command.Parameters.Add("$id", SqliteType.Integer);
        var name = command.Parameters.Add("$name", SqliteType.Text);
        var icon = command.Parameters.Add("$icon", SqliteType.Text);
        var rarity = command.Parameters.Add("$rarity", SqliteType.Text);
        var fetched = command.Parameters.Add("$fetched", SqliteType.Text);
        fetched.Value = _time.GetUtcNow().ToString("O", CultureInfo.InvariantCulture);

        foreach (var item in items)
        {
            id.Value = item.Id;
            name.Value = item.Name;
            icon.Value = (object?)item.IconUrl ?? DBNull.Value;
            rarity.Value = item.Rarity;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
