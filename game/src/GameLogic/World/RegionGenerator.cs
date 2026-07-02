namespace CrownAndColony.GameLogic.World;

/// <summary>
/// Assigns every tile of a finished map to exactly one <see cref="Region"/> (FreeCol
/// <c>ServerRegion.requireFixedRegions</c> + <c>TerrainGenerator.createLandRegions/createMountains/createRivers</c>):
/// the arctic/antarctic polar bands, the Atlantic/Pacific oceans split into north/south quadrants,
/// contiguous mountain ranges, the per-landmass land regions (split when larger than 75 tiles), and one
/// <see cref="RegionType.River"/> region per connected river system (river tiles are <b>reassigned</b> from their
/// land region — FreeCol <c>ServerRegion.addTile</c> re-points <c>tile.setRegion</c>). It also appends FreeCol's
/// nine virtual <b>geographic-thirds</b> regions (<c>ServerRegion.getStandardRegions</c>): keyed, score-0
/// bounding-box regions that claim no tiles, splitting the (non-polar) land into equal-land thirds each way —
/// consumed by native settlement placement in FreeCol (our placement doesn't read them yet; documented gap).
///
/// <para>Like <see cref="NativeLandClaimGenerator"/> this is a <b>pure, deterministic, RNG-free</b> function
/// of the terrain + river layer — it draws no randomness, so it perturbs no RNG stream and recomputes
/// byte-identically at game start and on load. Region ownership is therefore re-derivable; saves persist it (v35)
/// only so a loaded game keeps the exact ids it was played with even if this algorithm later changes.</para>
///
/// <para>Faithful subset and divergences (see <c>docs/systems/map-terrain.md</c>): FreeCol builds mountain
/// regions during a generation-time range walk; our altitude is per-tile noise, so we derive mountain regions
/// from the resulting hill/mountain terrain instead (same <see cref="RegionType.Mountain"/> and
/// <c>score = 2 × size</c>). FreeCol likewise builds one river region per river <i>walk</i>; our river identity is
/// the finished improvement layer, so we derive one region per <b>connected river system</b> (a tributary and its
/// trunk share one region) with the same <c>score = 2 × Σ magnitude</c>. Enclosed water the ocean fill cannot reach
/// is tagged <see cref="RegionType.Lake"/> (FreeCol <c>TerrainGenerator.createLakeRegions</c>);
/// <see cref="MapGenerator"/> then retypes those tiles to lake <i>terrain</i> after region assignment (FreeCol
/// <c>makeLakes</c>). The COAST/DESERT region types are <b>reserved, never instantiated</b> — faithful: FreeCol's
/// generator never creates them either. The thirds boxes partition the whole map (FreeCol's <c>ensureWithinMap</c>
/// cap drops the last row/column from every box — a Java <c>Rectangle</c> artifact we deliberately do not copy, so
/// the nine boxes tile the map exactly).</para>
/// </summary>
public static class RegionGenerator
{
    // FreeCol constants, ported verbatim.
    private const int PolarHeight = 2;            // Map.POLAR_HEIGHT
    private const int LandRegionMaxSize = 75;     // TerrainGenerator.LAND_REGION_MAX_SIZE
    private const int LandRegionsScoreValue = 1000; // TerrainGenerator.LAND_REGIONS_SCORE_VALUE
    private const int LandRegionMinScore = 5;     // TerrainGenerator.LAND_REGION_MIN_SCORE
    private const int PacificScoreValue = 100;    // ServerRegion.PACIFIC_SCORE_VALUE

    /// <summary>The 8 neighbour offsets in FreeCol <c>Direction.values()</c> order (N, NE, E, SE, S, SW, W, NW).</summary>
    private static readonly (int Dx, int Dy)[] Dirs =
        [(0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1)];

