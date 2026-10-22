namespace Gw2LegendaryTracker.Infrastructure.Persistence;

/// <summary>
/// Ordered schema migrations. The index + 1 of an entry is the schema version stored in <c>PRAGMA user_version</c>.
/// Never edit or reorder an entry that has shipped; append a new one instead.
/// </summary>
internal static class Migrations
{
    public static IReadOnlyList<string> All { get; } =
    [
        // v1: user data (settings). Item/recipe data lands in separate tables so it can be refreshed without touching user data.
        """
        CREATE TABLE user_setting (
            key   TEXT PRIMARY KEY NOT NULL,
            value TEXT NOT NULL
        ) WITHOUT ROWID;
        """,
    ];
}
