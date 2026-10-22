using System.Globalization;
using System.Windows.Input;
using System.Windows.Threading;
using Gw2LegendaryTracker.Core.Settings;
using Gw2LegendaryTracker.Core.State;
using Gw2LegendaryTracker.Core.Sync;

namespace Gw2LegendaryTracker.App.ViewModels;

/// <summary>
/// View model shared by the main window and the overlay. Both windows bind to the same instance,
/// so a change made in one is visible in the other without extra plumbing.
/// </summary>
public sealed class AppViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan OpacityPersistDelay = TimeSpan.FromMilliseconds(300);

    private readonly AppState _state;
    private readonly ConnectionService _connection;
    private readonly SyncService _sync;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _opacityTimer;
    private double? _pendingOpacity;
    private bool _isOverlayVisible;
    private string? _hotkeyError;
    private string? _errorMessage;
    private bool _hasApiKey;
    private bool _isBusy;
    private string? _connectMessage;
    private bool _connectMessageIsError;

    public AppViewModel(
        AppState state,
        ConnectionService connection,
        SyncService sync,
        InventoryViewModel inventory,
        GoalsViewModel goals,
        Dispatcher dispatcher)
    {
        Inventory = inventory;
        Goals = goals;
        _state = state;
        _connection = connection;
        _sync = sync;
        _dispatcher = dispatcher;
        _opacityTimer = new DispatcherTimer(OpacityPersistDelay, DispatcherPriority.Background, OnOpacityTimerTick, dispatcher)
        {
            IsEnabled = false,
        };

        _state.SettingsChanged += OnSettingsChanged;
        _state.SyncStatusChanged += OnSyncStatusChanged;

        ToggleOverlayCommand = new RelayCommand(() => OverlayToggleRequested?.Invoke(this, EventArgs.Empty));
        ShowMainWindowCommand = new RelayCommand(() => MainWindowRequested?.Invoke(this, EventArgs.Empty));
        ToggleExpandedCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
        HideOverlayCommand = new RelayCommand(() => OverlayHideRequested?.Invoke(this, EventArgs.Empty));
        SyncNowCommand = new RelayCommand(() => _ = SyncAsync());
    }

    public event EventHandler? OverlayToggleRequested;

    public event EventHandler? OverlayHideRequested;

    public event EventHandler? MainWindowRequested;

    public ICommand ToggleOverlayCommand { get; }

    public ICommand ShowMainWindowCommand { get; }

    public ICommand ToggleExpandedCommand { get; }

    public ICommand HideOverlayCommand { get; }

    public ICommand SyncNowCommand { get; }

    public InventoryViewModel Inventory { get; }

    public GoalsViewModel Goals { get; }

    public OverlaySettings Overlay => _state.Settings.Overlay;

    public double Opacity
    {
        get => _pendingOpacity ?? Overlay.Opacity;
        set
        {
            _pendingOpacity = Math.Clamp(value, OverlaySettings.MinOpacity, OverlaySettings.MaxOpacity);
            RaisePropertyChanged(nameof(Opacity));
            _opacityTimer.Stop();
            _opacityTimer.Start();
        }
    }

    public bool IsExpanded
    {
        get => Overlay.IsExpanded;
        set => Persist(s => s with { Overlay = s.Overlay with { IsExpanded = value } });
    }

    public bool IsLocked
    {
        get => Overlay.IsLocked;
        set => Persist(s => s with { Overlay = s.Overlay with { IsLocked = value } });
    }

    public bool IsClickThrough
    {
        get => Overlay.IsClickThrough;
        set => Persist(s => s with { Overlay = s.Overlay with { IsClickThrough = value } });
    }

    public bool ShowOverlayOnStartup
    {
        get => Overlay.ShowOnStartup;
        set => Persist(s => s with { Overlay = s.Overlay with { ShowOnStartup = value } });
    }

    public bool AutoHideEnabled
    {
        get => Overlay.AutoHideSeconds is not null;
        set => Persist(s => s with { Overlay = s.Overlay with { AutoHideSeconds = value ? 10 : null } });
    }

    public bool IsOverlayVisible
    {
        get => _isOverlayVisible;
        set
        {
            if (_isOverlayVisible != value)
            {
                _isOverlayVisible = value;
                RaisePropertyChanged(nameof(IsOverlayVisible));
            }
        }
    }

    public string HotkeyText => FormatHotkey(Overlay.ToggleHotkey);

    public string? HotkeyError
    {
        get => _hotkeyError;
        set
        {
            _hotkeyError = value;
            RaisePropertyChanged(nameof(HotkeyError));
            RaisePropertyChanged(nameof(HasHotkeyError));
        }
    }

    public bool HasHotkeyError => _hotkeyError is not null;

    /// <summary>Last failure to persist a setting; shown so a failed save is never silent.</summary>
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            _errorMessage = value;
            RaisePropertyChanged(nameof(ErrorMessage));
            RaisePropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => _errorMessage is not null;

    public SyncPhase SyncPhase => _state.SyncStatus.Phase;

    public string SyncText => _state.SyncStatus switch
    {
        { Phase: SyncPhase.Syncing } => "Synchroniseren…",
        { Phase: SyncPhase.Failed, Error: { } error } => $"Synchronisatie mislukt: {error}",
        { Phase: SyncPhase.Failed } => "Synchronisatie mislukt",
        { LastSuccessUtc: { } at } => $"Laatst gesynchroniseerd: {at.ToLocalTime().ToString("t", CultureInfo.CurrentCulture)}",
        _ when !_hasApiKey => "Geen API-key ingesteld",
        _ => "Nog niet gesynchroniseerd",
    };

    public bool HasApiKey
    {
        get => _hasApiKey;
        private set
        {
            _hasApiKey = value;
            RaisePropertyChanged(nameof(HasApiKey));
            RaisePropertyChanged(nameof(NeedsApiKey));
            RaisePropertyChanged(nameof(SyncText));
        }
    }

    public bool NeedsApiKey => !_hasApiKey;

    /// <summary>True while a key is being checked, so the button cannot be pressed twice.</summary>
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

    /// <summary>Loads connection state and syncs in the background; offline start-up still shows stored data.</summary>
    public async Task InitializeAsync()
    {
        await Goals.InitializeAsync();
        await _sync.RestoreStatusAsync();
        HasApiKey = await _connection.HasKeyAsync();
        if (HasApiKey)
        {
            _ = SyncAsync();
        }
    }

    public async Task ConnectAsync(string? apiKey)
    {
        IsBusy = true;
        ConnectMessage = "Key controleren…";
        ConnectMessageIsError = false;
        try
        {
            var result = await _connection.ConnectAsync(apiKey);
            ConnectMessage = result.IsConnected ? $"{result.Message} Gegevens worden opgehaald…" : result.Message;
            ConnectMessageIsError = !result.IsConnected;
            HasApiKey = result.IsConnected;
            if (result.IsConnected)
            {
                _ = SyncAsync();
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
            HasApiKey = false;
            ConnectMessage = "De key is verwijderd van deze computer.";
            ConnectMessageIsError = false;
            _state.ReportSyncStatus(new SyncStatus(SyncPhase.Idle, _state.SyncStatus.LastSuccessUtc));
        }
        catch (Exception ex)
        {
            ConnectMessage = $"De key kon niet worden verwijderd: {ex.Message}";
            ConnectMessageIsError = true;
        }
    }

    private async Task SyncAsync()
    {
        try
        {
            await _sync.SyncAsync();
        }
        catch (Exception ex)
        {
            // SyncService reports API and storage failures itself; this catches anything unforeseen (e.g. the key store).
            _state.ReportSyncStatus(new SyncStatus(SyncPhase.Failed, _state.SyncStatus.LastSuccessUtc, ex.Message));
        }
    }

    public void Dispose()
    {
        _opacityTimer.Stop();
        _state.SettingsChanged -= OnSettingsChanged;
        _state.SyncStatusChanged -= OnSyncStatusChanged;
        Inventory.Dispose();
        Goals.Dispose();
    }

    /// <summary>Persists window bounds chosen by the user (move/resize).</summary>
    public void SaveOverlayBounds(OverlayBounds bounds) =>
        Persist(s => s with { Overlay = s.Overlay with { Bounds = bounds } });

    private static string FormatHotkey(HotkeyGesture gesture)
    {
        var parts = new List<string>(4);
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        parts.Add(System.Windows.Input.KeyInterop.KeyFromVirtualKey(gesture.VirtualKey).ToString());
        return string.Join("+", parts);
    }

    private void Persist(Func<AppSettings, AppSettings> change) => _ = PersistAsync(change);

    private async Task PersistAsync(Func<AppSettings, AppSettings> change)
    {
        try
        {
            await _state.UpdateAsync(change);
            await _dispatcher.InvokeAsync(() => ErrorMessage = null);
        }
        catch (Exception ex)
        {
            // Fire-and-forget callers cannot observe the failure, so surface every persistence error in the UI.
            await _dispatcher.InvokeAsync(() =>
            {
                ErrorMessage = $"Instelling kon niet worden opgeslagen: {ex.Message}";
                RaiseAllPropertiesChanged();
            });
        }
    }

    private void OnOpacityTimerTick(object? sender, EventArgs e)
    {
        _opacityTimer.Stop();
        if (_pendingOpacity is { } opacity)
        {
            _ = PersistOpacityAsync(opacity);
        }
    }

    private async Task PersistOpacityAsync(double opacity)
    {
        await PersistAsync(s => s with { Overlay = s.Overlay with { Opacity = opacity } });
        await _dispatcher.InvokeAsync(() =>
        {
            // A newer slider value may have arrived while saving; only release the value we just stored.
            if (!_opacityTimer.IsEnabled && _pendingOpacity == opacity)
            {
                _pendingOpacity = null;
                RaisePropertyChanged(nameof(Opacity));
            }
        });
    }

    private void OnSettingsChanged(object? sender, AppSettings settings) =>
        _dispatcher.InvokeAsync(() =>
        {
            // Keep the in-flight slider value until the debounced save has landed.
            if (!_opacityTimer.IsEnabled)
            {
                _pendingOpacity = null;
            }

            RaiseAllPropertiesChanged();
        });

    private void OnSyncStatusChanged(object? sender, SyncStatus status) =>
        _dispatcher.InvokeAsync(() =>
        {
            RaisePropertyChanged(nameof(SyncPhase));
            RaisePropertyChanged(nameof(SyncText));
        });
}
