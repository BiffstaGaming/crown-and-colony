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
/// mirror FreeCol (width 30–200, height 20–200, land 15–50%); every preset here stays inside them and keeps the map at
/// least as large as the generator's watery margin needs (≥ 30×20). The defaults (<see cref="DefaultSize"/> 36×24 +
/// <see cref="DefaultLandMass"/> 45%) match the shipped world, so a default new game is byte-identical (ADR-009) and
/// the visual goldens are untouched.
/// </summary>
public static class WorldSizeOptions
{
    /// <summary>The offered map sizes, smallest first; <see cref="DefaultSizeIndex"/> marks the shipped default.</summary>
    public static IReadOnlyList<WorldSize> Sizes { get; } =
    [
        new("Small", 30, 20),
        new("Standard", 36, 24),
        new("Large", 56, 38),
        new("Huge", 80, 52),
    ];

    /// <summary>The offered land amounts, least land first; <see cref="DefaultLandMassIndex"/> marks the shipped default.</summary>
    public static IReadOnlyList<LandMass> LandMasses { get; } =
    [
        new("Sparse", 0.30),
        new("Normal", MapGenerator.DefaultLandMassFraction), // 0.45 — the shipped default
        new("Dense", 0.50),
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

    /// <summary>Index of the shipped-default size (Standard 36×24) in <see cref="Sizes"/>.</summary>
    public const int DefaultSizeIndex = 1;

    /// <summary>Index of the shipped-default land amount (Normal 45%) in <see cref="LandMasses"/>.</summary>
    public const int DefaultLandMassIndex = 1;

    /// <summary>Index of the shipped-default landmass style (Continent) in <see cref="LandStyles"/>.</summary>
    public const int DefaultLandStyleIndex = 0;

    /// <summary>The shipped-default map size (Standard 36×24) — what the historical default game uses.</summary>
    public static WorldSize DefaultSize => Sizes[DefaultSizeIndex];

    /// <summary>The shipped-default land amount (Normal 45%) — what the historical default game uses.</summary>
    public static LandMass DefaultLandMass => LandMasses[DefaultLandMassIndex];

    /// <summary>The shipped-default landmass style (Continent) — what the historical default game uses.</summary>
    public static LandStyleOption DefaultLandStyle => LandStyles[DefaultLandStyleIndex];
}
