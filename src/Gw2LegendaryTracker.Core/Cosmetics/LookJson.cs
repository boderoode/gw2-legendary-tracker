using System.Text.Json;

namespace Gw2LegendaryTracker.Core.Cosmetics;

/// <summary>The stored form of a <see cref="CharacterLook"/>. Unknown slots are ignored so newer data never breaks an older app.</summary>
public static class LookJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(CharacterLook look)
    {
        ArgumentNullException.ThrowIfNull(look);

        var dto = new LookDto(
            look.Race,
            look.Gender,
            look.Profession,
            [.. look.Slots
                .OrderBy(pair => pair.Key)
                .Select(pair => new SlotDto(
                    pair.Key.ToString(),
                    pair.Value.SkinId,
                    [pair.Value.Dye1, pair.Value.Dye2, pair.Value.Dye3, pair.Value.Dye4]))]);
        return JsonSerializer.Serialize(dto, Options);
    }

    /// <summary>Reads a stored look; returns null when the text is not a look.</summary>
    public static CharacterLook? Deserialize(string json)
    {
        LookDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<LookDto>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }

        if (dto is null || string.IsNullOrWhiteSpace(dto.Race) || string.IsNullOrWhiteSpace(dto.Gender) || string.IsNullOrWhiteSpace(dto.Profession))
        {
            return null;
        }

        var slots = new Dictionary<LookSlot, SlotLook>();
        foreach (var slot in dto.Slots ?? [])
        {
            if (Enum.TryParse<LookSlot>(slot.Slot, out var key) && Enum.IsDefined(key))
            {
                var dyes = slot.Dyes ?? [];
                slots[key] = new SlotLook(slot.Skin, dyes.ElementAtOrDefault(0), dyes.ElementAtOrDefault(1), dyes.ElementAtOrDefault(2), dyes.ElementAtOrDefault(3));
            }
        }

        return new CharacterLook(dto.Race, dto.Gender, dto.Profession, slots);
    }

    private sealed record SlotDto(string? Slot, int? Skin, List<int?>? Dyes);

    private sealed record LookDto(string? Race, string? Gender, string? Profession, List<SlotDto>? Slots);
}
