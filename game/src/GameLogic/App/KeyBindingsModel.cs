using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CrownAndColony.GameLogic.App;

/// <summary>
/// The canonical, engine-free description of the game's rebindable keyboard actions and the player's <b>overrides</b>
/// of their default keys. A plain value object (no Godot types) so the action list, the default keys and the
/// override round-trip get L1 coverage without the engine; the Godot side (<c>KeyBindingsService</c>) maps an action
/// id + a <see cref="KeyChord"/> onto an <c>InputMap</c> action / <c>InputEventKey</c>, and persists the overrides to
/// <c>user://settings.cfg</c> (a <c>[keybindings]</c> section — <b>not</b> the game save, so no save-format change).
/// </summary>
/// <remarks>
/// The default keys here mirror the <c>[input]</c> action defaults shipped in <c>project.godot</c> (and the historical
/// hardcoded hotkeys). A binding is <i>rebindable</i> from the in-game key-bindings screen; an override that differs
/// from the default is the only thing persisted, so a fresh install (or a Reset) reads no <c>[keybindings]</c> section
/// and the engine simply uses the project defaults. Application config, deliberately separate from per-game rule
/// options (ADR-006 / ADR-009).
/// </remarks>
public sealed class KeyBindingsModel
{
    /// <summary>A single key combination: a Godot <c>Key</c> keycode plus whether Ctrl must be held. Engine-free.</summary>
    /// <param name="Keycode">The Godot <c>Key</c> enum value as a plain <see cref="long"/> (e.g. 4194309 = Enter, 87 = W).</param>
    /// <param name="Ctrl">Whether the Ctrl modifier must be held for the chord to match.</param>
    public readonly record struct KeyChord(long Keycode, bool Ctrl);

    /// <summary>
    /// One rebindable action: its stable <see cref="ActionId"/> (the <c>InputMap</c> action name + the persistence key),
    /// a human <see cref="Label"/> for the legend / rebind screen, and the <see cref="DefaultChord"/> shipped in
    /// <c>project.godot</c>. <see cref="Rebindable"/> is false for actions the player may not remap (none today, kept as
    /// a seam) — every shipped action is rebindable.
    /// </summary>
    /// <param name="ActionId">Stable id — the <c>InputMap</c> action name and the <c>settings.cfg</c> key.</param>
    /// <param name="Label">Human-readable label shown in the F1 legend and the rebind screen.</param>
    /// <param name="DefaultChord">The default key combination (mirrors the <c>project.godot</c> default).</param>
    /// <param name="Rebindable">Whether the player may remap this action (always true today).</param>
    public sealed record ActionDef(string ActionId, string Label, KeyChord DefaultChord, bool Rebindable = true);

    /// <summary>
    /// The authoritative list of rebindable actions, in display order. The <see cref="ActionDef.ActionId"/>s match the
    /// <c>[input]</c> action names in <c>project.godot</c>; the <see cref="ActionDef.DefaultChord"/>s match those defaults
    /// (Enter=4194309, Keypad-Enter handled engine-side as a second default event, etc.). This is the single source the
    /// rebind screen, the legend regeneration and the boot-time override apply all read.
    /// </summary>
    public static readonly IReadOnlyList<ActionDef> Actions =
    [
        new("end_turn",         "End turn",                   new KeyChord(4194309, false)),
        new("skip_unit",        "Skip unit (this turn)",      new KeyChord(32, false)),
        new("next_unit",        "Next unit needing orders",   new KeyChord(87, false)),
        new("goto_mode",        "Go to (set destination)",    new KeyChord(71, false)),
        new("build_colony",     "Build colony",               new KeyChord(66, false)),
        new("disband_unit",     "Disband unit",               new KeyChord(68, false)),
        new("open_europe",      "Europe",                     new KeyChord(69, false)),
        new("find_settlement",  "Find settlement",            new KeyChord(76, false)),
        new("founding_fathers", "Founding fathers",           new KeyChord(70, false)),
        new("colopedia",        "Colopedia",                  new KeyChord(67, false)),
        new("center_unit",      "Centre on unit",             new KeyChord(67, true)),
        new("new_map",          "New map",                    new KeyChord(78, false)),
        new("save_game",        "Save game",                  new KeyChord(83, true)),
        new("load_game",        "Load game",                  new KeyChord(79, true)),
        new("quick_save",       "Quick save",                 new KeyChord(4194336, false)),
        new("quick_load",       "Quick load",                 new KeyChord(4194340, false)),
        new("toggle_legend",    "Toggle this legend",         new KeyChord(4194332, false)),
    ];

