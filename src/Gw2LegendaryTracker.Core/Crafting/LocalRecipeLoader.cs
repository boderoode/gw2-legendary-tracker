using System.Globalization;
using System.Text.Json;

namespace Gw2LegendaryTracker.Core.Crafting;

/// <param name="ExpectedItemNames">Item id to the name the file claims for it; used to verify ids against the API.</param>
/// <param name="UnreviewedRecipeIds">Recipes the file marks as not yet checked against their source.</param>
public sealed record LocalRecipeLoadResult(
    IReadOnlyList<Recipe> Recipes,
    IReadOnlyList<string> Errors,
    IReadOnlyDictionary<int, string> ExpectedItemNames,
    IReadOnlySet<int> UnreviewedRecipeIds)
{
    public static LocalRecipeLoadResult Failed(string error) =>
        new([], [error], new Dictionary<int, string>(), new HashSet<int>());
}

/// <summary>
/// Reads locally imported recipes, for what the API does not describe (e.g. Mystic Forge steps and currency costs).
/// Invalid entries are skipped and reported; one bad entry never discards the rest.
/// <para>
/// Every item carries its <c>name</c>. That makes the file self-checking: <see cref="RecipePackVerifier"/> compares
/// each id and name with the API, and the loader rejects a recipe that gives one id two different names.
/// </para>
/// <code>
/// { "recipes": [ { "id": -1, "status": "reviewed", "source": "https://…",
///                  "output": { "itemId": 100, "count": 1, "name": "Gift of Might" },
///                  "ingredients": [ { "itemId": 5, "count": 2, "name": "Some Item" }, { "currencyId": 23, "count": 250 } ] } ] }
/// </code>
/// </summary>
public static class LocalRecipeLoader
{
    public const string UnreviewedStatus = "unreviewed";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static LocalRecipeLoadResult Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        FileDto? file;
        try
        {
            file = JsonSerializer.Deserialize<FileDto>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            return LocalRecipeLoadResult.Failed($"Het bestand is geen geldige JSON: {ex.Message}");
        }

        if (file?.Recipes is null)
        {
            return LocalRecipeLoadResult.Failed("Het bestand bevat geen lijst \"recipes\".");
        }

        var recipes = new List<Recipe>();
        var errors = new List<string>();
        var ids = new HashSet<int>();
        var names = new Dictionary<int, string>();
        var unreviewed = new HashSet<int>();

        for (var index = 0; index < file.Recipes.Count; index++)
        {
            var label = $"Recept #{(index + 1).ToString(CultureInfo.InvariantCulture)}";
            var error = Validate(file.Recipes[index], ids, names);
            if (error is not null)
            {
                errors.Add($"{label}: {error}");
                continue;
            }

            var dto = file.Recipes[index]!;
            ids.Add(dto.Id);
            RememberName(names, dto.Output!.ItemId, dto.Output.Name);
            foreach (var ingredient in dto.Ingredients!.Where(i => i!.ItemId is > 0))
            {
                RememberName(names, ingredient!.ItemId!.Value, ingredient.Name);
            }

            if (string.Equals(dto.Status, UnreviewedStatus, StringComparison.OrdinalIgnoreCase))
            {
                unreviewed.Add(dto.Id);
            }

            recipes.Add(new Recipe(
                dto.Id,
                dto.Output.ItemId,
                dto.Output.Count,
                [.. dto.Ingredients!.Select(i => new Ingredient(i!.CurrencyId is { } c ? MaterialKey.Currency(c) : MaterialKey.Item(i.ItemId!.Value), i.Count))],
                dto.Disciplines ?? [],
                RecipeSource.Local));
        }

        return new LocalRecipeLoadResult(recipes, errors, names, unreviewed);
    }

    private static void RememberName(Dictionary<int, string> names, int itemId, string? name)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            names.TryAdd(itemId, name.Trim());
        }
    }

    private static string? Validate(RecipeDto? dto, HashSet<int> seenIds, Dictionary<int, string> knownNames)
    {
        if (dto is null)
        {
            return "leeg item.";
        }

        if (dto.Id >= 0)
        {
            return "id moet negatief zijn, zodat het nooit botst met een recept uit de API.";
        }

        if (seenIds.Contains(dto.Id))
        {
            return $"id {dto.Id} komt dubbel voor.";
        }

        if (dto.Output is not { ItemId: > 0, Count: > 0 })
        {
            return "\"output\" heeft een itemId en een count groter dan 0 nodig.";
        }

        if (dto.Ingredients is not { Count: > 0 })
        {
            return "minstens één ingrediënt is nodig.";
        }

        // Within one recipe the same item must not appear under two names, and an ingredient must not repeat:
        // both are the signature of a mistyped id.
        var inRecipe = new Dictionary<int, string?> { [dto.Output.ItemId] = dto.Output.Name };
        var ingredientIds = new HashSet<int>();
        foreach (var ingredient in dto.Ingredients)
        {
            if (ingredient is null || ingredient.Count <= 0)
            {
                return "elk ingrediënt heeft een count groter dan 0 nodig.";
            }

            if ((ingredient.ItemId is > 0) == (ingredient.CurrencyId is > 0))
            {
                return "elk ingrediënt heeft precies één van itemId of currencyId nodig.";
            }

            if (ingredient.ItemId is not > 0)
            {
                continue;
            }

            if (!ingredientIds.Add(ingredient.ItemId.Value))
            {
                return $"item {ingredient.ItemId} staat twee keer als ingrediënt; voeg de aantallen samen of controleer het id.";
            }

            var conflict = NameConflict(ingredient.ItemId.Value, ingredient.Name, inRecipe, knownNames);
            if (conflict is not null)
            {
                return conflict;
            }

            inRecipe[ingredient.ItemId.Value] = ingredient.Name;
        }

        return NameConflict(dto.Output.ItemId, dto.Output.Name, [], knownNames);
    }

    private static string? NameConflict(int itemId, string? name, Dictionary<int, string?> inRecipe, Dictionary<int, string> knownNames)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        if (inRecipe.TryGetValue(itemId, out var other) && !string.IsNullOrWhiteSpace(other) && !string.Equals(other, name, StringComparison.OrdinalIgnoreCase))
        {
            return $"id {itemId} heeft twee namen in hetzelfde recept (\"{other}\" en \"{name}\").";
        }

        if (knownNames.TryGetValue(itemId, out var known) && !string.Equals(known, name.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return $"id {itemId} heet hier \"{name}\", maar eerder in het bestand \"{known}\".";
        }

        return null;
    }

    private sealed record FileDto(List<RecipeDto?>? Recipes);

    private sealed record RecipeDto(int Id, OutputDto? Output, List<IngredientDto?>? Ingredients, List<string>? Disciplines, string? Status, string? Source);

    private sealed record OutputDto(int ItemId, int Count, string? Name);

    private sealed record IngredientDto(int? ItemId, int? CurrencyId, long Count, string? Name);
}
