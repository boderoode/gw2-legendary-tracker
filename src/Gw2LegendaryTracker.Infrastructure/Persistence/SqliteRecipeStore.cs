using Gw2LegendaryTracker.Core.Crafting;
using Microsoft.Data.Sqlite;

namespace Gw2LegendaryTracker.Infrastructure.Persistence;

public sealed class SqliteRecipeStore : IRecipeStore
{
    private const char DisciplineSeparator = ',';

    private readonly SqliteDatabase _database;

    public SqliteRecipeStore(SqliteDatabase database) => _database = database;

    public async Task<IReadOnlySet<int>> GetKnownIdsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT recipe_id FROM recipe;";

        var ids = new HashSet<int>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            ids.Add(reader.GetInt32(0));
        }

        return ids;
    }

    public async Task SaveAsync(IReadOnlyCollection<Recipe> recipes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recipes);
        if (recipes.Count == 0)
        {
            return;
        }

        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using var delete = connection.CreateCommand();
        delete.Transaction = transaction;
        delete.CommandText = "DELETE FROM recipe WHERE recipe_id = $id;";
        var deleteId = delete.Parameters.Add("$id", SqliteType.Integer);

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO recipe (recipe_id, output_item_id, output_count, disciplines, requires_guild_upgrades)
            VALUES ($id, $output, $count, $disciplines, $guild);
            """;
        var id = insert.Parameters.Add("$id", SqliteType.Integer);
        var output = insert.Parameters.Add("$output", SqliteType.Integer);
        var count = insert.Parameters.Add("$count", SqliteType.Integer);
        var disciplines = insert.Parameters.Add("$disciplines", SqliteType.Text);
        var guild = insert.Parameters.Add("$guild", SqliteType.Integer);

        await using var ingredient = connection.CreateCommand();
        ingredient.Transaction = transaction;
        ingredient.CommandText = "INSERT INTO recipe_ingredient (recipe_id, item_id, count) VALUES ($recipe, $item, $count);";
        var ingredientRecipe = ingredient.Parameters.Add("$recipe", SqliteType.Integer);
        var ingredientItem = ingredient.Parameters.Add("$item", SqliteType.Integer);
        var ingredientCount = ingredient.Parameters.Add("$count", SqliteType.Integer);

        foreach (var recipe in recipes)
        {
            // Replace as a whole so a re-saved recipe never keeps stale ingredients (cascade removes the old rows).
            deleteId.Value = recipe.Id;
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            id.Value = recipe.Id;
            output.Value = recipe.OutputItemId;
            count.Value = recipe.OutputCount;
            disciplines.Value = string.Join(DisciplineSeparator, recipe.Disciplines);
            guild.Value = recipe.RequiresGuildUpgrades ? 1 : 0;
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            foreach (var item in recipe.Ingredients.Where(i => i.Key.Kind == MaterialKind.Item))
            {
                ingredientRecipe.Value = recipe.Id;
                ingredientItem.Value = item.Key.Id;
                ingredientCount.Value = item.Count;
                await ingredient.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Recipe>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);

        var ingredients = new Dictionary<int, List<Ingredient>>();
        await using (var command = connection.CreateCommand())
        {
            // Row order inside a recipe is the insertion order, which keeps plans deterministic.
            command.CommandText = "SELECT recipe_id, item_id, count FROM recipe_ingredient ORDER BY rowid;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var recipeId = reader.GetInt32(0);
                if (!ingredients.TryGetValue(recipeId, out var list))
                {
                    ingredients[recipeId] = list = [];
                }

                list.Add(new Ingredient(MaterialKey.Item(reader.GetInt32(1)), reader.GetInt64(2)));
            }
        }

        var recipes = new List<Recipe>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT recipe_id, output_item_id, output_count, disciplines, requires_guild_upgrades FROM recipe ORDER BY recipe_id;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var recipeId = reader.GetInt32(0);
                var disciplineText = reader.GetString(3);
                recipes.Add(new Recipe(
                    recipeId,
                    reader.GetInt32(1),
                    reader.GetInt32(2),
                    ingredients.GetValueOrDefault(recipeId) ?? [],
                    disciplineText.Length == 0 ? [] : disciplineText.Split(DisciplineSeparator),
                    RecipeSource.Api,
                    reader.GetInt32(4) != 0));
            }
        }

        return recipes;
    }
}
