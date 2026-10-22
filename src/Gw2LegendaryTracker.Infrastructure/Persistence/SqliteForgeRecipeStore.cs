using System.Globalization;
using Gw2LegendaryTracker.Core.Crafting;
using Gw2LegendaryTracker.Core.Crafting.Forge;
using Microsoft.Data.Sqlite;

namespace Gw2LegendaryTracker.Infrastructure.Persistence;

public sealed class SqliteForgeRecipeStore : IForgeRecipeStore
{
    private readonly SqliteDatabase _database;

    public SqliteForgeRecipeStore(SqliteDatabase database) => _database = database;

    public async Task<DateTimeOffset?> GetFetchedUtcAsync(int itemId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT fetched_utc FROM forge_lookup WHERE item_id = $item;";
        command.Parameters.AddWithValue("$item", itemId);

        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is string text
            ? DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            : null;
    }

    public async Task SaveAsync(int itemId, IReadOnlyList<ForgeRecipeDraft> recipes, DateTimeOffset fetchedUtc, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recipes);

        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // Replace what was stored for this item as a whole; the cascade removes the old ingredient rows.
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM forge_recipe WHERE output_item_id = $item;";
            delete.Parameters.AddWithValue("$item", itemId);
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var recipe in recipes)
        {
            long recipeId;
            await using (var insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO forge_recipe (output_item_id, output_count, is_verified) VALUES ($item, $count, $verified);
                    SELECT last_insert_rowid();
                    """;
                insert.Parameters.AddWithValue("$item", itemId);
                insert.Parameters.AddWithValue("$count", recipe.OutputCount);
                insert.Parameters.AddWithValue("$verified", recipe.IsVerified ? 1 : 0);
                recipeId = (long)(await insert.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
            }

            await using var ingredient = connection.CreateCommand();
            ingredient.Transaction = transaction;
            ingredient.CommandText = "INSERT INTO forge_recipe_ingredient (recipe_id, item_id, count) VALUES ($recipe, $item, $count);";
            ingredient.Parameters.AddWithValue("$recipe", recipeId);
            var itemParameter = ingredient.Parameters.Add("$item", SqliteType.Integer);
            var countParameter = ingredient.Parameters.Add("$count", SqliteType.Integer);

            foreach (var item in recipe.Ingredients.Where(i => i.Key.Kind == MaterialKind.Item))
            {
                itemParameter.Value = item.Key.Id;
                countParameter.Value = item.Count;
                await ingredient.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        await using (var lookup = connection.CreateCommand())
        {
            lookup.Transaction = transaction;
            lookup.CommandText = """
                INSERT INTO forge_lookup (item_id, fetched_utc) VALUES ($item, $at)
                ON CONFLICT(item_id) DO UPDATE SET fetched_utc = excluded.fetched_utc;
                """;
            lookup.Parameters.AddWithValue("$item", itemId);
            lookup.Parameters.AddWithValue("$at", fetchedUtc.ToString("O", CultureInfo.InvariantCulture));
            await lookup.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Recipe>> GetRecipesForAsync(int itemId, CancellationToken cancellationToken = default) =>
        (await LoadAsync(itemId, cancellationToken).ConfigureAwait(false)).Recipes;

    public Task<ForgeRecipeSet> LoadAllAsync(CancellationToken cancellationToken = default) => LoadAsync(null, cancellationToken);

    private async Task<ForgeRecipeSet> LoadAsync(int? itemId, CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);

        var ingredients = new Dictionary<long, List<Ingredient>>();
        await using (var command = connection.CreateCommand())
        {
            // Insertion order inside a recipe keeps plans deterministic.
            command.CommandText = """
                SELECT i.recipe_id, i.item_id, i.count
                FROM forge_recipe_ingredient i
                JOIN forge_recipe r ON r.id = i.recipe_id
                WHERE $item IS NULL OR r.output_item_id = $item
                ORDER BY i.rowid;
                """;
            command.Parameters.AddWithValue("$item", (object?)itemId ?? DBNull.Value);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var recipeId = reader.GetInt64(0);
                if (!ingredients.TryGetValue(recipeId, out var list))
                {
                    ingredients[recipeId] = list = [];
                }

                list.Add(new Ingredient(MaterialKey.Item(reader.GetInt32(1)), reader.GetInt64(2)));
            }
        }

        var recipes = new List<Recipe>();
        var singleSource = new HashSet<int>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT id, output_item_id, output_count, is_verified
                FROM forge_recipe
                WHERE $item IS NULL OR output_item_id = $item
                ORDER BY id;
                """;
            command.Parameters.AddWithValue("$item", (object?)itemId ?? DBNull.Value);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var rowId = reader.GetInt64(0);
                var id = -(IForgeRecipeStore.IdBase + checked((int)rowId));
                recipes.Add(new Recipe(
                    id,
                    reader.GetInt32(1),
                    reader.GetInt32(2),
                    ingredients.GetValueOrDefault(rowId) ?? [],
                    [],
                    RecipeSource.Local));
                if (reader.GetInt32(3) == 0)
                {
                    singleSource.Add(id);
                }
            }
        }

        return new ForgeRecipeSet(recipes, singleSource);
    }
}