    private readonly Dictionary<string, KeyChord> _overrides = [];

    /// <summary>The set of action ids that currently differ from their default (i.e. have a player override).</summary>
    public IReadOnlyCollection<string> OverriddenActions => _overrides.Keys;

    /// <summary>
    /// The chord currently bound to <paramref name="actionId"/>: the player's override if one is set, otherwise the
    /// action's shipped default. Throws for an unknown action id (a programming error — ids come from <see cref="Actions"/>).
    /// </summary>
    public KeyChord ChordFor(string actionId)
    {
        if (_overrides.TryGetValue(actionId, out KeyChord chord))
        {
            return chord;
        }
        return DefaultFor(actionId);
    }

    /// <summary>Whether <paramref name="actionId"/> currently has a player override (differs from its default).</summary>
    public bool HasOverride(string actionId) => _overrides.ContainsKey(actionId);

    /// <summary>The shipped default chord for <paramref name="actionId"/>. Throws for an unknown id.</summary>
    public static KeyChord DefaultFor(string actionId) =>
        Actions.FirstOrDefault(a => a.ActionId == actionId)?.DefaultChord
            ?? throw new ArgumentException($"Unknown key-binding action '{actionId}'.", nameof(actionId));

    /// <summary>
    /// Rebinds <paramref name="actionId"/> to <paramref name="chord"/>. If the chord equals the action's default the
    /// override is cleared (so the default is used and nothing is persisted); otherwise it is recorded as an override.
    /// No-op for an unknown id (defensive — callers pass ids from <see cref="Actions"/>).
    /// </summary>
    public void Set(string actionId, KeyChord chord)
    {
        if (Actions.All(a => a.ActionId != actionId))
        {
            return;
        }
        if (chord == DefaultFor(actionId))
        {
            _overrides.Remove(actionId);
        }
        else
        {
            _overrides[actionId] = chord;
        }
    }

    /// <summary>Clears the override for <paramref name="actionId"/> (back to its default). No-op if none is set.</summary>
    public void Reset(string actionId) => _overrides.Remove(actionId);

    /// <summary>Clears every override (all actions back to their shipped defaults).</summary>
    public void ResetAll() => _overrides.Clear();

    /// <summary>
    /// Serializes the <b>overrides only</b> to a flat string→string map for persistence (the inverse of
    /// <see cref="FromDictionary"/>). Each entry is <c>actionId → "keycode[,ctrl]"</c>; defaults are omitted, so a
    /// model with no overrides serializes to an empty map (no <c>[keybindings]</c> section written).
    /// </summary>
    public IReadOnlyDictionary<string, string> ToDictionary() =>
        _overrides.ToDictionary(kv => kv.Key, kv => Encode(kv.Value));

    /// <summary>
    /// Builds a model from a persisted override map. Entries whose key is not a known action, or whose value does not
    /// parse, are ignored; an override equal to its action's default is dropped (so it stays a default). A missing or
    /// empty map yields all-defaults — so a corrupt or partial file can never produce an invalid binding.
    /// </summary>
    public static KeyBindingsModel FromDictionary(IReadOnlyDictionary<string, string> data)
    {
        var m = new KeyBindingsModel();
        foreach (KeyValuePair<string, string> kv in data)
        {
            if (Actions.All(a => a.ActionId != kv.Key))
            {
                continue; // unknown action id — ignore
            }
            if (TryDecode(kv.Value, out KeyChord chord))
            {
                m.Set(kv.Key, chord); // Set() drops a chord that equals the default
            }
        }
        return m;
    }

    // "keycode" or "keycode,ctrl" — culture-invariant; ctrl is the literal "ctrl" suffix.
    private static string Encode(KeyChord c) =>
        c.Ctrl ? $"{c.Keycode.ToString(CultureInfo.InvariantCulture)},ctrl"
               : c.Keycode.ToString(CultureInfo.InvariantCulture);

    private static bool TryDecode(string raw, out KeyChord chord)
    {
        chord = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }
        string[] parts = raw.Split(',');
        if (!long.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long keycode))
        {
            return false;
        }
        bool ctrl = parts.Length > 1 && parts[1].Trim().Equals("ctrl", StringComparison.OrdinalIgnoreCase);
        chord = new KeyChord(keycode, ctrl);
        return true;
    }
}
