using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using System.Windows.Threading;
using Gw2LegendaryTracker.Core.Export;
using Gw2LegendaryTracker.Core.Gw2;
using Gw2LegendaryTracker.Core.Logging;
using Gw2LegendaryTracker.Core.Masteries;
using Gw2LegendaryTracker.Infrastructure.Persistence;

namespace Gw2LegendaryTracker.App.ViewModels;

/// <summary>Text the view should put on the clipboard, and what to tell the user when that worked.</summary>
public sealed record ClipboardRequest(string Text, string SuccessMessage);

public sealed record MasteryLinkRow(string Label, string Url);

/// <summary>One step of the plan as the Masteries tab shows it. The check box changes the manual choice.</summary>
public sealed class MasteryStepRow : ObservableObject
{
    private readonly Action<MasteryStepRow, bool> _setDone;
    private bool _isDone;

    public MasteryStepRow(MasteryStepView view, bool hasManualChoice, Action<MasteryStepRow, bool> setDone)
    {
        _setDone = setDone;
        var step = view.Step;
        Id = step.Id;
        Title = step.Title;
        Description = step.Description;
        WhereText = view.WhereText;
        TimeText = view.TimeText;
        Waypoint = step.Waypoint;
        Notes = step.Notes;
        Links = [.. step.Links.Select(l => new MasteryLinkRow(l.Label, l.Url))];
        IsHighPriority = step.Priority == MasteryPriority.High;
        Apply(view, hasManualChoice);
    }

    /// <summary>
    /// Takes over a new status of the same step. The row object stays, so the check box keeps keyboard focus and the list
    /// keeps its scroll position while you tick off steps one after the other.
    /// </summary>
    public void Update(MasteryStepView view, bool hasManualChoice)
    {
        Apply(view, hasManualChoice);
        RaiseAllPropertiesChanged();
    }

    public string Id { get; }

    public string Title { get; }

    public string Description { get; }

    public bool HasDescription => Description.Length > 0;

    public string? WhereText { get; }

    public string? TimeText { get; }

    /// <summary>Place and time on one line, e.g. "Jade · Seitung Province · 30 min".</summary>
    public string? MetaText => string.Join(" · ", new[] { WhereText, TimeText }.Where(t => !string.IsNullOrEmpty(t))) is { Length: > 0 } text ? text : null;

    public bool HasMeta => MetaText is not null;

    public string? Waypoint { get; }

    public bool HasWaypoint => Waypoint is not null;

    public string? Notes { get; }

    public bool HasNotes => Notes is not null;

    public IReadOnlyList<MasteryLinkRow> Links { get; }

    public bool HasLinks => Links.Count > 0;

    public MasteryStepState State { get; private set; }

    public string StatusText { get; private set; } = string.Empty;

    public string? WaitingText { get; private set; }

    public bool HasWaitingText => WaitingText is not null;

    public bool IsTracked { get; private set; }

    public bool AutoDone { get; private set; }

    public bool IsOverridden { get; private set; }

    /// <summary>A manual choice overrules the account data; offer to go back to automatic.</summary>
    public bool CanReset { get; private set; }

    public bool IsHighPriority { get; }

    public string CheckBoxName => $"Klaar: {Title}";

    public bool IsDone
    {
        get => _isDone;
        set
        {
            if (_isDone == value)
            {
                return;
            }

            _isDone = value;
            RaisePropertyChanged(nameof(IsDone));
            _setDone(this, value);
        }
    }

    private void Apply(MasteryStepView view, bool hasManualChoice)
    {
        State = view.Status.State;
        StatusText = view.StatusText;
        WaitingText = view.WaitingText;
        IsTracked = view.Status.IsTracked;
        AutoDone = view.Status.AutoDone;
        IsOverridden = view.Status.IsOverridden;
        CanReset = view.Status.IsTracked && hasManualChoice;
        _isDone = view.Status.IsDone;
    }
}

public sealed class MasteryPhaseRow : ObservableObject
{
    private readonly Action<string, bool> _rememberExpanded;
    private bool _isExpanded;

    public MasteryPhaseRow(MasteryPhaseView view, IReadOnlyList<MasteryStepRow> steps, bool isExpanded, Action<string, bool> rememberExpanded)
    {
        Id = view.Phase.Id;
        Title = view.Phase.Title;
        Goal = view.Phase.Goal;
        Progress = view.Progress;
        ProgressText = view.ProgressText;
        Steps = steps;
        _isExpanded = isExpanded;
        _rememberExpanded = rememberExpanded;
    }

