namespace Gw2LegendaryTracker.Core.Masteries;

/// <summary>Local copy of <c>/v2/masteries</c> (public data), so plans can be checked offline.</summary>
public interface IMasteryDefinitionStore
{
    Task<IReadOnlyList<MasteryDefinition>> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>When the stored tracks were fetched, or <c>null</c> when nothing is stored.</summary>
    Task<DateTimeOffset?> GetFetchedUtcAsync(CancellationToken cancellationToken = default);

    /// <summary>Replaces all stored tracks.</summary>
    Task SaveAsync(IReadOnlyCollection<MasteryDefinition> definitions, DateTimeOffset fetchedUtc, CancellationToken cancellationToken = default);
}

public enum MasterySyncState
{
    /// <summary>The account's mastery data was never fetched.</summary>
    Never,
    Succeeded,

    /// <summary>The API key lacks the "progression" permission.</summary>
    NoPermission,
    Failed,
}

/// <param name="LastSuccessUtc">When the stored account data was fetched; kept when a later fetch fails.</param>
public sealed record MasterySyncStatus(MasterySyncState State, DateTimeOffset? LastSuccessUtc = null, string? Error = null)
{
    public static MasterySyncStatus Initial { get; } = new(MasterySyncState.Never);
}

/// <summary>The account's mastery data from the last mastery sync, and how that sync went.</summary>
public interface IMasteryProgressStore
{
    Task<MasteryAccountProgress?> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Replaces the stored account data as a whole.</summary>
    Task SaveAsync(MasteryAccountProgress progress, CancellationToken cancellationToken = default);

    Task<MasterySyncStatus> LoadStatusAsync(CancellationToken cancellationToken = default);

    Task SaveStatusAsync(MasterySyncStatus status, CancellationToken cancellationToken = default);
}

/// <param name="Overrides">Manual choices by step id: <c>true</c> = done, <c>false</c> = open.</param>
public sealed record StoredMasteryPlan(MasteryPlan Plan, IReadOnlyDictionary<string, bool> Overrides, DateTimeOffset ImportedUtc);

/// <summary>The user's one active mastery plan and the manual ticks on it (user data).</summary>
public interface IMasteryPlanStore
{
    Task<StoredMasteryPlan?> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Replaces the plan and all manual choices in one go.</summary>
    Task ReplaceAsync(MasteryPlan plan, IReadOnlyDictionary<string, bool> manualChoices, DateTimeOffset importedUtc, CancellationToken cancellationToken = default);

    /// <summary>Stores a manual choice for one step; <c>null</c> removes it (back to automatic).</summary>
    Task SetOverrideAsync(string stepId, bool? done, CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}
