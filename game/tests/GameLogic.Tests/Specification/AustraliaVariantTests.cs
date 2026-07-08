using System.Linq;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Specification;

/// <summary>
/// The Australian Federation variant (P8, ADR-018) — its skeleton slice: the variant is registered and selectable, its
/// ruleset loads, the authored Australia continent map (60×40, de-staggered from the FreeCol community map pack) imports and
/// boots a game, and a save records the variant so it reloads under the Australia ruleset. The spec starts as a copy of
/// classic; the transposability-anchor guard here protects the later reskin slices (renaming display text must keep the
/// well-known ids the engine machinery reads).
/// </summary>
public class AustraliaVariantTests
{
    private static readonly Ruleset Australia = GameVariants.Australia.LoadRuleset();

    [Fact]
    public void Registry_ContainsAustralia_SelectableAndResolvable()
    {
        Assert.Contains(GameVariants.All, v => v.Id == "australia");
        Assert.Same(GameVariants.Australia, GameVariants.ById("australia"));
        Assert.Same(GameVariants.Australia, GameVariants.Resolve("australia"));
        Assert.Equal("Australian Federation", GameVariants.Australia.DisplayName);
        Assert.NotSame(GameVariants.Australia, GameVariants.Default); // classic stays the default
    }

    [Fact]
    public void AustraliaVariant_LoadsARuleset_KeepingTheTransposabilityAnchors()
    {
        // The spec parses (units + fathers present)…
        Assert.NotEmpty(Australia.UnitTypes);
        Assert.NotEmpty(Australia.FoundingFathers);

        // …and it still declares every structural id the variant-agnostic engine keys on (ADR-018). A later reskin
        // renames the DISPLAY of these (Liberty Bells → Civic Voice), but the ids must stay or the machinery breaks.
        foreach (string goodsId in new[]
                 {
                     "model.goods.food", "model.goods.bells", "model.goods.crosses", "model.goods.grain",
                 })
        {
            Assert.NotNull(Australia.Goods(goodsId));
        }
        Assert.NotNull(Australia.Terrain("model.tile.ocean"));
        Assert.NotNull(Australia.Terrain("model.tile.highSeas"));
    }

    [Fact]
    public void AustraliaMap_ImportsAt60x40_EveryTileResolved()
    {
        // 60×40 — de-staggered from the FreeCol 30×80 source (FreeCol y counts HALF-rows), restoring the continent's
        // real wider-than-tall proportions (Chris 2026-07-08; see data/maps/PROVENANCE.md).
        GameMap map = FixedMap.LoadAustralia(Australia);
        Assert.Equal(60, map.Width);
        Assert.Equal(40, map.Height);
        // Reaching here means all 2400 tiles resolved (the importer throws on an unknown id).
        Assert.Equal(60 * 40, map.AllPositions().Count());
    }

    [Fact]
    public void AustraliaMap_LooksLikeAustralia_AridInteriorInAWideOcean()
    {
        GameMap map = FixedMap.LoadAustralia(Australia);

        // A continent adrift in ocean: the 60×40 grid is mostly water (Tasman/Indian/Southern seas + high seas).
        int water = map.AllPositions().Count(p => map.TerrainAt(p).IsWater);
        Assert.True(water > map.Width * map.Height / 2, $"expected mostly water, got {water}/{map.Width * map.Height}");

        // …with the hallmark arid centre plus coastal/temperate variety (the real continent's terrain palette).
        var counts = map.AllPositions().GroupBy(p => map.TerrainAt(p).Id).ToDictionary(g => g.Key, g => g.Count());
        foreach (string id in new[]
                 {
                     "model.tile.desert", "model.tile.savannah", "model.tile.plains", "model.tile.mountains",
                 })
        {
            Assert.True(counts.GetValueOrDefault(id, 0) > 0, $"expected some {id} on the Australia map");
        }
    }

    [Fact]
    public void AustraliaMap_IsDeterministic_SameBytesEveryLoad()
    {
        GameMap a = FixedMap.LoadAustralia(Australia);
        GameMap b = FixedMap.LoadAustralia(Australia);
        Assert.Equal(
            a.AllPositions().Select(p => a.TerrainAt(p).Id),
            b.AllPositions().Select(p => b.TerrainAt(p).Id));
        Assert.NotNull(FixedMap.TryLoad(MapSource.Australia, Australia));
    }

    [Fact]
    public void AustraliaGame_BootsOnItsMap_AndTheTurnAdvances()
    {
        Game game = Game.New(Australia, seed: 0xA05UL, mapSource: MapSource.Australia);
        Assert.Equal(60, game.Map.Width);
        Assert.Equal(40, game.Map.Height);
        Assert.NotEmpty(game.PlayerUnits); // the human landed with a starting party on the Australia map

        int before = game.Turn;
        game.EndTurn();
        Assert.Equal(before + 1, game.Turn); // a full round completes on the new map
    }

    [Fact]
    public void AustraliaSave_RecordsTheVariant_AndReloadsUnderItsRuleset()
    {
        Game game = Game.New(Australia, seed: 0xA05UL, mapSource: MapSource.Australia);

        SaveGame save = SaveGame.From(game, GameVariants.Australia.Id);
        Assert.Equal("australia", save.Variant);
        SaveGame restored = SaveGame.FromJson(save.ToJson());
        Assert.Equal("australia", restored.Variant);                       // survives the JSON round-trip
        Assert.Same(GameVariants.Australia, GameVariants.Resolve(restored.Variant)); // → the Australia ruleset on load
    }
}
