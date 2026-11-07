using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Gw2LegendaryTracker.Core.Acquisition;
using Gw2LegendaryTracker.Core.Crafting;
using Gw2LegendaryTracker.Core.Goals;
using Gw2LegendaryTracker.Core.Masteries;
using Gw2LegendaryTracker.Core.Settings;

namespace Gw2LegendaryTracker.Core.Export;

/// <param name="Settings">The settings from the file, or <c>null</c> when the file has none (current settings are then kept).</param>
/// <param name="Warnings">Things that were left out or corrected; the import still succeeded.</param>
/// <param name="Error">Why the file cannot be imported at all; <c>null</c> on success.</param>
/// <param name="Acquisitions">The vendor offers in the file, or <c>null</c> when the file has none (an older export): current ones are then kept.</param>
/// <param name="MasteryPlan">The mastery plan in the file, or <c>null</c> when it has none (or an unusable one): the current plan is then kept.</param>
public sealed record ImportResult(
    AppSettings? Settings,
    IReadOnlyList<Goal> Goals,
    IReadOnlyList<string> Warnings,
    string? Error,
    IReadOnlyList<NewOffer>? Acquisitions = null,
    MasteryPlanBackup? MasteryPlan = null)
{
    public bool Succeeded => Error is null;

    public static ImportResult Failure(string error) => new(null, [], [], error);
}

/// <summary>The active mastery plan with its manual choices, as it travels in a user data export.</summary>
public sealed record MasteryPlanBackup(MasteryPlan Plan, IReadOnlyDictionary<string, bool> ManualChoices);

/// <summary>
/// Reads and writes the user's own data: goals (order, notes, ticks) and settings.
/// It never contains API keys, the inventory (it is fetched again) or downloaded game data.
/// A file that is invalid is refused as a whole; single bad entries inside a valid file are skipped and reported.
/// </summary>
public static class UserDataSerializer
{
    public const string FormatName = "gw2-legendary-tracker-export";

    /// <summary>Version 2 added the mastery plan; version 1 files are still read.</summary>
    public const int CurrentVersion = 2;

    /// <summary>A real export is a few kilobytes plus a mastery plan of at most 1 MB; anything much larger is not one.</summary>
    public const int MaxBytes = 2_500_000;

    public const int MaxGoals = 200;

    // Accents and other non-ASCII text are written as is: the file is never embedded in HTML, and escaping (é as é) would make
    // a mastery plan with much Dutch text up to three times larger.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Serialize(
        AppSettings settings,
        IReadOnlyList<Goal> goals,
        DateTimeOffset exportedUtc,
        IReadOnlyList<AcquisitionOffer>? acquisitions = null,
        MasteryPlanBackup? masteryPlan = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(goals);

