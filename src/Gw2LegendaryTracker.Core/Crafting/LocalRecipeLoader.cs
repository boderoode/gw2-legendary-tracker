using System.Globalization;
using System.Text.Json;

namespace Gw2LegendaryTracker.Core.Crafting;

public sealed record LocalRecipeLoadResult(IReadOnlyList<Recipe> Recipes, IReadOnlyList<string> Errors);

/// <summary>
/// Reads locally imported recipes, for what the API does not describe (e.g. Mystic Forge steps and currency costs).
/// Invalid entries are skipped and reported; one bad entry never discards the rest.
/// <code>
/// { "recipes": [ { "id": -1, "output": { "itemId": 100, "count": 1 },
///                  "ingredients": [ { "itemId": 5, "count": 2 }, { "currencyId": 23, "count": 250 } ] } ] }
/// </code>
/// </summary>
public static class LocalRecipeLoader
{
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
            return new LocalRecipeLoadResult([], [$"Het bestand is geen geldige JSON: {ex.Message}"]);
        }

        if (file?.Recipes is null)
        {
            return new LocalRecipeLoadResult([], ["Het bestand bevat geen lijst \"recipes\"."]);
        }

        var recipes = new List<Recipe>();
        var errors = new List<string>();
        var ids = new HashSet<int>();

        for (var index = 0; index < file.Recipes.Count; index++)
        {
            var label = $"Recept #{(index + 1).ToString(CultureInfo.InvariantCulture)}";
            var error = Validate(file.Recipes[index], ids);
            if (error is not null)
            {
                errors.Add($"{label}: {error}");
                continue;
            }

            var dto = file.Recipes[index]!;
            ids.Add(dto.Id);
            recipes.Add(new Recipe(
                dto.Id,
                dto.Output!.ItemId,
                dto.Output.Count,
                [.. dto.Ingredients!.Select(i => new Ingredient(i!.CurrencyId is { } c ? MaterialKey.Currency(c) : MaterialKey.Item(i.ItemId!.Value), i.Count))],
                dto.Disciplines ?? [],
                RecipeSource.Local));
        }

        return new LocalRecipeLoadResult(recipes, errors);
    }

    private static string? Validate(RecipeDto? dto, HashSet<int> seenIds)
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
        }

        return null;
    }

    private sealed record FileDto(List<RecipeDto?>? Recipes);

    private sealed record RecipeDto(int Id, OutputDto? Output, List<IngredientDto?>? Ingredients, List<string>? Disciplines);

    private sealed record OutputDto(int ItemId, int Count);

    private sealed record IngredientDto(
        int? ItemId,
        int? CurrencyId,
        long Count);
}
