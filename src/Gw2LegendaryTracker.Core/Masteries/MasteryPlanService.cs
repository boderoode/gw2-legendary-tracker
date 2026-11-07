namespace Gw2LegendaryTracker.Core.Masteries;

/// <summary>What an import would do, shown before the active plan is replaced.</summary>
/// <param name="KeptChoices">Manual choices that stay, because their step id is also in the new plan.</param>
/// <param name="DroppedChoices">Manual choices that are lost, because their step is not in the new plan.</param>
public sealed record MasteryImportPreview(MasteryPlanParseResult Result, int PhaseCount, int StepCount, int KeptChoices, int DroppedChoices)
{
    public bool Succeeded => Result.Succeeded;
}

/// <summary>The one active mastery plan and the manual choices on its steps.</summary>
public sealed class MasteryPlanService
{
    private readonly IMasteryPlanStore _store;
    private readonly MasteryCatalog _catalog;
    private readonly TimeProvider _time;
    private State _state = State.Empty;

    public MasteryPlanService(IMasteryPlanStore store, MasteryCatalog catalog, TimeProvider? time = null)
    {
        _store = store;
        _catalog = catalog;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Raised after the plan or a manual choice changed; may be raised on any thread.</summary>
    public event EventHandler? Changed;

    public MasteryPlan? Plan => Current.Plan;

    /// <summary>Manual choices by step id: <c>true</c> = done, <c>false</c> = open.</summary>
    public IReadOnlyDictionary<string, bool> Overrides => Current.Overrides;

    public DateTimeOffset? ImportedUtc => Current.ImportedUtc;

    /// <summary>The achievements the plan uses, to ask the API about.</summary>
    public IReadOnlyCollection<int> AchievementIds =>
        Current.Plan is { } plan ? [.. plan.AllSteps.Select(s => s.AchievementId).OfType<int>().Distinct()] : [];

    private State Current => Volatile.Read(ref _state);

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var stored = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
        Publish(stored is null ? State.Empty : new State(stored.Plan, new Dictionary<string, bool>(stored.Overrides, StringComparer.Ordinal), stored.ImportedUtc));
    }

    /// <summary>Validates <paramref name="text"/> (a file or pasted AI answer) without changing anything.</summary>
    public MasteryImportPreview Preview(string? text)
    {
        var result = MasteryPlanSerializer.Parse(text, _catalog.Definitions);
        if (!result.Succeeded)
        {
            return new MasteryImportPreview(result, 0, 0, 0, 0);
        }

        var plan = result.Plan!;
        var kept = KeptChoices(plan, Current.Overrides);
        return new MasteryImportPreview(result, plan.Phases.Count, plan.AllSteps.Count(), kept.Count, Current.Overrides.Count - kept.Count);
    }

    /// <summary>Makes <paramref name="plan"/> the active plan. Manual choices on step ids that are also in the new plan are kept.</summary>
    public Task ReplaceAsync(MasteryPlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return ReplaceAsync(plan, KeptChoices(plan, Current.Overrides), cancellationToken);
    }

    /// <summary>Makes <paramref name="plan"/> the active plan with exactly these manual choices (e.g. from a back-up).</summary>
    public async Task ReplaceAsync(MasteryPlan plan, IReadOnlyDictionary<string, bool> manualChoices, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(manualChoices);

        var choices = KeptChoices(plan, manualChoices);
        var now = _time.GetUtcNow();
        await _store.ReplaceAsync(plan, choices, now, cancellationToken).ConfigureAwait(false);
        Publish(new State(plan, choices, now));
    }

    /// <summary>Sets a manual choice: <c>true</c> = done, <c>false</c> = open, <c>null</c> = back to automatic.</summary>
    public async Task SetOverrideAsync(string stepId, bool? done, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(stepId);
        var current = Current;
        if (current.Plan is null || !current.Plan.AllSteps.Any(s => s.Id == stepId))
        {
            return;
        }

        await _store.SetOverrideAsync(stepId, done, cancellationToken).ConfigureAwait(false);
        var overrides = new Dictionary<string, bool>(current.Overrides, StringComparer.Ordinal);
        if (done is { } value)
        {
            overrides[stepId] = value;
        }
        else
        {
            overrides.Remove(stepId);
        }

        Publish(current with { Overrides = overrides });
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _store.ClearAsync(cancellationToken).ConfigureAwait(false);
        Publish(State.Empty);
    }

    /// <summary>The active plan in its own file format, to give back to an AI; <c>null</c> without a plan.</summary>
    public string? Export() => Current.Plan is { } plan ? MasteryPlanSerializer.Serialize(plan) : null;

    private static Dictionary<string, bool> KeptChoices(MasteryPlan plan, IReadOnlyDictionary<string, bool> choices)
    {
        var ids = plan.AllSteps.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        return choices.Where(c => ids.Contains(c.Key)).ToDictionary(c => c.Key, c => c.Value, StringComparer.Ordinal);
    }

    private void Publish(State state)
    {
        Volatile.Write(ref _state, state);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed record State(MasteryPlan? Plan, Dictionary<string, bool> Overrides, DateTimeOffset? ImportedUtc)
    {
        public static State Empty { get; } = new(null, new Dictionary<string, bool>(StringComparer.Ordinal), null);
    }
}
