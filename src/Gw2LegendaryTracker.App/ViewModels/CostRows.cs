using Gw2LegendaryTracker.Core.Links;

namespace Gw2LegendaryTracker.App.ViewModels;

/// <summary>One purchasable shortage in the cost overview of the main window.</summary>
public sealed record CostRowVm(string Name, string? IconUrl, long Quantity, string UnitText, string TotalText);

/// <summary>Something that is missing and cannot be bought on the Trading Post; the user can look it up and record where to get it.</summary>
public sealed record UnbuyableRowVm(int ItemId, string Name, string? IconUrl, long Quantity)
{
    public string WikiUrl => ItemLinks.Wiki(Name);

    public string TreasuresUrl => ItemLinks.Treasures(ItemId);
}
