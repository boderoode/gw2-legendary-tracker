namespace Gw2LegendaryTracker.Core.Goals;

/// <summary>How much of a requirement is covered, as a share between 0 and 1, for the mini bars in the overlay.</summary>
public static class Coverage
{
    public static double Ratio(long covered, long needed) =>
        needed <= 0 ? 1 : Math.Clamp((double)covered / needed, 0, 1);
}
