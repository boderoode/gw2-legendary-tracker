namespace Gw2LegendaryTracker.Core.Masteries;

/// <summary>What an import would do, shown before the active plan is replaced.</summary>
/// <param name="KeptChoices">Manual choices that stay, because their step id is also in the new plan.</param>
/// <param name="DroppedChoices">Manual choices that are lost, because their step is not in the new plan.</param>
public sealed record MasteryImportPreview(MasteryPlanParseResult Result, int PhaseCount, int StepCount, int KeptChoices, int DroppedChoices)
{
    public bool Succeeded => Result.Succeeded;
}

/// <summary>The one active mastery plan and the manual choices on its steps.</summary>
public sealed class MasteryPlanService : IDisposable
{
    private readonly IMasteryPlanStore _store;
    private readonly SemaphoreSlim _gate = new(1, 1);
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

    /// <summary>Raised after another plan became the active one (an import or a restored back-up), after <see cref="Changed"/>.</summary>
    public event EventHandler? PlanReplaced;

    public MasteryPlan? Plan => Current.Plan;

    /// <summary>Manual choices by step id: <c>true</c> = done, <c>false</c> = open.</summary>
    public IReadOnlyDictionary<string, bool> Overrides => Current.Overrides;

    public DateTimeOffset? ImportedUtc => Current.ImportedUtc;

    /// <summary>The achievements the plan uses, to ask the API about.</summary>
    public IReadOnlyCollection<int> AchievementIds => Current.Plan?.AchievementIds ?? [];

    private State Current => Volatile.Read(ref _state);

    public Task LoadAsync(CancellationToken cancellationToken = default) =>
        WriteAsync(
            async _ =>
            {
                var stored = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
                return stored is null
                    ? State.Empty
                    : new State(stored.Plan, new Dictionary<string, bool>(stored.Overrides, StringComparer.Ordinal), stored.ImportedUtc);
            },
            cancellationToken);

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
        return ReplaceCoreAsync(plan, current => current.Overrides, cancellationToken);
    }

    /// <summary>Makes <paramref name="plan"/> the active plan with exactly these manual choices (e.g. from a back-up).</summary>
    public Task ReplaceAsync(MasteryPlan plan, IReadOnlyDictionary<string, bool> manualChoices, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(manualChoices);
        return ReplaceCoreAsync(plan, _ => manualChoices, cancellationToken);
    }

    /// <summary>Sets a manual choice: <c>true</c> = done, <c>false</c> = open, <c>null</c> = back to automatic.</summary>
    public Task SetOverrideAsync(string stepId, bool? done, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(stepId);
        return WriteAsync(
            async current =>
            {
                if (current.Plan is null || !current.Plan.AllSteps.Any(s => s.Id == stepId))
                {
                    return null;
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

                return current with { Overrides = overrides };
            },
            cancellationToken);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default) =>
        WriteAsync(
            async _ =>
            {
                await _store.ClearAsync(cancellationToken).ConfigureAwait(false);
                return State.Empty;
            },
            cancellationToken);

    public void Dispose() => _gate.Dispose();

    private async Task ReplaceCoreAsync(MasteryPlan plan, Func<State, IReadOnlyDictionary<string, bool>> choicesFrom, CancellationToken cancellationToken)
    {
        await WriteAsync(
            async current =>
            {
                // Read under the gate, so a tick that was just stored is kept as well.
                var choices = KeptChoices(plan, choicesFrom(current));
                var now = _time.GetUtcNow();
                await _store.ReplaceAsync(plan, choices, now, cancellationToken).ConfigureAwait(false);
                return new State(plan, choices, now);
            },
            cancellationToken).ConfigureAwait(false);
        PlanReplaced?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Runs one change at a time: store first, then publish. Each change starts from the state the previous one left,
    /// so quick ticks one after the other never undo each other.
    /// </summary>
    /// <param name="change">Returns the new state, or <c>null</c> when nothing changed.</param>
    private async Task WriteAsync(Func<State, Task<State?>> change, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        State? next;
        try
        {
            next = await change(Current).ConfigureAwait(false);
            if (next is not null)
            {
                Volatile.Write(ref _state, next);
            }
        }
        finally
        {
            _gate.Release();
        }

        if (next is not null)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The active plan in its own file format, to give back to an AI; <c>null</c> without a plan.</summary>
    public string? Export() => Current.Plan is { } plan ? MasteryPlanSerializer.Serialize(plan) : null;

    private static Dictionary<string, bool> KeptChoices(MasteryPlan plan, IReadOnlyDictionary<string, bool> choices)
    {
        var ids = plan.AllSteps.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        return choices.Where(c => ids.Contains(c.Key)).ToDictionary(c => c.Key, c => c.Value, StringComparer.Ordinal);
    }

    private sealed record State(MasteryPlan? Plan, Dictionary<string, bool> Overrides, DateTimeOffset? ImportedUtc)
    {
        public static State Empty { get; } = new(null, new Dictionary<string, bool>(StringComparer.Ordinal), null);
    }
}
