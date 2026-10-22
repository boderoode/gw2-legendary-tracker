using System.Windows;
using System.Windows.Threading;
using Gw2LegendaryTracker.App.ViewModels;
using Gw2LegendaryTracker.App.Views;
using Gw2LegendaryTracker.Core.Settings;

namespace Gw2LegendaryTracker.App.Services;

/// <summary>Owns the overlay window: visibility, auto-hide, position persistence and click-through.</summary>
public sealed class OverlayController : IDisposable
{
    private static readonly TimeSpan BoundsPersistDelay = TimeSpan.FromMilliseconds(500);
    private const double DefaultMargin = 24;

    private readonly AppViewModel _viewModel;
    private readonly OverlayWindow _window;
    private readonly DispatcherTimer _autoHideTimer;
    private readonly DispatcherTimer _boundsTimer;

    public OverlayController(AppViewModel viewModel)
    {
        _viewModel = viewModel;
        _window = new OverlayWindow(viewModel);
        _autoHideTimer = new DispatcherTimer(DispatcherPriority.Background, _window.Dispatcher);
        _autoHideTimer.Tick += (_, _) => Hide();
        _boundsTimer = new DispatcherTimer(BoundsPersistDelay, DispatcherPriority.Background, OnBoundsTimerTick, _window.Dispatcher)
        {
            IsEnabled = false,
        };

        _window.LocationChanged += OnBoundsChanged;
        _window.SizeChanged += OnBoundsChanged;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.OverlayToggleRequested += (_, _) => Toggle();
        _viewModel.OverlayHideRequested += (_, _) => Hide();

        RestoreBounds(viewModel.Overlay.Bounds);
    }

    public bool IsVisible => _window.IsVisible;

    public void Toggle()
    {
        if (IsVisible)
        {
            Hide();
        }
        else
        {
            Show();
        }
    }

    public void Show()
    {
        if (!_window.IsVisible)
        {
            _window.Show();
        }

        _window.ApplyClickThrough(_viewModel.IsClickThrough);
        _window.RaiseToTop();
        _viewModel.IsOverlayVisible = true;
        RestartAutoHide();
    }

    public void Hide()
    {
        _autoHideTimer.Stop();
        _window.Hide();
        _viewModel.IsOverlayVisible = false;
    }

    public void Dispose()
    {
        _autoHideTimer.Stop();
        _boundsTimer.Stop();
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _window.Close();
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // An empty name means "everything changed", i.e. settings were replaced. Sync-status updates raise
        // specific names and must not keep the overlay alive.
        if (!string.IsNullOrEmpty(e.PropertyName))
        {
            return;
        }

        _window.ApplyClickThrough(_viewModel.IsClickThrough);
        if (_window.IsVisible)
        {
            RestartAutoHide();
        }
    }

    private void RestartAutoHide()
    {
        _autoHideTimer.Stop();
        if (_viewModel.Overlay.AutoHideSeconds is { } seconds)
        {
            _autoHideTimer.Interval = TimeSpan.FromSeconds(seconds);
            _autoHideTimer.Start();
        }
    }

    private void OnBoundsChanged(object? sender, EventArgs e)
    {
        if (!_window.IsLoaded)
        {
            return;
        }

        _boundsTimer.Stop();
        _boundsTimer.Start();
    }

    private void OnBoundsTimerTick(object? sender, EventArgs e)
    {
        _boundsTimer.Stop();
        if (_window.WindowState == WindowState.Normal)
        {
            _viewModel.SaveOverlayBounds(new OverlayBounds(_window.Left, _window.Top, _window.ActualWidth, _window.ActualHeight));
        }
    }

    /// <summary>Applies saved bounds, falling back to the top-right corner if they are no longer on any monitor.</summary>
    private void RestoreBounds(OverlayBounds? saved)
    {
        _window.WindowStartupLocation = WindowStartupLocation.Manual;

        if (saved is { } b && IsOnScreen(b))
        {
            _window.Left = b.Left;
            _window.Top = b.Top;
            _window.Width = Math.Max(b.Width, _window.MinWidth);
            _window.Height = Math.Max(b.Height, _window.MinHeight);
            return;
        }

        var work = SystemParameters.WorkArea;
        _window.Left = work.Right - _window.Width - DefaultMargin;
        _window.Top = work.Top + DefaultMargin;
    }

    /// <summary>True when the window's title strip still intersects the virtual desktop, so it can be grabbed.</summary>
    private static bool IsOnScreen(OverlayBounds b)
    {
        var desktop = new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);
        var strip = new Rect(b.Left, b.Top, Math.Max(b.Width, 1), 32);
        return desktop.IntersectsWith(strip);
    }
}
