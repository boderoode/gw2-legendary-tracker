using System.Net;
using System.Text;
using Gw2LegendaryTracker.Core.Crafting;
using Gw2LegendaryTracker.Core.Goals;
using Gw2LegendaryTracker.Core.Gw2;
using Gw2LegendaryTracker.Core.Items;
using Gw2LegendaryTracker.Core.Settings;
using Gw2LegendaryTracker.Core.Sync;

namespace Gw2LegendaryTracker.Tests;

internal static class TestData
{
    /// <summary>Synthetic key with the real shape. Never use a real key in tests.</summary>
    public const string Key = "11111111-2222-3333-4444-55555555555555555555-6666-7777-8888-999999999999";

    public static AccountSnapshot Snapshot(DateTimeOffset? at = null) => new(
        "Test.1234",
        at ?? new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
        [
            new InventoryEntry(19721, 250, InventorySource.Materials),
            new InventoryEntry(30684, 1, InventorySource.Bank),
            new InventoryEntry(19976, 3, InventorySource.Character, "Hero One"),
        ],
        [new WalletEntry(1, 12345), new WalletEntry(23, 40)]);
}

internal sealed class FakeApiKeyStore : IApiKeyStore
{
    public string? Key { get; set; }

    public Task<string?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Key);

    public Task SaveAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        Key = apiKey;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(CancellationToken cancellationToken = default)
    {
        Key = null;
        return Task.CompletedTask;
    }
}

internal sealed class FakeSnapshotStore : ISnapshotStore
{
    public AccountSnapshot? Saved { get; set; }

    public Exception? SaveFailure { get; init; }

    public Task SaveAsync(AccountSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        if (SaveFailure is not null)
        {
            return Task.FromException(SaveFailure);
        }

        Saved = snapshot;
        return Task.CompletedTask;
    }

    public Task<AccountSnapshot?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Saved);
}

internal sealed class FakeApiClient : IGw2ApiClient
{
    public Gw2TokenInfo Token { get; set; } = new("id", "Test key", [.. ApiPermissions.Required]);

    public Exception? Failure { get; set; }

    public TaskCompletionSource? Gate { get; set; }

    public int SnapshotCalls { get; private set; }

    public List<IReadOnlyCollection<int>> ItemRequests { get; } = [];

    /// <summary>When set, only these items exist (id to name); otherwise every id resolves to a synthetic item.</summary>
    public Dictionary<int, string>? KnownItems { get; set; }

    public Dictionary<int, string>? ItemDescriptions { get; set; }

    public List<int> LegendaryIds { get; set; } = [];

    public Task<IReadOnlyList<int>> GetLegendaryItemIdsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<int>>(LegendaryIds);

    public List<int> RecipeIds { get; set; } = [];

    public Dictionary<int, Recipe> Recipes { get; } = [];

    public List<IReadOnlyCollection<int>> RecipeRequests { get; } = [];

    public Task<IReadOnlyList<int>> GetRecipeIdsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<int>>(RecipeIds);

    public Task<IReadOnlyList<Recipe>> GetRecipesAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default)
    {
        RecipeRequests.Add(ids);
        return Task.FromResult<IReadOnlyList<Recipe>>([.. ids.Where(Recipes.ContainsKey).Select(id => Recipes[id])]);
    }

    public Exception? ItemFailure { get; set; }

    public Task<IReadOnlyList<ItemInfo>> GetItemsAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default)
    {
        ItemRequests.Add(ids);
        if (ItemFailure is not null)
        {
            return Task.FromException<IReadOnlyList<ItemInfo>>(ItemFailure);
        }

        if (KnownItems is not null)
        {
            return Task.FromResult<IReadOnlyList<ItemInfo>>([.. ids.Where(KnownItems.ContainsKey).Select(id => new ItemInfo(id, KnownItems[id], null, "Basic", ItemDescriptions?.GetValueOrDefault(id)))]);
        }

        return Task.FromResult<IReadOnlyList<ItemInfo>>([.. ids.Select(id => new ItemInfo(id, $"Item {id}", $"https://icons/{id}.png", "Rare"))]);
    }

    public Task<Gw2TokenInfo> GetTokenInfoAsync(string apiKey, CancellationToken cancellationToken = default) =>
        Failure is null ? Task.FromResult(Token) : Task.FromException<Gw2TokenInfo>(Failure);

    public async Task<AccountSnapshot> GetAccountSnapshotAsync(string apiKey, DateTimeOffset capturedUtc, CancellationToken cancellationToken = default)
    {
        SnapshotCalls++;
        if (Gate is not null)
        {
            await Gate.Task;
        }

        return TestData.Snapshot(capturedUtc);
    }
}

internal sealed class FakeSettingsStore : ISettingsStore
{
    public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(new AppSettings());

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal sealed class StubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

    public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

    public List<HttpRequestMessage> Requests { get; } = [];

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(_respond(request));
    }
}

internal sealed class ThrowingHandler : HttpMessageHandler
{
    private readonly Exception _exception;

    public ThrowingHandler(Exception exception) => _exception = exception;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromException<HttpResponseMessage>(_exception);
}

internal sealed class FakeItemInfoStore : IItemInfoStore
{
    public Dictionary<int, ItemInfo> Items { get; } = [];

    public int SaveCalls { get; private set; }

    public Task<IReadOnlyDictionary<int, ItemInfo>> GetAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<int, ItemInfo>>(ids.Where(Items.ContainsKey).ToDictionary(id => id, id => Items[id]));

    public Task SaveAsync(IReadOnlyCollection<ItemInfo> items, CancellationToken cancellationToken = default)
    {
        SaveCalls++;
        foreach (var item in items)
        {
            Items[item.Id] = item;
        }

        return Task.CompletedTask;
    }
}

internal sealed class FakeRecipeStore : IRecipeStore
{
    public Dictionary<int, Recipe> Recipes { get; } = [];

    public Task<IReadOnlySet<int>> GetKnownIdsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<int>>(Recipes.Keys.ToHashSet());

    public Task SaveAsync(IReadOnlyCollection<Recipe> recipes, CancellationToken cancellationToken = default)
    {
        foreach (var recipe in recipes)
        {
            Recipes[recipe.Id] = recipe;
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Recipe>> LoadAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Recipe>>([.. Recipes.Values]);
}

internal sealed class FakeGoalStore : IGoalStore
{
    public IReadOnlyList<Goal> Saved { get; set; } = [];

    public int SaveCount { get; private set; }

    public Exception? Failure { get; set; }

    public Task<IReadOnlyList<Goal>> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Saved);

    public Task SaveAsync(IReadOnlyList<Goal> goals, CancellationToken cancellationToken = default)
    {
        if (Failure is not null)
        {
            return Task.FromException(Failure);
        }

        Saved = goals;
        SaveCount++;
        return Task.CompletedTask;
    }
}

internal sealed class FakeLegendaryStore : ILegendaryStore
{
    public List<int> Ids { get; } = [];

    public Task<IReadOnlyList<int>> LoadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<int>>([.. Ids]);

    public Task SaveAsync(IReadOnlyCollection<int> itemIds, CancellationToken cancellationToken = default)
    {
        Ids.Clear();
        Ids.AddRange(itemIds);
        return Task.CompletedTask;
    }
}
