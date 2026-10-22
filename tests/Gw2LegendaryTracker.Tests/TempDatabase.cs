using Gw2LegendaryTracker.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace Gw2LegendaryTracker.Tests;

internal sealed class TempDatabase : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "gw2lt-tests-" + Guid.NewGuid().ToString("N"));

    public TempDatabase() => Database = new SqliteDatabase(Path.Combine(_directory, "test.db"));

    public SqliteDatabase Database { get; }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }
}
