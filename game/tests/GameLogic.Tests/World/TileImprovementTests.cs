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

    // The generalised MoveCost over a tile's improvement list (river AND road).
    private static readonly TileImprovementType Road =
        Ruleset.LoadClassic().Improvement(TileImprovementType.RoadId);
    private static readonly TileImprovementType Plow =
        Ruleset.LoadClassic().Improvement(TileImprovementType.PlowId);

    [Fact]
    public void MoveCost_BothTilesHaveRoad_ReducedToRoadCost()
    {
        Assert.Equal(1, ImprovementMovement.MoveCost([Road], [Road], baseCost: 3));
    }

    [Fact]
    public void MoveCost_RoadConnectsToRiver_BonusApplies()
    {
        // A road tile stepping onto a river tile (and vice versa) still follows the bonus (both grant movement).
        Assert.Equal(1, ImprovementMovement.MoveCost([Road], [River], baseCost: 3));
        Assert.Equal(1, ImprovementMovement.MoveCost([River], [Road], baseCost: 3));
    }

    [Fact]
    public void MoveCost_RiverAndRoadOnOneTile_StillConnectsToRoadOnlyNeighbour()
    {
        // A tile with both a river and a road connects to a road-only (or river-only) neighbour.
        Assert.Equal(1, ImprovementMovement.MoveCost([River, Road], [Road], baseCost: 6));
    }

    [Fact]
    public void MoveCost_OnlyOneTileHasAMovementImprovement_NoBonus()
    {
        Assert.Equal(3, ImprovementMovement.MoveCost([], [Road], baseCost: 3));     // origin has none
        Assert.Equal(3, ImprovementMovement.MoveCost([Road], [], baseCost: 3));     // destination has none
    }

    [Fact]
    public void MoveCost_PlowGrantsNoMovementBonus()
    {
        // Plowed fields confer no movement bonus (movement-cost absent in the spec) — base cost stands.
        Assert.Equal(3, ImprovementMovement.MoveCost([Plow], [Plow], baseCost: 3));
    }
}

