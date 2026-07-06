using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Colonies;

/// <summary>
/// Per-worker building production (<c>86d3b6nrz</c> slice 5): a building's output is now the <em>sum</em> of each
/// assigned worker's type-modified contribution — a master carpenter makes more hammers, an indentured/petty colonist
/// makes fewer (floored at 0 per worker) — and the inputs consumed scale with that modified output (FreeCol's output
/// ratio). The all-free case is unchanged (sums to base × workers), which the existing <c>BuildingTests</c> lock down.
/// Deltas are asserted (specialist − free) so the shared Sons-of-Liberty bonus cancels and the test is bonus-agnostic.
/// </summary>
public class BuildingWorkerTypeProductionTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    // A seed whose starting tile is non-sugar terrain, so the colony's centre tile auto-produces no sugar and the
    // factory input-flooring assertions below see only the sugar the test adds (the P5 mountain-range map-gen change
    // moved the old 0xC0FFEE start tile onto savannah, which auto-produces sugar — see WorldTests range pass).
    private const ulong Seed = 3UL;
    private const string Carpenter = "model.building.carpenterHouse";
    private const string Lumber = "model.goods.lumber";
    private const string Hammers = "model.goods.hammers";
    private const string Food = "model.goods.food";
    private const string Free = "model.unit.freeColonist";
    private const string MasterCarpenter = "model.unit.masterCarpenter"; // hammers +3 (base 3 → 6)
    private const string MasterDistiller = "model.unit.masterDistiller"; // rum ×2 (multiplicative)
    private const string Indentured = "model.unit.indenturedServant";    // hammers -1 (base 3 → 2)
    private const string Petty = "model.unit.pettyCriminal";             // hammers -2 (base 3 → 1)
    private const string DistillerHouse = "model.building.distillerHouse"; // sugar 3 → rum 3 (base, rebel-factor 1)
    private const string LumberMill = "model.building.lumberMill";       // lumber 6 → hammers 6, rebel-factor 2, competence 2
    private const string RumFactory = "model.building.rumFactory";       // sugar 6 → rum 9 (non-1:1 factory)
    private const string Distillery = "model.building.rumDistillery";    // sugar 6 → rum 6, competence 2 (master distiller ×2 — multiplicative)
    private const string Sugar = "model.goods.sugar";
    private const string Rum = "model.goods.rum";

    /// <summary>A fresh colony whose only workers are the given types, all in the carpenter's house, well-fed.</summary>
    private static Colony CarpenterColony(Game game, params string[] workerTypes)
    {
        Colony colony = game.FoundColony(game.Units.First(u => u.IsOnMap && u.Type.CanFoundColony));
        foreach (Position tile in colony.TileWorkers.Keys.ToList())
        {
            game.UnassignWork(colony, tile); // pull the founder out of the fields so only building work runs
        }
        colony.Population = workerTypes.Length;
        foreach (string type in workerTypes)
        {
            colony.AssignBuildingWorker(Carpenter, type);
        }
        colony.AddGoods(Food, 100); // no starvation / growth perturbing the worker count this turn
        return colony;
    }

    private static int HammersAfterOneTurn(string[] workerTypes, int lumber)
    {
        Game game = Game.New(Classic, Seed);
        Colony colony = CarpenterColony(game, workerTypes);
        colony.AddGoods(Lumber, lumber);
        game.EndTurn();
        return colony.StoreOf(Hammers);
    }

    [Fact]
    public void MasterCarpenter_MakesThreeMoreHammers_ThanAFreeColonist()
    {
        // Base carpenter's house is 3 hammers/worker; the master's +3 modifier → 6. The +3 delta cancels the SoL bonus.
        Assert.Equal(HammersAfterOneTurn([Free], lumber: 10) + 3, HammersAfterOneTurn([MasterCarpenter], lumber: 10));
    }

    [Fact]
    public void IndenturedServant_MakesOneFewerHammer()
    {
        Assert.Equal(HammersAfterOneTurn([Free], lumber: 10) - 1, HammersAfterOneTurn([Indentured], lumber: 10));
    }

    [Fact]
    public void PettyCriminal_MakesTwoFewerHammers()
    {
        Assert.Equal(HammersAfterOneTurn([Free], lumber: 10) - 2, HammersAfterOneTurn([Petty], lumber: 10));
    }

    [Fact]
    public void MixedBuilding_SumsEachWorkersOutput()
    {
        // master (6) + free (3) = 9 vs two free (3 + 3 = 6): a +3 delta, with the same 2×SoL bonus on both buildings.
        Assert.Equal(
            HammersAfterOneTurn([Free, Free], lumber: 20) + 3,
            HammersAfterOneTurn([MasterCarpenter, Free], lumber: 20));
    }

    [Fact]
    public void Input_ScalesWithModifiedOutput_MasterEatsDoubleTheLumber()
    {
        // 1:1 conversion: a master carpenter making 6 hammers eats 6 lumber; a free colonist making 3 eats 3. Input
        // consumption carries no SoL term, so these are exact.
        Game gMaster = Game.New(Classic, Seed);
        Colony cMaster = CarpenterColony(gMaster, MasterCarpenter);
        cMaster.AddGoods(Lumber, 10);
        gMaster.EndTurn();

        Game gFree = Game.New(Classic, Seed);
        Colony cFree = CarpenterColony(gFree, Free);
        cFree.AddGoods(Lumber, 10);
        gFree.EndTurn();

        Assert.Equal(10 - 6, cMaster.StoreOf(Lumber)); // 6 consumed
        Assert.Equal(10 - 3, cFree.StoreOf(Lumber));   // 3 consumed
    }

    [Theory]
    [InlineData(Free, 3, 3)]        // base: 3 hammers from 3 lumber
    [InlineData(MasterCarpenter, 6, 6)] // +3 modifier: 6 hammers from 6 lumber (double throughput)
    [InlineData(Indentured, 2, 2)]  // -1 modifier: 2 hammers from 2 lumber — NOT 1 (no float-floor conjuring)
    [InlineData(Petty, 1, 1)]       // -2 modifier: 1 hammer from 1 lumber — NOT 0 (the fractional-ratio trap)
    public void Conversion_ConsumesInputProportionalToModifiedOutput(string worker, int expectedHammers, int expectedLumberUsed)
    {
        Game game = Game.New(Classic, Seed);
        Colony colony = CarpenterColony(game, worker);
        colony.AddGoods(Lumber, 10);
        game.EndTurn();
        // Output asserted as a delta vs a free colonist would over-couple to SoL; here both quantities are 1:1 with the
        // worker's modified output, and input carries no SoL term, so the absolute lumber-used figure is exact.
        Assert.Equal(10 - expectedLumberUsed, colony.StoreOf(Lumber));
        Assert.Equal(expectedHammers + colony.ProductionBonus, colony.StoreOf(Hammers)); // + the (here zero) SoL bonus
    }

    [Fact]
    public void Input_LimitsModifiedOutput_LumberStarvedMasterMatchesAFreeColonist()
    {
        // Only 3 lumber: a master wanting 6 is capped at fraction 0.5 → floor(6×0.5) = 3 hammers (and eats 3 lumber),
        // exactly a free colonist's full output. (Same colony shape ⇒ same SoL bonus ⇒ equality holds.)
        Assert.Equal(HammersAfterOneTurn([Free], lumber: 3), HammersAfterOneTurn([MasterCarpenter], lumber: 3));
    }

    // ---- Sons-of-Liberty bonus folds in per worker BEFORE the index-30 modifier, scaled by the rebel-factor ----

    /// <summary>A fresh colony with one building, set population/liberty (for a precise SoL bonus), workers, fed.</summary>
    private static Colony BuildingColony(Game game, string buildingId, int population, int liberty, params string[] workerTypes)
    {
        Colony colony = game.FoundColony(game.Units.First(u => u.IsOnMap && u.Type.CanFoundColony));
        foreach (Position tile in colony.TileWorkers.Keys.ToList())
        {
            game.UnassignWork(colony, tile);
        }
        if (!colony.HasBuilding(buildingId))
        {
            colony.AddBuilding(buildingId); // direct (bypasses the build gate); the base building is left empty → 0 output
        }
        colony.Population = population;
        colony.Liberty = liberty; // SoL% = floor(liberty·100 / (200·pop)); 100·pop → 50% → +1, 200·pop → 100% → +2
        foreach (string type in workerTypes)
        {
            colony.AssignBuildingWorker(buildingId, type);
        }
        colony.AddGoods(Food, 150); // fed, below the 200 growth bar so population is stable this turn
        return colony;
    }

    [Fact]
    public void MultiplicativeExpert_HasTheSoLBonusFoldedInBeforeItsMultiplier()
    {
        // Master distiller (rum ×2) at SoL +1: FreeCol folds the +1 in BEFORE the multiplier → (3+1)×2 = 8.
        // The old "multiply, then add the bonus flat" gave (3×2)+1 = 7 — the bug this slice's review caught.
        Game game = Game.New(Classic, Seed);
        Colony colony = BuildingColony(game, DistillerHouse, population: 1, liberty: 100, MasterDistiller);
        Assert.Equal(1, colony.ProductionBonus);
        colony.AddGoods(Sugar, 20);
        game.EndTurn();
        Assert.Equal(8, colony.StoreOf(Rum));
    }

    [Fact]
    public void RebelFactorBuilding_DoublesTheSoLBonus()
    {
        // Lumber mill has rebel-factor 2, so SoL +1 contributes floor(1×2) = +2 per worker, folded before the modifier.
        // A free colonist: base 6 + 2 = 8 hammers (no expert modifier, so the lumber mill's competence factor 2 is a
        // no-op for it). A master carpenter: (6+2) + 3×2 = 14 — the lumber mill's competence factor 2 scales the +3
        // expert bonus to +6 (86d3drmzf). (Before competence this was 11; the old flat-add bug gave 7 and 10.)
        Game free = Game.New(Classic, Seed);
        Colony freeColony = BuildingColony(free, LumberMill, population: 1, liberty: 100, Free);
        Assert.Equal(1, freeColony.ProductionBonus);
        freeColony.AddGoods(Lumber, 30);
        free.EndTurn();
        Assert.Equal(8, freeColony.StoreOf(Hammers));

        Game master = Game.New(Classic, Seed);
        Colony masterColony = BuildingColony(master, LumberMill, population: 1, liberty: 100, MasterCarpenter);
        masterColony.AddGoods(Lumber, 30);
        master.EndTurn();
        Assert.Equal(14, masterColony.StoreOf(Hammers));
    }

    // ---- Competence factor: an upgraded building scales a specialist's ADDITIVE bonus (86d3drmzf) ----

    [Fact]
    public void CompetenceFactor_ScalesAnAdditiveExpertBonus_LumberMillDoublesTheMasterCarpentersThree()
    {
        // No SoL (liberty 0, pop 1 → ProductionBonus 0), so the only effect is the lumber mill's competence factor 2.
        // Free colonist: base 6 (no modifier, competence is a no-op). Master carpenter: 6 + 3×2 = 12 — the +3 expert
        // bonus is scaled to +6. So the master out-produces the free colonist by 6, double the +3 he'd add in the
        // base carpenter's house (competence 1).
        Game free = Game.New(Classic, Seed);
        Colony freeColony = BuildingColony(free, LumberMill, population: 1, liberty: 0, Free);
        Assert.Equal(0, freeColony.ProductionBonus);
        freeColony.AddGoods(Lumber, 30);
        free.EndTurn();
        Assert.Equal(6, freeColony.StoreOf(Hammers));

        Game master = Game.New(Classic, Seed);
        Colony masterColony = BuildingColony(master, LumberMill, population: 1, liberty: 0, MasterCarpenter);
        masterColony.AddGoods(Lumber, 30);
        master.EndTurn();
        Assert.Equal(12, masterColony.StoreOf(Hammers));
    }

    [Fact]
    public void CompetenceFactor_DoesNotScaleAMultiplicativeExpert_MasterDistillerInTheDistilleryStaysDoubled()
    {
        // The rum distillery has competence factor 2, but the master distiller's bonus is ×2 rum (multiplicative).
        // FreeCol scales only ADDITIVE modifiers, so the doubling is untouched: base 6 × 2 = 12 (NOT 6 × 2 × 2 = 24).
        // No SoL (liberty 0, pop 1) so the figure is exact.
        Game game = Game.New(Classic, Seed);
        Colony colony = BuildingColony(game, Distillery, population: 1, liberty: 0, MasterDistiller);
        Assert.Equal(0, colony.ProductionBonus);
        colony.AddGoods(Sugar, 30);
        game.EndTurn();
        Assert.Equal(12, colony.StoreOf(Rum));
    }

    [Fact]
    public void CompetenceFactor_BaseBuildingIsOne_MasterCarpenterAddsOnlyThreeInTheCarpentersHouse()
    {
        // The carpenter's house has no competence-factor attribute → defaults to 1, so a master carpenter adds his
        // plain +3 (base 3 → 6), confirming competence > 1 is exclusive to the upgraded buildings.
        Assert.Equal(HammersAfterOneTurn([Free], lumber: 10) + 3, HammersAfterOneTurn([MasterCarpenter], lumber: 10));
    }

    [Fact]
    public void BadGovernment_PenalisesEachWorker_FlooredAtZero()
    {
        // Bad government (11 tories → −1; Col1 caps at −1, no FreeCol −2 "very bad" tier — 86d3kgbtj): a free colonist
        // makes max(0, 3−1) = 2 hammers, a petty criminal max(0, (3−2)−1) = 0. Each worker is floored SEPARATELY.
        // (The old −2-government vehicle that pushed a productive colonist itself negative — distinguishing per-worker
        // from a pooled floor — is gone with the Col1 single-tier cap; the per-worker floor remains, defensive.)
        Game game = Game.New(Classic, Seed);
        Colony colony = BuildingColony(game, Carpenter, population: 11, liberty: 0, Free, Petty);
        Assert.Equal(-1, colony.ProductionBonus);
        colony.AddGoods(Lumber, 30);
        game.EndTurn();
        Assert.Equal(2, colony.StoreOf(Hammers));
    }

    // ---- Non-1:1 factory under short input: FreeCol floors the required input first (+EPSILON), losing no unit ----
    // The rum factory has competence factor 3, so an indentured servant's −1 rum penalty is scaled to −3 (86d3drmzf):
    // an indentured makes 9 − 3 = 6 rum here (was 8 before competence). The flooring-edge intent is preserved by
    // re-picking the sugar amounts: one case where required input == available (full output), one where it exceeds it.

    [Theory]
    [InlineData(new[] { Free, Indentured }, 10, 15, 10)] // total 9+6=15; required floor(6·15/9)=10 == avail → full 15 rum / 10 sugar
    [InlineData(new[] { Indentured, Indentured }, 7, 10, 7)] // total 12; required floor(6·12/9)=8 > 7 → scale 7/8 → 10 rum / 7 sugar
    public void Factory_ShortInput_FloorsRequiredFirst(string[] workers, int sugar, int expectedRum, int expectedSugarUsed)
    {
        Game game = Game.New(Classic, Seed);
        Colony colony = BuildingColony(game, RumFactory, population: workers.Length, liberty: 0, workers);
        colony.AddGoods(Sugar, sugar);
        game.EndTurn();
        Assert.Equal(expectedRum, colony.StoreOf(Rum));
        Assert.Equal(sugar - expectedSugarUsed, colony.StoreOf(Sugar));
    }

    // ---- Experts have connections: a factory expert produces without raw input when the option is on (86d3fpyja) ----
    // FreeCol BuildingProductionCalculator input-scarcity floor, gated behind model.option.expertsHaveConnections
    // (classic default OFF, so classic production is untouched — the byte-identity / soak guard).

    [Fact]
    public void ExpertsHaveConnections_OptionPlumbsThrough_ClassicDefaultOff()
    {
        // The option defaults off in classic (byte-identity guard) and the seam flips it on a FRESH ruleset (never the
        // shared static Classic, which the seam would mutate in place).
        Assert.False(Ruleset.LoadClassic().GameOptions.ExpertsHaveConnections);
        Assert.True(Ruleset.LoadClassic().WithExpertsHaveConnections(true).GameOptions.ExpertsHaveConnections);
        Assert.False(Ruleset.LoadClassic().WithExpertsHaveConnections(false).GameOptions.ExpertsHaveConnections);
    }

    [Fact]
    public void RumFactory_DeclaresExpertsUseConnections_WithAFloorOfFour()
    {
        // The rum factory carries the ability + the experts-with-connections-production="4" attribute (parsed).
        BuildingType rumFactory = Classic.Building(RumFactory);
        Assert.True(rumFactory.ExpertsUseConnections);
        Assert.Equal(4, rumFactory.EffectiveExpertConnectionProduction);
        // A base building without the ability has no floor.
        Assert.False(Classic.Building(Carpenter).ExpertsUseConnections);
        Assert.Equal(0, Classic.Building(Carpenter).EffectiveExpertConnectionProduction);
    }

    [Fact]
    public void MasterDistiller_WithNoSugar_ProducesTheConnectionsFloor_OnlyWhenTheOptionIsOn()
    {
        // A master distiller in a rum factory (sugar 6 → rum 9, distiller ×2 → output ratio 2) with ZERO sugar.
        // OFF (classic default): no input → no rum (the byte-identity guard).
        Game off = Game.New(Classic, Seed);
        Colony offColony = BuildingColony(off, RumFactory, population: 1, liberty: 0, MasterDistiller);
        Assert.Equal(0, offColony.ProductionBonus);
        // (no sugar added)
        off.EndTurn();
        Assert.Equal(0, offColony.StoreOf(Rum));
        Assert.Equal(0, offColony.StoreOf(Sugar)); // nothing consumed either

        // ON (fresh ruleset — never mutate the shared Classic): the connections floor lifts the effective sugar to
        // 4 × 1 expert = 4, so scarcity = 4/12 and the master makes floor(18 × 4/12) = 6 rum with no sugar on hand.
        Ruleset connected = Ruleset.LoadClassic().WithExpertsHaveConnections(true);
        Game on = Game.New(connected, Seed);
        Colony onColony = BuildingColony(on, RumFactory, population: 1, liberty: 0, MasterDistiller);
        Assert.Equal(0, onColony.ProductionBonus);
        on.EndTurn();
        Assert.Equal(6, onColony.StoreOf(Rum));
        Assert.Equal(0, onColony.StoreOf(Sugar)); // no sugar existed to consume; the floor draws none down (clamped ≥ 0)
    }

    [Fact]
    public void ConnectionsFloor_OnlyRaisesOutput_NeverLowersAWellStockedFactory()
    {
        // With plenty of sugar the floor is inert (available already ≥ required), so the option-on output equals the
        // option-off output — the floor "only ever raises available, never lowers it".
        Game off = Game.New(Classic, Seed);
        Colony offColony = BuildingColony(off, RumFactory, population: 1, liberty: 0, MasterDistiller);
        offColony.AddGoods(Sugar, 40);
        off.EndTurn();
        int offRum = offColony.StoreOf(Rum);

        Ruleset connected = Ruleset.LoadClassic().WithExpertsHaveConnections(true);
        Game on = Game.New(connected, Seed);
        Colony onColony = BuildingColony(on, RumFactory, population: 1, liberty: 0, MasterDistiller);
        onColony.AddGoods(Sugar, 40);
        on.EndTurn();

        Assert.Equal(offRum, onColony.StoreOf(Rum));
        Assert.True(offRum > 0);
    }

    [Fact]
    public void ConnectionsFloor_IsInertForANonExpertWorker_EvenWithTheOptionOn()
    {
        // A FREE colonist is not the building's expert type, so the expert count is 0 → floor 0 → no relief: a rum
        // factory with a free colonist and no sugar makes no rum even with the option on (only EXPERTS have connections).
        Ruleset connected = Ruleset.LoadClassic().WithExpertsHaveConnections(true);
        Game on = Game.New(connected, Seed);
        Colony onColony = BuildingColony(on, RumFactory, population: 1, liberty: 0, Free);
        on.EndTurn();
        Assert.Equal(0, onColony.StoreOf(Rum));
    }

    [Fact]
    public void ConnectionsFloor_ChargesTheFreeColConsumption_NotClampedToStock()
    {
        // FreeCol BuildingProductionCalculator.java:203-214 charges consumption = floor(amount × minimumRatio + ε)
        // with NO clamp to the stock on hand (86d3hjz87). A master distiller in a rum factory (sugar 6 → rum 9,
        // distiller ×2 → output ratio 2, required floor(6·2) = 12) holding only 2 sugar: the connections floor lifts
        // the effective sugar to 4 → scarcity 4/12 = 1/3 → rum floor(18/3 + ε) = 6, and the sugar CHARGED is
        // floor(6 × 2 × 1/3 + ε) = 4 — not the old min(4, stock 2). The warehouse itself still bottoms out at 0
        // (both application sites floor there — Colony.AddGoods and the summary's working copy), so the end-of-turn
        // stores match FreeCol's; only the reported consumption figure moves.
        Ruleset connected = Ruleset.LoadClassic().WithExpertsHaveConnections(true);
        Game game = Game.New(connected, Seed);
        Colony colony = BuildingColony(game, RumFactory, population: 1, liberty: 0, MasterDistiller);
        Assert.Equal(0, colony.ProductionBonus);
        colony.AddGoods(Sugar, 2);

        ColonyGoodFlow sugarFlow = game.ColonyProductionSummary(colony)[Sugar];
        Assert.Equal(4, sugarFlow.Consumed); // the FreeCol figure — the deleted clamp under-reported 2 (the stock)
        Assert.Equal(-4, sugarFlow.Net);     // nothing in this colony produces sugar (non-sugar centre tile, Seed 3)

        game.EndTurn();
        Assert.Equal(6, colony.StoreOf(Rum));   // production is untouched by the charge (scarcity already used the floor)
        Assert.Equal(0, colony.StoreOf(Sugar)); // 2 − 4 floors at 0 — the store never goes negative (FreeCol-identical)
    }

    [Fact]
    public void BuildingExpertType_ScansAllOutputs_ForTheFirstExpert()
    {
        // FreeCol BuildingProductionCalculator.getExpertUnitType scans ALL outputs for the first non-null expert
        // (find(map(outputs, getExpertForProducing), isNotNull())) — not just Outputs[0] (86d3hjz87). Trade goods
        // have no producing expert in classic, so a synthetic two-output entry [tradeGoods, rum] must find the
        // master distiller via the SECOND output (the old first-output-only read returned null and silently
        // dropped the connections floor for such an entry).
        Game game = Game.New(Classic, Seed);
        var twoOutput = new ProductionEntry(false,
            [new GoodsOutput("model.goods.tradeGoods", 1), new GoodsOutput(Rum, 1)]);
        Assert.Equal(MasterDistiller, game.BuildingExpertType(twoOutput));

        // No output with an expert → null (no floor).
        Assert.Null(game.BuildingExpertType(
            new ProductionEntry(false, [new GoodsOutput("model.goods.tradeGoods", 1)])));

        // A classic single-output entry is unchanged: the rum factory's own entry still finds the master distiller.
        Assert.Equal(MasterDistiller, game.BuildingExpertType(
            Classic.Building(RumFactory).Productions.First(p => !p.Unattended)));
    }
}
