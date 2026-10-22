using Gw2LegendaryTracker.Core.Crafting;
using Gw2LegendaryTracker.Core.Inventory;

namespace Gw2LegendaryTracker.Core.Goals;

/// <summary>The plan of one goal, or the reason there is none.</summary>
/// <param name="HasRecipeData">False when no recipe is known for the goal itself, so the plan can only say "obtain it".</param>
public sealed record GoalPlan(Goal Goal, CraftPlan? Plan, bool HasRecipeData, string? Error);

/// <summary>
/// Keeps the plans of all goals current. Both windows read from here, so they always show the same numbers.
/// Recalculates when the goals, the inventory or the recipes change.
/// </summary>
public sealed class PlanningService : IDisposable
{
    private readonly GoalService _goals;
    private readonly InventoryProvider _inventory;
    private readonly object _gate = new();
    private RecipeBook _recipes = RecipeBook.Empty;
    private IReadOnlyList<GoalPlan> _current = [];

    public PlanningService(GoalService goals, InventoryProvider inventory)
    {
        _goals = goals;
        _inventory = inventory;
        _goals.Changed += OnInputChanged;
        _inventory.Changed += OnInputChanged;
    }

    public IReadOnlyList<GoalPlan> Current => Volatile.Read(ref _current);

    /// <summary>Raised after the plans were recalculated. May fire on any thread.</summary>
    public event EventHandler<IReadOnlyList<GoalPlan>>? Changed;

    public RecipeBook Recipes
    {
        get
        {
            lock (_gate)
            {
                return _recipes;
            }
        }
    }

    public void SetRecipes(RecipeBook recipes)
    {
        ArgumentNullException.ThrowIfNull(recipes);
        lock (_gate)
        {
            _recipes = recipes;
        }

        Recalculate();
    }

    public void Dispose()
    {
        _goals.Changed -= OnInputChanged;
        _inventory.Changed -= OnInputChanged;
    }

    private void OnInputChanged(object? sender, object e) => Recalculate();

    private void Recalculate()
    {
        IReadOnlyList<GoalPlan> result;
        lock (_gate)
        {
            result = Calculate(_goals.Goals, _inventory.Current, _recipes);
            Volatile.Write(ref _current, result);
        }

        Changed?.Invoke(this, result);
    }

    private static List<GoalPlan> Calculate(IReadOnlyList<Goal> goals, InventoryModel inventory, RecipeBook recipes)
    {
        var craftGoals = goals.Select(g => new CraftGoal(g.ItemId, g.Quantity) { Completed = g.CompletedItemIds }).ToList();

        try
        {
            var plans = CraftingPlanner.Plan(new PlanRequest(craftGoals, inventory, recipes));
            return [.. goals.Select((g, i) => new GoalPlan(g, plans[i], recipes.RecipesFor(g.ItemId).Count > 0, null))];
        }
        catch (CraftingPlanException ex)
        {
            return [.. goals.Select(g => new GoalPlan(g, null, recipes.RecipesFor(g.ItemId).Count > 0, ex.Message))];
        }
    }
}
