using System.Globalization;
using Gw2LegendaryTracker.Core.Masteries;
using Microsoft.Data.Sqlite;

namespace Gw2LegendaryTracker.Infrastructure.Persistence;

/// <summary>The account's mastery data from the last mastery sync (replaced as a whole) and how that sync went.</summary>
public sealed class SqliteMasteryProgressStore : IMasteryProgressStore
{
    private readonly SqliteDatabase _database;

    public SqliteMasteryProgressStore(SqliteDatabase database) => _database = database;

    public async Task<MasteryAccountProgress?> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);

        DateTimeOffset captured;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT captured_utc FROM account_mastery_capture WHERE id = 1;";
            if (await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not string text)
            {
                return null;
            }

            captured = ParseDate(text);
        }

        var masteries = await ReadAsync(connection, "SELECT mastery_id, level FROM account_mastery ORDER BY mastery_id;",
            r => new AccountMastery(r.GetInt32(0), r.GetInt32(1)), cancellationToken).ConfigureAwait(false);
        var points = await ReadAsync(connection, "SELECT region, spent, earned FROM account_mastery_points ORDER BY region;",
            r => new RegionPoints(r.GetString(0), r.GetInt32(1), r.GetInt32(2)), cancellationToken).ConfigureAwait(false);
        var unlocked = await ReadAsync(connection, "SELECT mastery_id FROM account_mastery_unlocked ORDER BY mastery_id;",
            r => r.GetInt32(0), cancellationToken).ConfigureAwait(false);
        var achievements = await ReadAsync(connection, "SELECT achievement_id, done, current, max FROM account_achievement ORDER BY achievement_id;",
            r => new AchievementProgress(r.GetInt32(0), r.GetInt32(1) != 0, r.GetInt32(2), r.GetInt32(3)), cancellationToken).ConfigureAwait(false);

        return new MasteryAccountProgress(captured, masteries, points, unlocked, achievements);
    }

    public async Task SaveAsync(MasteryAccountProgress progress, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(progress);

        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(connection, transaction, """
            DELETE FROM account_mastery;
            DELETE FROM account_mastery_points;
            DELETE FROM account_mastery_unlocked;
            DELETE FROM account_achievement;
            """, cancellationToken).ConfigureAwait(false);

        await using (var capture = connection.CreateCommand())
        {
            capture.Transaction = transaction;
            capture.CommandText = """
                INSERT INTO account_mastery_capture (id, captured_utc) VALUES (1, $at)
                ON CONFLICT(id) DO UPDATE SET captured_utc = excluded.captured_utc;
                """;
            capture.Parameters.AddWithValue("$at", progress.CapturedUtc.ToString("O", CultureInfo.InvariantCulture));
            await capture.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = "INSERT OR REPLACE INTO account_mastery (mastery_id, level) VALUES ($id, $level);";
            var id = insert.Parameters.Add("$id", SqliteType.Integer);
            var level = insert.Parameters.Add("$level", SqliteType.Integer);
            foreach (var mastery in progress.Masteries)
            {
                id.Value = mastery.Id;
                level.Value = mastery.Level;
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = "INSERT OR REPLACE INTO account_mastery_points (region, spent, earned) VALUES ($region, $spent, $earned);";
            var region = insert.Parameters.Add("$region", SqliteType.Text);
            var spent = insert.Parameters.Add("$spent", SqliteType.Integer);
            var earned = insert.Parameters.Add("$earned", SqliteType.Integer);
            foreach (var points in progress.Points)
            {
                region.Value = points.Region;
                spent.Value = points.Spent;
                earned.Value = points.Earned;
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = "INSERT OR IGNORE INTO account_mastery_unlocked (mastery_id) VALUES ($id);";
            var id = insert.Parameters.Add("$id", SqliteType.Integer);
            foreach (var masteryId in progress.Unlocked)
            {
                id.Value = masteryId;
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = "INSERT OR REPLACE INTO account_achievement (achievement_id, done, current, max) VALUES ($id, $done, $current, $max);";
            var id = insert.Parameters.Add("$id", SqliteType.Integer);
            var done = insert.Parameters.Add("$done", SqliteType.Integer);
            var current = insert.Parameters.Add("$current", SqliteType.Integer);
            var max = insert.Parameters.Add("$max", SqliteType.Integer);
            foreach (var achievement in progress.Achievements)
            {
                id.Value = achievement.Id;
                done.Value = achievement.Done ? 1 : 0;
                current.Value = achievement.Current;
                max.Value = achievement.Max;
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<MasterySyncStatus> LoadStatusAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT state, last_success_utc, error FROM mastery_sync_state WHERE id = 1;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return MasterySyncStatus.Initial;
        }

        var state = Enum.TryParse<MasterySyncState>(reader.GetString(0), out var parsed) ? parsed : MasterySyncState.Never;
        DateTimeOffset? lastSuccess = reader.IsDBNull(1) ? null : ParseDate(reader.GetString(1));
        var error = reader.IsDBNull(2) ? null : reader.GetString(2);
        return new MasterySyncStatus(state, lastSuccess, error);
    }

    public async Task SaveStatusAsync(MasterySyncStatus status, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(status);

        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO mastery_sync_state (id, state, last_success_utc, error) VALUES (1, $state, $at, $error)
            ON CONFLICT(id) DO UPDATE SET state = excluded.state, last_success_utc = excluded.last_success_utc, error = excluded.error;
            """;
        command.Parameters.AddWithValue("$state", status.State.ToString());
        command.Parameters.AddWithValue("$at", (object?)status.LastSuccessUtc?.ToString("O", CultureInfo.InvariantCulture) ?? DBNull.Value);
        command.Parameters.AddWithValue("$error", (object?)status.Error ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static DateTimeOffset ParseDate(string text) => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static async Task<List<T>> ReadAsync<T>(SqliteConnection connection, string sql, Func<SqliteDataReader, T> map, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var list = new List<T>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(map(reader));
        }

        return list;
    }

    private static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
