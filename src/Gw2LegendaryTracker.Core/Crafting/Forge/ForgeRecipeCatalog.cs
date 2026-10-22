using Gw2LegendaryTracker.Core.Gw2;
using Gw2LegendaryTracker.Core.Sync;

namespace Gw2LegendaryTracker.Core.Crafting.Forge;

/// <param name="Visited">Items looked at so far.</param>
/// <param name="Queued">Items still to look at; grows while the tree is discovered.</param>
public sealed record ForgeProgress(int Visited, int Queued);

/// <param name="Rejected">Recipes dropped because they contradict the official item description.</param>
public sealed record ForgeFetchResult(
    int Fetched,
    int FromCache,
    int RandomSkipped,
    bool Truncated,
    bool MissingKey,
    string? Error,
    IReadOnlyList<string> Rejected)
{
    public bool Completed => !Truncated && !MissingKey && Error is null;
}

/// <summary>
/// Gets the Mystic Forge recipes of a goal and everything below it, one item at a time, and remembers them.
/// Each recipe is compared with the official item description; a recipe that contradicts it is not used,
/// and one that has no official description to compare with is kept but marked as single-source.
/// </summary>
public sealed class ForgeRecipeCatalog
{
    public static readonly TimeSpan DefaultMaxAge = TimeSpan.FromDays(7);

    /// <summary>Upper bound on lookups in one run, so an unexpectedly large tree cannot hammer the service.</summary>
    public const int MaxRequestsPerRun = 150;

    private static readonly TimeSpan DefaultMinDelay = TimeSpan.FromMilliseconds(1600);

    private readonly IForgeApiClient _forge;
    private readonly IApiKeyStore _keys;
    private readonly IForgeRecipeStore _store;
    private readonly IGw2ApiClient _official;
    private readonly TimeProvider _time;
    private readonly TimeSpan _minDelay;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private HashSet<int>? _materials;

    public ForgeRecipeCatalog(
        IForgeApiClient forge,
        IApiKeyStore keys,
        IForgeRecipeStore store,
        IGw2ApiClient official,
        TimeProvider? time = null,
        TimeSpan? minDelay = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _forge = forge;
        _keys = keys;
        _store = store;
        _official = official;
        _time = time ?? TimeProvider.System;
        _minDelay = minDelay ?? DefaultMinDelay;
        _delay = delay ?? Task.Delay;
    }

    public Task<ForgeRecipeSet> LoadAsync(CancellationToken cancellationToken = default) => _store.LoadAllAsync(cancellationToken);

