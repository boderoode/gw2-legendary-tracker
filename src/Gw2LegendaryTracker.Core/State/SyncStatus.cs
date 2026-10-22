namespace Gw2LegendaryTracker.Core.State;

public enum SyncPhase
{
    /// <summary>No API key configured, or no sync has been attempted yet.</summary>
    Idle,
    Syncing,
    Succeeded,
    Failed,
}

public sealed record SyncStatus(SyncPhase Phase, DateTimeOffset? LastSuccessUtc = null, string? Error = null)
{
    public static SyncStatus Initial { get; } = new(SyncPhase.Idle);
}
