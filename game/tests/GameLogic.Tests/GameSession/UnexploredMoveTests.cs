using System.Linq;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// Moving a land unit into a tile that was still unexplored (for the mover's owner) ends its turn — the classic
/// "the wilderness uses up your turn" rule (task 86d3fpxm5). FreeCol's pathfinder treats an unexplored tile as
/// impassable to a planned route (<c>BaseCostDecider</c>: a not-yet-explored target is <c>ILLEGAL_MOVE</c>), so the
/// only way into the black is a manual single step, and that step spends all remaining movement. A step onto a tile
/// that was already explored costs only the normal terrain move. Verified against FreeCol movement behaviour.
/// </summary>
public class UnexploredMoveTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    private const string Scout = "model.unit.freeColonist"; // 1 move; plains move-cost is one full move

    /// <summary>
    /// A 5×1 row of plains with a free colonist at (0,0) holding a full move allowance (3 FreeCol units), and the
    /// human player's explored set seeded with <paramref name="exploredXs"/> (row-major on a 5-wide map).
    /// </summary>
    private static Game RowWithExplored(params int[] exploredXs)
    {
        var save = new SaveGame
        {
            Turn = 1,
            RandomStateValue = 1,
            RandomIncrement = 1,
            MapWidth = 5,
            MapHeight = 1,
            Terrain = [.. Enumerable.Repeat("model.tile.plains", 5)],
            Units = [new SavedUnit(1, Scout, 0, 0, 3)],
            Explored = exploredXs,
        };
        return save.Restore(Classic);
    }

    private static Unit TheUnit(Game game) => game.Units.First(u => u.Id == 1);

    [Fact]
    public void StepIntoExploredTile_CostsOnlyTerrainMove()
    {
        // (1,0) is already explored and the unit has a generous allowance → the step spends just the plains
        // move-cost (3), leaving the rest (this is the control: an explored step is NOT a forced end-of-turn).
        Game game = RowWithExplored(0, 1, 2);
        Unit unit = TheUnit(game);
        unit.MovementLeft = 12;

        game.MoveUnit(unit, new Position(1, 0));

        Assert.Equal(new Position(1, 0), unit.Position);
        Assert.Equal(9, unit.MovementLeft); // 12 − 3 (terrain cost), not zeroed
    }

    [Fact]
    public void StepIntoUnexploredTile_EndsTheTurn()
    {
        // Only the start tile is explored; (1,0) is still black → stepping in ends the turn (movement → 0).
        Game game = RowWithExplored(0);
        Unit unit = TheUnit(game);

        game.MoveUnit(unit, new Position(1, 0));

        Assert.Equal(new Position(1, 0), unit.Position);
        Assert.Equal(0, unit.MovementLeft); // all remaining movement spent
    }

    [Fact]
    public void StepIntoUnexploredTile_EndsTurnEvenWithMovesToSpare()
    {
        // A scout-grade allowance (give the unit 12 units = 4 moves). Stepping into the unknown still zeroes it all,
        // proving the rule spends ALL remaining movement, not just the terrain cost.
        Game game = RowWithExplored(0);
        Unit unit = TheUnit(game);
        unit.MovementLeft = 12;

        game.MoveUnit(unit, new Position(1, 0)); // (1,0) unexplored

        Assert.Equal(0, unit.MovementLeft);
    }

    [Fact]
    public void ExploredStep_LeavesMovesForAFurtherMove()
    {
        // With a 12-unit allowance and (1,0) explored, the explored step costs only 3 → 9 left for more movement.
        Game game = RowWithExplored(0, 1);
        Unit unit = TheUnit(game);
        unit.MovementLeft = 12;

        game.MoveUnit(unit, new Position(1, 0)); // explored → normal cost

        Assert.Equal(9, unit.MovementLeft);
    }
}
