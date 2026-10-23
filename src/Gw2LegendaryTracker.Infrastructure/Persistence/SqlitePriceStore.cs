using System.Globalization;
using Gw2LegendaryTracker.Core.Trading;
using Microsoft.Data.Sqlite;

namespace Gw2LegendaryTracker.Infrastructure.Persistence;

public sealed class SqlitePriceStore : IPriceStore
{
    /// <summary>Stays far below SQLite's bound-parameter limit.</summary>
    private const int LookupBatchSize = 500;

    private readonly SqliteDatabase _database;

    public SqlitePriceStore(SqliteDatabase database) => _database = database;

    public async Task<IReadOnlyDictionary<int, PriceLookup>> GetAsync(IReadOnlyCollection<int> itemIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(itemIds);

        var result = new Dictionary<int, PriceLookup>();
        if (itemIds.Count == 0)
        {
            return result;
        }

        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        foreach (var batch in itemIds.Distinct().Chunk(LookupBatchSize))
        {
            await using var command = connection.CreateCommand();
            var names = new string[batch.Length];
            for (var i = 0; i < batch.Length; i++)
            {
                names[i] = "$p" + i.ToString(CultureInfo.InvariantCulture);
                command.Parameters.AddWithValue(names[i], batch[i]);
            }

            // Only generated parameter names are concatenated; values are always bound.
            command.CommandText = $"""
                SELECT item_id, listed, buy_price, buy_quantity, sell_price, sell_quantity, fetched_utc
                FROM tp_price WHERE item_id IN ({string.Join(',', names)});
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var id = reader.GetInt32(0);
                var price = reader.GetInt32(1) == 0
                    ? null
                    : new TradingPostPrice(id, reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5));
                result[id] = new PriceLookup(id, price, DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
            }
        }

        return result;
    }

    public async Task SaveAsync(IReadOnlyCollection<PriceLookup> lookups, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lookups);
        if (lookups.Count == 0)
        {
            return;
        }

        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO tp_price (item_id, listed, buy_price, buy_quantity, sell_price, sell_quantity, fetched_utc)
            VALUES ($id, $listed, $buyPrice, $buyQuantity, $sellPrice, $sellQuantity, $fetched)
            ON CONFLICT(item_id) DO UPDATE SET
                listed = excluded.listed, buy_price = excluded.buy_price, buy_quantity = excluded.buy_quantity,
                sell_price = excluded.sell_price, sell_quantity = excluded.sell_quantity, fetched_utc = excluded.fetched_utc;
            """;
        var id = command.Parameters.Add("$id", SqliteType.Integer);
        var listed = command.Parameters.Add("$listed", SqliteType.Integer);
        var buyPrice = command.Parameters.Add("$buyPrice", SqliteType.Integer);
        var buyQuantity = command.Parameters.Add("$buyQuantity", SqliteType.Integer);
        var sellPrice = command.Parameters.Add("$sellPrice", SqliteType.Integer);
        var sellQuantity = command.Parameters.Add("$sellQuantity", SqliteType.Integer);
        var fetched = command.Parameters.Add("$fetched", SqliteType.Text);

        foreach (var lookup in lookups)
        {
            id.Value = lookup.ItemId;
            listed.Value = lookup.Price is null ? 0 : 1;
            buyPrice.Value = lookup.Price?.BuyUnitPrice ?? 0;
            buyQuantity.Value = lookup.Price?.BuyQuantity ?? 0;
            sellPrice.Value = lookup.Price?.SellUnitPrice ?? 0;
            sellQuantity.Value = lookup.Price?.SellQuantity ?? 0;
            fetched.Value = lookup.FetchedUtc.ToString("O", CultureInfo.InvariantCulture);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
