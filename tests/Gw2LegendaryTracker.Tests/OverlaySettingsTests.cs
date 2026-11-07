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

    [Theory]
    [InlineData(null, null)]
    [InlineData("legendaries", "legendaries")]
    [InlineData("masteries", "masteries")]
    [InlineData("Masteries", null)]
    [InlineData("iets anders", null)]
    public void Normalize_keeps_only_known_overlay_modes(string? input, string? expected)
    {
        Assert.Equal(expected, new OverlaySettings { Mode = input }.Normalize().Mode);
    }

    [Fact]
    public void The_overlay_shows_the_legendaries_unless_masteries_is_chosen()
    {
        Assert.False(OverlayModes.IsMasteries(new OverlaySettings().Mode));
        Assert.False(OverlayModes.IsMasteries("onbekend"));
        Assert.True(OverlayModes.IsMasteries(OverlayModes.Masteries));
    }

    [Fact]
    public void Settings_stored_before_the_mode_existed_show_the_legendaries()
    {
        var restored = System.Text.Json.JsonSerializer.Deserialize<OverlaySettings>(
            """{"opacity":0.7,"isExpanded":true}""",
            System.Text.Json.JsonSerializerOptions.Web);

        Assert.NotNull(restored);
        Assert.Null(restored.Mode);
        Assert.True(restored.IsExpanded);
    }
}
