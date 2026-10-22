using System.Globalization;
using System.Windows.Input;
using System.Windows.Threading;
using Gw2LegendaryTracker.Core.Settings;
using Gw2LegendaryTracker.Core.State;

namespace Gw2LegendaryTracker.App.ViewModels;

/// <summary>
/// View model shared by the main window and the overlay. Both windows bind to the same instance,
/// so a change made in one is visible in the other without extra plumbing.
/// </summary>
public sealed class AppViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan OpacityPersistDelay = TimeSpan.FromMilliseconds(300);

    private readonly AppState _state;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _opacityTimer;
    private double? _pendingOpacity;
    private bool _isOverlayVisible;
    private string? _hotkeyError;
    private string? _errorMessage;

    public AppViewModel(AppState state, Dispatcher dispatcher)
    {
        _state = state;
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
    }

    public event EventHandler? OverlayToggleRequested;

    public event EventHandler? OverlayHideRequested;

    public event EventHandler? MainWindowRequested;

    public ICommand ToggleOverlayCommand { get; }

    public ICommand ShowMainWindowCommand { get; }

    public ICommand ToggleExpandedCommand { get; }

    public ICommand HideOverlayCommand { get; }

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

    public string SelectedLegendaryName => _state.Settings.SelectedLegendaryId ?? "Geen Legendary geselecteerd";

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
        _ => "Nog niet gesynchroniseerd",
    };

    public void Dispose()
    {
        _opacityTimer.Stop();
        _state.SettingsChanged -= OnSettingsChanged;
        _state.SyncStatusChanged -= OnSyncStatusChanged;
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
