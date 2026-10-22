using Gw2LegendaryTracker.Core.Crafting;

namespace Gw2LegendaryTracker.Infrastructure.Persistence;

public sealed record LocalRecipeFilesResult(IReadOnlyList<Recipe> Recipes, IReadOnlyList<string> Errors, int UnreviewedCount);

/// <summary>Loads every <c>*.json</c> recipe file from the given folders (the shipped data and the user's own).</summary>
public static class LocalRecipeFiles
{
    public static async Task<LocalRecipeFilesResult> LoadAsync(IEnumerable<string> directories, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(directories);

        var recipes = new List<Recipe>();
        var errors = new List<string>();
        var ids = new HashSet<int>();
        var unreviewed = 0;

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
                unreviewed += result.UnreviewedRecipeIds.Count;

                foreach (var recipe in result.Recipes)
                {
                    // Two files must not define the same recipe id; keeping the first would hide a mistake.
                    if (ids.Add(recipe.Id))
                    {
                        recipes.Add(recipe);
                    }
                    else
                    {
                        errors.Add($"{name}: recept-id {recipe.Id} bestaat al in een ander bestand en is overgeslagen.");
                    }
                }
            }
        }

        return new LocalRecipeFilesResult(recipes, errors, unreviewed);
    }
}
