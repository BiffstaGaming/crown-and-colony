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
    public void TheDefaultLandStyle_IsContinent_AndIndexed()
    {
        // The shipped default style is Continent (the historical generator) at the default index — the dialog pre-selects it.
        Assert.Equal(LandStyle.Continent, WorldSizeOptions.DefaultLandStyle.Style);
        Assert.Equal(WorldSizeOptions.DefaultLandStyle, WorldSizeOptions.LandStyles[WorldSizeOptions.DefaultLandStyleIndex]);
        // The three FreeCol shapes are offered, Continent first.
        Assert.Equal(
            new[] { LandStyle.Continent, LandStyle.Archipelago, LandStyle.Islands },
            WorldSizeOptions.LandStyles.Select(s => s.Style));
    }

    [Fact]
    public void GameNew_WithDefaultLandStyle_MatchesTheParameterlessDefault()
    {
        // Passing the default style (Continent) explicitly must equal omitting it — the byte-identity contract (ADR-009).
        Game omitted = Game.New(Classic, seed: 31);
        Game continent = Game.New(Classic, seed: 31, landStyle: LandStyle.Continent);

        Assert.Equal(
            omitted.Map.AllPositions().Select(p => omitted.Map.TerrainAt(p).Id),
            continent.Map.AllPositions().Select(p => continent.Map.TerrainAt(p).Id));
    }

    [Fact]
    public void AnIslandsStyledGame_RoundTripsThroughSave_WithNoVersionBump()
    {
        // The landmass style only shapes which tiles are land; the result persists as terrain (since save v2), so a
        // non-default style needs no new save field/version — it round-trips like any generated map.
        Game game = Game.New(Classic, seed: 23, mapWidth: 56, mapHeight: 38, landStyle: LandStyle.Islands);
        Game restored = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);

        Assert.Equal(
            game.Map.AllPositions().Select(p => game.Map.TerrainAt(p).Id),
            restored.Map.AllPositions().Select(p => restored.Map.TerrainAt(p).Id));
        Assert.Equal(64, SaveGame.CurrentVersion); // the landmass-style feature adds no save field of its own (later bumps are unrelated slices)
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
        Assert.Equal(64, SaveGame.CurrentVersion); // the map-size feature itself added no save field (later bumps are unrelated slices)
    }
}
