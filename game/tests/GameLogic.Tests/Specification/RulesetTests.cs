using CrownAndColony.GameLogic.Specification;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Specification;

public class RulesetTests
{
    // One parse shared by all tests in this class — the spec is immutable.
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    [Fact]
    public void Classic_Defines23TerrainTypes()
    {
        // The classic FreeCol ruleset has exactly 23 tile types (8 base land,
        // 8 forests, hills, mountains, arctic, and 4 water types).
        Assert.Equal(23, Classic.TerrainTypes.Count);
    }

    [Fact]
    public void Plains_MatchesSpecificationValues()
    {
        TerrainType plains = Classic.Terrain("model.tile.plains");

        Assert.Equal(3, plains.MoveCost);
        Assert.Equal(3, plains.WorkTurns);
        Assert.False(plains.IsForest);
        Assert.False(plains.IsWater);
        Assert.False(plains.IsElevation);
        Assert.True(plains.CanSettle);
        Assert.Equal("plains", plains.ShortName);

        // Unattended (colony-center) yield: grain 3 + cotton 2.
        ProductionEntry unattended = Assert.Single(plains.Productions, p => p.Unattended);
        Assert.Equal(
            [("model.goods.grain", 3), ("model.goods.cotton", 2)],
            unattended.Outputs.Select(o => (o.GoodsId, o.Amount)).ToArray());

        // Attended options include grain 5 (the plains farming yield).
        Assert.Contains(plains.Productions, p =>
            !p.Unattended && p.Outputs.Any(o => o is { GoodsId: "model.goods.grain", Amount: 5 }));
    }

    [Fact]
    public void Mountains_AreUnsettleableSlowElevation()
    {
        TerrainType mountains = Classic.Terrain("model.tile.mountains");

        Assert.Equal(9, mountains.MoveCost);
        Assert.True(mountains.IsElevation);
        Assert.False(mountains.CanSettle);
        Assert.False(mountains.IsWater);
    }

    [Theory]
    [InlineData("model.tile.ocean", true)]
    [InlineData("model.tile.highSeas", true)]
    [InlineData("model.tile.greatRiver", false)] // spec: rivers are not high-seas connected
    [InlineData("model.tile.lake", false)]
    public void WaterTypes_AreWater_WithExpectedConnectivity(string id, bool connected)
    {
        TerrainType water = Classic.Terrain(id);

        Assert.True(water.IsWater);
        Assert.False(water.CanSettle);
        Assert.Equal(connected, water.IsConnected);
    }

    [Fact]
    public void AllForestTypes_AreFlagged()
    {
        // 8 forest variants in the classic ruleset.
        Assert.Equal(8, Classic.TerrainTypes.Count(t => t.IsForest));
    }

    [Fact]
    public void EveryTerrainType_HasPositiveMovementAndWorkValues()
    {
        Assert.All(Classic.TerrainTypes, t =>
        {
            Assert.True(t.MoveCost > 0, $"{t.Id} has non-positive MoveCost");
            Assert.True(t.WorkTurns > 0, $"{t.Id} has non-positive WorkTurns");
        });
    }

    [Fact]
    public void UnknownTerrainId_Throws()
    {
        Assert.Throws<KeyNotFoundException>(() => Classic.Terrain("model.tile.atlantis"));
    }

    [Fact]
    public void GenRanges_ParsedFromSpec()
    {
        GenRanges plains = Classic.Terrain("model.tile.plains").Gen!;
        Assert.Equal((0, 60, 0, 15, 1, 2), (
            plains.HumidityMin, plains.HumidityMax,
            plains.TemperatureMin, plains.TemperatureMax,
            plains.AltitudeMin, plains.AltitudeMax));

        GenRanges mountains = Classic.Terrain("model.tile.mountains").Gen!;
        Assert.Equal((20, 30), (mountains.AltitudeMin, mountains.AltitudeMax));

        Assert.True(plains.Contains(30, 10, 1));
        Assert.False(plains.Contains(70, 10, 1)); // too humid
        Assert.False(plains.Contains(30, 30, 1)); // too hot
    }

    [Fact]
    public void FreeColonist_ResolvesInheritedAttributes()
    {
        // movement/line-of-sight come from the abstract 'colonist' parent;
        // foundColony ability likewise.
        UnitType colonist = Classic.Unit("model.unit.freeColonist");

        Assert.Equal(3, colonist.Movement);
        Assert.Equal(1, colonist.LineOfSight);
        Assert.False(colonist.IsNaval);
        Assert.True(colonist.CanFoundColony);
        Assert.Equal("freeColonist", colonist.ShortName);
    }

    [Fact]
    public void Caravel_IsNavalWithShipAttributes()
    {
        UnitType caravel = Classic.Unit("model.unit.caravel");

        Assert.Equal(12, caravel.Movement);
        Assert.True(caravel.IsNaval);   // via abstract 'ship' parent's ability
        Assert.False(caravel.CanFoundColony);
    }

