using System.Text.Json.Nodes;
using Gw2LegendaryTracker.Core.Masteries;

namespace Gw2LegendaryTracker.Tests;

public sealed class MasteryPlanSerializerTests
{
    private static MasteryPlanParseResult Parse(JsonNode node, IReadOnlyDictionary<int, MasteryDefinition>? definitions = null) =>
        MasteryPlanSerializer.Parse(node.ToJsonString(), definitions ?? MasteryTestData.Definitions);

    [Fact]
    public void A_valid_plan_is_read_without_warnings()
    {
        var result = MasteryPlanSerializer.Parse(MasteryTestData.ValidJson, MasteryTestData.Definitions);

        Assert.True(result.Succeeded, result.Error);
        Assert.Empty(result.Warnings);
        var plan = result.Plan!;
        Assert.Equal("Masteries in Cantha", plan.Title);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero), plan.CreatedUtc);
        Assert.Equal(["Je hebt End of Dragons"], plan.Assumptions);
        Assert.Equal(2, plan.Phases.Count);

        var first = plan.Phases[0].Steps[0];
        Assert.Equal(MasteryStepType.MasteryLevel, first.Type);
        Assert.Equal(30, first.MasteryId);
        Assert.Equal(0, first.MasteryLevel);
        Assert.Equal("[&BAAOAAA=]", first.Waypoint);
        Assert.Equal(MasteryPriority.High, first.Priority);
        Assert.Equal(new MasteryLink("Skiff", "https://wiki.guildwars2.com/wiki/Skiff"), Assert.Single(first.Links));
        Assert.Equal(["s1"], plan.Phases[0].Steps[1].Requires);
        Assert.Equal(6000, plan.Phases[1].Steps[0].AchievementId);
    }

    [Fact]
    public void Serialize_and_parse_round_trip()
    {
        var plan = MasteryTestData.Plan();

        var json = MasteryPlanSerializer.Serialize(plan);
        var again = MasteryPlanSerializer.Parse(json, MasteryTestData.Definitions);

        Assert.True(again.Succeeded, again.Error);
        Assert.Empty(again.Warnings);
        Assert.Equal(plan, again.Plan);
        Assert.Contains("\"format\": \"gw2-mastery-plan\"", json, StringComparison.Ordinal);
        Assert.Contains("\"type\": \"mastery-level\"", json, StringComparison.Ordinal);
        Assert.Contains("\"masteryLevel\": 0", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("```json\n{0}\n```")]
    [InlineData("```\n{0}\n```")]
    [InlineData("Hier is je plan:\n\n```json\n{0}\n```\n\nSucces!")]
    [InlineData("```JSON\r\n{0}\r\n```")]
    public void Json_in_a_code_block_is_accepted(string wrapper)
    {
        var text = wrapper.Replace("{0}", MasteryTestData.ValidJson, StringComparison.Ordinal);

        var result = MasteryPlanSerializer.Parse(text, MasteryTestData.Definitions);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(MasteryTestData.Plan(), result.Plan);
    }

    [Theory]
    [InlineData(null, "leeg")]
    [InlineData("", "leeg")]
    [InlineData("   ", "leeg")]
    [InlineData("```json\n```", "leeg")]
    [InlineData("{ niet json", "geen geldige JSON")]
    [InlineData("[1,2,3]", "geen geldige JSON")]
    [InlineData("""{"format":"gw2-legendary-tracker-export","version":1}""", "geen masteryplan")]
    [InlineData("""{"version":1}""", "geen masteryplan")]
    [InlineData("""{"format":"gw2-mastery-plan","version":2,"phases":[]}""", "nieuwere versie")]
    [InlineData("""{"format":"gw2-mastery-plan","phases":[]}""", "geen geldige versie")]
    [InlineData("""{"format":"gw2-mastery-plan","version":1,"phases":[]}""", "geen fases")]
    [InlineData("""{"format":"gw2-mastery-plan","version":1}""", "geen fases")]
    public void Invalid_files_are_refused(string? text, string expected)
    {
        var result = MasteryPlanSerializer.Parse(text, MasteryTestData.Definitions);

        Assert.False(result.Succeeded);
        Assert.Null(result.Plan);
        Assert.Contains(expected, result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_over_the_size_limit_is_refused()
    {
        var node = MasteryTestData.Node();
        node["summary"] = new string('x', MasteryPlanSerializer.MaxBytes);

        var result = Parse(node);

        Assert.False(result.Succeeded);
        Assert.Contains("te groot", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Too_many_phases_are_refused()
    {
        var node = MasteryTestData.Node();
        var phases = node["phases"]!.AsArray();
        for (var i = 0; i < MasteryPlanSerializer.MaxPhases; i++)
        {
            phases.Add(new JsonObject { ["id"] = $"extra{i}", ["title"] = "x", ["steps"] = new JsonArray() });
        }

        var result = Parse(node);

        Assert.False(result.Succeeded);
        Assert.Contains("hooguit 20 zijn", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Too_many_steps_are_refused()
    {
        var node = MasteryTestData.Node();
        var steps = node["phases"]![1]!["steps"]!.AsArray();
        for (var i = 0; i < MasteryPlanSerializer.MaxSteps; i++)
        {
            steps.Add(new JsonObject { ["id"] = $"extra{i}", ["title"] = "x" });
        }

        var result = Parse(node);

        Assert.False(result.Succeeded);
        Assert.Contains("hooguit 300 zijn", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Duplicate_step_ids_are_refused()
    {
        var node = MasteryTestData.Node();
        MasteryTestData.Step(node, 1, 0)["id"] = "s1";

        var result = Parse(node);

        Assert.False(result.Succeeded);
        Assert.Contains("\"s1\" komt meer dan één keer voor", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Duplicate_phase_ids_are_refused()
    {
        var node = MasteryTestData.Node();
        node["phases"]![1]!["id"] = "p1";

        var result = Parse(node);

        Assert.False(result.Succeeded);
        Assert.Contains("fase-id \"p1\"", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_step_without_id_is_refused(string? id)
    {
        var node = MasteryTestData.Node();
        MasteryTestData.Step(node, 0, 0)["id"] = id;

        var result = Parse(node);

        Assert.False(result.Succeeded);
        Assert.Contains("geen id", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_too_long_id_is_refused()
    {
        var node = MasteryTestData.Node();
        node["phases"]![0]!["id"] = new string('p', MasteryPlanSerializer.MaxIdLength + 1);

        var result = Parse(node);

        Assert.False(result.Succeeded);
        Assert.Contains("langer is dan 64 tekens", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Requires_to_an_unknown_step_is_refused()
    {
        var node = MasteryTestData.Node();
        MasteryTestData.Step(node, 0, 1)["requires"] = new JsonArray("s1", "bestaat-niet");

        var result = Parse(node);

        Assert.False(result.Succeeded);
        Assert.Contains("\"bestaat-niet\"", result.Error, StringComparison.Ordinal);
        Assert.Contains("bestaat niet", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_cycle_in_requires_is_refused_and_named()
    {
        var node = MasteryTestData.Node();
        MasteryTestData.Step(node, 0, 0)["requires"] = new JsonArray("s3");

        var result = Parse(node);

        Assert.False(result.Succeeded);
        Assert.Contains("cirkel", result.Error, StringComparison.Ordinal);
        Assert.Contains("s1 → s3 → s2 → s1", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_step_that_requires_itself_is_a_cycle()
    {
        var node = MasteryTestData.Node();
        MasteryTestData.Step(node, 0, 0)["requires"] = new JsonArray("s1");

        var result = Parse(node);

        Assert.False(result.Succeeded);
        Assert.Contains("s1 → s1", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_type_becomes_other_with_a_warning()
    {
        var node = MasteryTestData.Node();
        MasteryTestData.Step(node, 0, 1)["type"] = "dungeon";

        var result = Parse(node);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(MasteryStepType.Other, result.Plan!.Phases[0].Steps[1].Type);
        Assert.Contains(result.Warnings, w => w.Contains("onbekend type \"dungeon\"", StringComparison.Ordinal));
    }

    [Fact]
    public void An_unknown_priority_becomes_normal_with_a_warning()
    {
        var node = MasteryTestData.Node();
        MasteryTestData.Step(node, 0, 0)["priority"] = "urgent";

        var result = Parse(node);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(MasteryPriority.Normal, result.Plan!.Phases[0].Steps[0].Priority);
        Assert.Contains(result.Warnings, w => w.Contains("onbekende prioriteit", StringComparison.Ordinal));
    }

    [Fact]
    public void A_missing_type_and_priority_get_defaults_without_warning()
    {
        var node = MasteryTestData.Node();
        MasteryTestData.Step(node, 0, 1).Remove("type");
        MasteryTestData.Step(node, 0, 1).Remove("priority");

        var result = Parse(node);

        Assert.Empty(result.Warnings);
        Assert.Equal(MasteryStepType.Other, result.Plan!.Phases[0].Steps[1].Type);
        Assert.Equal(MasteryPriority.Normal, result.Plan.Phases[0].Steps[1].Priority);
    }

    [Fact]
    public void An_unknown_mastery_id_is_cleared_with_a_warning()
    {
        var node = MasteryTestData.Node();
        MasteryTestData.Step(node, 0, 0)["masteryId"] = 999;

        var result = Parse(node);

        var step = result.Plan!.Phases[0].Steps[0];
        Assert.Null(step.MasteryId);
        Assert.Null(step.MasteryLevel);
        Assert.False(step.CanAutoComplete);
        Assert.Contains(result.Warnings, w => w.Contains("masteryId 999", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(-1)]
    public void A_mastery_level_out_of_range_is_cleared_with_a_warning(int level)
    {
        var node = MasteryTestData.Node();
        MasteryTestData.Step(node, 0, 0)["masteryLevel"] = level;

        var result = Parse(node);

        var step = result.Plan!.Phases[0].Steps[0];
        Assert.Null(step.MasteryId);
        Assert.Null(step.MasteryLevel);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void Without_definitions_the_mastery_ids_are_kept_with_one_warning()
    {
        var node = MasteryTestData.Node();
        MasteryTestData.Step(node, 0, 1)["masteryId"] = 999;
        MasteryTestData.Step(node, 0, 1)["masteryLevel"] = 7;

        var result = MasteryPlanSerializer.Parse(node.ToJsonString(), definitions: null);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(30, result.Plan!.Phases[0].Steps[0].MasteryId);
        Assert.Equal(999, result.Plan.Phases[0].Steps[1].MasteryId);
        Assert.Single(result.Warnings, w => w.Contains("niet worden gecontroleerd", StringComparison.Ordinal));
    }

    [Fact]
    public void A_mastery_level_step_without_id_is_manual_with_a_warning()
    {
        var node = MasteryTestData.Node();
        MasteryTestData.Step(node, 0, 0)["masteryId"] = null;
        MasteryTestData.Step(node, 0, 0)["masteryLevel"] = null;

        var result = Parse(node);

        Assert.False(result.Plan!.Phases[0].Steps[0].CanAutoComplete);
        Assert.Contains(result.Warnings, w => w.Contains("zelf af", StringComparison.Ordinal));
    }

    [Fact]
    public void An_achievement_step_without_id_is_manual_with_a_warning()
    {
        var node = MasteryTestData.Node();
        MasteryTestData.Step(node, 1, 0)["achievementId"] = null;

        var result = Parse(node);

        Assert.False(result.Plan!.Phases[1].Steps[0].CanAutoComplete);
        Assert.Contains(result.Warnings, w => w.Contains("achievement-stap zonder achievementId", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("http://wiki.guildwars2.com/wiki/Skiff")]
    [InlineData("https://example.com/wiki/Skiff")]
    [InlineData("https://wiki.guildwars2.com.evil.example/wiki")]
    [InlineData("https://user@wiki.guildwars2.com/wiki")]
    [InlineData("javascript:alert(1)")]
    [InlineData("niet een url")]
    public void Links_outside_the_wiki_are_left_out(string url)
    {
        var node = MasteryTestData.Node();
        MasteryTestData.Step(node, 0, 0)["links"] = new JsonArray(new JsonObject { ["label"] = "x", ["url"] = url });

        var result = Parse(node);

        Assert.True(result.Succeeded, result.Error);
        Assert.Empty(result.Plan!.Phases[0].Steps[0].Links);
        Assert.Contains(result.Warnings, w => w.Contains("wiki.guildwars2.com", StringComparison.Ordinal));
    }

    [Fact]
    public void More_than_ten_links_are_cut_off()
    {
        var node = MasteryTestData.Node();
        var links = new JsonArray();
        for (var i = 0; i < 12; i++)
        {
            links.Add(new JsonObject { ["label"] = $"L{i}", ["url"] = $"https://wiki.guildwars2.com/wiki/{i}" });
        }

        MasteryTestData.Step(node, 0, 0)["links"] = links;

        var result = Parse(node);

        Assert.Equal(MasteryPlanSerializer.MaxLinksPerStep, result.Plan!.Phases[0].Steps[0].Links.Count);
        Assert.Contains(result.Warnings, w => w.Contains("meer dan 10 links", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("BAAOAAA=")]
    [InlineData("[BAAOAAA=]")]
    [InlineData("[&BAAOAAA=] extra")]
    [InlineData("[&niet geldig]")]
    public void An_invalid_waypoint_is_left_out(string waypoint)
    {
        var node = MasteryTestData.Node();
        MasteryTestData.Step(node, 0, 0)["waypoint"] = waypoint;

        var result = Parse(node);

        Assert.Null(result.Plan!.Phases[0].Steps[0].Waypoint);
        Assert.Contains(result.Warnings, w => w.Contains("waypoint", StringComparison.Ordinal));
    }

    [Fact]
    public void Negative_minutes_are_left_out()
    {
        var node = MasteryTestData.Node();
        MasteryTestData.Step(node, 0, 0)["estimatedMinutes"] = -5;

        var result = Parse(node);

        Assert.Null(result.Plan!.Phases[0].Steps[0].EstimatedMinutes);
        Assert.Contains(result.Warnings, w => w.Contains("negatieve tijdsinschatting", StringComparison.Ordinal));
    }

    [Fact]
    public void Too_long_texts_are_shortened_with_a_warning()
    {
        var node = MasteryTestData.Node();
        MasteryTestData.Step(node, 0, 0)["title"] = new string('t', MasteryPlanSerializer.MaxShortText + 10);
        MasteryTestData.Step(node, 0, 0)["notes"] = new string('n', MasteryPlanSerializer.MaxLongText + 10);

        var result = Parse(node);

        var step = result.Plan!.Phases[0].Steps[0];
        Assert.Equal(MasteryPlanSerializer.MaxShortText, step.Title.Length);
        Assert.Equal(MasteryPlanSerializer.MaxLongText, step.Notes!.Length);
        Assert.Equal(2, result.Warnings.Count(w => w.Contains("ingekort", StringComparison.Ordinal)));
    }

    [Fact]
    public void Too_many_assumptions_are_cut_off()
    {
        var node = MasteryTestData.Node();
        var assumptions = new JsonArray();
        for (var i = 0; i < MasteryPlanSerializer.MaxAssumptions + 5; i++)
        {
            assumptions.Add($"Aanname {i}");
        }

        node["assumptions"] = assumptions;

        var result = Parse(node);

        Assert.Equal(MasteryPlanSerializer.MaxAssumptions, result.Plan!.Assumptions.Count);
        Assert.Contains(result.Warnings, w => w.Contains("aannames", StringComparison.Ordinal));
    }

    [Fact]
    public void Numbers_written_as_text_and_trailing_commas_are_accepted()
    {
        var text = MasteryTestData.ValidJson
            .Replace("\"masteryId\": 30,", "\"masteryId\": \"30\",", StringComparison.Ordinal)
            .Replace("\"notes\": \"Hou je XP-boosters klaar.\"", "\"notes\": \"Hou je XP-boosters klaar.\",", StringComparison.Ordinal);

        var result = MasteryPlanSerializer.Parse(text, MasteryTestData.Definitions);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(30, result.Plan!.Phases[0].Steps[0].MasteryId);
    }

    [Fact]
    public void Duplicate_requires_are_merged()
    {
        var node = MasteryTestData.Node();
        MasteryTestData.Step(node, 0, 1)["requires"] = new JsonArray("s1", "s1");

        var result = Parse(node);

        Assert.Equal(["s1"], result.Plan!.Phases[0].Steps[1].Requires);
    }
}
