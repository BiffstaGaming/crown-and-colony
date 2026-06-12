using CrownAndColony.GameLogic.Specification;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Specification;

/// <summary>
/// L2 cross-check (docs/TESTING.md): terrain production values pinned against
/// FreeCol's classic ruleset — the foundation the Phase 3 economy is verified
/// on. Values read once from `data/rules/classic/specification.xml` and frozen
/// here; if these fail, either the spec file or the parser changed.
/// </summary>
public class ProductionCrossCheckTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    public static TheoryData<string, string, int> AttendedProduction => new()
    {
        // terrain, goods, best attended yield
        { "model.tile.plains", "model.goods.grain", 5 },
        { "model.tile.plains", "model.goods.cotton", 2 },
        { "model.tile.plains", "model.goods.ore", 1 },
        { "model.tile.grassland", "model.goods.tobacco", 3 },
        { "model.tile.prairie", "model.goods.cotton", 3 },
        { "model.tile.savannah", "model.goods.grain", 4 },
        { "model.tile.savannah", "model.goods.sugar", 3 },
        { "model.tile.marsh", "model.goods.ore", 2 },
        { "model.tile.swamp", "model.goods.sugar", 2 },
        { "model.tile.mountains", "model.goods.ore", 4 },
        { "model.tile.mountains", "model.goods.silver", 1 },
    };

    [Theory]
    [MemberData(nameof(AttendedProduction))]
    public void AttendedYields_MatchClassicRuleset(string terrainId, string goodsId, int expected)
    {
        TerrainType terrain = Classic.Terrain(terrainId);

        int best = terrain.Productions
            .Where(p => !p.Unattended)
            .SelectMany(p => p.Outputs)
            .Where(o => o.GoodsId == goodsId)
            .Max(o => o.Amount);

        Assert.Equal(expected, best);
    }

    [Fact]
    public void UnattendedColonyCentreYields_MatchClassicRuleset()
    {
        // The tile a colony sits on works itself: plains gives grain 3 + cotton 2.
        TerrainType plains = Classic.Terrain("model.tile.plains");
        ProductionEntry centre = Assert.Single(plains.Productions, p => p.Unattended);
        Assert.Equal(
            [("model.goods.grain", 3), ("model.goods.cotton", 2)],
            centre.Outputs.Select(o => (o.GoodsId, o.Amount)).ToArray());

        // Grassland centre: grain 3 + tobacco 3.
        TerrainType grassland = Classic.Terrain("model.tile.grassland");
        ProductionEntry gCentre = Assert.Single(grassland.Productions, p => p.Unattended);
        Assert.Contains(gCentre.Outputs, o => o is { GoodsId: "model.goods.tobacco", Amount: 3 });
    }

    [Fact]
    public void EveryLandTerrain_HasAtLeastOneProductionOption()
    {
        Assert.All(
            Classic.TerrainTypes.Where(t => !t.IsWater),
            t => Assert.NotEmpty(t.Productions));
    }
}