    [Fact]
    public void BuildingTypes_ParseWithProductionsAndCosts()
    {
        // Town hall: free building producing bells (1 unattended, 3 per worker).
        BuildingType townHall = Classic.Building("model.building.townHall");
        Assert.Empty(townHall.BuildCost);
        Assert.Contains(townHall.Productions, p =>
            p.Unattended && p.Outputs.Any(o => o is { GoodsId: "model.goods.bells", Amount: 1 }));
        Assert.Contains(townHall.Productions, p =>
            !p.Unattended && p.Outputs.Any(o => o is { GoodsId: "model.goods.bells", Amount: 3 }));

        // Carpenter's house: lumber 3 → hammers 3 per worker.
        BuildingType carpenter = Classic.Building("model.building.carpenterHouse");
        ProductionEntry conversion = Assert.Single(carpenter.Productions);
        Assert.Equal([("model.goods.lumber", 3)], conversion.Inputs.Select(i => (i.GoodsId, i.Amount)));
        Assert.Equal([("model.goods.hammers", 3)], conversion.Outputs.Select(o => (o.GoodsId, o.Amount)));

        // Lumber mill: upgrade with a hammer cost and population requirement.
        BuildingType mill = Classic.Building("model.building.lumberMill");
        Assert.Equal("model.building.carpenterHouse", mill.UpgradesFrom);
        Assert.Equal(3, mill.RequiredPopulation);
        Assert.Contains(mill.BuildCost, g => g is { GoodsId: "model.goods.hammers", Amount: 52 });

        Assert.True(Classic.BuildingTypes.Count >= 15, $"only {Classic.BuildingTypes.Count} building types");
    }

    [Fact]
    public void GoodsTypes_ParseMilitaryAndTradeFlags()
    {
        // Native tribute-demand goods selection (IndianDemandMission) classifies goods. Spec attributes
        // is-military / trade-goods (specification.xml: horses+muskets military, tradeGoods trade).
        Assert.True(Classic.Goods("model.goods.horses").IsMilitary);
        Assert.True(Classic.Goods("model.goods.muskets").IsMilitary);
        Assert.True(Classic.Goods("model.goods.tradeGoods").IsTradeGoods);

        // Plain goods carry neither flag (the default).
        GoodsType food = Classic.Goods("model.goods.food");
        Assert.False(food.IsMilitary);
        Assert.False(food.IsTradeGoods);
        Assert.False(Classic.Goods("model.goods.sugar").IsMilitary);
        Assert.False(Classic.Goods("model.goods.muskets").IsTradeGoods);
    }

    [Fact]
    public void BuildingMaterials_DerivedFromBuildCosts()
    {
        // The building-material category (FreeCol GoodsType.isBuildingMaterial) is derived from buildable
        // required-goods; classic colonies build with hammers (+ tools for upgrades), so both are present and
        // non-build goods (food/bells) are not.
        Assert.Contains("model.goods.hammers", Classic.BuildingMaterials);
        Assert.Contains("model.goods.tools", Classic.BuildingMaterials);
        Assert.DoesNotContain("model.goods.food", Classic.BuildingMaterials);
        Assert.DoesNotContain("model.goods.bells", Classic.BuildingMaterials);
    }

    [Fact]
    public void AbstractUnitTypes_AreNotExposed()
    {
        Assert.Throws<KeyNotFoundException>(() => Classic.Unit("colonist"));
        Assert.Throws<KeyNotFoundException>(() => Classic.Unit("ship"));
        Assert.DoesNotContain(Classic.UnitTypes, u => u.Id is "colonist" or "ship");
        Assert.True(Classic.UnitTypes.Count >= 20, $"only {Classic.UnitTypes.Count} unit types");
    }

    [Fact]
    public void MalformedSpecification_ThrowsFormatException()
    {
        static Stream Xml(string content)
        {
            var ms = new MemoryStream();
            using (var writer = new StreamWriter(ms, leaveOpen: true))
            {
                writer.Write(content);
            }
            ms.Position = 0;
            return ms;
        }

        // No tile-types section.
        Assert.Throws<RulesetFormatException>(() =>
            Ruleset.Load(Xml("<freecol-specification/>")));

        // Tile type without movement cost.
        Assert.Throws<RulesetFormatException>(() =>
            Ruleset.Load(Xml(
                "<freecol-specification><tile-types><tile-type id=\"x\" basic-work-turns=\"3\"/></tile-types></freecol-specification>")));

        // Duplicate ids.
        Assert.Throws<RulesetFormatException>(() =>
            Ruleset.Load(Xml(
                "<freecol-specification><tile-types>" +
                "<tile-type id=\"x\" basic-move-cost=\"3\" basic-work-turns=\"3\"/>" +
                "<tile-type id=\"x\" basic-move-cost=\"3\" basic-work-turns=\"3\"/>" +
                "</tile-types></freecol-specification>")));
    }
}
