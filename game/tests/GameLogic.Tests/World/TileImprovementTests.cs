using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.World.Improvements;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.World;

/// <summary>
/// Rivers &amp; tile improvements (<c>86d3b3qdx</c>) — foundation slice: the <see cref="TileImprovementType"/>
/// data model plus the pure production-delta and river-movement rule functions. No map placement, no
/// persistence, and no <c>Game</c> wiring yet (those are deferred follow-up slices). Cross-checked against
/// FreeCol's classic <c>model.improvement.river</c> (<c>data/rules/classic/specification.xml</c>) and
/// <c>TileImprovementType.getMoveCost</c>.
/// </summary>
public class TileImprovementTypeTests
{
    [Fact]
    public void ClassicRiver_MatchesFreeColSpecAttributes()
    {
        TileImprovementType river = TileImprovementType.ClassicRiver();

        Assert.Equal("model.improvement.river", river.Id);
        Assert.Equal("river", river.ShortName);
        Assert.Equal(1, river.Magnitude);      // classic spec magnitude="1"
        Assert.Equal(1, river.MovementCost);   // classic spec movement-cost="1"
        Assert.Equal(0, river.AddWorkTurns);   // classic spec add-work-turns="0"
        Assert.True(river.GrantsMovementBonus);
    }

    [Fact]
    public void ClassicRiver_HasExactlyTheFreeColGoodsModifiers()
    {
        TileImprovementType river = TileImprovementType.ClassicRiver();

        // Verbatim from data/rules/classic/specification.xml model.improvement.river <modifier> children.
        var expected = new Dictionary<string, double>
        {
            ["model.goods.grain"] = 1,
            ["model.goods.sugar"] = 1,
            ["model.goods.tobacco"] = 1,
            ["model.goods.cotton"] = 1,
            ["model.goods.furs"] = 2,
            ["model.goods.lumber"] = 2,
            ["model.goods.ore"] = 1,
            ["model.goods.silver"] = 1,
        };

        Assert.Equal(expected.Count, river.Modifiers.Count);
        foreach (ImprovementModifier mod in river.Modifiers)
        {
            Assert.True(expected.TryGetValue(mod.GoodsId, out double value), $"unexpected goods {mod.GoodsId}");
            Assert.Equal(ModifierType.Additive, mod.Type);
            Assert.Equal(value, mod.Value);
        }
    }

    [Fact]
    public void LargeRiver_IsMagnitudeTwo_SameFlatBonuses()
    {
        TileImprovementType large = TileImprovementType.ClassicRiver(magnitude: 2);

        Assert.Equal(2, large.Magnitude);
        // River bonuses are flat additives in FreeCol — magnitude does not scale them.
        Assert.Equal(TileImprovementType.ClassicRiver().Modifiers, large.Modifiers);
    }

    [Fact]
    public void FromModifiers_DefaultsToAdditive()
    {
        TileImprovementType t = TileImprovementType.FromModifiers(
            "model.improvement.test", magnitude: 1, movementCost: 0, addWorkTurns: 0,
            goodsDeltas: [("model.goods.furs", 3)]);

        ImprovementModifier mod = Assert.Single(t.Modifiers);
        Assert.Equal("model.goods.furs", mod.GoodsId);
        Assert.Equal(ModifierType.Additive, mod.Type);
        Assert.Equal(3, mod.Value);
        Assert.False(t.GrantsMovementBonus); // movementCost 0 → no travel bonus
    }

    [Fact]
    public void Modifiers_AreValueRecords_ComparedByValue()
    {
        // The scalar ImprovementModifier records compare by value, so two independently-built classic
        // rivers carry sequence-equal modifier lists. (The outer record holds the list by reference, like
        // the existing ResourceType/FatherModifier records, so whole-record equality is not asserted.)
        Assert.Equal(TileImprovementType.ClassicRiver().Modifiers, TileImprovementType.ClassicRiver().Modifiers);
        Assert.Equal(
            new ImprovementModifier("model.goods.furs", ModifierType.Additive, 2),
            new ImprovementModifier("model.goods.furs", ModifierType.Additive, 2));
        Assert.NotEqual(
            new ImprovementModifier("model.goods.furs", ModifierType.Additive, 2),
            new ImprovementModifier("model.goods.furs", ModifierType.Additive, 1));
    }
}

/// <summary>Pure production-delta rule (<see cref="ImprovementProduction"/>).</summary>
public class ImprovementProductionTests
{
    private static readonly TileImprovementType River = TileImprovementType.ClassicRiver();

    [Theory]
    [InlineData("model.goods.grain", 1)]
    [InlineData("model.goods.sugar", 1)]
    [InlineData("model.goods.tobacco", 1)]
    [InlineData("model.goods.cotton", 1)]
    [InlineData("model.goods.furs", 2)]
    [InlineData("model.goods.lumber", 2)]
    [InlineData("model.goods.ore", 1)]
    [InlineData("model.goods.silver", 1)]
    public void RiverYieldDelta_MatchesFreeColAdditiveBonus(string goodsId, double expected)
    {
        Assert.Equal(expected, ImprovementProduction.YieldDelta(River, goodsId));
    }

    [Fact]
    public void YieldDelta_GoodsNotBoosted_IsZero()
    {
        // The classic river confers no bell/coat/cloth bonus.
        Assert.Equal(0, ImprovementProduction.YieldDelta(River, "model.goods.bells"));
        Assert.Equal(0, ImprovementProduction.YieldDelta(River, "model.goods.cloth"));
    }

