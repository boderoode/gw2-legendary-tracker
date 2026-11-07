using System.Globalization;

namespace Gw2LegendaryTracker.Core.Masteries;

/// <summary>A step with everything the screen shows about it.</summary>
/// <param name="Order">Position in the whole plan (0-based), used to keep the plan's order.</param>
public sealed record MasteryStepView(
    MasteryStep Step,
    string PhaseId,
    int Order,
    StepStatus Status,
    string StatusText,
    string? WhereText,
    string? TimeText,
    string? WaitingText);

public sealed record MasteryPhaseView(MasteryPhase Phase, int DoneCount, int TotalCount, double Progress, string ProgressText, IReadOnlyList<MasteryStepView> Steps);

public sealed record MasteryTrackView(int Id, string Name, int Trained, int Max, string Text);

/// <param name="Earned">Points earned in the region; <c>null</c> when unknown.</param>
public sealed record MasteryRegionView(string Region, int? Earned, int? Spent, string PointsText, IReadOnlyList<MasteryTrackView> Tracks);

public sealed record MasteryPlanSummary(
    int DoneCount,
    int TotalCount,
    double Progress,
    string ProgressText,
    string RemainingText,
    MasteryStepView? NextStep,
    IReadOnlyList<MasteryPhaseView> Phases,
    IReadOnlyList<string> Regions);

