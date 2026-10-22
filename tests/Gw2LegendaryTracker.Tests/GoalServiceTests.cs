using Gw2LegendaryTracker.Core.Goals;

namespace Gw2LegendaryTracker.Tests;

public sealed class GoalServiceTests : IDisposable
{
    private readonly FakeGoalStore _store = new();
    private readonly GoalService _service;

    public GoalServiceTests() => _service = new GoalService(_store);

    public void Dispose() => _service.Dispose();

    [Fact]
    public async Task Add_persists_before_notifying()
    {
        IReadOnlyList<Goal>? savedWhenNotified = null;
        _service.Changed += (_, _) => savedWhenNotified = _store.Saved;

        await _service.AddAsync(10);

        Assert.Equal([10], savedWhenNotified?.Select(g => g.ItemId));
        Assert.Equal([10], _service.Goals.Select(g => g.ItemId));
    }

    [Fact]
    public async Task Adding_the_same_goal_twice_changes_nothing()
    {
        await _service.AddAsync(10);
        var saves = _store.SaveCount;

        await _service.AddAsync(10);

        Assert.Equal(saves, _store.SaveCount);
        Assert.Single(_service.Goals);
    }

    [Fact]
    public async Task Failed_save_keeps_the_list_and_does_not_notify()
    {
        await _service.AddAsync(10);
        var notified = false;
        _service.Changed += (_, _) => notified = true;
        _store.Failure = new IOException("disk full");

        await Assert.ThrowsAsync<IOException>(() => _service.AddAsync(20));

        Assert.False(notified);
        Assert.Equal([10], _service.Goals.Select(g => g.ItemId));
    }

    [Fact]
    public async Task Remove_deletes_only_that_goal()
    {
        await _service.AddAsync(1);
        await _service.AddAsync(2);

        await _service.RemoveAsync(1);

        Assert.Equal([2], _service.Goals.Select(g => g.ItemId));
    }

    [Theory]
    [InlineData(3, -1, new[] { 1, 3, 2 })]
    [InlineData(1, 1, new[] { 2, 1, 3 })]
    [InlineData(1, -1, new[] { 1, 2, 3 })]
    [InlineData(3, 1, new[] { 1, 2, 3 })]
    [InlineData(1, 99, new[] { 2, 3, 1 })]
    [InlineData(99, 1, new[] { 1, 2, 3 })]
    public async Task Move_changes_priority_and_stops_at_the_ends(int itemId, int offset, int[] expected)
    {
        await _service.AddAsync(1);
        await _service.AddAsync(2);
        await _service.AddAsync(3);

        await _service.MoveAsync(itemId, offset);

        Assert.Equal(expected, _service.Goals.Select(g => g.ItemId));
    }

    [Fact]
    public async Task Note_is_trimmed_and_limited()
    {
        await _service.AddAsync(1);

        await _service.SetNoteAsync(1, "  hello  ");
        Assert.Equal("hello", _service.Goals[0].Note);

        await _service.SetNoteAsync(1, new string('x', GoalService.MaxNoteLength + 50));
        Assert.Equal(GoalService.MaxNoteLength, _service.Goals[0].Note.Length);
    }

    [Fact]
    public async Task Components_can_be_ticked_and_unticked_per_goal()
    {
        await _service.AddAsync(1);
        await _service.AddAsync(2);

        await _service.SetCompletedAsync(1, 500, true);
        await _service.SetCompletedAsync(1, 501, true);
        await _service.SetCompletedAsync(1, 500, false);

        Assert.Equal([501], _service.Goals[0].CompletedItemIds.Order());
        Assert.Empty(_service.Goals[1].CompletedItemIds);
    }

    [Fact]
    public async Task Ticking_the_same_component_again_does_not_save_again()
    {
        await _service.AddAsync(1);
        await _service.SetCompletedAsync(1, 500, true);
        var saves = _store.SaveCount;

        await _service.SetCompletedAsync(1, 500, true);

        Assert.Equal(saves, _store.SaveCount);
    }

    [Fact]
    public async Task Load_restores_goals_and_notifies()
    {
        _store.Saved = [new Goal(7) { CompletedItemIds = new HashSet<int> { 3 } }];
        IReadOnlyList<Goal>? seen = null;
        _service.Changed += (_, goals) => seen = goals;

        await _service.LoadAsync();

        Assert.Equal([7], _service.Goals.Select(g => g.ItemId));
        Assert.Same(_service.Goals, seen);
    }

    [Fact]
    public async Task Concurrent_additions_are_all_kept()
    {
        await Task.WhenAll(Enumerable.Range(1, 25).Select(i => _service.AddAsync(i)));

        Assert.Equal(25, _service.Goals.Count);
        Assert.Equal(25, _store.Saved.Count);
    }
}
