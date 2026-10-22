using Gw2LegendaryTracker.Core.Gw2;

namespace Gw2LegendaryTracker.Core.Inventory;

/// <summary>Holds the current <see cref="InventoryModel"/> and announces when it is replaced.</summary>
public sealed class InventoryProvider
{
    private InventoryModel _current = InventoryModel.Empty;

    public InventoryModel Current => Volatile.Read(ref _current);

    /// <summary>Raised after <see cref="Current"/> was replaced. May fire on any thread.</summary>
    public event EventHandler<InventoryModel>? Changed;

    public void Update(AccountSnapshot snapshot)
    {
        var model = InventoryModel.From(snapshot);
        Volatile.Write(ref _current, model);
        Changed?.Invoke(this, model);
    }
}
