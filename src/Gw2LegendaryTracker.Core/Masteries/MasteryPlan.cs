namespace Gw2LegendaryTracker.Core.Masteries;

/// <summary>What a step asks the player to do. Only some types can be checked automatically.</summary>
public enum MasteryStepType
{
    Other,
    MasteryPoint,
    MasteryLevel,
    Experience,
    Achievement,
    Story,
    Currency,
}

public enum MasteryPriority
{
    High,
    Normal,
    Low,
}

public sealed record MasteryLink(string Label, string Url);

/// <summary>One step of a mastery plan, already validated and cleaned by <see cref="MasteryPlanSerializer"/>.</summary>
/// <param name="MasteryLevel">Zero-based index into the mastery's levels, the same meaning as <c>/v2/account/masteries</c>.</param>
public sealed record MasteryStep(
    string Id,
    string Title,
    string Description,
    MasteryStepType Type,
    string? Region,
    int? MasteryId,
    int? MasteryLevel,
    int? AchievementId,
    string? Map,
    string? Waypoint,
    int? EstimatedMinutes,
    MasteryPriority Priority,
    IReadOnlyList<string> Requires,
    IReadOnlyList<MasteryLink> Links,
    string? Notes)
{
    public bool Equals(MasteryStep? other) =>
        other is not null
        && Id == other.Id && Title == other.Title && Description == other.Description && Type == other.Type
        && Region == other.Region && MasteryId == other.MasteryId && MasteryLevel == other.MasteryLevel
        && AchievementId == other.AchievementId && Map == other.Map && Waypoint == other.Waypoint
        && EstimatedMinutes == other.EstimatedMinutes && Priority == other.Priority
        && Requires.SequenceEqual(other.Requires) && Links.SequenceEqual(other.Links) && Notes == other.Notes;

    public override int GetHashCode() => HashCode.Combine(Id, Title, Type, MasteryId, MasteryLevel, AchievementId);
}

public sealed record MasteryPhase(string Id, string Title, string Goal, IReadOnlyList<MasteryStep> Steps)
{
    public bool Equals(MasteryPhase? other) =>
        other is not null && Id == other.Id && Title == other.Title && Goal == other.Goal && Steps.SequenceEqual(other.Steps);

    public override int GetHashCode() => HashCode.Combine(Id, Title, Goal, Steps.Count);
}

/// <summary>A mastery plan written by an external AI (format <c>gw2-mastery-plan</c>), after validation.</summary>
public sealed record MasteryPlan(
    string Title,
    DateTimeOffset? CreatedUtc,
    string Author,
    string Summary,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<MasteryPhase> Phases)
{
    public IEnumerable<MasteryStep> AllSteps => Phases.SelectMany(p => p.Steps);

    /// <summary>The achievements the plan's steps refer to, each once.</summary>
    public IReadOnlyCollection<int> AchievementIds => [.. AllSteps.Select(s => s.AchievementId).OfType<int>().Distinct()];

    public bool Equals(MasteryPlan? other) =>
        other is not null && Title == other.Title && CreatedUtc == other.CreatedUtc && Author == other.Author
        && Summary == other.Summary && Assumptions.SequenceEqual(other.Assumptions) && Phases.SequenceEqual(other.Phases);

    public override int GetHashCode() => HashCode.Combine(Title, CreatedUtc, Author, Phases.Count);
}
