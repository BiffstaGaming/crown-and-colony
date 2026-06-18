using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.World;

/// <summary>
/// Goto pathfinding (<c>86d3c9pfy</c>) — slice 2: the pure deterministic A* <see cref="Pathfinder"/>. Hand-built
/// char maps pin least-cost routing, diagonal preference, cost-aware detours, byte-stable tie-breaking, and no-path.
/// </summary>
public class PathfinderTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    private static TerrainType Terrain(char c) => c switch
    {
        'O' => Classic.Terrain("model.tile.ocean"),     // water — impassable for the land predicate
        'L' => Classic.Terrain("model.tile.plains"),    // cost 3
        'M' => Classic.Terrain("model.tile.mountains"), // cost 9
        _ => throw new ArgumentException($"unknown terrain char '{c}'"),
    };

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
        return new GameMap(w, h, terrain);
    }

    private static IReadOnlyList<Position> Path(GameMap map, Position start, Position goal) =>
        Pathfinder.FindPath(map, start, goal, p => !map.TerrainAt(p).IsWater); // land-only passability

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
        // Direct (0,0)→(1,0)M→(2,0) costs 9+3=12; the plains detour (0,0)→(1,1)→(2,0) costs 3+3=6.
        GameMap map = FromRows("LML", "LLL");
        IReadOnlyList<Position> path = Path(map, new Position(0, 0), new Position(2, 0));

        Assert.DoesNotContain(new Position(1, 0), path); // never enters the mountain
        Assert.Equal(new Position(2, 0), path[^1]);
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
    public void IsDeterministic_AndTieBreaksLowerYThenX()
    {
        // (0,0)→(2,0) on open plains: two equal-cost 2-step routes. The (f,g,Y,X) tie-break takes the first step
        // with the lower Y (the straight (1,0)), not the diagonal (1,1).
        GameMap map = FromRows("LLL", "LLL", "LLL");
        IReadOnlyList<Position> a = Path(map, new Position(0, 0), new Position(2, 0));
        IReadOnlyList<Position> b = Path(map, new Position(0, 0), new Position(2, 0));

        Assert.Equal(a, b);                       // byte-stable across runs
        Assert.Equal(new Position(1, 0), a[0]);   // lower-Y first step wins the tie
        Assert.Equal(2, a.Count);
    }
}
