namespace Gw2LegendaryTracker.Core.Goals;

public interface IGoalStore
{
    Task<IReadOnlyList<Goal>> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Replaces the stored goals atomically, so a failed save keeps the previous list.</summary>
    Task SaveAsync(IReadOnlyList<Goal> goals, CancellationToken cancellationToken = default);
}
