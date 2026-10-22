namespace Gw2LegendaryTracker.Core.Settings;

public sealed record OverlaySettings
{
    public const double MinOpacity = 0.2;
    public const double MaxOpacity = 1.0;
    public const int MinVisibleMaterials = 1;
    public const int MaxVisibleMaterials = 20;

    /// <summary>Window bounds in device-independent pixels; <c>null</c> until the user has moved the overlay.</summary>
    public OverlayBounds? Bounds { get; init; }

    public double Opacity { get; init; } = 0.85;

    public bool IsExpanded { get; init; }

    /// <summary>Prevents dragging the overlay.</summary>
    public bool IsLocked { get; init; }

    /// <summary>Lets mouse input pass through the overlay to the window below.</summary>
    public bool IsClickThrough { get; init; }

    public bool ShowOnStartup { get; init; } = true;

    /// <summary>Hides the overlay automatically after this many seconds; <c>null</c> disables the behaviour.</summary>
    public int? AutoHideSeconds { get; init; }

    /// <summary>Number of materials in the compact overlay.</summary>
    public int VisibleMaterialCount { get; init; } = 5;

    /// <summary>In the expanded overlay: list only what is still missing.</summary>
    public bool ShowOnlyMissing { get; init; } = true;

    public HotkeyGesture ToggleHotkey { get; init; } = HotkeyGesture.Default;

    /// <summary>Returns a copy with all numeric values clamped to their supported ranges.</summary>
    public OverlaySettings Normalize() => this with
    {
        Opacity = Math.Clamp(double.IsFinite(Opacity) ? Opacity : 0.85, MinOpacity, MaxOpacity),
        VisibleMaterialCount = Math.Clamp(VisibleMaterialCount, MinVisibleMaterials, MaxVisibleMaterials),
        AutoHideSeconds = AutoHideSeconds is > 0 ? AutoHideSeconds : null,
        ToggleHotkey = ToggleHotkey is { IsValid: true } ? ToggleHotkey : HotkeyGesture.Default,
    };
}

public sealed record OverlayBounds(double Left, double Top, double Width, double Height);
