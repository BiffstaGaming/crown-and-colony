using CrownAndColony.GameLogic.Randomness;
using CrownAndColony.GameLogic.Specification;

namespace CrownAndColony.GameLogic.World;

/// <summary>
/// Placeholder map generator for the Phase 1 walking skeleton: an ocean border
/// around a single landmass with randomly clustered terrain. Deterministic for a
/// given <see cref="IGameRandom"/>. Real FreeCol-style generation (humidity /
/// temperature / altitude bands from the spec's &lt;gen&gt; data) is Phase 2 work.
/// </summary>
public static class MapGenerator
{
    /// <summary>Generates a width × height map. Same ruleset + same RNG state → identical map.</summary>
    public static GameMap Generate(Ruleset ruleset, int width, int height, IGameRandom random)
    {
        TerrainType ocean = ruleset.Terrain("model.tile.ocean");

        // Land terrains for the skeleton, weighted toward open ground.
        (TerrainType type, int weight)[] landTable =
        [
            (ruleset.Terrain("model.tile.plains"), 25),
            (ruleset.Terrain("model.tile.grassland"), 20),
            (ruleset.Terrain("model.tile.prairie"), 15),
            (ruleset.Terrain("model.tile.mixedForest"), 15),
            (ruleset.Terrain("model.tile.coniferForest"), 10),
            (ruleset.Terrain("model.tile.hills"), 10),
            (ruleset.Terrain("model.tile.mountains"), 5),
        ];
        int totalWeight = landTable.Sum(e => e.weight);

        var terrain = new TerrainType[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // 2-tile ocean border; everything inland is part of the landmass.
                bool isOcean = x < 2 || y < 2 || x >= width - 2 || y >= height - 2;
                terrain[y * width + x] = isOcean ? ocean : PickWeighted(landTable, totalWeight, random);
            }
        }

        return new GameMap(width, height, terrain);
    }

    private static TerrainType PickWeighted(
        (TerrainType type, int weight)[] table, int totalWeight, IGameRandom random)
    {
        int roll = random.Next(totalWeight);
        foreach ((TerrainType type, int weight) in table)
        {
            roll -= weight;
            if (roll < 0)
            {
                return type;
            }
        }
        return table[^1].type; // unreachable; satisfies definite return
    }
}
