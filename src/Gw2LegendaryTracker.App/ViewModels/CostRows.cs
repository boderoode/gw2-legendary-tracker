namespace Gw2LegendaryTracker.App.ViewModels;

/// <summary>One purchasable shortage in the cost overview of the main window.</summary>
public sealed record CostRowVm(string Name, string? IconUrl, long Quantity, string UnitText, string TotalText);
