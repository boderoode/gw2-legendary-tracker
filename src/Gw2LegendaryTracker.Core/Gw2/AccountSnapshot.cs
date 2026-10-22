namespace Gw2LegendaryTracker.Core.Gw2;

public enum InventorySource
{
    Bank,
    Materials,
    SharedInventory,
    Character,
}

/// <summary>One occupied slot. <paramref name="CharacterName"/> is empty unless <paramref name="Source"/> is <see cref="InventorySource.Character"/>.</summary>
public sealed record InventoryEntry(int ItemId, int Count, InventorySource Source, string CharacterName = "");

public sealed record WalletEntry(int CurrencyId, long Value);

/// <summary>Everything read from the account in one synchronisation.</summary>
public sealed record AccountSnapshot(
    string AccountName,
    DateTimeOffset CapturedUtc,
    IReadOnlyList<InventoryEntry> Items,
    IReadOnlyList<WalletEntry> Wallet);
