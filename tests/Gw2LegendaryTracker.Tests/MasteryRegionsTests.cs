using System.Globalization;
using Gw2LegendaryTracker.Core.Masteries;

namespace Gw2LegendaryTracker.Tests;

/// <summary>/v2/masteries names regions by code, /v2/account/mastery/points by expansion; both must meet.</summary>
public sealed class MasteryRegionsTests
{
    [Theory]
    [InlineData("Tyria", "Central Tyria")]
    [InlineData("Maguuma", "Heart of Thorns")]
    [InlineData("Desert", "Path of Fire")]
    [InlineData("Tundra", "Icebrood Saga")]
    [InlineData("Jade", "End of Dragons")]
    [InlineData("Sky", "Secrets of the Obscure")]
    [InlineData("Wild", "Janthir Wilds")]
    [InlineData("Magic", "Visions of Eternity")]
    public void A_region_code_and_its_expansion_belong_together(string code, string expansion)
    {
        Assert.Equal(expansion, MasteryRegions.DisplayName(code));
        Assert.Equal(code, MasteryRegions.CodeOf(expansion));
        Assert.Equal(code, MasteryRegions.CodeOf(expansion.ToUpperInvariant()));
    }

    [Fact]
    public void An_unknown_region_keeps_its_name()
    {
        Assert.Equal("Nieuw", MasteryRegions.DisplayName("Nieuw"));
        Assert.Equal("Nieuw", MasteryRegions.CodeOf("Nieuw"));
    }

    [Fact]
    public void Points_named_by_expansion_are_matched_to_the_tracks_of_that_region()
    {
        var progress = new MasteryAccountProgress(
            DateTimeOffset.UnixEpoch,
            [new AccountMastery(1, 0)],
            [new RegionPoints("Heart of Thorns", 11, 22), new RegionPoints("End of Dragons", 5, 15)],
            [],
            []);

        var regions = MasteryPlanPresenter.Regions(progress, MasteryTestData.Definitions, CultureInfo.GetCultureInfo("nl-NL"));

        Assert.Equal(2, regions.Count);
        Assert.Equal("22 punten verdiend, 11 uitgegeven", regions.Single(r => r.Region == "Maguuma").PointsText);
        Assert.Equal("15 punten verdiend, 5 uitgegeven", regions.Single(r => r.Region == "Jade").PointsText);
    }

    [Fact]
    public void One_point_is_written_in_the_singular()
    {
        var progress = new MasteryAccountProgress(DateTimeOffset.UnixEpoch, [], [new RegionPoints("Heart of Thorns", 1, 1)], [], []);

        var regions = MasteryPlanPresenter.Regions(progress, MasteryTestData.Definitions, CultureInfo.GetCultureInfo("nl-NL"));

        Assert.Equal("1 punt verdiend, 1 uitgegeven", regions.Single(r => r.Region == "Maguuma").PointsText);
    }

    [Fact]
    public void A_plan_that_names_the_expansion_gets_the_region_code()
    {
        var node = MasteryTestData.Node();
        MasteryTestData.Step(node, 0, 0)["region"] = "End of Dragons";

        var plan = MasteryPlanSerializer.Parse(node.ToJsonString(), MasteryTestData.Definitions).Plan!;

        Assert.Equal("Jade", plan.Phases[0].Steps[0].Region);
    }

    [Fact]
    public void The_prompt_gives_the_region_code_next_to_the_expansion()
    {
        var progress = new MasteryAccountProgress(DateTimeOffset.UnixEpoch, [], [new RegionPoints("End of Dragons", 5, 15)], [], []);

        var prompt = MasteryPromptBuilder.Build(progress, MasteryTestData.Definitions);

        Assert.Contains("\"region\": \"Jade\"", prompt, StringComparison.Ordinal);
        Assert.Contains("\"expansion\": \"End of Dragons\"", prompt, StringComparison.Ordinal);
    }
}
