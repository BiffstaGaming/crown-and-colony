using CrownAndColony.GameLogic.Randomness;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;

namespace CrownAndColony.GameLogic.GameSession;

/// <summary>
/// One running game: the map, the units, exploration state, and the turn counter.
/// All mutations of game state go through methods on this class so rules are
/// enforced in one place.
/// </summary>
public sealed class Game
{
    /// <summary>The starting unit's type for a new game.</summary>
    public const string StartingUnitTypeId = "model.unit.freeColonist";

    private readonly List<Unit> _units = [];
    private readonly HashSet<Position> _explored = [];
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

    /// <summary>Tiles the player has seen (fog of war).</summary>
    public IReadOnlySet<Position> Explored => _explored;

    /// <summary>Whether a tile has been revealed.</summary>
    public bool IsExplored(Position p) => _explored.Contains(p);

    /// <summary>
    /// Starts a new game: generates a map from the seed and places one starting
    /// colonist on the first settleable land tile, revealing its surroundings.
    /// </summary>
    public static Game New(Ruleset ruleset, ulong seed, int mapWidth = 36, int mapHeight = 24)
    {
        var random = new Pcg32Random(seed);
        GameMap map = MapGenerator.Generate(ruleset, mapWidth, mapHeight, random);

        var game = new Game(ruleset, map, random, turn: 1);

        // Start on settleable land that has somewhere to walk to (not a 1-tile islet).
        bool Settleable(Position p)
        {
            TerrainType t = map.TerrainAt(p);
            return !t.IsWater && t.CanSettle;
        }
        Position start = map.AllPositions()
            .Where(Settleable)
            .FirstOrDefault(
                p => p.Neighbours().Any(n => map.InBounds(n) && !map.TerrainAt(n).IsWater),
                map.AllPositions().First(Settleable));
        game.SpawnUnit(ruleset.Unit(StartingUnitTypeId), start);

        return game;
    }

    /// <summary>Restores a game from saved state (see <see cref="Persistence.SaveGame"/>).</summary>
    internal static Game Restore(
        Ruleset ruleset, GameMap map, RandomState randomState, int turn,
        IEnumerable<(int id, UnitType type, Position position, int movementLeft)> units,
        IEnumerable<Position>? explored)
    {
        var game = new Game(ruleset, map, Pcg32Random.FromState(randomState), turn);
        foreach ((int id, UnitType type, Position position, int movementLeft) in units)
        {
            var unit = new Unit(id, type, position) { MovementLeft = movementLeft };
            game._units.Add(unit);
            game._nextUnitId = Math.Max(game._nextUnitId, id + 1);
        }

        if (explored is not null)
        {
            game._explored.UnionWith(explored.Where(map.InBounds));
        }
        else
        {
            // Pre-fog save (format v1): reveal around the units we have.
            foreach (Unit unit in game._units)
            {
                game.Reveal(unit);
            }
        }
        return game;
    }

    /// <summary>The game's RNG state, captured for saving.</summary>
    internal RandomState RandomState => _random.SaveState();

    /// <summary>Creates a new unit at a position and reveals its surroundings.</summary>
    public Unit SpawnUnit(UnitType type, Position position)
    {
        if (!Map.InBounds(position))
        {
            throw new ArgumentOutOfRangeException(nameof(position), position, "Off the map.");
        }
        if (Map.TerrainAt(position).IsWater != type.IsNaval)
        {
            throw new InvalidMoveException(type.IsNaval
                ? "Naval units must be placed on water."
                : "Land units cannot be placed on water.");
        }

        var unit = new Unit(_nextUnitId++, type, position);
        _units.Add(unit);
        Reveal(unit);
        return unit;
    }

    /// <summary>
    /// Whether <paramref name="unit"/> may move to <paramref name="target"/> right now,
    /// and why not if not.
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

        TerrainType terrain = Map.TerrainAt(target);
        if (terrain.IsWater && !unit.Type.IsNaval)
        {
            return MoveCheck.No("Land units cannot enter water.");
        }
        if (!terrain.IsWater && unit.Type.IsNaval)
        {
            return MoveCheck.No("Ships cannot move onto land.");
        }

        int movesLeft = unit.MovementLeft;
        if (movesLeft <= 0)
        {
            return MoveCheck.No("No movement left this turn.");
        }

        // FreeCol's partial-movement rule (Unit.getMoveCost): when the terrain
        // costs more than the unit has left, the move is still allowed — for the
        // full remainder — only if the unit is near full movement (lost at most
        // 2/3 of a move) or the shortfall is small. (A settlement target also
        // qualifies; none exist yet.) Otherwise the unit must wait.
        int cost = terrain.MoveCost;
        if (cost > movesLeft)
        {
            bool allowed = movesLeft + 2 >= unit.Type.Movement || cost <= movesLeft + 2;
            if (!allowed)
            {
                return MoveCheck.No("Not enough movement left this turn.");
            }
            cost = movesLeft;
        }
        return MoveCheck.Yes(cost);
    }

    /// <summary>Moves a unit one tile, spending movement and revealing new ground.</summary>
    /// <exception cref="InvalidMoveException">The move is not allowed; see <see cref="CheckMove"/>.</exception>
    public void MoveUnit(Unit unit, Position target)
    {
        MoveCheck check = CheckMove(unit, target);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }

        unit.Position = target;
        unit.MovementLeft -= check.Cost;
        Reveal(unit);
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

    /// <summary>Reveals all tiles within the unit's line of sight.</summary>
    private void Reveal(Unit unit)
    {
        int r = unit.Type.LineOfSight;
        for (int dy = -r; dy <= r; dy++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                var p = new Position(unit.Position.X + dx, unit.Position.Y + dy);
                if (Map.InBounds(p))
                {
                    _explored.Add(p);
                }
            }
        }
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
