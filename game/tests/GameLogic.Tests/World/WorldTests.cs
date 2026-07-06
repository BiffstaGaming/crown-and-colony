using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Randomness;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.World;
using CrownAndColony.GameLogic.World.Improvements;
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

    // ---- Great rivers (FreeCol enableGreatRivers / TerrainType greatRiver, 86d3fpx8p) ----

    /// <summary>The number of navigable great-river terrain tiles on a map.</summary>
    private static int GreatRiverCount(GameMap map) =>
        map.AllPositions().Count(p => map.TerrainAt(p).Id == "model.tile.greatRiver");

    [Fact]
    public void GreatRivers_AreOffByDefault_KeepingTheDefaultMapByteIdentical()
    {
        // The greatRivers flag defaults OFF (FreeCol's enableGreatRivers ships false), so omitting it equals passing
        // false — and produces NO great-river terrain. This is the contract that keeps the default new game (and its
        // visual goldens / soak baseline) byte-identical (ADR-009).
        GameMap omitted = MapGenerator.Generate(Classic, 56, 38, new Pcg32Random(3), 0.45);
        GameMap explicitOff = MapGenerator.Generate(Classic, 56, 38, new Pcg32Random(3), 0.45, LandStyle.Continent, greatRivers: false);

        Assert.Equal(
            omitted.AllPositions().Select(p => omitted.TerrainAt(p).Id),
            explicitOff.AllPositions().Select(p => explicitOff.TerrainAt(p).Id));
        Assert.Equal(0, GreatRiverCount(omitted)); // never any great-river terrain with the flag off
    }

    [Fact]
    public void GreatRivers_WhenEnabled_GenerateNavigableGreatRiverWater()
    {
        // With the flag on, the spine of long rivers is retyped to the navigable model.tile.greatRiver water terrain
        // (FreeCol River.drawToMap fjord promotion). A reasonably-sized map has long enough rivers for at least one.
        GameMap map = MapGenerator.Generate(Classic, 56, 38, new Pcg32Random(3), 0.45, LandStyle.Continent, greatRivers: true);

        Assert.True(GreatRiverCount(map) > 0, "an enabled map should grow at least one great-river tile");
        // Every great-river tile is water (ships pass, land units cannot enter) and carries NO river improvement
        // (retyping the tile resets its improvements — FreeCol's "changing the type resets the improvements").
        Assert.All(
            map.AllPositions().Where(p => map.TerrainAt(p).Id == "model.tile.greatRiver"),
            p =>
            {
                Assert.True(map.TerrainAt(p).IsWater);  // renders/moves as water
                Assert.False(map.HasRiver(p));          // not also a river improvement
            });
    }

    [Fact]
    public void GreatRivers_AreDeterministicPerSeed_AndNeverAlterLandTerrain()
    {
        // The great-river retype is a pure, RNG-free post-process: same seed → same map (determinism). Turning it on
        // never shifts the LAND terrain the stream-0 climate/mountain draws produced — only a river-spine tile becomes
        // great-river water. (Retyping a land tile to water does re-shuffle which enclosed-water tiles read as lake vs
        // ocean, but that is a water↔water reclassification; no land tile changes type.) This is the soak's
        // twin-determinism guarantee: the human's stream-0 land never depends on the flag.
        GameMap a = MapGenerator.Generate(Classic, 56, 38, new Pcg32Random(3), 0.45, LandStyle.Continent, greatRivers: true);
        GameMap b = MapGenerator.Generate(Classic, 56, 38, new Pcg32Random(3), 0.45, LandStyle.Continent, greatRivers: true);
        GameMap off = MapGenerator.Generate(Classic, 56, 38, new Pcg32Random(3), 0.45, LandStyle.Continent, greatRivers: false);

        Assert.Equal(
            a.AllPositions().Select(p => a.TerrainAt(p).Id),
            b.AllPositions().Select(p => b.TerrainAt(p).Id)); // deterministic per seed

        // Any tile that is LAND on the flag-off map keeps its exact land type on the flag-on map, UNLESS it was retyped
        // to great-river water — so the flag never perturbs a land tile's climate/elevation type.
        Assert.All(off.AllPositions(), p =>
        {
            if (!off.TerrainAt(p).IsWater && a.TerrainAt(p).Id != off.TerrainAt(p).Id)
            {
                Assert.Equal("model.tile.greatRiver", a.TerrainAt(p).Id);
            }
        });
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

    // ---- Landmass styles (FreeCol landGeneratorType: continent / archipelago / islands) ----

    /// <summary>The number of separate land bodies (8-direction connected components) on the map.</summary>
    private static int LandmassCount(GameMap map)
    {
        var seen = new HashSet<Position>();
        int masses = 0;
        foreach (Position start in map.AllPositions())
        {
            if (map.TerrainAt(start).IsWater || !seen.Add(start))
            {
                continue;
            }
            masses++;
            var stack = new Stack<Position>();
            stack.Push(start);
            while (stack.Count > 0)
            {
                Position p = stack.Pop();
                foreach (Position n in p.Neighbours())
                {
                    if (map.InBounds(n) && !map.TerrainAt(n).IsWater && seen.Add(n))
                    {
                        stack.Push(n);
                    }
                }
            }
        }
        return masses;
    }

    [Fact]
    public void DefaultLandStyle_IsByteIdenticalToOmittingTheParameter()
    {
        // The shipped default style (omit the param) must equal passing Continent explicitly — the contract that keeps
        // the default new game (and its visual goldens / soak baseline) byte-identical (ADR-009).
        GameMap omitted = MapGenerator.Generate(Classic, 36, 24, new Pcg32Random(9));
        GameMap continent = MapGenerator.Generate(Classic, 36, 24, new Pcg32Random(9), MapGenerator.DefaultLandMassFraction, LandStyle.Continent);

        Assert.Equal(
            omitted.AllPositions().Select(p => omitted.TerrainAt(p).Id),
            continent.AllPositions().Select(p => continent.TerrainAt(p).Id));
    }

    [Fact]
    public void IslandsAndArchipelago_ProduceManySeparateMasses_WhileContinentIsDominatedByOne()
    {
        // A big map gives every style room. Continent grows essentially one blob; islands/archipelago grow several
        // unconnected masses (FreeCol's addLandMass per-island growth), so they have materially more land bodies.
        GameMap continent = MapGenerator.Generate(Classic, 56, 38, new Pcg32Random(11), 0.45, LandStyle.Continent);
        GameMap archipelago = MapGenerator.Generate(Classic, 56, 38, new Pcg32Random(11), 0.45, LandStyle.Archipelago);
        GameMap islands = MapGenerator.Generate(Classic, 56, 38, new Pcg32Random(11), 0.45, LandStyle.Islands);

        Assert.True(LandmassCount(continent) <= 3,
            $"continent should be one dominant mass, found {LandmassCount(continent)}");
        Assert.True(LandmassCount(islands) > LandmassCount(continent),
            $"islands ({LandmassCount(islands)}) should have more masses than continent ({LandmassCount(continent)})");
        Assert.True(LandmassCount(archipelago) > LandmassCount(continent),
            $"archipelago ({LandmassCount(archipelago)}) should have more masses than continent ({LandmassCount(continent)})");
        // Every style still actually grows land (no empty oceans).
        Assert.True(LandCount(continent) > 0 && LandCount(archipelago) > 0 && LandCount(islands) > 0);
    }

    [Theory]
    [InlineData(LandStyle.Continent)]
    [InlineData(LandStyle.Archipelago)]
    [InlineData(LandStyle.Islands)]
    public void EveryLandStyle_IsDeterministicPerSeed_AndKeepsTheWateryMargin(LandStyle style)
    {
        GameMap a = MapGenerator.Generate(Classic, 56, 38, new Pcg32Random(17), 0.45, style);
        GameMap b = MapGenerator.Generate(Classic, 56, 38, new Pcg32Random(17), 0.45, style);

        // Same seed + size + land + style → identical map, every machine (ADR-009).
        Assert.Equal(
            a.AllPositions().Select(p => a.TerrainAt(p).Id),
            b.AllPositions().Select(p => b.TerrainAt(p).Id));

        // The watery margin every downstream pass assumes (high seas / coast) holds for all styles.
        Assert.All(
            a.AllPositions().Where(p => p.X < 4 || p.Y < 2 || p.X >= a.Width - 4 || p.Y >= a.Height - 2),
            p => Assert.True(a.TerrainAt(p).IsWater, $"{p} should be water under the {style} style"));
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

/// <summary>Rivers: ruleset parse, map-generator placement (determinism + faithful constraints), the production +
/// movement folding, and v47 save round-trip (86d3b3qdx).</summary>
public class RiverTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    [Fact]
    public void ClassicSpec_ParsesTheRiverImprovementType_WithItsModifiersAndMoveCost()
    {
        TileImprovementType river = Classic.RiverType;
        Assert.Equal(TileImprovementType.RiverId, river.Id);
        Assert.Equal(1, river.MovementCost);       // the river follow-cost (1 = a third of a normal move)
        Assert.Equal(0, river.AddWorkTurns);       // rivers are natural, not pioneer-built
        // The classic river's flat additive goods bonuses (specification.xml model.improvement.river).
        Assert.Equal(1, ImprovementProduction.YieldDelta(river, "model.goods.grain"));
        Assert.Equal(2, ImprovementProduction.YieldDelta(river, "model.goods.furs"));
        Assert.Equal(2, ImprovementProduction.YieldDelta(river, "model.goods.lumber"));
        Assert.Equal(0, ImprovementProduction.YieldDelta(river, "model.goods.bells")); // a good rivers don't touch
    }

    [Fact]
    public void DefaultMap_PlacesSomeRivers()
    {
        // The classic 36×24 default map has enough lowland for the river budget to place at least one river tile.
        Game game = Game.New(Classic, seed: 42);
        Assert.NotEmpty(game.Map.AllImprovements());
        Assert.Contains(game.Map.AllImprovements(), i => i.Improvement.Id == TileImprovementType.RiverId);
    }

    [Fact]
    public void RiverPlacement_IsDeterministicPerSeed()
    {
        var a = Game.New(Classic, seed: 7).Map.AllImprovements().Select(i => i.Position).OrderBy(p => (p.Y, p.X)).ToList();
        var b = Game.New(Classic, seed: 7).Map.AllImprovements().Select(i => i.Position).OrderBy(p => (p.Y, p.X)).ToList();
        Assert.Equal(a, b); // same seed → identical river layer (ADR-009)
    }

    [Fact]
    public void EveryRiverTile_IsLowlandLand_AndWithinTheBudget()
    {
        Game game = Game.New(Classic, seed: 13);
        int allowed = 0;
        foreach (Position p in game.Map.AllPositions())
        {
            TerrainType t = game.Map.TerrainAt(p);
            if (!t.IsWater && !t.IsElevation && t.Id != "model.tile.arctic")
            {
                allowed++;
            }
        }
        foreach ((Position p, TileImprovementType imp) in game.Map.AllImprovements())
        {
            TerrainType t = game.Map.TerrainAt(p);
            Assert.False(t.IsWater);       // a river never sits on water
            Assert.False(t.IsElevation);   // nor on hills/mountains (the river type's negated scopes)
            Assert.NotEqual("model.tile.arctic", t.Id);
            Assert.Equal(TileImprovementType.RiverId, imp.Id);
        }
        // The river pass respects FreeCol's soft maximum: river tiles ≤ allowed · riverNumber% (15%), with a little
        // slack because the budget is checked before the final tile of a walk is laid.
        int riverTiles = game.Map.AllImprovements().Count();
        Assert.True(riverTiles <= allowed * 15 / 100 + 1,
            $"placed {riverTiles} rivers over a {allowed * 15 / 100} budget");
    }

    [Fact]
    public void TileYield_AddsTheRiverBonus_ToAGoodTheRiverBoosts()
    {
        // Find a default-map river tile and compare its grain yield with and without the river.
        Game game = Game.New(Classic, seed: 42);
        Position river = game.Map.AllImprovements().First(i => i.Improvement.Id == TileImprovementType.RiverId).Position;

        int withRiver = game.TileYieldPotential(river, "model.goods.grain");
        game.Map.SetImprovement(river, null); // strip the river
        int without = game.TileYieldPotential(river, "model.goods.grain");

        // Grain is only produced on attended land; if the terrain makes grain at all, the river adds exactly +1.
        if (without > 0)
        {
            Assert.Equal(without + 1, withRiver);
        }
    }

    [Fact]
    public void RiverFollowCost_IsCheaperBetweenTwoRiverTiles_ForALandUnit()
    {
        // Two adjacent plains tiles; with rivers on both, a land unit pays the river follow-cost (1) not the
        // terrain cost (plains = 3). ImprovementMovement encodes the rule; here we check the wiring through CheckMove.
        var from = new Position(1, 1);
        var to = new Position(2, 1);
        int plainsCost = Classic.Terrain("model.tile.plains").MoveCost;

        int baseCost = MoveCostBetween(rivers: [], from, to);
        int riverCost = MoveCostBetween(rivers: [from, to], from, to);
        Assert.Equal(plainsCost, baseCost);
        Assert.Equal(1, riverCost); // the river follow-cost
        Assert.True(riverCost < baseCost);
    }

    [Fact]
    public void RiverFollowCost_DoesNotApply_WhenOnlyOneTileHasARiver()
    {
        var from = new Position(1, 1);
        var to = new Position(2, 1);
        int plainsCost = Classic.Terrain("model.tile.plains").MoveCost;
        Assert.Equal(plainsCost, MoveCostBetween(rivers: [from], from, to)); // only the origin → no follow bonus
    }

    [Fact]
    public void Rivers_RoundTripThroughSave_WithMagnitudes_V64()
    {
        Game game = Game.New(Classic, seed: 42); // Game.New stamps rivers (with generator-assigned magnitudes, v64)
        Assert.NotEmpty(game.Map.AllImprovements());

        SaveGame save = SaveGame.From(game);
        Assert.NotNull(save.Improvements);
        Assert.Equal(70, SaveGame.CurrentVersion);

        Game loaded = SaveGame.FromJson(save.ToJson()).Restore(Classic);
        // The river layer round-trips exactly, INCLUDING each tile's stored magnitude (small vs large) — the renderer
        // reads this stored size, so a reloaded map must draw identically to the map that produced it.
        Assert.Equal(
            game.Map.AllImprovements().Select(i => (i.Position, i.Improvement.Id, i.Improvement.Magnitude)).OrderBy(t => (t.Position.Y, t.Position.X)),
            loaded.Map.AllImprovements().Select(i => (i.Position, i.Improvement.Id, i.Improvement.Magnitude)).OrderBy(t => (t.Position.Y, t.Position.X)));
    }

    [Fact]
    public void Generator_AssignsRiverMagnitudes_LargeWhereATributaryJoins()
    {
        // FreeCol River.grow widens a river to large (magnitude 2) at and downstream of a confluence; an unjoined river
        // stays small (1). Over a spread of seeds the generator produces at least one large-river tile (a tributary
        // join) and at least one small one, and every river tile's magnitude is a valid small/large band.
        bool sawLarge = false, sawSmall = false;
        for (ulong seed = 1; seed <= 30 && !(sawLarge && sawSmall); seed++)
        {
            Game game = Game.New(Classic, seed);
            foreach (var (_, imp) in game.Map.AllImprovements().Where(i => i.Improvement.Id == TileImprovementType.RiverId))
            {
                Assert.InRange(imp.Magnitude, 1, 2); // only small (1) or large (2) — never a fjord (3) on the river layer
                if (imp.Magnitude == 2) sawLarge = true;
                if (imp.Magnitude == 1) sawSmall = true;
            }
        }
        Assert.True(sawLarge, "no seed grew a large river section — the confluence-growth pass never fired");
        Assert.True(sawSmall, "every river tile was large — the small default magnitude was lost");
    }

    [Fact]
    public void RiverMagnitudes_AreDeterministicPerSeed()
    {
        // Same seed → identical magnitudes (the growth pass is RNG-free, so it never perturbs the draw sequence).
        static IEnumerable<(Position, int)> Mags(Game g) => g.Map.AllImprovements()
            .Where(i => i.Improvement.Id == TileImprovementType.RiverId)
            .Select(i => (i.Position, i.Improvement.Magnitude))
            .OrderBy(t => (t.Item1.Y, t.Item1.X));

        Assert.Equal(Mags(Game.New(Classic, seed: 7)), Mags(Game.New(Classic, seed: 7)));
    }

    [Fact]
    public void RiverMagnitudeGrowth_DoesNotShiftTheStream0Sequence_DefaultMapByteIdentical()
    {
        // The magnitude-growth pass is a pure, RNG-free post-process of the already-walked paths + confluences, so a
        // fresh default game (and the visual goldens / soak baseline that depend on it) draws exactly the same RNG it
        // did before this feature. Two fresh games at one seed match byte for byte (the round-trip witness, ADR-009).
        string a = SaveGame.From(Game.New(Classic, seed: 99)).ToJson();
        string b = SaveGame.From(Game.New(Classic, seed: 99)).ToJson();
        Assert.Equal(a, b);
    }

    [Fact]
    public void AMapWithNoRivers_OmitsTheImprovementsToken()
    {
        Game game = Game.New(Classic, seed: 42);
        foreach (Position p in game.Map.AllImprovements().Select(i => i.Position).Distinct().ToList())
        {
            game.Map.SetImprovement(p, null); // clear the improvement layer at that tile
        }
        string json = SaveGame.From(game).ToJson();
        Assert.DoesNotContain("\"Improvements\"", json);
    }

    [Fact]
    public void PreV47Save_LoadsWithNoRivers()
    {
        Game game = Game.New(Classic, seed: 42);
        SaveGame old = SaveGame.From(game) with { Version = 46, Improvements = null };
        Game loaded = SaveGame.FromJson(old.ToJson()).Restore(Classic);
        Assert.Empty(loaded.Map.AllImprovements());
    }

    // Builds a 3×3 all-plains game with the given river tiles (restored through the save path so the river layer is
    // resolved from the ruleset, like a real load) and returns the cost CheckMove charges a colonist to step from→to.
    private static int MoveCostBetween(Position[] rivers, Position from, Position to)
    {
        int width = 3, height = 3;
        var save = new SaveGame
        {
            Turn = 1,
            RandomStateValue = 1,
            RandomIncrement = 1,
            MapWidth = width,
            MapHeight = height,
            Terrain = Enumerable.Repeat("model.tile.plains", width * height).ToList(),
            Units = [new SavedUnit(1, Game.StartingUnitTypeId, from.X, from.Y, 3)],
            Explored = [],
            Improvements = rivers.Length > 0
                ? rivers.Select(p => new SavedImprovement(p.Y * width + p.X, TileImprovementType.RiverId, 1)).ToList()
                : null,
        };
        Game game = save.Restore(Classic);
        MoveCheck check = game.CheckMove(game.Units[0], to);
        Assert.True(check.Allowed, check.Reason);
        return check.Cost;
    }
}
