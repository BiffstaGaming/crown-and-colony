using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
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

    private static int LandCount(GameMap map) =>
        map.AllPositions().Count(p => !map.TerrainAt(p).IsWater);

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
    public void MapEdges_AreWater_WithAHighSeasLandProximityBand()
    {
        GameMap map = Generate(1);

        // Watery margin: 4 columns east/west, 2 rows north/south.
        Assert.All(
            map.AllPositions().Where(p => p.X < 4 || p.Y < 2 || p.X >= map.Width - 4 || p.Y >= map.Height - 2),
            p => Assert.True(map.TerrainAt(p).IsWater, $"{p} should be water"));

        // High seas is a land-proximity band along the east/west edges (FreeCol resetHighSeas), not fixed columns.
        var highSeas = map.AllPositions().Where(p => map.TerrainAt(p).Id == "model.tile.highSeas").ToList();
        Assert.NotEmpty(highSeas);
        Assert.Contains(highSeas, p => p.X < map.Width / 2);  // a west exit to Europe
        Assert.Contains(highSeas, p => p.X >= map.Width / 2); // an east exit to Europe

        // Every high-seas tile is water and sits within the near-edge band (≤ 10 columns from an edge).
        Assert.All(highSeas, p =>
        {
            Assert.True(map.TerrainAt(p).IsWater, $"{p} high seas should be water");
            Assert.True(p.X < 10 || p.X >= map.Width - 10, $"{p} high seas should hug an edge");
        });

        // The band rule is genuine (not just the per-side fallback): at least one high-seas tile has no land
        // within Chebyshev distance 4 — open sea, exactly the resetHighSeas condition.
        Assert.Contains(highSeas, p => !map.AllPositions().Any(q =>
            !map.TerrainAt(q).IsWater && System.Math.Max(System.Math.Abs(q.X - p.X), System.Math.Abs(q.Y - p.Y)) <= 4));
    }

    [Fact]
    public void GeneratedMap_RetypesEnclosedLakeRegionsToLakeTerrain()
    {
        // Enclosed water (the Lake regions RegionGenerator finds) is retyped to model.tile.lake terrain (FreeCol
        // makeLakes). Seed 424242 generates enclosed lakes; assert region and terrain agree, both ways.
        GameMap map = Generate(424242);

        var lakeRegionTiles = map.AllPositions().Where(p => map.RegionOf(p)!.Type == RegionType.Lake).ToList();
        Assert.NotEmpty(lakeRegionTiles); // non-vacuous: this seed has enclosed lakes
        Assert.All(lakeRegionTiles, p => Assert.Equal("model.tile.lake", map.TerrainAt(p).Id));

        // And no tile is lake terrain without being a Lake region (the retype is exactly the Lake-region set).
        Assert.All(
            map.AllPositions().Where(p => map.TerrainAt(p).Id == "model.tile.lake"),
            p => Assert.Equal(RegionType.Lake, map.RegionOf(p)!.Type));
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
    public void WaterResources_OnlyBorderLand_AndLandDensityMatchesTheBonusNumber()
    {
        // FreeCol perhapsAddBonus (86d3c9wbp): land bonuses at ~bonusNumber% (10); water resources only where a tile
        // borders MORE THAN ONE land tile (open ocean stays bare), at 1/(10−adjacentLand) odds. A larger map gives a
        // meaningful sample.
        GameMap map = MapGenerator.Generate(Classic, 56, 38, new Pcg32Random(42), 0.45);

        // (a) Every water tile that hosts a resource borders > 1 land tile (the adjacency gate holds exactly).
        foreach ((Position p, string _) in map.Resources.Where(kv => map.TerrainAt(kv.Key).IsWater))
        {
            int landNeighbours = p.Neighbours().Count(n => map.InBounds(n) && !map.TerrainAt(n).IsWater);
            Assert.True(landNeighbours > 1, $"water resource at {p} borders only {landNeighbours} land tile(s)");
        }

        // (b) Land-resource density over eligible land (terrain that has a resource table) sits around the 10% bonus number.
        var eligibleLand = map.AllPositions()
            .Where(p => !map.TerrainAt(p).IsWater && map.TerrainAt(p).Resources.Count > 0)
            .ToList();
        Assert.NotEmpty(eligibleLand);
        double density = eligibleLand.Count(p => map.ResourceAt(p) is not null) / (double)eligibleLand.Count;
        Assert.InRange(density, 0.05, 0.16); // ~10%, with sampling slack
    }

    [Fact]
    public void DefaultLandMass_IsByteIdenticalToOmittingTheParameter()
    {
        // The shipped default (omit the param) must equal passing DefaultLandMassFraction explicitly — the contract
        // that keeps the default new game (and its visual goldens / soak baseline) byte-identical (ADR-009).
        GameMap omitted = MapGenerator.Generate(Classic, 36, 24, new Pcg32Random(9));
        GameMap explicitDefault = MapGenerator.Generate(Classic, 36, 24, new Pcg32Random(9), MapGenerator.DefaultLandMassFraction);

        Assert.Equal(
            omitted.AllPositions().Select(p => omitted.TerrainAt(p).Id),
            explicitDefault.AllPositions().Select(p => explicitDefault.TerrainAt(p).Id));
    }

    [Fact]
    public void HigherLandMass_GrowsMoreLand()
    {
        // Same seed + size, only the land fraction differs: more requested land → more land tiles.
        GameMap sparse = MapGenerator.Generate(Classic, 36, 24, new Pcg32Random(7), 0.30);
        GameMap dense = MapGenerator.Generate(Classic, 36, 24, new Pcg32Random(7), 0.50);

        Assert.True(LandCount(dense) > LandCount(sparse),
            $"0.50 land ({LandCount(dense)}) should exceed 0.30 land ({LandCount(sparse)})");
        // Each lands near its target (the interior is large enough that the frontier reaches it).
        Assert.InRange(LandCount(sparse) / (double)(36 * 24), 0.22, 0.38);
        Assert.InRange(LandCount(dense) / (double)(36 * 24), 0.42, 0.55);
    }

    [Fact]
    public void NonDefaultSize_GeneratesDeterministically_AndKeepsTheWateryMargin()
    {
        foreach ((int w, int h) in new[] { (30, 20), (56, 38) })
        {
            GameMap a = MapGenerator.Generate(Classic, w, h, new Pcg32Random(4), 0.45);
            GameMap b = MapGenerator.Generate(Classic, w, h, new Pcg32Random(4), 0.45);

            Assert.Equal(w, a.Width);
            Assert.Equal(h, a.Height);
            // Same seed + size + land mass → identical map (ADR-009), independent of the default size.
            Assert.Equal(
                a.AllPositions().Select(p => a.TerrainAt(p).Id),
                b.AllPositions().Select(p => b.TerrainAt(p).Id));

            // The watery-margin invariant is size-relative, not hard-wired to 36×24.
            Assert.All(
                a.AllPositions().Where(p => p.X < 4 || p.Y < 2 || p.X >= a.Width - 4 || p.Y >= a.Height - 2),
                p => Assert.True(a.TerrainAt(p).IsWater, $"{p} should be water on a {w}×{h} map"));
            // High seas is now a land-proximity band hugging both edges (FreeCol resetHighSeas), not fixed columns:
            // both sides have an exit and every high-seas tile sits within the near-edge band and is water.
            var highSeas = a.AllPositions().Where(p => a.TerrainAt(p).Id == "model.tile.highSeas").ToList();
            Assert.Contains(highSeas, p => p.X < a.Width / 2);
            Assert.Contains(highSeas, p => p.X >= a.Width / 2);
            Assert.All(highSeas, p =>
            {
                Assert.True(a.TerrainAt(p).IsWater);
                Assert.True(p.X < 10 || p.X >= a.Width - 10, $"{p} high seas should hug an edge on a {w}×{h} map");
            });
        }
    }

    private static int ElevationCount(GameMap map) =>
        map.AllPositions().Count(p => map.TerrainAt(p).IsElevation);

    [Fact]
    public void Mountains_FormContiguousRanges_NotIsolatedScatter()
    {
        // The directional-walk generator (FreeCol createMountains) grows ridgelines: most elevation tiles touch
        // another elevation tile (8-neighbour), unlike the old per-tile altitude scatter where lone peaks abounded.
        GameMap map = Generate(7);

        var elevation = map.AllPositions().Where(p => map.TerrainAt(p).IsElevation).ToList();
        Assert.NotEmpty(elevation); // a 36x24 map has mountains/hills

        bool HasElevationNeighbour(Position p) => p.Neighbours()
            .Any(n => map.InBounds(n) && map.TerrainAt(n).IsElevation);

        int connected = elevation.Count(HasElevationNeighbour);
        // Ranges, not noise: the clear majority of elevation tiles sit beside another elevation tile.
        Assert.True(connected >= elevation.Count * 0.7,
            $"only {connected}/{elevation.Count} elevation tiles have an elevation neighbour — looks like scatter, not ranges");

        // And at least one genuine chain of three mountains in a line exists (a ridgeline, not just a 2x2 clump).
        bool HasMountain(Position p) => map.InBounds(p) && map.TerrainAt(p).Id == "model.tile.mountains";
        (int, int)[] dirs = [(1, 0), (0, 1), (1, 1), (1, -1)];
        Assert.Contains(map.AllPositions().Where(p => HasMountain(p)), p =>
            dirs.Any(d => HasMountain(new Position(p.X + d.Item1, p.Y + d.Item2))
                       && HasMountain(new Position(p.X + 2 * d.Item1, p.Y + 2 * d.Item2))));
    }

    [Fact]
    public void ElevationCount_ScalesWithMapSize()
    {
        // The mountain/hill budget is a fraction of land (FreeCol's mountainNumber), so a much bigger map of the
        // same land fraction carries materially more elevation. Same seed isolates size as the only variable.
        GameMap small = MapGenerator.Generate(Classic, 30, 20, new Pcg32Random(7), 0.45);
        GameMap large = MapGenerator.Generate(Classic, 56, 38, new Pcg32Random(7), 0.45);

        Assert.True(ElevationCount(small) > 0 && ElevationCount(large) > 0, "both maps should have elevation");
        Assert.True(ElevationCount(large) > ElevationCount(small),
            $"large map elevation ({ElevationCount(large)}) should exceed small ({ElevationCount(small)})");
    }

    [Fact]
    public void Mountains_AreDeterministicPerSeed()
    {
        // The range walk draws RNG; the same seed must still lay the identical elevation, on every machine (ADR-009).
        GameMap a = Generate(13);
        GameMap b = Generate(13);

        Assert.Equal(
            a.AllPositions().Where(p => a.TerrainAt(p).IsElevation).ToList(),
            b.AllPositions().Where(p => b.TerrainAt(p).IsElevation).ToList());
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

/// <summary>
/// Per-resource starting quantity (<c>86d3c9wbp</c> facet): the spec min/max range parsed onto <see cref="ResourceType"/>,
/// the deterministic roll, the <see cref="GameMap"/> quantity layer, and its save round-trip (v46, omit-when-default).
/// The gen-time placement is deferred (rolling at game start would break a default game's byte-stability — most maps
/// already place finite resources), so the roller is exercised directly here.
/// </summary>
public class ResourceQuantityTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    [Theory]
    [InlineData("model.resource.minerals", 40, 400)]
    [InlineData("model.resource.ore", 200, 4000)]
    [InlineData("model.resource.silver", 80, 800)]
    public void ClassicSpec_ParsesTheFiniteResourceRanges(string id, int min, int max)
    {
        ResourceType type = Classic.Resource(id);
        Assert.True(type.HasQuantityRange);
        Assert.Equal(min, type.MinValue);
        Assert.Equal(max, type.MaxValue);
    }

    [Fact]
    public void ALimitlessResource_HasNoRange()
    {
        // lumber/furs/grain carry no minimum/maximum-value in the classic spec → limitless.
        ResourceType type = Classic.Resource("model.resource.lumber");
        Assert.False(type.HasQuantityRange);
        Assert.Equal(0, type.MinValue);
        Assert.Equal(0, type.MaxValue);
        Assert.Equal(0, type.RollQuantity(new Pcg32Random(1))); // limitless rolls nothing
    }

    [Fact]
    public void RollQuantity_StaysWithinTheInclusiveRange_AndIsDeterministic()
    {
        ResourceType minerals = Classic.Resource("model.resource.minerals");
        for (ulong seed = 0; seed < 50; seed++)
        {
            int q = minerals.RollQuantity(new Pcg32Random(seed));
            Assert.InRange(q, minerals.MinValue, minerals.MaxValue);
            Assert.Equal(q, minerals.RollQuantity(new Pcg32Random(seed))); // same seed → same roll (ADR-009)
        }
    }

    [Fact]
    public void Roller_AssignsAFiniteQuantityToEachFiniteResource_AndSkipsLimitlessOnes()
    {
        Game game = Game.New(Classic, seed: 42);
        // Game.New already ran the roll; calling it again re-rolls the reserved stream identically (idempotent).
        game.RollResourceQuantities(seed: 42);

        // Every finite resource on the map now carries a quantity in range; limitless ones carry none.
        foreach ((Position p, string resourceId) in game.Map.Resources)
        {
            ResourceType type = Classic.Resource(resourceId);
            int? q = game.Map.ResourceQuantityAt(p);
            if (type.HasQuantityRange)
            {
                Assert.NotNull(q);
                Assert.InRange(q!.Value, type.MinValue, type.MaxValue);
            }
            else
            {
                Assert.Null(q);
            }
        }
        // The classic default map does place some finite resources, so the roller does real work here.
        Assert.NotEmpty(game.Map.ResourceQuantities);
    }

    [Fact]
    public void GameNew_RollsResourceQuantities_AtGameStart()
    {
        // 86d3c9wbp: the roll is wired into Game.New, so a fresh default game already carries finite quantities
        // (the classic map places minerals/ore/silver) — no explicit roller call needed.
        Game game = Game.New(Classic, seed: 42);
        Assert.NotEmpty(game.Map.ResourceQuantities);
        foreach ((Position p, int q) in game.Map.ResourceQuantities)
        {
            ResourceType type = Classic.Resource(game.Map.ResourceAt(p)!);
            Assert.True(type.HasQuantityRange);
            Assert.InRange(q, type.MinValue, type.MaxValue);
        }
    }

    [Fact]
    public void ResourceQuantities_RoundTripThroughSave()
    {
        Game game = Game.New(Classic, seed: 42); // Game.New rolls the quantities

        SaveGame save = SaveGame.From(game);
        Assert.NotNull(save.ResourceQuantities);

        Game loaded = SaveGame.FromJson(save.ToJson()).Restore(Classic);
        Assert.Equal(
            game.Map.ResourceQuantities.OrderBy(kv => (kv.Key.Y, kv.Key.X)),
            loaded.Map.ResourceQuantities.OrderBy(kv => (kv.Key.Y, kv.Key.X)));
    }

    [Fact]
    public void AMapWithNoFiniteResources_OmitsTheResourceQuantitiesToken()
    {
        // The token is still omit-when-empty: a game whose map carries no finite quantities writes no token.
        Game game = Game.New(Classic, seed: 5);
        foreach (Position p in game.Map.ResourceQuantities.Keys.ToList())
        {
            game.Map.SetResourceQuantity(p, null); // clear every quantity → empty layer
        }
        string json = SaveGame.From(game).ToJson();
        Assert.DoesNotContain("\"ResourceQuantities\"", json);
    }

    [Fact]
    public void PreQuantitySave_LoadsWithNoQuantities()
    {
        Game game = Game.New(Classic, seed: 9); // Game.New rolls the quantities
        // Simulate an old save that predates per-resource quantity: drop the token + back-date the version.
        SaveGame old = SaveGame.From(game) with { Version = 45, ResourceQuantities = null };
        Game loaded = SaveGame.FromJson(old.ToJson()).Restore(Classic);
        Assert.Empty(loaded.Map.ResourceQuantities);
    }
}
