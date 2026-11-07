using System.Text.RegularExpressions;
using Gw2LegendaryTracker.Core.Masteries;

namespace Gw2LegendaryTracker.Tests;

public sealed partial class MasteryPromptBuilderTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    private static MasteryAccountProgress Progress() => new(
        At,
        [new AccountMastery(30, 0)],
        [new RegionPoints("Jade", 3, 5)],
        [30],
        [new AchievementProgress(6000, false, 2, 4), new AchievementProgress(9999, true, 1, 1)]);

    [Fact]
    public void The_prompt_describes_the_format_and_the_rules()
    {
        var prompt = MasteryPromptBuilder.Build(progress: null, definitions: null);

        Assert.Contains("\"format\": \"gw2-mastery-plan\"", prompt, StringComparison.Ordinal);
        Assert.Contains("\"version\": 1", prompt, StringComparison.Ordinal);
        foreach (var field in new[] { "title", "createdUtc", "author", "summary", "assumptions", "phases", "goal", "steps", "description", "type", "region", "masteryId", "masteryLevel", "achievementId", "map", "waypoint", "estimatedMinutes", "priority", "requires", "links", "notes" })
        {
            Assert.Contains($"\"{field}\"", prompt, StringComparison.Ordinal);
        }

        Assert.Contains("uitsluitend één JSON-object", prompt, StringComparison.Ordinal);
        Assert.Contains("alleen echte id's", prompt, StringComparison.Ordinal);
        Assert.Contains("`null`", prompt, StringComparison.Ordinal);
        Assert.Contains("Nederlands", prompt, StringComparison.Ordinal);
        Assert.Contains("Er is nog geen voortgang bekend", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain(MasteryPromptBuilder.ProgressPlaceholder, prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("<!--", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_type_and_priority_in_the_prompt_is_one_the_app_reads()
    {
        foreach (MasteryStepType type in Enum.GetValues<MasteryStepType>())
        {
            Assert.Contains($"\"{MasteryPlanSerializer.TypeName(type)}\"", MasteryPromptBuilder.TemplateText, StringComparison.Ordinal);
        }

        foreach (MasteryPriority priority in Enum.GetValues<MasteryPriority>())
        {
            Assert.Contains($"\"{MasteryPlanSerializer.PriorityName(priority)}\"", MasteryPromptBuilder.TemplateText, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_example_in_the_prompt_is_a_valid_plan()
    {
        var example = MasteryPlanSerializer.ExtractJson(MasteryPromptBuilder.TemplateText[MasteryPromptBuilder.TemplateText.IndexOf("## Formaat", StringComparison.Ordinal)..]);

        var result = MasteryPlanSerializer.Parse(example, MasteryTestData.Definitions);

        Assert.True(result.Succeeded, result.Error);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void The_prompt_contains_the_current_progress_as_json()
    {
        var prompt = MasteryPromptBuilder.Build(Progress(), MasteryTestData.Definitions, MasteryTestData.Plan());

        Assert.Contains("\"capturedUtc\": \"2026-11-07T12:00:00Z\"", prompt, StringComparison.Ordinal);
        Assert.Contains("\"region\": \"Jade\"", prompt, StringComparison.Ordinal);
        Assert.Contains("\"earned\": 5", prompt, StringComparison.Ordinal);
        Assert.Contains("\"masteryId\": 30", prompt, StringComparison.Ordinal);
        Assert.Contains("\"name\": \"Skiff\"", prompt, StringComparison.Ordinal);
        Assert.Contains("\"trainedLevels\": 1", prompt, StringComparison.Ordinal);
        Assert.Contains("\"achievementId\": 6000", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("9999", prompt, StringComparison.Ordinal); // not used by the plan
    }

    [Fact]
    public void Without_account_progress_the_tracks_are_still_listed()
    {
        var prompt = MasteryPromptBuilder.Build(progress: null, MasteryTestData.Definitions);

        Assert.Contains("\"name\": \"Exalted Lore\"", prompt, StringComparison.Ordinal);
        Assert.Contains("\"trainedLevels\": null", prompt, StringComparison.Ordinal);
        Assert.Contains("niet bekend", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void The_prompt_never_contains_an_api_key()
    {
        // Even when a key ended up in the data by mistake (here: as a region and track name), it must not reach the clipboard.
        var leaky = new Dictionary<int, MasteryDefinition>
        {
            [1] = new(1, TestData.Key, TestData.Key, 1, [new("Bearer " + TestData.Key, 1, 1)]),
        };
        var progress = new MasteryAccountProgress(At, [], [new RegionPoints(TestData.Key, 1, 1)], [], []);

        var prompts = new[]
        {
            MasteryPromptBuilder.Build(progress: null, definitions: null),
            MasteryPromptBuilder.Build(Progress(), MasteryTestData.Definitions, MasteryTestData.Plan()),
            MasteryPromptBuilder.Build(progress, leaky),
        };

        foreach (var prompt in prompts)
        {
            Assert.DoesNotContain(TestData.Key, prompt, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotMatch(KeyShape(), prompt);
            Assert.DoesNotContain("Bearer 1", prompt, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void The_documented_prompt_is_the_one_the_app_uses()
    {
        var docs = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "docs", "mastery-plan-prompt.md")).Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.EndsWith(MasteryPromptBuilder.TemplateText, docs, StringComparison.Ordinal);
        Assert.Contains(MasteryPromptBuilder.ProgressPlaceholder, MasteryPromptBuilder.TemplateText, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}")]
    private static partial Regex KeyShape();
}
