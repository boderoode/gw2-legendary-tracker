using System.Globalization;
using Gw2LegendaryTracker.Core.Goals;
using Microsoft.Data.Sqlite;

namespace Gw2LegendaryTracker.Infrastructure.Persistence;

public sealed class SqliteLegendaryStore : ILegendaryStore
{
    private const string MetaKey = "legendary_list";

    private readonly SqliteDatabase _database;

    public SqliteLegendaryStore(SqliteDatabase database) => _database = database;

    public async Task<IReadOnlyList<int>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT item_id FROM legendary_item ORDER BY item_id;";

        var ids = new List<int>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            ids.Add(reader.GetInt32(0));
        }

        return ids;
    }

    public async Task<DateTimeOffset?> GetFetchedUtcAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT fetched_utc FROM public_data_meta WHERE key = $key;";
        command.Parameters.AddWithValue("$key", MetaKey);

        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is string text
            ? DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            : null;
    }

    public async Task SaveAsync(IReadOnlyCollection<int> itemIds, DateTimeOffset fetchedUtc, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(itemIds);

        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using (var clear = connection.CreateCommand())
        {
            clear.Transaction = transaction;
            clear.CommandText = "DELETE FROM legendary_item;";
            await clear.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = "INSERT OR IGNORE INTO legendary_item (item_id) VALUES ($id);";
            var id = insert.Parameters.Add("$id", SqliteType.Integer);
            foreach (var itemId in itemIds)
            {
                id.Value = itemId;
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        await using (var stamp = connection.CreateCommand())
        {
            stamp.Transaction = transaction;
            stamp.CommandText = """
                INSERT INTO public_data_meta (key, fetched_utc) VALUES ($key, $at)
                ON CONFLICT(key) DO UPDATE SET fetched_utc = excluded.fetched_utc;
                """;
            stamp.Parameters.AddWithValue("$key", MetaKey);
            stamp.Parameters.AddWithValue("$at", fetchedUtc.ToString("O", CultureInfo.InvariantCulture));
            await stamp.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
