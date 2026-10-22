using Gw2LegendaryTracker.Core.Goals;
using Microsoft.Data.Sqlite;

namespace Gw2LegendaryTracker.Infrastructure.Persistence;

public sealed class SqliteGoalStore : IGoalStore
{
    private readonly SqliteDatabase _database;

    public SqliteGoalStore(SqliteDatabase database) => _database = database;

    public async Task<IReadOnlyList<Goal>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);

        var completed = new Dictionary<int, HashSet<int>>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT goal_item_id, item_id FROM goal_completed;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var goalId = reader.GetInt32(0);
                if (!completed.TryGetValue(goalId, out var set))
                {
                    completed[goalId] = set = [];
                }

                set.Add(reader.GetInt32(1));
            }
        }

        var goals = new List<Goal>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT item_id, quantity, note FROM goal ORDER BY position;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var itemId = reader.GetInt32(0);
                goals.Add(new Goal(itemId, reader.GetInt32(1), reader.GetString(2))
                {
                    CompletedItemIds = completed.GetValueOrDefault(itemId) ?? [],
                });
            }
        }

        return goals;
    }

    public async Task SaveAsync(IReadOnlyList<Goal> goals, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(goals);

        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // The list is small and changes rarely; replacing it as a whole inside one transaction keeps it consistent.
        await using (var clear = connection.CreateCommand())
        {
            clear.Transaction = transaction;
            clear.CommandText = "DELETE FROM goal;";
            await clear.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var insertGoal = connection.CreateCommand();
        insertGoal.Transaction = transaction;
        insertGoal.CommandText = "INSERT INTO goal (item_id, position, quantity, note) VALUES ($id, $position, $quantity, $note);";
        var goalId = insertGoal.Parameters.Add("$id", SqliteType.Integer);
        var position = insertGoal.Parameters.Add("$position", SqliteType.Integer);
        var quantity = insertGoal.Parameters.Add("$quantity", SqliteType.Integer);
        var note = insertGoal.Parameters.Add("$note", SqliteType.Text);

        await using var insertDone = connection.CreateCommand();
        insertDone.Transaction = transaction;
        insertDone.CommandText = "INSERT INTO goal_completed (goal_item_id, item_id) VALUES ($goal, $item);";
        var doneGoal = insertDone.Parameters.Add("$goal", SqliteType.Integer);
        var doneItem = insertDone.Parameters.Add("$item", SqliteType.Integer);

        for (var index = 0; index < goals.Count; index++)
        {
            var goal = goals[index];
            goalId.Value = goal.ItemId;
            position.Value = index;
            quantity.Value = goal.Quantity;
            note.Value = goal.Note;
            await insertGoal.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            foreach (var item in goal.CompletedItemIds)
            {
                doneGoal.Value = goal.ItemId;
                doneItem.Value = item;
                await insertDone.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
