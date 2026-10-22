using System.Globalization;
using Gw2LegendaryTracker.Core.Crafting;

namespace Gw2LegendaryTracker.Core.Goals;

public sealed record ShortageLine(int ItemId, long Missing, long Needed);

public sealed record ChecklistLine(int ItemId, string StatusText, bool IsDone);

/// <summary>Everything the screens show about one goal, as plain values and Dutch texts.</summary>
/// <param name="Notice">Replaces the numbers when they cannot be trusted (no recipe data, or a plan that could not be made).</param>
public sealed record GoalSummary(
    double Progress,
    string ProgressText,
    string NextStepText,
    string? Notice,
    IReadOnlyList<ShortageLine> Shortages,
    IReadOnlyList<ChecklistLine> Checklist);

/// <summary>Turns a plan into texts for the main window and the overlay. Kept free of UI so it can be tested.</summary>
public static class GoalPresenter
{
    public const string NoRecipeDataNotice =
        "Voor deze Legendary is nog geen receptdata beschikbaar. De app kan alleen laten zien dat je hem nog niet hebt; de onderdelen en tekorten ontbreken.";

    /// <summary>Item ids whose names are needed to show <paramref name="plan"/>.</summary>
    public static IReadOnlySet<int> ReferencedItemIds(GoalPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var ids = new HashSet<int> { plan.Goal.ItemId };
        if (plan.Plan is not { } p)
        {
            return ids;
        }

        ids.UnionWith(p.Materials.Where(m => m.Key.Kind == MaterialKind.Item).Select(m => m.Key.Id));
        ids.UnionWith(ChecklistBuilder.Build(p).Select(c => c.ItemId));
        if (p.Next.Craft is { } craft)
        {
            ids.Add(craft.Recipe.OutputItemId);
        }

        return ids;
    }

    public static GoalSummary Summarize(GoalPlan goalPlan, Func<int, string> itemName, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(goalPlan);
        ArgumentNullException.ThrowIfNull(itemName);
        culture ??= CultureInfo.CurrentCulture;

        if (goalPlan.Plan is not { } plan)
        {
            return new GoalSummary(0, string.Empty, string.Empty, goalPlan.Error ?? "Dit doel kon niet worden doorgerekend.", [], []);
        }

        var progress = string.Create(culture, $"{plan.Progress:P0} · {plan.Root.SatisfiedCount} van {plan.Root.NodeCount} onderdelen klaar");

        var shortages = plan.Shortages
            .Where(m => m.Key.Kind == MaterialKind.Item)
            .Select(m => new ShortageLine(m.Key.Id, m.Missing, m.Needed))
            .ToList();

        var checklist = ChecklistBuilder.Build(plan)
            .Select(c => new ChecklistLine(
                c.ItemId,
                c.IsDone ? "Zelf afgevinkt"
                : c.Missing > 0 ? string.Create(culture, $"Nog {c.Missing:N0} nodig")
                : "Te maken met wat je hebt",
                c.IsDone))
            .ToList();

        return new GoalSummary(
            plan.Progress,
            progress,
            DescribeNext(plan.Next, itemName, culture),
            goalPlan.HasRecipeData ? null : NoRecipeDataNotice,
            shortages,
            checklist);
    }

    public static string DescribeNext(NextStep next, Func<int, string> itemName, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(itemName);
        ArgumentNullException.ThrowIfNull(culture);

        return next.Kind switch
        {
            NextStepKind.Done => "Je hebt deze Legendary al.",
            NextStepKind.Craft when next.Craft is { } craft =>
                string.Create(culture, $"Maak nu: {craft.Times:N0}× {itemName(craft.Recipe.OutputItemId)}{Where(craft.Recipe)}"),
            NextStepKind.Gather when next.Material is { } material =>
                string.Create(culture, $"Verzamel: {next.Amount:N0}× {(material.Kind == MaterialKind.Item ? itemName(material.Id) : $"currency #{material.Id}")}"),
            _ => string.Empty,
        };
    }

    private static string Where(Recipe recipe) =>
        recipe.Source == RecipeSource.Local ? " (in de Mystic Forge)"
        : recipe.Disciplines.Count > 0 ? $" (bij {string.Join(" of ", recipe.Disciplines)})"
        : string.Empty;
}
