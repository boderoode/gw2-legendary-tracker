namespace Gw2LegendaryTracker.Core.Settings;

/// <summary>A global hotkey. <paramref name="VirtualKey"/> is a Win32 virtual-key code.</summary>
public sealed record HotkeyGesture(HotkeyModifiers Modifiers, int VirtualKey)
{
    /// <summary>Ctrl+Alt+L, chosen to avoid colliding with common Guild Wars 2 bindings.</summary>
    public static HotkeyGesture Default { get; } = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x4C);

    public bool IsValid => Modifiers != HotkeyModifiers.None && VirtualKey is > 0 and < 0xFF;
}
