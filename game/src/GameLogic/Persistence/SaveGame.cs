using System.Text.Json;
using System.Text.Json.Serialization;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Randomness;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;

namespace CrownAndColony.GameLogic.Persistence;

/// <summary>
/// Serializable snapshot of a complete game, including the RNG state so a loaded
/// game continues with the identical future random sequence (ADR-009). Terrain and
/// unit types are stored as ruleset ids — the ruleset itself is not saved, so the
/// matching ruleset is required to load.
/// </summary>
public sealed record SaveGame
{
    /// <summary>Current save format version.</summary>
    public const int CurrentVersion = 5;

    /// <summary>
    /// Save format version. v1 lacked <see cref="Explored"/> and unit type ids;
    /// v2 lacked <see cref="Colonies"/>; v3 colonies lacked goods stores;
    /// v4 lacked tile workers.
    /// </summary>
    public int Version { get; init; } = CurrentVersion;

    /// <summary>Current turn number.</summary>
    public required int Turn { get; init; }

    /// <summary>RNG internal state word.</summary>
    public required ulong RandomStateValue { get; init; }

    /// <summary>RNG stream increment.</summary>
    public required ulong RandomIncrement { get; init; }

    /// <summary>Map width in tiles.</summary>
    public required int MapWidth { get; init; }

    /// <summary>Map height in tiles.</summary>
    public required int MapHeight { get; init; }

    /// <summary>Row-major terrain ids for every tile.</summary>
    public required IReadOnlyList<string> Terrain { get; init; }

    /// <summary>All units.</summary>
    public required IReadOnlyList<SavedUnit> Units { get; init; }

    /// <summary>
    /// Explored tile indexes (row-major <c>y * MapWidth + x</c>), fog of war.
    /// Null in v1 saves — loading reveals around units instead.
    /// </summary>
    public IReadOnlyList<int>? Explored { get; init; }

    /// <summary>All colonies. Null in pre-v3 saves (no colonies existed).</summary>
    public IReadOnlyList<SavedColony>? Colonies { get; init; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Captures the complete state of a running game.</summary>
    public static SaveGame From(Game game)
    {
        RandomState rng = game.RandomState;
        return new SaveGame
        {
            Turn = game.Turn,
            RandomStateValue = rng.State,
            RandomIncrement = rng.Increment,
            MapWidth = game.Map.Width,
            MapHeight = game.Map.Height,
            Terrain = game.Map.AllPositions().Select(p => game.Map.TerrainAt(p).Id).ToList(),
            Units = game.Units
                .Select(u => new SavedUnit(u.Id, u.Type.Id, u.Position.X, u.Position.Y, u.MovementLeft))
                .ToList(),
            Explored = game.Explored
                .Select(p => p.Y * game.Map.Width + p.X)
                .OrderBy(i => i)
                .ToList(),
            Colonies = game.Colonies
                .Select(c => new SavedColony(
                    c.Id, c.Name, c.Position.X, c.Position.Y, c.Population,
                    c.Stores.Count > 0 ? new Dictionary<string, int>(c.Stores) : null,
                    c.TileWorkers.Count > 0
                        ? c.TileWorkers.Select(w => new SavedWorker(w.Key.X, w.Key.Y, w.Value)).ToList()
                        : null))
                .ToList(),
        };
    }

    /// <summary>Reconstructs a running game from this snapshot.</summary>
    /// <exception cref="KeyNotFoundException">A saved terrain or unit type id is missing from the ruleset.</exception>
    public Game Restore(Ruleset ruleset)
    {
        var terrain = Terrain.Select(ruleset.Terrain).ToList();
        var map = new GameMap(MapWidth, MapHeight, terrain);
        return Game.Restore(
            ruleset,
            map,
            new RandomState(RandomStateValue, RandomIncrement),
            Turn,
            Units.Select(u => (
                u.Id,
                // v1 saves carry no type id; everything was effectively a colonist.
                ruleset.Unit(u.TypeId ?? Game.StartingUnitTypeId),
                new Position(u.X, u.Y),
                u.MovementLeft)),
            Explored?.Select(i => new Position(i % MapWidth, i / MapWidth)),
            Colonies?.Select(c =>
            {
                var colony = new CrownAndColony.GameLogic.Colonies.Colony(
                    c.Id, c.Name, new Position(c.X, c.Y), c.Population);
                foreach ((string goods, int amount) in
                         c.Stores ?? new Dictionary<string, int>())
                {
                    // Normalizes pre-v6 saves that stored raw grain/fish.
                    colony.AddGoods(ruleset.StorageIdOf(goods), amount);
                }
                foreach (SavedWorker worker in c.Workers ?? [])
                {
                    colony.SetWorker(new Position(worker.X, worker.Y), worker.GoodsId);
                }
                return colony;
            }));
    }

    /// <summary>Serializes to JSON.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>Deserializes from JSON produced by <see cref="ToJson"/>.</summary>
    /// <exception cref="JsonException">The JSON is not a valid save.</exception>
    public static SaveGame FromJson(string json) =>
        JsonSerializer.Deserialize<SaveGame>(json, JsonOptions)
            ?? throw new JsonException("Save file deserialized to null.");
}

/// <summary>A colony inside a <see cref="SaveGame"/>.</summary>
/// <param name="Id">Colony id.</param>
/// <param name="Name">Display name.</param>
/// <param name="X">Map column.</param>
/// <param name="Y">Map row.</param>
/// <param name="Population">Colonists living in the colony.</param>
/// <param name="Stores">Warehouse contents by goods id (null in pre-v4 saves / when empty).</param>
/// <param name="Workers">Tile work assignments (null in pre-v5 saves / when none).</param>
public sealed record SavedColony(
    int Id, string Name, int X, int Y, int Population,
    IReadOnlyDictionary<string, int>? Stores = null,
    IReadOnlyList<SavedWorker>? Workers = null);

/// <summary>A colonist's tile assignment inside a <see cref="SavedColony"/>.</summary>
/// <param name="X">Worked tile column.</param>
/// <param name="Y">Worked tile row.</param>
/// <param name="GoodsId">Goods being produced there.</param>
public sealed record SavedWorker(int X, int Y, string GoodsId);

/// <summary>A unit inside a <see cref="SaveGame"/>.</summary>
/// <param name="Id">Unit id.</param>
/// <param name="TypeId">Ruleset unit type id (null in v1 saves → free colonist).</param>
/// <param name="X">Map column.</param>
/// <param name="Y">Map row.</param>
/// <param name="MovementLeft">Movement points remaining this turn.</param>
public sealed record SavedUnit(int Id, string? TypeId, int X, int Y, int MovementLeft);
