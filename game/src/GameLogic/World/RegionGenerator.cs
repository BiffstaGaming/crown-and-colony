namespace CrownAndColony.GameLogic.World;

/// <summary>
/// Assigns every tile of a finished map to exactly one <see cref="Region"/> (FreeCol
/// <c>ServerRegion.requireFixedRegions</c> + <c>TerrainGenerator.createLandRegions/createMountains</c>):
/// the arctic/antarctic polar bands, the Atlantic/Pacific oceans split into north/south quadrants,
/// contiguous mountain ranges, and the per-landmass land regions (split when larger than 75 tiles).
///
/// <para>Like <see cref="NativeLandClaimGenerator"/> this is a <b>pure, deterministic, RNG-free</b> function
/// of the terrain — it draws no randomness, so it perturbs no RNG stream and recomputes byte-identically at
/// game start and on load. Region ownership is therefore re-derivable; saves persist it (v35) only so a
/// loaded game keeps the exact ids it was played with even if this algorithm later changes.</para>
///
/// <para>Faithful subset and divergences (see <c>docs/systems/map-terrain.md</c>): FreeCol builds mountain
/// regions during a generation-time range walk; our altitude is per-tile noise, so we derive mountain regions
/// from the resulting hill/mountain terrain instead (same <see cref="RegionType.Mountain"/> and
/// <c>score = 2 × size</c>). Enclosed water the ocean fill cannot reach is tagged <see cref="RegionType.Lake"/>
/// (FreeCol <c>TerrainGenerator.createLakeRegions</c>) — region classification only; FreeCol additionally retypes
/// the tile to lake <i>terrain</i>, which we defer (it would move the map goldens). FreeCol's nine virtual
/// "geographic thirds" bounding boxes (used only to seed native placement) and the RIVER/COAST/DESERT region
/// types are deferred until rivers and that placement hook exist.</para>
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

        int NewRegion(RegionType type, int score, string? key = null, int? parent = null)
        {
            int id = regions.Count;
            regions.Add(new Region(id, type, score, key, parent));
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
        // no water tile is left unassigned. Region classification only: FreeCol also retypes the tile to lake
        // terrain, which we defer (it would move the map goldens); rivers (RNG-bearing) are a separate slice.
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

        return (ids, regions);
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
    private static void AssignLand(GameMap map, int[] ids, int w, int h, Func<RegionType, int, string?, int?, int> newRegion)
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
            landregion[c] = newRegion(RegionType.Land, score, null, null);
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
