using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Gw2LegendaryTracker.App.Native;
using Gw2LegendaryTracker.App.Services;
using Gw2LegendaryTracker.App.ViewModels;

namespace Gw2LegendaryTracker.App.Views;

public partial class OverlayWindow : Window
{
    private nint _hwnd;

    public OverlayWindow(AppViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private AppViewModel ViewModel => (AppViewModel)DataContext;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;

        // NOACTIVATE keeps the game focused when the overlay is clicked; TOOLWINDOW hides it from Alt+Tab.
        WindowStyles.SetExtendedStyle(_hwnd, NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow, enabled: true);
        ApplyClickThrough(ViewModel.IsClickThrough);
    }

    public void ApplyClickThrough(bool enabled)
    {
        if (_hwnd != nint.Zero)
        {
            WindowStyles.SetExtendedStyle(_hwnd, NativeMethods.WsExTransparent, enabled);
        }
    }

    /// <summary>Re-asserts the topmost z-order, which another topmost window may have taken over.</summary>
    public void RaiseToTop()
    {
        if (_hwnd != nint.Zero)
        {
            WindowStyles.BringToTopWithoutActivation(_hwnd);
        }
    }

    private void OnDragHandleMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && !ViewModel.IsLocked)
        {
            DragMove();
        }
    }
}
