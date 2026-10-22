using Gw2LegendaryTracker.Core.Crafting;

namespace Gw2LegendaryTracker.Infrastructure.Persistence;

/// <summary>Loads every <c>*.json</c> recipe file from the given folders (the shipped data and the user's own).</summary>
public static class LocalRecipeFiles
{
    public static async Task<LocalRecipes> LoadAsync(IEnumerable<string> directories, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(directories);

        var recipes = new List<Recipe>();
        var errors = new List<string>();
        var ids = new HashSet<int>();
        var unreviewed = new HashSet<int>();

        foreach (var directory in directories.Where(Directory.Exists))
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*.json").Order(StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(file);
                string json;
                try
                {
                    json = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
                }
                catch (IOException ex)
                {
                    errors.Add($"{name}: kon niet worden gelezen ({ex.Message})");
                    continue;
                }

                var result = LocalRecipeLoader.Parse(json);
                errors.AddRange(result.Errors.Select(e => $"{name}: {e}"));

                foreach (var recipe in result.Recipes)
                {
                    // Two files must not define the same recipe id; keeping the first would hide a mistake.
                    if (!ids.Add(recipe.Id))
                    {
                        errors.Add($"{name}: recept-id {recipe.Id} bestaat al in een ander bestand en is overgeslagen.");
                        continue;
                    }

                    recipes.Add(recipe);
                    if (result.UnreviewedRecipeIds.Contains(recipe.Id))
                    {
                        unreviewed.Add(recipe.Id);
                    }
                }
            }
        }

        return new LocalRecipes(recipes, unreviewed, errors);
    }
}
