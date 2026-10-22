using System.Globalization;
using System.IO;
using Gw2LegendaryTracker.Core.Export;
using Gw2LegendaryTracker.Core.Logging;
using Gw2LegendaryTracker.Infrastructure.Persistence;

namespace Gw2LegendaryTracker.App.ViewModels;

/// <summary>Export and import of the user's data, with an automatic back-up before anything is replaced.</summary>
public sealed class DataTransferViewModel : ObservableObject
{
    private readonly UserDataTransferService _transfer;
    private readonly IAppLog _log;
    private readonly TimeProvider _time;
    private string? _message;
    private bool _isError;
    private string _warnings = string.Empty;

    public DataTransferViewModel(UserDataTransferService transfer, IAppLog log, string backupFolder, string logFolder, TimeProvider? time = null)
    {
        _transfer = transfer;
        _log = log;
        BackupFolder = backupFolder;
        LogFolder = logFolder;
        _time = time ?? TimeProvider.System;
    }

    public string BackupFolder { get; }

    public string LogFolder { get; }

    public string? Message
    {
        get => _message;
        private set
        {
            _message = value;
            RaisePropertyChanged(nameof(Message));
            RaisePropertyChanged(nameof(HasMessage));
        }
    }

    public bool HasMessage => _message is not null;

    public bool IsError
    {
        get => _isError;
        private set
        {
            _isError = value;
            RaisePropertyChanged(nameof(IsError));
        }
    }

    /// <summary>Things left out or corrected during the last import.</summary>
    public string Warnings
    {
        get => _warnings;
        private set
        {
            _warnings = value;
            RaisePropertyChanged(nameof(Warnings));
            RaisePropertyChanged(nameof(HasWarnings));
        }
    }

    public bool HasWarnings => _warnings.Length > 0;

    public async Task ExportAsync(string path)
    {
        Warnings = string.Empty;
        try
        {
            await UserDataFiles.WriteAtomicAsync(path, _transfer.Export());
            Report(false, $"Geëxporteerd naar {path}");
            _log.Info("Export gelukt");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Report(true, $"Exporteren is mislukt: {ex.Message}");
            _log.Failure("Export mislukt", ex);
        }
    }

    public async Task ImportAsync(string path)
    {
        Warnings = string.Empty;

        var read = await UserDataFiles.ReadAsync(path);
        if (read.Text is null)
        {
            Report(true, read.Error ?? "Het bestand kon niet worden gelezen.");
            _log.Warning($"Import geweigerd: {read.Error}");
            return;
        }

        // Validate first: a file that cannot be imported must not even cost a back-up.
        var parsed = UserDataSerializer.Parse(read.Text);
        if (!parsed.Succeeded)
        {
            Report(true, $"Importeren is niet gelukt. {parsed.Error}");
            _log.Warning($"Import geweigerd: {parsed.Error}");
            return;
        }

        string backup;
        try
        {
            backup = await UserDataFiles.BackupAsync(BackupFolder, _transfer.Export(), _time.GetUtcNow());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Without a way back, nothing is replaced.
            Report(true, $"De back-up van je huidige gegevens kon niet worden gemaakt, dus er is niets geïmporteerd: {ex.Message}");
            _log.Failure("Back-up voor import mislukt", ex);
            return;
        }

        var outcome = await _transfer.ImportAsync(read.Text);
        Warnings = string.Join(Environment.NewLine, outcome.Warnings.Take(5).Select(w => "• " + w))
            + (outcome.Warnings.Count > 5 ? string.Create(CultureInfo.CurrentCulture, $"{Environment.NewLine}• … en nog {outcome.Warnings.Count - 5} melding(en).") : string.Empty);

        if (outcome.Succeeded)
        {
            Report(false, $"{outcome.Message} Een back-up van je vorige gegevens staat in {Path.GetDirectoryName(backup)}.");
            _log.Info($"Import gelukt ({outcome.Warnings.Count} waarschuwing(en))");
        }
        else
        {
            Report(true, outcome.Message);
            _log.Failure($"Import niet volledig: {outcome.Message}");
        }
    }

    private void Report(bool isError, string message)
    {
        IsError = isError;
        Message = message;
    }
}
