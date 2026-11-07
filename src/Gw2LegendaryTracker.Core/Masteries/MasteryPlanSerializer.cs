using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Gw2LegendaryTracker.Core.Masteries;

/// <param name="Plan">The cleaned plan; <c>null</c> when the file was refused.</param>
/// <param name="Warnings">Things that were left out or corrected; the plan is still usable.</param>
/// <param name="Error">Why the file cannot be imported at all; <c>null</c> on success.</param>
public sealed record MasteryPlanParseResult(MasteryPlan? Plan, IReadOnlyList<string> Warnings, string? Error)
{
    public bool Succeeded => Error is null && Plan is not null;

    public static MasteryPlanParseResult Failure(string error) => new(null, [], error);
}

/// <summary>
/// Reads and writes mastery plans in the <c>gw2-mastery-plan</c> format, which an external AI writes.
/// A file that is structurally wrong is refused as a whole; single bad values inside a valid file are corrected and reported,
/// the same way <see cref="Export.UserDataSerializer"/> treats the app's own exports.
/// </summary>
public static partial class MasteryPlanSerializer
{
    public const string FormatName = "gw2-mastery-plan";

    public const int CurrentVersion = 1;

    public const int MaxBytes = 1_000_000;

    public const int MaxPhases = 20;

    public const int MaxSteps = 300;

    public const int MaxIdLength = 64;

    public const int MaxShortText = 200;

    public const int MaxLongText = 2000;

    public const int MaxAssumptionLength = 500;

    public const int MaxAssumptions = 30;

    public const int MaxUrlLength = 500;

    public const int MaxLinksPerStep = 10;

    /// <summary>A step estimated at more than 1000 hours is not a real estimate.</summary>
    public const int MaxEstimatedMinutes = 60_000;

    private const string WikiHost = "wiki.guildwars2.com";