/// <summary>
/// The pioneer-built improvement types parsed from the classic ruleset (road / plow / clear-forest) — their
/// FreeCol attributes (required role, tool cost, work turns), applicability scopes, and terrain transformations.
/// Cross-checked against <c>data/rules/classic/specification.xml</c> <c>&lt;tile-improvement-type&gt;</c>.
/// </summary>
public class PioneerImprovementTypeTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private static readonly TileImprovementType Road = Classic.Improvement(TileImprovementType.RoadId);
    private static readonly TileImprovementType Plow = Classic.Improvement(TileImprovementType.PlowId);
    private static readonly TileImprovementType Clear = Classic.Improvement(TileImprovementType.ClearForestId);

    [Fact]
    public void Road_MatchesFreeColSpec()
    {
        Assert.True(Road.IsRoad);
        Assert.False(Road.IsNatural);                                   // pioneer-built
        Assert.Equal("model.role.pioneer", Road.RequiredRoleId);
        Assert.Equal(1, Road.ExpendedAmount);                           // one pioneer count = 20 tools
        Assert.Equal(0, Road.AddWorkTurns);
        Assert.Equal(1, Road.MovementCost);                             // road speeds movement
        Assert.True(Road.GrantsMovementBonus);
        Assert.False(Road.ChangesTerrain);
    }

    [Fact]
    public void Plow_MatchesFreeColSpec()
    {
        Assert.True(Plow.IsPlow);
        Assert.False(Plow.IsNatural);
        Assert.Equal("model.role.pioneer", Plow.RequiredRoleId);
        Assert.Equal(1, Plow.ExpendedAmount);
        Assert.Equal(2, Plow.AddWorkTurns);                            // plow add-work-turns="2"
        Assert.Equal(0, Plow.MovementCost);                            // plow grants no movement bonus
        Assert.False(Plow.GrantsMovementBonus);
        Assert.False(Plow.ChangesTerrain);
        // The four farmed-goods +1 bonuses (folded by TileYieldPotential once placed).
        Assert.Equal(1, ImprovementProduction.YieldDelta(Plow, "model.goods.grain"));
        Assert.Equal(1, ImprovementProduction.YieldDelta(Plow, "model.goods.cotton"));
    }

    [Fact]
    public void ClearForest_MatchesFreeColSpec()
    {
        Assert.True(Clear.IsClearForest);
        Assert.False(Clear.IsNatural);
        Assert.Equal("model.role.pioneer", Clear.RequiredRoleId);
        Assert.Equal(1, Clear.ExpendedAmount);
        Assert.Equal(2, Clear.AddWorkTurns);                           // clearForest add-work-turns="2"
        Assert.Equal(5, Clear.ExposeResourcePercent);                  // expose-resource-percent="5"
        Assert.True(Clear.ChangesTerrain);
    }

    [Fact]
    public void Road_AppliesToAnyLand_ButNotWater()
    {
        Assert.True(Road.AppliesTo(Classic.Terrain("model.tile.plains")));
        Assert.True(Road.AppliesTo(Classic.Terrain("model.tile.mixedForest")));
        Assert.True(Road.AppliesTo(Classic.Terrain("model.tile.hills")));
        Assert.False(Road.AppliesTo(Classic.Terrain("model.tile.ocean")));    // <scope method-name="isWater" method-value="false"/>
    }

    [Fact]
    public void Plow_AppliesToClearedFarmland_ButNotForestWaterOrElevation()
    {
        Assert.True(Plow.AppliesTo(Classic.Terrain("model.tile.plains")));
        Assert.True(Plow.AppliesTo(Classic.Terrain("model.tile.grassland")));
        Assert.False(Plow.AppliesTo(Classic.Terrain("model.tile.mixedForest"))); // not forested
        Assert.False(Plow.AppliesTo(Classic.Terrain("model.tile.hills")));        // negated hills scope
        Assert.False(Plow.AppliesTo(Classic.Terrain("model.tile.mountains")));    // negated mountains scope
        Assert.False(Plow.AppliesTo(Classic.Terrain("model.tile.arctic")));       // negated arctic scope
        Assert.False(Plow.AppliesTo(Classic.Terrain("model.tile.ocean")));        // not water
    }

    [Fact]
    public void ClearForest_AppliesOnlyToForest()
    {
        Assert.True(Clear.AppliesTo(Classic.Terrain("model.tile.mixedForest")));
        Assert.True(Clear.AppliesTo(Classic.Terrain("model.tile.coniferForest")));
        Assert.False(Clear.AppliesTo(Classic.Terrain("model.tile.plains")));      // <scope isForested true/>
        Assert.False(Clear.AppliesTo(Classic.Terrain("model.tile.ocean")));
    }

    [Theory]
    [InlineData("model.tile.mixedForest", "model.tile.plains", 20)]
    [InlineData("model.tile.coniferForest", "model.tile.grassland", 20)]
    [InlineData("model.tile.broadleafForest", "model.tile.prairie", 20)]
    [InlineData("model.tile.scrubForest", "model.tile.desert", 10)]   // scrub yields only 10 lumber
    [InlineData("model.tile.borealForest", "model.tile.tundra", 20)]
    public void ClearForest_TileTypeChange_MatchesFreeColSpec(string from, string toTerrain, int lumber)
    {
        ImprovementTypeChange? change = Clear.ChangeFrom(from);
        Assert.NotNull(change);
        Assert.Equal(toTerrain, change!.ToTerrainId);
        Assert.Equal("model.goods.lumber", change.ProductionGoodsId);
        Assert.Equal(lumber, change.ProductionAmount);
    }

    [Fact]
    public void ClearForest_NoChangeFromNonForest()
    {
        Assert.Null(Clear.ChangeFrom("model.tile.plains"));
    }

    [Fact]
    public void River_ParsesAsNatural_NoRequiredRoleOrToolCost()
    {
        TileImprovementType river = Classic.RiverType;
        Assert.True(river.IsNatural);
        Assert.Null(river.RequiredRoleId);
        Assert.Equal(0, river.ExpendedAmount);
    }

    [Fact]
    public void PioneerRole_CanImproveTerrain()
    {
        Assert.True(Classic.Role("model.role.pioneer").CanImproveTerrain);
        Assert.False(Classic.Role("model.role.soldier").CanImproveTerrain);
        Assert.False(Classic.Role(CrownAndColony.GameLogic.Specification.RoleType.DefaultRoleId).CanImproveTerrain);
    }
}
