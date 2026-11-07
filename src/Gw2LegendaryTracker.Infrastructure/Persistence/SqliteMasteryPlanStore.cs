using System.Globalization;
using Gw2LegendaryTracker.Core.Masteries;
using Microsoft.Data.Sqlite;

namespace Gw2LegendaryTracker.Infrastructure.Persistence;

/// <summary>
/// The active mastery plan and its manual choices (user data). The plan is stored in its own file format,
/// so the format has a single definition: <see cref="MasteryPlanSerializer"/>.
/// </summary>
public sealed class SqliteMasteryPlanStore : IMasteryPlanStore
{
    private readonly SqliteDatabase _database;

    public SqliteMasteryPlanStore(SqliteDatabase database) => _database = database;

    public async Task<StoredMasteryPlan?> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);

        string json;
        DateTimeOffset importedUtc;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT plan_json, imported_utc FROM mastery_plan WHERE id = 1;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            json = reader.GetString(0);
            importedUtc = DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        }

        // The stored plan was cleaned and size-checked when it was imported; it is read back without the import limits.
        var parsed = MasteryPlanSerializer.ReadStored(json);
        if (!parsed.Succeeded)
        {
            throw new InvalidOperationException($"Het opgeslagen masteryplan kan niet worden gelezen: {parsed.Error}");
        }

        var overrides = new Dictionary<string, bool>(StringComparer.Ordinal);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT step_id, done FROM mastery_step_override;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                overrides[reader.GetString(0)] = reader.GetInt32(1) != 0;
            }
        }

        return new StoredMasteryPlan(parsed.Plan!, overrides, importedUtc);
    }

    public async Task ReplaceAsync(MasteryPlan plan, IReadOnlyDictionary<string, bool> manualChoices, DateTimeOffset importedUtc, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(manualChoices);

        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using (var clear = connection.CreateCommand())
        {
            clear.Transaction = transaction;
            clear.CommandText = "DELETE FROM mastery_step_override;";
            await clear.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var save = connection.CreateCommand())
        {
            save.Transaction = transaction;
            save.CommandText = """
                INSERT INTO mastery_plan (id, plan_json, imported_utc) VALUES (1, $json, $at)
                ON CONFLICT(id) DO UPDATE SET plan_json = excluded.plan_json, imported_utc = excluded.imported_utc;
                """;
            save.Parameters.AddWithValue("$json", MasteryPlanSerializer.Serialize(plan));
            save.Parameters.AddWithValue("$at", importedUtc.ToString("O", CultureInfo.InvariantCulture));
            await save.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO mastery_step_override (step_id, done) VALUES ($id, $done);";
            var id = insert.Parameters.Add("$id", SqliteType.Text);
            var done = insert.Parameters.Add("$done", SqliteType.Integer);
            foreach (var (stepId, value) in manualChoices)
            {
                id.Value = stepId;
                done.Value = value ? 1 : 0;
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetOverrideAsync(string stepId, bool? done, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(stepId);

        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        if (done is { } value)
        {
            command.CommandText = """
                INSERT INTO mastery_step_override (step_id, done) VALUES ($id, $done)
                ON CONFLICT(step_id) DO UPDATE SET done = excluded.done;
                """;
            command.Parameters.AddWithValue("$done", value ? 1 : 0);
        }
        else
        {
            command.CommandText = "DELETE FROM mastery_step_override WHERE step_id = $id;";
        }

        command.Parameters.AddWithValue("$id", stepId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM mastery_step_override; DELETE FROM mastery_plan;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
