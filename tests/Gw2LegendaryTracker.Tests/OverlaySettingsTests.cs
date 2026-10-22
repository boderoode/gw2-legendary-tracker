using Gw2LegendaryTracker.Core.Settings;

namespace Gw2LegendaryTracker.Tests;

public class OverlaySettingsTests
{
    [Theory]
    [InlineData(-1, OverlaySettings.MinOpacity)]
    [InlineData(5, OverlaySettings.MaxOpacity)]
    [InlineData(double.NaN, 0.85)]
    [InlineData(0.5, 0.5)]
    public void Normalize_clamps_opacity(double input, double expected)
    {
        var result = new OverlaySettings { Opacity = input }.Normalize();

        Assert.Equal(expected, result.Opacity);
    }

    [Theory]
    [InlineData(0, OverlaySettings.MinVisibleMaterials)]
    [InlineData(999, OverlaySettings.MaxVisibleMaterials)]
    public void Normalize_clamps_visible_material_count(int input, int expected)
    {
        var result = new OverlaySettings { VisibleMaterialCount = input }.Normalize();

        Assert.Equal(expected, result.VisibleMaterialCount);
    }

    [Fact]
    public void Normalize_replaces_hotkey_without_modifier()
    {
        var result = new OverlaySettings { ToggleHotkey = new HotkeyGesture(HotkeyModifiers.None, 0x4C) }.Normalize();

        Assert.Equal(HotkeyGesture.Default, result.ToggleHotkey);
    }

    [Fact]
    public void Normalize_disables_non_positive_auto_hide()
    {
        Assert.Null(new OverlaySettings { AutoHideSeconds = 0 }.Normalize().AutoHideSeconds);
        Assert.Equal(10, new OverlaySettings { AutoHideSeconds = 10 }.Normalize().AutoHideSeconds);
    }
}
