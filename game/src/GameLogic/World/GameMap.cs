using CrownAndColony.GameLogic.Specification;

namespace CrownAndColony.GameLogic.World;

/// <summary>The game world: a rectangular grid of tiles, each with a terrain type.</summary>
public sealed class GameMap
{
    private readonly TerrainType[] _terrain;
    private readonly Dictionary<Position, string> _resources;
    private readonly HashSet<Position> _rumours;
    private readonly Dictionary<Position, string> _nativeOwners = []; // tile → owning native nation type id (derived, not saved)
    private readonly HashSet<Position> _claimedFromNatives; // tiles bought/taken from the natives — a SAVED override the derivation honours

    /// <summary>Creates a map from a row-major terrain array (length must be Width × Height).</summary>
    /// <param name="width">Map width in tiles.</param>
    /// <param name="height">Map height in tiles.</param>
    /// <param name="terrain">Row-major terrain per tile.</param>
    /// <param name="resources">Bonus resources by tile (sparse; null = none).</param>
    /// <param name="rumours">Tiles holding a Lost City Rumour (sparse; null = none). Restored from the save here; placed at game start by the LCR generator.</param>
    /// <param name="claimedFromNatives">Tiles the player has bought or taken from the natives (sparse; null = none). Restored from the save here; the native-land claim re-derivation honours this override so a claimed tile never reverts to native ownership.</param>
    public GameMap(
        int width, int height, IReadOnlyList<TerrainType> terrain,
        IReadOnlyDictionary<Position, string>? resources = null,
        IReadOnlyCollection<Position>? rumours = null,
        IReadOnlyCollection<Position>? claimedFromNatives = null)
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
        _rumours = rumours is null ? [] : [.. rumours];
        _claimedFromNatives = claimedFromNatives is null ? [] : [.. claimedFromNatives];
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

    /// <summary>True when a tile holds an unexplored Lost City Rumour.</summary>
    public bool HasRumour(Position p) => _rumours.Contains(p);

    /// <summary>All tiles holding a Lost City Rumour (sparse).</summary>
    public IReadOnlyCollection<Position> Rumours => _rumours;

    /// <summary>Places a Lost City Rumour on a tile (game-start generation only).</summary>
    internal void AddRumour(Position p) => _rumours.Add(p);

    /// <summary>Removes a Lost City Rumour from a tile once it has been explored (one-shot).</summary>
    internal void RemoveRumour(Position p) => _rumours.Remove(p);

    /// <summary>True when a tile is claimed by a native nation (within a native settlement's claim radius).</summary>
    public bool IsNativeOwned(Position p) => _nativeOwners.ContainsKey(p);

    /// <summary>The native nation type id owning a tile (e.g. <c>model.nationType.apache</c>), or null if unclaimed.</summary>
    public string? NativeOwnerOf(Position p) => _nativeOwners.GetValueOrDefault(p);

    /// <summary>All tiles claimed by a native nation, keyed by tile (sparse). Derived at game start / on load, never saved.</summary>
    public IReadOnlyDictionary<Position, string> NativeOwners => _nativeOwners;

    /// <summary>Claims a tile for a native nation (derivation at game start / on load; later, a purchase transfer).</summary>
    internal void SetNativeOwner(Position p, string nationTypeId) => _nativeOwners[p] = nationTypeId;

    /// <summary>Releases a tile's native claim (land bought, taken, or the owning settlement gone).</summary>
    internal void ClearNativeOwner(Position p) => _nativeOwners.Remove(p);

    /// <summary>Drops every native land claim (before a full re-derivation when the settlements change).</summary>
    internal void ClearNativeOwners() => _nativeOwners.Clear();

    /// <summary>True when the player has bought or taken this tile from the natives (it stays un-native across re-derivation).</summary>
    public bool IsClaimedFromNatives(Position p) => _claimedFromNatives.Contains(p);

    /// <summary>All tiles the player has bought or taken from the natives (the SAVED override; sparse).</summary>
    public IReadOnlyCollection<Position> ClaimedFromNatives => _claimedFromNatives;

    /// <summary>Records a tile as claimed from the natives (bought or taken) and drops its current native claim.</summary>
    internal void ClaimFromNatives(Position p)
    {
        _claimedFromNatives.Add(p);
        _nativeOwners.Remove(p);
    }

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
