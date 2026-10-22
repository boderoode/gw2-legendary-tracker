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

        // v2: latest account snapshot (replaced as a whole on every successful sync).
        """
        CREATE TABLE sync_state (
            id           INTEGER PRIMARY KEY CHECK (id = 1),
            account_name TEXT NOT NULL,
            captured_utc TEXT NOT NULL
        );

        CREATE TABLE inventory_item (
            source         TEXT NOT NULL,
            character_name TEXT NOT NULL,
            item_id        INTEGER NOT NULL,
            count          INTEGER NOT NULL
        );
        CREATE INDEX ix_inventory_item_item ON inventory_item (item_id);

        CREATE TABLE wallet_currency (
            currency_id INTEGER PRIMARY KEY NOT NULL,
            value       INTEGER NOT NULL
        );
        """,

        // v3: public item data cache. Separate from user data; safe to refresh or clear.
        """
        CREATE TABLE item_info (
            item_id     INTEGER PRIMARY KEY NOT NULL,
            name        TEXT NOT NULL,
            icon_url    TEXT NULL,
            rarity      TEXT NOT NULL,
            fetched_utc TEXT NOT NULL
        );
        """,

        // v4: public recipe cache (API recipes only; locally imported recipes are loaded from files).
        """
        CREATE TABLE recipe (
            recipe_id               INTEGER PRIMARY KEY NOT NULL,
            output_item_id          INTEGER NOT NULL,
            output_count            INTEGER NOT NULL,
            disciplines             TEXT NOT NULL,
            requires_guild_upgrades INTEGER NOT NULL
        );
        CREATE INDEX ix_recipe_output ON recipe (output_item_id);

        CREATE TABLE recipe_ingredient (
            recipe_id INTEGER NOT NULL REFERENCES recipe (recipe_id) ON DELETE CASCADE,
            item_id   INTEGER NOT NULL,
            count     INTEGER NOT NULL
        );
        CREATE INDEX ix_recipe_ingredient_recipe ON recipe_ingredient (recipe_id);
        """,
    ];
}
