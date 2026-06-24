using System.Collections.Generic;
using CrownAndColony.GameLogic.App;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// The presentation bridge between the engine-free <see cref="KeyBindingsModel"/> (the action list + the player's key
/// overrides) and Godot's global <c>InputMap</c> + the <c>user://settings.cfg</c> file. It is a small static helper
/// (no node / autoload of its own): the <see cref="SettingsService"/> autoload calls <see cref="LoadAndApply"/> once on
/// boot so any saved overrides reach the <c>InputMap</c> before the game scene runs, and the key-bindings screen calls
/// <see cref="Apply"/> / <see cref="Save"/> as the player rebinds.
/// </summary>
/// <remarks>
/// Presentation-only (ADR-006): it touches no game rules. Persistence lives in <c>settings.cfg</c> under a
/// <c>[keybindings]</c> section — the application-settings file, <b>not</b> the game save (ADR-009: no save-format
/// change). Only <i>overridden</i> actions are written; an action left at its <c>project.godot</c> default is not
/// touched in the <c>InputMap</c> (so a multi-event default like End-Turn's Enter + Keypad-Enter is preserved until
/// the player deliberately rebinds it).
/// </remarks>
public static class KeyBindingsService
{
    private const string ConfigPath = "user://settings.cfg";
    private const string Section = "keybindings";

    /// <summary>Loads the saved key overrides from <c>settings.cfg</c> and applies them to the global <c>InputMap</c>. Returns the loaded model so a caller can reuse it (the rebind screen reloads its own).</summary>
    public static KeyBindingsModel LoadAndApply()
    {
        KeyBindingsModel model = Load();
        Apply(model);
        return model;
    }

    /// <summary>
    /// Reads the persisted overrides from <c>settings.cfg</c> into a fresh <see cref="KeyBindingsModel"/>. A missing
    /// file or missing <c>[keybindings]</c> section yields an all-defaults model (no overrides).
    /// </summary>
    public static KeyBindingsModel Load()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(ConfigPath) != Error.Ok || !cfg.HasSection(Section))
        {
            return new KeyBindingsModel(); // first run / no rebinds → all defaults
        }
        var data = new Dictionary<string, string>();
        foreach (string key in cfg.GetSectionKeys(Section))
        {
            data[key] = cfg.GetValue(Section, key).AsString();
        }
        return KeyBindingsModel.FromDictionary(data);
    }

    /// <summary>
    /// Writes the model's overrides to the <c>[keybindings]</c> section of <c>settings.cfg</c>, leaving every other
    /// section (the <c>[settings]</c> video/audio/accessibility block) intact. The section is rebuilt from scratch each
    /// time (erased then repopulated) so a removed override (a Reset back to default) does not linger on disk.
    /// </summary>
    public static void Save(KeyBindingsModel model)
    {
        var cfg = new ConfigFile();
        cfg.Load(ConfigPath); // ignore the result: a missing file just starts empty, other sections are preserved
        if (cfg.HasSection(Section))
        {
            cfg.EraseSection(Section); // drop stale overrides (incl. any since Reset to default)
        }
        foreach (KeyValuePair<string, string> kv in model.ToDictionary())
        {
            cfg.SetValue(Section, kv.Key, kv.Value);
        }
        cfg.Save(ConfigPath);
    }

    /// <summary>
    /// Applies the model to the global <c>InputMap</c>: for each <b>overridden</b> action it clears that action's events
    /// and sets the single override key event; an action left at its default is not touched (so the <c>project.godot</c>
    /// defaults — including any multi-key default — stand). Unknown actions are skipped (defensive).
    /// </summary>
    public static void Apply(KeyBindingsModel model)
    {
        foreach (string actionId in model.OverriddenActions)
        {
            if (!InputMap.HasAction(actionId))
            {
                continue;
            }
            InputMap.ActionEraseEvents(actionId);
            InputMap.ActionAddEvent(actionId, EventFor(model.ChordFor(actionId)));
        }
    }

    /// <summary>
    /// Rebinds a single action live: records the override on <paramref name="model"/> and updates the <c>InputMap</c>
    /// for just that action — sets the new event when it differs from the default, or restores the action's default
    /// event(s) when the chord <i>is</i> the default (a Reset). Does not persist; the caller saves on Back.
    /// </summary>
    public static void Rebind(KeyBindingsModel model, string actionId, KeyBindingsModel.KeyChord chord)
    {
        model.Set(actionId, chord);
        if (!InputMap.HasAction(actionId))
        {
            return;
        }
        InputMap.ActionEraseEvents(actionId);
        if (model.HasOverride(actionId))
        {
            InputMap.ActionAddEvent(actionId, EventFor(chord));
        }
        else
        {
            // Back to default — restore the shipped default chord (the engine had multi-event defaults only for
            // end_turn's Keypad-Enter, which the rebind screen does not expose, so a single default event suffices).
            InputMap.ActionAddEvent(actionId, EventFor(KeyBindingsModel.DefaultFor(actionId)));
        }
    }

    /// <summary>Builds the <see cref="InputEventKey"/> for a chord (keycode + Ctrl), matching the existing logical-keycode dispatch.</summary>
    public static InputEventKey EventFor(KeyBindingsModel.KeyChord chord) => new()
    {
        Keycode = (Key)chord.Keycode,
        CtrlPressed = chord.Ctrl,
    };

    /// <summary>
    /// A human-readable label for a chord (e.g. "Ctrl+S", "Enter", "Space", "W") — shared by the F1 legend and the
    /// rebind screen so they read identically. Mirrors the old <c>KeyChord.ToString()</c> in <c>GameController</c>.
    /// </summary>
    public static string Describe(KeyBindingsModel.KeyChord chord)
    {
        var code = (Key)chord.Keycode;
        string name = code switch
        {
            Key.Enter or Key.KpEnter => "Enter",
            Key.Space => "Space",
            _ => code.ToString(),
        };
        return chord.Ctrl ? $"Ctrl+{name}" : name;
    }
}
