using System.Globalization;
using Gw2LegendaryTracker.Core.Masteries;
using Microsoft.Data.Sqlite;

namespace Gw2LegendaryTracker.Infrastructure.Persistence;

/// <summary>Local copy of the game's mastery tracks (public data, safe to refresh).</summary>
public sealed class SqliteMasteryDefinitionStore : IMasteryDefinitionStore
{
    private const string MetaKey = "masteries";

    private readonly SqliteDatabase _database;

    public SqliteMasteryDefinitionStore(SqliteDatabase database) => _database = database;

    public async Task<IReadOnlyList<MasteryDefinition>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);

        var levels = new Dictionary<int, List<MasteryLevelDefinition>>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT mastery_id, name, point_cost, exp_cost FROM mastery_level ORDER BY mastery_id, level_index;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var id = reader.GetInt32(0);
                if (!levels.TryGetValue(id, out var list))
                {
                    levels[id] = list = [];
                }

                list.Add(new MasteryLevelDefinition(reader.GetString(1), reader.GetInt32(2), reader.GetInt64(3)));
            }
        }

        var definitions = new List<MasteryDefinition>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id, name, region, sort_order FROM mastery ORDER BY id;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var id = reader.GetInt32(0);
                definitions.Add(new MasteryDefinition(id, reader.GetString(1), reader.GetString(2), reader.GetInt32(3), levels.GetValueOrDefault(id) ?? []));
            }
        }

        return definitions;
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

    public async Task SaveAsync(IReadOnlyCollection<MasteryDefinition> definitions, DateTimeOffset fetchedUtc, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using (var clear = connection.CreateCommand())
        {
            clear.Transaction = transaction;
            clear.CommandText = "DELETE FROM mastery_level; DELETE FROM mastery;";
            await clear.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var insert = connection.CreateCommand())
        await using (var insertLevel = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = "INSERT OR REPLACE INTO mastery (id, name, region, sort_order) VALUES ($id, $name, $region, $order);";
            var id = insert.Parameters.Add("$id", SqliteType.Integer);
            var name = insert.Parameters.Add("$name", SqliteType.Text);
            var region = insert.Parameters.Add("$region", SqliteType.Text);
            var order = insert.Parameters.Add("$order", SqliteType.Integer);

            insertLevel.Transaction = transaction;
            insertLevel.CommandText = """
                INSERT OR REPLACE INTO mastery_level (mastery_id, level_index, name, point_cost, exp_cost)
                VALUES ($id, $index, $name, $points, $exp);
                """;
            var levelId = insertLevel.Parameters.Add("$id", SqliteType.Integer);
            var index = insertLevel.Parameters.Add("$index", SqliteType.Integer);
            var levelName = insertLevel.Parameters.Add("$name", SqliteType.Text);
            var points = insertLevel.Parameters.Add("$points", SqliteType.Integer);
            var exp = insertLevel.Parameters.Add("$exp", SqliteType.Integer);

            foreach (var definition in definitions)
            {
                id.Value = definition.Id;
                name.Value = definition.Name;
                region.Value = definition.Region;
                order.Value = definition.Order;
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                for (var i = 0; i < definition.Levels.Count; i++)
                {
                    levelId.Value = definition.Id;
                    index.Value = i;
                    levelName.Value = definition.Levels[i].Name;
                    points.Value = definition.Levels[i].PointCost;
                    exp.Value = definition.Levels[i].ExpCost;
                    await insertLevel.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
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
