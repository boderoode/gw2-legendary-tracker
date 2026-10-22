using Gw2LegendaryTracker.Infrastructure.Persistence;

namespace Gw2LegendaryTracker.Tests;

public sealed class LocalRecipeFilesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gw2lt-recipes-" + Guid.NewGuid().ToString("N"));

    public LocalRecipeFilesTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static string Recipe(int id, int output, string status = "reviewed") => $$"""
        { "recipes": [ { "id": {{id}}, "status": "{{status}}", "output": { "itemId": {{output}}, "count": 1, "name": "X" },
                         "ingredients": [ { "itemId": 9, "count": 1, "name": "Y" } ] } ] }
        """;

    private string Folder(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public async Task Recipes_from_several_files_and_folders_are_combined_in_a_stable_order()
    {
        var shipped = Folder("shipped");
        var user = Folder("user");
        await File.WriteAllTextAsync(Path.Combine(shipped, "b.json"), Recipe(-2, 200));
        await File.WriteAllTextAsync(Path.Combine(shipped, "a.json"), Recipe(-1, 100));
        await File.WriteAllTextAsync(Path.Combine(user, "mine.json"), Recipe(-3, 300, "unreviewed"));

        var result = await LocalRecipeFiles.LoadAsync([shipped, user]);

        Assert.Empty(result.Errors);
        Assert.Equal([-1, -2, -3], result.Recipes.Select(r => r.Id));
        Assert.Equal([-3], result.UnreviewedIds);
    }

    [Fact]
    public async Task The_same_recipe_id_in_two_files_is_reported_and_the_second_is_skipped()
    {
        var folder = Folder("data");
        await File.WriteAllTextAsync(Path.Combine(folder, "a.json"), Recipe(-1, 100));
        await File.WriteAllTextAsync(Path.Combine(folder, "b.json"), Recipe(-1, 200));

        var result = await LocalRecipeFiles.LoadAsync([folder]);

        Assert.Equal([100], result.Recipes.Select(r => r.OutputItemId));
        var error = Assert.Single(result.Errors);
        Assert.StartsWith("b.json:", error, StringComparison.Ordinal);
        Assert.Contains("-1", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_broken_file_is_reported_by_name_and_does_not_block_the_others()
    {
        var folder = Folder("data");
        await File.WriteAllTextAsync(Path.Combine(folder, "broken.json"), "{ nope");
        await File.WriteAllTextAsync(Path.Combine(folder, "good.json"), Recipe(-1, 100));

        var result = await LocalRecipeFiles.LoadAsync([folder]);

        Assert.Single(result.Recipes);
        Assert.StartsWith("broken.json:", Assert.Single(result.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_folders_and_other_file_types_are_ignored()
    {
        var folder = Folder("data");
        await File.WriteAllTextAsync(Path.Combine(folder, "notes.txt"), "not a recipe file");

        var result = await LocalRecipeFiles.LoadAsync([Path.Combine(_root, "does-not-exist"), folder]);

        Assert.Empty(result.Recipes);
        Assert.Empty(result.Errors);
    }
}