    private static readonly JsonSerializerOptions ReadOptions = new(JsonSerializerDefaults.Web)
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    // The file is read by people and AIs, never embedded in HTML: keep accents and arrows readable instead of \u-escaped.
    private static readonly JsonSerializerOptions WriteOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonSerializerOptions CompactOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly Dictionary<string, MasteryStepType> TypeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mastery-point"] = MasteryStepType.MasteryPoint,
        ["mastery-level"] = MasteryStepType.MasteryLevel,
        ["experience"] = MasteryStepType.Experience,
        ["achievement"] = MasteryStepType.Achievement,
        ["story"] = MasteryStepType.Story,
        ["currency"] = MasteryStepType.Currency,
        ["other"] = MasteryStepType.Other,
    };

    private static readonly Dictionary<string, MasteryPriority> PriorityNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["high"] = MasteryPriority.High,
        ["normal"] = MasteryPriority.Normal,
        ["low"] = MasteryPriority.Low,
    };

    /// <summary>The contract name of a step type, e.g. <c>mastery-level</c>.</summary>
    public static string TypeName(MasteryStepType type) => TypeNames.First(p => p.Value == type).Key;

    public static string PriorityName(MasteryPriority priority) => PriorityNames.First(p => p.Value == priority).Key;

    /// <summary>
    /// Returns the JSON inside the first Markdown code block (<c>```json … ```</c>), as an AI usually answers;
    /// text without a code block is returned trimmed.
    /// </summary>
    public static string ExtractJson(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var match = CodeBlock().Match(text);
        return (match.Success ? match.Groups["body"].Value : text).Trim();
    }

    /// <summary>
    /// Writes the plan readably indented. A plan near the size limit is written compact instead,
    /// so every plan the app accepted can be imported again.
    /// </summary>
    public static string Serialize(MasteryPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var dto = ToDto(plan);
        var json = JsonSerializer.Serialize(dto, WriteOptions) + "\n";
        return Encoding.UTF8.GetByteCount(json) <= MaxBytes ? json : JsonSerializer.Serialize(dto, CompactOptions) + "\n";
    }

    /// <param name="definitions">
    /// The known mastery tracks by id, to check <c>masteryId</c> and <c>masteryLevel</c>. <c>null</c> or empty when they
    /// are not available (offline, never fetched): the ids are then kept and one warning says they were not checked.
    /// </param>
    /// <param name="maxBytes">
    /// The size limit. Plans from outside use <see cref="MaxBytes"/>; the app's own stored copy and a plan inside a user data
    /// export (which has its own limit) pass a larger value, because escaping and indentation can make the same plan longer.
    /// </param>
    public static MasteryPlanParseResult Parse(string? text, IReadOnlyDictionary<int, MasteryDefinition>? definitions, int maxBytes = MaxBytes) =>
        ParseCore(text, definitions, maxBytes, checkStoredSize: true);

    /// <summary>
    /// Reads the app's own stored copy of a plan. It passed <see cref="Parse"/> when it was imported, so no size limits apply:
    /// a stored plan must always load again.
    /// </summary>
    public static MasteryPlanParseResult ReadStored(string? json) => ParseCore(json, definitions: null, int.MaxValue, checkStoredSize: false);

    private static MasteryPlanParseResult ParseCore(string? text, IReadOnlyDictionary<int, MasteryDefinition>? definitions, int maxBytes, bool checkStoredSize)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return MasteryPlanParseResult.Failure("Het plan is leeg.");
        }

        if (Encoding.UTF8.GetByteCount(text) > maxBytes)
        {
            return MasteryPlanParseResult.Failure("Het plan is te groot (meer dan 1 MB).");
        }

        var json = ExtractJson(text);
        if (json.Length == 0)
        {
            return MasteryPlanParseResult.Failure("Het plan is leeg.");
        }

        EnvelopeDto? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<EnvelopeDto>(json, ReadOptions);
        }
        catch (JsonException)
        {
            return MasteryPlanParseResult.Failure("Het plan is geen geldige JSON: de inhoud kan niet worden gelezen.");
        }

        if (envelope is null || !string.Equals(envelope.Format?.Trim(), FormatName, StringComparison.Ordinal))
        {
            return MasteryPlanParseResult.Failure($"Dit is geen masteryplan: het veld \"format\" moet \"{FormatName}\" zijn.");
        }

        if (envelope.Version > CurrentVersion)
        {
            return MasteryPlanParseResult.Failure($"Dit plan gebruikt een nieuwere versie van het formaat ({envelope.Version.ToString(CultureInfo.InvariantCulture)}). Werk de app bij om het te importeren.");
        }

        if (envelope.Version < 1)
        {
            return MasteryPlanParseResult.Failure("Het plan heeft geen geldige versie.");
        }

        var result = new Cleaner(definitions).Clean(envelope);

        // The app writes every field explicitly (also null and empty ones), so a compact source can grow when it is written
        // back. Refuse a plan that the app could not export and import again.
        if (checkStoredSize && result.Plan is { } plan && Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(ToDto(plan), CompactOptions)) > MaxBytes)
        {
            return MasteryPlanParseResult.Failure("Het plan is te groot (meer dan 1 MB) zodra de app het opslaat. Maak het plan kleiner, bijvoorbeeld met kortere teksten.");
        }

        return result;
    }

    private sealed class Cleaner(IReadOnlyDictionary<int, MasteryDefinition>? definitions)
    {
        private readonly List<string> _warnings = [];
        private readonly IReadOnlyDictionary<int, MasteryDefinition>? _definitions = definitions is { Count: > 0 } ? definitions : null;
        private bool _reportedUnchecked;

        public MasteryPlanParseResult Clean(EnvelopeDto envelope)
        {
            var phaseDtos = envelope.Phases ?? [];
            if (phaseDtos.Count == 0)
            {
                return MasteryPlanParseResult.Failure("Het plan bevat geen fases.");
            }

            if (phaseDtos.Count > MaxPhases)
            {
                return MasteryPlanParseResult.Failure($"Het plan heeft {phaseDtos.Count.ToString(CultureInfo.InvariantCulture)} fases; er mogen er hooguit {MaxPhases} zijn.");
            }

            var stepCount = phaseDtos.Sum(p => p?.Steps?.Count ?? 0);
            if (stepCount > MaxSteps)
            {
                return MasteryPlanParseResult.Failure($"Het plan heeft {stepCount.ToString(CultureInfo.InvariantCulture)} stappen; er mogen er hooguit {MaxSteps} zijn.");
            }

            var phaseIds = new HashSet<string>(StringComparer.Ordinal);
            var stepIds = new HashSet<string>(StringComparer.Ordinal);
            var phases = new List<MasteryPhase>();
            for (var p = 0; p < phaseDtos.Count; p++)
            {
                var phaseLabel = $"Fase #{(p + 1).ToString(CultureInfo.InvariantCulture)}";
                var dto = phaseDtos[p];
                if (dto is null)
                {
                    return MasteryPlanParseResult.Failure($"{phaseLabel} is leeg.");
                }

                if (CheckId(dto.Id, phaseLabel) is { } idError)
                {
                    return MasteryPlanParseResult.Failure(idError);
                }

                var phaseId = dto.Id!.Trim();
                if (!phaseIds.Add(phaseId))
                {
                    return MasteryPlanParseResult.Failure($"De fase-id \"{phaseId}\" komt meer dan één keer voor.");
                }

                phaseLabel = $"Fase \"{phaseId}\"";
                var phaseLower = $"fase \"{phaseId}\"";
                var steps = new List<MasteryStep>();
                var stepDtos = dto.Steps ?? [];
                if (stepDtos.Count == 0)
                {
                    _warnings.Add($"{phaseLabel} heeft geen stappen.");
                }

                for (var s = 0; s < stepDtos.Count; s++)
                {
                    var stepLabel = $"Stap #{(s + 1).ToString(CultureInfo.InvariantCulture)} van {phaseLower}";
                    var stepDto = stepDtos[s];
                    if (stepDto is null)
                    {
                        _warnings.Add($"{stepLabel} is leeg en overgeslagen.");
                        continue;
                    }

                    if (CheckId(stepDto.Id, stepLabel) is { } stepIdError)
                    {
                        return MasteryPlanParseResult.Failure(stepIdError);
                    }

                    var stepId = stepDto.Id!.Trim();
                    if (!stepIds.Add(stepId))
                    {
                        return MasteryPlanParseResult.Failure($"De stap-id \"{stepId}\" komt meer dan één keer voor.");
                    }

                    steps.Add(CleanStep(stepDto, stepId));
                }

                phases.Add(new MasteryPhase(
                    phaseId,
                    Text(dto.Title, MaxShortText, $"de titel van {phaseLower}") is { Length: > 0 } title ? title : phaseId,
                    Text(dto.Goal, MaxLongText, $"het doel van {phaseLower}"),
                    steps));
            }

            var allSteps = phases.SelectMany(ph => ph.Steps).ToList();
            foreach (var step in allSteps)
            {
                var unknown = step.Requires.FirstOrDefault(r => !stepIds.Contains(r));
                if (unknown is not null)
                {
                    return MasteryPlanParseResult.Failure($"Stap \"{step.Id}\" wacht op \"{unknown}\", maar die stap bestaat niet in het plan.");
                }
            }

            if (FindCycle(allSteps) is { } cycle)
            {
                return MasteryPlanParseResult.Failure($"De stappen wachten in een cirkel op elkaar: {string.Join(" → ", cycle)}.");
            }

            var plan = new MasteryPlan(
                Text(envelope.Title, MaxShortText, "de titel van het plan") is { Length: > 0 } planTitle ? planTitle : "Masteryplan",
                ParseDate(envelope.CreatedUtc),
                Text(envelope.Author, MaxShortText, "de auteur"),
                Text(envelope.Summary, MaxLongText, "de samenvatting"),
                CleanAssumptions(envelope.Assumptions),
                phases);

            return new MasteryPlanParseResult(plan, _warnings, null);
        }

        private static string? CheckId(string? id, string label)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return $"{label} heeft geen id.";
            }

            return id.Trim().Length > MaxIdLength
                ? $"{label} heeft een id die langer is dan {MaxIdLength} tekens."
                : null;
        }

        private MasteryStep CleanStep(StepDto dto, string id)
        {
            var label = $"Stap \"{id}\"";
            var lower = $"stap \"{id}\"";

            var type = MasteryStepType.Other;
            if (!string.IsNullOrWhiteSpace(dto.Type))
            {
                if (TypeNames.TryGetValue(dto.Type.Trim(), out var known))
                {
                    type = known;
                }
                else
                {
                    _warnings.Add($"{label} heeft een onbekend type \"{Shorten(dto.Type)}\"; het wordt \"other\".");
                }
            }

            var priority = MasteryPriority.Normal;
            if (!string.IsNullOrWhiteSpace(dto.Priority))
            {
                if (PriorityNames.TryGetValue(dto.Priority.Trim(), out var knownPriority))
                {
                    priority = knownPriority;
                }
                else
                {
                    _warnings.Add($"{label} heeft een onbekende prioriteit \"{Shorten(dto.Priority)}\"; het wordt \"normal\".");
                }
            }

            var (masteryId, masteryLevel) = CleanMastery(dto.MasteryId, dto.MasteryLevel, label);

            var achievementId = dto.AchievementId;
            if (achievementId is <= 0)
            {
                _warnings.Add($"{label} heeft een ongeldige achievementId; die wordt genegeerd.");
                achievementId = null;
            }

            if (type == MasteryStepType.MasteryLevel && (masteryId is null || masteryLevel is null))
            {
                _warnings.Add($"{label} is een mastery-level-stap zonder geldige masteryId en masteryLevel; je vinkt hem zelf af.");
            }

            if (type == MasteryStepType.Achievement && achievementId is null)
            {
                _warnings.Add($"{label} is een achievement-stap zonder achievementId; je vinkt hem zelf af.");
            }

            var waypoint = NullIfEmpty(dto.Waypoint);
            if (waypoint is not null && !Waypoint().IsMatch(waypoint))
            {
                _warnings.Add($"{label} heeft een ongeldige waypoint-code; die wordt weggelaten.");
                waypoint = null;
            }

            var minutes = dto.EstimatedMinutes;
            if (minutes is < 0)
            {
                _warnings.Add($"{label} heeft een negatieve tijdsinschatting; die wordt weggelaten.");
                minutes = null;
            }
            else if (minutes is > MaxEstimatedMinutes)
            {
                _warnings.Add($"{label} heeft een onrealistisch hoge tijdsinschatting (meer dan 1000 uur); die wordt weggelaten.");
                minutes = null;
            }

            var requires = new List<string>();
            foreach (var r in dto.Requires ?? [])
            {
                if (string.IsNullOrWhiteSpace(r))
                {
                    _warnings.Add($"{label} heeft een lege verwijzing in \"requires\"; die wordt genegeerd.");
                    continue;
                }

                var trimmed = r.Trim();
                if (!requires.Contains(trimmed, StringComparer.Ordinal))
                {
                    requires.Add(trimmed);
                }
            }

            var title = Text(dto.Title, MaxShortText, $"de titel van {lower}");
            if (title.Length == 0)
            {
                _warnings.Add($"{label} heeft geen titel; de id wordt gebruikt.");
                title = id;
            }

            return new MasteryStep(
                id,
                title,
                Text(dto.Description, MaxLongText, $"de beschrijving van {lower}"),
                type,
                NullIfEmpty(Text(dto.Region, MaxShortText, $"de regio van {lower}")),
                masteryId,
                masteryLevel,
                achievementId,
                NullIfEmpty(Text(dto.Map, MaxShortText, $"de map van {lower}")),
                waypoint,
                minutes,
                priority,
                requires,
                CleanLinks(dto.Links, label, lower),
                NullIfEmpty(Text(dto.Notes, MaxLongText, $"de notities van {lower}")));
        }

        private (int? MasteryId, int? MasteryLevel) CleanMastery(int? masteryId, int? masteryLevel, string label)
        {
            if (masteryId is null)
            {
                if (masteryLevel is not null)
                {
                    _warnings.Add($"{label} heeft een masteryLevel zonder masteryId; dat wordt genegeerd.");
                }

                return (null, null);
            }

            if (masteryId <= 0)
            {
                _warnings.Add($"{label} heeft een ongeldige masteryId; je vinkt hem zelf af.");
                return (null, null);
            }

            if (masteryLevel is < 0)
            {
                _warnings.Add($"{label} heeft een negatief masteryLevel; je vinkt hem zelf af.");
                return (null, null);
            }

            if (_definitions is null)
            {
                if (!_reportedUnchecked)
                {
                    _reportedUnchecked = true;
                    _warnings.Add("De mastery-gegevens van Guild Wars 2 zijn nog niet opgehaald, dus de masteryId's en -levels in het plan konden niet worden gecontroleerd.");
                }

                return (masteryId, masteryLevel);
            }

            if (!_definitions.TryGetValue(masteryId.Value, out var definition))
            {
                _warnings.Add($"{label} verwijst naar masteryId {masteryId.Value.ToString(CultureInfo.InvariantCulture)}, die niet bestaat; je vinkt hem zelf af.");
                return (null, null);
            }

            if (masteryLevel is { } level && level >= definition.Levels.Count)
            {
                _warnings.Add($"{label} vraagt level {level.ToString(CultureInfo.InvariantCulture)} van \"{definition.Name}\", maar die track heeft {definition.Levels.Count.ToString(CultureInfo.InvariantCulture)} levels (0 t/m {(definition.Levels.Count - 1).ToString(CultureInfo.InvariantCulture)}); je vinkt hem zelf af.");
                return (null, null);
            }

            return (masteryId, masteryLevel);
        }

        private List<MasteryLink> CleanLinks(List<LinkDto?>? dtos, string label, string lower)
        {
            var links = new List<MasteryLink>();
            foreach (var dto in dtos ?? [])
            {
                var url = dto?.Url?.Trim();
                if (url is null || url.Length > MaxUrlLength || !IsWikiUrl(url))
                {
                    _warnings.Add($"{label} heeft een link die niet naar wiki.guildwars2.com (https) gaat; die wordt weggelaten.");
                    continue;
                }

                if (links.Count >= MaxLinksPerStep)
                {
                    _warnings.Add($"{label} heeft meer dan {MaxLinksPerStep} links; de rest wordt weggelaten.");
                    break;
                }

                var linkLabel = Text(dto!.Label, MaxShortText, $"een linktekst van {lower}");
                links.Add(new MasteryLink(linkLabel.Length > 0 ? linkLabel : "Wiki", url));
            }

            return links;
        }

        private List<string> CleanAssumptions(List<string?>? dtos)
        {
            var assumptions = new List<string>();
            foreach (var dto in dtos ?? [])
            {
                if (assumptions.Count >= MaxAssumptions)
                {
                    _warnings.Add($"Het plan heeft meer dan {MaxAssumptions} aannames; de rest wordt weggelaten.");
                    break;
                }

                var text = Text(dto, MaxAssumptionLength, "een aanname");
                if (text.Length > 0)
                {
                    assumptions.Add(text);
                }
            }

            return assumptions;
        }

        private DateTimeOffset? ParseDate(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            if (DateTimeOffset.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date))
            {
                return date;
            }

            _warnings.Add("De aanmaakdatum van het plan is ongeldig en wordt genegeerd.");
            return null;
        }

        private string Text(string? value, int max, string what)
        {
            var text = (value ?? string.Empty).Trim();
            if (text.Length <= max)
            {
                return text;
            }

            _warnings.Add($"De tekst van {what} is ingekort tot {max.ToString(CultureInfo.InvariantCulture)} tekens.");
            return text[..max];
        }

        private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static string Shorten(string value)
        {
            var trimmed = value.Trim();
            return trimmed.Length <= 40 ? trimmed : trimmed[..40] + "…";
        }
    }

    /// <summary>Returns the ids that form a cycle (first id repeated at the end), or <c>null</c> when there is none.</summary>
    private static List<string>? FindCycle(IReadOnlyList<MasteryStep> steps)
    {
        var byId = steps.ToDictionary(s => s.Id, StringComparer.Ordinal);
        var state = new Dictionary<string, int>(StringComparer.Ordinal); // 0/absent = new, 1 = on the path, 2 = done
        var path = new List<string>();

        List<string>? Visit(string id)
        {
            state[id] = 1;
            path.Add(id);
            foreach (var next in byId[id].Requires)
            {
                var seen = state.GetValueOrDefault(next);
                if (seen == 1)
                {
                    return [.. path.Skip(path.IndexOf(next)), next];
                }

                if (seen == 0 && Visit(next) is { } found)
                {
                    return found;
                }
            }

            path.RemoveAt(path.Count - 1);
            state[id] = 2;
            return null;
        }

        foreach (var step in steps)
        {
            if (state.GetValueOrDefault(step.Id) == 0 && Visit(step.Id) is { } cycle)
            {
                return cycle;
            }
        }

        return null;
    }

    private static bool IsWikiUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && string.Equals(uri.Host, WikiHost, StringComparison.OrdinalIgnoreCase)
        && uri.IsDefaultPort
        && string.IsNullOrEmpty(uri.UserInfo);

    private static EnvelopeDto ToDto(MasteryPlan plan) => new(
        FormatName,
        CurrentVersion,
        plan.Title,
        plan.CreatedUtc?.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
        plan.Author,
        plan.Summary,
        [.. plan.Assumptions],
        [.. plan.Phases.Select(p => new PhaseDto(
            p.Id,
            p.Title,
            p.Goal,
            [.. p.Steps.Select(s => new StepDto(
                s.Id,
                s.Title,
                s.Description,
                TypeName(s.Type),
                s.Region,
                s.MasteryId,
                s.MasteryLevel,
                s.AchievementId,
                s.Map,
                s.Waypoint,
                s.EstimatedMinutes,
                PriorityName(s.Priority),
                [.. s.Requires],
                [.. s.Links.Select(l => new LinkDto(l.Label, l.Url))],
                s.Notes))]))]);

    [GeneratedRegex(@"```[ \t]*(?:json|JSON|Json)?[ \t]*\r?\n(?<body>.*?)```", RegexOptions.Singleline | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CodeBlock();

    // A chat code is "[&" + base64 + "]"; waypoint codes are short, so anything long is not one.
    [GeneratedRegex(@"^\[&[A-Za-z0-9+/]{4,40}={0,2}\]$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Waypoint();

    // The record shapes below are the file format: names and order are the contract.
    private sealed record EnvelopeDto(
        string? Format,
        int Version,
        string? Title,
        string? CreatedUtc,
        string? Author,
        string? Summary,
        List<string?>? Assumptions,
        List<PhaseDto?>? Phases);

    private sealed record PhaseDto(string? Id, string? Title, string? Goal, List<StepDto?>? Steps);

    private sealed record StepDto(
        string? Id,
        string? Title,
        string? Description,
        string? Type,
        string? Region,
        int? MasteryId,
        int? MasteryLevel,
        int? AchievementId,
        string? Map,
        string? Waypoint,
        int? EstimatedMinutes,
        string? Priority,
        List<string?>? Requires,
        List<LinkDto?>? Links,
        string? Notes);

    private sealed record LinkDto(string? Label, string? Url);
}
