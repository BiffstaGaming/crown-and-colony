using CrownAndColony.GameLogic.Specification;

namespace CrownAndColony.GameLogic.World;

/// <summary>The game world: a rectangular grid of tiles, each with a terrain type.</summary>
public sealed class GameMap
{
    private readonly TerrainType[] _terrain;
    private readonly Dictionary<Position, string> _resources;

    /// <summary>Creates a map from a row-major terrain array (length must be Width × Height).</summary>
    /// <param name="width">Map width in tiles.</param>
    /// <param name="height">Map height in tiles.</param>
    /// <param name="terrain">Row-major terrain per tile.</param>
    /// <param name="resources">Bonus resources by tile (sparse; null = none).</param>
    public GameMap(
        int width, int height, IReadOnlyList<TerrainType> terrain,
        IReadOnlyDictionary<Position, string>? resources = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (terrain.Count != width * height)
        {
            throw new ArgumentException(
                $"Terrain array length {terrain.Count} does not match {width}x{height} map.", nameof(terrain));
        }

        Width = width;
        Height = height;
        _terrain = [.. terrain];
        _resources = resources is null ? [] : new Dictionary<Position, string>(resources);
    }

    /// <summary>Map width in tiles.</summary>
    public int Width { get; }

    /// <summary>Map height in tiles.</summary>
    public int Height { get; }

    /// <summary>True when the position lies on the map.</summary>
    public bool InBounds(Position p) => p.X >= 0 && p.X < Width && p.Y >= 0 && p.Y < Height;

    /// <summary>The terrain at a position.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Position is off the map.</exception>
    public TerrainType TerrainAt(Position p) =>
        InBounds(p)
            ? _terrain[p.Y * Width + p.X]
            : throw new ArgumentOutOfRangeException(nameof(p), p, "Position is off the map.");

    /// <summary>The bonus resource on a tile (e.g. <c>model.resource.grain</c>), or null.</summary>
    public string? ResourceAt(Position p) => _resources.GetValueOrDefault(p);

    /// <summary>All tiles carrying a bonus resource.</summary>
    public IReadOnlyDictionary<Position, string> Resources => _resources;

    /// <summary>All positions on the map, row by row.</summary>
    public IEnumerable<Position> AllPositions()
    {
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                yield return new Position(x, y);
            }
        }
    }
}
