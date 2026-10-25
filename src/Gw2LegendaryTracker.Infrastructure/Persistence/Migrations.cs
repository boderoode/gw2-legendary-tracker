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

        // v5: Legendary list (public data) and the user's goals (user data).
        """
        CREATE TABLE legendary_item (
            item_id INTEGER PRIMARY KEY NOT NULL
        );

        CREATE TABLE goal (
            item_id  INTEGER PRIMARY KEY NOT NULL,
            position INTEGER NOT NULL,
            quantity INTEGER NOT NULL,
            note     TEXT NOT NULL
        );

        CREATE TABLE goal_completed (
            goal_item_id INTEGER NOT NULL REFERENCES goal (item_id) ON DELETE CASCADE,
            item_id      INTEGER NOT NULL,
            PRIMARY KEY (goal_item_id, item_id)
        ) WITHOUT ROWID;
        """,

        // v6: when public lists were last fetched, and the forge recipes fetched per item (public data).
        // forge_lookup also records items that turned out to have no recipe, so they are not asked for again.
        """
        CREATE TABLE public_data_meta (
            key         TEXT PRIMARY KEY NOT NULL,
            fetched_utc TEXT NOT NULL
        ) WITHOUT ROWID;

        CREATE TABLE forge_lookup (
            item_id     INTEGER PRIMARY KEY NOT NULL,
            fetched_utc TEXT NOT NULL
        );

        CREATE TABLE forge_recipe (
            id             INTEGER PRIMARY KEY AUTOINCREMENT,
            output_item_id INTEGER NOT NULL,
            output_count   INTEGER NOT NULL,
            is_verified    INTEGER NOT NULL
        );
        CREATE INDEX ix_forge_recipe_output ON forge_recipe (output_item_id);

        CREATE TABLE forge_recipe_ingredient (
            recipe_id INTEGER NOT NULL REFERENCES forge_recipe (id) ON DELETE CASCADE,
            item_id   INTEGER NOT NULL,
            count     INTEGER NOT NULL
        );
        CREATE INDEX ix_forge_recipe_ingredient_recipe ON forge_recipe_ingredient (recipe_id);
        """,

        // v7: Trading Post prices (public data). listed = 0 records that the item is not on the Trading Post.
        """
        CREATE TABLE tp_price (
            item_id       INTEGER PRIMARY KEY NOT NULL,
            listed        INTEGER NOT NULL,
            buy_price     INTEGER NOT NULL,
            buy_quantity  INTEGER NOT NULL,
            sell_price    INTEGER NOT NULL,
            sell_quantity INTEGER NOT NULL,
            fetched_utc   TEXT NOT NULL
        );
        """,

        // v8: the user's own vendor offers (user data; exported with the goals).
        """
        CREATE TABLE acquisition (
            id           INTEGER PRIMARY KEY NOT NULL,
            item_id      INTEGER NOT NULL,
            output_count INTEGER NOT NULL,
            vendor_name  TEXT NOT NULL,
            location     TEXT NOT NULL
        );
        CREATE INDEX ix_acquisition_item ON acquisition (item_id);

        CREATE TABLE acquisition_cost (
            acquisition_id INTEGER NOT NULL REFERENCES acquisition (id) ON DELETE CASCADE,
            kind           INTEGER NOT NULL,
            ref_id         INTEGER NOT NULL,
            amount         INTEGER NOT NULL
        );
        CREATE INDEX ix_acquisition_cost_offer ON acquisition_cost (acquisition_id);
        """,

        // v9: cached wardrobe skins and dye colours (public data), and the looks the user saved (user data).
        """
        CREATE TABLE cosmetic_skin (
            id           INTEGER PRIMARY KEY NOT NULL,
            name         TEXT NOT NULL,
            icon_url     TEXT NULL,
            category     INTEGER NOT NULL,
            slot         TEXT NOT NULL,
            weight_class TEXT NULL,
            rarity       TEXT NOT NULL,
            races        TEXT NOT NULL,
            dye_slots    INTEGER NOT NULL,
            description  TEXT NULL
        );

        CREATE TABLE cosmetic_skin_fetch (
            id          INTEGER PRIMARY KEY NOT NULL CHECK (id = 1),
            fetched_utc TEXT NOT NULL
        );

        CREATE TABLE dye_color (
            id       INTEGER PRIMARY KEY NOT NULL,
            name     TEXT NOT NULL,
            red      INTEGER NOT NULL,
            green    INTEGER NOT NULL,
            blue     INTEGER NOT NULL,
            hue      TEXT NULL,
            material TEXT NULL,
            rarity   TEXT NULL
        );

        CREATE TABLE look_preset (
            id          TEXT PRIMARY KEY NOT NULL,
            name        TEXT NOT NULL,
            look_json   TEXT NOT NULL,
            updated_utc TEXT NOT NULL
        );
        """,
    ];
}
