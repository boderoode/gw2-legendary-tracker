namespace Gw2LegendaryTracker.Core.Cosmetics;

/// <summary>Read-only access to the cosmetic data of the official API. Implementations throw <c>Gw2ApiException</c> on failure.</summary>
public interface ICosmeticsApiClient
{
    /// <summary>All wardrobe skins (public, no key needed). <paramref name="progress"/> reports how many were loaded.</summary>
    Task<IReadOnlyList<CosmeticSkin>> GetAllSkinsAsync(IProgress<int>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>All dye colours (public, no key needed).</summary>
    Task<IReadOnlyList<DyeColor>> GetAllDyeColorsAsync(CancellationToken cancellationToken = default);

    /// <summary>Ids of the skins the account has unlocked. Needs the "unlocks" permission.</summary>
    Task<IReadOnlySet<int>> GetUnlockedSkinIdsAsync(string apiKey, CancellationToken cancellationToken = default);

    /// <summary>Ids of the dyes the account has unlocked. Needs the "unlocks" permission.</summary>
    Task<IReadOnlySet<int>> GetUnlockedDyeIdsAsync(string apiKey, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetCharacterNamesAsync(string apiKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// The look of a character: race, gender, profession and the skins and dyes it wears. Slots that wear the
    /// default skin of their item have no skin in the API, so they are left empty.
    /// </summary>
    Task<CharacterLook> GetCharacterLookAsync(string apiKey, string characterName, CancellationToken cancellationToken = default);
}
