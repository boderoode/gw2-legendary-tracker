using System.Globalization;
using Gw2LegendaryTracker.Core.Crafting;
using Gw2LegendaryTracker.Core.Goals;
using static Gw2LegendaryTracker.Tests.CraftingTestHelpers;

namespace Gw2LegendaryTracker.Tests;

/// <summary>Plans the Twilight tree from the recipe file that ships with the app.</summary>
public class ShippedRecipeDataTests
{
    private const int Twilight = 30704;
    private const int GiftOfFortune = 19626;
    private const int GiftOfMight = 19672;
    private const int GiftOfMagic = 19673;
    private const int MysticClover = 19675;
    private const int GlobOfEctoplasm = 19721;
    private const int ViciousFang = 24357;
    private const int ArmoredScale = 24289;
    private const int ViciousClaw = 24351;
    private const int AncientBone = 24358;
    private const int ObsidianShard = 19925;
    private const int IcyRunestone = 19676;
    private const int OrichalcumIngot = 19685;
    private const int Dusk = 29185;

    private static readonly string DataFolder = Path.Combine(AppContext.BaseDirectory, "data", "recipes");

    private static List<Recipe> ShippedRecipes()
    {
        var recipes = new List<Recipe>();
        foreach (var file in Directory.EnumerateFiles(DataFolder, "*.json"))
        {
            var result = LocalRecipeLoader.Parse(File.ReadAllText(file));
            Assert.Empty(result.Errors);
            recipes.AddRange(result.Recipes);
        }

        return recipes;
    }

    private static CraftPlan PlanTwilight(Gw2LegendaryTracker.Core.Inventory.InventoryModel inventory, params int[] completed) =>
        CraftingPlanner.Plan(new PlanRequest([new CraftGoal(Twilight) { Completed = completed.ToHashSet() }], inventory, new RecipeBook(ShippedRecipes())))[0];

    private static long Missing(CraftPlan plan, int itemId) => CraftingTestHelpers.Missing(plan, MaterialKey.Item(itemId));

    [Fact]
    public void Shipped_files_load_without_errors_and_with_unique_ids()
    {
        Assert.True(Directory.Exists(DataFolder), "the recipe data must be copied next to the tests");

        var recipes = ShippedRecipes();

        Assert.NotEmpty(recipes);
        Assert.Equal(recipes.Count, recipes.Select(r => r.Id).Distinct().Count());
        Assert.All(recipes, r => Assert.True(r.Id < 0 && r.Source == RecipeSource.Local));
    }

    [Fact]
    public void Twilight_has_the_expected_tree_and_totals_with_an_empty_inventory()
    {
        var plan = PlanTwilight(Inventory());

        // Twilight, Gift of Twilight, Gift of Metal, Gift of Mastery, Gift of Fortune, Gift of Magic, Gift of Might.
        Assert.Equal(7, plan.Steps.Count);
        Assert.Equal(0.0, plan.Progress);
        Assert.Equal(250, Missing(plan, ViciousFang));
        Assert.Equal(250, Missing(plan, ArmoredScale));
        Assert.Equal(250, Missing(plan, ViciousClaw));
        Assert.Equal(250, Missing(plan, AncientBone));
        Assert.Equal(77, Missing(plan, MysticClover));
        Assert.Equal(250, Missing(plan, ObsidianShard));
        Assert.Equal(100, Missing(plan, IcyRunestone));
        Assert.Equal(250, Missing(plan, OrichalcumIngot));
        Assert.Equal(1, Missing(plan, Dusk));
        Assert.Equal(NextStepKind.Gather, plan.Next.Kind);
    }

    [Fact]
    public void Owning_a_gift_removes_everything_that_gift_would_need()
    {
        var plan = PlanTwilight(Inventory([(GiftOfMight, 1)]));

        Assert.Equal(0, Missing(plan, ViciousFang));
        Assert.Equal(0, Missing(plan, AncientBone));
        Assert.Equal(6, plan.Steps.Count);
        // Gift of Fortune still needs its own 250 Globs of Ectoplasm.
        Assert.Equal(250, Missing(plan, GlobOfEctoplasm));
    }

    [Fact]
    public void Having_the_materials_for_a_gift_makes_crafting_it_the_next_step()
    {
        var plan = PlanTwilight(Inventory([(ViciousFang, 250), (ArmoredScale, 250), (ViciousClaw, 250), (AncientBone, 250)]));

        Assert.Equal(NextStepKind.Craft, plan.Next.Kind);
        Assert.Equal(GiftOfMight, plan.Next.Craft?.Recipe.OutputItemId);
        Assert.True(plan.Next.Craft?.IsReady);
    }

    [Fact]
    public void Ticking_a_gift_by_hand_removes_its_whole_subtree()
    {
        var plan = PlanTwilight(Inventory(), GiftOfFortune);

        Assert.Equal(0, Missing(plan, ViciousFang));
        Assert.Equal(0, Missing(plan, MysticClover));
        Assert.Equal(4, plan.Steps.Count);
        Assert.Contains(ChecklistBuilder.Build(plan), e => e.ItemId == GiftOfFortune && e.IsDone);
    }

    [Fact]
    public void The_presenter_names_the_forge_as_the_place_to_craft()
    {
        var plan = PlanTwilight(Inventory([(ViciousFang, 250), (ArmoredScale, 250), (ViciousClaw, 250), (AncientBone, 250)]));
        var goalPlan = new GoalPlan(new Goal(Twilight), plan, HasRecipeData: true, Error: null);
        var names = new Dictionary<int, string> { [GiftOfMight] = "Gift of Might" };

        var summary = GoalPresenter.Summarize(goalPlan, id => names.GetValueOrDefault(id, $"#{id}"), CultureInfo.InvariantCulture);

        Assert.Equal("Maak nu: 1× Gift of Might (in de Mystic Forge)", summary.NextStepText);
        Assert.Null(summary.Notice);
    }
}
