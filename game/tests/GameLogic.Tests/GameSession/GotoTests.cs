using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using CrownAndColony.GameLogic.World.Improvements;
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

    [Fact]
    public void NextUnitToMove_SkipSet_PassesOverSkippedUnits_ButStillOffersTheRest()
    {
        // The presentation layer's Space-skip set (86d3f0vuy): supplying a unit id excludes it this cycle, so the
        // next-lowest unaffected unit is offered instead — and an empty/null set behaves exactly as before.
        var game = Game.New(Classic, seed: 99);
        Player human = game.HumanPlayer;
        var onMap = game.PlayerUnits.Where(u => u.IsOnMap).OrderBy(u => u.Id).ToList();
        Assert.True(onMap.Count >= 2, "this test needs at least two on-map units");

        Assert.Equal(onMap[0].Id, game.NextUnitToMove(human, new HashSet<int>())!.Id); // empty set = unchanged
        Assert.Equal(onMap[0].Id, game.NextUnitToMove(human, null)!.Id);                // null = unchanged

        // Skip the lowest id → the next one is offered.
        var skip = new HashSet<int> { onMap[0].Id };
        Assert.Equal(onMap[1].Id, game.NextUnitToMove(human, skip)!.Id);

        // Skip every unit → none offered.
        var all = onMap.Select(u => u.Id).ToHashSet();
        Assert.Null(game.NextUnitToMove(human, all));
    }

    // ── Road/river-aware goto pathing (86d3dqr62) ────────────────────────────────────────────────────────

    /// <summary>
    /// A wide, fully-explored plains map with a single unit of <paramref name="unitTypeId"/> at the west edge of
    /// the middle row. Built from a save (mirrors PioneerBuildTests) so tests can stamp a road/river corridor and
    /// drive the real <see cref="Game"/> (CheckMove + FindPath), not just the bare pathfinder.
    /// </summary>
    private static Game CorridorGame(int width, int height, string unitTypeId = "model.unit.freeColonist")
    {
        var save = new SaveGame
        {
            Turn = 1,
            RandomStateValue = 1,
            RandomIncrement = 1,
            MapWidth = width,
            MapHeight = height,
            Terrain = [.. Enumerable.Repeat("model.tile.plains", width * height)],
            Units = [new SavedUnit(1, unitTypeId, 0, height / 2, 3)],
            Explored = [],
        };
        Game game = save.Restore(Classic);
        game.HumanPlayer.ExploredSet.UnionWith(game.Map.AllPositions()); // reveal the whole map so a far goto routes
        return game;
    }

    // The total cost CheckMove charges to walk a route, step by step from the unit's start, refreshing movement
    // each step so partial-move clamping never kicks in — i.e. the un-clamped per-edge sum the pathfinder minimises.
    private static int SummedCheckMoveCost(Game game, Unit unit, IReadOnlyList<Position> route)
    {
        int total = 0;
        foreach (Position step in route)
        {
            unit.MovementLeft = 99; // plenty, so CheckMove returns the true per-edge cost (no partial-move clamp)
            MoveCheck check = game.CheckMove(unit, step);
            Assert.True(check.Allowed, $"step into {step} should be legal");
            total += check.Cost;
            game.MoveUnit(unit, step);
        }
        return total;
    }

    [Fact]
    public void Goto_PrefersARoadCorridor_OverParallelOpenPlains_AndTheCostMatchesCheckMove()
    {
        // A straight road runs along the middle row (the unit's row); the rows above/below are open plains. The
        // goto to the east end of the road must hug the road (each road→road step costs 1, vs plains 3). We also
        // assert the chosen route's cost, summed through CheckMove step by step, equals the road's 1-per-step total —
        // proving the pathfinder's edge cost and CheckMove's per-step charge agree (the whole point of this change).
        Game game = CorridorGame(width: 6, height: 3);
        Unit unit = game.Units.First(u => u.Id == 1);
        int row = unit.Position.Y;
        var road = Classic.Improvement(TileImprovementType.RoadId);
        for (int x = 0; x < game.Map.Width; x++)
        {
            game.Map.AddImprovement(new Position(x, row), road); // road the unit's whole row
        }
        var goal = new Position(5, row);

        IReadOnlyList<Position> path = game.FindPath(unit, goal);

        Assert.Equal(5, path.Count);                                                  // 5 straight road steps east
        Assert.All(path, p => Assert.True(game.Map.HasImprovement(p, TileImprovementType.RoadId)));
        Assert.Equal(goal, path[^1]);
        // The route runs entirely along the road row — never detours onto a plains row.
        Assert.All(path, p => Assert.Equal(row, p.Y));
        // Cost agreement: walking the route through CheckMove charges 1 per road step = 5 total (not 5 × 3 = 15).
        Assert.Equal(5, SummedCheckMoveCost(game, game.Units.First(u => u.Id == 1), path));
    }

    [Fact]
    public void Goto_FollowsARiverCorridor_TheSameAsARoad()
    {
        Game game = CorridorGame(width: 6, height: 3);
        Unit unit = game.Units.First(u => u.Id == 1);
        int row = unit.Position.Y;
        for (int x = 0; x < game.Map.Width; x++)
        {
            game.Map.AddImprovement(new Position(x, row), Classic.RiverType); // river the unit's whole row
        }
        var goal = new Position(5, row);

        IReadOnlyList<Position> path = game.FindPath(unit, goal);

        Assert.All(path, p => Assert.True(game.Map.HasRiver(p)));         // hugs the river
        Assert.Equal(5, SummedCheckMoveCost(game, game.Units.First(u => u.Id == 1), path)); // river follow-cost 1 each
    }

    [Fact]
    public void Goto_Ships_AreUnaffectedByRoadsOrRivers()
    {
        // Rivers/roads are land features; a ship gets no bonus, so its route is pure terrain cost. We sail a ship
        // east along an all-ocean middle row that carries a (nonsensical-but-harmless) road stamp on every tile and
        // confirm the route cost is plain ocean cost — the improvement bonus is genuinely gated off for naval units.
        var save = new SaveGame
        {
            Turn = 1,
            RandomStateValue = 1,
            RandomIncrement = 1,
            MapWidth = 5,
            MapHeight = 3,
            Terrain = [.. Enumerable.Repeat("model.tile.ocean", 15)],
            Units = [new SavedUnit(1, "model.unit.caravel", 0, 1, 3)],
            Explored = [],
        };
        Game game = save.Restore(Classic);
        game.HumanPlayer.ExploredSet.UnionWith(game.Map.AllPositions());
        Unit ship = game.Units.First(u => u.Id == 1);
        var road = Classic.Improvement(TileImprovementType.RoadId);
        for (int x = 0; x < game.Map.Width; x++)
        {
            game.Map.AddImprovement(new Position(x, 1), road); // a road on the ship's water row (no effect for a ship)
        }
        int oceanCost = game.Map.TerrainAt(new Position(1, 1)).MoveCost;
        var goal = new Position(4, 1);

        IReadOnlyList<Position> path = game.FindPath(ship, goal);

        Assert.Equal(4, path.Count);
        // Each step costs full ocean cost — the road grants the ship nothing (CheckMove never reduces a naval step).
        ship.MovementLeft = 99;
        Assert.Equal(oceanCost, game.CheckMove(ship, path[0]).Cost);
    }

    [Fact]
    public void Goto_RoadPathing_IsDeterministic_SameInputSamePath()
    {
        Game game = CorridorGame(width: 6, height: 3);
        Unit unit = game.Units.First(u => u.Id == 1);
        int row = unit.Position.Y;
        var road = Classic.Improvement(TileImprovementType.RoadId);
        for (int x = 0; x < game.Map.Width; x++)
        {
            game.Map.AddImprovement(new Position(x, row), road);
        }
        var goal = new Position(5, row);

        Assert.Equal(game.FindPath(unit, goal), game.FindPath(unit, goal)); // byte-stable on a repeat (ADR-009)
    }

    // ── Settlement / Europe / open-sea goto destinations (86d3drn06) ──────────────────────────────────────

    /// <summary>Builds a game on a hand-crafted map via save/restore (with the whole map revealed for the human).</summary>
    private static Game GameOn(
        string[] terrain, int w, int h, SavedUnit[] units,
        SavedColony[]? colonies = null, SavedNativeSettlement[]? natives = null)
    {
        var save = new SaveGame
        {
            Turn = 1,
            RandomStateValue = 1,
            RandomIncrement = 1,
            MapWidth = w,
            MapHeight = h,
            Terrain = terrain,
            Units = units,
            Explored = [],
            Colonies = colonies ?? [],
            NativeSettlements = natives,
        };
        Game game = save.Restore(Classic);
        game.HumanPlayer.ExploredSet.UnionWith(game.Map.AllPositions()); // reveal so a goal isn't rejected as unexplored
        return game;
    }

    [Fact]
    public void CheckSetDestination_ClassifiesAndRoutesEachDestinationKind()
    {
        // A 3×3 land strip across the middle row, water above/below; a native camp at (2,1).
        //   row0: O O O
        //   row1: L L C   (C = camp on land at (2,1))
        //   row2: O O O
        Game game = GameOn(
            ["model.tile.ocean", "model.tile.ocean", "model.tile.ocean",
             "model.tile.plains", "model.tile.plains", "model.tile.plains",
             "model.tile.ocean", "model.tile.ocean", "model.tile.ocean"],
            3, 3,
            [new SavedUnit(1, "model.unit.freeColonist", 0, 1, 3)],
            natives:
            [new SavedNativeSettlement(7777, "model.nationType.apache", "model.settlement.camp", false, 2, 1, 5)]);
        Unit colonist = game.Units.First(u => u.Id == 1);
        var camp = new Position(2, 1);

        // The camp tile itself is classified as a Settlement destination (the unit can't enter it).
        Assert.Equal(DestinationKind.Settlement, game.DestinationKindOf(colonist, camp));
        // A plain land tile is a Tile destination.
        Assert.Equal(DestinationKind.Tile, game.DestinationKindOf(colonist, new Position(1, 1)));
        // Goto to the camp is allowed (route to an adjacent tile exists).
        Assert.True(game.CheckSetDestination(colonist, camp).Allowed);
    }

    [Fact]
    public void Goto_LandUnit_ReachesASettlement_ByArrivingAdjacent()
    {
        // A long land row with a native camp at the east end; the colonist marches up beside it and stops.
        Game game = GameOn(
            ["model.tile.plains", "model.tile.plains", "model.tile.plains", "model.tile.plains", "model.tile.plains"],
            5, 1,
            [new SavedUnit(1, "model.unit.freeColonist", 0, 0, 3)],
            natives:
            [new SavedNativeSettlement(7777, "model.nationType.apache", "model.settlement.camp", false, 4, 0, 5)]);
        Unit colonist = game.Units.First(u => u.Id == 1);
        var camp = new Position(4, 0);
        game.SetDestination(colonist, camp);

        int turns = 0;
        while (colonist.IsGoingTo && turns < 20)
        {
            game.EndTurn();
            turns++;
        }

        Assert.False(colonist.IsGoingTo);                         // arrived → goto cleared
        Assert.True(colonist.Position.IsAdjacentTo(camp));        // stopped BESIDE the camp (never on it)
        Assert.NotEqual(camp, colonist.Position);                 // and not on the camp tile
        Assert.Null(game.NativeSettlementAt(colonist.Position));  // the camp tile is still the camp's
    }

    [Fact]
    public void Goto_Ship_WithAEuropeDestination_RoutesToTheHighSeas_ThenSailsToEurope()
    {
        // ocean … ocean highSeas — a caravel at the west end with a goto onto the high-seas tile.
        Game game = GameOn(
            ["model.tile.ocean", "model.tile.ocean", "model.tile.highSeas"],
            3, 1,
            [new SavedUnit(1, "model.unit.caravel", 0, 0, 12)]);
        Unit ship = game.Units.First(u => u.Id == 1);
        var highSeas = new Position(2, 0);

        Assert.Equal(DestinationKind.Europe, game.DestinationKindOf(ship, highSeas)); // a ship → high seas = Europe
        game.SetDestination(ship, highSeas);

        GotoAdvance result = game.AdvanceDestination(ship); // 12 MP sails the two ocean steps onto the high seas this turn

        Assert.Equal(GotoOutcome.SailedToEurope, result.Outcome);
        Assert.Equal(UnitLocation.SailingToEurope, ship.Location); // handed off to the existing SailToEurope command
        Assert.Null(ship.Destination);                             // goto cleared on the hand-off
        Assert.Equal(3, ship.SailTurnsRemaining);                  // the standard crossing length
    }

    [Fact]
    public void Goto_Ship_NavalPathsOverOpenWater_ToASeaDestination()
    {
        // A 5-wide ocean row (no land, no high seas); the ship sails to the far open-water tile.
        Game game = GameOn(
            ["model.tile.ocean", "model.tile.ocean", "model.tile.ocean", "model.tile.ocean", "model.tile.ocean"],
            5, 1,
            [new SavedUnit(1, "model.unit.caravel", 0, 0, 12)]);
        Unit ship = game.Units.First(u => u.Id == 1);
        var seaGoal = new Position(4, 0);

        Assert.Equal(DestinationKind.Tile, game.DestinationKindOf(ship, seaGoal)); // open water = a plain Tile destination
        Assert.True(game.CheckSetDestination(ship, seaGoal).Allowed);
        game.SetDestination(ship, seaGoal);

        GotoAdvance result = game.AdvanceDestination(ship);

        Assert.Equal(GotoOutcome.Reached, result.Outcome);
        Assert.Equal(seaGoal, ship.Position);   // sailed the whole open-water route onto the goal
        Assert.Null(ship.Destination);          // arrived → cleared
        Assert.True(ship.IsOnMap);              // a plain sea destination does NOT sail to Europe
    }

    [Fact]
    public void CheckSetDestination_RejectsIllegalDestinations()
    {
        // Land row with a coastal native camp on land, ocean below.
        //   row0: L L C
        //   row1: O O O
        Game game = GameOn(
            ["model.tile.plains", "model.tile.plains", "model.tile.plains",
             "model.tile.ocean", "model.tile.ocean", "model.tile.ocean"],
            3, 2,
            [new SavedUnit(1, "model.unit.freeColonist", 0, 0, 3),
             new SavedUnit(2, "model.unit.caravel", 0, 1, 12)],
            natives:
            [new SavedNativeSettlement(7777, "model.nationType.apache", "model.settlement.camp", false, 2, 0, 5)]);
        Unit colonist = game.Units.First(u => u.Id == 1);
        Unit ship = game.Units.First(u => u.Id == 2);

        // A land unit cannot set a course onto open water (an open-sea tile is a ships-only destination).
        Assert.False(game.CheckSetDestination(colonist, new Position(1, 1)).Allowed);
        // A ship cannot set a course onto open land (only a settlement is the terrain exception — and there the ship
        // still arrives on an adjacent WATER tile; plain land is rejected).
        Assert.False(game.CheckSetDestination(ship, new Position(0, 0)).Allowed);
        // A land unit cannot reach a settlement that is walled off from it (no land tile adjacent to the camp that the
        // unit can path to): isolate the camp by surrounding its land neighbours with water — here the camp at (2,0)
        // has only (1,0) land adjacent; flood that to ocean so no reachable land neighbour remains.
        Game walled = GameOn(
            ["model.tile.plains", "model.tile.ocean", "model.tile.plains",
             "model.tile.ocean", "model.tile.ocean", "model.tile.ocean"],
            3, 2,
            [new SavedUnit(1, "model.unit.freeColonist", 0, 0, 3)],
            natives:
            [new SavedNativeSettlement(7777, "model.nationType.apache", "model.settlement.camp", false, 2, 0, 5)]);
        Unit isolated = walled.Units.First(u => u.Id == 1);
        // The camp at (2,0): its neighbours are (1,0)O (1,1)O (2,1)O — no land tile a land unit can stand on, and the
        // unit at (0,0) is separated from (2,0) by water — so no adjacent route exists → the goto is rejected.
        Assert.False(walled.CheckSetDestination(isolated, new Position(2, 0)).Allowed);
    }
}
