using System.Globalization;
using System.Windows.Input;
using System.Windows.Threading;
using Gw2LegendaryTracker.Core.Acquisition;
using Gw2LegendaryTracker.Core.Crafting;
using Gw2LegendaryTracker.Core.Crafting.Forge;
using Gw2LegendaryTracker.Core.Goals;
using Gw2LegendaryTracker.Core.Logging;

namespace Gw2LegendaryTracker.App.ViewModels;

/// <summary>
/// The gw2treasures key, and getting the recipes of the selected Legendary: when a goal is chosen the app asks
/// for the recipes of that goal and everything below it, remembers them, and rebuilds the plans.
/// </summary>
public sealed class RecipeDataViewModel : ObservableObject, IDisposable
{
    private readonly ForgeConnectionService _connection;
    private readonly ForgeRecipeCatalog _forge;
    private readonly RecipeBookProvider _provider;
    private readonly PlanningService _planning;
    private readonly GoalsViewModel _goals;
    private readonly AcquisitionService _acquisitions;
    private readonly IAppLog _log;
    private readonly Dispatcher _dispatcher;
    private readonly SemaphoreSlim _bookGate = new(1, 1);
    private CancellationTokenSource? _run;
    private int? _lastRequestedId;
    private bool _hasKey;
    private bool _isBusy;
    private string? _connectMessage;
    private bool _connectMessageIsError;
    private string _fetchText = string.Empty;
    private string _notice = string.Empty;

    public RecipeDataViewModel(
        ForgeConnectionService connection,
        ForgeRecipeCatalog forge,
        RecipeBookProvider provider,
        PlanningService planning,
        GoalsViewModel goals,
        AcquisitionService acquisitions,
        IAppLog log,
        Dispatcher dispatcher)
    {
        _connection = connection;
        _forge = forge;
        _provider = provider;
        _planning = planning;
        _goals = goals;
        _acquisitions = acquisitions;
        _log = log;
        _dispatcher = dispatcher;

        _goals.PropertyChanged += OnGoalsPropertyChanged;
        _acquisitions.Changed += OnAcquisitionsChanged;
        RefreshCommand = new RelayCommand(() => _ = EnsureSelectedAsync(force: true));
    }

    public ICommand RefreshCommand { get; }

    public bool HasKey
    {
        get => _hasKey;
        private set
        {
            _hasKey = value;
            RaisePropertyChanged(nameof(HasKey));
            RaisePropertyChanged(nameof(NeedsKey));
        }
    }

