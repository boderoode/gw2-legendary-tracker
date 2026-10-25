namespace Gw2LegendaryTracker.Core.Cosmetics;

/// <summary>A look the user saved under a name.</summary>
public sealed record LookPreset(string Id, string Name, CharacterLook Look, DateTimeOffset UpdatedUtc);

/// <summary>Local cache of public cosmetic data, plus the presets, which are user data.</summary>
public interface ICosmeticsStore
{
    Task<IReadOnlyList<CosmeticSkin>> LoadSkinsAsync(CancellationToken cancellationToken = default);

    /// <summary>Replaces all cached skins. The cache never holds a half-written list.</summary>
    Task SaveSkinsAsync(IReadOnlyList<CosmeticSkin> skins, DateTimeOffset fetchedUtc, CancellationToken cancellationToken = default);

    /// <summary>When the skins were last stored, or null when there is no cache.</summary>
    Task<DateTimeOffset?> GetSkinsFetchedUtcAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DyeColor>> LoadColorsAsync(CancellationToken cancellationToken = default);

    Task SaveColorsAsync(IReadOnlyList<DyeColor> colors, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LookPreset>> LoadPresetsAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds or overwrites the preset with the same id.</summary>
    Task SavePresetAsync(LookPreset preset, CancellationToken cancellationToken = default);

    Task DeletePresetAsync(string id, CancellationToken cancellationToken = default);
}
