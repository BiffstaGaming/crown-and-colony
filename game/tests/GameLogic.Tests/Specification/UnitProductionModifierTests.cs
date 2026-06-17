using System.Linq;
using CrownAndColony.GameLogic.Specification;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Specification;

/// <summary>
/// Per-colonist production data (<c>86d3b6nrz</c> slice 1): each unit type parses its <c>expert-production</c> good and
/// its <c>&lt;modifier id="model.goods.*" index="30"&gt;</c> children — the expert bonus or indentured/petty penalty
/// that colony production will fold once per-colonist identity lands. This slice is parse-only (no behaviour change).
/// </summary>
public class UnitProductionModifierTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    private static UnitProductionModifier ModifierFor(string unitTypeId, string goodsId) =>
        Classic.Unit(unitTypeId).ProductionModifiersOrEmpty.Single(m => m.GoodsId == goodsId);

    // ---- Experts: expert-production + index-30 bonus ----

    [Fact]
    public void ExpertFarmer_IsExpertAtGrain_WithAnAdditivePlusTwo()
    {
        UnitType expert = Classic.Unit("model.unit.expertFarmer");
        Assert.Equal("model.goods.grain", expert.ExpertProduction);
        UnitProductionModifier grain = ModifierFor("model.unit.expertFarmer", "model.goods.grain");
        Assert.Equal(ModifierType.Additive, grain.Type);
        Assert.Equal(2.0, grain.Value);   // the spec comment notes "2, not 3"
        Assert.Equal(30, grain.Index);    // unit-production modifier index
    }

    [Fact]
    public void ExpertFisherman_GetsPlusThreeFish()
    {
        Assert.Equal("model.goods.fish", Classic.Unit("model.unit.expertFisherman").ExpertProduction);
        Assert.Equal(3.0, ModifierFor("model.unit.expertFisherman", "model.goods.fish").Value);
    }

    [Fact]
    public void ExpertFurTrapper_GetsAMultiplicativeFursBonus()
    {
        UnitProductionModifier furs = ModifierFor("model.unit.expertFurTrapper", "model.goods.furs");
        Assert.Equal(ModifierType.Multiplicative, furs.Type);
        Assert.Equal(2.0, furs.Value);
    }

    // ---- Indentured servant / petty criminal penalties (manufactured goods only) ----

    private static readonly string[] PenalisedGoods =
    [
        "model.goods.rum", "model.goods.cigars", "model.goods.cloth", "model.goods.coats",
        "model.goods.muskets", "model.goods.bells", "model.goods.crosses", "model.goods.hammers", "model.goods.tools",
    ];

    [Theory]
    [InlineData("model.unit.indenturedServant", -1.0)]
    [InlineData("model.unit.pettyCriminal", -2.0)]
    public void LesserColonists_PenaliseTheNineManufacturedGoods_NotRawTiles(string unitTypeId, double penalty)
    {
        UnitType unit = Classic.Unit(unitTypeId);
        Assert.Equal(9, unit.ProductionModifiersOrEmpty.Count);
        Assert.All(unit.ProductionModifiersOrEmpty, m =>
        {
            Assert.Equal(ModifierType.Additive, m.Type);
            Assert.Equal(penalty, m.Value);
            Assert.Contains(m.GoodsId, PenalisedGoods);
        });
        // No penalty on raw-tile goods (so a lesser colonist's tile yield is unchanged — the penalty is building-only).
        Assert.DoesNotContain(unit.ProductionModifiersOrEmpty, m => m.GoodsId is "model.goods.grain" or "model.goods.furs" or "model.goods.ore");
        Assert.Null(unit.ExpertProduction);
    }

    // ---- Plain colonist: nothing ----

    [Fact]
    public void FreeColonist_HasNoProductionModifiersAndNoExpertise()
    {
        UnitType free = Classic.Unit("model.unit.freeColonist");
        Assert.Empty(free.ProductionModifiersOrEmpty);
        Assert.Null(free.ExpertProduction);
    }

    // ---- The modifier math (shared ModifierMath) ----

    [Fact]
    public void ApplyTo_FoldsAdditiveAndMultiplicativeCorrectly()
    {
        Assert.Equal(7.0, ModifierFor("model.unit.expertFarmer", "model.goods.grain").ApplyTo(5.0));     // 5 + 2
        Assert.Equal(6.0, ModifierFor("model.unit.expertFurTrapper", "model.goods.furs").ApplyTo(3.0));  // 3 × 2
    }
}
