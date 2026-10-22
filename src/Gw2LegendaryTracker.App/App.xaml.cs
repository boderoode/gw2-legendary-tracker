using System.IO;
using System.Net.Http;
using System.Windows;
using Gw2LegendaryTracker.App.Services;
using Gw2LegendaryTracker.App.ViewModels;
using Gw2LegendaryTracker.App.Views;
using Gw2LegendaryTracker.Core.Inventory;
using Gw2LegendaryTracker.Core.Items;
using Gw2LegendaryTracker.Core.State;
using Gw2LegendaryTracker.Core.Sync;
using Gw2LegendaryTracker.Infrastructure.Gw2;
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
    private HotkeyService? _hotkey;
    private OverlayController? _overlay;

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

        try
        {
            await StartAsync();
        }
        catch (Exception ex)
        {
            // Startup failures (e.g. unreadable database) must be visible; there is no window to report them in yet.
            MessageBox.Show($"De applicatie kon niet starten:\n\n{ex.Message}", "GW2 Legendary Tracker", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _overlay?.Dispose();
        _hotkey?.Dispose();
        _viewModel?.Dispose();
        _http?.Dispose();
        _state?.Dispose();
        if (_ownsMutex)
        {
            _singleInstance?.ReleaseMutex();
        }

        _singleInstance?.Dispose();

        base.OnExit(e);
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

        _http = new HttpClient { BaseAddress = new Uri("https://api.guildwars2.com/"), Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Gw2LegendaryTracker/0.1");
        var api = new Gw2ApiClient(_http);
        var keys = new DpapiApiKeyStore(DataPath("apikey.bin"));
        var inventory = new InventoryProvider();
        var sync = new SyncService(api, keys, new SqliteSnapshotStore(database), _state, inventory);
        var catalog = new ItemCatalog(api, new SqliteItemInfoStore(database));

        _viewModel = new AppViewModel(
            _state,
            new ConnectionService(api, keys),
            sync,
            new InventoryViewModel(inventory, catalog, Dispatcher),
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