    public bool NeedsKey => !_hasKey;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            _isBusy = value;
            RaisePropertyChanged(nameof(IsBusy));
            RaisePropertyChanged(nameof(IsNotBusy));
        }
    }

    public bool IsNotBusy => !_isBusy;

    public string? ConnectMessage
    {
        get => _connectMessage;
        private set
        {
            _connectMessage = value;
            RaisePropertyChanged(nameof(ConnectMessage));
            RaisePropertyChanged(nameof(HasConnectMessage));
        }
    }

    public bool HasConnectMessage => _connectMessage is not null;

    public bool ConnectMessageIsError
    {
        get => _connectMessageIsError;
        private set
        {
            _connectMessageIsError = value;
            RaisePropertyChanged(nameof(ConnectMessageIsError));
        }
    }

    /// <summary>What the recipe download is doing or did.</summary>
    public string FetchText
    {
        get => _fetchText;
        private set
        {
            _fetchText = value;
            RaisePropertyChanged(nameof(FetchText));
            RaisePropertyChanged(nameof(HasFetchText));
        }
    }

    public bool HasFetchText => _fetchText.Length > 0;

    /// <summary>Things the user should know about the recipes in use, e.g. how many are not confirmed by a second source.</summary>
    public string Notice
    {
        get => _notice;
        private set
        {
            _notice = value;
            RaisePropertyChanged(nameof(Notice));
            RaisePropertyChanged(nameof(HasNotice));
        }
    }

    public bool HasNotice => _notice.Length > 0;

    public async Task InitializeAsync() => HasKey = await _connection.HasKeyAsync();

    /// <summary>Builds the recipe book from all sources and hands it to the planner. Serialised, so the newest data always wins.</summary>
    public async Task RebuildBookAsync()
    {
        await _bookGate.WaitAsync();
        try
        {
            var built = await _provider.BuildAsync();
            _planning.SetRecipes(built.Book);

            var parts = new List<string>();
            if (built.LocalErrors.Count > 0)
            {
                parts.Add($"Let op: {built.LocalErrors.Count} probleem(en) in de receptbestanden, bijvoorbeeld: {built.LocalErrors[0]}");
            }

            if (built.SingleSourceCount > 0)
            {
                parts.Add($"{built.SingleSourceCount} recept(en) hebben maar één bron en zijn niet gecontroleerd tegen de officiële gegevens.");
            }

            Notice = string.Join(" ", parts);
        }
        finally
        {
            _bookGate.Release();
        }
    }

    public async Task ConnectAsync(string? key)
    {
        IsBusy = true;
        ConnectMessage = "Key controleren…";
        ConnectMessageIsError = false;
        try
        {
            var result = await _connection.ConnectAsync(key);
            ConnectMessage = result.Message;
            ConnectMessageIsError = !result.IsConnected;
            HasKey = result.IsConnected;
            if (result.IsConnected)
            {
                _lastRequestedId = null;
                _ = EnsureSelectedAsync(force: false);
            }
        }
        catch (Exception ex)
        {
            // e.g. the encrypted key file could not be written.
            ConnectMessage = $"De key kon niet veilig worden opgeslagen: {ex.Message}";
            ConnectMessageIsError = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task DisconnectAsync()
    {
        try
        {
            await _connection.DisconnectAsync();
            HasKey = false;
            ConnectMessage = "De key is verwijderd van deze computer. Al opgehaalde recepten blijven bruikbaar.";
            ConnectMessageIsError = false;
            FetchText = string.Empty;
        }
        catch (Exception ex)
        {
            ConnectMessage = $"De key kon niet worden verwijderd: {ex.Message}";
            ConnectMessageIsError = true;
        }
    }

    /// <summary>Looks at the selected goal again, e.g. after the other recipes changed. Cheap when everything is cached.</summary>
    public Task RecheckSelectedAsync()
    {
        _lastRequestedId = null;
        return EnsureSelectedAsync(force: false);
    }

    /// <summary>Gets the recipes of the selected goal (from the cache when recent). <paramref name="force"/> asks the service again.</summary>
    public async Task EnsureSelectedAsync(bool force = false)
    {
        if (_goals.SelectedGoalId is not { } goalId)
        {
            return;
        }

        if (!force && _lastRequestedId == goalId)
        {
            return;
        }

        _lastRequestedId = goalId;

        if (!_hasKey)
        {
            FetchText = "Voeg hierboven je gw2treasures-key toe om de recepten van deze Legendary op te halen.";
            return;
        }

        // A newer request (another goal, or a forced refresh) replaces a run that is still going.
        _run?.Cancel();
        _run?.Dispose();
        var run = _run = new CancellationTokenSource();

        FetchText = "Recepten ophalen…";
        var progress = new Progress<ForgeProgress>(p =>
            FetchText = p.Queued > 0
                ? string.Create(CultureInfo.CurrentCulture, $"Recepten ophalen… {p.Visited} onderdelen bekeken, nog {p.Queued} te gaan.")
                : string.Create(CultureInfo.CurrentCulture, $"Recepten ophalen… {p.Visited} onderdelen bekeken."));

        try
        {
            var result = await _forge.EnsureAsync(goalId, _planning.Recipes, force, progress: progress, cancellationToken: run.Token);
            await RebuildBookAsync();

            if (run.IsCancellationRequested)
            {
                return;
            }

            FetchText = Describe(result);
            LogResult(goalId, result);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer request; that one reports.
        }
        catch (Exception ex)
        {
            FetchText = $"Recepten ophalen is mislukt: {ex.Message}";
            _log.Failure("Recepten ophalen mislukt", ex);
        }
    }

    public void Dispose()
    {
        _goals.PropertyChanged -= OnGoalsPropertyChanged;
        _acquisitions.Changed -= OnAcquisitionsChanged;
        _run?.Cancel();
        _run?.Dispose();
        _bookGate.Dispose();
    }

    private void LogResult(int goalId, ForgeFetchResult result)
    {
        var summary = $"Recepten voor item {goalId}: {result.Fetched} opgehaald, {result.FromCache} uit cache, {result.RandomSkipped} willekeurige overgeslagen, {result.Rejected.Count} afgewezen";
        if (result.Error is not null)
        {
            _log.Warning($"{summary}; gestopt: {result.Error}");
        }
        else if (result.Truncated)
        {
            _log.Warning($"{summary}; afgebroken bij het maximum aantal aanroepen");
        }
        else
        {
            _log.Info(summary);
        }

        foreach (var rejected in result.Rejected)
        {
            _log.Warning(rejected);
        }
    }

    private static string Describe(ForgeFetchResult result)
    {
        var text = result switch
        {
            { Error: { } error } => $"Recepten ophalen is gestopt: {error} Opgeslagen recepten blijven bruikbaar.",
            { Truncated: true } => "Recepten zijn gedeeltelijk opgehaald, want de crafting tree is erg groot.",
            { Fetched: 0 } => "Recepten zijn actueel.",
            _ => $"Recepten zijn bijgewerkt ({result.Fetched} onderdelen opgehaald).",
        };

        return result.Rejected.Count > 0
            ? $"{text} {result.Rejected.Count} recept(en) niet gebruikt omdat ze niet kloppen met de officiële beschrijving. {result.Rejected[0]}"
            : text;
    }

    /// <summary>A vendor offer was added or removed: the plans must use the new offers straight away.</summary>
    private void OnAcquisitionsChanged(object? sender, IReadOnlyList<AcquisitionOffer> offers) =>
        _ = _dispatcher.InvokeAsync(async () =>
        {
            try
            {
                await RebuildBookAsync();
            }
            catch (Exception ex)
            {
                _log.Failure("Receptboek bijwerken na een wijziging in de herkomstgegevens mislukt", ex);
            }
        });

    private void OnGoalsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GoalsViewModel.SelectedGoalId))
        {
            _ = _dispatcher.InvokeAsync(() => EnsureSelectedAsync(force: false));
        }
    }
}
