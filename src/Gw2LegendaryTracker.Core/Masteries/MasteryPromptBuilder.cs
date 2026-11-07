using System.Globalization;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using Gw2LegendaryTracker.Core.Logging;

namespace Gw2LegendaryTracker.Core.Masteries;

/// <summary>
/// Builds the prompt for an external AI that writes a mastery plan. The text comes from <c>docs/mastery-plan-prompt.md</c>,
/// embedded in this assembly; the player's mastery data is added as JSON. The API key is never part of it.
/// </summary>
public static class MasteryPromptBuilder
{
    public const string ProgressPlaceholder = "{{VOORTGANG}}";

    private const string ResourceName = "Gw2LegendaryTracker.Core.Masteries.MasteryPlanPrompt.md";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly Lazy<string> Template = new(LoadTemplate);

    /// <summary>The prompt text as shipped, without the developer comment and with the placeholder still in it.</summary>
    public static string TemplateText => Template.Value;

    /// <param name="progress">The account's mastery data, or <c>null</c> when unknown (no permission, never synced).</param>
    /// <param name="definitions">The game's mastery tracks, or <c>null</c> when not fetched yet.</param>
    /// <param name="plan">The active plan, to report progress on the achievements it uses; may be <c>null</c>.</param>
    public static string Build(
        MasteryAccountProgress? progress,
        IReadOnlyDictionary<int, MasteryDefinition>? definitions,
        MasteryPlan? plan = null)
    {
        var text = Template.Value.Replace(ProgressPlaceholder, ProgressSection(progress, definitions, plan), StringComparison.Ordinal);

        // Defence in depth: nothing that looks like a key or token may ever leave the app through the clipboard.
        return LogRedactor.Redact(text);
    }

    private static string ProgressSection(MasteryAccountProgress? progress, IReadOnlyDictionary<int, MasteryDefinition>? definitions, MasteryPlan? plan)
    {
        if (progress is null && definitions is not { Count: > 0 })
        {
            return "Er is nog geen voortgang bekend. Ga uit van een account zonder getrainde masteries, tenzij ik onder \"Mijn wensen\" iets anders aangeef.";
        }

        var trained = (progress?.Masteries ?? []).GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.Max(m => m.Level) + 1);
        var unlocked = (progress?.Unlocked ?? []).ToHashSet();
        var tracks = (definitions?.Values ?? [])
            .OrderBy(d => d.Region, StringComparer.Ordinal).ThenBy(d => d.Order).ThenBy(d => d.Id)
            .Select(d => new TrackDto(
                d.Id,
                d.Name,
                d.Region,
                d.Levels.Count,
                progress is null ? null : Math.Min(trained.GetValueOrDefault(d.Id), d.Levels.Count),
                progress is null ? null : unlocked.Contains(d.Id),
                [.. d.Levels.Select((l, i) => new LevelDto(i, l.Name, l.PointCost))]))
            .ToList();

        var achievementIds = plan?.AllSteps.Select(s => s.AchievementId).OfType<int>().ToHashSet() ?? [];
        var data = new ProgressDto(
            progress?.CapturedUtc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            progress is null ? null : [.. progress.Points.Select(p => new PointsDto(p.Region, p.Earned, p.Spent))],
            tracks,
            progress is null ? null : [.. progress.Achievements.Where(a => achievementIds.Contains(a.Id)).Select(a => new AchievementDto(a.Id, a.Done, a.Current, a.Max))]);

        var intro = progress is null
            ? "Mijn voortgang op het account is niet bekend (de app mag die niet lezen of heeft hem nog niet opgehaald). Hieronder staan wel alle mastery-tracks uit de API, met hun echte id's en regio's."
            : "Dit is mijn actuele voortgang uit de officiële API. `trainedLevels` is het aantal getrainde levels van een track; het eerstvolgende level heeft `masteryLevel` = `trainedLevels`.";

        return $"{intro}\n\n```json\n{JsonSerializer.Serialize(data, JsonOptions)}\n```";
    }

    private static string LoadTemplate()
    {
        using var stream = typeof(MasteryPromptBuilder).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The embedded resource {ResourceName} is missing.");
        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal);

        // The file starts with a comment for developers; the AI does not need it.
        if (text.StartsWith("<!--", StringComparison.Ordinal) && text.IndexOf("-->", StringComparison.Ordinal) is var end and >= 0)
        {
            text = text[(end + 3)..];
        }

        return text.TrimStart('\n');
    }

    private sealed record ProgressDto(string? CapturedUtc, List<PointsDto>? RegionPoints, List<TrackDto> MasteryTracks, List<AchievementDto>? PlanAchievements);

    private sealed record PointsDto(string Region, int Earned, int Spent);

    /// <param name="TrainedLevels"><c>null</c> when the account's progress is unknown.</param>
    private sealed record TrackDto(int MasteryId, string Name, string Region, int MaxLevels, int? TrainedLevels, bool? Unlocked, List<LevelDto> Levels);

    private sealed record LevelDto(int MasteryLevel, string Name, int PointCost);

    private sealed record AchievementDto(int AchievementId, bool Done, int Current, int Max);
}
