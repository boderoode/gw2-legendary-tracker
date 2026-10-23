using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using Gw2LegendaryTracker.App.Services;
using Gw2LegendaryTracker.App.ViewModels;
using Gw2LegendaryTracker.App.Views;
using Gw2LegendaryTracker.Core.Crafting;
using Gw2LegendaryTracker.Core.Crafting.Forge;
using Gw2LegendaryTracker.Core.Export;
using Gw2LegendaryTracker.Core.Goals;
using Gw2LegendaryTracker.Core.Gw2;
using Gw2LegendaryTracker.Core.Inventory;
using Gw2LegendaryTracker.Core.Items;
using Gw2LegendaryTracker.Core.Logging;
using Gw2LegendaryTracker.Core.State;
using Gw2LegendaryTracker.Core.Sync;
using Gw2LegendaryTracker.Core.Trading;
using Gw2LegendaryTracker.Infrastructure.Gw2;
using Gw2LegendaryTracker.Infrastructure.Logging;
using Gw2LegendaryTracker.Infrastructure.Security;
using Gw2LegendaryTracker.Infrastructure.Persistence;

namespace Gw2LegendaryTracker.App;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "WPF Application has a fixed lifetime; resources are released in OnExit.")]
public partial class App : Application
{
    private Mutex? _singleInstance;
    private bool _ownsMutex;
    private AppState? _state;
    private AppViewModel? _viewModel;
    private HttpClient? _http;
    private HttpClient? _forgeHttp;
    private GoalService? _goalService;
    private PlanningService? _planning;
    private HotkeyService? _hotkey;
    private OverlayController? _overlay;
    private FileAppLog? _log;
    private readonly List<DateTimeOffset> _recentFailures = [];

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // A second instance would fight over the global hotkey and the database.
        _singleInstance = new Mutex(initiallyOwned: true, "Gw2LegendaryTracker.SingleInstance", out var isFirstInstance);
        _ownsMutex = isFirstInstance;
        if (!isFirstInstance)
        {
            MessageBox.Show("GW2 Legendary Tracker draait al.", "GW2 Legendary Tracker", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _log = new FileAppLog(DataPath("logs"));
        _log.Info($"Start, versie {AppInfo.Version}, {Environment.OSVersion}");
        RegisterExceptionHandlers();

        try
        {
            await StartAsync();
        }
        catch (Exception ex)
        {
            // Startup failures (e.g. unreadable database) must be visible; there is no window to report them in yet.
            _log.Failure("Starten mislukt", ex);
            MessageBox.Show(
                $"De applicatie kon niet starten:\n\n{ex.Message}\n\nDetails staan in het logbestand in:\n{_log.Directory}",
                "GW2 Legendary Tracker",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _log?.Info("Afgesloten");
        _overlay?.Dispose();
        _hotkey?.Dispose();
        _viewModel?.Dispose();
        _planning?.Dispose();
        _goalService?.Dispose();
        _http?.Dispose();
        _forgeHttp?.Dispose();
        _state?.Dispose();
        if (_ownsMutex)
        {
            _singleInstance?.ReleaseMutex();
        }

        _singleInstance?.Dispose();

        base.OnExit(e);
    }

    /// <summary>
    /// Last line of defence: unexpected errors are logged and reported instead of silently closing the app.
    /// Everything is stored before it is shown, so continuing is safe; after repeated failures the app stops.
    /// </summary>
    private void RegisterExceptionHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            _log?.Failure("Niet-afgehandelde fout in een achtergrondtaak", e.Exception);
            e.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            _log?.Failure("Fatale fout, de app stopt", e.ExceptionObject as Exception);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _log?.Failure("Onverwachte fout in het scherm", e.Exception);
        e.Handled = true;

        var now = DateTimeOffset.UtcNow;
        _recentFailures.RemoveAll(at => now - at > TimeSpan.FromMinutes(1));
        _recentFailures.Add(now);

        var folder = _log?.Directory ?? "het logbestand";
        if (_recentFailures.Count >= 3)
        {
            MessageBox.Show(
                $"Er blijven fouten optreden, dus de app sluit af. Je gegevens zijn bewaard.\n\nDetails staan in het logbestand in:\n{folder}",
                "GW2 Legendary Tracker",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        MessageBox.Show(
            $"Er ging iets onverwachts mis. Je gegevens zijn veilig: alles wordt direct opgeslagen.\n\nDetails staan in het logbestand in:\n{folder}\n(In het hoofdvenster vind je een knop om die map te openen.)",
            "GW2 Legendary Tracker",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private static string DataPath(string fileName) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Gw2LegendaryTracker",
        fileName);

    private async Task StartAsync()
    {
        var database = new SqliteDatabase(DataPath("tracker.db"));
        await database.MigrateAsync();
        _state = await AppState.LoadAsync(new SqliteSettingsStore(database));
        var log = _log ?? throw new InvalidOperationException("The log is created before the app starts.");
        _state.SyncStatusChanged += (_, status) =>
        {
            if (status.Phase == SyncPhase.Failed)
            {
                log.Warning($"Synchronisatie mislukt: {status.Error}");
            }
            else if (status.Phase == SyncPhase.Succeeded)
            {
                log.Info("Synchronisatie gelukt");
            }
        };

        _http = new HttpClient { BaseAddress = new Uri("https://api.guildwars2.com/"), Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Gw2LegendaryTracker/0.1");
        var api = new Gw2ApiClient(_http);
        var keys = new DpapiApiKeyStore(DataPath("apikey.bin"));
        var inventory = new InventoryProvider();
        var sync = new SyncService(api, keys, new SqliteSnapshotStore(database), _state, inventory);
        var catalog = new ItemCatalog(api, new SqliteItemInfoStore(database));

        _goalService = new GoalService(new SqliteGoalStore(database));
        _planning = new PlanningService(_goalService, inventory);
        var recipes = new RecipeCatalog(api, new SqliteRecipeStore(database));
        var goals = new GoalsViewModel(
            _goalService,
            _planning,
            new LegendaryCatalog(api, new SqliteLegendaryStore(database)),
            catalog,
            new CurrencyCatalog(api),
            new PriceCatalog(api, new SqlitePriceStore(database)),
            inventory,
            _state,
            Dispatcher);

        // gw2treasures: the Mystic Forge recipes of Legendaries, which the official API does not have.
        _forgeHttp = new HttpClient { BaseAddress = new Uri("https://api.gw2treasures.com/"), Timeout = TimeSpan.FromSeconds(30) };
        _forgeHttp.DefaultRequestHeaders.UserAgent.ParseAdd("Gw2LegendaryTracker/0.1");
        var forgeClient = new ForgeApiClient(_forgeHttp);
        var forgeKeys = new DpapiApiKeyStore(DataPath("forgekey.bin"));
        var forge = new ForgeRecipeCatalog(forgeClient, forgeKeys, new SqliteForgeRecipeStore(database), api);
        var provider = new RecipeBookProvider(recipes, forge, token => LocalRecipeFiles.LoadAsync(RecipeFolders(), token));
        var recipeData = new RecipeDataViewModel(new ForgeConnectionService(forgeClient, forgeKeys), forge, provider, _planning, goals, log, Dispatcher);
        var transfer = new DataTransferViewModel(new UserDataTransferService(_state, _goalService), log, DataPath("backups"), log is FileAppLog file ? file.Directory : DataPath("logs"));

        _viewModel = new AppViewModel(
            _state,
            new ConnectionService(api, keys),
            sync,
            new InventoryViewModel(inventory, catalog, Dispatcher),
            goals,
            recipeData,
            transfer,
            log,
            Dispatcher);
        _overlay = new OverlayController(_viewModel);

        var mainWindow = new MainWindow { DataContext = _viewModel };
        _viewModel.MainWindowRequested += (_, _) => ShowMainWindow(mainWindow);
        MainWindow = mainWindow;

        _hotkey = new HotkeyService();
        _hotkey.Pressed += (_, _) => _overlay.Toggle();
        _viewModel.HotkeyError = _hotkey.TryRegister(_state.Settings.Overlay.ToggleHotkey);

        mainWindow.Show();
        if (_state.Settings.Overlay.ShowOnStartup)
        {
            _overlay.Show();
        }

        await _viewModel.InitializeAsync();

        // Recipes are public data: use what is cached right away, then refresh in the background.
        _ = LoadRecipesAsync(recipes, recipeData, goals);
    }

    private static string[] RecipeFolders() =>
    [
        Path.Combine(AppContext.BaseDirectory, "data", "recipes"),
        DataPath("recipes"),
    ];

    private async Task LoadRecipesAsync(RecipeCatalog catalog, RecipeDataViewModel recipeData, GoalsViewModel goals)
    {
        try
        {
            // Use what is cached right away, then refresh the official recipes in the background.
            await recipeData.RebuildBookAsync();
            goals.StatusText = "Recepten bijwerken… (de eerste keer duurt dit enkele minuten)";

            // The forge recipes of the selected goal are fetched in parallel; they need the official recipes
            // to know which other components to look at, so this runs again once those are complete.
            var forgeRun = recipeData.RecheckSelectedAsync();
            if (await catalog.RefreshAsync() > 0)
            {
                await recipeData.RebuildBookAsync();
                forgeRun = recipeData.RecheckSelectedAsync();
            }

            await forgeRun;
            goals.StatusText = "Receptdata is actueel.";
            _log?.Info("Officiële recepten bijgewerkt");
        }
        catch (Gw2ApiException ex)
        {
            goals.StatusText = $"Recepten konden niet worden bijgewerkt: {ex.Message} De opgeslagen recepten blijven bruikbaar.";
            _log?.Warning($"Recepten bijwerken mislukt: {ex.Message}");
        }
        catch (Exception ex)
        {
            goals.StatusText = $"Recepten konden niet worden geladen: {ex.Message}";
            _log?.Failure("Recepten laden mislukt", ex);
        }
    }

    private static void ShowMainWindow(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Show();
        window.Activate();
    }
}
