using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Gw2LegendaryTracker.Core.Crafting;
using Gw2LegendaryTracker.Core.Items;
using Gw2LegendaryTracker.Infrastructure.Gw2;

// Builds the local recipe file for Legendaries (the part the official recipe API does not contain).
//
//   GW2TREASURES_KEY=<key> dotnet run --project tools/Gw2LegendaryTracker.PackBuilder -- \
//       --root 30704 [--root <itemId> ...] [--out data/recipes/legendary.json] [--max-requests 80] [--delay-ms 1600]
//
// It follows Mystic Forge recipes from the given items downwards, then verifies the result against the official API:
// ids and names must match, and where the official item description states the recipe, the quantities must match too.
// The key is read from the environment only and is never written anywhere.

var options = Options.Parse(args);
if (options is null)
{
    return 2;
}

var key = Environment.GetEnvironmentVariable("GW2TREASURES_KEY");
if (string.IsNullOrWhiteSpace(key))
{
    Console.Error.WriteLine("Zet de omgevingsvariabele GW2TREASURES_KEY (de key van api.gw2treasures.com).");
    return 2;
}

using var official = new HttpClient { BaseAddress = new Uri("https://api.guildwars2.com/"), Timeout = TimeSpan.FromSeconds(60) };
official.DefaultRequestHeaders.UserAgent.ParseAdd("Gw2LegendaryTracker-PackBuilder/0.1");
using var treasures = new HttpClient { BaseAddress = new Uri("https://api.gw2treasures.com/"), Timeout = TimeSpan.FromSeconds(30) };
treasures.DefaultRequestHeaders.UserAgent.ParseAdd("Gw2LegendaryTracker-PackBuilder/0.1");
treasures.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);

var api = new Gw2ApiClient(official);

// Raw materials are bought or gathered, not forged; following them would only pull in unrelated conversions.
var categories = await official.GetFromJsonAsync<List<MaterialCategory>>("v2/materials?ids=all") ?? [];
var materials = categories.SelectMany(c => c.Items).ToHashSet();

var forged = new List<ForgeRecipe>();
var seen = new HashSet<int>();
var queue = new Queue<int>(options.Roots);
var requests = 0;
var randomSkipped = 0;
var stoppedEarly = false;

while (queue.Count > 0)
{
    var itemId = queue.Dequeue();
    if (!seen.Add(itemId) || materials.Contains(itemId))
    {
        continue;
    }

    if (requests >= options.MaxRequests)
    {
        stoppedEarly = true;
        break;
    }

    // The site asks for a crawl delay; always wait, also before the first request.
    await Task.Delay(options.DelayMs);
    requests++;
    using var response = await treasures.GetAsync($"items/{itemId.ToString(CultureInfo.InvariantCulture)}/mystic-forge");
    if (response.StatusCode == HttpStatusCode.NotFound)
    {
        continue;
    }

    if (!response.IsSuccessStatusCode)
    {
        Console.Error.WriteLine($"gw2treasures gaf HTTP {(int)response.StatusCode} voor item {itemId}; gestopt.");
        return 1;
    }

    var recipes = await response.Content.ReadFromJsonAsync<List<ForgeRecipe>>() ?? [];
    foreach (var recipe in recipes)
    {
        // A recipe with a random output is a lottery, not a way to make something.
        if (recipe.OutputMin != recipe.OutputMax)
        {
            randomSkipped++;
            continue;
        }

        forged.Add(recipe);
        foreach (var ingredient in recipe.Ingredients)
        {
            queue.Enqueue(ingredient.ItemId);
        }
    }
}

// Stable order and ids, so regenerating the file gives a readable diff.
var ordered = forged
    .OrderBy(r => r.OutputItemId)
    .ThenBy(r => string.Join(',', r.Ingredients.Select(i => $"{i.ItemId}x{i.Count}")), StringComparer.Ordinal)
    .ToList();

var ids = ordered.SelectMany(r => r.Ingredients.Select(i => i.ItemId).Append(r.OutputItemId)).Distinct().Order().ToList();
var names = new Dictionary<int, string>();
foreach (var batch in ids.Chunk(200))
{
    foreach (var item in await api.GetItemsAsync(batch))
    {
        names[item.Id] = item.Name;
    }
}

// First pass without review marks, to let the verifier decide which recipes the official descriptions confirm.
var draft = Pack.Render(ordered, names, verified: new HashSet<int>());
var loaded = LocalRecipeLoader.Parse(draft);
var verification = await RecipePackVerifier.VerifyAsync(loaded, api);
var final = Pack.Render(ordered, names, verification.DescriptionVerifiedRecipeIds);