/// <summary>Turns a plan and its statuses into the texts and numbers of the Masteries tab.</summary>
public static class MasteryPlanPresenter
{
    public static MasteryPlanSummary Summarize(MasteryPlan plan, IReadOnlyDictionary<string, StepStatus> statuses, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(statuses);
        culture ??= CultureInfo.CurrentCulture;

        var titles = plan.AllSteps.ToDictionary(s => s.Id, s => s.Title, StringComparer.Ordinal);
        var order = 0;
        var phases = new List<MasteryPhaseView>();
        foreach (var phase in plan.Phases)
        {
            var steps = new List<MasteryStepView>();
            foreach (var step in phase.Steps)
            {
                var status = statuses[step.Id];
                steps.Add(new MasteryStepView(
                    step,
                    phase.Id,
                    order++,
                    status,
                    StatusText(status),
                    Join(step.Region, step.Map),
                    step.EstimatedMinutes is { } minutes ? FormatMinutes(minutes, culture) : null,
                    status.WaitingFor.Count > 0 ? "Wacht op: " + string.Join(", ", status.WaitingFor.Select(id => titles.GetValueOrDefault(id, id))) : null));
            }

            var phaseDone = steps.Count(s => s.Status.IsDone);
            phases.Add(new MasteryPhaseView(
                phase,
                phaseDone,
                steps.Count,
                Fraction(phaseDone, steps.Count),
                CountText(phaseDone, steps.Count, culture),
                steps));
        }

        var all = phases.SelectMany(p => p.Steps).ToList();
        var done = all.Count(s => s.Status.IsDone);
        var progress = Fraction(done, all.Count);
        var percent = (progress * 100).ToString("0", culture);

        return new MasteryPlanSummary(
            done,
            all.Count,
            progress,
            $"{percent}% · {CountText(done, all.Count, culture)}",
            RemainingText(all, culture),
            NextStep(all),
            phases,
            [.. all.Select(s => s.Step.Region).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)]);
    }

    /// <summary>The first available step: highest priority first, then the plan's own order.</summary>
    public static MasteryStepView? NextStep(IEnumerable<MasteryStepView> steps) =>
        steps.Where(s => s.Status.State == MasteryStepState.Available)
            .OrderBy(s => s.Step.Priority)
            .ThenBy(s => s.Order)
            .FirstOrDefault();

    /// <summary>Applies the filters of the tab. Phases without visible steps are left out; their progress stays that of the whole phase.</summary>
    /// <param name="region"><c>null</c> for all regions.</param>
    public static IReadOnlyList<MasteryPhaseView> Filter(IReadOnlyList<MasteryPhaseView> phases, bool onlyOpen, string? region)
    {
        ArgumentNullException.ThrowIfNull(phases);
        if (!onlyOpen && region is null)
        {
            return phases;
        }

        return
        [
            .. phases
                .Select(p => p with
                {
                    Steps = [.. p.Steps.Where(s =>
                        (!onlyOpen || !s.Status.IsDone)
                        && (region is null || string.Equals(s.Step.Region, region, StringComparison.OrdinalIgnoreCase)))],
                })
                .Where(p => p.Steps.Count > 0),
        ];
    }

    /// <summary>Points per region and the tracks of each region with how far they are trained.</summary>
    public static IReadOnlyList<MasteryRegionView> Regions(
        MasteryAccountProgress? progress,
        IReadOnlyDictionary<int, MasteryDefinition>? definitions,
        CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        var defs = definitions?.Values ?? [];
        var points = (progress?.Points ?? []).GroupBy(p => p.Region, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var trained = (progress?.Masteries ?? []).GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.Max(m => m.Level) + 1);

        // Regions in the order the game added them (lowest track id first); regions only known from the points come last.
        var regions = defs.GroupBy(d => d.Region, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Min(d => d.Id))
            .Select(g => g.Key)
            .ToList();
        regions.AddRange(points.Keys.Where(r => !regions.Contains(r, StringComparer.OrdinalIgnoreCase)));

        var result = new List<MasteryRegionView>();
        foreach (var region in regions)
        {
            var tracks = defs.Where(d => string.Equals(d.Region, region, StringComparison.OrdinalIgnoreCase))
                .OrderBy(d => d.Order).ThenBy(d => d.Id)
                .Select(d =>
                {
                    var level = Math.Min(trained.GetValueOrDefault(d.Id), d.Levels.Count);
                    return new MasteryTrackView(d.Id, d.Name, level, d.Levels.Count, CountOf(level, d.Levels.Count, culture));
                })
                .ToList();

            var text = points.TryGetValue(region, out var p)
                ? string.Create(culture, $"{p.Earned:N0} punten verdiend, {p.Spent:N0} uitgegeven")
                : progress is null ? "Punten onbekend" : "Nog geen punten";
            result.Add(new MasteryRegionView(region, p?.Earned, p?.Spent, text, tracks));
        }

        return result;
    }

    public static string StatusText(StepStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        var text = status.State switch
        {
            MasteryStepState.DoneAuto => "Klaar (automatisch)",
            MasteryStepState.DoneManual => "Klaar (handmatig)",
            MasteryStepState.Available => "Beschikbaar",
            _ => "Geblokkeerd",
        };
        return status.IsOverridden ? text + " · handmatig aangepast" : text;
    }

    public static string FormatMinutes(int minutes, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        if (minutes < 60)
        {
            return string.Create(culture, $"{minutes} min");
        }

        var hours = minutes / 60;
        var rest = minutes % 60;
        return rest == 0 ? string.Create(culture, $"{hours:N0} u") : string.Create(culture, $"{hours:N0} u {rest} min");
    }

    private static string RemainingText(List<MasteryStepView> steps, CultureInfo culture)
    {
        var open = steps.Where(s => !s.Status.IsDone).ToList();
        if (open.Count == 0)
        {
            return steps.Count == 0 ? "Geen stappen" : "Alle stappen zijn klaar";
        }

        var withTime = open.Where(s => s.Step.EstimatedMinutes is not null).ToList();
        var withoutTime = open.Count - withTime.Count;
        var unknown = withoutTime == 1 ? "1 stap zonder schatting" : string.Create(culture, $"{withoutTime} stappen zonder schatting");
        if (withTime.Count == 0)
        {
            return $"Nog geen tijdsinschatting ({unknown})";
        }

        var total = FormatMinutes(withTime.Sum(s => s.Step.EstimatedMinutes!.Value), culture);
        return withoutTime == 0 ? $"Nog ongeveer {total}" : $"Nog ongeveer {total}, plus {unknown}";
    }

    private static string? Join(params string?[] parts)
    {
        var present = parts.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        return present.Count == 0 ? null : string.Join(" · ", present);
    }

    private static double Fraction(int done, int total) => total == 0 ? 0 : (double)done / total;

    private static string CountText(int done, int total, CultureInfo culture) =>
        string.Create(culture, $"{done}/{total} {(total == 1 ? "stap" : "stappen")}");

    private static string CountOf(int done, int total, CultureInfo culture) => string.Create(culture, $"{done}/{total}");
}
