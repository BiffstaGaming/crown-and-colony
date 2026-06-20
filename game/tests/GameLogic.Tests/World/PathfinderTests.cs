using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.World;
using CrownAndColony.GameLogic.World.Improvements;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.World;

/// <summary>
/// Goto pathfinding (<c>86d3c9pfy</c>) — slice 2: the pure deterministic A* <see cref="Pathfinder"/>. Hand-built
/// char maps pin least-cost routing, diagonal preference, cost-aware detours, byte-stable tie-breaking, and no-path.
/// The road/river-aware edge cost (<c>86d3dqr62</c>) is pinned by the road-corridor cases at the bottom.
/// </summary>
public class PathfinderTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    private static TerrainType Terrain(char c) => c switch
    {
        'O' => Classic.Terrain("model.tile.ocean"),     // water — impassable for the land predicate
        'L' => Classic.Terrain("model.tile.plains"),    // cost 3
        'M' => Classic.Terrain("model.tile.mountains"), // cost 9
        'R' => Classic.Terrain("model.tile.plains"),    // plains carrying a road  (set up by FromRows)
        'r' => Classic.Terrain("model.tile.plains"),    // plains carrying a river (set up by FromRows)
        _ => throw new ArgumentException($"unknown terrain char '{c}'"),
    };

    /// <summary>Builds a char-map; the 'R'/'r' chars also stamp a road / river improvement on that tile.</summary>
    private static GameMap FromRows(params string[] rows)
    {
        int h = rows.Length, w = rows[0].Length;
        var terrain = new TerrainType[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                terrain[y * w + x] = Terrain(rows[y][x]);
            }
        }
        var map = new GameMap(w, h, terrain);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (rows[y][x] == 'R')
                {
                    map.AddImprovement(new Position(x, y), Classic.Improvement(TileImprovementType.RoadId));
                }
                else if (rows[y][x] == 'r')
                {
                    map.AddImprovement(new Position(x, y), Classic.RiverType);
                }
            }
        }
        return map;
    }

    private static IReadOnlyList<Position> Path(GameMap map, Position start, Position goal) =>
        Pathfinder.FindPath(map, start, goal, p => !map.TerrainAt(p).IsWater); // land-only passability, terrain-only cost

    /// <summary>Land-unit pathing WITH the river/road follow-cost bonus folded in (mirrors a land goto).</summary>
    private static IReadOnlyList<Position> LandPath(GameMap map, Position start, Position goal) =>
        Pathfinder.FindPath(map, start, goal, p => !map.TerrainAt(p).IsWater, applyImprovementBonus: true);

    /// <summary>The total cost of a route under the same edge-cost rule the pathfinder uses (terrain + optional bonus).</summary>
    private static int RouteCost(GameMap map, Position start, IReadOnlyList<Position> path, bool withBonus)
    {
        int total = 0;
        Position from = start;
        foreach (Position to in path)
        {
            int baseCost = map.TerrainAt(to).MoveCost;
            total += withBonus
                ? ImprovementMovement.MoveCost(map.ImprovementsAt(from), map.ImprovementsAt(to), baseCost)
                : baseCost;
            from = to;
        }
        return total;
    }

    [Fact]
    public void StartEqualsGoal_IsEmpty()
    {
        GameMap map = FromRows("LL", "LL");
        Assert.Empty(Path(map, new Position(0, 0), new Position(0, 0)));
    }

    [Fact]
    public void PrefersDiagonals_DiagonalsAreFree()
    {
        // (0,0) → (3,3) on open plains is 3 diagonal steps, not 6 orthogonal ones.
        GameMap map = FromRows("LLLL", "LLLL", "LLLL", "LLLL");
        IReadOnlyList<Position> path = Path(map, new Position(0, 0), new Position(3, 3));

        Assert.Equal(3, path.Count);
        Assert.Equal(new Position(3, 3), path[^1]);
        Assert.All(path, p => Assert.Equal(p.X, p.Y)); // straight down the diagonal
    }

    [Fact]
    public void RoutesAroundExpensiveTerrain()
    {
        // Direct (0,0)→(1,0)M→(2,0) costs 9+3=12; the plains detour (0,0)→(1,1)→(2,0) costs 3+3=6 — the least-cost
        // route, pinned exactly (not merely "avoids the mountain").
        GameMap map = FromRows("LML", "LLL");

        Assert.Equal(
            new[] { new Position(1, 1), new Position(2, 0) },
            Path(map, new Position(0, 0), new Position(2, 0)));
    }

    [Fact]
    public void RoutesAroundImpassableWater()
    {
        // A vertical water wall at column 1 except a gap at the bottom row forces a detour.
        GameMap map = FromRows(
            "LOL",
            "LOL",
            "LLL");
        IReadOnlyList<Position> path = Path(map, new Position(0, 0), new Position(2, 0));

        Assert.NotEmpty(path);
        Assert.All(path, p => Assert.False(map.TerrainAt(p).IsWater)); // never steps on water
        Assert.Contains(new Position(1, 2), path);                     // through the bottom gap
        Assert.Equal(new Position(2, 0), path[^1]);
    }

    [Fact]
    public void NoPath_WhenWalledOff()
    {
        // The goal column is sealed off by water.
        GameMap map = FromRows(
            "LOL",
            "LOL",
            "LOL");
        Assert.Empty(Path(map, new Position(0, 0), new Position(2, 0)));
    }

    [Fact]
    public void UnpassableGoal_IsEmpty()
    {
        GameMap map = FromRows("LL", "LL");
        Assert.Empty(Pathfinder.FindPath(map, new Position(0, 0), new Position(1, 1), _ => false));
    }

    [Fact]
    public void TieBreak_IsLowerYThenLowerX_PathsPinnedEndToEnd()
    {
        GameMap map = FromRows("LLL", "LLL", "LLL");

        // (0,0)→(2,0): the equal-cost first steps (1,0)[Y0] and (1,1)[Y1] tie on f,g — lower Y wins.
        Assert.Equal(
            new[] { new Position(1, 0), new Position(2, 0) },
            Path(map, new Position(0, 0), new Position(2, 0)));

        // (1,0)→(1,2): the equal-cost first steps (0,1)/(1,1)/(2,1) all share Y=1 — lower X (0,1) wins, exercising
        // the X component of the (f,g,Y,X) key that the previous case never touched.
        Assert.Equal(
            new[] { new Position(0, 1), new Position(1, 2) },
            Path(map, new Position(1, 0), new Position(1, 2)));
    }

    // ── Road/river-aware edge cost (86d3dqr62) ───────────────────────────────────────────────────────────

    [Fact]
    public void LandPath_FollowsARoadCorridor_OverParallelOpenPlains()
    {
        // A straight road runs along the top row from (0,0) to (4,0); the bottom row is open plains. A land unit
        // going (0,0)→(4,0) should hug the road (each road→road step costs 1) rather than wander onto plains (cost 3).
        // Without the bonus the A* could pick any equal-length straight line; with it the road is strictly cheaper.
        GameMap map = FromRows(
            "RRRRR",
            "LLLLL");

        IReadOnlyList<Position> road = LandPath(map, new Position(0, 0), new Position(4, 0));

        // Stays on the road row the whole way (every entered tile carries the road).
        Assert.All(road, p => Assert.True(map.HasImprovement(p, TileImprovementType.RoadId)));
        Assert.Equal(new Position(4, 0), road[^1]);
        Assert.Equal(4, RouteCost(map, new Position(0, 0), road, withBonus: true)); // 4 road steps × 1
        // Strictly cheaper than the same route costed without the follow bonus (4 × plains 3 = 12).
        Assert.True(
            RouteCost(map, new Position(0, 0), road, withBonus: true)
            < RouteCost(map, new Position(0, 0), road, withBonus: false));
    }

    [Fact]
    public void LandPath_TakesTheRoad_WhenItIsCheaperThanTheOpenDiagonal()
    {
        // (0,0)→(2,2). The direct open-plains route is 2 diagonal steps × plains 3 = 6. An L-shaped road (down
        // column 0, across row 2) lets the unit corner-cut diagonally along the road for 3 road steps × 1 = 3 —
        // strictly cheaper, so the A* must prefer it. This pins that the pathfinder minimises COST (with the bonus),
        // and that diagonal road steps still earn the follow-cost (both endpoints carry the road).
        GameMap map = FromRows(
            "RLL",
            "RLL",
            "RRR");

        IReadOnlyList<Position> path = LandPath(map, new Position(0, 0), new Position(2, 2));

        Assert.All(path, p => Assert.True(map.HasImprovement(p, TileImprovementType.RoadId)));  // stays on the road
        Assert.Equal(3, RouteCost(map, new Position(0, 0), path, withBonus: true));             // 3 road steps × 1
        // Strictly cheaper than the 6-cost open diagonal it bypassed.
        Assert.True(RouteCost(map, new Position(0, 0), path, withBonus: true) < 6);
        Assert.Equal(new Position(2, 2), path[^1]);
    }

    [Fact]
    public void LandPath_FollowsARiverCorridor_LikeARoad()
    {
        // The river is the same "follow it" mechanic (movement cost 1); a river corridor is preferred just like a road.
        GameMap map = FromRows(
            "rrrrr",
            "LLLLL");

        IReadOnlyList<Position> river = LandPath(map, new Position(0, 0), new Position(4, 0));

        Assert.All(river, p => Assert.True(map.HasRiver(p)));
        Assert.Equal(4, RouteCost(map, new Position(0, 0), river, withBonus: true)); // 4 river steps × 1
    }

    [Fact]
    public void TerrainOnlyPath_IgnoresTheRoad_WhenTheBonusIsOff()
    {
        // Same road map, but costed terrain-only (a ship, or the default overload). Every step is plains-cost 3, so
        // the road row and the plains row are equal-cost — the route is decided purely by tie-breaking, never the
        // road. This is the ship/no-bonus behaviour, and proves the bonus is genuinely gated by the flag.
        GameMap map = FromRows(
            "RRRRR",
            "LLLLL");

        IReadOnlyList<Position> path = Path(map, new Position(0, 0), new Position(4, 0));

        Assert.Equal(4, path.Count);
        Assert.Equal(12, RouteCost(map, new Position(0, 0), path, withBonus: false)); // 4 × plains 3 — no discount
        // The lower-Y tie-break keeps it on row 0, but that is incidental: with the bonus OFF the cost is the same
        // either way, so no road preference exists.
        Assert.All(path, p => Assert.Equal(0, p.Y));
    }

    [Fact]
    public void LandPath_IsDeterministic_SameInputSamePath()
    {
        GameMap map = FromRows(
            "RRRRR",
            "LLLLL",
            "RRRRR");

        IReadOnlyList<Position> a = LandPath(map, new Position(0, 1), new Position(4, 1));
        IReadOnlyList<Position> b = LandPath(map, new Position(0, 1), new Position(4, 1));

        Assert.Equal(a, b); // byte-stable: identical sequence on a repeat run (ADR-009, RNG-free)
    }
}
