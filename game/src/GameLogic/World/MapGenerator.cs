using CrownAndColony.GameLogic.Randomness;
using CrownAndColony.GameLogic.Specification;

namespace CrownAndColony.GameLogic.World;

/// <summary>
/// Climate-band map generation driven by the ruleset's <c>&lt;gen&gt;</c> data
/// (the same climate envelopes FreeCol uses): a continent is grown from seeded
/// blobs, then each land tile gets a temperature from its latitude, humidity and
/// altitude from smoothed noise, and a terrain type whose climate envelope
/// matches. Deterministic for a given <see cref="IGameRandom"/>.
/// </summary>
public static class MapGenerator
{
    /// <summary>Fraction of matching land tiles that come up forested.</summary>
    private const double ForestChance = 0.45;

    /// <summary>Chance a tile hosts a bonus resource (prime grain, minerals, fishery…).</summary>
    private const double ResourceChanceFraction = 0.08;

    /// <summary>Fraction of land tiles raised to hills / mountains.</summary>
    private const double HillsChance = 0.10;
    private const double MountainsChance = 0.04;

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
                    // high-seas tiles carry no resource table, so they skip the per-tile resource roll below, exactly
                    // as before. ResetHighSeas (after the loop) then recomputes the real high-seas band from
                    // distance-to-land — a pure, RNG-free reclassification. All other water is coastal ocean.
                    type = x == 0 || x == width - 1 ? highSeas : ocean;
                }
                else
                {
                    int altitude = RollAltitude(random);
                    type = PickLandTerrain(ruleset, humidity[x, y], temperature, altitude, random);
                }
                terrain[y * width + x] = type;

                // Bonus resources, picked from the terrain's own table by weight.
                if (type.Resources.Count > 0 && random.NextDouble() < ResourceChanceFraction)
                {
                    resources[new Position(x, y)] = PickWeightedResource(type.Resources, random);
                }
            }
        }

        // High-seas band: recompute which near-edge ocean tiles are the open route to Europe from their
        // distance to land (FreeCol Map.resetHighSeas), replacing the old fixed-outermost-columns rule. Pure and
        // RNG-free, so it consumes no randomness and reorders no later draw (the loop above kept its high-seas
        // seeding only to preserve the resource-roll sequence). Regions don't distinguish ocean subtypes, so the
        // region layer is unchanged.
        ResetHighSeas(terrain, resources, width, height, ocean, highSeas);

        var map = new GameMap(width, height, terrain, resources);

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
        return new GameMap(width, height, terrain, resources, regionIds: regionIds, regions: regions);
    }

    /// <summary>
    /// Marks the open-sea route to Europe as a land-proximity band along the east and west edges, faithful to
    /// FreeCol <c>Map.resetHighSeas(distToLandFromHighSeas, maxDistanceToEdge)</c>: every high-seas tile is reset to
    /// ocean, then for each row the contiguous ocean strip up to <see cref="MaxDistanceToEdge"/> columns in from each
    /// edge is walked outward-to-inward, and an ocean tile with no land within <see cref="DistanceToHighSea"/> (8-dir
    /// Chebyshev distance) becomes high seas. If a side ends up with no high seas, its furthest-from-land strip tile
    /// is promoted (FreeCol's <c>seaL</c>/<c>seaR</c> fallback) so each side always has an exit. Pure and RNG-free.
    /// Resources are dropped from any tile that becomes high seas (the open sea hosts no bonus). Replaces the former
    /// fixed-outermost-columns rule.
    /// </summary>
    private static void ResetHighSeas(
        TerrainType[] terrain, Dictionary<Position, string> resources,
        int width, int height, TerrainType ocean, TerrainType highSeas)
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

        void Promote(int x, int y)
        {
            terrain[Idx(x, y)] = highSeas;
            resources.Remove(new Position(x, y)); // the open sea hosts no bonus resource
        }

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

    /// <summary>Mostly lowland (1–3) with occasional hills (10–19) and mountains (20–30).</summary>
    private static int RollAltitude(IGameRandom random)
    {
        double roll = random.NextDouble();
        if (roll < MountainsChance)
        {
            return 20 + random.Next(11);
        }
        if (roll < MountainsChance + HillsChance)
        {
            return 10 + random.Next(10);
        }
        return 1 + random.Next(3);
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
