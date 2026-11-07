using System.Text.Json.Nodes;
using Gw2LegendaryTracker.Core.Masteries;

namespace Gw2LegendaryTracker.Tests;

/// <summary>A small, valid mastery plan and matching mastery definitions for the mastery tests.</summary>
internal static class MasteryTestData
{
    public const string ValidJson = """
        {
          "format": "gw2-mastery-plan",
          "version": 1,
          "title": "Masteries in Cantha",
          "createdUtc": "2026-11-01T10:00:00Z",
          "author": "Test-AI",
          "summary": "Eerst skiff, dan jade bot.",
          "assumptions": ["Je hebt End of Dragons"],
          "phases": [
            {
              "id": "p1",
              "title": "Begin",
              "goal": "Skiff vrijspelen",
              "steps": [
                {
                  "id": "s1",
                  "title": "Skiff level 1",
                  "description": "Train de eerste skiff-level.",
                  "type": "mastery-level",
                  "region": "Jade",
                  "masteryId": 30,
                  "masteryLevel": 0,
                  "achievementId": null,
                  "map": "Seitung Province",
                  "waypoint": "[&BAAOAAA=]",
                  "estimatedMinutes": 30,
                  "priority": "high",
                  "requires": [],
                  "links": [{ "label": "Skiff", "url": "https://wiki.guildwars2.com/wiki/Skiff" }],
                  "notes": "Hou je XP-boosters klaar."
                },
                {
                  "id": "s2",
                  "title": "Story hoofdstuk 1",
                  "description": "Speel het eerste hoofdstuk.",
                  "type": "story",
                  "region": "Jade",
                  "masteryId": null,
                  "masteryLevel": null,
                  "achievementId": null,
                  "map": null,
                  "waypoint": null,
                  "estimatedMinutes": 60,
                  "priority": "normal",
                  "requires": ["s1"],
                  "links": [],
                  "notes": null
                }
              ]
            },
            {
              "id": "p2",
              "title": "Verder",
              "goal": "Achievement halen",
              "steps": [
                {
                  "id": "s3",
                  "title": "Een achievement",
                  "description": "Haal de achievement.",
                  "type": "achievement",
                  "region": "Jade",
                  "masteryId": null,
                  "masteryLevel": null,
                  "achievementId": 6000,
                  "map": null,
                  "waypoint": null,
                  "estimatedMinutes": null,
                  "priority": "low",
                  "requires": ["s2"],
                  "links": [],
                  "notes": null
                }
              ]
            }
          ]
        }
        """;

    public static IReadOnlyDictionary<int, MasteryDefinition> Definitions { get; } = new Dictionary<int, MasteryDefinition>
    {
        [1] = new(1, "Exalted Lore", "Maguuma", 1, [Level("A"), Level("B"), Level("C")]),
        [30] = new(30, "Skiff", "Jade", 1, [Level("Skiff 1"), Level("Skiff 2")]),
        [31] = new(31, "Jade Bot", "Jade", 2, [Level("Bot 1")]),
    };

    public static JsonNode Node() => JsonNode.Parse(ValidJson)!;

    public static JsonObject Step(JsonNode root, int phase, int step) => root["phases"]![phase]!["steps"]![step]!.AsObject();

    public static MasteryPlan Plan() => MasteryPlanSerializer.Parse(ValidJson, Definitions).Plan!;

    private static MasteryLevelDefinition Level(string name) => new(name, 1, 254_000);
}
