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

    [Fact]
    public void Hills_AreElevation_Too()
    {
        // Both hills and mountains are IsElevation — the region generator's mountain pass keys on it
        // (see RegionGeneratorTests.MountainRegions_IncludeHills_AndMergeAdjacentHillAndMountain).
        Assert.True(Classic.Terrain("model.tile.hills").IsElevation);
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
    public void UnitTypeAbilities_ParseFromSpecIntoHasAbilityMap()
    {
        // 86d3drpgg ABILITIES slice: navalUnit / foundColony / captureGoods / piracy / carryTreasure / expertScout
        // are now read from the parsed <ability> map via HasAbility, not a hardcoded flag. HasAbility must agree with
        // the convenience property, and an absent ability must read false (not throw).
        UnitType privateer = Classic.Unit("model.unit.privateer");
        Assert.True(privateer.HasAbility("model.ability.navalUnit"));
        Assert.True(privateer.HasAbility("model.ability.captureGoods"));
        Assert.True(privateer.HasAbility("model.ability.piracy"));
        Assert.Equal(privateer.IsNaval, privateer.HasAbility("model.ability.navalUnit"));
        Assert.Equal(privateer.CaptureGoods, privateer.HasAbility("model.ability.captureGoods"));
        Assert.Equal(privateer.Piracy, privateer.HasAbility("model.ability.piracy"));

        // Unknown / undeclared ability id defaults to false rather than throwing.
        Assert.False(privateer.HasAbility("model.ability.thisDoesNotExist"));
        Assert.False(privateer.HasAbility("model.ability.carryTreasure"));
    }

    [Theory]
    // (unit, naval, foundColony, captureGoods, piracy, carryTreasure, expertScout) — the classic values these
    // six data-driven capabilities resolve to. Pins the byte-identical result of the hardcoded→parsed refactor.
    [InlineData("model.unit.freeColonist", false, true, false, false, false, false)]
    [InlineData("model.unit.seasonedScout", false, true, false, false, false, true)]  // expertScout + colonist's foundColony
    [InlineData("model.unit.caravel", true, false, false, false, false, false)]       // naval via 'ship'; foundColony absent
    [InlineData("model.unit.frigate", true, false, true, false, false, false)]        // naval raider: captureGoods, no piracy
    [InlineData("model.unit.manOWar", true, false, true, false, false, false)]
    [InlineData("model.unit.privateer", true, false, true, true, false, false)]       // captureGoods + piracy
    [InlineData("model.unit.treasureTrain", false, false, false, false, true, false)] // carryTreasure; wagon sets foundColony=false
    public void DataDrivenUnitAbilities_MatchClassicValues(
        string id, bool naval, bool foundColony, bool captureGoods, bool piracy, bool carryTreasure, bool expertScout)
    {
        UnitType unit = Classic.Unit(id);

        Assert.Equal(naval, unit.IsNaval);
        Assert.Equal(foundColony, unit.CanFoundColony);
        Assert.Equal(captureGoods, unit.CaptureGoods);
        Assert.Equal(piracy, unit.Piracy);
        Assert.Equal(carryTreasure, unit.CarryTreasure);
        Assert.Equal(expertScout, unit.ExpertScout);

        // Each convenience property must be exactly its HasAbility consult (no divergence).
        Assert.Equal(unit.HasAbility("model.ability.navalUnit"), unit.IsNaval);
        Assert.Equal(unit.HasAbility("model.ability.foundColony"), unit.CanFoundColony);
        Assert.Equal(unit.HasAbility("model.ability.captureGoods"), unit.CaptureGoods);
        Assert.Equal(unit.HasAbility("model.ability.piracy"), unit.Piracy);
        Assert.Equal(unit.HasAbility("model.ability.carryTreasure"), unit.CarryTreasure);
        Assert.Equal(unit.HasAbility("model.ability.expertScout"), unit.ExpertScout);
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
    public void BuildingTypeAbilities_ParseFromSpecIntoHasAbilityMap()
    {
        // 86d3drpgg building-ABILITIES slice: repairUnits / bombardShips / export / dressMissionary / produceInWater /
        // teach are now read from the parsed <ability> map via BuildingType.HasAbility, not six dedicated flags.
        // HasAbility must agree with the convenience property, and an absent ability must read false (not throw).
        BuildingType docks = Classic.Building("model.building.docks");
        Assert.True(docks.HasAbility("model.ability.produceInWater"));
        Assert.Equal(docks.ProducesInWater, docks.HasAbility("model.ability.produceInWater"));

        // Inheritance down the extends chain: the drydock/shipyard inherit docks' produceInWater (nearest wins).
        Assert.True(Classic.Building("model.building.drydock").HasAbility("model.ability.produceInWater"));
        Assert.True(Classic.Building("model.building.shipyard").HasAbility("model.ability.produceInWater"));

        // Custom house declares export; the cathedral inherits the church's dressMissionary; the fortress inherits
        // the fort's bombardShips; the college/university inherit the schoolhouse's teach.
        Assert.True(Classic.Building("model.building.customHouse").GrantsExport);
        Assert.True(Classic.Building("model.building.cathedral").DressesMissionary);
        Assert.True(Classic.Building("model.building.fortress").BombardsShips);
        Assert.True(Classic.Building("model.building.university").Teaches);

        // Drydock declares repairUnits; the shipyard inherits it.
        Assert.True(Classic.Building("model.building.drydock").RepairsNavalUnits);
        Assert.True(Classic.Building("model.building.shipyard").RepairsNavalUnits);

        // Unknown / undeclared ability id defaults to false rather than throwing.
        Assert.False(docks.HasAbility("model.ability.thisDoesNotExist"));
        Assert.False(docks.HasAbility("model.ability.export"));
    }

    [Theory]
    // (building, repairUnits, bombardShips, export, dressMissionary, produceInWater, teach) — the classic values the
    // six data-driven building capabilities resolve to. Pins the byte-identical result of the hardcoded→parsed refactor.
    [InlineData("model.building.townHall", false, false, false, false, false, false)]
    [InlineData("model.building.chapel", false, false, false, false, false, false)]   // chapel does NOT dress missionaries
    [InlineData("model.building.church", false, false, false, true, false, false)]    // church declares dressMissionary
    [InlineData("model.building.cathedral", false, false, false, true, false, false)] // inherits church's dressMissionary
    [InlineData("model.building.stockade", false, false, false, false, false, false)]
    [InlineData("model.building.fort", false, true, false, false, false, false)]      // fort grants bombardShips
    [InlineData("model.building.fortress", false, true, false, false, false, false)]  // inherits fort's bombardShips
    [InlineData("model.building.docks", false, false, false, false, true, false)]     // docks grant produceInWater
    [InlineData("model.building.drydock", true, false, false, false, true, false)]    // repairUnits + inherited produceInWater
    [InlineData("model.building.shipyard", true, false, false, false, true, false)]   // inherits repairUnits + produceInWater
    [InlineData("model.building.customHouse", false, false, true, false, false, false)] // customHouse grants export
    [InlineData("model.building.schoolhouse", false, false, false, false, false, true)] // schoolhouse declares teach
    [InlineData("model.building.college", false, false, false, false, false, true)]    // inherits teach
    [InlineData("model.building.university", false, false, false, false, false, true)] // inherits teach
    public void DataDrivenBuildingAbilities_MatchClassicValues(
        string id, bool repairUnits, bool bombardShips, bool export, bool dressMissionary, bool produceInWater, bool teach)
    {
        BuildingType building = Classic.Building(id);

        Assert.Equal(repairUnits, building.RepairsNavalUnits);
        Assert.Equal(bombardShips, building.BombardsShips);
        Assert.Equal(export, building.GrantsExport);
        Assert.Equal(dressMissionary, building.DressesMissionary);
        Assert.Equal(produceInWater, building.ProducesInWater);
        Assert.Equal(teach, building.Teaches);

        // Each convenience property must be exactly its HasAbility consult (no divergence).
        Assert.Equal(building.HasAbility("model.ability.repairUnits"), building.RepairsNavalUnits);
        Assert.Equal(building.HasAbility("model.ability.bombardShips"), building.BombardsShips);
        Assert.Equal(building.HasAbility("model.ability.export"), building.GrantsExport);
        Assert.Equal(building.HasAbility("model.ability.dressMissionary"), building.DressesMissionary);
        Assert.Equal(building.HasAbility("model.ability.produceInWater"), building.ProducesInWater);
        Assert.Equal(building.HasAbility("model.ability.teach"), building.Teaches);
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
    public void BuildingMaterials_DerivedFromAllBuildables()
    {
        // The building-material category (FreeCol GoodsType.isBuildingMaterial) is derived over ALL buildable types
        // (buildings + units + roles). Classic content: buildings/units build with hammers (+ tools); the free
        // colonist's required-goods food=200 makes food one; the armed/mounted roles make muskets/horses ones
        // (86d3c18n8 — so the native tribute-demand building rung includes food, as FreeCol does).
        Assert.Contains("model.goods.hammers", Classic.BuildingMaterials);
        Assert.Contains("model.goods.tools", Classic.BuildingMaterials);
        Assert.Contains("model.goods.food", Classic.BuildingMaterials);     // freeColonist required-goods food=200
        Assert.Contains("model.goods.muskets", Classic.BuildingMaterials);  // armedBrave/soldier role required-goods
        Assert.Contains("model.goods.horses", Classic.BuildingMaterials);   // mountedBrave/dragoon role required-goods
        Assert.DoesNotContain("model.goods.bells", Classic.BuildingMaterials); // nothing is built from bells
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
