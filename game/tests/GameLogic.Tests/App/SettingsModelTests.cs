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
        };

        SettingsModel restored = SettingsModel.FromDictionary(original.ToDictionary());

        Assert.Equal(original.WindowMode, restored.WindowMode);
        Assert.Equal(original.VSync, restored.VSync);
        Assert.Equal(original.MasterVolume, restored.MasterVolume);
        Assert.Equal(original.MusicVolume, restored.MusicVolume);
        Assert.Equal(original.SfxVolume, restored.SfxVolume);
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
    }

    [Fact]
    public void FromDictionary_GarbageOrOutOfRange_IsSafe()
    {
        SettingsModel m = SettingsModel.FromDictionary(new Dictionary<string, string>
        {
            ["window_mode"] = "Hologram",  // unknown enum → default
            ["vsync"] = "yes",             // not "true" → false
            ["master_volume"] = "lots",    // unparseable → default (1.0)
            ["music_volume"] = "9.0",      // out of range → clamped to 1.0
            ["sfx_volume"] = "-3",         // out of range → clamped to 0.0
        });

        Assert.Equal(WindowMode.Windowed, m.WindowMode);
        Assert.False(m.VSync);
        Assert.Equal(1.0f, m.MasterVolume);
        Assert.Equal(1.0f, m.MusicVolume);
        Assert.Equal(0.0f, m.SfxVolume);
    }
}
