using CrownAndColony.GameLogic.Randomness;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.World;

public class PositionTests
{
    [Theory]
    [InlineData(5, 5, 5, 6, true)]   // south
    [InlineData(5, 5, 6, 6, true)]   // diagonal
    [InlineData(5, 5, 4, 4, true)]   // diagonal
    [InlineData(5, 5, 5, 5, false)]  // self
    [InlineData(5, 5, 7, 5, false)]  // two away
    [InlineData(5, 5, 6, 7, false)]  // knight's move
    public void IsAdjacentTo_Allows8Directions(int x1, int y1, int x2, int y2, bool expected)
    {
        Assert.Equal(expected, new Position(x1, y1).IsAdjacentTo(new Position(x2, y2)));
    }

    [Fact]
    public void Neighbours_Returns8Positions_AllAdjacent()
    {
        var origin = new Position(3, 3);
        var neighbours = origin.Neighbours().ToList();

        Assert.Equal(8, neighbours.Count);
        Assert.Equal(8, neighbours.Distinct().Count());
        Assert.All(neighbours, n => Assert.True(origin.IsAdjacentTo(n)));
    }
}

public class GameMapTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    private static GameMap TwoByTwo()
    {
        TerrainType plains = Classic.Terrain("model.tile.plains");
        TerrainType ocean = Classic.Terrain("model.tile.ocean");
        return new GameMap(2, 2, [plains, ocean, ocean, plains]);
    }

    [Fact]
    public void TerrainAt_ReadsRowMajor()
    {
        GameMap map = TwoByTwo();

        Assert.Equal("model.tile.plains", map.TerrainAt(new Position(0, 0)).Id);
        Assert.Equal("model.tile.ocean", map.TerrainAt(new Position(1, 0)).Id);
        Assert.Equal("model.tile.ocean", map.TerrainAt(new Position(0, 1)).Id);
        Assert.Equal("model.tile.plains", map.TerrainAt(new Position(1, 1)).Id);
    }

    [Fact]
    public void InBounds_And_OffMapAccess()
    {
        GameMap map = TwoByTwo();

        Assert.True(map.InBounds(new Position(0, 0)));
        Assert.True(map.InBounds(new Position(1, 1)));
        Assert.False(map.InBounds(new Position(2, 0)));
        Assert.False(map.InBounds(new Position(0, -1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.TerrainAt(new Position(2, 2)));
    }

    [Fact]
    public void Constructor_RejectsMismatchedTerrainArray()
    {
        TerrainType plains = Classic.Terrain("model.tile.plains");
        Assert.Throws<ArgumentException>(() => new GameMap(2, 2, [plains]));
    }
}

public class MapGeneratorTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    [Fact]
    public void SameSeed_GeneratesIdenticalMap()
    {
        GameMap a = MapGenerator.Generate(Classic, 24, 16, new Pcg32Random(seed: 5));
        GameMap b = MapGenerator.Generate(Classic, 24, 16, new Pcg32Random(seed: 5));

        Assert.Equal(
            a.AllPositions().Select(p => a.TerrainAt(p).Id),
            b.AllPositions().Select(p => b.TerrainAt(p).Id));
    }

    [Fact]
    public void DifferentSeeds_GenerateDifferentMaps()
    {
        GameMap a = MapGenerator.Generate(Classic, 24, 16, new Pcg32Random(seed: 5));
        GameMap b = MapGenerator.Generate(Classic, 24, 16, new Pcg32Random(seed: 6));

        Assert.NotEqual(
            a.AllPositions().Select(p => a.TerrainAt(p).Id).ToList(),
            b.AllPositions().Select(p => b.TerrainAt(p).Id).ToList());
    }

    [Fact]
    public void Map_HasOceanBorder_AndLandInterior()
    {
        GameMap map = MapGenerator.Generate(Classic, 24, 16, new Pcg32Random(seed: 1));

        // Border ring (2 tiles) is water.
        Assert.All(
            map.AllPositions().Where(p => p.X < 2 || p.Y < 2 || p.X >= 22 || p.Y >= 14),
            p => Assert.True(map.TerrainAt(p).IsWater, $"{p} should be ocean"));

        // Interior is land.
        Assert.All(
            map.AllPositions().Where(p => p.X is >= 2 and < 22 && p.Y is >= 2 and < 14),
            p => Assert.False(map.TerrainAt(p).IsWater, $"{p} should be land"));
    }
}
