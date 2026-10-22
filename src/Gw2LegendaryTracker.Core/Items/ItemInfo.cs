namespace Gw2LegendaryTracker.Core.Items;

/// <param name="Description">Official description text. Only filled by direct API reads; the local cache does not keep it.</param>
public sealed record ItemInfo(int Id, string Name, string? IconUrl, string Rarity, string? Description = null);
