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

    private static GameMap Generate(ulong seed, int w = 36, int h = 24) =>
        MapGenerator.Generate(Classic, w, h, new Pcg32Random(seed));

    [Fact]
    public void SameSeed_GeneratesIdenticalMap()
    {
        GameMap a = Generate(5);
        GameMap b = Generate(5);

        Assert.Equal(
            a.AllPositions().Select(p => a.TerrainAt(p).Id),
            b.AllPositions().Select(p => b.TerrainAt(p).Id));
    }

    [Fact]
    public void DifferentSeeds_GenerateDifferentMaps()
    {
        GameMap a = Generate(5);
        GameMap b = Generate(6);

        Assert.NotEqual(
            a.AllPositions().Select(p => a.TerrainAt(p).Id).ToList(),
            b.AllPositions().Select(p => b.TerrainAt(p).Id).ToList());
    }

    [Fact]
    public void MapEdges_AreWater_WithHighSeasColumns()
    {
        GameMap map = Generate(1);

        // Watery margin: 4 columns east/west, 2 rows north/south.
        Assert.All(
            map.AllPositions().Where(p => p.X < 4 || p.Y < 2 || p.X >= map.Width - 4 || p.Y >= map.Height - 2),
            p => Assert.True(map.TerrainAt(p).IsWater, $"{p} should be water"));

        // Outermost columns are the route to Europe.
        Assert.All(
            map.AllPositions().Where(p => p.X == 0 || p.X == map.Width - 1),
            p => Assert.Equal("model.tile.highSeas", map.TerrainAt(p).Id));
    }

    [Fact]
    public void Map_HasSubstantialVariedLandmass()
    {
        GameMap map = Generate(7);
        var landTypes = map.AllPositions()
            .Select(map.TerrainAt)
            .Where(t => !t.IsWater)
            .ToList();

        // A real continent: a meaningful share of the map…
        double landFraction = landTypes.Count / (double)(map.Width * map.Height);
        Assert.InRange(landFraction, 0.20, 0.60);

        // …with climate-driven variety, not a monoculture.
        Assert.True(landTypes.Select(t => t.Id).Distinct().Count() >= 5,
            $"only {landTypes.Select(t => t.Id).Distinct().Count()} land types generated");

        // Settleable ground exists (a colonist must be able to start).
        Assert.Contains(landTypes, t => t.CanSettle);
    }

    [Fact]
    public void PolarLand_IsColdTerrain()
    {
        // Land near the poles (top/bottom three rows) must be cold-climate types —
        // the climate envelopes at −20…−10 °C only admit these.
        string[] polarTypes =
        [
            "model.tile.arctic", "model.tile.tundra", "model.tile.borealForest",
            "model.tile.hills", "model.tile.mountains",
        ];

        for (ulong seed = 1; seed <= 5; seed++)
        {
            GameMap map = Generate(seed);
            var polarLand = map.AllPositions()
                .Where(p => p.Y < 3 || p.Y >= map.Height - 3)
                .Select(map.TerrainAt)
                .Where(t => !t.IsWater);

            Assert.All(polarLand, t => Assert.Contains(t.Id, polarTypes));
        }
    }

    [Fact]
    public void BonusResources_AppearSparsely_AndOnlyWhereTheTerrainAllows()
    {
        GameMap map = Generate(11);

        Assert.True(map.Resources.Count > 0, "a 36x24 map should host some bonus resources");
        double fraction = map.Resources.Count / (double)(map.Width * map.Height);
        Assert.InRange(fraction, 0.005, 0.20); // sparse, not carpeted

        Assert.All(map.Resources, kv =>
        {
            TerrainType terrain = map.TerrainAt(kv.Key);
            Assert.Contains(terrain.Resources, r => r.ResourceId == kv.Value);
        });
    }

    [Fact]
    public void BonusResources_AreDeterministicPerSeed()
    {
        GameMap a = Generate(12);
        GameMap b = Generate(12);

        Assert.Equal(
            a.Resources.OrderBy(r => (r.Key.Y, r.Key.X)),
            b.Resources.OrderBy(r => (r.Key.Y, r.Key.X)));
    }

    [Fact]
    public void Tropics_GrowHotTerrain_NotArctic()
    {
        // The equatorial band (middle rows) must never produce polar tiles.
        GameMap map = Generate(3);
        int mid = map.Height / 2;
        var equatorLand = map.AllPositions()
            .Where(p => Math.Abs(p.Y - mid) <= 2)
            .Select(map.TerrainAt)
            .Where(t => !t.IsWater);

        Assert.All(equatorLand, t =>
            Assert.DoesNotContain(t.Id, new[] { "model.tile.arctic", "model.tile.tundra", "model.tile.borealForest" }));
    }
}
