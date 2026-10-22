namespace Gw2LegendaryTracker.Core.Goals;

/// <summary>
/// The user's ordered list of goals, shared by the main window and the overlay.
/// Every change is stored first; observers are told only after the write succeeded.
/// </summary>
public sealed class GoalService : IDisposable
{
    public const int MaxNoteLength = 2000;

    private readonly IGoalStore _store;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<Goal> _goals = [];

    public GoalService(IGoalStore store) => _store = store;

    public IReadOnlyList<Goal> Goals => Volatile.Read(ref _goals);

    /// <summary>Raised after the list changed. May fire on any thread.</summary>
    public event EventHandler<IReadOnlyList<Goal>>? Changed;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var loaded = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
        Volatile.Write(ref _goals, loaded);
        Changed?.Invoke(this, loaded);
    }

    /// <summary>Adds a goal at the end of the list. Adding the same item twice does nothing.</summary>
    public Task AddAsync(int itemId, CancellationToken cancellationToken = default) =>
        UpdateAsync(goals => goals.Any(g => g.ItemId == itemId) ? goals : [.. goals, new Goal(itemId)], cancellationToken);

    /// <summary>Replaces the whole list, e.g. after an import. The new list is stored before anyone is told.</summary>
    public Task ReplaceAsync(IReadOnlyList<Goal> goals, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(goals);
        return UpdateAsync(_ => [.. goals], cancellationToken);
    }

    public Task RemoveAsync(int itemId, CancellationToken cancellationToken = default) =>
        UpdateAsync(goals => [.. goals.Where(g => g.ItemId != itemId)], cancellationToken);

    /// <summary>Moves a goal up (negative) or down (positive) in priority. Moving past either end stops there.</summary>
    public Task MoveAsync(int itemId, int offset, CancellationToken cancellationToken = default) =>
        UpdateAsync(
            goals =>
            {
                var from = goals.ToList().FindIndex(g => g.ItemId == itemId);
                if (from < 0)
                {
                    return goals;
                }

                var to = Math.Clamp(from + offset, 0, goals.Count - 1);
                var list = goals.ToList();
                var goal = list[from];
                list.RemoveAt(from);
                list.Insert(to, goal);
                return list;
            },
            cancellationToken);

    public Task SetNoteAsync(int itemId, string note, CancellationToken cancellationToken = default)
    {
        var trimmed = (note ?? string.Empty).Trim();
        if (trimmed.Length > MaxNoteLength)
        {
            trimmed = trimmed[..MaxNoteLength];
        }

        return UpdateGoalAsync(itemId, g => g with { Note = trimmed }, cancellationToken);
    }

    /// <summary>Ticks a component of a goal on or off by hand.</summary>
    public Task SetCompletedAsync(int goalItemId, int componentItemId, bool isDone, CancellationToken cancellationToken = default) =>
        UpdateGoalAsync(
            goalItemId,
            g =>
            {
                var set = new HashSet<int>(g.CompletedItemIds);
                _ = isDone ? set.Add(componentItemId) : set.Remove(componentItemId);
                return g with { CompletedItemIds = set };
            },
            cancellationToken);

    public void Dispose() => _gate.Dispose();

    private Task UpdateGoalAsync(int itemId, Func<Goal, Goal> change, CancellationToken cancellationToken) =>
        UpdateAsync(goals => [.. goals.Select(g => g.ItemId == itemId ? change(g) : g)], cancellationToken);

    private async Task UpdateAsync(Func<IReadOnlyList<Goal>, IReadOnlyList<Goal>> change, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = _goals;
            var next = change(current);
            if (next.SequenceEqual(current))
            {
                return;
            }

            await _store.SaveAsync(next, cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _goals, next);
            Changed?.Invoke(this, next);
        }
        finally
        {
            _gate.Release();
        }
    }
}
