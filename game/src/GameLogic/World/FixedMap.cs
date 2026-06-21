using System.IO;
using System.Reflection;
using CrownAndColony.GameLogic.Specification;

namespace CrownAndColony.GameLogic.World;

/// <summary>Which map a new game is built on: a procedurally generated New World, or a fixed scenario map.</summary>
public enum MapSource
{
    /// <summary>A procedurally generated random New World — the default (see <see cref="MapGenerator"/>).</summary>
    Random,

    /// <summary>FreeCol's hand-made map of the Americas (a fixed terrain grid, 40×180).</summary>
    America,
}

/// <summary>
/// Loads a fixed scenario map (its terrain grid) from an embedded resource — FreeCol's hand-made America map,
/// extracted from <c>data/maps/M_America_Mazim.fsm</c> (GPL v2). <b>Only the terrain layer is loaded</b>: rivers,
/// bonus resources, native settlements and the player's start are placed on top by the usual game-start generators
/// (so a fixed map plays with our gameplay layers — the real America landmass, our systems). Engine-free (ADR-006);
/// loading is deterministic (the embedded bytes never change).
/// </summary>
public static class FixedMap
{
    /// <summary>Manifest resource name of the embedded America terrain grid (see <c>GameLogic.csproj</c>).</summary>
    private const string AmericaResource = "CrownAndColony.GameLogic.Maps.america.txt";

    /// <summary>The terrain-only <see cref="GameMap"/> for a fixed map source (or null for <see cref="MapSource.Random"/>, which the caller generates instead).</summary>
    /// <param name="source">The chosen map source.</param>
    /// <param name="ruleset">The ruleset whose <see cref="TerrainType"/>s the tile ids resolve against.</param>
    public static GameMap? TryLoad(MapSource source, Ruleset ruleset) =>
        source == MapSource.America ? LoadAmerica(ruleset) : null;

    /// <summary>Builds the America <see cref="GameMap"/> (FreeCol's M_America, 40×180) from the embedded terrain grid.</summary>
    /// <param name="ruleset">The ruleset whose <see cref="TerrainType"/>s the tile ids resolve against.</param>
    public static GameMap LoadAmerica(Ruleset ruleset) => Load(AmericaResource, ruleset);

    /// <summary>
    /// Parses an embedded terrain-grid resource into a <see cref="GameMap"/>. Format: a header line <c>WIDTH HEIGHT</c>,
    /// then <c>HEIGHT</c> rows of <c>WIDTH</c> terrain short names (row-major; e.g. <c>plains</c> → <c>model.tile.plains</c>).
    /// </summary>
    private static GameMap Load(string resourceName, Ruleset ruleset)
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        using Stream stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded map '{resourceName}' is missing from the assembly.");
        using var reader = new StreamReader(stream);

        string header = reader.ReadLine()
            ?? throw new InvalidDataException($"Map '{resourceName}' is empty.");
        string[] dims = header.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
        if (dims.Length != 2 || !int.TryParse(dims[0], out int width) || !int.TryParse(dims[1], out int height)
            || width <= 0 || height <= 0)
        {
            throw new InvalidDataException($"Map '{resourceName}' header must be 'WIDTH HEIGHT', got '{header}'.");
        }

        var terrain = new TerrainType[width * height];
        for (int y = 0; y < height; y++)
        {
            string row = reader.ReadLine()
                ?? throw new InvalidDataException($"Map '{resourceName}' ended at row {y}; expected {height} rows.");
            string[] cells = row.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            if (cells.Length != width)
            {
                throw new InvalidDataException($"Map '{resourceName}' row {y} has {cells.Length} tiles; expected {width}.");
            }
            for (int x = 0; x < width; x++)
            {
                terrain[(y * width) + x] = ruleset.Terrain($"model.tile.{cells[x]}");
            }
        }
        return new GameMap(width, height, terrain);
    }
}
