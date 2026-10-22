using Gw2LegendaryTracker.Core.Gw2;

namespace Gw2LegendaryTracker.Core.Sync;

public interface ISnapshotStore
{
    /// <summary>Replaces the stored snapshot atomically, so a failed save keeps the previous one.</summary>
    Task SaveAsync(AccountSnapshot snapshot, CancellationToken cancellationToken = default);

    Task<AccountSnapshot?> LoadAsync(CancellationToken cancellationToken = default);
}