    public string Id { get; }

    public string Title { get; }

    public string Goal { get; }

    public bool HasGoal => Goal.Length > 0;

    public double Progress { get; private set; }

    public string ProgressText { get; private set; }

    public IReadOnlyList<MasteryStepRow> Steps { get; private set; }

    public string AccessibleName => $"Fase {Title}, {ProgressText}";

    /// <summary>Takes over new progress; the step list is only replaced when other steps are visible, so focus stays put.</summary>
    public void Update(MasteryPhaseView view, IReadOnlyList<MasteryStepRow> steps)
    {
        Progress = view.Progress;
        ProgressText = view.ProgressText;
        if (!Steps.SequenceEqual(steps))
        {
            Steps = steps;
        }

        RaisePropertyChanged(nameof(Progress));
        RaisePropertyChanged(nameof(ProgressText));
        RaisePropertyChanged(nameof(AccessibleName));
        RaisePropertyChanged(nameof(Steps));
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                RaisePropertyChanged(nameof(IsExpanded));
                _rememberExpanded(Id, value);
            }
        }
    }
}

public sealed record MasteryTrackRow(string Name, string Text, double Progress);

public sealed record MasteryRegionRow(string Region, string PointsText, IReadOnlyList<MasteryTrackRow> Tracks);

/// <summary>The Masteries tab: the imported plan as steps, the progress per region, and import/export of the plan.</summary>
public sealed class MasteriesViewModel : ObservableObject, IDisposable
{
    public const string AllRegions = "Alle regio's";

    private readonly MasteryPlanService _plans;
    private readonly MasteryCatalog _catalog;
    private readonly MasteryProgressService _progress;
    private readonly UserDataTransferService _transfer;
    private readonly IAppLog _log;
    private readonly string _backupFolder;
    private readonly Dispatcher _dispatcher;
    private readonly TimeProvider _time;
    private readonly Dictionary<string, bool> _expanded = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MasteryStepRow> _stepRows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MasteryPhaseRow> _phaseRows = new(StringComparer.Ordinal);
    private MasteryPlan? _rowsPlan;
    private Dictionary<string, string> _regionCodes = new(StringComparer.Ordinal);

    private MasteryPlanSummary? _summary;
    private IReadOnlyList<MasteryPhaseRow> _phases = [];
    private IReadOnlyList<MasteryRegionRow> _regions = [];
    private IReadOnlyList<string> _regionOptions = [AllRegions];
    private MasteryStepRow? _nextStep;
    private bool _onlyOpen;
    private string _regionFilter = AllRegions;
    private string? _message;
    private bool _isError;
    private string _warnings = string.Empty;

    public MasteriesViewModel(
        MasteryPlanService plans,
        MasteryCatalog catalog,
        MasteryProgressService progress,
        UserDataTransferService transfer,
        IAppLog log,
        string backupFolder,
        Dispatcher dispatcher,
        TimeProvider? time = null)
    {
        _plans = plans;
        _catalog = catalog;
        _progress = progress;
        _transfer = transfer;
        _log = log;
        _backupFolder = backupFolder;
        _dispatcher = dispatcher;
        _time = time ?? TimeProvider.System;

        _plans.Changed += OnDataChanged;
        _catalog.Changed += OnDataChanged;
        _progress.Changed += OnDataChanged;
        _plans.PlanReplaced += OnPlanReplaced;

        CopyPromptCommand = new RelayCommand(CopyPrompt);
        CopyWaypointCommand = new RelayCommand<string>(waypoint =>
            ClipboardRequested?.Invoke(this, new ClipboardRequest(waypoint, $"Waypoint {waypoint} gekopieerd. Plak hem in de chat van het spel.")));
        OpenLinkCommand = new RelayCommand<string>(Services.LinkOpener.Open);
        ResetCommand = new RelayCommand<MasteryStepRow>(row => Run(() => _plans.SetOverrideAsync(row.Id, null)));
        MarkDoneCommand = new RelayCommand<MasteryStepRow>(row => row.IsDone = true);
        DismissMessageCommand = new RelayCommand(() =>
        {
            Message = null;
            Warnings = string.Empty;
        });
    }

    public ICommand DismissMessageCommand { get; }

    /// <summary>The view puts the text on the clipboard (only the view may touch it).</summary>
    public event EventHandler<ClipboardRequest>? ClipboardRequested;

