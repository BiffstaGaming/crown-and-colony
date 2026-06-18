using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// Goto / multi-turn move orders + unit cycling (<c>86d3c9pfy</c>) — slice 1: the <see cref="Unit.Destination"/>
/// standing order, its v36 persistence, and the manual-move clear. Pathfinding/advance/cycling land in later slices.
/// </summary>
public class GotoTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    private static Unit LandUnit(Game game) =>
        game.PlayerUnits.First(u => u.IsOnMap && !u.Type.IsNaval);

    private static Position AdjacentLand(Game game, Position from) =>
        from.Neighbours().First(n => game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater);

    [Fact]
    public void ManualMove_ClearsAStandingDestination()
    {
        var game = Game.New(Classic, seed: 99);
        Unit unit = LandUnit(game);
        unit.Destination = new Position(20, 20); // a standing goto (set directly; the SetDestination oracle lands in slice 3)
        Assert.True(unit.IsGoingTo);

        game.MoveUnit(unit, AdjacentLand(game, unit.Position));

        Assert.Null(unit.Destination); // a manual move cancels the goto (FreeCol setDestination(null))
        Assert.False(unit.IsGoingTo);
    }

    [Fact]
    public void SaveRoundTrip_PreservesADestination()
    {
        var game = Game.New(Classic, seed: 99);
        Unit unit = LandUnit(game);
        var dest = new Position(15, 11);
        unit.Destination = dest;

        Game loaded = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);

        Assert.Equal(dest, loaded.Units.First(u => u.Id == unit.Id).Destination);
    }

    [Fact]
    public void NoGoto_OmitsDestinationTokens()
    {
        // A fresh game has no unit with a goto, so the save carries no DestX/DestY tokens (byte-identical to v35).
        string json = SaveGame.From(Game.New(Classic, seed: 5)).ToJson();

        Assert.DoesNotContain("\"DestX\"", json);
        Assert.DoesNotContain("\"DestY\"", json);
    }

    [Fact]
    public void PreV36Save_LoadsWithNoDestination()
    {
        // A pre-v36 save carries no destination fields; loading must default every unit to no goto.
        SaveGame full = SaveGame.From(Game.New(Classic, seed: 5));
        SaveGame v35 = full with
        {
            Version = 35,
            Units = full.Units.Select(u => u with { DestX = null, DestY = null }).ToList(),
        };

        Game loaded = SaveGame.FromJson(v35.ToJson()).Restore(Classic);

        Assert.All(loaded.Units, u => Assert.Null(u.Destination));
    }

    // ── Set/clear destination oracle (slice 3) ───────────────────────────────────────────────────────────

    [Fact]
    public void CheckSetDestination_RejectsInvalidTargets_AndAllowsAReachableTile()
    {
        var game = Game.New(Classic, seed: 99);
        Unit unit = LandUnit(game);

        Assert.False(game.CheckSetDestination(unit, new Position(-1, 0)).Allowed);   // off the map
        Assert.False(game.CheckSetDestination(unit, unit.Position).Allowed);         // already there
        Assert.False(game.CheckSetDestination(unit, new Position(35, 23)).Allowed);  // unexplored / unreachable far corner

        Position reachable = unit.Position.Neighbours().First(n => game.CheckSetDestination(unit, n).Allowed);
        Assert.True(game.CheckSetDestination(unit, reachable).Allowed);
    }

    [Fact]
    public void SetDestination_Throws_OnInvalidTarget()
    {
        var game = Game.New(Classic, seed: 99);
        Unit unit = LandUnit(game);
        Assert.Throws<InvalidMoveException>(() => game.SetDestination(unit, unit.Position));
    }

    [Fact]
    public void ClearDestination_RemovesTheGoto()
    {
        var game = Game.New(Classic, seed: 99);
        Unit unit = LandUnit(game);
        unit.Destination = new Position(10, 10);

        game.ClearDestination(unit);

        Assert.Null(unit.Destination);
    }

    // ── Advance (slice 3) ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AdvanceDestination_NotGoing_WithoutADestination()
    {
        var game = Game.New(Classic, seed: 99);
        GotoAdvance result = game.AdvanceDestination(LandUnit(game));
        Assert.Equal(GotoOutcome.NotGoing, result.Outcome);
    }

    [Fact]
    public void AdvanceDestination_ReachesAnAdjacentGoal_AndClearsTheGoto()
    {
        var game = Game.New(Classic, seed: 99);
        Unit unit = LandUnit(game);
        Position goal = unit.Position.Neighbours().First(n => game.CheckSetDestination(unit, n).Allowed);
        game.SetDestination(unit, goal);

        GotoAdvance result = game.AdvanceDestination(unit);

        Assert.Equal(GotoOutcome.Reached, result.Outcome);
        Assert.Equal(goal, unit.Position);
        Assert.Null(unit.Destination); // arriving clears the goto
    }

    [Fact]
    public void AdvanceDestination_OutOfMoves_KeepsTheGoto()
    {
        var game = Game.New(Classic, seed: 99);
        Unit unit = LandUnit(game);
        Position goal = unit.Position.Neighbours().First(n => game.CheckSetDestination(unit, n).Allowed);
        game.SetDestination(unit, goal);
        unit.MovementLeft = 0; // spent

        GotoAdvance result = game.AdvanceDestination(unit);

        Assert.Equal(GotoOutcome.OutOfMoves, result.Outcome);
        Assert.Equal(goal, unit.Destination); // kept for next turn
    }

    // ── Cross-turn advance via the turn loop (slice 4) ───────────────────────────────────────────────────

    [Fact]
    public void Goto_WalksTheUnitAcrossMultipleTurns()
    {
        var game = Game.New(Classic, seed: 7);
        game.HumanPlayer.ExploredSet.UnionWith(game.Map.AllPositions()); // reveal the map so a far goto can route
        Unit unit = LandUnit(game);

        // A reachable land tile several steps away (so arrival genuinely spans multiple turns).
        Position goal = game.Map.AllPositions()
            .Where(p => !game.Map.TerrainAt(p).IsWater)
            .Select(p => (p, steps: game.FindPath(unit, p).Count))
            .Where(t => t.steps >= 4)
            .OrderBy(t => t.steps)
            .First().p;
        int startDist = Chebyshev(unit.Position, goal);

        game.SetDestination(unit, goal);
        for (int turn = 0; turn < 20 && unit.IsGoingTo; turn++)
        {
            game.EndTurn(); // ProcessGotos advances the unit on the human's turn
        }

        Assert.True(Chebyshev(unit.Position, goal) < startDist, "the goto unit made no cross-turn progress");
        if (!unit.IsGoingTo)
        {
            Assert.Equal(goal, unit.Position); // arrived → goto cleared
        }
    }

    // ── Unit cycling (slice 4) ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void NextUnitToMove_OrdersById_AndSkipsRestingGotoAndSpentUnits()
    {
        var game = Game.New(Classic, seed: 99);
        Player human = game.HumanPlayer;
        var onMap = game.PlayerUnits.Where(u => u.IsOnMap).OrderBy(u => u.Id).ToList();
        Assert.NotEmpty(onMap);

        Assert.Equal(onMap[0].Id, game.NextUnitToMove(human)!.Id); // lowest id, fresh moves

        foreach (Unit u in onMap)
        {
            u.MovementLeft = 0;
        }
        Assert.Null(game.NextUnitToMove(human));     // all spent
        Assert.False(game.HasUnitsToMove(human));

        Unit first = onMap[0];
        first.MovementLeft = first.Type.Movement;    // give its moves back

        first.Orders = UnitOrders.Sentry;
        Assert.Null(game.NextUnitToMove(human));      // resting → skipped
        first.Orders = UnitOrders.Active;

        first.Destination = new Position(20, 20);
        Assert.Null(game.NextUnitToMove(human));      // on a goto → skipped (auto-advances)
        first.Destination = null;

        Assert.Equal(first.Id, game.NextUnitToMove(human)!.Id); // offered again
        Assert.True(game.HasUnitsToMove(human));
    }

    private static int Chebyshev(Position a, Position b) =>
        Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
}
