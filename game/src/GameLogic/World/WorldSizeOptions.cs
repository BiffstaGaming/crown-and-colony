using System.Collections.Generic;

namespace CrownAndColony.GameLogic.World;

/// <summary>A selectable new-game map size — width × height in tiles (FreeCol <c>model.option.mapWidth</c>/<c>mapHeight</c>).</summary>
public sealed record WorldSize(string Name, int Width, int Height);

/// <summary>A selectable new-game land amount — the fraction of the map grown into land (FreeCol <c>model.option.landMass</c>).</summary>
public sealed record LandMass(string Name, double Fraction);

/// <summary>
/// The shape the land takes on a freshly generated map — FreeCol's <c>model.option.landGeneratorType</c>
/// (<c>LandGeneratorType</c>): one big <see cref="Continent"/>, a handful of large islands
/// (<see cref="Archipelago"/>), or many small <see cref="Islands"/>. Maps to FreeCol's
/// <c>LAND_GENERATOR_CONTINENT / ARCHIPELAGO / ISLANDS</c>. <see cref="Continent"/> is the shipped default and is
/// the historical generator (byte-identical, ADR-009); the other two are opt-in alternatives.
/// </summary>
public enum LandStyle
{
    /// <summary>One main landmass (the shipped default — the historical frontier-grown continent).</summary>
    Continent = 0,

    /// <summary>A few large islands separated by sea (FreeCol <c>LAND_GENERATOR_ARCHIPELAGO</c>).</summary>
    Archipelago = 1,

    /// <summary>Many small islands scattered across the sea (FreeCol <c>LAND_GENERATOR_ISLANDS</c>).</summary>
    Islands = 2,
}

/// <summary>A selectable new-game landmass style — the shape the land takes (FreeCol <c>model.option.landGeneratorType</c>).</summary>
public sealed record LandStyleOption(string Name, LandStyle Style);

/// <summary>
/// The new-game world-shape presets (FreeCol's <c>MapGeneratorOptions</c>): pick a map <b>size</b> and how much of it
/// is <b>land</b>. "Data not code" like the game variants — adding a band is a list entry, no logic change. The bounds
/// mirror FreeCol (width 30–200, height 20–200, land 15–50%); every preset here stays inside them.
/// <para><b>Portrait, FreeCol-faithful (86d3jy0rn).</b> The presets are <b>taller than wide</b> — Colonization's maps
/// run north–south (the Americas), and a landscape map drawn in isometric squeezes the continent into a wide diagonal
/// band ("rotated/squished", Chris's playtest). The default (<see cref="DefaultSize"/> <b>40×100</b> + 25% land) is
/// FreeCol's own <c>model.option.mapWidth</c>/<c>mapHeight</c>/<c>landMass</c> default. This is the <b>player-facing</b>
/// new-game default; the engine-free unit tests keep <see cref="GameSession.Game.New"/>'s own small map default
/// (36×24, 45% — a fast, stable fixture that doesn't exercise these presets), so the L1/L2 suite is unaffected. The
/// presentation goldens (which start from this default) were regenerated for the new portrait world.</para>
/// </summary>
public static class WorldSizeOptions
{
    /// <summary>The offered map sizes, smallest first; <see cref="DefaultSizeIndex"/> marks the default. Portrait (taller than wide) so the continent runs north–south like Colonization; Standard 40×100 is FreeCol's default.</summary>
    public static IReadOnlyList<WorldSize> Sizes { get; } =
    [
        new("Small", 30, 72),
        new("Standard", 40, 100),
        new("Large", 56, 140),
        new("Huge", 72, 180),
    ];

    /// <summary>The offered land amounts, least land first; <see cref="DefaultLandMassIndex"/> marks the default. Normal 25% is FreeCol's <c>model.option.landMass</c> default (its watery maps leave room to explore + sail).</summary>
    public static IReadOnlyList<LandMass> LandMasses { get; } =
    [
        new("Sparse", 0.18),
        new("Normal", 0.25), // FreeCol's default landMass (decoupled from Game.New's 45% test-fixture default)
        new("Dense", 0.40),
    ];

    /// <summary>
    /// The offered landmass styles (FreeCol <c>landGeneratorType</c>); <see cref="DefaultLandStyleIndex"/> marks the
    /// shipped default (<see cref="LandStyle.Continent"/> — the historical, byte-identical generator). Archipelago and
    /// Islands are opt-in alternatives that shape the land into separate masses.
    /// </summary>
    public static IReadOnlyList<LandStyleOption> LandStyles { get; } =
    [
        new("Continent", LandStyle.Continent),
        new("Archipelago", LandStyle.Archipelago),
        new("Islands", LandStyle.Islands),
    ];

    /// <summary>Index of the default size (Standard 40×100 portrait) in <see cref="Sizes"/>.</summary>
    public const int DefaultSizeIndex = 1;

    /// <summary>Index of the default land amount (Normal 25%) in <see cref="LandMasses"/>.</summary>
    public const int DefaultLandMassIndex = 1;

    /// <summary>Index of the shipped-default landmass style (Continent) in <see cref="LandStyles"/>.</summary>
    public const int DefaultLandStyleIndex = 0;

    /// <summary>The default map size (Standard 40×100 portrait) — what a default new game uses (FreeCol's map default).</summary>
    public static WorldSize DefaultSize => Sizes[DefaultSizeIndex];

    /// <summary>The default land amount (Normal 25%) — what a default new game uses (FreeCol's landMass default).</summary>
    public static LandMass DefaultLandMass => LandMasses[DefaultLandMassIndex];

    /// <summary>The shipped-default landmass style (Continent) — what the historical default game uses.</summary>
    public static LandStyleOption DefaultLandStyle => LandStyles[DefaultLandStyleIndex];
}
