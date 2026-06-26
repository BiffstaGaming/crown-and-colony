using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CrownAndColony.GameLogic.App;

/// <summary>How the game window is presented.</summary>
public enum WindowMode
{
    /// <summary>A normal resizable window.</summary>
    Windowed,

    /// <summary>Borderless full-screen on the current monitor.</summary>
    Fullscreen,
}

/// <summary>
/// The player's <b>application</b> settings — client preferences (video + audio), not game rules. A plain,
/// engine-free value object so it can be unit-tested (L1) without the Godot runtime: the Godot side
/// (<c>SettingsService</c>) maps it to/from a <c>user://settings.cfg</c> file and applies it to the engine.
/// </summary>
/// <remarks>
/// This is deliberately app config rather than gameplay state, but it lives in the engine-free GameLogic library so
/// it gets the strong L1 coverage (defaults, clamping, round-trip). Per-game <i>rule</i> options (difficulty, the
/// custom-house export mode) are a separate concern and do NOT belong here. Volumes are linear in <c>[0,1]</c>.
/// </remarks>
public sealed class SettingsModel
{
    // Dictionary keys for round-tripping through SettingsService's ConfigFile (string-keyed, culture-invariant).
    private const string KeyWindowMode = "window_mode";
    private const string KeyVSync = "vsync";
    private const string KeyMaster = "master_volume";
    private const string KeyMusic = "music_volume";
    private const string KeySfx = "sfx_volume";
    private const string KeyUiScale = "ui_scale";
    private const string KeyColorblind = "colorblind_mode";
    private const string KeyAutosavePeriod = "autosave_period";
    private const string KeyHiddenMessageCategories = "hidden_message_categories";
    private const string KeySilencedMessageCategories = "silenced_message_categories";

    /// <summary>Smallest allowed <see cref="UiScale"/> (75% — text/UI noticeably tighter but still legible).</summary>
    public const float MinUiScale = 0.75f;

    /// <summary>Largest allowed <see cref="UiScale"/> (200% — double-size UI for low-vision accessibility).</summary>
    public const float MaxUiScale = 2.0f;

    /// <summary>Largest allowed <see cref="AutosavePeriod"/> (every 100 turns — a sane upper bound for the slider).</summary>
    public const int MaxAutosavePeriod = 100;

    /// <summary>Window presentation mode. Default: <see cref="WindowMode.Windowed"/>.</summary>
    public WindowMode WindowMode { get; set; } = WindowMode.Windowed;

    /// <summary>Whether vertical sync is enabled. Default: on.</summary>
    public bool VSync { get; set; } = true;

    /// <summary>Master output volume, linear <c>[0,1]</c>. Default: 1.0.</summary>
    public float MasterVolume { get; set; } = 1.0f;

    /// <summary>Music bus volume, linear <c>[0,1]</c>. Default: 0.8.</summary>
    public float MusicVolume { get; set; } = 0.8f;

    /// <summary>Sound-effects bus volume, linear <c>[0,1]</c>. Default: 0.8.</summary>
    public float SfxVolume { get; set; } = 0.8f;

    /// <summary>
    /// UI scale factor applied to the whole interface (text + controls) via the engine's content-scale.
    /// <c>1.0</c> = the design size; clamped to <c>[<see cref="MinUiScale"/>, <see cref="MaxUiScale"/>]</c>.
    /// An accessibility option for small/large displays and low vision (FreeCol <c>DISPLAY_SCALING</c> /
    /// <c>MAIN_FONT_SIZE</c>). Default: 1.0.
    /// </summary>
    public float UiScale { get; set; } = 1.0f;

    /// <summary>
    /// When on, player/nation map colours are drawn from a colourblind-friendly high-contrast palette
    /// (the Okabe–Ito colour-universal set) instead of the default ruleset hues. An accessibility option;
    /// no FreeCol equivalent. Default: off.
    /// </summary>
    public bool ColorblindMode { get; set; }