    [Fact]
    public void YieldDelta_NoImprovement_IsZero()
    {
        Assert.Equal(0, ImprovementProduction.YieldDelta((TileImprovementType?)null, "model.goods.furs"));
    }

    [Fact]
    public void YieldDelta_SumsMultipleModifiersForSameGoods()
    {
        // Two additive +2 furs modifiers on one (hypothetical) improvement → +4.
        var doubleFurs = new TileImprovementType(
            "model.improvement.test", 1, 0, 0,
            [
                new ImprovementModifier("model.goods.furs", ModifierType.Additive, 2),
                new ImprovementModifier("model.goods.furs", ModifierType.Additive, 2),
            ]);

        Assert.Equal(4, ImprovementProduction.YieldDelta(doubleFurs, "model.goods.furs"));
    }

    [Fact]
    public void YieldDelta_SupportsNegativeAdditive()
    {
        // FreeCol improvement modifiers can subtract; the helper must carry the sign through.
        var penalty = TileImprovementType.FromModifiers(
            "model.improvement.test", 1, 0, 0, [("model.goods.grain", -1)]);

        Assert.Equal(-1, ImprovementProduction.YieldDelta(penalty, "model.goods.grain"));
    }

    [Fact]
    public void YieldDelta_PercentageModifier_ContributesNothingOnZeroBase()
    {
        // A percentage modifier scales an existing yield; on its own (zero base) it adds nothing —
        // the caller folds it into a running yield later (deferred Game.TileYield wiring).
        var pct = new TileImprovementType(
            "model.improvement.test", 1, 0, 0,
            [new ImprovementModifier("model.goods.furs", ModifierType.Percentage, 50)]);

        Assert.Equal(0, ImprovementProduction.YieldDelta(pct, "model.goods.furs"));
    }

    [Fact]
    public void YieldDelta_OverImprovementList_SumsAndSkipsNulls()
    {
        var road = TileImprovementType.FromModifiers(
            "model.improvement.road", 1, 1, 0, [("model.goods.furs", 1)]);

        // river (+2 furs) + road (+1 furs) + a null slot → +3.
        Assert.Equal(3, ImprovementProduction.YieldDelta([River, null, road], "model.goods.furs"));
    }
}

/// <summary>Pure river-movement rule (<see cref="ImprovementMovement"/>).</summary>
public class ImprovementMovementTests
{
    private static readonly TileImprovementType River = TileImprovementType.ClassicRiver();

    [Fact]
    public void ReducedCost_AppliesWhenImprovementIsStrictlyCheaper()
    {
        // river cost 1 < normal entry 3 → reduced to 1.
        Assert.Equal(1, ImprovementMovement.ReducedCost(1, 3));
    }

    [Fact]
    public void ReducedCost_KeepsBaseWhenImprovementNotCheaper()
    {
        Assert.Equal(3, ImprovementMovement.ReducedCost(3, 3)); // equal → not strictly cheaper
        Assert.Equal(2, ImprovementMovement.ReducedCost(5, 2)); // dearer → base stands
    }

    [Fact]
    public void ReducedCost_NeverFreeMove_WhenImprovementCostIsZeroOrNegative()
    {
        // FreeCol guard: a zero/negative movement cost must not produce a free move.
        Assert.Equal(3, ImprovementMovement.ReducedCost(0, 3));
        Assert.Equal(3, ImprovementMovement.ReducedCost(-1, 3));
    }

    [Fact]
    public void RiverMoveCost_BothTilesHaveRiver_ReducedToRiverCost()
    {
        // Moving along a river between two river tiles costs the river cost (1 = a third of a normal move).
        Assert.Equal(1, ImprovementMovement.RiverMoveCost(River, River, baseCost: 3));
    }

    [Fact]
    public void RiverMoveCost_FromTileLacksRiver_NoBonus()
    {
        // The "follow the river" bonus needs a river on the tile being left as well as the one entered.
        Assert.Equal(3, ImprovementMovement.RiverMoveCost(null, River, baseCost: 3));
    }

    [Fact]
    public void RiverMoveCost_ToTileLacksRiver_NoBonus()
    {
        Assert.Equal(3, ImprovementMovement.RiverMoveCost(River, null, baseCost: 3));
    }

    [Fact]
    public void RiverMoveCost_NeitherTileHasRiver_NoBonus()
    {
        Assert.Equal(6, ImprovementMovement.RiverMoveCost(null, null, baseCost: 6));
    }

    [Fact]
    public void RiverMoveCost_RiverNotCheaperThanTerrain_BaseStands()
    {
        // If the destination terrain already costs <= the river cost, no reduction (never free, never dearer).
        Assert.Equal(1, ImprovementMovement.RiverMoveCost(River, River, baseCost: 1));
    }

    [Fact]
    public void RiverMoveCost_DestinationImprovementGrantsNoBonus_BaseStands()
    {
        var noBonusRiver = TileImprovementType.FromModifiers(
            "model.improvement.river", 1, movementCost: 0, addWorkTurns: 0, goodsDeltas: []);

        Assert.Equal(3, ImprovementMovement.RiverMoveCost(River, noBonusRiver, baseCost: 3));
    }
}
