namespace Gw2LegendaryTracker.Core.Cosmetics;

/// <summary>The slots of a look that the studio can fill. Aquatic gear and the second weapon set are left out on purpose.</summary>
public enum LookSlot
{
    Helm,
    Shoulders,
    Coat,
    Gloves,
    Leggings,
    Boots,
    Back,
    MainHand,
    OffHand,
}

/// <summary>The skin and up to four dye channels of one slot. A null skin means "nothing chosen".</summary>
public readonly record struct SlotLook(int? SkinId, int? Dye1 = null, int? Dye2 = null, int? Dye3 = null, int? Dye4 = null)
{
    public const int DyeChannels = 4;

    public int? GetDye(int channel) => channel switch
    {
        0 => Dye1,
        1 => Dye2,
        2 => Dye3,
        3 => Dye4,
        _ => throw new ArgumentOutOfRangeException(nameof(channel)),
    };

    public SlotLook WithDye(int channel, int? colorId) => channel switch
    {
        0 => this with { Dye1 = colorId },
        1 => this with { Dye2 = colorId },
        2 => this with { Dye3 = colorId },
        3 => this with { Dye4 = colorId },
        _ => throw new ArgumentOutOfRangeException(nameof(channel)),
    };
}

/// <summary>An immutable configuration of a character: race, gender, profession and what each slot wears.</summary>
public sealed class CharacterLook : IEquatable<CharacterLook>
{
    private readonly Dictionary<LookSlot, SlotLook> _slots;

    public CharacterLook(string race, string gender, string profession, IEnumerable<KeyValuePair<LookSlot, SlotLook>>? slots = null)
    {
        Race = race;
        Gender = gender;
        Profession = profession;
        _slots = slots is null ? [] : new Dictionary<LookSlot, SlotLook>(slots);
    }

    public string Race { get; }

    public string Gender { get; }

    public string Profession { get; }

    public IReadOnlyDictionary<LookSlot, SlotLook> Slots => _slots;

    public static CharacterLook Default { get; } = new(CharacterOptions.Races[0], CharacterOptions.Genders[0], CharacterOptions.Professions[0]);

    public SlotLook Get(LookSlot slot) => _slots.GetValueOrDefault(slot);

    public CharacterLook WithCharacter(string race, string gender, string profession) => new(race, gender, profession, _slots);

    public CharacterLook WithSlot(LookSlot slot, SlotLook look)
    {
        var next = new Dictionary<LookSlot, SlotLook>(_slots);
        if (look == default)
        {
            next.Remove(slot);
        }
        else
        {
            next[slot] = look;
        }

        return new CharacterLook(Race, Gender, Profession, next);
    }

    /// <summary>Puts a skin in a slot. The dyes stay, because most players want the same colours on another skin.</summary>
    public CharacterLook WithSkin(LookSlot slot, int? skinId) => WithSlot(slot, Get(slot) with { SkinId = skinId });

    public CharacterLook WithDye(LookSlot slot, int channel, int? colorId) => WithSlot(slot, Get(slot).WithDye(channel, colorId));

    public CharacterLook ClearSlot(LookSlot slot) => WithSlot(slot, default);

    public bool Equals(CharacterLook? other) =>
        other is not null
        && Race == other.Race
        && Gender == other.Gender
        && Profession == other.Profession
        && _slots.Count == other._slots.Count
        && _slots.All(pair => other._slots.TryGetValue(pair.Key, out var value) && value == pair.Value);

    public override bool Equals(object? obj) => Equals(obj as CharacterLook);

    public override int GetHashCode() => HashCode.Combine(Race, Gender, Profession, _slots.Count);
}

/// <summary>The choices offered in the studio, and what follows from them.</summary>
public static class CharacterOptions
{
    public static IReadOnlyList<string> Races { get; } = ["Human", "Norn", "Charr", "Asura", "Sylvari"];

    public static IReadOnlyList<string> Genders { get; } = ["Female", "Male"];

    public static IReadOnlyList<string> Professions { get; } =
        ["Guardian", "Warrior", "Revenant", "Engineer", "Ranger", "Thief", "Elementalist", "Mesmer", "Necromancer"];

    /// <summary>The armor weight class a profession can wear. Unknown professions get null, so nothing is filtered out by mistake.</summary>
    public static string? WeightClassOf(string profession) => profession switch
    {
        "Guardian" or "Warrior" or "Revenant" => "Heavy",
        "Engineer" or "Ranger" or "Thief" => "Medium",
        "Elementalist" or "Mesmer" or "Necromancer" => "Light",
        _ => null,
    };

    /// <summary>The slot an armor skin goes in, or null for slots the studio does not cover (such as the aquatic helm).</summary>
    public static LookSlot? SlotOfArmor(string armorSlot) => armorSlot switch
    {
        "Helm" => LookSlot.Helm,
        "Shoulders" => LookSlot.Shoulders,
        "Coat" => LookSlot.Coat,
        "Gloves" => LookSlot.Gloves,
        "Leggings" => LookSlot.Leggings,
        "Boots" => LookSlot.Boots,
        _ => null,
    };

    /// <summary>Maps a slot name of /v2/characters/:id/equipment to a studio slot; null for slots that are not covered.</summary>
    public static LookSlot? SlotOfEquipment(string equipmentSlot) => equipmentSlot switch
    {
        "Backpack" => LookSlot.Back,
        "WeaponA1" => LookSlot.MainHand,
        "WeaponA2" => LookSlot.OffHand,
        _ => SlotOfArmor(equipmentSlot),
    };
}