    /// <summary>
    /// Computes the region layer for <paramref name="map"/>: a row-major region id per tile and the region table
    /// (indexed by id). The caller installs it via <see cref="GameMap.SetRegions"/>. No in-bounds tile is left
    /// with the <see cref="GameMap.NoRegion"/> sentinel.
    /// </summary>
    public static (int[] RegionIds, IReadOnlyList<Region> Regions) Assign(GameMap map)
    {
        int w = map.Width, h = map.Height;
        var ids = new int[w * h];
        Array.Fill(ids, GameMap.NoRegion);
        var regions = new List<Region>();

        int NewRegion(RegionType type, int score, string? key = null, int? parent = null, RegionBounds? bounds = null)
        {
            int id = regions.Count;
            regions.Add(new Region(id, type, score, key, parent, Bounds: bounds));
            return id;
        }

        int Idx(int x, int y) => y * w + x;
        bool IsLand(int x, int y) => !map.TerrainAt(new Position(x, y)).IsWater;
        bool IsElevation(int x, int y) => map.TerrainAt(new Position(x, y)).IsElevation;

        // Fixed regions always exist (FreeCol creates them even when they end up empty), giving stable ids:
        // 0 arctic, 1 antarctic, 2 pacific(+3 N,4 S), 5 atlantic(+6 N,7 S). Ocean tiles land in the leaf
        // quadrants; the parent ocean carries the discovery score (Pacific 100).
        int arctic = NewRegion(RegionType.Land, 0, "model.region.arctic");
        int antarctic = NewRegion(RegionType.Land, 0, "model.region.antarctic");
        int pacific = NewRegion(RegionType.Ocean, PacificScoreValue, "model.region.pacific");
        int northPacific = NewRegion(RegionType.Ocean, 0, "model.region.northPacific", pacific);
        int southPacific = NewRegion(RegionType.Ocean, 0, "model.region.southPacific", pacific);
        int atlantic = NewRegion(RegionType.Ocean, 0, "model.region.atlantic");
        int northAtlantic = NewRegion(RegionType.Ocean, 0, "model.region.northAtlantic", atlantic);
        int southAtlantic = NewRegion(RegionType.Ocean, 0, "model.region.southAtlantic", atlantic);

        // ── 1. Polar bands (land tiles only; water in polar rows becomes ocean below) ───────────────────────
        // Faithful to ServerRegion.requireFixedRegions: arctic = rows [0, POLAR_HEIGHT); antarctic =
        // rows [Height-POLAR_HEIGHT-1, Height) — the same 2-row/3-row asymmetry FreeCol's loops produce.
        int arcticHeight = PolarHeight;
        int antarcticHeight = h - PolarHeight - 1;
        for (int y = 0; y < arcticHeight && y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (IsLand(x, y))
                {
                    ids[Idx(x, y)] = arctic;
                }
            }
        }
        for (int y = Math.Max(antarcticHeight, 0); y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (IsLand(x, y) && ids[Idx(x, y)] == GameMap.NoRegion) // arctic wins any overlap on tiny maps
                {
                    ids[Idx(x, y)] = antarctic;
                }
            }
        }

        // ── 2. Oceans: fill the four quadrants from a seed inward, escalating quadrant → half → whole map ────
        AssignOceans(map, ids, w, h, northPacific, southPacific, northAtlantic, southAtlantic);

        // Any water the directional ocean fill never reached is an enclosed body with no sea route out: an
        // inland lake (FreeCol TerrainGenerator.createLakeRegions tags exactly this set — water that is
        // "!isLand && getRegion()==null" after the oceans are assigned). One score-0 lake region per blob, so
        // no water tile is left unassigned. MapGenerator retypes these tiles to lake terrain after this pass
        // (FreeCol makeLakes); rivers (RNG-bearing) are a separate slice.
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (!IsLand(x, y) && ids[Idx(x, y)] == GameMap.NoRegion)
                {
                    int region = NewRegion(RegionType.Lake, 0);
                    foreach ((int bx, int by) in CollectBlob((px, py) => !IsLand(px, py) && ids[Idx(px, py)] == GameMap.NoRegion, x, y, w, h))
                    {
                        ids[Idx(bx, by)] = region;
                    }
                }
            }
        }

        // ── 3. Mountain regions: contiguous hill/mountain land not already claimed by a polar band ───────────
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (IsLand(x, y) && IsElevation(x, y) && ids[Idx(x, y)] == GameMap.NoRegion)
                {
                    var blob = CollectBlob(
                        (px, py) => IsLand(px, py) && IsElevation(px, py) && ids[Idx(px, py)] == GameMap.NoRegion,
                        x, y, w, h);
                    int region = NewRegion(RegionType.Mountain, 2 * blob.Count); // FreeCol mountain score = 2·size
                    foreach ((int bx, int by) in blob)
                    {
                        ids[Idx(bx, by)] = region;
                    }
                }
            }
        }

        // ── 4. Land regions: the remaining unclaimed land, one per landmass, split when larger than 75 ───────
        AssignLand(map, ids, w, h, NewRegion);

        // ── 5. River regions (86d3fpxnm): one per connected river system, REASSIGNING its tiles ──────────────
        // FreeCol TerrainGenerator.createRivers creates a discoverable ServerRegion(RegionType.RIVER) per river and
        // River.java adds every river tile to it — ServerRegion.addTile L203-204 re-points tile.setRegion, so a
        // river tile genuinely leaves its land region. Our river identity is the finished improvement layer, so a
        // connected component of river-improved tiles (8-dir, scan order) is one system; score = 2 × Σ per-tile
        // magnitude (FreeCol 2 × Σ RiverSection.getSize()). Appended AFTER the land regions so every pre-existing
        // region keeps its id (the table prefix is unchanged; only river TILES change region — the ruling's
        // deliberate layer change). Runs before the thirds so the thirds ids are the table's last nine.
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var p = new Position(x, y);
                if (!map.HasRiver(p) || regions[ids[Idx(x, y)]].Type == RegionType.River)
                {
                    continue; // not a river tile, or already claimed by an earlier system
                }
                var system = CollectBlob((px, py) => map.HasRiver(new Position(px, py))
                    && regions[ids[Idx(px, py)]].Type != RegionType.River, x, y, w, h);
                int score = 2 * system.Sum(t => map.RiverAt(new Position(t.X, t.Y))!.Magnitude);
                int region = NewRegion(RegionType.River, score);
                foreach ((int bx, int by) in system)
                {
                    ids[Idx(bx, by)] = region; // reassignment — the faithful ServerRegion.addTile behaviour
                }
            }
        }

        // ── 6. The nine geographic-thirds boxes (86d3fpxnm): virtual, keyed, score 0, no tiles ───────────────
        AppendGeographicThirds(map, w, h, NewRegion);

        return (ids, regions);
    }

    /// <summary>
    /// Appends FreeCol's nine virtual "geographic thirds" regions (<c>ServerRegion.getStandardRegions</c>,
    /// <c>server/model/ServerRegion.java</c> L370-472): keyed <see cref="RegionType.Land"/> regions carrying only a
    /// bounding box — they claim no tiles and are never discoverable (they have keys). The delimiters are
    /// <b>land-weighted</b> thirds, not geometric ones (FreeCol <c>findYForThirdsOfLand</c>/<c>findXForThirdsOfLand</c>/
    /// <c>findThirdsOfValues</c>): the two Y delimiters split the non-polar land tiles into three equal-count
    /// horizontal bands, then each band gets its own two X delimiters splitting that band's land into equal-count
    /// column thirds (the X count includes the delimiter rows on both sides, exactly like FreeCol's inclusive
    /// <c>subMap</c> ranges). Our boxes are the half-open partition <c>[0,xD0)/[xD0,xD1)/[xD1,w)</c> per band
    /// <c>[0,yD0)/[yD0,yD1)/[yD1,h)</c> — covering the map exactly, without FreeCol's <c>ensureWithinMap</c>
    /// last-row/column drop (a Java Rectangle artifact; documented deviation). Creation order NW, N, NE, W, C, E,
    /// SW, S, SE (the FreeCol order), so the nine ids are stable per map.
    /// </summary>
    private static void AppendGeographicThirds(
        GameMap map, int w, int h, Func<RegionType, int, string?, int?, RegionBounds?, int> newRegion)
    {
        // FreeCol Map.isPolar: y <= POLAR_HEIGHT || y >= height - POLAR_HEIGHT - 1 (rows 0..2 and h-3..h-1) —
        // polar land never counts toward the thirds.
        bool IsCountedLand(int x, int y) =>
            !map.TerrainAt(new Position(x, y)).IsWater
            && y > PolarHeight && y < h - PolarHeight - 1;

        // Y delimiters over all counted land (findYForThirdsOfLand: array length h-1 — a counted tile never sits in
        // the last row, which is polar).
        var rowCounts = new int[Math.Max(1, h - 1)];
        long total = 0;
        for (int y = 0; y < h - 1; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (IsCountedLand(x, y))
                {
                    rowCounts[y]++;
                    total++;
                }
            }
        }
        int[] yD = FindThirdsOfValues(rowCounts, total);

        // Per horizontal band, the X delimiters (findXForThirdsOfLand over the band's INCLUSIVE row range — FreeCol's
        // subMap(0, startY, w-1, endY-startY+1) shares the delimiter row with both adjacent bands). Array length w-1
        // faithfully; a counted land tile at x == w-1 would overflow FreeCol's array (it throws there) — we skip it
        // defensively (generated maps keep a watery margin, so none exists in practice).
        int[] XDelims(int startY, int endY)
        {
            var colCounts = new int[Math.Max(1, w - 1)];
            long sum = 0;
            for (int y = Math.Max(0, startY); y <= Math.Min(h - 1, endY); y++)
            {
                for (int x = 0; x < w - 1; x++)
                {
                    if (IsCountedLand(x, y))
                    {
                        colCounts[x]++;
                        sum++;
                    }
                }
            }
            return FindThirdsOfValues(colCounts, sum);
        }

        int[] xTop = XDelims(0, yD[0]);
        int[] xMid = XDelims(yD[0], yD[1]);
        int[] xBot = XDelims(yD[1], h - 1);

        // Nine half-open boxes partitioning the map; FreeCol creation order (NW,N,NE,W,C,E,SW,S,SE) → stable ids.
        (string Key, int[] XD, int RowFrom, int RowTo)[] bands =
        [
            ("model.region.northWest", xTop, 0, yD[0]),
            ("model.region.north", xTop, 0, yD[0]),
            ("model.region.northEast", xTop, 0, yD[0]),
            ("model.region.west", xMid, yD[0], yD[1]),
            ("model.region.center", xMid, yD[0], yD[1]),
            ("model.region.east", xMid, yD[0], yD[1]),
            ("model.region.southWest", xBot, yD[1], h),
            ("model.region.south", xBot, yD[1], h),
            ("model.region.southEast", xBot, yD[1], h),
        ];
        for (int i = 0; i < bands.Length; i++)
        {
            (string key, int[] xd, int rowFrom, int rowTo) = bands[i];
            int colFrom = (i % 3) switch { 0 => 0, 1 => xd[0], _ => xd[1] };
            int colTo = (i % 3) switch { 0 => xd[0], 1 => xd[1], _ => w };
            newRegion(RegionType.Land, 0, key, null,
                new RegionBounds(colFrom, rowFrom, Math.Max(0, colTo - colFrom), Math.Max(0, rowTo - rowFrom)));
        }
    }

    /// <summary>
    /// The two indices splitting a weighted array into three equal-weight parts (FreeCol
    /// <c>ServerRegion.findThirdsOfValues</c>, ported verbatim): the first index whose running total reaches
    /// <c>sum/3</c>, and the first reaching <c>(2·sum)/3</c> (integer division; a zero <paramref name="sum"/> yields
    /// <c>[0, 0]</c> — empty west/centre boxes, everything in the east/south, exactly as FreeCol degenerates).
    /// </summary>
    private static int[] FindThirdsOfValues(int[] timesTheValue, long sum)
    {
        var result = new int[2];
        long accumulated = 0;
        bool needFirst = true;
        for (int i = 0; i < timesTheValue.Length; i++)
        {
            accumulated += timesTheValue[i];
            if (needFirst && accumulated >= sum / 3)
            {
                result[0] = i;
                needFirst = false;
            }
            if (accumulated >= (2 * sum) / 3)
            {
                result[1] = i;
                break;
            }
        }
        return result;
    }

    /// <summary>
    /// Fills the four ocean quadrants (FreeCol <c>ServerRegion.fillOcean</c>): seed each from the first water
    /// tile down its outer column (Pacific = west column 0, Atlantic = east column Width-1; North = upper half),
    /// then flood-fill water in three escalating bounds — own quadrant, own horizontal half, the whole map — so
    /// a region can overflow into its opposite quadrant. Deterministic (fixed seed scan + direction order).
    /// </summary>
    private static void AssignOceans(
        GameMap map, int[] ids, int w, int h,
        int northPacific, int southPacific, int northAtlantic, int southAtlantic)
    {
        int midx = w / 2, midy = h / 2;
        bool IsWater(int x, int y) => map.TerrainAt(new Position(x, y)).IsWater;

        // Seeds: first water tile scanning from the equator outward along each outer column.
        (int, int)? tNP = null, tSP = null, tNA = null, tSA = null;
        for (int y = midy - 1; y >= 0; y--)
        {
            if (tNP is null && IsWater(0, y)) tNP = (0, y);
            if (tNA is null && IsWater(w - 1, y)) tNA = (w - 1, y);
            if (tNP is not null && tNA is not null) break;
        }
        for (int y = midy; y < h; y++)
        {
            if (tSP is null && IsWater(0, y)) tSP = (0, y);
            if (tSA is null && IsWater(w - 1, y)) tSA = (w - 1, y);
            if (tSP is not null && tSA is not null) break;
        }

        // (minX, minY, maxX, maxY) half-open bounds.
        var rNP = (0, 0, midx, midy);
        var rSP = (0, midy, midx, h);
        var rNA = (midx, 0, w, midy);
        var rSA = (midx, midy, w, h);
        var rN = (0, 0, w, midy);
        var rS = (0, midy, w, h);
        var rAll = (0, 0, w, h);

        foreach (var (seed, region, bounds) in new[]
        {
            // Own quadrant first, then own half, then the whole map — Pacific quadrants before Atlantic at each level.
            (tNP, northPacific, rNP), (tSP, southPacific, rSP), (tNA, northAtlantic, rNA), (tSA, southAtlantic, rSA),
            (tNP, northPacific, rN),  (tSP, southPacific, rS),  (tNA, northAtlantic, rN),  (tSA, southAtlantic, rS),
            (tNP, northPacific, rAll),(tSP, southPacific, rAll),(tNA, northAtlantic, rAll),(tSA, southAtlantic, rAll),
        })
        {
            if (seed is not null)
            {
                FillOcean(map, ids, w, h, seed.Value, region, bounds);
            }
        }
    }

    /// <summary>Flood-fills water tiles into <paramref name="region"/> from <paramref name="seed"/>, bounded, claiming only unowned or own-region water (never another region's).</summary>
    private static void FillOcean(
        GameMap map, int[] ids, int w, int h, (int X, int Y) seed, int region, (int MinX, int MinY, int MaxX, int MaxY) b)
    {
        bool IsWater(int x, int y) => map.TerrainAt(new Position(x, y)).IsWater;
        var visited = new bool[w, h];
        var q = new Queue<(int X, int Y)>();
        visited[seed.X, seed.Y] = true;
        q.Enqueue(seed);
        while (q.Count > 0)
        {
            (int cx, int cy) = q.Dequeue();
            ids[cy * w + cx] = region; // idempotent on escalation passes (seed already this region)
            foreach ((int dx, int dy) in Dirs)
            {
                int nx = cx + dx, ny = cy + dy;
                if (nx < b.MinX || nx >= b.MaxX || ny < b.MinY || ny >= b.MaxY || visited[nx, ny])
                {
                    continue;
                }
                visited[nx, ny] = true;
                if (IsWater(nx, ny) && (ids[ny * w + nx] == GameMap.NoRegion || ids[ny * w + nx] == region))
                {
                    q.Enqueue((nx, ny));
                }
            }
        }
    }

    /// <summary>
    /// Land regions (FreeCol <c>TerrainGenerator.createLandRegions</c>): flood-fill each contiguous unclaimed
    /// landmass, split any larger than <see cref="LandRegionMaxSize"/> into ~75-tile chunks, and score each
    /// <c>max((int)(size/landsize·1000), 5)</c> where <c>landsize</c> is the total land-tile count.
    /// </summary>
    private static void AssignLand(
        GameMap map, int[] ids, int w, int h, Func<RegionType, int, string?, int?, RegionBounds?, int> newRegion)
    {
        bool IsLand(int x, int y) => !map.TerrainAt(new Position(x, y)).IsWater;

        // landsize counts ALL land tiles (polar + mountain included), the score denominator (faithful to TG).
        int landsize = 0;
        var landmap = new bool[w, h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (IsLand(x, y))
                {
                    landsize++;
                    landmap[x, y] = ids[y * w + x] == GameMap.NoRegion; // only the still-unclaimed land
                }
            }
        }

        // Number contiguous landmasses (scan y-outer, x-inner like FreeCol).
        int continents = 0;
        var continentmap = new int[w, h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (landmap[x, y])
                {
                    continents++;
                    bool[,] continent = FloodFillBool(landmap, x, y, w, h, int.MaxValue);
                    for (int yy = 0; yy < h; yy++)
                    {
                        for (int xx = 0; xx < w; xx++)
                        {
                            if (continent[xx, yy])
                            {
                                continentmap[xx, yy] = continents;
                                landmap[xx, yy] = false;
                            }
                        }
                    }
                }
            }
        }

        var continentsize = new int[continents + 1];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                continentsize[continentmap[x, y]]++; // index 0 = water / already-claimed
            }
        }

        // Split landmasses larger than the cap into ~75-tile sub-regions (faithful carve loop).
        int oldcontinents = continents;
        for (int c = 1; c <= oldcontinents; c++)
        {
            if (continentsize[c] <= LandRegionMaxSize)
            {
                continue;
            }
            var splitcontinent = new bool[w, h];
            int splitX = 0, splitY = 0;
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    splitcontinent[x, y] = continentmap[x, y] == c;
                    if (splitcontinent[x, y])
                    {
                        splitX = x;
                        splitY = y;
                    }
                }
            }
            while (continentsize[c] > LandRegionMaxSize)
            {
                int targetsize = continentsize[c] < 2 * LandRegionMaxSize ? continentsize[c] / 2 : LandRegionMaxSize;
                continents++;
                bool[,] newregion = FloodFillBool(splitcontinent, splitX, splitY, w, h, targetsize);
                for (int x = 0; x < w; x++)
                {
                    for (int y = 0; y < h; y++)
                    {
                        if (newregion[x, y])
                        {
                            continentmap[x, y] = continents;
                            splitcontinent[x, y] = false;
                            continentsize[c]--;
                        }
                        if (splitcontinent[x, y])
                        {
                            splitX = x;
                            splitY = y;
                        }
                    }
                }
            }
        }

        // One LAND region per non-empty (post-split) landmass index, scored by its share of the total land.
        var finalsize = new int[continents + 1];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                finalsize[continentmap[x, y]]++;
            }
        }
        var landregion = new int[continents + 1];
        for (int c = 1; c <= continents; c++)
        {
            if (finalsize[c] == 0)
            {
                landregion[c] = GameMap.NoRegion; // a split remainder that ended up empty: no region
                continue;
            }
            int score = Math.Max((int)((float)finalsize[c] / landsize * LandRegionsScoreValue), LandRegionMinScore);
            landregion[c] = newRegion(RegionType.Land, score, null, null, null);
        }
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int c = continentmap[x, y];
                if (c > 0)
                {
                    ids[y * w + x] = landregion[c];
                }
            }
        }
    }

    /// <summary>
    /// 8-direction BFS over an "allowed" grid, faithful to FreeCol <c>Map.floodFillBool</c> (including the
    /// <paramref name="limit"/> semantics used to carve ~N-tile land sub-regions). Returns the reached cells.
    /// </summary>
    private static bool[,] FloodFillBool(bool[,] allowed, int sx, int sy, int w, int h, int limit)
    {
        var visited = new bool[w, h];
        var q = new Queue<(int X, int Y)>();
        visited[sx, sy] = true;
        (int X, int Y)? p = (sx, sy);
        while (p is not null && --limit > 0)
        {
            (int cx, int cy) = p.Value;
            foreach ((int dx, int dy) in Dirs)
            {
                int nx = cx + dx, ny = cy + dy;
                if (nx < 0 || nx >= w || ny < 0 || ny >= h || visited[nx, ny] || !allowed[nx, ny])
                {
                    continue;
                }
                visited[nx, ny] = true;
                q.Enqueue((nx, ny));
            }
            p = q.Count > 0 ? q.Dequeue() : null;
        }
        return visited;
    }

    /// <summary>Collects every cell reachable from the seed (8-dir BFS) for which <paramref name="member"/> holds.</summary>
    private static List<(int X, int Y)> CollectBlob(Func<int, int, bool> member, int sx, int sy, int w, int h)
    {
        var blob = new List<(int X, int Y)>();
        var visited = new bool[w, h];
        var q = new Queue<(int X, int Y)>();
        visited[sx, sy] = true;
        q.Enqueue((sx, sy));
        while (q.Count > 0)
        {
            (int cx, int cy) = q.Dequeue();
            blob.Add((cx, cy));
            foreach ((int dx, int dy) in Dirs)
            {
                int nx = cx + dx, ny = cy + dy;
                if (nx < 0 || nx >= w || ny < 0 || ny >= h || visited[nx, ny] || !member(nx, ny))
                {
                    continue;
                }
                visited[nx, ny] = true;
                q.Enqueue((nx, ny));
            }
        }
        return blob;
    }
}
