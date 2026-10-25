using System.Globalization;
using Gw2LegendaryTracker.Core.Cosmetics;
using Microsoft.Data.Sqlite;

namespace Gw2LegendaryTracker.Infrastructure.Persistence;

public sealed class SqliteCosmeticsStore : ICosmeticsStore
{
    /// <summary>Race names never contain this, so it is a safe separator.</summary>
    private const char RaceSeparator = '|';

    private readonly SqliteDatabase _database;

    public SqliteCosmeticsStore(SqliteDatabase database) => _database = database;

    public async Task<IReadOnlyList<CosmeticSkin>> LoadSkinsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, name, icon_url, category, slot, weight_class, rarity, races, dye_slots, description FROM cosmetic_skin;";

        var skins = new List<CosmeticSkin>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var races = reader.GetString(7);
            skins.Add(new CosmeticSkin(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                (SkinCategory)reader.GetInt32(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetString(6),
                races.Length == 0 ? [] : races.Split(RaceSeparator),
                reader.GetInt32(8),
                reader.IsDBNull(9) ? null : reader.GetString(9)));
        }

        return skins;
    }

    public async Task SaveSkinsAsync(IReadOnlyList<CosmeticSkin> skins, DateTimeOffset fetchedUtc, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(skins);

        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using (var clear = connection.CreateCommand())
        {
            clear.Transaction = transaction;
            clear.CommandText = "DELETE FROM cosmetic_skin;";
            await clear.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT OR REPLACE INTO cosmetic_skin (id, name, icon_url, category, slot, weight_class, rarity, races, dye_slots, description)
                VALUES ($id, $name, $icon, $category, $slot, $weight, $rarity, $races, $dyes, $description);
                """;
            var id = insert.Parameters.Add("$id", SqliteType.Integer);
            var name = insert.Parameters.Add("$name", SqliteType.Text);
            var icon = insert.Parameters.Add("$icon", SqliteType.Text);
            var category = insert.Parameters.Add("$category", SqliteType.Integer);
            var slot = insert.Parameters.Add("$slot", SqliteType.Text);
            var weight = insert.Parameters.Add("$weight", SqliteType.Text);
            var rarity = insert.Parameters.Add("$rarity", SqliteType.Text);
            var races = insert.Parameters.Add("$races", SqliteType.Text);
            var dyes = insert.Parameters.Add("$dyes", SqliteType.Integer);
            var description = insert.Parameters.Add("$description", SqliteType.Text);

            foreach (var skin in skins)
            {
                id.Value = skin.Id;
                name.Value = skin.Name;
                icon.Value = (object?)skin.IconUrl ?? DBNull.Value;
                category.Value = (int)skin.Category;
                slot.Value = skin.Slot;
                weight.Value = (object?)skin.WeightClass ?? DBNull.Value;
                rarity.Value = skin.Rarity;
                races.Value = string.Join(RaceSeparator, skin.Races);
                dyes.Value = skin.DyeSlotCount;
                description.Value = (object?)skin.Description ?? DBNull.Value;
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        await using (var stamp = connection.CreateCommand())
        {
            stamp.Transaction = transaction;
            stamp.CommandText = """
                INSERT INTO cosmetic_skin_fetch (id, fetched_utc) VALUES (1, $at)
                ON CONFLICT(id) DO UPDATE SET fetched_utc = excluded.fetched_utc;
                """;
            stamp.Parameters.AddWithValue("$at", fetchedUtc.ToString("O", CultureInfo.InvariantCulture));
            await stamp.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<DateTimeOffset?> GetSkinsFetchedUtcAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT fetched_utc FROM cosmetic_skin_fetch WHERE id = 1;";
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is string text
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
                ? at
                : null;
    }

    public async Task<IReadOnlyList<DyeColor>> LoadColorsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, name, red, green, blue, hue, material, rarity FROM dye_color ORDER BY name, id;";

        var colors = new List<DyeColor>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            colors.Add(new DyeColor(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7)));
        }

        return colors;
    }

    public async Task SaveColorsAsync(IReadOnlyList<DyeColor> colors, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(colors);

        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using (var clear = connection.CreateCommand())
        {
            clear.Transaction = transaction;
            clear.CommandText = "DELETE FROM dye_color;";
            await clear.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT OR REPLACE INTO dye_color (id, name, red, green, blue, hue, material, rarity)
                VALUES ($id, $name, $red, $green, $blue, $hue, $material, $rarity);
                """;
            var id = insert.Parameters.Add("$id", SqliteType.Integer);
            var name = insert.Parameters.Add("$name", SqliteType.Text);
            var red = insert.Parameters.Add("$red", SqliteType.Integer);
            var green = insert.Parameters.Add("$green", SqliteType.Integer);
            var blue = insert.Parameters.Add("$blue", SqliteType.Integer);
            var hue = insert.Parameters.Add("$hue", SqliteType.Text);
            var material = insert.Parameters.Add("$material", SqliteType.Text);
            var rarity = insert.Parameters.Add("$rarity", SqliteType.Text);

            foreach (var color in colors)
            {
                id.Value = color.Id;
                name.Value = color.Name;
                red.Value = color.Red;
                green.Value = color.Green;
                blue.Value = color.Blue;
                hue.Value = (object?)color.Hue ?? DBNull.Value;
                material.Value = (object?)color.Material ?? DBNull.Value;
                rarity.Value = (object?)color.Rarity ?? DBNull.Value;
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LookPreset>> LoadPresetsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, name, look_json, updated_utc FROM look_preset ORDER BY name COLLATE NOCASE, id;";

        var presets = new List<LookPreset>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            // A preset that cannot be read is skipped; one bad row must not hide the others.
            if (LookJson.Deserialize(reader.GetString(2)) is { } look
                && DateTimeOffset.TryParse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var updated))
            {
                presets.Add(new LookPreset(reader.GetString(0), reader.GetString(1), look, updated));
            }
        }

        return presets;
    }

    public async Task SavePresetAsync(LookPreset preset, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preset);

        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO look_preset (id, name, look_json, updated_utc) VALUES ($id, $name, $json, $updated)
            ON CONFLICT(id) DO UPDATE SET name = excluded.name, look_json = excluded.look_json, updated_utc = excluded.updated_utc;
            """;
        command.Parameters.AddWithValue("$id", preset.Id);
        command.Parameters.AddWithValue("$name", preset.Name);
        command.Parameters.AddWithValue("$json", LookJson.Serialize(preset.Look));
        command.Parameters.AddWithValue("$updated", preset.UpdatedUtc.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeletePresetAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM look_preset WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
