namespace Gw2LegendaryTracker.Core.Masteries;

public enum MasteryStepState
{
    DoneAuto,
    DoneManual,
    Available,
    Blocked,
}

/// <param name="IsTracked">True when the app can check this step from the account data.</param>
/// <param name="AutoDone">What the account data says (always <c>false</c> for untracked steps).</param>
/// <param name="IsOverridden">True when a manual choice overrules what the account data says.</param>
/// <param name="WaitingFor">Ids of the required steps that are not done yet.</param>
public sealed record StepStatus(MasteryStepState State, bool IsTracked, bool AutoDone, bool IsOverridden, IReadOnlyList<string> WaitingFor)
{
    public bool IsDone => State is MasteryStepState.DoneAuto or MasteryStepState.DoneManual;
}

/// <summary>Works out the status of every step from the account data, the manual ticks and the "requires" links.</summary>
public static class MasteryStatusCalculator
{
    /// <param name="progress">The last mastery sync, or <c>null</c> when there is none (no permission, never synced).</param>
    /// <param name="definitions">Known mastery tracks; a mastery step whose track is unknown is treated as manual.</param>
    /// <param name="overrides">Manual choices by step id: <c>true</c> = done, <c>false</c> = open, absent = automatic.</param>
    public static IReadOnlyDictionary<string, StepStatus> Compute(
        MasteryPlan plan,
        MasteryAccountProgress? progress,
        IReadOnlyDictionary<int, MasteryDefinition>? definitions,
        IReadOnlyDictionary<string, bool> overrides)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(overrides);

        var levels = (progress?.Masteries ?? []).GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.Max(m => m.Level));
        var achievements = (progress?.Achievements ?? []).GroupBy(a => a.Id).ToDictionary(g => g.Key, g => g.Any(a => a.Done));

        var done = new Dictionary<string, bool>(StringComparer.Ordinal);
        var facts = new Dictionary<string, (bool Tracked, bool Auto, bool? Manual)>(StringComparer.Ordinal);
        foreach (var step in plan.AllSteps)
        {
            var (tracked, auto) = Automatic(step, progress, definitions, levels, achievements);
            bool? manual = overrides.TryGetValue(step.Id, out var value) ? value : null;
            facts[step.Id] = (tracked, auto, manual);
            done[step.Id] = manual ?? auto;
        }

        var result = new Dictionary<string, StepStatus>(StringComparer.Ordinal);
        foreach (var step in plan.AllSteps)
        {
            var (tracked, auto, manual) = facts[step.Id];
            var overridden = tracked && manual is { } m && m != auto;
            MasteryStepState state;
            IReadOnlyList<string> waiting = [];
            if (done[step.Id])
            {
                state = manual is null ? MasteryStepState.DoneAuto : MasteryStepState.DoneManual;
            }
            else
            {
                waiting = [.. step.Requires.Where(r => !done.GetValueOrDefault(r))];
                state = waiting.Count > 0 ? MasteryStepState.Blocked : MasteryStepState.Available;
            }

            result[step.Id] = new StepStatus(state, tracked, auto, overridden, waiting);
        }

        return result;
    }

    private static (bool Tracked, bool Done) Automatic(
        MasteryStep step,
        MasteryAccountProgress? progress,
        IReadOnlyDictionary<int, MasteryDefinition>? definitions,
        Dictionary<int, int> levels,
        Dictionary<int, bool> achievements)
    {
        if (progress is null)
        {
            return (false, false);
        }

        if (step.Type == MasteryStepType.MasteryLevel && step.MasteryId is { } masteryId && step.MasteryLevel is { } level
            && definitions is not null && definitions.ContainsKey(masteryId))
        {
            return (true, levels.TryGetValue(masteryId, out var trained) && trained >= level);
        }

        if (step.AchievementId is { } achievementId)
        {
            return (true, achievements.GetValueOrDefault(achievementId));
        }

        return (false, false);
    }
}
