namespace Gw2LegendaryTracker.Core.Masteries;

/// <summary>A mastery track the account has trained in.</summary>
/// <param name="Level">Zero-based index of the highest trained level (the meaning of <c>/v2/account/masteries</c>).</param>
public sealed record AccountMastery(int Id, int Level);

/// <summary>Mastery points of one region from <c>/v2/account/mastery/points</c>.</summary>
public sealed record RegionPoints(string Region, int Spent, int Earned);

public sealed record AchievementProgress(int Id, bool Done, int Current, int Max);

/// <summary>The mastery-related account data from the last successful mastery sync.</summary>
public sealed record MasteryAccountProgress(
    DateTimeOffset CapturedUtc,
    IReadOnlyList<AccountMastery> Masteries,
    IReadOnlyList<RegionPoints> Points,
    IReadOnlyList<int> Unlocked,
    IReadOnlyList<AchievementProgress> Achievements)
{
    public static MasteryAccountProgress Empty(DateTimeOffset capturedUtc) => new(capturedUtc, [], [], [], []);

    public bool Equals(MasteryAccountProgress? other) =>
        other is not null && CapturedUtc == other.CapturedUtc && Masteries.SequenceEqual(other.Masteries)
        && Points.SequenceEqual(other.Points) && Unlocked.SequenceEqual(other.Unlocked) && Achievements.SequenceEqual(other.Achievements);

    public override int GetHashCode() => HashCode.Combine(CapturedUtc, Masteries.Count, Points.Count, Achievements.Count);
}
