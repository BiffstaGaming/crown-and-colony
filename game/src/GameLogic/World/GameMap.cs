using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.World.Improvements;

namespace CrownAndColony.GameLogic.World;

/// <summary>The game world: a rectangular grid of tiles, each with a terrain type.</summary>
public sealed class GameMap
{
    private readonly TerrainType[] _terrain;
    private readonly Dictionary<Position, string> _resources;
    private readonly Dictionary<Position, int> _resourceQuantities; // tile → a finite resource's remaining quantity (sparse; absent = limitless / no range)
    private readonly Dictionary<Position, List<TileImprovementType>> _improvements; // tile → the improvements on it (sparse; a tile may carry several — e.g. a river + a road + a plow)
    private readonly HashSet<Position> _rumours;
    private readonly Dictionary<Position, string> _nativeOwners = []; // tile → owning native nation type id (derived, not saved)
    private readonly HashSet<Position> _claimedFromNatives; // tiles bought/taken from the natives — a SAVED override the derivation honours
    private int[]? _regionIds; // dense row-major region id per tile (index y*Width+x); null until assigned, NoRegion = unassigned tile
    private List<Region> _regions = []; // the region table, indexed by region id (Regions[i].Id == i)

    /// <summary>Creates a map from a row-major terrain array (length must be Width × Height).</summary>
    /// <param name="width">Map width in tiles.</param>
    /// <param name="height">Map height in tiles.</param>
    /// <param name="terrain">Row-major terrain per tile.</param>
    /// <param name="resources">Bonus resources by tile (sparse; null = none).</param>
    /// <param name="rumours">Tiles holding a Lost City Rumour (sparse; null = none). Restored from the save here; placed at game start by the LCR generator.</param>
    /// <param name="claimedFromNatives">Tiles the player has bought or taken from the natives (sparse; null = none). Restored from the save here; the native-land claim re-derivation honours this override so a claimed tile never reverts to native ownership.</param>
    /// <param name="regionIds">Row-major region id per tile (length Width × Height; null = no region layer yet). Restored from a v35+ save; otherwise the region generator re-derives it after construction.</param>
    /// <param name="regions">The region table indexed by region id (null = none). Restored alongside <paramref name="regionIds"/>.</param>
    /// <param name="resourceQuantities">A finite resource's remaining quantity by tile (sparse; null = none). Restored from a v46+ save; only finite (min/max-ranged) resources carry one.</param>
    /// <param name="improvements">The tile improvements on each tile (sparse; null = none) — a tile may carry several (a river plus a pioneer-built road/plow). Restored from a v47+ save; rivers are stamped at game start by the map generator, roads/plows/clearings by pioneers. A pre-v47 save has none.</param>
    public GameMap(
        int width, int height, IReadOnlyList<TerrainType> terrain,
        IReadOnlyDictionary<Position, string>? resources = null,
        IReadOnlyCollection<Position>? rumours = null,
        IReadOnlyCollection<Position>? claimedFromNatives = null,
        IReadOnlyList<int>? regionIds = null,
        IReadOnlyList<Region>? regions = null,
        IReadOnlyDictionary<Position, int>? resourceQuantities = null,
        IReadOnlyDictionary<Position, IReadOnlyList<TileImprovementType>>? improvements = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (terrain.Count != width * height)
        {
            throw new ArgumentException(
                $"Terrain array length {terrain.Count} does not match {width}x{height} map.", nameof(terrain));
        }
        if (regionIds is not null && regionIds.Count != width * height)
        {
            throw new ArgumentException(
                $"Region id array length {regionIds.Count} does not match {width}x{height} map.", nameof(regionIds));
        }

        Width = width;
        Height = height;
        _terrain = [.. terrain];
        _resources = resources is null ? [] : new Dictionary<Position, string>(resources);
        _resourceQuantities = resourceQuantities is null ? [] : new Dictionary<Position, int>(resourceQuantities);
        _improvements = improvements is null
            ? []
            : improvements.Where(kv => kv.Value.Count > 0).ToDictionary(kv => kv.Key, kv => kv.Value.ToList());
        _rumours = rumours is null ? [] : [.. rumours];
        _claimedFromNatives = claimedFromNatives is null ? [] : [.. claimedFromNatives];
        _regionIds = regionIds is null ? null : [.. regionIds];
        _regions = regions is null ? [] : [.. regions];
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

    /// <summary>Places a bonus resource on a tile (gen-time placement is via the ctor; this serves scenario setup/tests).</summary>
    internal void SetResource(Position p, string resourceId) => _resources[p] = resourceId;

    /// <summary>Removes the bonus resource on a tile (and any stored quantity) — the tile reverts to its bare yield (FreeCol <c>Tile.removeResource</c> when a finite deposit is exhausted).</summary>
    internal void RemoveResource(Position p)
    {
        _resources.Remove(p);
        _resourceQuantities.Remove(p);
    }

    /// <summary>The finite remaining quantity of the resource on a tile (FreeCol <c>Resource.quantity</c>), or null when the tile has no resource or a limitless one (no min/max range).</summary>
    public int? ResourceQuantityAt(Position p) => _resourceQuantities.TryGetValue(p, out int q) ? q : null;

    /// <summary>Every tile carrying a finite resource quantity (sparse — a limitless resource is absent).</summary>
    public IReadOnlyDictionary<Position, int> ResourceQuantities => _resourceQuantities;

    /// <summary>Records (or clears, when <paramref name="quantity"/> is null) a finite resource's quantity on a tile (gen-time rolling + save restore).</summary>
    internal void SetResourceQuantity(Position p, int? quantity)
    {
        if (quantity is { } q)
        {
            _resourceQuantities[p] = q;
        }
        else
        {
            _resourceQuantities.Remove(p);
        }
    }

    private static readonly IReadOnlyList<TileImprovementType> NoImprovements = [];

    /// <summary>All tile improvements on a tile (sparse; a tile may carry a river plus a pioneer-built road/plow), or an empty list when it has none.</summary>
    public IReadOnlyList<TileImprovementType> ImprovementsAt(Position p) =>
        _improvements.TryGetValue(p, out List<TileImprovementType>? list) ? list : NoImprovements;

    /// <summary>The improvement of a given type id on a tile (e.g. its river or its road), or null when the tile carries no such improvement.</summary>
    public TileImprovementType? ImprovementOfType(Position p, string improvementId) =>
        _improvements.TryGetValue(p, out List<TileImprovementType>? list)
            ? list.FirstOrDefault(i => i.Id == improvementId)
            : null;

    /// <summary>True when a tile carries an improvement of the given type id.</summary>
    public bool HasImprovement(Position p, string improvementId) =>
        ImprovementOfType(p, improvementId) is not null;

    /// <summary>The river improvement on a tile (FreeCol <c>model.improvement.river</c>), or null when the tile has none.</summary>
    public TileImprovementType? RiverAt(Position p) => ImprovementOfType(p, TileImprovementType.RiverId);

    /// <summary>True when a tile carries a river improvement (FreeCol <c>Tile.hasRiver</c>).</summary>
    public bool HasRiver(Position p) => HasImprovement(p, TileImprovementType.RiverId);

    /// <summary>Every (tile, improvement) pair on the map (sparse; a tile with several improvements yields several pairs). For persistence + enumeration.</summary>
    public IEnumerable<(Position Position, TileImprovementType Improvement)> AllImprovements() =>
        _improvements.SelectMany(kv => kv.Value.Select(i => (kv.Key, i)));

    /// <summary>
    /// Adds (or, when an improvement of the same type id is already present, replaces) an improvement on a tile —
    /// gen-time river stamping, pioneer-built road/plow placement, and save restore. A tile may hold several
    /// improvements of different types (a river and a road); only one of each type.
    /// </summary>
    internal void AddImprovement(Position p, TileImprovementType improvement)
    {
        if (!_improvements.TryGetValue(p, out List<TileImprovementType>? list))
        {
            _improvements[p] = list = [];
        }
        list.RemoveAll(i => i.Id == improvement.Id); // one of each type per tile (re-stamp wins)
        list.Add(improvement);
    }

    /// <summary>Removes the improvement of a given type id from a tile (e.g. a forest cleared away), if present.</summary>
    internal void RemoveImprovement(Position p, string improvementId)
    {
        if (_improvements.TryGetValue(p, out List<TileImprovementType>? list))
        {
            list.RemoveAll(i => i.Id == improvementId);
            if (list.Count == 0)
            {
                _improvements.Remove(p);
            }
        }
    }

    /// <summary>Places (or, when <paramref name="improvement"/> is null, clears all improvements on) a tile — a convenience over <see cref="AddImprovement"/> used by gen-time/test stamping of a single improvement.</summary>
    internal void SetImprovement(Position p, TileImprovementType? improvement)
    {
        if (improvement is { } imp)
        {
            AddImprovement(p, imp);
        }
        else
        {
            _improvements.Remove(p);
        }
    }

    /// <summary>Changes a tile's terrain type (FreeCol <c>Tile.changeType</c>) — a pioneer clearing a forest to its base type. Improvements on the tile are left in place (a river survives the clearing).</summary>
    internal void SetTerrain(Position p, TerrainType terrain)
    {
        if (!InBounds(p))
        {
            throw new ArgumentOutOfRangeException(nameof(p), p, "Position is off the map.");
        }
        _terrain[p.Y * Width + p.X] = terrain;
    }

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

    /// <summary>Sentinel returned by <see cref="RegionIdAt"/> for a tile with no region (no layer yet, or unassigned).</summary>
    public const int NoRegion = -1;

    /// <summary>The region table, indexed by region id (empty until the region generator runs).</summary>
    public IReadOnlyList<Region> Regions => _regions;

    /// <summary>
    /// The region id of a tile, or <see cref="NoRegion"/> when the map has no region layer yet or the tile is
    /// unassigned. Resolve to the <see cref="Region"/> itself via <see cref="RegionOf"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Position is off the map.</exception>
    public int RegionIdAt(Position p) =>
        InBounds(p)
            ? _regionIds is null ? NoRegion : _regionIds[p.Y * Width + p.X]
            : throw new ArgumentOutOfRangeException(nameof(p), p, "Position is off the map.");

    /// <summary>The region a tile belongs to, or null when it has none (see <see cref="RegionIdAt"/>).</summary>
    /// <exception cref="ArgumentOutOfRangeException">Position is off the map.</exception>
    public Region? RegionOf(Position p)
    {
        int id = RegionIdAt(p);
        return id >= 0 && id < _regions.Count ? _regions[id] : null;
    }

    /// <summary>The region in the table with the given id, or null if out of range.</summary>
    public Region? RegionById(int id) => id >= 0 && id < _regions.Count ? _regions[id] : null;

    /// <summary>
    /// The discoverable region a tile contributes to (FreeCol <c>Region.getDiscoverableRegion</c>): the tile's own
    /// region if it is <see cref="Region.IsDiscoverable"/>, otherwise the nearest discoverable ancestor walked up the
    /// <see cref="Region.ParentId"/> chain (an ocean leaf quadrant resolves to its discoverable Pacific parent), or
    /// null if neither the tile's region nor any ancestor is discoverable. A region already discovered is no longer
    /// discoverable, so this returns null once its discoverable ancestor has been found.
    /// </summary>
    public Region? DiscoverableRegionOf(Position p)
    {
        Region? region = RegionOf(p);
        while (region is not null)
        {
            if (region.IsDiscoverable)
            {
                return region;
            }
            region = region.ParentId is { } parent ? RegionById(parent) : null;
        }
        return null;
    }

    /// <summary>
    /// Replaces the region at <paramref name="region"/>'s id in the table (the discovery rules stamp the
    /// discoverer/name/turn this way). The region's <see cref="Region.Id"/> must already index the table.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The region's id is outside the current table.</exception>
    internal void UpdateRegion(Region region)
    {
        if (region.Id < 0 || region.Id >= _regions.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(region), region.Id, "Region id is outside the table.");
        }
        _regions[region.Id] = region;
    }

    /// <summary>
    /// Installs the region layer computed by the region generator (game-start generation and the on-load
    /// re-derivation). <paramref name="regionIds"/> is row-major (length Width × Height); <paramref name="regions"/>
    /// is indexed by region id.
    /// </summary>
    internal void SetRegions(int[] regionIds, IReadOnlyList<Region> regions)
    {
        if (regionIds.Length != Width * Height)
        {
            throw new ArgumentException(
                $"Region id array length {regionIds.Length} does not match {Width}x{Height} map.", nameof(regionIds));
        }
        _regionIds = regionIds;
        _regions = [.. regions];
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