        MasteryPlanDto? planDto = null;
        if (masteryPlan is not null)
        {
            using var document = JsonDocument.Parse(MasteryPlanSerializer.Serialize(masteryPlan.Plan));
            planDto = new MasteryPlanDto(
                document.RootElement.Clone(),
                [.. masteryPlan.ManualChoices.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => new ManualChoiceDto(c.Key, c.Value))]);
        }

        var envelope = new Envelope(
            FormatName,
            CurrentVersion,
            exportedUtc.ToString("O", CultureInfo.InvariantCulture),
            settings,
            [.. goals.Select(g => new GoalDto(g.ItemId, g.Quantity, g.Note, [.. g.CompletedItemIds.Order()]))],
            [.. (acquisitions ?? []).Select(o => new OfferDto(
                o.ItemId,
                o.OutputCount,
                [.. o.Costs.Select(c => new CostDto(c.Key.Kind == MaterialKind.Currency ? CurrencyKind : ItemKind, c.Key.Id, c.Count))],
                o.VendorName,
                o.Location))],
            planDto);
        return JsonSerializer.Serialize(envelope, JsonOptions) + "\n";
    }

    /// <param name="masteryDefinitions">Known mastery tracks, to check the ids in a mastery plan; see <see cref="MasteryPlanSerializer.Parse"/>.</param>
    public static ImportResult Parse(string? json, IReadOnlyDictionary<int, MasteryDefinition>? masteryDefinitions = null)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return ImportResult.Failure("Het bestand is leeg.");
        }

        if (json.Length > MaxBytes)
        {
            return ImportResult.Failure("Het bestand is te groot om een export van deze app te zijn.");
        }

        Envelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<Envelope>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return ImportResult.Failure("Het bestand is geen geldige export: de inhoud kan niet worden gelezen.");
        }

        if (envelope is null || !string.Equals(envelope.Format, FormatName, StringComparison.Ordinal))
        {
            return ImportResult.Failure("Dit is geen export van GW2 Legendary Tracker.");
        }

        if (envelope.Version > CurrentVersion)
        {
            return ImportResult.Failure($"Dit bestand is gemaakt met een nieuwere versie van de app (formaat {envelope.Version}). Werk de app bij om het te importeren.");
        }

        if (envelope.Version < 1)
        {
            return ImportResult.Failure("Het bestand heeft geen geldige versie.");
        }

        var warnings = new List<string>();
        return new ImportResult(
            CleanSettings(envelope.Settings, warnings),
            CleanGoals(envelope.Goals, warnings),
            warnings,
            null,
            CleanOffers(envelope.Acquisitions, warnings),
            CleanMasteryPlan(envelope.MasteryPlan, masteryDefinitions, warnings));
    }

    /// <summary>
    /// A missing or empty mastery plan keeps the current one: an export from a pc without a plan should not wipe out
    /// a plan on this one. A plan that cannot be read is skipped with a warning; the rest of the file is still imported.
    /// </summary>
    private static MasteryPlanBackup? CleanMasteryPlan(MasteryPlanDto? dto, IReadOnlyDictionary<int, MasteryDefinition>? definitions, List<string> warnings)
    {
        if (dto?.Plan is not { ValueKind: JsonValueKind.Object } element)
        {
            return null;
        }

        // The file as a whole is already within MaxBytes; inside it the plan is indented and escaped, so it may be longer than 1 MB.
        var parsed = MasteryPlanSerializer.Parse(element.GetRawText(), definitions, maxBytes: MaxBytes);
        if (!parsed.Succeeded)
        {
            warnings.Add($"Het masteryplan in het bestand is overgeslagen: {parsed.Error}");
            return null;
        }

        warnings.AddRange(parsed.Warnings.Select(w => $"Masteryplan: {w}"));
        var stepIds = parsed.Plan!.AllSteps.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        var choices = new Dictionary<string, bool>(StringComparer.Ordinal);
        var skipped = 0;
        foreach (var choice in dto.ManualChoices ?? [])
        {
            if (choice?.StepId is { } id && stepIds.Contains(id))
            {
                choices[id] = choice.Done;
            }
            else
            {
                skipped++;
            }
        }

        if (skipped > 0)
        {
            warnings.Add($"{skipped.ToString(CultureInfo.InvariantCulture)} afvinking(en) bij het masteryplan horen bij geen enkele stap en zijn overgeslagen.");
        }

        return new MasteryPlanBackup(parsed.Plan, choices);
    }

    private static AppSettings? CleanSettings(AppSettings? settings, List<string> warnings)
    {
        if (settings is null)
        {
            warnings.Add("Het bestand bevat geen instellingen; je huidige instellingen blijven staan.");
            return null;
        }

        var raw = settings.Overlay ?? new OverlaySettings();
        var overlay = raw.Normalize();
        if (overlay != raw)
        {
            // e.g. a transparency outside the allowed range, or a hotkey without modifier key.
            warnings.Add("Sommige overlay-instellingen in het bestand waren ongeldig en zijn aangepast.");
        }
        if (overlay.Bounds is { } b && !(IsFinite(b.Left) && IsFinite(b.Top) && IsFinite(b.Width) && IsFinite(b.Height) && b.Width > 0 && b.Height > 0))
        {
            warnings.Add("De opgeslagen positie van de overlay is ongeldig en wordt genegeerd.");
            overlay = overlay with { Bounds = null };
        }

        var selected = settings.SelectedLegendaryId;
        if (selected is not null && !int.TryParse(selected, NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            warnings.Add("De geselecteerde Legendary in het bestand is ongeldig en wordt genegeerd.");
            selected = null;
        }

        return new AppSettings { Overlay = overlay, SelectedLegendaryId = selected };
    }

    private static List<Goal> CleanGoals(List<GoalDto?>? dtos, List<string> warnings)
    {
        var goals = new List<Goal>();
        if (dtos is null)
        {
            warnings.Add("Het bestand bevat geen lijst met doelen.");
            return goals;
        }

        var seen = new HashSet<int>();
        for (var index = 0; index < dtos.Count; index++)
        {
            var label = $"Doel #{(index + 1).ToString(CultureInfo.InvariantCulture)}";
            var dto = dtos[index];
            if (goals.Count >= MaxGoals)
            {
                warnings.Add($"{label} en de rest zijn overgeslagen: er kunnen er hooguit {MaxGoals} zijn.");
                break;
            }

            if (dto is null || dto.ItemId <= 0)
            {
                warnings.Add($"{label} is overgeslagen: geen geldig item.");
                continue;
            }

            if (!seen.Add(dto.ItemId))
            {
                warnings.Add($"{label} is overgeslagen: dit item staat al eerder in de lijst.");
                continue;
            }

            var note = (dto.Note ?? string.Empty).Trim();
            if (note.Length > GoalService.MaxNoteLength)
            {
                warnings.Add($"De notitie van {label.ToLowerInvariant()} is ingekort tot {GoalService.MaxNoteLength} tekens.");
                note = note[..GoalService.MaxNoteLength];
            }

            goals.Add(new Goal(dto.ItemId, dto.Quantity > 0 ? dto.Quantity : 1, note)
            {
                CompletedItemIds = (dto.CompletedItemIds ?? []).Where(id => id > 0).ToHashSet(),
            });
        }

        return goals;
    }

    private static List<NewOffer>? CleanOffers(List<OfferDto?>? dtos, List<string> warnings)
    {
        if (dtos is null)
        {
            return null;
        }

        var offers = new List<NewOffer>();
        for (var index = 0; index < dtos.Count; index++)
        {
            var label = $"Herkomst #{(index + 1).ToString(CultureInfo.InvariantCulture)}";
            if (offers.Count >= AcquisitionService.MaxOffers)
            {
                warnings.Add($"{label} en de rest zijn overgeslagen: er kunnen er hooguit {AcquisitionService.MaxOffers} zijn.");
                break;
            }

            var dto = dtos[index];
            var costs = (dto?.Costs ?? []).Where(c => c is not null).Select(c => new Ingredient(
                string.Equals(c!.Kind, CurrencyKind, StringComparison.Ordinal) ? MaterialKey.Currency(c.Id) : MaterialKey.Item(c.Id),
                c.Count)).ToList();
            var offer = dto is null ? null : new NewOffer(dto.ItemId, dto.OutputCount, costs, dto.VendorName ?? string.Empty, dto.Location ?? string.Empty);
            var error = offer is null ? "leeg item." : AcquisitionService.Validate(offer);
            if (error is not null)
            {
                warnings.Add($"{label} is overgeslagen: {error}");
                continue;
            }

            offers.Add(offer!);
        }

        return offers;
    }

    private static bool IsFinite(double value) => double.IsFinite(value);

    private const string ItemKind = "item";
    private const string CurrencyKind = "currency";

    private sealed record Envelope(
        string? Format,
        int Version,
        string? ExportedUtc,
        AppSettings? Settings,
        List<GoalDto?>? Goals,
        List<OfferDto?>? Acquisitions = null,
        MasteryPlanDto? MasteryPlan = null);

    /// <param name="Plan">The plan in its own <c>gw2-mastery-plan</c> format, so it can also be handed to an AI as is.</param>
    private sealed record MasteryPlanDto(JsonElement? Plan, List<ManualChoiceDto?>? ManualChoices);

    private sealed record ManualChoiceDto(string? StepId, bool Done);

    private sealed record OfferDto(int ItemId, int OutputCount, List<CostDto?>? Costs, string? VendorName, string? Location);

    private sealed record CostDto(string? Kind, int Id, long Count);

    private sealed record GoalDto(int ItemId, int Quantity, string? Note, List<int>? CompletedItemIds);
}
