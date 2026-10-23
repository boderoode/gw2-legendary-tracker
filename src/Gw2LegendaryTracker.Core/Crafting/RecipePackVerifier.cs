using Gw2LegendaryTracker.Core.Gw2;

namespace Gw2LegendaryTracker.Core.Crafting;

/// <param name="Problems">Wrong or unverifiable ids. Any problem means the file must not be trusted.</param>
/// <param name="Notes">Reminders that are not errors, e.g. recipes still marked as unreviewed.</param>
/// <param name="DescriptionVerifiedRecipeIds">Recipes whose ingredients and quantities match the official item description.</param>
public sealed record PackVerification(
    int ItemsChecked,
    IReadOnlyList<string> Problems,
    IReadOnlyList<string> Notes,
    IReadOnlySet<int> DescriptionVerifiedRecipeIds)
{
    public bool IsClean => Problems.Count == 0;
}

/// <summary>
/// Checks the item ids of a local recipe file against the official API: each id must exist and carry the name
/// the file gives it. This catches mistyped ids (a wrong id for a real item), which would otherwise silently
/// produce wrong shortages. Where the official description of the produced item states its recipe (most gifts do),
/// the ingredients and quantities are compared too. Everything else stays a review step.
/// </summary>
public static class RecipePackVerifier
{
    private const int BatchSize = 200;

    public static async Task<PackVerification> VerifyAsync(
        LocalRecipeLoadResult pack,
        IGw2ApiClient api,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(api);

        var problems = new List<string>();
        var notes = new List<string>();

        var referenced = pack.Recipes
            .SelectMany(r => r.Ingredients.Select(i => i.Key).Append(MaterialKey.Item(r.OutputItemId)))
            .Where(k => k.Kind == MaterialKind.Item)
            .Select(k => k.Id)
            .Distinct()
            .Order()
            .ToList();

        foreach (var id in referenced.Where(id => !pack.ExpectedItemNames.ContainsKey(id)))
        {
            problems.Add($"Item {id} heeft geen naam in het bestand, dus het id kan niet worden gecontroleerd.");
        }

        var actual = new Dictionary<int, string>();
        var descriptions = new Dictionary<int, string?>();
        foreach (var batch in referenced.Chunk(BatchSize))
        {
            foreach (var item in await api.GetItemsAsync(batch, cancellationToken).ConfigureAwait(false))
            {
                actual[item.Id] = item.Name;
                descriptions[item.Id] = item.Description;
            }
        }

        foreach (var id in referenced.Where(pack.ExpectedItemNames.ContainsKey))
        {
            var expected = pack.ExpectedItemNames[id];
            if (!actual.TryGetValue(id, out var real))
            {
                problems.Add($"Item {id} (\"{expected}\") bestaat niet in de API.");
            }
            else if (!string.Equals(real, expected, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"Item {id} is \"{real}\" in de API, maar het bestand zegt \"{expected}\".");
            }
        }

        await VerifyCurrenciesAsync(pack, api, problems, cancellationToken).ConfigureAwait(false);

        var verified = new HashSet<int>();
        foreach (var group in pack.Recipes.GroupBy(r => r.OutputItemId))
        {
            var stated = MysticForgeDescription.TryParse(descriptions.GetValueOrDefault(group.Key));
            if (stated is null)
            {
                continue;
            }

            var matching = group.Where(r => MatchesDescription(r, stated, actual)).ToList();
            if (matching.Count == 0)
            {
                var name = actual.GetValueOrDefault(group.Key, group.Key.ToString(System.Globalization.CultureInfo.InvariantCulture));
                problems.Add($"De officiële beschrijving van \"{name}\" noemt andere ingrediënten of aantallen dan het bestand: {string.Join(", ", stated.Select(i => $"{i.Count} {i.Name}"))}.");
                continue;
            }

            verified.UnionWith(matching.Select(r => r.Id));
        }

        foreach (var recipeId in pack.UnreviewedRecipeIds.Order())
        {
            notes.Add($"Recept {recipeId} is nog niet nagelopen tegen de bron (status \"unreviewed\").");
        }

        return new PackVerification(referenced.Count, problems, notes, verified);
    }

    /// <summary>True when the recipe lists exactly the items and quantities the description states.</summary>
    private static bool MatchesDescription(Recipe recipe, IReadOnlyList<(int Count, string Name)> stated, Dictionary<int, string> actualNames)
    {
        // The description cannot state currencies, so a recipe that needs them cannot be compared this way.
        if (recipe.Ingredients.Any(i => i.Key.Kind != MaterialKind.Item))
        {
            return false;
        }

        return MysticForgeDescription.Matches(
            stated,
            recipe.Ingredients.Select(i => (actualNames.GetValueOrDefault(i.Key.Id, string.Empty), i.Count)));
    }

    /// <summary>Currency ids are checked like item ids: they must exist and carry the name the file gives them.</summary>
    private static async Task VerifyCurrenciesAsync(LocalRecipeLoadResult pack, IGw2ApiClient api, List<string> problems, CancellationToken cancellationToken)
    {
        var referenced = pack.Recipes
            .SelectMany(r => r.Ingredients)
            .Where(i => i.Key.Kind == MaterialKind.Currency)
            .Select(i => i.Key.Id)
            .Distinct()
            .Order()
            .ToList();
        if (referenced.Count == 0)
        {
            return;
        }

        var expectedNames = pack.ExpectedCurrencyNames ?? new Dictionary<int, string>();
        var actual = await api.GetCurrencyNamesAsync(referenced, cancellationToken).ConfigureAwait(false);
        foreach (var id in referenced)
        {
            if (!expectedNames.TryGetValue(id, out var expected))
            {
                problems.Add($"Valuta {id} heeft geen naam in het bestand, dus het id kan niet worden gecontroleerd.");
            }
            else if (!actual.TryGetValue(id, out var real))
            {
                problems.Add($"Valuta {id} (\"{expected}\") bestaat niet in de API.");
            }
            else if (!string.Equals(real, expected, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"Valuta {id} is \"{real}\" in de API, maar het bestand zegt \"{expected}\".");
            }
        }
    }
}
