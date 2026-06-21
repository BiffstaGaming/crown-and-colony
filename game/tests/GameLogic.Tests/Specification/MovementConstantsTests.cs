using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using CrownAndColony.GameLogic.World.Improvements;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Specification;

/// <summary>
/// Pins the terrain/movement scalar constants routed through the ruleset (86d3drpgg) — each parses to its classic
/// FreeCol value, and the routed paths (the <see cref="Game.CheckMove"/> partial-move rule and the
/// <see cref="Pathfinder"/> heuristic) are byte-identical to the pre-routing hardcoded behaviour.
/// </summary>
public class MovementConstantsTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    [Fact]
    public void Classic_MovementConstants_MatchClassicValues()
    {
        // BaseMoveCost 3 = the plains tile basic-move-cost (one whole move); MinMoveCost 1 = the cheapest improvement
        // movement-cost (river/road); PartialMoveThreshold 2 = FreeCol's Unit.getMoveCost +2 round-up slack.
        MovementConstants m = Classic.MovementConstants;
        Assert.Equal(3, m.BaseMoveCost);
        Assert.Equal(1, m.MinMoveCost);
        Assert.Equal(2, m.PartialMoveThreshold);
    }

    [Fact]
    public void ClassicConstants_EqualThePackagedDefaults()
    {
        // The classic spec parses to exactly the ClassicDefaults bundle — the fallback path and the parsed path are the
        // same bytes (ADR-009).
        Assert.Equal(MovementConstants.ClassicDefaults, Classic.MovementConstants);
    }

    [Fact]
    public void BaseMoveCost_TracksThePlainsTerrainMoveCost()
    {
        // The "one whole move" denominator is sourced from the plains tile's parsed cost — they must agree.
        Assert.Equal(Classic.Terrain("model.tile.plains").MoveCost, Classic.MovementConstants.BaseMoveCost);
    }

    [Fact]
    public void MinMoveCost_IsTheCheapestImprovementMovementCost()
    {
        // The pathfinder heuristic lower bound equals the cheapest positive movement-cost any improvement grants — the
        // river and road both declare movement-cost="1".
        int cheapest = Classic.ImprovementTypes
            .Select(i => i.MovementCost)
            .Where(c => c > 0)
            .Min();
        Assert.Equal(cheapest, Classic.MovementConstants.MinMoveCost);
        Assert.Equal(1, Classic.Improvement(TileImprovementType.RiverId).MovementCost);
        Assert.Equal(1, Classic.Improvement(TileImprovementType.RoadId).MovementCost);
    }

    [Fact]
    public void PartialMoveRule_AllowsTheNearFullMove_AtTheClassicThreshold()
    {
        // FreeCol partial-move: a caravel (12 MP) with 1 MP left entering ocean (cost 3) still moves — 3 <= 1+2 — and
        // spends all remaining movement. This is exactly the +2 threshold now read from MovementConstants.
        Game game = OceanPair();
        Unit caravel = game.Units[0];
        caravel.MovementLeft = 1;

        MoveCheck check = game.CheckMove(caravel, new Position(1, 0));
        Assert.True(check.Allowed);
        Assert.Equal(1, check.Cost); // charged the remainder, not the full 3

        game.MoveUnit(caravel, new Position(1, 0));
        Assert.Equal(0, caravel.MovementLeft);
    }

    [Fact]
    public void PartialMoveRule_RejectsBeyondTheThreshold_AtTheClassicValue()
    {
        // A land unit far from full movement facing a costlier-than-threshold shortfall must wait: 4 MP left, mountains
        // cost 9 — 4+2 < the unit's full 12 (not near-full) and 9 > 4+2 — so the move is rejected. PartialMoveThreshold.
        Game game = MountainsBesidePlains();
        Unit rider = game.Units[0];
        rider.MovementLeft = 4;

        MoveCheck check = game.CheckMove(rider, new Position(1, 0));
        Assert.False(check.Allowed);
    }

    [Fact]
    public void PathfinderHeuristic_WithRoutedMinCost_ReturnsTheSameOptimalRoute()
    {
        // The routed MinMoveCost (1) feeds the admissible heuristic; the returned least-cost route is identical to the
        // default-min run. A 4×4 plains diagonal is 3 free diagonal steps either way.
        var terrain = new TerrainType[16];
        Array.Fill(terrain, Classic.Terrain("model.tile.plains"));
        var map = new GameMap(4, 4, terrain);
        bool Land(Position p) => !map.TerrainAt(p).IsWater;

        IReadOnlyList<Position> defaultRun = Pathfinder.FindPath(map, new Position(0, 0), new Position(3, 3), Land);
        IReadOnlyList<Position> routedRun = Pathfinder.FindPath(
            map, new Position(0, 0), new Position(3, 3), Land,
            minEnterCost: Classic.MovementConstants.MinMoveCost);

        Assert.Equal(defaultRun, routedRun);
        Assert.Equal(3, routedRun.Count);
        Assert.Equal(new Position(3, 3), routedRun[^1]);
    }

    /// <summary>A caravel on a 1×2 all-ocean map (so a ship can move from (0,0) to (1,0)).</summary>
    private static Game OceanPair()
    {
        var save = new SaveGame
        {
            Turn = 1,
            RandomStateValue = 1,
            RandomIncrement = 1,
            MapWidth = 2,
            MapHeight = 1,
            Terrain = ["model.tile.ocean", "model.tile.ocean"],
            Explored = [0, 1],
            Units = [new SavedUnit(1, "model.unit.caravel", 0, 0, 0)],
            Colonies = [],
        };
        return save.Restore(Classic);
    }

    /// <summary>A mounted unit on plains at (0,0) beside mountains at (1,0) — a 9-cost step to test the reject branch.</summary>
    private static Game MountainsBesidePlains()
    {
        var save = new SaveGame
        {
            Turn = 1,
            RandomStateValue = 1,
            RandomIncrement = 1,
            MapWidth = 2,
            MapHeight = 1,
            Terrain = ["model.tile.plains", "model.tile.mountains"],
            Explored = [0, 1],
            // A scout-role free colonist = 12 MP (base 3 + scout movement-bonus 9) — far from full at 4 MP left, so the
            // 9-cost mountains step exercises the partial-move reject branch.
            Units = [new SavedUnit(1, "model.unit.freeColonist", 0, 0, 0, Role: "model.role.scout", RoleCount: 1)],
            Colonies = [],
        };
        return save.Restore(Classic);
    }
}