    /// <summary>
    /// A new plan (imported here or restored with the user data) may use achievements whose progress is not known yet;
    /// fetching the mastery data again finds them.
    /// </summary>
    public event EventHandler? SyncRequested;

    public ICommand CopyPromptCommand { get; }

    public ICommand CopyWaypointCommand { get; }

    public ICommand OpenLinkCommand { get; }

    public ICommand ResetCommand { get; }

    public ICommand MarkDoneCommand { get; }

    public bool HasPlan => _plans.Plan is not null;

    public bool HasNoPlan => !HasPlan;

    public string PlanTitle => _plans.Plan?.Title ?? string.Empty;

    public string PlanSummary => _plans.Plan?.Summary ?? string.Empty;

    public bool HasPlanSummary => PlanSummary.Length > 0;

    public string AssumptionsText => _plans.Plan is { Assumptions.Count: > 0 } plan
        ? "Aannames: " + string.Join(" · ", plan.Assumptions)
        : string.Empty;

    public bool HasAssumptions => AssumptionsText.Length > 0;

    public double Progress => _summary?.Progress ?? 0;

    public string ProgressText => _summary?.ProgressText ?? string.Empty;

    public string RemainingText => _summary?.RemainingText ?? string.Empty;

    public MasteryStepRow? NextStep
    {
        get => _nextStep;
        private set
        {
            _nextStep = value;
            RaisePropertyChanged(nameof(NextStep));
            RaisePropertyChanged(nameof(HasNextStep));
            RaisePropertyChanged(nameof(NextStepEmptyText));
        }
    }

    public bool HasNextStep => _nextStep is not null;

    public string NextStepEmptyText => _summary is { TotalCount: > 0 } summary && summary.DoneCount == summary.TotalCount
        ? "Alle stappen zijn klaar. Vraag de AI om een vervolgplan."
        : "Er is nu geen stap beschikbaar: alle open stappen wachten op een andere stap.";

    public IReadOnlyList<MasteryPhaseRow> Phases
    {
        get => _phases;
        private set
        {
            _phases = value;
            RaisePropertyChanged(nameof(Phases));
            RaisePropertyChanged(nameof(HasNoVisibleSteps));
        }
    }

    public bool HasNoVisibleSteps => HasPlan && _phases.Count == 0;

    public IReadOnlyList<MasteryRegionRow> Regions
    {
        get => _regions;
        private set
        {
            _regions = value;
            RaisePropertyChanged(nameof(Regions));
            RaisePropertyChanged(nameof(HasRegions));
        }
    }

    public bool HasRegions => _regions.Count > 0;

    public IReadOnlyList<string> RegionOptions
    {
        get => _regionOptions;
        private set
        {
            _regionOptions = value;
            RaisePropertyChanged(nameof(RegionOptions));
        }
    }

    public bool OnlyOpen
    {
        get => _onlyOpen;
        set
        {
            if (_onlyOpen != value)
            {
                _onlyOpen = value;
                RaisePropertyChanged(nameof(OnlyOpen));
                Rebuild();
            }
        }
    }

    public string RegionFilter
    {
        get => _regionFilter;
        set
        {
            var next = value ?? AllRegions;
            if (_regionFilter != next)
            {
                _regionFilter = next;
                RaisePropertyChanged(nameof(RegionFilter));
                Rebuild();
            }
        }
    }

    /// <summary>The key lacks "progression": explain it, the plan still works with manual ticks.</summary>
    public bool NeedsProgressionPermission => _progress.Status.State == MasterySyncState.NoPermission;

    public string ProgressionReason { get; } = ApiPermissions.Optional.First(p => p.Name == ApiPermissions.Progression).Reason;

    public string SyncText
    {
        get
        {
            var status = _progress.Status;
            var last = status.LastSuccessUtc is { } at
                ? at.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
                : null;
            return status.State switch
            {
                MasterySyncState.Succeeded => $"Mastery-voortgang bijgewerkt op {last}.",
                MasterySyncState.Failed when last is not null => $"Mastery-voortgang ophalen is mislukt: {status.Error} Je ziet de gegevens van {last}.",
                MasterySyncState.Failed => $"Mastery-voortgang ophalen is mislukt: {status.Error}",
                MasterySyncState.NoPermission => "Je API-key mag je mastery-voortgang niet lezen; je vinkt stappen zelf af.",
                _ => "Je mastery-voortgang is nog niet opgehaald. Koppel je account en synchroniseer, of vink stappen zelf af.",
            };
        }
    }