    /// <summary>
    /// How often the game autosaves, in player turns: <c>N</c> means "write the autosave at the end of every
    /// <c>N</c>th turn". <c>0</c> disables autosaving entirely. Clamped to <c>[0, <see cref="MaxAutosavePeriod"/>]</c>.
    /// FreeCol's <c>ClientOptions.AUTOSAVE_PERIOD</c>. Default: 1 (autosave every turn).
    /// </summary>
    public int AutosavePeriod { get; set; } = 1;

    /// <summary>
    /// The message-log categories the player has chosen to <b>hide</b> — events of these kinds are filtered out of the
    /// re-openable message log. Empty by default (every category shown). A client preference (FreeCol's per-type
    /// <c>guiShow…</c> "show this kind of message" toggles, inverted to a hide-set so the common "show all" state
    /// persists as nothing). Persisted in <c>settings.cfg</c> as a comma-separated list of category names.
    /// </summary>
    public ISet<MessageCategory> HiddenMessageCategories { get; set; } = new HashSet<MessageCategory>();

    /// <summary>
    /// The message-log categories the player has chosen to <b>silence</b> — events of these kinds are still <em>logged</em>
    /// (they appear in the re-openable message log, unlike <see cref="HiddenMessageCategories"/>) but they no longer
    /// <b>pop up</b> the dismissible end-of-turn message panel. The popup-vs-silent half of FreeCol's per-type message
    /// display options (its "messages" client-option category lets each message type be <c>popup</c>, <c>log</c> only, or
    /// <c>ignore</c>d): our two sets compose to the same three states — silenced = "log only", hidden = "ignore", neither =
    /// "popup". Empty by default (every category pops up). Persisted in <c>settings.cfg</c> as a comma-separated list of
    /// category names.
    /// </summary>
    public ISet<MessageCategory> SilencedMessageCategories { get; set; } = new HashSet<MessageCategory>();

    /// <summary>Forces every field into its valid range (volumes clamped to <c>[0,1]</c>, UI scale to its range, autosave period to its range, unknown enum reset to default).</summary>
    public void Clamp()
    {
        MasterVolume = ClampUnit(MasterVolume);
        MusicVolume = ClampUnit(MusicVolume);
        SfxVolume = ClampUnit(SfxVolume);
        UiScale = float.IsNaN(UiScale) ? 1.0f : Math.Clamp(UiScale, MinUiScale, MaxUiScale);
        AutosavePeriod = Math.Clamp(AutosavePeriod, 0, MaxAutosavePeriod);
        if (!Enum.IsDefined(typeof(WindowMode), WindowMode))
        {
            WindowMode = WindowMode.Windowed;
        }
        // Drop any unknown/duplicate category token that slipped in (a hand-edited or stale config) so the hide/silence
        // sets never carry a value outside the enum.
        HiddenMessageCategories = new HashSet<MessageCategory>(
            HiddenMessageCategories.Where(c => Enum.IsDefined(typeof(MessageCategory), c)));
        SilencedMessageCategories = new HashSet<MessageCategory>(
            SilencedMessageCategories.Where(c => Enum.IsDefined(typeof(MessageCategory), c)));
    }

    private static float ClampUnit(float v) => float.IsNaN(v) ? 0f : Math.Clamp(v, 0f, 1f);

