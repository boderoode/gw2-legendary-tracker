using Gw2LegendaryTracker.Core.Inventory;

namespace Gw2LegendaryTracker.Core.Crafting;

public sealed record PlanRequest(
    IReadOnlyList<CraftGoal> Goals,
    InventoryModel Inventory,
    RecipeBook Recipes)
{
    /// <summary>Amounts that must stay untouched (e.g. reserved for something else).</summary>
    public IReadOnlyDictionary<MaterialKey, long>? Reserved { get; init; }

    /// <summary>Forces a recipe for an item: item id to recipe id. Ignored when the recipe does not produce that item.</summary>
    public IReadOnlyDictionary<int, int>? RouteOverrides { get; init; }

    /// <summary>Safety limit on evaluated tree positions.</summary>
    public int MaxNodes { get; init; } = 50_000;
}

/// <summary>
/// Turns goals into crafting plans. Pure and deterministic: the same inputs always give the same plan.
/// <list type="bullet">
/// <item>Owned items are used first; only the rest is crafted, recursively.</item>
/// <item>Crafting more than needed (recipes that output several items) leaves surplus that later positions reuse.</item>
/// <item>With several recipes for an item, the route with the fewest missing units wins, then the fewest crafts, then the lowest recipe id.</item>
/// <item>Goals are planned in order and share one stock: earlier goals have priority over later ones.</item>
/// </list>
/// </summary>
public static class CraftingPlanner
{
    public static IReadOnlyList<CraftPlan> Plan(PlanRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var context = new Context(request);
        var ledger = new Ledger(request.Inventory, request.Reserved);
        var plans = new List<CraftPlan>(request.Goals.Count);

        foreach (var goal in request.Goals)
        {
            if (goal.Quantity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(request), "A goal needs a positive quantity.");
            }

            context.Completed = goal.Completed;
            var root = Build(MaterialKey.Item(goal.ItemId), goal.Quantity, ledger, [], context);
            plans.Add(Summarize(goal, root));
        }