    public bool SyncFailed => _progress.Status.State == MasterySyncState.Failed;

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

    /// <summary>Loads what is stored, then refreshes the public mastery tracks in the background when they are old.</summary>
    public async Task InitializeAsync()
    {
        try
        {
            await _catalog.LoadAsync();
            await _progress.LoadAsync();
            await _plans.LoadAsync();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or DbException)
        {
            Report(true, $"Je masteryplan kon niet worden geladen: {ex.Message}");
            _log.Failure("Masteryplan laden mislukt", ex);
        }

        Rebuild();
        _ = RefreshCatalogAsync();
    }

    public void Report(bool isError, string message)
    {
        IsError = isError;
        Message = message;
        if (!isError)
        {
            Warnings = string.Empty;
        }
    }

    /// <summary>Checks a plan (from a file or the clipboard) without changing anything. A refused plan is reported here.</summary>
    public MasteryImportPreview PreviewImport(string? text)
    {
        var preview = _plans.Preview(text);
        if (!preview.Succeeded)
        {
            Warnings = string.Empty;
            Report(true, $"Het plan kan niet worden geïmporteerd. {preview.Result.Error}");
            _log.Warning($"Masteryplan geweigerd: {preview.Result.Error}");
        }

        return preview;
    }

    /// <summary>Replaces the active plan after a back-up of all user data. Without a back-up nothing is replaced.</summary>
    public async Task ApplyImportAsync(MasteryImportPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        if (!preview.Succeeded)
        {
            return;
        }

        string backup;
        try
        {
            backup = await UserDataFiles.BackupAsync(_backupFolder, _transfer.Export(), _time.GetUtcNow());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Report(true, $"De back-up van je huidige gegevens kon niet worden gemaakt, dus het plan is niet geïmporteerd: {ex.Message}");
            _log.Failure("Back-up voor masteryplan mislukt", ex);
            return;
        }

        try
        {
            await _plans.ReplaceAsync(preview.Result.Plan!);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or DbException)
        {
            Report(true, $"Het plan kon niet worden opgeslagen: {ex.Message}");
            _log.Failure("Masteryplan opslaan mislukt", ex);
            return;
        }

        Report(false, $"Plan \"{preview.Result.Plan!.Title}\" geïmporteerd. Een back-up van je vorige gegevens staat in {Path.GetDirectoryName(backup)}.");
        Warnings = FormatWarnings(preview.Result.Warnings);
        _log.Info($"Masteryplan geïmporteerd ({preview.StepCount} stappen, {preview.Result.Warnings.Count} waarschuwing(en))");
    }

    public async Task ExportAsync(string path)
    {
        var json = _plans.Export();
        if (json is null)
        {
            Report(true, "Er is nog geen plan om te exporteren.");
            return;
        }

        try
        {
            await UserDataFiles.WriteAtomicAsync(path, json);
            Report(false, $"Plan geëxporteerd naar {path}. Je kunt het bestand aan een AI geven om het plan bij te stellen.");
            _log.Info("Masteryplan geëxporteerd");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Report(true, $"Exporteren is mislukt: {ex.Message}");
            _log.Failure("Masteryplan exporteren mislukt", ex);
        }
    }

    public void Dispose()
    {
        _plans.Changed -= OnDataChanged;
        _catalog.Changed -= OnDataChanged;
        _progress.Changed -= OnDataChanged;
        _plans.PlanReplaced -= OnPlanReplaced;
    }

    internal static string FormatWarnings(IReadOnlyList<string> warnings) =>
        string.Join(Environment.NewLine, warnings.Take(8).Select(w => "• " + w))
        + (warnings.Count > 8 ? string.Create(CultureInfo.CurrentCulture, $"{Environment.NewLine}• … en nog {warnings.Count - 8} melding(en).") : string.Empty);

    private void CopyPrompt()
    {
        var prompt = MasteryPromptBuilder.Build(_progress.Progress, _catalog.Definitions, _plans.Plan);
        ClipboardRequested?.Invoke(this, new ClipboardRequest(
            prompt,
            "De AI-prompt staat op het klembord. Plak hem in een AI-chat, vul eventueel je wensen aan, en importeer het antwoord met \"Plan plakken uit klembord\"."));
    }

