using CrownAndColony.GameLogic.Randomness;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.World.Improvements;

namespace CrownAndColony.GameLogic.World;

/// <summary>
/// Climate-band map generation driven by the ruleset's <c>&lt;gen&gt;</c> data
/// (the same climate envelopes FreeCol uses): a continent is grown from seeded
/// blobs, then each land tile gets a temperature from its latitude, humidity from
/// smoothed noise, and a lowland terrain type whose climate envelope matches.
/// Hills and mountains are then grown as ranges by a directional walk
/// (<see cref="MakeMountains"/>, FreeCol <c>TerrainGenerator.createMountains</c>),
/// not scattered per-tile. Deterministic for a given <see cref="IGameRandom"/>.
/// </summary>
public static class MapGenerator
{
    /// <summary>Fraction of matching land tiles that come up forested.</summary>
    private const double ForestChance = 0.45;

    /// <summary>
    /// Chance a <b>land</b> tile hosts a bonus resource — FreeCol <c>BONUS_NUMBER</c> (`model.option.bonusNumber`,
    /// classic default 10%). (Hardcoded from the spec for now, like <see cref="MountainNumber"/>; reading the
    /// map-generator option is a follow-up.)
    /// </summary>
    private const double LandBonusChance = 0.10;

    /// <summary>A water tile only hosts a resource when it borders <b>more than</b> this many land tiles (FreeCol <c>perhapsAddBonus</c>: <c>&gt; 1</c>).</summary>
    private const int WaterResourceMinLandNeighbours = 1;

    /// <summary>Water-resource odds are <c>1 / (this − adjacentLand)</c> (FreeCol <c>1/(10−adjacentLand)</c>) — a tile hugged by land is likelier.</summary>
    private const int WaterResourceOddsBase = 10;

    /// <summary>
    /// "One elevation tile per this many land tiles" — FreeCol's <c>model.option.mountainNumber</c> (classic
    /// default 10; higher = fewer mountains). The mountain-range pass aims for half of this budget, the random
    /// hill/mountain sprinkle the other half (FreeCol <c>randomHillsRatio = 0.5</c>).
    /// </summary>
    private const int MountainNumber = 10;

    /// <summary>FreeCol's split: half the elevation budget goes to walked ranges, half to a random sprinkle.</summary>
    private const double RandomHillsRatio = 0.5;

    /// <summary>
    /// River budget as a percentage of the river-allowed land tiles — FreeCol <c>model.option.riverNumber</c> (classic
    /// default 15). The river pass stops once it has laid this fraction of the allowed-tile count as river tiles.
    /// (Hardcoded from the spec for now, like <see cref="MountainNumber"/>; reading the map-generator option is a follow-up.)
    /// </summary>
    private const int RiverNumber = 15;

    /// <summary>Hard cap on a single river's length in tiles, so a river can't wander the whole map on a pathological seed (FreeCol bounds rivers implicitly via its section logic; we cap explicitly for the faithful subset).</summary>
    private const int MaxRiverLength = 16;

    /// <summary>The shipped default fraction of the map that is land (FreeCol <c>model.option.landMass</c>); the value the default new game uses.</summary>
    public const double DefaultLandMassFraction = 0.45;

    /// <summary>A near-edge ocean tile is high seas when no land lies within this many tiles (FreeCol <c>model.option.distanceToHighSea</c>, classic 4).</summary>
    private const int DistanceToHighSea = 4;

    /// <summary>How many columns inward from each east/west edge the high-seas band can reach (FreeCol <c>model.option.maximumDistanceToEdge</c>, classic 10).</summary>
    private const int MaxDistanceToEdge = 10;

