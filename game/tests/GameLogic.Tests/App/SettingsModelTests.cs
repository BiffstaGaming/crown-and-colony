using System.Collections.Generic;
using CrownAndColony.GameLogic.App;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.App;

public class SettingsModelTests
{
    [Fact]
    public void Defaults_AreSensible()
    {
        var m = new SettingsModel();

        Assert.Equal(WindowMode.Windowed, m.WindowMode);
        Assert.True(m.VSync);
        Assert.Equal(1.0f, m.MasterVolume);
        Assert.Equal(0.8f, m.MusicVolume);
        Assert.Equal(0.8f, m.SfxVolume);
        Assert.Equal(1.0f, m.UiScale);     // accessibility: no scaling by default
        Assert.False(m.ColorblindMode);    // accessibility: default ruleset palette
        Assert.Equal(1, m.AutosavePeriod); // autosave every turn by default (FreeCol AUTOSAVE_PERIOD)
    }

    [Fact]
    public void Clamp_ForcesVolumesIntoUnitRange()
    {
        var m = new SettingsModel { MasterVolume = 2.5f, MusicVolume = -1f, SfxVolume = float.NaN };

        m.Clamp();

        Assert.Equal(1.0f, m.MasterVolume);
        Assert.Equal(0.0f, m.MusicVolume);
        Assert.Equal(0.0f, m.SfxVolume);
    }

    [Theory]
    [InlineData(0.1f, SettingsModel.MinUiScale)]    // below range → floor
    [InlineData(5.0f, SettingsModel.MaxUiScale)]    // above range → ceiling
    [InlineData(float.NaN, 1.0f)]                   // NaN → default
    [InlineData(1.25f, 1.25f)]                      // in range → unchanged
    public void Clamp_ForcesUiScaleIntoRange(float input, float expected)
    {
        var m = new SettingsModel { UiScale = input };

        m.Clamp();

        Assert.Equal(expected, m.UiScale);
    }

    [Theory]
    [InlineData(-5, 0)]                                  // below range → off (0)
    [InlineData(0, 0)]                                   // off stays off
    [InlineData(5, 5)]                                   // in range → unchanged
    [InlineData(1000, SettingsModel.MaxAutosavePeriod)]  // above range → ceiling
    public void Clamp_ForcesAutosavePeriodIntoRange(int input, int expected)
    {
        var m = new SettingsModel { AutosavePeriod = input };

        m.Clamp();

        Assert.Equal(expected, m.AutosavePeriod);
    }

    [Fact]
    public void Dictionary_RoundTrip_PreservesEveryField()
    {
        var original = new SettingsModel
        {
            WindowMode = WindowMode.Fullscreen,
            VSync = false,
            MasterVolume = 0.5f,
            MusicVolume = 0.25f,
            SfxVolume = 0.1f,
            UiScale = 1.5f,
            ColorblindMode = true,
            AutosavePeriod = 7,
        };

        SettingsModel restored = SettingsModel.FromDictionary(original.ToDictionary());

        Assert.Equal(original.WindowMode, restored.WindowMode);
        Assert.Equal(original.VSync, restored.VSync);
        Assert.Equal(original.MasterVolume, restored.MasterVolume);
        Assert.Equal(original.MusicVolume, restored.MusicVolume);
        Assert.Equal(original.SfxVolume, restored.SfxVolume);
        Assert.Equal(original.UiScale, restored.UiScale);
        Assert.Equal(original.ColorblindMode, restored.ColorblindMode);
        Assert.Equal(original.AutosavePeriod, restored.AutosavePeriod);
    }

    [Fact]
    public void FromDictionary_MissingKeys_FallBackToDefaults()
    {
        SettingsModel m = SettingsModel.FromDictionary(new Dictionary<string, string>());

        Assert.Equal(WindowMode.Windowed, m.WindowMode);
        Assert.True(m.VSync);
        Assert.Equal(1.0f, m.MasterVolume);
        Assert.Equal(0.8f, m.MusicVolume);
        Assert.Equal(0.8f, m.SfxVolume);
        Assert.Equal(1.0f, m.UiScale);
        Assert.False(m.ColorblindMode);
        Assert.Equal(1, m.AutosavePeriod); // missing → default (every turn)
    }

    [Fact]
    public void FromDictionary_GarbageOrOutOfRange_IsSafe()
    {
        SettingsModel m = SettingsModel.FromDictionary(new Dictionary<string, string>
        {
            ["window_mode"] = "Hologram",   // unknown enum → default
            ["vsync"] = "yes",              // not "true" → false
            ["master_volume"] = "lots",     // unparseable → default (1.0)
            ["music_volume"] = "9.0",       // out of range → clamped to 1.0
            ["sfx_volume"] = "-3",          // out of range → clamped to 0.0
            ["ui_scale"] = "huge",          // unparseable → default (1.0)
            ["colorblind_mode"] = "maybe",  // not "true" → false
            ["autosave_period"] = "often",  // unparseable → default (1)
        });

        Assert.Equal(WindowMode.Windowed, m.WindowMode);
        Assert.False(m.VSync);
        Assert.Equal(1.0f, m.MasterVolume);
        Assert.Equal(1.0f, m.MusicVolume);
        Assert.Equal(0.0f, m.SfxVolume);
        Assert.Equal(1.0f, m.UiScale);
        Assert.False(m.ColorblindMode);
        Assert.Equal(1, m.AutosavePeriod);
    }

    [Fact]
    public void FromDictionary_AutosavePeriodOutOfRange_IsClamped()
    {
        SettingsModel m = SettingsModel.FromDictionary(new Dictionary<string, string>
        {
            ["autosave_period"] = "9999", // above range → clamped to the ceiling
        });

        Assert.Equal(SettingsModel.MaxAutosavePeriod, m.AutosavePeriod);
    }

    [Fact]
    public void FromDictionary_UiScaleOutOfRange_IsClamped()
    {
        SettingsModel m = SettingsModel.FromDictionary(new Dictionary<string, string>
        {
            ["ui_scale"] = "10.0", // above range → clamped to the ceiling
        });

        Assert.Equal(SettingsModel.MaxUiScale, m.UiScale);
    }
}
