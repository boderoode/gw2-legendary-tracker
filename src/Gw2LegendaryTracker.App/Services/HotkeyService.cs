using System.ComponentModel;
using System.Windows.Interop;
using Gw2LegendaryTracker.App.Native;
using Gw2LegendaryTracker.Core.Settings;

namespace Gw2LegendaryTracker.App.Services;

/// <summary>Registers one system-wide hotkey on a hidden message-only window.</summary>
public sealed class HotkeyService : IDisposable
{
    private const int HotkeyId = 0x4C54;

    private readonly HwndSource _source;
    private bool _isRegistered;

    public HotkeyService()
    {
        _source = new HwndSource(new HwndSourceParameters("Gw2LegendaryTracker.HotkeyWindow")
        {
            ParentWindow = NativeMethods.HwndMessage,
        });
        _source.AddHook(WndProc);
    }

    public event EventHandler? Pressed;

    /// <summary>Replaces the current registration. Returns an error message, or <c>null</c> on success.</summary>
    public string? TryRegister(HotkeyGesture gesture)
    {
        ArgumentNullException.ThrowIfNull(gesture);
        Unregister();

        if (!NativeMethods.RegisterHotKey(_source.Handle, HotkeyId, ToNativeModifiers(gesture.Modifiers), (uint)gesture.VirtualKey))
        {
            // The usual cause is another application already owning this combination.
            return new Win32Exception().Message;
        }

        _isRegistered = true;
        return null;
    }

    public void Dispose()
    {
        Unregister();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    private static uint ToNativeModifiers(HotkeyModifiers modifiers)
    {
        // Win32 MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, which matches HotkeyModifiers.
        return (uint)modifiers | NativeMethods.ModNoRepeat;
    }

    private void Unregister()
    {
        if (_isRegistered)
        {
            NativeMethods.UnregisterHotKey(_source.Handle, HotkeyId);
            _isRegistered = false;
        }
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmHotkey && wParam == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }

        return nint.Zero;
    }
}
