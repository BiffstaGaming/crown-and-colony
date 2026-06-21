using System.Linq;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.World;

/// <summary>
/// New-game world-size / land-mass options (<c>86d3c9w9c</c>, FreeCol <c>MapGeneratorOptions</c>): a player picks the
/// map size and how much of it is land; the picks thread through <see cref="Game.New"/> →
/// <see cref="MapGenerator.Generate"/>. The presets are "data not code" (<see cref="WorldSizeOptions"/>); the defaults
/// match the shipped world so a default game is byte-identical. No save-format change — map dimensions already persist.
/// </summary>
public class WorldSizeOptionsTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    [Fact]
    public void EveryPreset_StaysWithinFreeColBounds()
    {
        // FreeCol's MapGeneratorOptions limits: width 30–200, height 20–200, land mass 15–50%.
        Assert.All(WorldSizeOptions.Sizes, s =>
        {
            Assert.InRange(s.Width, 30, 200);
            Assert.InRange(s.Height, 20, 200);
        });
        Assert.All(WorldSizeOptions.LandMasses, l => Assert.InRange(l.Fraction, 0.15, 0.50));
    }

    [Fact]
    public void TheDefaults_MatchTheShippedWorld()
    {
        Assert.Equal(36, WorldSizeOptions.DefaultSize.Width);
        Assert.Equal(24, WorldSizeOptions.DefaultSize.Height);
        Assert.Equal(MapGenerator.DefaultLandMassFraction, WorldSizeOptions.DefaultLandMass.Fraction);

        // The default indices point at the default presets (the dialog pre-selects them).
        Assert.Equal(WorldSizeOptions.DefaultSize, WorldSizeOptions.Sizes[WorldSizeOptions.DefaultSizeIndex]);
        Assert.Equal(WorldSizeOptions.DefaultLandMass, WorldSizeOptions.LandMasses[WorldSizeOptions.DefaultLandMassIndex]);
    }

    [Fact]
    public void Presets_AreOrdered_SmallestAndLeastLandFirst()
    {
        Assert.Equal(
            WorldSizeOptions.Sizes.OrderBy(s => s.Width * s.Height).ToList(),
            WorldSizeOptions.Sizes.ToList());
        Assert.Equal(
            WorldSizeOptions.LandMasses.OrderBy(l => l.Fraction).ToList(),
            WorldSizeOptions.LandMasses.ToList());
    }

    [Fact]
    public void GameNew_AtTheDefaultSize_MatchesTheParameterlessDefault()
    {
        // Omitting the world-shape params and passing the shipped defaults must produce the identical map (so the
        // default new game — and the visual goldens/soak baseline that depend on it — never shift).
        Game omitted = Game.New(Classic, seed: 13);
        WorldSize size = WorldSizeOptions.DefaultSize;
        Game explicitDefault = Game.New(Classic, seed: 13, size.Width, size.Height,
            landMassFraction: WorldSizeOptions.DefaultLandMass.Fraction);

        Assert.Equal(omitted.Map.Width, explicitDefault.Map.Width);
        Assert.Equal(
            omitted.Map.AllPositions().Select(p => omitted.Map.TerrainAt(p).Id),
            explicitDefault.Map.AllPositions().Select(p => explicitDefault.Map.TerrainAt(p).Id));
    }

    [Fact]
    public void ANonDefaultSizedGame_RoundTripsThroughSave_WithNoVersionBump()
    {
        // Map dimensions have persisted since save v2, so a non-default size needs no new save field/version.
        Game game = Game.New(Classic, seed: 21, mapWidth: 30, mapHeight: 20, landMassFraction: 0.50);
        Assert.Equal(30, game.Map.Width);
        Assert.Equal(20, game.Map.Height);

        Game restored = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);

        Assert.Equal(30, restored.Map.Width);
        Assert.Equal(20, restored.Map.Height);
        Assert.Equal(
            game.Map.AllPositions().Select(p => game.Map.TerrainAt(p).Id),
            restored.Map.AllPositions().Select(p => restored.Map.TerrainAt(p).Id));
        Assert.Equal(49, SaveGame.CurrentVersion); // the map-size feature itself added no save field (later bumps are unrelated slices)
    }
}