Console.WriteLine($"gw2treasures-verzoeken: {requests}; recepten: {ordered.Count}; willekeurige recepten overgeslagen: {randomSkipped}");
if (stoppedEarly)
{
    Console.WriteLine($"LET OP: gestopt na {options.MaxRequests} verzoeken; er zijn nog onderdelen niet gevolgd. Verhoog --max-requests.");
}

foreach (var error in loaded.Errors)
{
    Console.WriteLine($"FOUT bij inlezen: {error}");
}

foreach (var problem in verification.Problems)
{
    Console.WriteLine($"PROBLEEM: {problem}");
}

Console.WriteLine($"Gecontroleerd tegen de officiële beschrijving: {verification.DescriptionVerifiedRecipeIds.Count} van {ordered.Count} recepten.");

if (loaded.Errors.Count > 0 || !verification.IsClean)
{
    Console.WriteLine("Bestand NIET geschreven: los de problemen hierboven eerst op.");
    return 1;
}

var directory = Path.GetDirectoryName(Path.GetFullPath(options.OutputPath));
if (!string.IsNullOrEmpty(directory))
{
    Directory.CreateDirectory(directory);
}

await File.WriteAllTextAsync(options.OutputPath, final, new UTF8Encoding(false));
Console.WriteLine($"Geschreven: {options.OutputPath}");
return 0;

internal sealed record MaterialCategory(List<int> Items);

internal sealed record ForgeIngredient([property: JsonPropertyName("item_id")] int ItemId, int Count);

internal sealed record ForgeRecipe(
    [property: JsonPropertyName("output_item_id")] int OutputItemId,
    [property: JsonPropertyName("output_item_count_min")] int OutputMin,
    [property: JsonPropertyName("output_item_count_max")] int OutputMax,
    List<ForgeIngredient> Ingredients);

internal sealed record Options(List<int> Roots, string OutputPath, int MaxRequests, int DelayMs)
{
    public static Options? Parse(string[] args)
    {
        var roots = new List<int>();
        var output = Path.Combine("data", "recipes", "legendary.json");
        var maxRequests = 80;
        var delay = 1600;

        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : string.Empty;
            switch (args[i])
            {
                case "--root" when int.TryParse(Next(), CultureInfo.InvariantCulture, out var root) && root > 0:
                    roots.Add(root);
                    break;
                case "--out":
                    output = Next();
                    break;
                case "--max-requests" when int.TryParse(Next(), CultureInfo.InvariantCulture, out var max) && max > 0:
                    maxRequests = max;
                    break;
                case "--delay-ms" when int.TryParse(Next(), CultureInfo.InvariantCulture, out var ms) && ms >= 1500:
                    delay = ms;
                    break;
                default:
                    Console.Error.WriteLine($"Onbekend of ongeldig argument: {args[i]} (--delay-ms moet minstens 1500 zijn, i.v.m. de crawl-delay van de site).");
                    return null;
            }
        }

        if (roots.Count == 0)
        {
            Console.Error.WriteLine("Geef minstens één --root <itemId>.");
            return null;
        }

        return new Options(roots, output, maxRequests, delay);
    }
}

internal static class Pack
{
    public const string ReviewedByDescription = "verified-by-official-description";

    /// <summary>Writes the file. <paramref name="verified"/> holds the recipe ids the official descriptions confirmed.</summary>
    public static string Render(IReadOnlyList<ForgeRecipe> recipes, IReadOnlyDictionary<int, string> names, IReadOnlySet<int> verified)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("about", "Mystic Forge recipes not present in the official recipe API. Generated by tools/Gw2LegendaryTracker.PackBuilder; item ids and names are verified against the official API.");
            writer.WriteStartArray("recipes");
            for (var index = 0; index < recipes.Count; index++)
            {
                var recipe = recipes[index];
                var id = -(index + 1);
                writer.WriteStartObject();
                writer.WriteNumber("id", id);
                writer.WriteString("status", verified.Contains(id) ? ReviewedByDescription : LocalRecipeLoader.UnreviewedStatus);
                writer.WriteString("source", $"https://api.gw2treasures.com/items/{recipe.OutputItemId.ToString(CultureInfo.InvariantCulture)}/mystic-forge");
                writer.WriteStartObject("output");
                writer.WriteNumber("itemId", recipe.OutputItemId);
                writer.WriteNumber("count", recipe.OutputMax);
                writer.WriteString("name", names.GetValueOrDefault(recipe.OutputItemId, string.Empty));
                writer.WriteEndObject();
                writer.WriteStartArray("ingredients");
                foreach (var ingredient in recipe.Ingredients)
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("itemId", ingredient.ItemId);
                    writer.WriteNumber("count", ingredient.Count);
                    writer.WriteString("name", names.GetValueOrDefault(ingredient.ItemId, string.Empty));
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }
}
