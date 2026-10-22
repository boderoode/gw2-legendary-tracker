using System.Globalization;
using Gw2LegendaryTracker.Core.Gw2;
using Gw2LegendaryTracker.Core.Sync;
using Microsoft.Data.Sqlite;

namespace Gw2LegendaryTracker.Infrastructure.Persistence;

public sealed class SqliteSnapshotStore : ISnapshotStore
{
    private readonly SqliteDatabase _database;

    public SqliteSnapshotStore(SqliteDatabase database) => _database = database;

    public async Task SaveAsync(AccountSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(connection, transaction, "DELETE FROM inventory_item; DELETE FROM wallet_currency;", cancellationToken).ConfigureAwait(false);

        await using (var insertItem = connection.CreateCommand())
        {
            insertItem.Transaction = transaction;
            insertItem.CommandText = "INSERT INTO inventory_item (source, character_name, item_id, count) VALUES ($source, $character, $item, $count);";
            var source = insertItem.Parameters.Add("$source", SqliteType.Text);
            var character = insertItem.Parameters.Add("$character", SqliteType.Text);
            var item = insertItem.Parameters.Add("$item", SqliteType.Integer);
            var count = insertItem.Parameters.Add("$count", SqliteType.Integer);

            foreach (var entry in snapshot.Items)
            {
                source.Value = entry.Source.ToString();
                character.Value = entry.CharacterName;
                item.Value = entry.ItemId;
                count.Value = entry.Count;
                await insertItem.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        await using (var insertCurrency = connection.CreateCommand())
        {
            insertCurrency.Transaction = transaction;
            insertCurrency.CommandText = "INSERT OR REPLACE INTO wallet_currency (currency_id, value) VALUES ($id, $value);";
            var id = insertCurrency.Parameters.Add("$id", SqliteType.Integer);
            var value = insertCurrency.Parameters.Add("$value", SqliteType.Integer);

            foreach (var entry in snapshot.Wallet)
            {
                id.Value = entry.CurrencyId;
                value.Value = entry.Value;
                await insertCurrency.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        await using (var state = connection.CreateCommand())
        {
            state.Transaction = transaction;
            state.CommandText = """
                INSERT INTO sync_state (id, account_name, captured_utc) VALUES (1, $name, $captured)
                ON CONFLICT(id) DO UPDATE SET account_name = excluded.account_name, captured_utc = excluded.captured_utc;
                """;
            state.Parameters.AddWithValue("$name", snapshot.AccountName);
            state.Parameters.AddWithValue("$captured", snapshot.CapturedUtc.ToString("O", CultureInfo.InvariantCulture));
            await state.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<AccountSnapshot?> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);

        string accountName;
        DateTimeOffset captured;
        await using (var state = connection.CreateCommand())
        {
            state.CommandText = "SELECT account_name, captured_utc FROM sync_state WHERE id = 1;";
            await using var reader = await state.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            accountName = reader.GetString(0);
            captured = DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        }

        var items = new List<InventoryEntry>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT source, character_name, item_id, count FROM inventory_item;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                items.Add(new InventoryEntry(
                    reader.GetInt32(2),
                    reader.GetInt32(3),
                    Enum.Parse<InventorySource>(reader.GetString(0)),
                    reader.GetString(1)));
            }
        }

        var wallet = new List<WalletEntry>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT currency_id, value FROM wallet_currency;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                wallet.Add(new WalletEntry(reader.GetInt32(0), reader.GetInt64(1)));
            }
        }

        return new AccountSnapshot(accountName, captured, items, wallet);
    }

    private static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
