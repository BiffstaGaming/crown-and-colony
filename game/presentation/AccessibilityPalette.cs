using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// Presentation-only (ADR-006) colour source for the player/nation markers that the map overlays draw —
/// <see cref="UnitMarker"/> owner rings, <see cref="MiniMap"/> colony/unit/settlement dots, and the rival/native
/// fallbacks in <see cref="GameController"/>. It exists so the colourblind-mode accessibility toggle can swap the
/// whole player-colour set in one place without each drawer knowing about settings.
/// </summary>
/// <remarks>
/// <para>
/// The flag is owned by <c>SettingsService.Apply()</c>, which sets <see cref="ColorblindMode"/> from the live
/// <c>SettingsModel</c> on boot and whenever the player toggles it; the drawers then read it the next time they
/// redraw. When the flag is <c>false</c> every accessor returns the original default colour, so existing behaviour
/// (and its golden/interaction tests) is unchanged.
/// </para>
/// <para>
/// The colourblind set is the <b>Okabe–Ito colour-universal palette</b> (Masataka Okabe &amp; Kei Ito, 2008,
/// <c>https://jfly.uni-koeln.de/color/</c>) — eight hues chosen to stay distinguishable under the common forms of
/// colour-vision deficiency (protanopia, deuteranopia, tritanopia). It is published for free reuse, so it carries
/// no licensing constraint for this GPL-v2 project.
/// </para>
/// </remarks>
public static class AccessibilityPalette
{
    /// <summary>
    /// Whether the colourblind-friendly palette is active. Set from the live settings by
    /// <c>SettingsService.Apply()</c>; read by the map drawers on redraw. Default off.
    /// </summary>
    public static bool ColorblindMode { get; set; }

    // ── Okabe–Ito colour-universal palette (https://jfly.uni-koeln.de/color/) ──────────────────────────────────
    // Vermillion, blue, bluish-green, orange, sky-blue, reddish-purple, yellow, black — distinguishable under the
    // common colour-vision deficiencies. Used as the per-nation cycle and the semantic role colours below.
    private static readonly Color CbVermillion = Color.FromString("#d55e00", Colors.Red);
    private static readonly Color CbBlue = Color.FromString("#0072b2", Colors.Blue);
    private static readonly Color CbBluishGreen = Color.FromString("#009e73", Colors.Green);
    private static readonly Color CbOrange = Color.FromString("#e69f00", Colors.Orange);
    private static readonly Color CbSkyBlue = Color.FromString("#56b4e9", Colors.SkyBlue);
    private static readonly Color CbReddishPurple = Color.FromString("#cc79a7", Colors.Purple);
    private static readonly Color CbYellow = Color.FromString("#f0e442", Colors.Yellow);

    /// <summary>Per-nation cycle (Okabe–Ito); a nation is keyed onto this by a stable hash of its short name.</summary>
    private static readonly Color[] CbNationCycle =
    {
        CbVermillion, CbBlue, CbBluishGreen, CbReddishPurple, CbOrange, CbSkyBlue, CbYellow,
    };

    /// <summary>
    /// The owner-ring / map colour for a foreign European nation. With colourblind mode off this is the nation's
    /// own ruleset colour (<paramref name="defaultColor"/>); with it on, a stable Okabe–Ito hue chosen by
    /// <paramref name="nationShortName"/> so the powers stay mutually distinguishable.
    /// </summary>
    /// <param name="nationShortName">The nation's short name (e.g. <c>dutch</c>), used as the cycle key.</param>
    /// <param name="defaultColor">The nation's normal ruleset colour, returned when colourblind mode is off.</param>
    public static Color RivalNation(string nationShortName, Color defaultColor) =>
        ColorblindMode ? CbNationCycle[StableIndex(nationShortName, CbNationCycle.Length)] : defaultColor;

    /// <summary>The native-power marker colour: a fixed bluish-green under colourblind mode, else <paramref name="defaultColor"/>.</summary>
    public static Color Native(Color defaultColor) => ColorblindMode ? CbBluishGreen : defaultColor;

    /// <summary>The generic rival fallback colour (no nation colour in the ruleset): vermillion under colourblind mode, else <paramref name="defaultColor"/>.</summary>
    public static Color RivalFallback(Color defaultColor) => ColorblindMode ? CbVermillion : defaultColor;

    /// <summary>The human player's own colony/unit marker colour: sky-blue under colourblind mode, else <paramref name="defaultColor"/>.</summary>
    public static Color Own(Color defaultColor) => ColorblindMode ? CbSkyBlue : defaultColor;

    /// <summary>The rival colony/unit dot colour on the minimap: vermillion under colourblind mode, else <paramref name="defaultColor"/>.</summary>
    public static Color Rival(Color defaultColor) => ColorblindMode ? CbVermillion : defaultColor;

    // Stable, culture-invariant index into a colour cycle from a short name (FNV-1a over the chars). Deterministic
    // across runs so a given nation always maps to the same colourblind hue.
    private static int StableIndex(string key, int modulo)
    {
        uint hash = 2166136261u;
        foreach (char c in key)
        {
            hash = (hash ^ c) * 16777619u;
        }
        return (int)(hash % (uint)modulo);
    }
}
