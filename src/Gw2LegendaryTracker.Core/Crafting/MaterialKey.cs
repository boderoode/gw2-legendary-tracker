namespace Gw2LegendaryTracker.Core.Crafting;

public enum MaterialKind
{
    Item,

    /// <summary>A wallet currency (karma, spirit shards, ...).</summary>
    Currency,
}

/// <summary>Identifies something that can be owned and consumed: an item or a wallet currency.</summary>
public readonly record struct MaterialKey(MaterialKind Kind, int Id)
{
    public static MaterialKey Item(int id) => new(MaterialKind.Item, id);

    public static MaterialKey Currency(int id) => new(MaterialKind.Currency, id);
}