    private async Task RefreshCatalogAsync()
    {
        try
        {
            await _catalog.RefreshIfStaleAsync();
        }
        catch (Exception ex) when (ex is Gw2ApiException or IOException or InvalidOperationException or DbException)
        {
            // Offline: plans can still be imported; their mastery ids are then checked later.
            _log.Warning($"Mastery-gegevens ophalen mislukt: {ex.Message}");
        }
    }

    private void OnDataChanged(object? sender, EventArgs e) => _dispatcher.InvokeAsync(Rebuild);

    private void OnPlanReplaced(object? sender, EventArgs e) => SyncRequested?.Invoke(this, EventArgs.Empty);

    private void Rebuild()
    {
        var plan = _plans.Plan;
        var definitions = _catalog.Definitions;
        var progress = _progress.Progress;
        Regions = [.. MasteryPlanPresenter.Regions(progress, definitions).Select(r => new MasteryRegionRow(
            r.DisplayName,
            r.PointsText,
            [.. r.Tracks.Select(t => new MasteryTrackRow(t.Name, t.Text, t.Max == 0 ? 0 : (double)t.Trained / t.Max))]))];

        // Rows are kept per plan and updated in place; only another plan starts with fresh rows.
        if (!ReferenceEquals(plan, _rowsPlan))
        {
            _stepRows.Clear();
            _phaseRows.Clear();
            _rowsPlan = plan;
        }

        if (plan is null)
        {
            _summary = null;
            Phases = [];
            NextStep = null;
            RegionOptions = [AllRegions];
            _regionFilter = AllRegions;
        }
        else
        {
            var overrides = _plans.Overrides;
            var statuses = MasteryStatusCalculator.Compute(plan, progress, definitions, overrides);
            _summary = MasteryPlanPresenter.Summarize(plan, statuses);
            // The filter shows what people call a region; the plan and the presenter use the region code.
            _regionCodes = _summary.Regions.GroupBy(MasteryRegions.DisplayName, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            if (!_regionOptions.SequenceEqual([AllRegions, .. _regionCodes.Keys]))
            {
                RegionOptions = [AllRegions, .. _regionCodes.Keys];
            }

            if (!_regionOptions.Contains(_regionFilter))
            {
                _regionFilter = AllRegions;
            }

            var filtered = MasteryPlanPresenter.Filter(_summary.Phases, _onlyOpen, _regionCodes.GetValueOrDefault(_regionFilter));
            var phases = filtered.Select(p => PhaseRow(p, [.. p.Steps.Select(s => Row(s, overrides))])).ToList();
            if (!_phases.SequenceEqual(phases))
            {
                Phases = phases;
            }

            NextStep = _summary.NextStep is { } next ? Row(next, overrides) : null;
        }

        RaiseAllPropertiesChanged();
    }

    private MasteryPhaseRow PhaseRow(MasteryPhaseView view, IReadOnlyList<MasteryStepRow> steps)
    {
        if (_phaseRows.TryGetValue(view.Phase.Id, out var row))
        {
            row.Update(view, steps);
            return row;
        }

        row = new MasteryPhaseRow(
            view,
            steps,
            _expanded.TryGetValue(view.Phase.Id, out var open) ? open : view.DoneCount < view.TotalCount,
            (id, isOpen) => _expanded[id] = isOpen);
        _phaseRows[view.Phase.Id] = row;
        return row;
    }

    private MasteryStepRow Row(MasteryStepView view, IReadOnlyDictionary<string, bool> overrides)
    {
        var hasChoice = overrides.ContainsKey(view.Step.Id);
        if (_stepRows.TryGetValue(view.Step.Id, out var row))
        {
            row.Update(view, hasChoice);
            return row;
        }

        row = new MasteryStepRow(view, hasChoice, SetDone);
        _stepRows[view.Step.Id] = row;
        return row;
    }

    /// <summary>
    /// Ticking a step that the account data already agrees with removes the manual choice, so the step follows the account
    /// again; anything else is stored as a manual choice that overrules it.
    /// </summary>
    private void SetDone(MasteryStepRow row, bool done)
    {
        bool? choice = row.IsTracked
            ? (done == row.AutoDone ? null : done)
            : (done ? true : null);
        Run(() => _plans.SetOverrideAsync(row.Id, choice));
    }

    private void Run(Func<Task> operation) => _ = RunAsync(operation);

    private async Task RunAsync(Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or DbException)
        {
            await _dispatcher.InvokeAsync(() =>
            {
                Report(true, $"Dat kon niet worden opgeslagen: {ex.Message}");
                Rebuild();
            });
        }
    }
}
