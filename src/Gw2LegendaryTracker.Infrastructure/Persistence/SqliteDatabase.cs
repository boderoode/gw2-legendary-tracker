using Microsoft.Data.Sqlite;

namespace Gw2LegendaryTracker.Infrastructure.Persistence;

/// <summary>Owns the SQLite connection string and keeps the schema up to date.</summary>
public sealed class SqliteDatabase
{
    private readonly string _connectionString;

    public SqliteDatabase(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (Path.GetDirectoryName(Path.GetFullPath(path)) is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
        }.ToString();
    }

    public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Applies all pending migrations, each in its own transaction.</summary>
    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);

        var current = await GetVersionAsync(connection, cancellationToken).ConfigureAwait(false);
        if (current > Migrations.All.Count)
        {
            throw new InvalidOperationException(
                $"Database schema version {current} is newer than this application supports ({Migrations.All.Count}). Update the application.");
        }

        for (var version = current + 1; version <= Migrations.All.Count; version++)
        {
            await using var transaction = (SqliteTransaction)await connection
                .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            // PRAGMA does not accept parameters; version is a loop counter, not user input.
            command.CommandText = Migrations.All[version - 1] + $"\nPRAGMA user_version = {version};";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<int> GetVersionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
    }
}
