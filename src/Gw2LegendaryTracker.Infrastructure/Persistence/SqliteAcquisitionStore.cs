using Gw2LegendaryTracker.Core.Acquisition;
using Gw2LegendaryTracker.Core.Crafting;
using Microsoft.Data.Sqlite;

namespace Gw2LegendaryTracker.Infrastructure.Persistence;

public sealed class SqliteAcquisitionStore : IAcquisitionStore
{
    private readonly SqliteDatabase _database;

    public SqliteAcquisitionStore(SqliteDatabase database) => _database = database;

    public async Task<IReadOnlyList<AcquisitionOffer>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);

        var costs = new Dictionary<int, List<Ingredient>>();
        await using (var command = connection.CreateCommand())
        {
            // Insertion order inside an offer is kept, so the texts always list the costs in the order they were entered.
            command.CommandText = "SELECT acquisition_id, kind, ref_id, amount FROM acquisition_cost ORDER BY rowid;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var offerId = reader.GetInt32(0);
                if (!costs.TryGetValue(offerId, out var list))
                {
                    costs[offerId] = list = [];
                }

                list.Add(new Ingredient(new MaterialKey((MaterialKind)reader.GetInt32(1), reader.GetInt32(2)), reader.GetInt64(3)));
            }
        }

        var offers = new List<AcquisitionOffer>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id, item_id, output_count, vendor_name, location FROM acquisition ORDER BY id;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var id = reader.GetInt32(0);
                offers.Add(new AcquisitionOffer(id, reader.GetInt32(1), reader.GetInt32(2), costs.GetValueOrDefault(id) ?? [], reader.GetString(3), reader.GetString(4)));
            }
        }

        return offers;
    }

    public async Task SaveAsync(IReadOnlyList<AcquisitionOffer> offers, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(offers);

        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // The list is small; replacing it as a whole inside one transaction keeps it consistent (the cascade removes old costs).
        await using (var clear = connection.CreateCommand())
        {
            clear.Transaction = transaction;
            clear.CommandText = "DELETE FROM acquisition;";
            await clear.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var insertOffer = connection.CreateCommand();
        insertOffer.Transaction = transaction;
        insertOffer.CommandText = "INSERT INTO acquisition (id, item_id, output_count, vendor_name, location) VALUES ($id, $item, $count, $vendor, $location);";
        var id = insertOffer.Parameters.Add("$id", SqliteType.Integer);
        var item = insertOffer.Parameters.Add("$item", SqliteType.Integer);
        var count = insertOffer.Parameters.Add("$count", SqliteType.Integer);
        var vendor = insertOffer.Parameters.Add("$vendor", SqliteType.Text);
        var location = insertOffer.Parameters.Add("$location", SqliteType.Text);

        await using var insertCost = connection.CreateCommand();
        insertCost.Transaction = transaction;
        insertCost.CommandText = "INSERT INTO acquisition_cost (acquisition_id, kind, ref_id, amount) VALUES ($offer, $kind, $ref, $amount);";
        var costOffer = insertCost.Parameters.Add("$offer", SqliteType.Integer);
        var kind = insertCost.Parameters.Add("$kind", SqliteType.Integer);
        var reference = insertCost.Parameters.Add("$ref", SqliteType.Integer);
        var amount = insertCost.Parameters.Add("$amount", SqliteType.Integer);

        foreach (var offer in offers)
        {
            id.Value = offer.Id;
            item.Value = offer.ItemId;
            count.Value = offer.OutputCount;
            vendor.Value = offer.VendorName;
            location.Value = offer.Location;
            await insertOffer.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            foreach (var cost in offer.Costs)
            {
                costOffer.Value = offer.Id;
                kind.Value = (int)cost.Key.Kind;
                reference.Value = cost.Key.Id;
                amount.Value = cost.Count;
                await insertCost.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
