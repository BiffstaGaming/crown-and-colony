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

    // The master-mute flag lives in the [settings] section under this key. It is a presentation-only audio control held
    // here (NOT on the engine-free SettingsModel — that stays pure GameLogic, ADR-006) so the mute needs no save bump and
    // no model change; it round-trips through the same settings.cfg ConfigFile as every other option.
    private const string KeyMasterMute = "master_mute";

    // The tutorial-hints flag lives in the [settings] section under this key. Like the master-mute flag it is a
    // presentation-only CLIENT preference (FreeCol's ClientOptions tutorial toggle) held here — NOT on the engine-free
    // SettingsModel and NOT in the save (ADR-006 / no save bump) — so the guided-intro tip cards are a purely cosmetic
    // overlay the player can turn off. Default ON: a new player sees the tutorial unless they opt out.
    private const string KeyTutorialHints = "tutorial_hints";

    private static readonly string[] AuxBuses = { "Music", "SFX" };

    /// <summary>The live settings. Mutate through <see cref="UpdateAndApply"/> so changes reach the engine.</summary>
    public SettingsModel Settings { get; private set; } = new();

    /// <summary>
    /// Whether the master output is muted (silences <em>all</em> audio while preserving the saved volume levels). A
    /// presentation-only control persisted alongside <see cref="Settings"/> in <c>settings.cfg</c>; mutate through
    /// <see cref="SetMasterMute"/> so the change reaches the Master bus. Default off.
    /// </summary>
    public bool MasterMute { get; private set; }

    /// <summary>
    /// Whether the guided-intro <b>tutorial hints</b> are enabled — the small contextual tip cards a new player sees as
    /// they reach each early milestone. A presentation-only CLIENT preference persisted alongside <see cref="Settings"/>
    /// in <c>settings.cfg</c> (not game state, no save bump — ADR-006); mutate through <see cref="SetTutorialHints"/>.
    /// Default <b>on</b>, so a new player is taught the opening loop unless they opt out (the Settings toggle or the
    /// card's "Skip tutorial" button).
    /// </summary>
    public bool TutorialHints { get; private set; } = true;

    /// <summary>On startup: ensure the Music/SFX buses exist, load saved settings, apply them to the engine, and apply any saved key-binding overrides to the global <c>InputMap</c> (so a rebound key works before the game scene runs).</summary>
    public override void _Ready()
    {
        EnsureAudioBuses();
        Settings = Load();
        MasterMute = LoadMute();
        TutorialHints = LoadTutorialHints();
        Apply();
        KeyBindingsService.LoadAndApply(); // saved hotkey overrides → InputMap (settings.cfg [keybindings]; no save bump)
    }

    /// <summary>Sets the master mute and applies it to the Master bus immediately (does not persist — call <see cref="Save"/> for that). Muting silences every bus at once without disturbing the saved volume sliders.</summary>
    public void SetMasterMute(bool muted)
    {
        MasterMute = muted;
        Apply();
    }

    /// <summary>
    /// Enables or disables the tutorial hints (does not persist — call <see cref="Save"/> for that). Purely a client
    /// preference: there is no engine effect to apply, so this only records the flag; the <c>TutorialService</c> reads it
    /// to decide whether to show its tip cards. Turning it off is how the card's "Skip tutorial" button and the Settings
    /// toggle both silence the guided intro.
    /// </summary>
    public void SetTutorialHints(bool enabled) => TutorialHints = enabled;

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
        // Muting the Master bus silences every child bus (Music + SFX) in one step while leaving their volume levels
        // untouched, so un-muting restores the exact sliders the player set (FreeCol's audio on/off, applied per-bus).
        SetBusMute("Master", MasterMute);
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

    /// <summary>Writes the current settings (and the master-mute flag) to <c>user://settings.cfg</c>, preserving the <c>[keybindings]</c> section.</summary>
    public void Save()
    {
        var cfg = new ConfigFile();
        cfg.Load(ConfigPath); // ignore the result: a missing file just starts empty; preserves the [keybindings] section
        if (cfg.HasSection(Section))
        {
            cfg.EraseSection(Section); // rebuild the section so a dropped key (e.g. mute back to default) does not linger
        }
        foreach (KeyValuePair<string, string> kv in Settings.ToDictionary())
        {
            cfg.SetValue(Section, kv.Key, kv.Value);
        }
        cfg.SetValue(Section, KeyMasterMute, MasterMute); // presentation-only flag, alongside the model's keys
        cfg.SetValue(Section, KeyTutorialHints, TutorialHints); // presentation-only client preference, no save bump
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
        return SettingsModel.FromDictionary(data); // FromDictionary ignores the extra master_mute key (unknown → skipped)
    }

    // Reads the master-mute flag from the [settings] section (default off when missing/unreadable — a first run, or a
    // config written before mute existed). Kept separate from SettingsModel so the engine-free model stays pure.
    private static bool LoadMute()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(ConfigPath) != Error.Ok || !cfg.HasSection(Section))
        {
            return false;
        }
        return cfg.GetValue(Section, KeyMasterMute, false).AsBool();
    }

    // Reads the tutorial-hints flag from the [settings] section. Defaults to ON when missing/unreadable — a first run, or
    // a config written before the tutorial existed — so a new player is taught the opening loop by default. Kept separate
    // from SettingsModel so the engine-free model stays pure (matches the master-mute flag's treatment).
    private static bool LoadTutorialHints()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(ConfigPath) != Error.Ok || !cfg.HasSection(Section))
        {
            return true;
        }
        return cfg.GetValue(Section, KeyTutorialHints, true).AsBool();
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

    private static void SetBusMute(string bus, bool muted)
    {
        int idx = AudioServer.GetBusIndex(bus);
        if (idx >= 0)
        {
            AudioServer.SetBusMute(idx, muted);
        }
    }
}
