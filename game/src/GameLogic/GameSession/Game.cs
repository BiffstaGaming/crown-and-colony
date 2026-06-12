using CrownAndColony.GameLogic.Randomness;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;

namespace CrownAndColony.GameLogic.GameSession;

/// <summary>
/// One running game: the map, the units, and the turn counter. All mutations of
/// game state go through methods on this class so rules are enforced in one place.
/// </summary>
public sealed class Game
{
    private readonly List<Unit> _units = [];
    private readonly Pcg32Random _random;
    private int _nextUnitId = 1;

    private Game(Ruleset ruleset, GameMap map, Pcg32Random random, int turn)
    {
        Ruleset = ruleset;
        Map = map;
        _random = random;
        Turn = turn;
    }

    /// <summary>The rule data this game plays by.</summary>
    public Ruleset Ruleset { get; }

    /// <summary>The game world.</summary>
    public GameMap Map { get; }

    /// <summary>Current turn number, starting at 1.</summary>
    public int Turn { get; private set; }

    /// <summary>All units in the game.</summary>
    public IReadOnlyList<Unit> Units => _units;

    /// <summary>
    /// Starts a new game: generates a map from the seed and places one starting
    /// unit on the first settleable land tile.
    /// </summary>
    public static Game New(Ruleset ruleset, ulong seed, int mapWidth = 24, int mapHeight = 16)
    {
        var random = new Pcg32Random(seed);
        GameMap map = MapGenerator.Generate(ruleset, mapWidth, mapHeight, random);

        var game = new Game(ruleset, map, random, turn: 1);

        Position start = map.AllPositions().First(p =>
        {
            TerrainType t = map.TerrainAt(p);
            return !t.IsWater && t.CanSettle;
        });
        game.SpawnUnit(start);

        return game;
    }

    /// <summary>Restores a game from saved state (see <see cref="Persistence.SaveGame"/>).</summary>
    internal static Game Restore(
        Ruleset ruleset, GameMap map, RandomState randomState, int turn,
        IEnumerable<(int id, Position position, int movementLeft)> units)
    {
        var game = new Game(ruleset, map, Pcg32Random.FromState(randomState), turn);
        foreach ((int id, Position position, int movementLeft) in units)
        {
            var unit = new Unit(id, position) { MovementLeft = movementLeft };
            game._units.Add(unit);
            game._nextUnitId = Math.Max(game._nextUnitId, id + 1);
        }
        return game;
    }

    /// <summary>The game's RNG state, captured for saving.</summary>
    internal RandomState RandomState => _random.SaveState();

    /// <summary>Creates a new unit at a position (skeleton: used for the starting unit).</summary>
    public Unit SpawnUnit(Position position)
    {
        if (!Map.InBounds(position))
        {
            throw new ArgumentOutOfRangeException(nameof(position), position, "Off the map.");
        }
        if (Map.TerrainAt(position).IsWater)
        {
            throw new InvalidMoveException("Land units cannot be placed on water.");
        }

        var unit = new Unit(_nextUnitId++, position);
        _units.Add(unit);
        return unit;
    }

    /// <summary>
    /// Whether <paramref name="unit"/> may move to <paramref name="target"/> right now,
    /// and why not if not. Movement rules (skeleton): one step to an adjacent on-map
    /// land tile, requiring at least 1 movement point remaining.
    /// </summary>
    public MoveCheck CheckMove(Unit unit, Position target)
    {
        if (!Map.InBounds(target))
        {
            return MoveCheck.No("Destination is off the map.");
        }
        if (!unit.Position.IsAdjacentTo(target))
        {
            return MoveCheck.No("Units move one tile at a time.");
        }
        if (Map.TerrainAt(target).IsWater)
        {
            return MoveCheck.No("Land units cannot enter water.");
        }
        if (unit.MovementLeft <= 0)
        {
            return MoveCheck.No("No movement left this turn.");
        }
        return MoveCheck.Yes(Map.TerrainAt(target).MoveCost);
    }

    /// <summary>
    /// Moves a unit one tile. Costs the target terrain's move cost (a unit with any
    /// movement remaining may always make one move; the cost may overdraw to 0 —
    /// pending FreeCol cross-check, see docs/systems/units-movement.md).
    /// </summary>
    /// <exception cref="InvalidMoveException">The move is not allowed; see <see cref="CheckMove"/>.</exception>
    public void MoveUnit(Unit unit, Position target)
    {
        MoveCheck check = CheckMove(unit, target);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }

        unit.Position = target;
        unit.MovementLeft = Math.Max(0, unit.MovementLeft - check.Cost);
    }

    /// <summary>Ends the current turn: all units regain movement, the turn counter advances.</summary>
    public void EndTurn()
    {
        foreach (Unit unit in _units)
        {
            unit.ResetMovement();
        }
        Turn++;
    }
}

/// <summary>Result of a move legality check.</summary>
/// <param name="Allowed">Whether the move may be made.</param>
/// <param name="Cost">Movement points the move would cost (when allowed).</param>
/// <param name="Reason">Why the move is rejected (when not allowed).</param>
public readonly record struct MoveCheck(bool Allowed, int Cost, string? Reason)
{
    /// <summary>An allowed move with the given cost.</summary>
    public static MoveCheck Yes(int cost) => new(true, cost, null);

    /// <summary>A rejected move with the reason shown to the player.</summary>
    public static MoveCheck No(string reason) => new(false, 0, reason);
}

/// <summary>Thrown when an illegal move is attempted directly (UI should use CheckMove first).</summary>
public sealed class InvalidMoveException : Exception
{
    /// <summary>Creates the exception with the player-facing reason.</summary>
    public InvalidMoveException(string message) : base(message) { }
}
