using System.Globalization;
using System.Text.Json;
using Gw2LegendaryTracker.Core.Goals;
using Gw2LegendaryTracker.Core.Settings;

namespace Gw2LegendaryTracker.Core.Export;

/// <param name="Settings">The settings from the file, or <c>null</c> when the file has none (current settings are then kept).</param>
/// <param name="Warnings">Things that were left out or corrected; the import still succeeded.</param>
/// <param name="Error">Why the file cannot be imported at all; <c>null</c> on success.</param>
public sealed record ImportResult(AppSettings? Settings, IReadOnlyList<Goal> Goals, IReadOnlyList<string> Warnings, string? Error)
{
    public bool Succeeded => Error is null;

    public static ImportResult Failure(string error) => new(null, [], [], error);
}

/// <summary>
/// Reads and writes the user's own data: goals (order, notes, ticks) and settings.
/// It never contains API keys, the inventory (it is fetched again) or downloaded game data.
/// A file that is invalid is refused as a whole; single bad entries inside a valid file are skipped and reported.
/// </summary>
public static class UserDataSerializer
{
    public const string FormatName = "gw2-legendary-tracker-export";

    public const int CurrentVersion = 1;

    /// <summary>A real export is a few kilobytes; anything much larger is not one.</summary>
    public const int MaxBytes = 1_000_000;

    public const int MaxGoals = 200;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static string Serialize(AppSettings settings, IReadOnlyList<Goal> goals, DateTimeOffset exportedUtc)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(goals);

        var envelope = new Envelope(
            FormatName,
            CurrentVersion,
            exportedUtc.ToString("O", CultureInfo.InvariantCulture),
            settings,
            [.. goals.Select(g => new GoalDto(g.ItemId, g.Quantity, g.Note, [.. g.CompletedItemIds.Order()]))]);
        return JsonSerializer.Serialize(envelope, JsonOptions) + "\n";
    }

    public static ImportResult Parse(string? json)
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
            null);
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

    private static bool IsFinite(double value) => double.IsFinite(value);

    private sealed record Envelope(string? Format, int Version, string? ExportedUtc, AppSettings? Settings, List<GoalDto?>? Goals);

    private sealed record GoalDto(int ItemId, int Quantity, string? Note, List<int>? CompletedItemIds);
}
