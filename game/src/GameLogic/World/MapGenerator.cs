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

    /// <summary>Generates a width × height map. Same ruleset + same RNG state → identical map.</summary>
    public static GameMap Generate(Ruleset ruleset, int width, int height, IGameRandom random)
    {
        TerrainType ocean = ruleset.Terrain("model.tile.ocean");
        TerrainType highSeas = ruleset.Terrain("model.tile.highSeas");

        bool[,] land = GrowContinent(width, height, random);
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
                    // Outermost columns are the high seas (the route to Europe);
                    // all other water is coastal ocean. Lakes/rivers are later work.
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

        return new GameMap(width, height, terrain, resources);
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
    private static bool[,] GrowContinent(int width, int height, IGameRandom random)
    {
        var land = new bool[width, height];
        int targetLand = (int)(width * height * 0.45);

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