        return plans;
    }

    public static CraftPlan Plan(CraftGoal goal, InventoryModel inventory, RecipeBook recipes) =>
        Plan(new PlanRequest([goal], inventory, recipes))[0];

    private static PlanNode Build(MaterialKey key, long required, Ledger ledger, List<int> path, Context context)
    {
        context.Spend();

        // Marked done by hand (never the goal itself): covered without touching stock.
        if (path.Count > 0 && key.Kind == MaterialKind.Item && context.Completed?.Contains(key.Id) == true)
        {
            var hidden = context.StructureSize(key.Id);
            return new PlanNode(key, required, required, 0, null, 0, [], [], hidden, hidden, 0, 0, IsManual: true);
        }

        var taken = ledger.Take(key, required);
        var deficit = required - taken;

        if (deficit == 0)
        {
            // Covered by stock: nothing below is needed, but the hidden positions still count as satisfied for progress.
            var size = key.Kind == MaterialKind.Item ? context.StructureSize(key.Id) : 1;
            return new PlanNode(key, required, taken, 0, null, 0, [], [], size, size, 0, 0);
        }

        var candidates = key.Kind == MaterialKind.Item && !path.Contains(key.Id)
            ? context.Candidates(key.Id)
            : [];

        if (candidates.Count == 0)
        {
            return new PlanNode(key, required, taken, deficit, null, 0, [], [], 1, 0, deficit, 0);
        }

        path.Add(key.Id);
        PlanNode? best = null;
        Ledger? bestLedger = null;
        var evaluated = new List<PlanNode>(candidates.Count);
        foreach (var recipe in candidates)
        {
            var trial = ledger.Clone();
            var node = Expand(key, required, taken, deficit, recipe, trial, path, context);
            evaluated.Add(node);
            if (best is null || IsBetter(node, best))
            {
                best = node;
                bestLedger = trial;
            }
        }

        path.RemoveAt(path.Count - 1);

        ledger.CopyFrom(bestLedger!);
        var options = evaluated
            .Select(n => new RouteOption(n.Recipe!, n.MissingUnits, n.CraftActions, ReferenceEquals(n, best)))
            .ToList();
        return best! with { Alternatives = options };
    }

    private static PlanNode Expand(
        MaterialKey key,
        long required,
        long taken,
        long deficit,
        Recipe recipe,
        Ledger trial,
        List<int> path,
        Context context)
    {
        var crafts = (deficit + recipe.OutputCount - 1) / recipe.OutputCount;

        var children = new List<PlanNode>(recipe.Ingredients.Count);
        foreach (var ingredient in recipe.Ingredients)
        {
            children.Add(Build(ingredient.Key, checked(ingredient.Count * crafts), trial, path, context));
        }

        var surplus = (crafts * recipe.OutputCount) - deficit;
        if (surplus > 0)
        {
            trial.Add(key, surplus);
        }

        var nodeCount = 1 + children.Sum(c => c.NodeCount);
        var childrenSatisfied = children.All(c => c.IsSatisfied);
        var satisfied = (childrenSatisfied ? 1 : 0) + children.Sum(c => c.SatisfiedCount);

        return new PlanNode(
            key,
            required,
            taken,
            0,
            recipe,
            crafts,
            children,
            [],
            nodeCount,
            satisfied,
            children.Sum(c => c.MissingUnits),
            crafts + children.Sum(c => c.CraftActions));
    }

    private static bool IsBetter(PlanNode candidate, PlanNode best)
    {
        if (candidate.MissingUnits != best.MissingUnits)
        {
            return candidate.MissingUnits < best.MissingUnits;
        }

        // Equal on everything else keeps the earlier (lower id) recipe.
        return candidate.CraftActions < best.CraftActions;
    }

    private static CraftPlan Summarize(CraftGoal goal, PlanNode root)
    {
        var used = new Dictionary<MaterialKey, long>();
        var missing = new Dictionary<MaterialKey, long>();
        var steps = new List<CraftStep>();
        var leavesWithGap = new List<PlanNode>();

        Walk(root);

        void Walk(PlanNode node)
        {
            foreach (var child in node.Children)
            {
                Walk(child);
            }

            if (node.FromStock > 0 && !node.IsManual)
            {
                used[node.Key] = used.GetValueOrDefault(node.Key) + node.FromStock;
            }

            if (node.Missing > 0)
            {
                missing[node.Key] = missing.GetValueOrDefault(node.Key) + node.Missing;
                leavesWithGap.Add(node);
            }

            if (node.Recipe is not null)
            {
                steps.Add(new CraftStep(node.Recipe, node.Crafts, node.Children.All(c => c.IsCoveredByStock)));
            }
        }

        var materials = used.Keys.Union(missing.Keys)
            .OrderBy(k => k.Kind)
            .ThenBy(k => k.Id)
            .Select(k => new MaterialLine(k, used.GetValueOrDefault(k), missing.GetValueOrDefault(k)))
            .ToList();

        return new CraftPlan(goal, root, materials, steps, ChooseNext(root, steps, leavesWithGap));
    }

    private static NextStep ChooseNext(PlanNode root, List<CraftStep> steps, List<PlanNode> leavesWithGap)
    {
        if (root.IsCoveredByStock)
        {
            return new NextStep(NextStepKind.Done);
        }

        // Steps are in dependency order (ingredients before the things made from them), so the first
        // ready step is always safe to do now.
        if (steps.FirstOrDefault(s => s.IsReady) is { } ready)
        {
            return new NextStep(NextStepKind.Craft, ready);
        }

        var gap = leavesWithGap[0];
        return new NextStep(NextStepKind.Gather, Material: gap.Key, Amount: gap.Missing);
    }

    /// <summary>Tracks what is still owned while a plan is built. Cheap to copy; only the changes are stored.</summary>
    private sealed class Ledger
    {
        private readonly InventoryModel _inventory;
        private readonly IReadOnlyDictionary<MaterialKey, long>? _reserved;
        private Dictionary<MaterialKey, long> _delta = [];

        public Ledger(InventoryModel inventory, IReadOnlyDictionary<MaterialKey, long>? reserved)
        {
            _inventory = inventory;
            _reserved = reserved;
        }

        public long Take(MaterialKey key, long wanted)
        {
            var taken = Math.Min(Available(key), wanted);
            if (taken > 0)
            {
                _delta[key] = _delta.GetValueOrDefault(key) - taken;
            }

            return taken;
        }

        public void Add(MaterialKey key, long amount) => _delta[key] = _delta.GetValueOrDefault(key) + amount;

        public Ledger Clone() => new(_inventory, _reserved) { _delta = new Dictionary<MaterialKey, long>(_delta) };

        public void CopyFrom(Ledger other) => _delta = new Dictionary<MaterialKey, long>(other._delta);

        private long Available(MaterialKey key)
        {
            var owned = key.Kind == MaterialKind.Item ? _inventory.CountOf(key.Id) : _inventory.CurrencyAmount(key.Id);
            var reserved = _reserved?.GetValueOrDefault(key) ?? 0;
            return Math.Max(0, Math.Max(0, owned - reserved) + _delta.GetValueOrDefault(key));
        }
    }

    private sealed class Context
    {
        private readonly PlanRequest _request;
        private readonly Dictionary<int, int> _structure = [];
        private int _budget;

        /// <summary>The manual completion marks of the goal currently being planned.</summary>
        public IReadOnlySet<int>? Completed { get; set; }

        public Context(PlanRequest request)
        {
            _request = request;
            _budget = request.MaxNodes;
        }

        public void Spend()
        {
            if (--_budget < 0)
            {
                throw new CraftingPlanException("Deze crafting tree is te groot om door te rekenen.");
            }
        }

        /// <summary>Recipes to consider for an item: all of them, or only the one the user forced.</summary>
        public IReadOnlyList<Recipe> Candidates(int itemId)
        {
            var all = _request.Recipes.RecipesFor(itemId);
            if (_request.RouteOverrides?.TryGetValue(itemId, out var recipeId) == true && all.FirstOrDefault(r => r.Id == recipeId) is { } forced)
            {
                return [forced];
            }

            return all;
        }

        /// <summary>Number of positions in the tree below an item when it is crafted by its first candidate recipe.</summary>
        public int StructureSize(int itemId) => StructureSize(itemId, []);

        private int StructureSize(int itemId, HashSet<int> visiting)
        {
            if (_structure.TryGetValue(itemId, out var cached))
            {
                return cached;
            }

            var candidates = Candidates(itemId);
            if (candidates.Count == 0 || !visiting.Add(itemId))
            {
                return 1;
            }

            var size = 1;
            foreach (var ingredient in candidates[0].Ingredients)
            {
                size += ingredient.Key.Kind == MaterialKind.Item ? StructureSize(ingredient.Key.Id, visiting) : 1;
            }

            visiting.Remove(itemId);
            _structure[itemId] = size;
            return size;
        }
    }
}
