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
/// game continues with the identical future random sequence (ADR-009). Terrain is
/// stored as ruleset ids — the ruleset itself is not saved, so saves stay valid
/// across ruleset-irrelevant code changes but require the matching ruleset to load.
/// </summary>
public sealed record SaveGame
{
    /// <summary>Save format version; bump on breaking shape changes.</summary>
    public int Version { get; init; } = 1;

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
                .Select(u => new SavedUnit(u.Id, u.Position.X, u.Position.Y, u.MovementLeft))
                .ToList(),
        };
    }

    /// <summary>Reconstructs a running game from this snapshot.</summary>
    /// <exception cref="KeyNotFoundException">A saved terrain id is missing from the ruleset.</exception>
    public Game Restore(Ruleset ruleset)
    {
        var terrain = Terrain.Select(ruleset.Terrain).ToList();
        var map = new GameMap(MapWidth, MapHeight, terrain);
        return Game.Restore(
            ruleset,
            map,
            new RandomState(RandomStateValue, RandomIncrement),
            Turn,
            Units.Select(u => (u.Id, new Position(u.X, u.Y), u.MovementLeft)));
    }

    /// <summary>Serializes to JSON.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>Deserializes from JSON produced by <see cref="ToJson"/>.</summary>
    /// <exception cref="JsonException">The JSON is not a valid save.</exception>
    public static SaveGame FromJson(string json) =>
        JsonSerializer.Deserialize<SaveGame>(json, JsonOptions)
            ?? throw new JsonException("Save file deserialized to null.");
}

/// <summary>A unit inside a <see cref="SaveGame"/>.</summary>
/// <param name="Id">Unit id.</param>
/// <param name="X">Map column.</param>
/// <param name="Y">Map row.</param>
/// <param name="MovementLeft">Movement points remaining this turn.</param>
public sealed record SavedUnit(int Id, int X, int Y, int MovementLeft);
