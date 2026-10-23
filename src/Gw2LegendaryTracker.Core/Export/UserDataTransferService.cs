using Gw2LegendaryTracker.Core.Acquisition;
using Gw2LegendaryTracker.Core.Goals;
using Gw2LegendaryTracker.Core.State;

namespace Gw2LegendaryTracker.Core.Export;

public sealed record ImportOutcome(bool Succeeded, string Message, IReadOnlyList<string> Warnings);

/// <summary>Exports and imports the user's data against the live state. Import validates before it changes anything.</summary>
public sealed class UserDataTransferService
{
    private readonly AppState _state;
    private readonly GoalService _goals;
    private readonly AcquisitionService? _acquisitions;
    private readonly TimeProvider _time;

    public UserDataTransferService(AppState state, GoalService goals, TimeProvider? time = null, AcquisitionService? acquisitions = null)
    {
        _state = state;
        _goals = goals;
        _acquisitions = acquisitions;
        _time = time ?? TimeProvider.System;
    }

    public string Export() => UserDataSerializer.Serialize(_state.Settings, _goals.Goals, _time.GetUtcNow(), _acquisitions?.Offers);

    /// <summary>
    /// Replaces the goals and settings with the contents of the file. Nothing changes when the file is invalid.
    /// Goals are stored first; if storing the settings then fails, the goals stay imported and the message says so.
    /// </summary>
    public async Task<ImportOutcome> ImportAsync(string json, CancellationToken cancellationToken = default)
    {
        var parsed = UserDataSerializer.Parse(json);
        if (!parsed.Succeeded)
        {
            return new ImportOutcome(false, parsed.Error!, []);
        }

        await _goals.ReplaceAsync(parsed.Goals, cancellationToken).ConfigureAwait(false);
        if (parsed.Acquisitions is not null && _acquisitions is not null)
        {
            await _acquisitions.ReplaceAsync(parsed.Acquisitions, cancellationToken).ConfigureAwait(false);
        }
        if (parsed.Settings is { } settings)
        {
            try
            {
                await _state.UpdateAsync(_ => settings, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or System.Data.Common.DbException)
            {
                return new ImportOutcome(false, $"De doelen zijn geïmporteerd, maar de instellingen konden niet worden opgeslagen: {ex.Message}", parsed.Warnings);
            }
        }

        var count = parsed.Goals.Count;
        var offers = parsed.Acquisitions is { Count: > 0 } && _acquisitions is not null ? $", {parsed.Acquisitions.Count} eigen herkomstgegeven(s)" : string.Empty;
        return new ImportOutcome(true, $"Geïmporteerd: {count} doel(en){offers}{(parsed.Settings is null ? string.Empty : " en je instellingen")}.", parsed.Warnings);
    }
}
