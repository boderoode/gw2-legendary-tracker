namespace Gw2LegendaryTracker.Core.Items;

/// <summary>Local cache of public item data. Kept apart from user data: it can be refreshed or dropped at any time.</summary>
public interface IItemInfoStore
{
    Task<IReadOnlyDictionary<int, ItemInfo>> GetAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default);

    Task SaveAsync(IReadOnlyCollection<ItemInfo> items, CancellationToken cancellationToken = default);
}