    /// <summary>
    /// Generates a width × height map. Same ruleset + same RNG state (+ same <paramref name="landMassFraction"/>) →
    /// identical map. <paramref name="landMassFraction"/> is the share of tiles grown into land (FreeCol's
    /// <c>model.option.landMass</c>); it defaults to <see cref="DefaultLandMassFraction"/>, so a call that omits it
    /// is byte-identical to the historical generator (the default new game and its goldens are unchanged).
    /// </summary>
    public static GameMap Generate(
        Ruleset ruleset, int width, int height, IGameRandom random,
        double landMassFraction = DefaultLandMassFraction)
    {
        TerrainType ocean = ruleset.Terrain("model.tile.ocean");
        TerrainType highSeas = ruleset.Terrain("model.tile.highSeas");

        bool[,] land = GrowContinent(width, height, random, landMassFraction);
        int[,] humidity = SmoothedNoise(width, height, random, 0, 101);

        var terrain = new TerrainType[width * height];
        var resources = new Dictionary<Position, string>();
        for (int y = 0; y < height; y++)
        {
            int temperature = TemperatureAtLatitude(y, height, random);
            for (int x = 0; x < width; x++)
            {
                TerrainType type;
                if (!land[x, y])
                {
                    // Outermost columns are seeded as high seas here ONLY to preserve the RNG-draw sequence:
                    // ResetHighSeas (after the loop) then recomputes the real high-seas band from distance-to-land
                    // — a pure, RNG-free reclassification. All other water is coastal ocean.
                    type = x == 0 || x == width - 1 ? highSeas : ocean;
                }
                else
                {
                    // Lowland climate terrain only. Hills and mountains are NOT scattered per-tile here; they are
                    // grown as ranges by MakeMountains (FreeCol TerrainGenerator.createMountains), which overwrites
                    // land tiles after the climate pass.
                    int altitude = RollLowlandAltitude(random);
                    type = PickLandTerrain(ruleset, humidity[x, y], temperature, altitude, random);
                }
                terrain[y * width + x] = type;
            }
        }

        // Mountain & hill RANGES (FreeCol TerrainGenerator.createMountains): pick seed land tiles, walk a chain in a
        // direction laying mountain tiles with a hill/mountain fringe, then sprinkle a few random hills/mountains —
        // so elevation looks like ridgelines, not altitude noise. Overwrites land tiles in place. Draws RNG, so it
        // reorders the stream-0 draw sequence (a deliberate map-gen change for this item, 86d3c9w71).
        MakeMountains(ruleset, terrain, land, width, height, random);

        // High-seas band: recompute which near-edge ocean tiles are the open route to Europe from their
        // distance to land (FreeCol Map.resetHighSeas), replacing the old fixed-outermost-columns rule. Pure and
        // RNG-free, so it consumes no randomness and reorders no later draw. Regions don't distinguish ocean
        // subtypes, so the region layer is unchanged.
        ResetHighSeas(terrain, highSeas, width, height, ocean);

        // Rivers (FreeCol TerrainGenerator.createRivers + River.flowFromSource): springs on high inland ground walk
        // downhill to the sea, laying a river improvement on each lowland tile. Runs after the terrain (mountains +
        // high-seas) is settled and before bonuses (so a later slice's river-mouth fish bonus could fire). Draws RNG,
        // so it reorders the stream-0 draw sequence (a deliberate map-gen change for this item, 86d3b3qdx).
        var improvements = MakeRivers(ruleset, terrain, width, height, random);

        // Bonus resources, picked from each tile's final terrain table by weight — placed only AFTER the terrain is
        // complete (FreeCol adds bonuses last, "otherwise we risk creating resources on fields where they do not
        // belong, like tobacco on hills"), so a tile retyped to mountains gets mountain resources, not the lowland
        // resource it might have rolled before the range pass.
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                TerrainType type = terrain[y * width + x];
                if (type.Resources.Count == 0)
                {
                    continue; // no resource table for this terrain → nothing to place, no RNG drawn
                }
                if (!type.IsWater)
                {
                    // Land: a flat bonus-number chance (FreeCol perhapsAddBonus land branch).
                    if (random.NextDouble() < LandBonusChance)
                    {
                        resources[new Position(x, y)] = PickWeightedResource(type.Resources, random);
                    }
                    continue;
                }
                // Water: a resource only where the tile borders land, at FreeCol's adjacency-scaled odds
                // (1 / (10 − adjacentLand)); open ocean gets none and draws nothing. The min-land-neighbour gate
                // also keeps fish off the deep sea. (FreeCol additionally requires high-seas connectivity; we
                // approximate that with the land-neighbour rule for now — see map-terrain.md.)
                int adjacentLand = LandNeighbours(terrain, width, height, x, y);
                if (adjacentLand > WaterResourceMinLandNeighbours
                    && random.NextDouble() < 1.0 / (WaterResourceOddsBase - adjacentLand))
                {
                    resources[new Position(x, y)] = PickWeightedResource(type.Resources, random);
                }
            }
        }

        var map = new GameMap(width, height, terrain, resources, improvements: improvements);

        // Partition the finished terrain into named regions (polar, ocean, mountain, land). Pure and RNG-free,
        // so it consumes no randomness and leaves the map RNG state untouched — see RegionGenerator.
        (int[] regionIds, IReadOnlyList<Region> regions) = RegionGenerator.Assign(map);

        // Lake terrain: retype the enclosed-water tiles that RegionGenerator just classified as Lake regions to the
        // lake terrain type (FreeCol TerrainGenerator.makeLakes `t.setType(lakeType)`) — completing the lake slice
        // beyond the region tag. RNG-free; a lake tile is still water, so the region layer is unchanged and reused
        // (no second Assign). Lake renders as ocean in the main map view (MapView BaseFor), so the main map golden
        // is unaffected; save stores terrain ids, so an enclosed tile just serialises "model.tile.lake" (no bump).
        TerrainType lake = ruleset.Terrain("model.tile.lake");
        for (int i = 0; i < regionIds.Length; i++)
        {
            if (regions[regionIds[i]].Type == RegionType.Lake)
            {
                terrain[i] = lake;
            }
        }
        return new GameMap(
            width, height, terrain, resources, regionIds: regionIds, regions: regions, improvements: improvements);
    }

    /// <summary>
    /// Marks the open-sea route to Europe as a land-proximity band along the east and west edges, faithful to
    /// FreeCol <c>Map.resetHighSeas(distToLandFromHighSeas, maxDistanceToEdge)</c>: every high-seas tile is reset to
    /// ocean, then for each row the contiguous ocean strip up to <see cref="MaxDistanceToEdge"/> columns in from each
    /// edge is walked outward-to-inward, and an ocean tile with no land within <see cref="DistanceToHighSea"/> (8-dir
    /// Chebyshev distance) becomes high seas. If a side ends up with no high seas, its furthest-from-land strip tile
    /// is promoted (FreeCol's <c>seaL</c>/<c>seaR</c> fallback) so each side always has an exit. Pure and RNG-free.
    /// Runs before bonus resources are placed, so a high-seas tile simply never gets a resource (the resource pass
    /// skips water with no resource table). Replaces the former fixed-outermost-columns rule.
    /// </summary>
    private static void ResetHighSeas(
        TerrainType[] terrain, TerrainType highSeas,
        int width, int height, TerrainType ocean)
    {
        int Idx(int x, int y) => y * width + x;
        bool IsWater(int x, int y) => terrain[Idx(x, y)].IsWater;

        // Reset all high seas to ocean (FreeCol's first pass), so the band is recomputed from scratch.
        for (int i = 0; i < terrain.Length; i++)
        {
            if (terrain[i] == highSeas)
            {
                terrain[i] = ocean;
            }
        }

        // The 8-direction Chebyshev distance to the nearest land within max, or -1 if none is that close.
        int NearestLand(int cx, int cy, int max)
        {
            for (int d = 1; d <= max; d++)
            {
                for (int ny = cy - d; ny <= cy + d; ny++)
                {
                    for (int nx = cx - d; nx <= cx + d; nx++)
                    {
                        if (Math.Max(Math.Abs(nx - cx), Math.Abs(ny - cy)) != d) continue; // ring at exactly d
                        if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                        if (!IsWater(nx, ny)) return d;
                    }
                }
            }
            return -1;
        }

        void Promote(int x, int y) => terrain[Idx(x, y)] = highSeas;

        int totalL = 0, totalR = 0, distL = -1, distR = -1, seaLx = -1, seaLy = -1, seaRx = -1, seaRy = -1;
        for (int y = 0; y < height; y++)
        {
            // West edge: walk in while still on the contiguous ocean strip (stops at the first land).
            for (int x = 0; x < MaxDistanceToEdge && x < width && terrain[Idx(x, y)] == ocean; x++)
            {
                int d = NearestLand(x, y, DistanceToHighSea);
                if (d < 0) { Promote(x, y); totalL++; }
                else if (d > distL) { distL = d; seaLx = x; seaLy = y; }
            }
            // East edge.
            for (int x = 0; x < MaxDistanceToEdge && x < width && terrain[Idx(width - 1 - x, y)] == ocean; x++)
            {
                int gx = width - 1 - x;
                int d = NearestLand(gx, y, DistanceToHighSea);
                if (d < 0) { Promote(gx, y); totalR++; }
                else if (d > distR) { distR = d; seaRx = gx; seaRy = y; }
            }
        }

        if (totalL <= 0 && seaLx >= 0) Promote(seaLx, seaLy); // guarantee a west exit
        if (totalR <= 0 && seaRx >= 0) Promote(seaRx, seaRy); // guarantee an east exit
    }

    /// <summary>The number of the eight neighbours of (<paramref name="x"/>,<paramref name="y"/>) that are land (off-map counts as none).</summary>
    private static int LandNeighbours(TerrainType[] terrain, int width, int height, int x, int y)
    {
        int count = 0;
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0)
                {
                    continue;
                }
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || nx >= width || ny < 0 || ny >= height)
                {
                    continue;
                }
                if (!terrain[ny * width + nx].IsWater)
                {
                    count++;
                }
            }
        }
        return count;
    }

    private static string PickWeightedResource(IReadOnlyList<ResourceChance> table, IGameRandom random)
    {
        int roll = random.Next(table.Sum(r => r.Probability));
        foreach (ResourceChance entry in table)
        {
            roll -= entry.Probability;
            if (roll < 0)
            {
                return entry.ResourceId;
            }
        }
        return table[^1].ResourceId;
    }

    /// <summary>
    /// Grows one continent from random interior seed points; keeps a watery margin
    /// (2 tiles vertically, 4 horizontally — room for the high seas and coast).
    /// </summary>
    private static bool[,] GrowContinent(int width, int height, IGameRandom random, double landMassFraction)
    {
        var land = new bool[width, height];
        int targetLand = (int)(width * height * landMassFraction);

        // Frontier-growth from a few seeds biased toward the middle.
        var frontier = new List<Position>();
        int seeds = 3;
        for (int i = 0; i < seeds; i++)
        {
            var seed = new Position(
                width / 4 + random.Next(width / 2),
                height / 4 + random.Next(height / 2));
            frontier.Add(seed);
        }

        int landCount = 0;
        while (landCount < targetLand && frontier.Count > 0)
        {
            int pick = random.Next(frontier.Count);
            Position p = frontier[pick];
            frontier[pick] = frontier[^1];
            frontier.RemoveAt(frontier.Count - 1);

            if (p.X < 4 || p.X >= width - 4 || p.Y < 2 || p.Y >= height - 2 || land[p.X, p.Y])
            {
                continue;
            }

            land[p.X, p.Y] = true;
            landCount++;
            foreach (Position n in p.Neighbours())
            {
                frontier.Add(n);
            }
        }

        return land;
    }

    /// <summary>Latitude → temperature: hottest (40) at the equator row, coldest (−20) at the poles, with jitter.</summary>
    private static int TemperatureAtLatitude(int y, int height, IGameRandom random)
    {
        double equatorDistance = Math.Abs(y - (height - 1) / 2.0) / ((height - 1) / 2.0);
        int baseTemperature = (int)(40 - equatorDistance * 60);
        return Math.Clamp(baseTemperature + random.Next(-4, 5), -20, 40);
    }

    /// <summary>
    /// Lowland altitude (1–3) for the climate pass. Hills (10–19) and mountains (20–30) are no longer scattered
    /// per-tile — <see cref="MakeMountains"/> grows them as ranges after the climate terrain is laid.
    /// </summary>
    private static int RollLowlandAltitude(IGameRandom random) => 1 + random.Next(3);

    /// <summary>
    /// Grows mountain &amp; hill RANGES across the land, faithful to FreeCol
    /// <c>TerrainGenerator.createMountains</c>. Two passes over the land tiles, in <paramref name="terrain"/>
    /// (row-major; <paramref name="land"/> marks which tiles are land):
    /// <list type="number">
    /// <item><b>Ranges.</b> Until a tile budget (<c>½ · landCount / MountainNumber</c>) is met: take the next
    /// shuffled land tile that isn't already elevation, pick a random walk direction and a length
    /// (<c>maxLength − rand(maxLength/2)</c>, <c>maxLength = max(w,h)/10</c>), then step that many land tiles laying
    /// a mountain at each step and, for each of the step tile's 8 neighbours, a mountain (2/8), a hill (5/8) or
    /// nothing (1/8). The walk stops early at water or the map edge.</item>
    /// <item><b>Sprinkle.</b> The other half of the budget is scattered over shuffled land tiles as 25% mountains,
    /// 75% hills — the "random hills here and there" FreeCol adds after the ranges.</item>
    /// </list>
    /// Hills/mountains carry full-latitude climate envelopes in the classic spec, so (unlike FreeCol's
    /// per-latitude tile-type lists) a single hill/mountain type suffices at every latitude. Overwrites land tiles
    /// in place; never touches water. Draws RNG (Fisher–Yates shuffle + walks), so it reorders stream-0 draws.
    /// </summary>
    private static void MakeMountains(
        Ruleset ruleset, TerrainType[] terrain, bool[,] land, int width, int height, IGameRandom random)
    {
        TerrainType mountains = ruleset.Terrain("model.tile.mountains");
        TerrainType hills = ruleset.Terrain("model.tile.hills");

        int Idx(int x, int y) => y * width + x;
        bool InBounds(int x, int y) => x >= 0 && x < width && y >= 0 && y < height;
        bool IsElevation(int x, int y) => terrain[Idx(x, y)].IsElevation;

        // Every land tile, in row-major order, then shuffled deterministically (FreeCol randomShuffle).
        var landTiles = new List<Position>();
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (land[x, y])
                {
                    landTiles.Add(new Position(x, y));
                }
            }
        }
        int landCount = landTiles.Count;
        if (landCount == 0)
        {
            return;
        }

        // The eight walk directions (E, SE, S, SW, W, NW, N, NE) — the same 8-neighbourhood the rest of the map uses.
        (int dx, int dy)[] directions =
        [
            (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1),
        ];

        // ---- Pass 1: walked mountain ranges ----
        int rangeBudget = (int)Math.Round((1.0 - RandomHillsRatio) * landCount / MountainNumber);
        int maxLength = Math.Max(1, Math.Max(width, height) / 10);

        var shuffled = Shuffle(landTiles, random);
        int placed = 0;
        foreach (Position start in shuffled)
        {
            if (placed >= rangeBudget)
            {
                break;
            }
            // isGoodMountainTile can change as new mountains are added (FreeCol re-checks here).
            if (IsElevation(start.X, start.Y))
            {
                continue;
            }

            (int ddx, int ddy) = directions[random.Next(directions.Length)];
            int length = maxLength - random.Next(maxLength / 2 + 1); // maxLength/2 can be 0 on tiny maps; +1 keeps Next valid
            int cx = start.X, cy = start.Y;
            for (int step = 0; step < length; step++)
            {
                // Raise the current tile to mountain.
                if (!IsElevation(cx, cy))
                {
                    terrain[Idx(cx, cy)] = mountains;
                    placed++;
                }
                // Fringe: each surrounding land tile gets a mountain (2/8), a hill (5/8) or nothing (1/8).
                foreach ((int nx, int ny) in Surrounding(cx, cy))
                {
                    if (!InBounds(nx, ny) || !land[nx, ny] || IsElevation(nx, ny))
                    {
                        continue;
                    }
                    int r = random.Next(8);
                    if (r < 2)
                    {
                        terrain[Idx(nx, ny)] = mountains;
                        placed++;
                    }
                    else if (r < 7)
                    {
                        terrain[Idx(nx, ny)] = hills;
                    }
                }
                // Step to the next tile in the walk direction; stop at water or the edge.
                cx += ddx;
                cy += ddy;
                if (!InBounds(cx, cy) || !land[cx, cy])
                {
                    break;
                }
            }
        }

        // ---- Pass 2: random hill/mountain sprinkle (FreeCol's "here and there") ----
        int sprinkleBudget = (int)(landCount * RandomHillsRatio) / MountainNumber;
        var sprinkleOrder = Shuffle(landTiles, random);
        int sprinkled = 0;
        foreach (Position p in sprinkleOrder)
        {
            if (sprinkled >= sprinkleBudget)
            {
                break;
            }
            if (IsElevation(p.X, p.Y))
            {
                continue;
            }
            terrain[Idx(p.X, p.Y)] = random.Next(4) == 0 ? mountains : hills; // 25% mountains, 75% hills
            sprinkled++;
        }

        IEnumerable<(int X, int Y)> Surrounding(int x, int y)
        {
            foreach ((int dx, int dy) in directions)
            {
                yield return (x + dx, y + dy);
            }
        }
    }

    /// <summary>
    /// Stamps rivers across the land, a faithful subset of FreeCol <c>TerrainGenerator.createRivers</c> +
    /// <c>River.flowFromSource</c>/<c>flow</c>. A river-allowed tile is lowland land (not water, not hills/mountains,
    /// not arctic — the river type's spec scopes). Spring tiles ("good river tiles" — FreeCol <c>Tile.isGoodRiverTile</c>)
    /// are river-allowed tiles whose <b>whole 8-neighbourhood is land</b>, so a source never starts on the coast. From
    /// the shuffled springs, each not already a river starts a walk: pick a random direction, lay the river type on the
    /// current tile, then step (mostly straight, sometimes turning left/right — FreeCol's <c>DirectionChange</c>) to the
    /// next tile; the walk ends when it reaches water (the river mouth), an existing river, the map edge, a non-allowed
    /// tile, or the per-river length cap. The pass stops once the laid river tiles reach the river budget
    /// (<c>allowedTileCount · RiverNumber / 100</c>). Draws RNG (shuffle + walks), so it reorders stream-0 draws.
    /// <para>Faithful-subset deviations vs FreeCol: rivers are stamped at a single (small) magnitude — the section-size
    /// growth that produces large rivers/fjords, the connect-to-other-river joining, and the delta branching are not
    /// modelled (they need the per-tile river <i>style</i>, a later slice); a river simply marks each land tile it
    /// crosses. Movement/production fidelity (the "both endpoints carry a river" follow-cost and the flat yield bonus)
    /// is exact — see <see cref="Improvements.ImprovementMovement"/> / <see cref="Improvements.ImprovementProduction"/>.</para>
    /// </summary>
    private static Dictionary<Position, IReadOnlyList<TileImprovementType>> MakeRivers(
        Ruleset ruleset, TerrainType[] terrain, int width, int height, IGameRandom random)
    {
        // Each river tile carries a single-improvement list (the river); pioneers later add roads/plows to the
        // same tiles, which is why the map's improvement layer is multi-valued per tile.
        var rivers = new Dictionary<Position, IReadOnlyList<TileImprovementType>>();
        TileImprovementType riverType = ruleset.ImprovementTypes.FirstOrDefault(i => i.Id == TileImprovementType.RiverId)
            ?? throw new InvalidOperationException("The ruleset declares no river tile-improvement type.");

        int Idx(int x, int y) => y * width + x;
        bool InBounds(int x, int y) => x >= 0 && x < width && y >= 0 && y < height;
        bool IsWaterAt(int x, int y) => terrain[Idx(x, y)].IsWater;

        // A tile a river may occupy: lowland land (the river type's scopes negate water, hills, mountains, arctic).
        bool RiverAllowed(int x, int y)
        {
            TerrainType t = terrain[Idx(x, y)];
            return !t.IsWater && !t.IsElevation && t.Id != "model.tile.arctic";
        }

        // All river-allowed land tiles (the budget denominator).
        var allowed = new List<Position>();
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (RiverAllowed(x, y))
                {
                    allowed.Add(new Position(x, y));
                }
            }
        }
        if (allowed.Count == 0)
        {
            return rivers;
        }
        int budget = allowed.Count * RiverNumber / 100;
        if (budget <= 0)
        {
            return rivers; // too little land for even one river tile
        }

        // Springs: river-allowed tiles whose entire 8-neighbourhood is land (FreeCol isGoodRiverTile) — not on the coast.
        bool IsSpring(Position p)
        {
            if (!RiverAllowed(p.X, p.Y))
            {
                return false;
            }
            foreach (Position n in p.Neighbours())
            {
                if (!InBounds(n.X, n.Y) || IsWaterAt(n.X, n.Y))
                {
                    return false;
                }
            }
            return true;
        }

        // The 8 walk directions (E, SE, S, SW, W, NW, N, NE), the same order MakeMountains uses.
        (int dx, int dy)[] directions =
        [
            (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1),
        ];

        var springs = Shuffle(allowed.Where(IsSpring).ToList(), random);
        foreach (Position spring in springs)
        {
            if (rivers.Count >= budget)
            {
                break;
            }
            if (rivers.ContainsKey(spring))
            {
                continue; // already part of a river
            }

            int dir = random.Next(directions.Length);
            int cx = spring.X, cy = spring.Y;
            for (int step = 0; step < MaxRiverLength; step++)
            {
                // Lay the river on the current (allowed) tile.
                rivers[new Position(cx, cy)] = [riverType];
                if (rivers.Count >= budget)
                {
                    break;
                }

                // Every other step, nudge the flow direction (FreeCol changes direction on even section counts):
                // 50% straight ahead, 25% one step left, 25% one step right.
                if (step % 2 == 1)
                {
                    int turn = random.Next(4);
                    if (turn == 1) dir = (dir + 1) % directions.Length;       // right turn
                    else if (turn == 2) dir = (dir + directions.Length - 1) % directions.Length; // left turn
                }

                int nx = cx + directions[dir].dx, ny = cy + directions[dir].dy;
                if (!InBounds(nx, ny))
                {
                    break; // ran off the map
                }
                if (IsWaterAt(nx, ny))
                {
                    break; // reached the sea (the river mouth) — done
                }
                if (rivers.ContainsKey(new Position(nx, ny)))
                {
                    break; // merged into an existing river
                }
                if (!RiverAllowed(nx, ny))
                {
                    break; // hit hills/mountains/arctic — a river cannot cross it
                }
                cx = nx;
                cy = ny;
            }
        }

        return rivers;
    }

    /// <summary>
    /// Returns a new list holding <paramref name="source"/> in a deterministic shuffled order (Fisher–Yates,
    /// drawing from the injected RNG — FreeCol <c>RandomUtils.randomShuffle</c>). Leaves the input untouched.
    /// </summary>
    private static List<Position> Shuffle(IReadOnlyList<Position> source, IGameRandom random)
    {
        var list = new List<Position>(source);
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
        return list;
    }

    /// <summary>
    /// Picks a land terrain whose climate envelope contains the triple; if the
    /// rolled triple falls between envelopes (the spec's bands don't tile the
    /// space completely), the climate-nearest terrain is used. Forested and clear
    /// variants share envelopes, so forest-vs-clear is its own roll.
    /// </summary>
    private static TerrainType PickLandTerrain(
        Ruleset ruleset, int humidity, int temperature, int altitude, IGameRandom random)
    {
        var landTypes = ruleset.TerrainTypes
            .Where(t => !t.IsWater && t.Gen is not null)
            .ToList();

        var candidates = landTypes
            .Where(t => t.Gen!.Contains(humidity, temperature, altitude))
            .ToList();
        if (candidates.Count == 0)
        {
            // Nearest envelope by total out-of-range distance (deterministic:
            // spec order breaks ties). Keeps hot zones hot — no arctic fallback.
            return landTypes.MinBy(t => EnvelopeDistance(t.Gen!, humidity, temperature, altitude))!;
        }

        bool wantForest = random.NextDouble() < ForestChance;
        var pool = candidates.Where(t => t.IsForest == wantForest).ToList();
        if (pool.Count == 0)
        {
            pool = candidates;
        }
        return pool[random.Next(pool.Count)];
    }

    /// <summary>How far the triple lies outside the envelope (0 = inside).</summary>
    private static int EnvelopeDistance(GenRanges gen, int humidity, int temperature, int altitude)
    {
        static int Outside(int value, int min, int max) =>
            value < min ? min - value : value > max ? value - max : 0;

        return Outside(humidity, gen.HumidityMin, gen.HumidityMax)
            + Outside(temperature, gen.TemperatureMin, gen.TemperatureMax)
            + Outside(altitude, gen.AltitudeMin, gen.AltitudeMax) * 10; // altitude bands matter most
    }

    /// <summary>Per-tile noise in [min, maxExclusive), smoothed once with a neighbourhood average for clumping.</summary>
    private static int[,] SmoothedNoise(
        int width, int height, IGameRandom random, int min, int maxExclusive)
    {
        var raw = new int[width, height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                raw[x, y] = random.Next(min, maxExclusive);
            }
        }

        var smooth = new int[width, height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int sum = 0, count = 0;
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                        {
                            sum += raw[nx, ny];
                            count++;
                        }
                    }
                }
                smooth[x, y] = sum / count;
            }
        }
        return smooth;
    }
}