    /// <summary>
    /// Makes sure the forge recipes below <paramref name="rootItemId"/> are known and not older than <paramref name="maxAge"/>.
    /// Progress is saved per item, so an interrupted run resumes where it stopped. Never throws for service trouble:
    /// the result says what happened and what is cached stays usable.
    /// </summary>
    /// <param name="knownRecipes">Other recipes (the official ones); their ingredients are followed too, as those may have forge recipes.</param>
    public async Task<ForgeFetchResult> EnsureAsync(
        int rootItemId,
        RecipeBook knownRecipes,
        bool forceRefresh = false,
        TimeSpan? maxAge = null,
        IProgress<ForgeProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(knownRecipes);

        var key = await _keys.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (key is null)
        {
            return new ForgeFetchResult(0, 0, 0, false, true, null, []);
        }

        var materials = await GetMaterialsAsync(cancellationToken).ConfigureAwait(false);
        var limit = maxAge ?? DefaultMaxAge;
        var rejected = new List<string>();
        var seen = new HashSet<int>();
        var queue = new Queue<int>([rootItemId]);
        int fetched = 0, cached = 0, random = 0, requests = 0;
        var truncated = false;
        string? error = null;

        while (queue.Count > 0)
        {
            var itemId = queue.Dequeue();
            if (!seen.Add(itemId) || materials.Contains(itemId))
            {
                continue;
            }

            foreach (var recipe in knownRecipes.RecipesFor(itemId))
            {
                Enqueue(recipe.Ingredients);
            }

            var lookedUp = await _store.GetFetchedUtcAsync(itemId, cancellationToken).ConfigureAwait(false);
            IReadOnlyList<Recipe> recipes;
            if (!forceRefresh && lookedUp is { } at && _time.GetUtcNow() - at < limit)
            {
                recipes = await _store.GetRecipesForAsync(itemId, cancellationToken).ConfigureAwait(false);
                cached++;
            }
            else
            {
                if (requests >= MaxRequestsPerRun)
                {
                    truncated = true;
                    break;
                }

                if (requests > 0)
                {
                    await _delay(_minDelay, cancellationToken).ConfigureAwait(false);
                }

                requests++;
                IReadOnlyList<ForgeRecipe> found;
                try
                {
                    found = await _forge.GetRecipesAsync(itemId, key, cancellationToken).ConfigureAwait(false);
                }
                catch (Gw2ApiException ex)
                {
                    error = ex.Message;
                    break;
                }

                random += found.Count(r => r.IsRandom);
                var drafts = await VerifyAsync(itemId, [.. found.Where(r => !r.IsRandom)], rejected, cancellationToken).ConfigureAwait(false);
                await _store.SaveAsync(itemId, drafts, _time.GetUtcNow(), cancellationToken).ConfigureAwait(false);
                recipes = await _store.GetRecipesForAsync(itemId, cancellationToken).ConfigureAwait(false);
                fetched++;
            }

            foreach (var recipe in recipes)
            {
                Enqueue(recipe.Ingredients);
            }

            progress?.Report(new ForgeProgress(seen.Count, queue.Count));
        }

        return new ForgeFetchResult(fetched, cached, random, truncated, false, error, rejected);

        void Enqueue(IEnumerable<Ingredient> ingredients)
        {
            // Raw materials are never looked up, so they are not queued either; that keeps the progress honest.
            foreach (var ingredient in ingredients.Where(i => i.Key.Kind == MaterialKind.Item && !seen.Contains(i.Key.Id) && !materials.Contains(i.Key.Id)))
            {
                queue.Enqueue(ingredient.Key.Id);
            }
        }
    }

    private async Task<IReadOnlyList<ForgeRecipeDraft>> VerifyAsync(
        int itemId,
        List<ForgeRecipe> candidates,
        List<string> rejected,
        CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
        {
            return [];
        }

        // One official lookup gives the output's description and the names of all ingredients.
        var ids = candidates.SelectMany(c => c.Ingredients.Select(i => i.Key.Id)).Append(itemId).Distinct().ToList();
        IReadOnlyList<Items.ItemInfo> info;
        try
        {
            info = await _official.GetItemsAsync(ids, cancellationToken).ConfigureAwait(false);
        }
        catch (Gw2ApiException)
        {
            // Without the official text there is nothing to compare with: keep the recipes, marked as single-source.
            return [.. candidates.Select(c => new ForgeRecipeDraft(c.OutputMax, c.Ingredients, false))];
        }

        var names = info.ToDictionary(i => i.Id, i => i.Name);
        var stated = MysticForgeDescription.TryParse(info.FirstOrDefault(i => i.Id == itemId)?.Description);
        if (stated is null)
        {
            return [.. candidates.Select(c => new ForgeRecipeDraft(c.OutputMax, c.Ingredients, false))];
        }

        var drafts = candidates
            .Select(c => new ForgeRecipeDraft(c.OutputMax, c.Ingredients, MysticForgeDescription.Matches(stated, c.Ingredients.Select(i => (names.GetValueOrDefault(i.Key.Id, string.Empty), i.Count)))))
            .ToList();

        if (drafts.Any(d => d.IsVerified))
        {
            return drafts;
        }

        var name = names.GetValueOrDefault(itemId, $"item {itemId}");
        rejected.Add($"Het recept van \"{name}\" komt niet overeen met de officiële beschrijving ({string.Join(", ", stated.Select(s => $"{s.Count} {s.Name}"))}) en wordt niet gebruikt.");
        return [];
    }

    private async Task<HashSet<int>> GetMaterialsAsync(CancellationToken cancellationToken)
    {
        if (_materials is not null)
        {
            return _materials;
        }

        try
        {
            return _materials = [.. await _official.GetMaterialItemIdsAsync(cancellationToken).ConfigureAwait(false)];
        }
        catch (Gw2ApiException)
        {
            // Not fatal: raw materials simply get looked up too (and have no forge recipe), at the cost of some requests.
            return [];
        }
    }
}