    /// <summary>Serializes to a flat string→string map for persistence (the inverse of <see cref="FromDictionary"/>).</summary>
    public IReadOnlyDictionary<string, string> ToDictionary()
    {
        var map = new Dictionary<string, string>
        {
            [KeyWindowMode] = WindowMode.ToString(),
            [KeyVSync] = VSync ? "true" : "false",
            [KeyMaster] = MasterVolume.ToString("R", CultureInfo.InvariantCulture),
            [KeyMusic] = MusicVolume.ToString("R", CultureInfo.InvariantCulture),
            [KeySfx] = SfxVolume.ToString("R", CultureInfo.InvariantCulture),
            [KeyUiScale] = UiScale.ToString("R", CultureInfo.InvariantCulture),
            [KeyColorblind] = ColorblindMode ? "true" : "false",
            [KeyAutosavePeriod] = AutosavePeriod.ToString(CultureInfo.InvariantCulture),
        };
        // Hidden message categories: omitted entirely when none are hidden (the default), so a player who never
        // touched the filter keeps a settings file with no extra key — written sorted by ordinal for a stable layout.
        if (HiddenMessageCategories.Count > 0)
        {
            map[KeyHiddenMessageCategories] = string.Join(
                ',', HiddenMessageCategories.OrderBy(c => (int)c).Select(c => c.ToString()));
        }
        // Silenced (popup-suppressed) categories: same omit-when-empty + ordinal-sorted layout as the hidden set, so a
        // player who never touched the popup toggles keeps a settings file with no extra key.
        if (SilencedMessageCategories.Count > 0)
        {
            map[KeySilencedMessageCategories] = string.Join(
                ',', SilencedMessageCategories.OrderBy(c => (int)c).Select(c => c.ToString()));
        }
        return map;
    }

    /// <summary>
    /// Builds a model from a persisted map. Missing or unparseable entries fall back to that field's default, and the
    /// result is always <see cref="Clamp"/>ed — so a corrupt or partial settings file can never produce invalid state.
    /// </summary>
    public static SettingsModel FromDictionary(IReadOnlyDictionary<string, string> data)
    {
        var m = new SettingsModel();
        if (data.TryGetValue(KeyWindowMode, out string? wm) && Enum.TryParse(wm, ignoreCase: true, out WindowMode parsed))
        {
            m.WindowMode = parsed;
        }
        if (data.TryGetValue(KeyVSync, out string? vsync))
        {
            m.VSync = vsync.Equals("true", StringComparison.OrdinalIgnoreCase);
        }
        m.MasterVolume = ParseUnit(data, KeyMaster, m.MasterVolume);
        m.MusicVolume = ParseUnit(data, KeyMusic, m.MusicVolume);
        m.SfxVolume = ParseUnit(data, KeySfx, m.SfxVolume);
        m.UiScale = ParseFloat(data, KeyUiScale, m.UiScale);
        if (data.TryGetValue(KeyColorblind, out string? cb))
        {
            m.ColorblindMode = cb.Equals("true", StringComparison.OrdinalIgnoreCase);
        }
        if (data.TryGetValue(KeyAutosavePeriod, out string? ap)
            && int.TryParse(ap, NumberStyles.Integer, CultureInfo.InvariantCulture, out int period))
        {
            m.AutosavePeriod = period; // Clamp() below forces it into range
        }
        ParseCategorySet(data, KeyHiddenMessageCategories, m.HiddenMessageCategories);
        ParseCategorySet(data, KeySilencedMessageCategories, m.SilencedMessageCategories);
        m.Clamp();
        return m;
    }

    // Parses a comma-separated list of MessageCategory names from data[key] into target; unknown/blank tokens are dropped
    // (Clamp() also re-filters). A missing key leaves target untouched (its default empty set).
    private static void ParseCategorySet(IReadOnlyDictionary<string, string> data, string key, ISet<MessageCategory> target)
    {
        if (!data.TryGetValue(key, out string? raw))
        {
            return;
        }
        foreach (string token in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse(token, ignoreCase: true, out MessageCategory cat))
            {
                target.Add(cat);
            }
        }
    }

    private static float ParseUnit(IReadOnlyDictionary<string, string> data, string key, float fallback) =>
        ParseFloat(data, key, fallback);

    private static float ParseFloat(IReadOnlyDictionary<string, string> data, string key, float fallback) =>
        data.TryGetValue(key, out string? raw)
            && float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float v)
            ? v
            : fallback;
}
