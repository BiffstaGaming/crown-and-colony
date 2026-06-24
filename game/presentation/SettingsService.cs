using System;
using System.Collections.Generic;
using CrownAndColony.GameLogic.App;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// Global autoload (<c>/root/Settings</c>) that owns the live application <see cref="SettingsModel"/>: it loads the
/// settings from <c>user://settings.cfg</c> at startup, applies them to the engine (window mode, vsync, audio-bus
/// volumes), and persists them on request. The settings UI reads and mutates this; nothing else needs to know how or
/// where settings are stored.
/// </summary>
public partial class SettingsService : Node
{
    private const string ConfigPath = "user://settings.cfg";
    private const string Section = "settings";
    private static readonly string[] AuxBuses = { "Music", "SFX" };

    /// <summary>The live settings. Mutate through <see cref="UpdateAndApply"/> so changes reach the engine.</summary>
    public SettingsModel Settings { get; private set; } = new();

    /// <summary>On startup: ensure the Music/SFX buses exist, load saved settings, and apply them to the engine.</summary>
    public override void _Ready()
    {
        EnsureAudioBuses();
        Settings = Load();
        Apply();
    }

    /// <summary>Applies <paramref name="mutate"/> to the live settings, clamps them, and re-applies to the engine (does not persist — call <see cref="Save"/> for that).</summary>
    public void UpdateAndApply(Action<SettingsModel> mutate)
    {
        mutate(Settings);
        Settings.Clamp();
        Apply();
    }

    /// <summary>
    /// Pushes the current settings to the engine: window mode, vsync, the Master/Music/SFX bus volumes, the UI scale
    /// (root content-scale factor — resizes the whole interface live), and the colourblind palette flag.
    /// </summary>
    public void Apply()
    {
        DisplayServer.WindowSetMode(Settings.WindowMode == WindowMode.Fullscreen
            ? DisplayServer.WindowMode.Fullscreen
            : DisplayServer.WindowMode.Windowed);
        DisplayServer.WindowSetVsyncMode(Settings.VSync
            ? DisplayServer.VSyncMode.Enabled
            : DisplayServer.VSyncMode.Disabled);
        SetBusVolume("Master", Settings.MasterVolume);
        SetBusVolume("Music", Settings.MusicVolume);
        SetBusVolume("SFX", Settings.SfxVolume);
        ApplyUiScale(Settings.UiScale);
        AccessibilityPalette.ColorblindMode = Settings.ColorblindMode;
    }

    // The root viewport's content-scale factor scales every Control under it (text + widgets) in one step, so a single
    // setting resizes the whole UI live and on boot without rebuilding the theme. Guarded for headless/CI where there
    // is no SceneTree window. See docs/systems/settings.md §3.
    private void ApplyUiScale(float scale)
    {
        if (GetTree()?.Root is { } root)
        {
            root.ContentScaleFactor = scale;
        }
    }

    /// <summary>Writes the current settings to <c>user://settings.cfg</c>.</summary>
    public void Save()
    {
        var cfg = new ConfigFile();
        foreach (KeyValuePair<string, string> kv in Settings.ToDictionary())
        {
            cfg.SetValue(Section, kv.Key, kv.Value);
        }
        cfg.Save(ConfigPath);
    }

    private static SettingsModel Load()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(ConfigPath) != Error.Ok || !cfg.HasSection(Section))
        {
            return new SettingsModel(); // first run / missing / unreadable → defaults
        }
        var data = new Dictionary<string, string>();
        foreach (string key in cfg.GetSectionKeys(Section))
        {
            data[key] = cfg.GetValue(Section, key).AsString();
        }
        return SettingsModel.FromDictionary(data);
    }

    // The default project has only a Master bus; create Music/SFX (routed to Master) so their volume sliders are real
    // even before any audio is routed to them (music/SFX assets are a later [ART] task).
    private static void EnsureAudioBuses()
    {
        foreach (string name in AuxBuses)
        {
            if (AudioServer.GetBusIndex(name) < 0)
            {
                int idx = AudioServer.BusCount;
                AudioServer.AddBus(idx);
                AudioServer.SetBusName(idx, name);
                AudioServer.SetBusSend(idx, "Master");
            }
        }
    }

    private static void SetBusVolume(string bus, float linear)
    {
        int idx = AudioServer.GetBusIndex(bus);
        if (idx >= 0)
        {
            AudioServer.SetBusVolumeDb(idx, Mathf.LinearToDb(linear));
        }
    }
}
