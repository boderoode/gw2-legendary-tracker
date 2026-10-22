using Gw2LegendaryTracker.App.Native;

namespace Gw2LegendaryTracker.App.Services;

internal static class WindowStyles
{
    /// <summary>Sets or clears extended style <paramref name="flag"/> on the window.</summary>
    public static void SetExtendedStyle(nint hwnd, int flag, bool enabled)
    {
        var style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GwlExStyle);
        var updated = enabled ? style | flag : style & ~flag;
        if (updated != style)
        {
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GwlExStyle, updated);
        }
    }

    /// <summary>Re-asserts topmost z-order without activating the window or taking focus from the game.</summary>
    public static void BringToTopWithoutActivation(nint hwnd) =>
        NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HwndTopmost,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
}
