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
        // A unit WITH a goto, snapshotted then stripped of DestX/DestY to simulate a pre-v36 save: loading must
        // drop the destination (a v35 save never carried one), proving strip→load, not a vacuous null→null.
        var game = Game.New(Classic, seed: 5);
        Unit travelling = LandUnit(game);
        travelling.Destination = new Position(12, 9);
        SaveGame full = SaveGame.From(game);
        Assert.Contains(full.Units, u => u.Id == travelling.Id && u.DestX == 12 && u.DestY == 9); // present at v36

        SaveGame v35 = full with
        {
            Version = 35,
            Units = full.Units.Select(u => u with { DestX = null, DestY = null }).ToList(),
        };

        Game loaded = SaveGame.FromJson(v35.ToJson()).Restore(Classic);

        Assert.All(loaded.Units, u => Assert.Null(u.Destination)); // the goto is gone after a pre-v36 load
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
        Assert.Equal(0, result.StepsTaken);
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
        Assert.Equal(1, result.StepsTaken); // one tile to the adjacent goal
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
        Assert.Equal(0, result.StepsTaken);   // never moved (no movement to spend)
        Assert.Equal(goal, unit.Destination); // kept for next turn
    }

    [Fact]
    public void AdvanceDestination_NoPath_WhenTheGoalIsUnreachable_KeepsTheGoto()
    {
        var game = Game.New(Classic, seed: 99);
        Unit unit = LandUnit(game);
        var unreachable = new Position(game.Map.Width - 1, game.Map.Height - 1); // a far, unexplored water corner → no route
        unit.Destination = unreachable; // set directly (CheckSetDestination would reject an unreachable goal up front)
        Assert.True(unit.MovementLeft > 0);

        GotoAdvance result = game.AdvanceDestination(unit);

        Assert.Equal(GotoOutcome.NoPath, result.Outcome);
        Assert.Equal(0, result.StepsTaken);
        Assert.Equal(unreachable, unit.Destination); // kept for a retry
    }

    [Fact]
    public void AdvanceDestination_PartialMoveRejectsTheNextStep_OutOfMoves_KeepsTheGoto()
    {
        // The in-loop OutOfMoves branch (distinct from the degenerate 0-moves case): a unit with positive but
        // insufficient movement for an expensive next step that the partial-move rule rejects.
        var game = Game.New(Classic, seed: 7);
        game.HumanPlayer.ExploredSet.UnionWith(game.Map.AllPositions());

        // A land tile T adjacent to an EXPENSIVE land tile E (forest/hills/mountains, enter cost ≥ 6), both empty.
        (Position t, Position e) = (from a in game.Map.AllPositions()
                                    where IsEmptyLand(game, a)
                                    from n in a.Neighbours()
                                    where game.Map.InBounds(n) && IsEmptyLand(game, n) && game.Map.TerrainAt(n).MoveCost >= 6
                                    select (a, n)).First();

        Unit scout = game.SpawnUnit(game.PlayerUnits.First().Type, t);
        scout.RoleId = "model.role.scout"; // +9 movement → InitialMovement 12, so a 1-move remainder is "not near full"
        game.SetDestination(scout, e);
        scout.MovementLeft = 1; // 1 < cost 6, and 1+2=3 < 12 → the partial-move rule rejects the step

        GotoAdvance result = game.AdvanceDestination(scout);

        Assert.Equal(GotoOutcome.OutOfMoves, result.Outcome);
        Assert.Equal(0, result.StepsTaken);
        Assert.Equal(t, scout.Position);   // did not move
        Assert.Equal(e, scout.Destination); // goto kept
    }

    [Fact]
    public void AdvanceDestination_WalksMultipleTilesInOneCall_ReassertingTheGotoMidRoute()
    {
        // Covers the multi-iteration loop + the line that re-asserts Destination after MoveUnit clears it.
        var game = Game.New(Classic, seed: 7);
        game.HumanPlayer.ExploredSet.UnionWith(game.Map.AllPositions());
        Unit unit = LandUnit(game);

        // A reachable land goal ≥ 2 steps away whose route crosses no rumour (so it doesn't stop early).
        (Position goal, IReadOnlyList<Position> path) = game.Map.AllPositions()
            .Where(p => !game.Map.TerrainAt(p).IsWater)
            .Select(p => (p, path: game.FindPath(unit, p)))
            .Where(t => t.path.Count is >= 2 and <= 6 && t.path.All(s => !game.Map.HasRumour(s)))
            .OrderBy(t => t.path.Count)
            .First();
        game.SetDestination(unit, goal);
        unit.MovementLeft = 999; // plenty to walk the whole route in a single advance

        GotoAdvance result = game.AdvanceDestination(unit);

        Assert.Equal(GotoOutcome.Reached, result.Outcome);
        Assert.True(result.StepsTaken > 1, $"expected a multi-tile advance, got {result.StepsTaken}");
        Assert.Equal(path.Count, result.StepsTaken); // walked the whole route in one call
        Assert.Equal(goal, unit.Position);
        Assert.Null(unit.Destination); // arrived → cleared
    }

    [Fact]
    public void AdvanceDestination_StopsOnAnUnresolvedRumourTile_KeepingTheGoto()
    {
        // FreeCol: exploring a rumour ends the move. A goto must not march a unit off an unresolved strange-mounds
        // tile (the rumour stays put — see LostCityRumourTests). The top-of-loop HasRumour guard covers both the
        // "started on a pending rumour" case (here) and the "stepped onto one mid-route" case (same code path).
        var game = Game.New(Classic, seed: 99);
        game.HumanPlayer.ExploredSet.UnionWith(game.Map.AllPositions());
        Unit unit = LandUnit(game);
        game.Map.AddRumour(unit.Position); // an unresolved rumour sits under the unit
        Position goal = game.Map.AllPositions()
            .Where(p => !game.Map.TerrainAt(p).IsWater && game.FindPath(unit, p).Count >= 2)
            .First();
        unit.Destination = goal;

        GotoAdvance result = game.AdvanceDestination(unit);

        Assert.Equal(GotoOutcome.Interrupted, result.Outcome);
        Assert.Equal(0, result.StepsTaken);   // did not march off the rumour
        Assert.Equal(goal, unit.Destination); // goto kept (resumes once the rumour clears)
    }

    private static bool IsEmptyLand(Game game, Position p) =>
        game.Map.InBounds(p) && !game.Map.TerrainAt(p).IsWater
        && game.ColonyAt(p) is null && game.NativeSettlementAt(p) is null
        && !game.Units.Any(u => u.IsOnMap && u.Position == p);

    // ── Cross-turn advance via the turn loop (slice 4) ───────────────────────────────────────────────────

    [Fact]
    public void Goto_WalksTheUnitAcrossMultipleTurns_AndArrives()
    {
        var game = Game.New(Classic, seed: 7);
        game.HumanPlayer.ExploredSet.UnionWith(game.Map.AllPositions()); // reveal the map so a far goto can route
        Unit unit = LandUnit(game);

        // A reachable land goal ≥4 steps away whose route crosses no rumour (a free colonist walks ~1 tile/turn,
        // so arrival genuinely spans several EndTurns and won't stop early on a strange-mounds).
        Position goal = game.Map.AllPositions()
            .Where(p => !game.Map.TerrainAt(p).IsWater)
            .Select(p => (p, path: game.FindPath(unit, p)))
            .Where(t => t.path.Count >= 4 && t.path.All(s => !game.Map.HasRumour(s)))
            .OrderBy(t => t.path.Count)
            .First().p;

        game.SetDestination(unit, goal);

        game.EndTurn(); // one turn must not be enough to arrive (≥4 steps, ~1 tile/turn)
        Assert.True(unit.IsGoingTo, "a ≥4-step goto must still be travelling after one turn");
        Assert.NotEqual(goal, unit.Position);

        int turns = 1;
        while (unit.IsGoingTo && turns < 40)
        {
            game.EndTurn(); // ProcessGotos advances the unit on the human's turn
            turns++;
        }

        Assert.False(unit.IsGoingTo);      // arrived → goto cleared
        Assert.Equal(goal, unit.Position); // exactly on the destination
        Assert.True(turns > 1, "arrival should have spanned more than one turn");
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

        foreach (UnitOrders resting in new[] { UnitOrders.Fortifying, UnitOrders.Fortified, UnitOrders.Sentry })
        {
            first.Orders = resting;
            Assert.Null(game.NextUnitToMove(human));   // every resting order is skipped
        }
        first.Orders = UnitOrders.Active;

        first.Destination = new Position(20, 20);
        Assert.Null(game.NextUnitToMove(human));      // on a goto → skipped (auto-advances)
        first.Destination = null;

        Assert.Equal(first.Id, game.NextUnitToMove(human)!.Id); // offered again
        Assert.True(game.HasUnitsToMove(human));
    }
}
