using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Randomness;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.World;
using CrownAndColony.GameLogic.World.Improvements;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.World;

/// <summary>
/// Map regions (<c>86d3c9w12</c>) — slices 2-4: <see cref="RegionGenerator"/> assigns every tile a region
/// (polar bands, ocean quadrants, mountains, per-landmass land split at 75). All assignment is RNG-free and
/// deterministic. Hand-built char fixtures pin the exact rules; the generated map proves full coverage.
/// </summary>
public class RegionGeneratorTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    private static TerrainType Terrain(char c) => c switch
    {
        'O' => Classic.Terrain("model.tile.ocean"),
        'L' => Classic.Terrain("model.tile.plains"),
        'M' => Classic.Terrain("model.tile.mountains"),
        'H' => Classic.Terrain("model.tile.hills"),
        _ => throw new ArgumentException($"unknown terrain char '{c}'"),
    };

    /// <summary>Builds a map from a char grid (one string per row) and runs the region generator over it.</summary>
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
        (int[] ids, IReadOnlyList<Region> regions) = RegionGenerator.Assign(map);
        map.SetRegions(ids, regions);
        return map;
    }

    private static string? KeyAt(GameMap map, int x, int y) => map.RegionOf(new Position(x, y))?.Key;
    private static Region RegionAt(GameMap map, int x, int y) => map.RegionOf(new Position(x, y))!;

    // ── Polar bands ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PolarBands_ClaimLandInTheTopTwoAndBottomThreeRows()
    {
        // Authoritative test for L2 named assertion #1 (both bands), with hand-placed polar land the generator's
        // water margin never produces. Height 8: arctic = rows [0,2); antarctic = rows [8-2-1, 8) = [5,8).
        GameMap map = FromRows(
            "LLLLL", // y0 arctic
            "LLLLL", // y1 arctic
            "OOOOO", // y2
            "OOOOO", // y3
            "OOOOO", // y4
            "LLLLL", // y5 antarctic
            "LLLLL", // y6 antarctic
            "LLLLL"); // y7 antarctic

        for (int x = 0; x < 5; x++)
        {
            Assert.Equal("model.region.arctic", KeyAt(map, x, 0));
            Assert.Equal("model.region.arctic", KeyAt(map, x, 1));
            Assert.Equal("model.region.antarctic", KeyAt(map, x, 5));
            Assert.Equal("model.region.antarctic", KeyAt(map, x, 7));
        }
        Assert.All([map.RegionOf(new Position(0, 0))!, map.RegionOf(new Position(0, 7))!],
            r => Assert.Equal(RegionType.Land, r.Type));
        // The mid-band water is ocean, not polar.
        Assert.Equal(RegionType.Ocean, RegionAt(map, 2, 3).Type);
    }

    [Fact]
    public void MountainInAPolarRow_StaysPolar_NotMountain()
    {
        // A mountain tile sitting in the arctic band is claimed by the polar pass first.
        GameMap map = FromRows(
            "OOOOO",
            "OMOOO", // (1,1) mountain, but arctic row
            "OOOOO",
            "OOOOO",
            "OOOOO",
            "OOOOO");

        Assert.Equal("model.region.arctic", KeyAt(map, 1, 1));
        Assert.Equal(RegionType.Land, RegionAt(map, 1, 1).Type);
    }

    // ── Ocean quadrants ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Oceans_SplitWestPacificEastAtlantic_AndNorthSouth()
    {
        GameMap map = FromRows(
            "OOOOOO",
            "OOOOOO",
            "OOOOOO",
            "OOOOOO",
            "OOOOOO",
            "OOOOOO"); // 6x6 all water, midx=3, midy=3

        Assert.Equal("model.region.northPacific", KeyAt(map, 0, 0));  // west + north
        Assert.Equal("model.region.southPacific", KeyAt(map, 0, 5));  // west + south
        Assert.Equal("model.region.northAtlantic", KeyAt(map, 5, 0)); // east + north
        Assert.Equal("model.region.southAtlantic", KeyAt(map, 5, 5)); // east + south
        Assert.All(map.AllPositions(), p => Assert.Equal(RegionType.Ocean, map.RegionOf(p)!.Type));
    }

    [Fact]
    public void PacificParent_CarriesTheHundredPointDiscoveryScore()
    {
        GameMap map = FromRows("OOOO", "OOOO", "OOOO", "OOOO");
        Region np = RegionAt(map, 0, 0);
        Region pacificParent = map.Regions[np.ParentId!.Value];

        Assert.Equal("model.region.pacific", pacificParent.Key);
        Assert.Equal(100, pacificParent.ScoreValue); // ServerRegion.PACIFIC_SCORE_VALUE
        Assert.Equal(0, map.Regions.First(r => r.Key == "model.region.atlantic").ScoreValue);
    }

    // ── Lakes (enclosed water) ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EnclosedWater_BecomesALakeRegion_WhileOpenOceanStaysOcean()
    {
        // A single water tile (3,3) fully ringed by land, with open ocean all around the ring. The ocean fill
        // reaches the open water from the edges but can never reach the enclosed pocket, so it is tagged as a
        // lake (FreeCol createLakeRegions), while the surrounding sea stays ocean. Land rows 2-4 sit clear of
        // the polar bands (arctic = rows [0,2); antarctic = rows [5,8)).
        GameMap map = FromRows(
            "OOOOOOOOO", // y0
            "OOOOOOOOO", // y1
            "OOLLLOOOO", // y2  land ring (top)
            "OOLOLOOOO", // y3  land | LAKE (3,3) | land
            "OOLLLOOOO", // y4  land ring (bottom)
            "OOOOOOOOO", // y5
            "OOOOOOOOO", // y6
            "OOOOOOOOO");// y7

        Region lake = RegionAt(map, 3, 3);
        Assert.Equal(RegionType.Lake, lake.Type);
        Assert.Null(lake.Key);          // a dynamic region, not a fixed ocean/polar one
        Assert.Equal(0, lake.ScoreValue); // lakes carry no discovery score (FreeCol leaves them score 0)

        // The open sea around the ring is ocean, not lake, and a different region from the pocket.
        Region openSea = RegionAt(map, 0, 3);
        Assert.Equal(RegionType.Ocean, openSea.Type);
        Assert.NotEqual(lake.Id, openSea.Id);

        // The ring itself is dry land.
        Assert.Equal(RegionType.Land, RegionAt(map, 2, 2).Type);

        // Every tile still gets exactly one valid region (no NoRegion sentinel survives the lake pass).
        Assert.All(map.AllPositions(), p => Assert.NotEqual(GameMap.NoRegion, map.RegionIdAt(p)));
    }

    // ── Mountain regions ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MountainRegions_AreContiguousBlocks_ScoredTwicePerTile()
    {
        // A 4-tile mountain block and a lone mountain, both clear of the polar bands, become two regions.
        GameMap map = FromRows(
            "OOOOOOO", // y0
            "OOOOOOO", // y1
            "OOOOOOO", // y2
            "OOMMOOO", // y3  block
            "OOMMOOO", // y4  block
            "OOOOOOO", // y5
            "OOOOMOO", // y6  lone mountain (4,6)
            "OOOOOOO", // y7
            "OOOOOOO", // y8
            "OOOOOOO");// y9

        Region block = RegionAt(map, 2, 3);
        Region lone = RegionAt(map, 4, 6);

        Assert.Equal(RegionType.Mountain, block.Type);
        Assert.Equal(RegionType.Mountain, lone.Type);
        Assert.Equal(block.Id, RegionAt(map, 3, 4).Id); // all four block tiles share one region
        Assert.NotEqual(block.Id, lone.Id);             // the lone mountain is separate
        Assert.Equal(8, block.ScoreValue);              // 2 × 4 tiles
        Assert.Equal(2, lone.ScoreValue);               // 2 × 1 tile
    }

    [Fact]
    public void MountainRegions_IncludeHills_AndMergeAdjacentHillAndMountain()
    {
        // The mountain pass keys on IsElevation, true for BOTH hills and mountains: an adjacent hill+mountain
        // form one region; a lone hill is its own mountain region. (Hills are the commoner elevation terrain.)
        GameMap map = FromRows(
            "OOOOOOO", // y0
            "OOOOOOO", // y1
            "OOOOOOO", // y2
            "OOMHOOO", // y3  mountain (2,3) + hill (3,3), adjacent
            "OOOOOOO", // y4
            "OOOOHOO", // y5  lone hill (4,5)
            "OOOOOOO", // y6
            "OOOOOOO", // y7
            "OOOOOOO", // y8
            "OOOOOOO");// y9

        Region mh = RegionAt(map, 2, 3);
        Assert.Equal(RegionType.Mountain, mh.Type);
        Assert.Equal(RegionType.Mountain, RegionAt(map, 3, 3).Type); // the hill is a mountain region too
        Assert.Equal(mh.Id, RegionAt(map, 3, 3).Id);                 // hill merges with the adjacent mountain
        Assert.Equal(4, mh.ScoreValue);                              // 2 × 2 tiles

        Region loneHill = RegionAt(map, 4, 5);
        Assert.Equal(RegionType.Mountain, loneHill.Type);
        Assert.NotEqual(mh.Id, loneHill.Id);
        Assert.Equal(2, loneHill.ScoreValue);                        // 2 × 1 tile
    }

    // ── Land regions ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SeparateContinents_GetSeparateLandRegions()
    {
        // Two land blobs separated by a 3-tile water channel (not even diagonally adjacent).
        GameMap map = FromRows(
            "OOOOOOOOOOO", // y0
            "OOOOOOOOOOO", // y1
            "OLLLOOOLLLO", // y2  blobA cols1-3, blobB cols7-9
            "OLLLOOOLLLO", // y3
            "OOOOOOOOOOO", // y4
            "OOOOOOOOOOO", // y5
            "OOOOOOOOOOO", // y6
            "OOOOOOOOOOO");// y7

        Region a = RegionAt(map, 1, 2);
        Region b = RegionAt(map, 7, 2);

        Assert.Equal(RegionType.Land, a.Type);
        Assert.Null(a.Key); // dynamically numbered, not a fixed region
        Assert.NotEqual(a.Id, b.Id);

        var blobAIds = new[] { (1, 2), (2, 2), (3, 2), (1, 3), (2, 3), (3, 3) }.Select(t => map.RegionIdAt(new Position(t.Item1, t.Item2))).ToHashSet();
        var blobBIds = new[] { (7, 2), (8, 2), (9, 2), (7, 3), (8, 3), (9, 3) }.Select(t => map.RegionIdAt(new Position(t.Item1, t.Item2))).ToHashSet();
        Assert.Single(blobAIds);
        Assert.Single(blobBIds);
        Assert.Empty(blobAIds.Intersect(blobBIds)); // disjoint region id sets per continent
    }

    [Fact]
    public void LandRegionScore_IsShareOfTotalLand_FlooredAtFive()
    {
        // Two equal 6-tile continents on a 12-land map: each scores 6/12 × 1000 = 500.
        GameMap map = FromRows(
            "OOOOOOOOOOO",
            "OOOOOOOOOOO",
            "OLLLOOOLLLO",
            "OLLLOOOLLLO",
            "OOOOOOOOOOO",
            "OOOOOOOOOOO",
            "OOOOOOOOOOO",
            "OOOOOOOOOOO");

        Assert.Equal(500, RegionAt(map, 1, 2).ScoreValue);
        Assert.Equal(500, RegionAt(map, 7, 2).ScoreValue);
    }

    [Fact]
    public void OversizedLandmass_SplitsIntoChunksNoLargerThan75()
    {
        // 12×12 all land: arctic claims rows 0-1, antarctic rows 9-11, leaving a contiguous 84-tile middle
        // landmass (rows 2-8) that exceeds LAND_REGION_MAX_SIZE=75 and must split.
        string allLand = new string('L', 12);
        GameMap map = FromRows(Enumerable.Repeat(allLand, 12).ToArray());

        var dynamicLand = map.Regions.Where(r => r.Type == RegionType.Land && r.Key is null).ToList();
        var tileCounts = dynamicLand.ToDictionary(
            r => r.Id,
            r => map.AllPositions().Count(p => map.RegionIdAt(p) == r.Id));

        Assert.True(dynamicLand.Count >= 2, $"expected the 84-tile landmass to split, got {dynamicLand.Count} region(s)");
        Assert.All(tileCounts.Values, n => Assert.True(n <= 75, $"a land region has {n} tiles (> 75)"));
        Assert.Equal(84, tileCounts.Values.Sum()); // every middle-land tile is covered
    }

    // ── Coverage + determinism ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EveryTile_GetsExactlyOneValidRegion()
    {
        GameMap map = FromRows(
            "OOOOOOO",
            "OLLMHLO",
            "OLLLLLO",
            "OOOOOOO",
            "OLLLLLO",
            "OOOOOOO");

        Assert.All(map.AllPositions(), p =>
        {
            int id = map.RegionIdAt(p);
            Assert.NotEqual(GameMap.NoRegion, id);          // no unassigned sentinel
            Assert.InRange(id, 0, map.Regions.Count - 1);   // and it indexes a real region
            Assert.Equal(id, map.RegionOf(p)!.Id);
        });
    }

    [Fact]
    public void Assign_IsDeterministic_AndDrawsNoRandomness()
    {
        TerrainType plains = Classic.Terrain("model.tile.plains");
        TerrainType ocean = Classic.Terrain("model.tile.ocean");
        var terrain = new TerrainType[8 * 8];
        for (int i = 0; i < terrain.Length; i++)
        {
            terrain[i] = i % 3 == 0 ? ocean : plains;
        }
        var map = new GameMap(8, 8, terrain);

        (int[] ids1, IReadOnlyList<Region> regions1) = RegionGenerator.Assign(map);
        (int[] ids2, IReadOnlyList<Region> regions2) = RegionGenerator.Assign(map);

        Assert.Equal(ids1, ids2);
        Assert.Equal(regions1, regions2); // record equality element-wise
    }

    [Fact]
    public void GeneratedMap_CoversEveryTile_AndIsRegionStablePerSeed()
    {
        GameMap a = MapGenerator.Generate(Classic, 36, 24, new Pcg32Random(7));
        GameMap b = MapGenerator.Generate(Classic, 36, 24, new Pcg32Random(7));

        // Full coverage and a valid region per tile on a real generated map.
        Assert.All(a.AllPositions(), p =>
        {
            int id = a.RegionIdAt(p);
            Assert.NotEqual(GameMap.NoRegion, id);
            Assert.InRange(id, 0, a.Regions.Count - 1);
        });
        // Same seed → identical region layer (RNG-free assignment over identical terrain).
        Assert.Equal(
            a.AllPositions().Select(a.RegionIdAt),
            b.AllPositions().Select(b.RegionIdAt));
        Assert.Equal(a.Regions, b.Regions);

        // L2 named assertion #1 on a real map — the antarctic half. The generator's vertical water margin
        // forbids land in rows 0-1 (MapGenerator.GrowContinent), so a generated map has NO arctic land: the
        // arctic rule is pinned by the L1 fixture PolarBands_ClaimLandInTheTopTwoAndBottomThreeRows. Row
        // Height-3 CAN hold land, so the antarctic band is genuinely populated — assert it non-vacuously.
        Assert.DoesNotContain(a.AllPositions(), p => !a.TerrainAt(p).IsWater && p.Y < 2); // no arctic land generated
        Assert.Contains(a.Regions, r => r.Key == "model.region.arctic");                  // region still exists in the table
        var antarcticLand = a.AllPositions().Where(p => !a.TerrainAt(p).IsWater && p.Y >= a.Height - 3).ToList();
        Assert.NotEmpty(antarcticLand); // the assertion below is not vacuous
        Assert.All(antarcticLand, p => Assert.Equal("model.region.antarctic", a.RegionOf(p)!.Key));
    }

    // ── River regions + geographic thirds (86d3fpxnm) ────────────────────────────────────────────────────
    //
    // FreeCol creates one discoverable ServerRegion(RegionType.RIVER) per river (TerrainGenerator.createRivers
    // ~L587) and River.java adds each river tile to it — ServerRegion.addTile L203-204 re-points tile.setRegion,
    // so river tiles genuinely LEAVE their land region (integrator ruling: the faithful option). It also appends
    // nine virtual "geographic thirds" regions (ServerRegion.getStandardRegions L370-472): keyed, score-0,
    // land-weighted bounding boxes claiming no tiles, consumed by FreeCol's native placement (our placement
    // doesn't read them yet — a documented consumer gap). COAST/DESERT stay reserved-only: FreeCol declares but
    // never instantiates them, and so do we.

    /// <summary>The nine thirds keys in FreeCol creation order (<c>ServerRegion.getStandardRegions</c>).</summary>
    private static readonly string[] ThirdsKeys =
    [
        "model.region.northWest", "model.region.north", "model.region.northEast",
        "model.region.west", "model.region.center", "model.region.east",
        "model.region.southWest", "model.region.south", "model.region.southEast",
    ];

    /// <summary>An 8×8 all-plains map with river improvements stamped on the given tiles (magnitude 1).</summary>
    private static GameMap PlainsWithRivers(params Position[] riverTiles)
    {
        TileImprovementType river = Classic.ImprovementTypes.First(i => i.Id == TileImprovementType.RiverId);
        var improvements = riverTiles.ToDictionary(
            p => p, p => (IReadOnlyList<TileImprovementType>)new List<TileImprovementType> { river });
        var map = new GameMap(8, 8, [.. Enumerable.Repeat(Classic.Terrain("model.tile.plains"), 64)],
            improvements: improvements);
        (int[] ids, IReadOnlyList<Region> regions) = RegionGenerator.Assign(map);
        map.SetRegions(ids, regions);
        return map;
    }

    [Fact]
    public void GeographicThirds_AreNineKeyedVirtualBoxes_PartitioningTheMapExactly()
    {
        GameMap map = MapGenerator.Generate(Classic, 36, 24, new Pcg32Random(7));
        var thirds = map.Regions.Where(r => r.Bounds is not null).ToList();

        Assert.Equal(ThirdsKeys, thirds.Select(r => r.Key));                        // nine, in FreeCol creation order
        Assert.Equal(map.Regions.Count - 9, thirds[0].Id);                          // appended as the table's last nine
        Assert.All(thirds, r => Assert.Equal(RegionType.Land, r.Type));             // FreeCol creates them as LAND
        Assert.All(thirds, r => Assert.Equal(0, r.ScoreValue));                     // never worth discovery points
        Assert.All(thirds, r => Assert.False(r.IsDiscoverable));                    // keyed → never discoverable
        // Virtual: no tile's region id points at a thirds region (a bounded region claims no tiles).
        Assert.All(map.AllPositions(), p => Assert.Null(map.RegionOf(p)!.Bounds));
        // The nine half-open boxes cover the map exactly and are disjoint: every tile lies in exactly one.
        Assert.All(map.AllPositions(), p =>
            Assert.Equal(1, thirds.Count(r => r.Bounds!.Value.Contains(p))));
    }

    [Fact]
    public void GeographicThirds_UseLandWeightedDelimiters_PinnedOnAUniformFixture()
    {
        // 12×12 all-land (the OversizedLandmass fixture shape). Counted land excludes polar rows (FreeCol
        // Map.isPolar: y <= 2 or y >= h-3 → counted y = 3..8) and defensively the last column (x = 0..10; FreeCol's
        // w-1-length array would throw on x = 11). Hand-derivation of the verbatim findThirdsOfValues port:
        //   rows: 6 rows × 11 = 66; 66/3 = 22 → yD0 = 4 (cum 11,22); (2·66)/3 = 44 → yD1 = 6 (cum 33,44).
        //   top band rows 0..4 inclusive → counted y 3..4 → 22; 22/3 = 7 → xD0 = 3 (cum 2,4,6,8); 44/3 = 14 → xD1 = 6.
        //   mid band rows 4..6 → counted y 4..6 → 33; 33/3 = 11 → xD0 = 3 (cum 12 at x=3); 22 → xD1 = 7 (cum 24).
        //   bottom band rows 6..11 → counted y 6..8 → 33 → same delimiters as mid.
        // Boxes are the half-open partition per band: [0,yD0)/[yD0,yD1)/[yD1,h) × [0,xD0)/[xD0,xD1)/[xD1,w).
        string allLand = new string('L', 12);
        GameMap map = FromRows(Enumerable.Repeat(allLand, 12).ToArray());
        var thirds = map.Regions.Where(r => r.Bounds is not null).ToList();

        Assert.Equal(9, thirds.Count);
        RegionBounds BoundsOf(string key) => thirds.Single(r => r.Key == key).Bounds!.Value;
        Assert.Equal(new RegionBounds(0, 0, 3, 4), BoundsOf("model.region.northWest"));
        Assert.Equal(new RegionBounds(3, 0, 3, 4), BoundsOf("model.region.north"));
        Assert.Equal(new RegionBounds(6, 0, 6, 4), BoundsOf("model.region.northEast"));
        Assert.Equal(new RegionBounds(0, 4, 3, 2), BoundsOf("model.region.west"));
        Assert.Equal(new RegionBounds(3, 4, 4, 2), BoundsOf("model.region.center"));
        Assert.Equal(new RegionBounds(7, 4, 5, 2), BoundsOf("model.region.east"));
        Assert.Equal(new RegionBounds(0, 6, 3, 6), BoundsOf("model.region.southWest"));
        Assert.Equal(new RegionBounds(3, 6, 4, 6), BoundsOf("model.region.south"));
        Assert.Equal(new RegionBounds(7, 6, 5, 6), BoundsOf("model.region.southEast"));
    }

    [Fact]
    public void RiverTiles_AreReassignedToRiverRegions_EverythingElseKeepsItsRegion_AndThePrefixIsUnchanged()
    {
        // The determinism bar (integrator ruling): the region LAYER may change deliberately, but every NON-river
        // tile keeps its prior region and the pre-existing region-table prefix is unchanged. Pin it comparatively:
        // the same generated terrain with and without its river layer must agree everywhere except river tiles.
        GameMap withRivers = MapGenerator.Generate(Classic, 36, 24, new Pcg32Random(7));
        var riverless = new GameMap(36, 24, [.. withRivers.AllPositions().Select(withRivers.TerrainAt)]);
        (int[] bareIds, IReadOnlyList<Region> bareRegions) = RegionGenerator.Assign(riverless);

        var riverTiles = withRivers.AllPositions().Where(withRivers.HasRiver).ToList();
        Assert.NotEmpty(riverTiles); // the default 36×24 seed-7 map has rivers — the comparison is non-vacuous

        // Non-river tiles: identical region id in both layers (the bare layer has no river regions at all).
        Assert.All(withRivers.AllPositions().Where(p => !withRivers.HasRiver(p)),
            p => Assert.Equal(bareIds[p.Y * 36 + p.X], withRivers.RegionIdAt(p)));
        // River tiles: reassigned to a River region (the faithful ServerRegion.addTile re-point).
        Assert.All(riverTiles, p => Assert.Equal(RegionType.River, withRivers.RegionOf(p)!.Type));

        // The pre-existing table prefix (fixed + lakes + mountains + land — everything before the appended river
        // regions) is byte-identical to the riverless derivation's same prefix.
        int prefixLength = bareRegions.Count - 9; // the bare table = the old prefix + its nine thirds
        Assert.True(prefixLength > 0);
        Assert.Equal(bareRegions.Take(prefixLength), withRivers.Regions.Take(prefixLength));
        // And the appended tail is exactly: the river regions, then the nine thirds (same boxes as the bare run —
        // rivers don't change the land count — with ids shifted by the river-region count).
        var riverRegions = withRivers.Regions.Skip(prefixLength).Where(r => r.Type == RegionType.River).ToList();
        Assert.NotEmpty(riverRegions);
        Assert.Equal(withRivers.Regions.Count, prefixLength + riverRegions.Count + 9);
        Assert.Equal(
            bareRegions.Skip(prefixLength).Select(r => r with { Id = 0 }),
            withRivers.Regions.Skip(prefixLength + riverRegions.Count).Select(r => r with { Id = 0 }));

        // Each river region's score is FreeCol's 2 × Σ per-tile magnitude over exactly its tiles.
        foreach (Region region in riverRegions)
        {
            var tiles = withRivers.AllPositions().Where(p => withRivers.RegionIdAt(p) == region.Id).ToList();
            Assert.NotEmpty(tiles);
            Assert.All(tiles, p => Assert.True(withRivers.HasRiver(p)));
            Assert.Equal(2 * tiles.Sum(p => withRivers.RiverAt(p)!.Magnitude), region.ScoreValue);
        }
    }

    [Fact]
    public void RiverSystems_OneRegionPerConnectedSystem_DiscoverableViaTheExistingPath()
    {
        // Two 2-tile rivers, far apart (not 8-adjacent) → two distinct keyless River regions; each tile resolves to
        // its own region through DiscoverableRegionOf (rivers are discoverable/nameable like land/mountain —
        // FreeCol's ServerRegion(game, RegionType.RIVER) ctor sets discoverable = true).
        GameMap map = PlainsWithRivers(new Position(1, 4), new Position(2, 4), new Position(6, 4), new Position(6, 5));

        Region a = map.RegionOf(new Position(1, 4))!;
        Region b = map.RegionOf(new Position(6, 4))!;
        Assert.Equal(RegionType.River, a.Type);
        Assert.Equal(RegionType.River, b.Type);
        Assert.Equal(a.Id, map.RegionOf(new Position(2, 4))!.Id);  // connected tiles share one system
        Assert.Equal(b.Id, map.RegionOf(new Position(6, 5))!.Id);
        Assert.NotEqual(a.Id, b.Id);                               // separate systems, separate regions
        Assert.Null(a.Key);                                        // dynamic, keyless…
        Assert.True(a.IsDiscoverable);                             // …and discoverable
        Assert.Equal(2 * 2, a.ScoreValue);                         // 2 × Σ magnitude (two magnitude-1 tiles)
        Assert.Equal(a.Id, map.DiscoverableRegionOf(new Position(1, 4))!.Id); // the existing discovery path finds it
    }

    // ── Persistence (v69: bounds omit-when-null; the river/thirds regions ride the existing v35 layer) ────

    [Fact]
    public void RegionLayer_WithRiversAndThirds_RoundTripsThroughSave()
    {
        Game game = Game.New(Classic, seed: 7);
        Assert.Contains(game.Map.Regions, r => r.Type == RegionType.River); // precondition: the layer has rivers
        Assert.Equal(9, game.Map.Regions.Count(r => r.Bounds is not null)); // …and the nine thirds boxes

        Game loaded = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);

        Assert.Equal(game.Map.Regions, loaded.Map.Regions); // record equality — incl. types, keys, scores, bounds
        Assert.Equal(
            game.Map.AllPositions().Select(game.Map.RegionIdAt),
            loaded.Map.AllPositions().Select(loaded.Map.RegionIdAt));
    }

    [Fact]
    public void ARegionlessSave_ReDerivesTheFullLayer_IncludingRiversAndThirds()
    {
        // A pre-v35 (regionless) save re-derives on load; the re-derivation now includes the river regions (from
        // the persisted improvement layer) and the nine thirds — structurally identical to the layer the game was
        // created with. (Discovery state rides the region TABLE, so a regionless save loses it — the pre-existing
        // v51 semantics — hence the structural comparison ignores the discovery fields.)
        Game game = Game.New(Classic, seed: 7);
        SaveGame regionless = SaveGame.From(game) with { Regions = null, RegionIds = null };

        Game loaded = SaveGame.FromJson(regionless.ToJson()).Restore(Classic);

        Assert.Equal(
            game.Map.Regions.Select(r => (r.Id, r.Type, r.ScoreValue, r.Key, r.ParentId, r.Bounds)),
            loaded.Map.Regions.Select(r => (r.Id, r.Type, r.ScoreValue, r.Key, r.ParentId, r.Bounds)));
        Assert.Contains(loaded.Map.Regions, r => r.Type == RegionType.River);  // rivers re-derived from improvements
        Assert.Equal(9, loaded.Map.Regions.Count(r => r.Bounds is not null));  // the nine thirds re-derived
        Assert.Equal(
            game.Map.AllPositions().Select(game.Map.RegionIdAt),
            loaded.Map.AllPositions().Select(loaded.Map.RegionIdAt));
    }

    [Fact]
    public void ATileBearingRegion_OmitsTheBoundsToken_FromJson()
    {
        // Every tile-bearing region (all of a v68-era save's) omits Bounds, so a v68-era region table round-trips
        // byte-identically; only a thirds region writes the token.
        string without = System.Text.Json.JsonSerializer.Serialize(
            new SavedRegion(0, (int)RegionType.Land, 500),
            new System.Text.Json.JsonSerializerOptions
            {
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            });
        Assert.DoesNotContain("Bounds", without);

        string with_ = System.Text.Json.JsonSerializer.Serialize(
            new SavedRegion(9, (int)RegionType.Land, 0, "model.region.northWest", Bounds: new SavedBounds(0, 0, 12, 8)),
            new System.Text.Json.JsonSerializerOptions
            {
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            });
        Assert.Contains("Bounds", with_);
    }
}
