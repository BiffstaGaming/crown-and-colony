using System.Collections.Generic;
using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// AI colony economy (<c>86d3c9vmr</c>, full-fidelity <c>ColonyPlan</c> port, built up in increments). Increment 2:
/// the <c>ColonyNetFood</c> production-query the worker planner uses to balance cash crops against starvation.
/// </summary>
public class AiColonyEconomyTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 0xC0FFEEUL;
    private const string Grain = "model.goods.grain";
    private const string Hammers = "model.goods.hammers";
    private const string Tools = "model.goods.tools";
    private const string Lumber = "model.goods.lumber";
    private const string Sugar = "model.goods.sugar";
    private const string Rum = "model.goods.rum";
    private const string Artillery = "model.unit.artillery";
    private const string WagonTrain = "model.unit.wagonTrain";
    private const string Armory = "model.building.armory";
    private const string DistillerHouse = "model.building.distillerHouse";
    private const string CarpenterHouse = "model.building.carpenterHouse";

    /// <summary>A fresh game with a founded human colony whose tiles have all been freed (the founder left idle).</summary>
    private static (Game Game, Colony Colony) IdleColony()
    {
        Game game = Game.New(Classic, Seed);
        Colony colony = game.FoundColony(game.Units.First(u => u.IsOnMap && u.Type.CanFoundColony));
        foreach (Position t in colony.TileWorkers.Keys.ToList())
        {
            game.UnassignWork(colony, t);
        }
        return (game, colony);
    }

    /// <summary>A human colony on a 3×3 plains block (no sugar/lumber source anywhere) — so a building's input is present
    /// only when a test deposits it, isolating the building-worker funding check from incidental centre/tile production.</summary>
    private static (Game Game, Colony Colony) PlainsColony()
    {
        var save = new SaveGame
        {
            Turn = 1,
            RandomStateValue = 1,
            RandomIncrement = 1,
            MapWidth = 3,
            MapHeight = 3,
            Terrain = [.. Enumerable.Repeat("model.tile.plains", 9)],
            Units = [],
            Explored = [.. Enumerable.Range(0, 9)],
            Players = [new SavedPlayer(0, NationId: null, IsHuman: true, PlayerType: (int)PlayerType.Colonial)],
            Colonies = [new SavedColony(1, "Plainsville", 1, 1, 0)],
        };
        Game game = save.Restore(Classic);
        Colony colony = game.Colonies[0];
        foreach (Position t in colony.TileWorkers.Keys.ToList())
        {
            game.UnassignWork(colony, t);
        }
        return (game, colony);
    }

    [Fact]
    public void ColonyNetFood_IsCentrePlusFoodTiles_MinusConsumption()
    {
        (Game game, Colony colony) = IdleColony();
        int baseline = game.ColonyNetFood(game.HumanPlayer, colony); // centre food − population·2, no tiles worked

        // Put the idle colonist on the best grain tile; net food must rise by exactly that tile's (floored) yield.
        Position foodTile = colony.Position.Neighbours()
            .First(n => game.Map.InBounds(n) && game.TileWorkOptions(n).Any(o => o.GoodsId == Grain));
        game.AssignWork(colony, foodTile, Grain);

        int tileFood = System.Math.Max(0, game.TileYield(foodTile, Grain) + colony.ProductionBonus);
        Assert.Equal(baseline + tileFood, game.ColonyNetFood(game.HumanPlayer, colony));
    }

    [Fact]
    public void ColonyNetFood_FallsAsThePopulationGrows()
    {
        (Game game, Colony colony) = IdleColony();
        int oneColonist = game.ColonyNetFood(game.HumanPlayer, colony);
        colony.Population += 1; // an extra mouth eats FoodPerColonist more
        Assert.Equal(oneColonist - Colony.FoodPerColonist, game.ColonyNetFood(game.HumanPlayer, colony));
    }

    [Fact]
    public void ColonyNetFood_IsAPureRead()
    {
        (Game game, Colony colony) = IdleColony();
        var before = game.RandomState;
        game.ColonyNetFood(game.HumanPlayer, colony);
        game.ColonyNetFood(game.HumanPlayer, colony);
        Assert.Equal(before, game.RandomState); // no RNG drawn
    }

    // ── Worker planner (increment 3) ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PlanColonyTileWork_StaffsIdleColonistsOntoTiles()
    {
        (Game game, Colony colony) = IdleColony();
        colony.Population = 4; // four colonists, all idle after IdleColony cleared the tiles

        game.PlanColonyTileWork(game.HumanPlayer, colony);

        Assert.True(colony.TileWorkers.Count > 0);                 // it staffed tiles (was all idle)
        Assert.True(colony.TileWorkers.Count <= colony.Population); // never more than the population
    }

    [Fact]
    public void PlanColonyTileWork_WorksFoodFirst_SoTheColonyDoesNotStarve()
    {
        (Game game, Colony colony) = IdleColony();
        colony.Population = 4; // 4 mouths eat 8 food; the centre alone can't feed them → food must be worked

        game.PlanColonyTileWork(game.HumanPlayer, colony);

        Assert.Contains(colony.TileWorkers, w => Classic.StorageIdOf(w.Value) == Colony.FoodId); // at least one food tile
    }

    [Fact]
    public void PlanColonyTileWork_PreservesAStableTilesWorker_AndItsExperience()
    {
        (Game game, Colony colony) = IdleColony();
        colony.Population = 4;
        game.PlanColonyTileWork(game.HumanPlayer, colony);

        // Pick a planned tile, give its worker some on-the-job experience, then re-plan the (unchanged) colony.
        (Position tile, string good) = colony.TileWorkers.First();
        colony.SetTileWorkerExperience(tile, 50);
        game.PlanColonyTileWork(game.HumanPlayer, colony);

        Assert.True(colony.TileWorkers.ContainsKey(tile) && colony.TileWorkers[tile] == good); // same tile + good (deterministic plan)
        Assert.Equal(50, colony.TileWorkerExperienceAt(tile)); // experience survived (diff-applied, not churned)
    }

    [Fact]
    public void PlanColonyTileWork_DrawsNoRandomness()
    {
        (Game game, Colony colony) = IdleColony();
        colony.Population = 4;
        var before = game.RandomState;
        game.PlanColonyTileWork(game.HumanPlayer, colony);
        Assert.Equal(before, game.RandomState); // pure ordinal/yield ranking (ADR-009)
    }

    // ── Building-worker planner (increment 5) ────────────────────────────────────────────────────────────────────

    [Fact]
    public void PlanColonyBuildingWork_StaffsARefinery_WhenItsRawIsAvailable()
    {
        // A plains colony (centre makes no sugar) with sugar in store has an idle colonist sent to the distiller house
        // (sugar → rum) — building work turns a raw into a higher-value refined good. Plains isolates the test: the only
        // sugar source is the deposit, so the distiller is the highest-value fundable building.
        (Game game, Colony colony) = PlainsColony();
        colony.Population = 1;            // exactly one idle colonist after the tiles were freed
        colony.AddGoods(Sugar, 50);       // the distiller's input is in store → the entry is fundable

        game.PlanColonyBuildingWork(game.HumanPlayer, colony);

        Assert.Equal(1, colony.BuildingWorkers.GetValueOrDefault(DistillerHouse)); // the colonist staffed the distiller
        Assert.Equal(0, colony.IdleColonists);                                     // no longer idle
    }

    [Fact]
    public void PlanColonyBuildingWork_FundsARefineryFromCentreProduction()
    {
        // The colony's own unattended centre output funds a building: a savannah colony makes sugar at its centre, so the
        // distiller is fundable with no deposit at all (the centre feeds it). Demonstrates the available-goods set folds
        // in centre + tile production, not just the warehouse.
        (Game game, Colony colony) = IdleColony(); // savannah → centre makes grain + sugar
        colony.Population = 1;

        game.PlanColonyBuildingWork(game.HumanPlayer, colony);

        Assert.Equal(1, colony.BuildingWorkers.GetValueOrDefault(DistillerHouse)); // fed by the centre's sugar
    }

    [Fact]
    public void PlanColonyBuildingWork_DoesNotStaffARefinery_WithoutItsRaw()
    {
        // A plains colony makes no sugar (centre = grain + cotton, no sugar tile) → the distiller has nothing to convert,
        // so it is NOT staffed (a worker there would produce nothing).
        (Game game, Colony colony) = PlainsColony();
        colony.Population = 1;

        game.PlanColonyBuildingWork(game.HumanPlayer, colony);

        Assert.Equal(0, colony.BuildingWorkers.GetValueOrDefault(DistillerHouse)); // unfed refinery left empty
    }

    [Fact]
    public void PlanColonyBuildingWork_StaffsTheCarpenter_WhenABuildIsQueued()
    {
        // With a build queued and lumber in store, the carpenter's house (lumber → hammers) is valued high (FreeCol's
        // HAMMERS construction priority) so it out-ranks the centre-cotton weaver and gets the colonist — construction
        // actually progresses instead of stalling for hammers.
        (Game game, Colony colony) = PlainsColony();
        colony.Population = 1;
        colony.AddGoods(Lumber, 50);
        game.SetBuild(colony, game.Buildables(colony).First().Id); // an active build raises the hammers value

        game.PlanColonyBuildingWork(game.HumanPlayer, colony);

        Assert.Equal(1, colony.BuildingWorkers.GetValueOrDefault(CarpenterHouse));
    }

    [Fact]
    public void PlanColonyBuildingWork_FavoursARefineryOverTheCarpenter_WhenNoBuildIsQueued()
    {
        // With no build queued, construction materials are valued modestly, so a high-value refinery (centre cotton →
        // weaver) is staffed ahead of the carpenter — the colony grows its cash economy when it isn't building.
        (Game game, Colony colony) = PlainsColony();
        colony.Population = 1;
        colony.AddGoods(Lumber, 50);
        game.SetBuild(colony, null);

        game.PlanColonyBuildingWork(game.HumanPlayer, colony);

        Assert.Equal(0, colony.BuildingWorkers.GetValueOrDefault(CarpenterHouse)); // refinery preferred over hammers
    }

    [Fact]
    public void PlanColonyBuildingWork_NeverExceedsIdleColonists()
    {
        // Plenty of raws, but only the idle colonists may be staffed — never more than the population minus tile workers.
        (Game game, Colony colony) = IdleColony();
        colony.Population = 2;
        colony.AddGoods(Sugar, 100);
        colony.AddGoods(Lumber, 100);

        game.PlanColonyBuildingWork(game.HumanPlayer, colony);

        Assert.True(colony.BuildingWorkers.Values.Sum() <= 2);
        Assert.True(colony.IdleColonists >= 0); // never over-assigned
    }

    [Fact]
    public void PlanColonyBuildingWork_LeavesFoodTileWorkersUntouched()
    {
        // The building fill only ever consumes colonists left idle after the (food-first) tile plan — it must never pull
        // a food worker, so the colony's survival margin is preserved (a soak invariant).
        (Game game, Colony colony) = IdleColony();
        colony.Population = 4;
        colony.AddGoods(Sugar, 100);
        game.PlanColonyTileWork(game.HumanPlayer, colony);   // food-first tile plan
        int tileWorkersBefore = colony.TileWorkers.Count;
        int netFoodBefore = game.ColonyNetFood(game.HumanPlayer, colony);

        game.PlanColonyBuildingWork(game.HumanPlayer, colony);

        Assert.Equal(tileWorkersBefore, colony.TileWorkers.Count);                 // no tile worker pulled
        Assert.Equal(netFoodBefore, game.ColonyNetFood(game.HumanPlayer, colony)); // food margin untouched
    }

    [Fact]
    public void PlanColonyBuildingWork_DrawsNoRandomness()
    {
        (Game game, Colony colony) = IdleColony();
        colony.Population = 3;
        colony.AddGoods(Sugar, 100);
        colony.AddGoods(Lumber, 100);
        var before = game.RandomState;
        game.PlanColonyBuildingWork(game.HumanPlayer, colony);
        Assert.Equal(before, game.RandomState); // pure value/ordinal ranking (ADR-009)
    }

    [Fact]
    public void PlanColonyBuildingWork_IsDeterministic()
    {
        // Two identical colonies make the identical building assignment.
        static (Game Game, Colony Colony) Setup()
        {
            (Game g, Colony c) = IdleColony();
            c.Population = 2;
            c.AddGoods(Sugar, 100);
            c.AddGoods(Lumber, 100);
            return (g, c);
        }

        (Game a, Colony ca) = Setup();
        (Game b, Colony cb) = Setup();
        a.PlanColonyBuildingWork(a.HumanPlayer, ca);
        b.PlanColonyBuildingWork(b.HumanPlayer, cb);

        Assert.Equal(
            ca.BuildingWorkers.OrderBy(kv => kv.Key, System.StringComparer.Ordinal),
            cb.BuildingWorkers.OrderBy(kv => kv.Key, System.StringComparer.Ordinal));
    }

    [Fact]
    public void ForeignPowerEconomy_StaffsAForeignColonysBuildings()
    {
        // Integration: building-worker planning must run for a foreign power through RunForeignPowerEconomy (guards
        // against the EndTurn wiring being removed). A colony has at most ~9 tile slots; a large population guarantees
        // colonists remain idle after the (food-first) tile plan, so the building fill must place at least one of them.
        Game game = Game.New(Classic, seed: 7);
        Player power = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);
        for (int i = 0; i < 30; i++)
        {
            game.EndTurn(); // by ~turn 30 the power founds a colony (cf. ForeignPowerEconomyTests)
        }
        Colony colony = game.Colonies.First(c => c.OwnerId == power.PlayerId);
        colony.Population = 15;       // far more colonists than the ≤9 tile slots → idle ones remain for buildings
        colony.AddGoods(Sugar, 400);  // a fundable refinery input (rum) so a building is worth staffing

        game.EndTurn(); // the power's economy re-plans tiles then buildings

        Assert.True(colony.BuildingWorkers.Values.Sum() > 0); // the building fill ran end-to-end (centre-only before)
    }

    // ── Build-queue planner (increment 4) ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildQueue_QueuesABuild_WhenTheColonyIsIdle()
    {
        (Game game, Colony colony) = IdleColony();
        colony.Population = 4;          // enough population to unlock buildables
        game.SetBuild(colony, null);   // ensure nothing is queued
        Assert.True(game.Buildables(colony).Any(), "no buildables for a size-4 colony — test premise broke");

        game.RunForeignColonyBuildPlan(colony);

        Assert.NotNull(colony.CurrentBuild); // it queued the highest-value building
    }

    [Fact]
    public void BuildQueue_DoesNotDisturbAnInProgressBuild()
    {
        (Game game, Colony colony) = IdleColony();
        colony.Population = 4;
        string chosen = game.Buildables(colony).First().Id;
        game.SetBuild(colony, chosen);

        game.RunForeignColonyBuildPlan(colony);

        Assert.Equal(chosen, colony.CurrentBuild); // left alone
    }

    [Fact]
    public void BuildQueue_ValuesAnImmigrationBuilding_AboveZero()
    {
        // A crosses-producing building (church/chapel) must be buildable — it has weight 0.05 (the IMMIGRATION class),
        // not 0 (which would make the AI never build it). Regression for the review's medium finding.
        (Game game, Colony colony) = IdleColony();
        BuildingType crossBuilding = Classic.BuildingTypes.First(b =>
            b.Productions.SelectMany(p => p.Outputs).Any(o => Classic.StorageIdOf(o.GoodsId) == "model.goods.crosses"));
        Assert.True(game.BuildingBuildWeight(colony, crossBuilding) > 0);
    }

    [Fact]
    public void BuildQueue_ValuesStorageAboveBreeding()
    {
        // Sanity on the class-weight ranking: a warehouse (STORAGE 0.85) outranks a breeding building (0.1).
        (Game game, Colony colony) = IdleColony();
        BuildingType warehouse = Classic.BuildingTypes.First(b => b.WarehouseStorage > 0);
        BuildingType breeder = Classic.BuildingTypes.First(b => b.BreedingDivisor > 0);
        Assert.True(game.BuildingBuildWeight(colony, warehouse) > game.BuildingBuildWeight(colony, breeder));
    }

    [Fact]
    public void BuildQueue_DrawsNoRandomness()
    {
        (Game game, Colony colony) = IdleColony();
        colony.Population = 4;
        game.SetBuild(colony, null);
        var before = game.RandomState;
        game.RunForeignColonyBuildPlan(colony);
        Assert.Equal(before, game.RandomState); // pure weight/difficulty ranking (ADR-009)
    }

    [Fact]
    public void ForeignPowerEconomy_StaffsAForeignColonysTiles()
    {
        // Integration: the planner must actually run for a foreign power through RunForeignPowerEconomy (not just
        // when called directly on the human) — guards against the EndTurn wiring being removed/guarded out.
        Game game = Game.New(Classic, seed: 7);
        Player power = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);
        for (int i = 0; i < 30; i++)
        {
            game.EndTurn(); // by ~turn 30 the power founds a colony (cf. ForeignPowerEconomyTests)
        }

        Colony colony = game.Colonies.First(c => c.OwnerId == power.PlayerId);
        Assert.True(colony.TileWorkers.Count > 0); // the planner staffed its tiles (centre-only/idle before 86d3c9vmr)
    }

    // ── Best-worker building fill (increment 6 — FreeCol getBestWorker) ───────────────────────────────────────────────

    private const string MasterDistiller = "model.unit.masterDistiller";

    [Fact]
    public void PlanColonyBuildingWork_AssignsTheMatchingExpert_WhenOneIsIdle()
    {
        // FreeCol getBestWorker: the worker that most improves the building's output wins the slot. A plains colony whose
        // sole idle colonist is a master distiller, with sugar in store, must seat the MASTER DISTILLER (×2 rum) in the
        // distiller house — the expert is the best worker for it.
        (Game game, Colony colony) = PlainsColony();
        colony.Population = 1;                 // one colonist total
        colony.AddIdleColonist(MasterDistiller); // …and it is the master distiller
        colony.AddGoods(Sugar, 100);

        game.PlanColonyBuildingWork(game.HumanPlayer, colony);

        Assert.Equal(1, colony.BuildingWorkers.GetValueOrDefault(DistillerHouse));
        Assert.Contains(MasterDistiller, colony.BuildingWorkerTypes.GetValueOrDefault(DistillerHouse, [])); // the expert took the slot
    }

    [Fact]
    public void PlanColonyBuildingWork_RoutesTheExpertToItsMatchingBuilding_NotAnUnrelatedOne()
    {
        // With a free colonist + a master distiller both idle, and BOTH a sugar-fed distiller house and a lumber-fed
        // carpenter fundable, the best-worker fill routes the master distiller to the distiller house (its ×2 rum match)
        // and never into the carpenter (where it adds no more than a free colonist) — FreeCol's "the expert wins its
        // building" placement, the free colonist taking the other slot.
        (Game game, Colony colony) = PlainsColony();
        colony.Population = 2;                  // one free colonist + one master distiller
        colony.AddIdleColonist(MasterDistiller);
        colony.AddGoods(Sugar, 100);           // distiller house fundable (rum)
        colony.AddGoods(Lumber, 100);          // carpenter fundable (hammers)
        game.SetBuild(colony, game.Buildables(colony).First().Id); // a build raises the carpenter's value so it competes

        game.PlanColonyBuildingWork(game.HumanPlayer, colony);

        Assert.Contains(MasterDistiller, colony.BuildingWorkerTypes.GetValueOrDefault(DistillerHouse, [])); // expert → its match
        Assert.DoesNotContain(MasterDistiller, colony.BuildingWorkerTypes.GetValueOrDefault(CarpenterHouse, [])); // never the carpenter
    }

    [Fact]
    public void PlanColonyBuildingWork_BestWorker_DrawsNoRandomness()
    {
        (Game game, Colony colony) = PlainsColony();
        colony.Population = 2;
        colony.AddIdleColonist(MasterDistiller);
        colony.AddGoods(Sugar, 100);
        var before = game.RandomState;
        game.PlanColonyBuildingWork(game.HumanPlayer, colony);
        Assert.Equal(before, game.RandomState); // pure value/ordinal ranking (ADR-009)
    }

    // ── Europe train/buy (increment 6 — FreeCol trainAIUnitInEurope + artillery/ship buy) ─────────────────────────────

    private const string Caravel = "model.unit.caravel";

    [Fact]
    public void CheckTrain_RejectsANonSpecialist_AndGatesOnGold()
    {
        Game game = ForeignColony(out Colony _, out int ownerId);
        Player power = game.Players.First(p => p.PlayerId == ownerId);

        Assert.False(game.CheckTrain(power, Caravel).Allowed);          // a ship is purchased, not trained
        Assert.False(game.CheckTrain(power, "model.unit.freeColonist").Allowed); // a plain colonist is recruited, not trained

        power.Gold = 0;
        Assert.False(game.CheckTrain(power, MasterDistiller).Allowed);  // can't afford it
        power.Gold = 100_000;
        Assert.True(game.CheckTrain(power, MasterDistiller).Allowed);   // affordable specialist → allowed
    }

    [Fact]
    public void TrainUnit_DocksAnOwnedSpecialist_AndSpendsItsGold()
    {
        Game game = ForeignColony(out Colony _, out int ownerId);
        Player power = game.Players.First(p => p.PlayerId == ownerId);
        power.Gold = 100_000;
        int before = power.Gold;

        Unit trained = game.TrainUnit(power, MasterDistiller);

        Assert.Equal(MasterDistiller, trained.Type.Id);
        Assert.Equal(ownerId, trained.OwnerId);                         // belongs to the power, not the human
        Assert.Equal(UnitLocation.InEurope, trained.Location);         // docked, ready to ship
        Assert.True(power.Gold < before);                              // its own gold was spent
        Assert.Empty(game.UnitsInEurope);                             // the human gained nothing
    }

    [Fact]
    public void EuropeSpend_TrainsAWantedSpecialist_WhenFlush()
    {
        // A flush power trains a specialist whose expertise matches a good its plains colony produces every turn (grain
        // + cotton at the centre → wanted grain/cotton/cloth). It picks the cheapest such expert, deterministically, and
        // docks it in its own Europe (a trained-in-Europe unit with skill > 0). Asserts the spend trained a *wanted*
        // expert without pinning the exact type (the cheapest-wins ordering is covered by the unit tests on the seam).
        Game game = ForeignColony(out Colony _, out int ownerId);
        Player power = game.Players.First(p => p.PlayerId == ownerId);
        power.Gold = 100_000;

        game.EndTurn(); // the power's economy runs, including the Europe spend

        // The specialist was trained in the power's Europe; the same turn the transport AI (86d3c9vq9) may already have
        // boarded it onto the ship the spend bought and set sail — so find it wherever it now is (docked, aboard or
        // sailing), not strictly InEurope.
        var trainedIds = game.UnitTypesTrainedInEurope().Select(t => t.Id).ToHashSet();
        Unit? trained = game.Units.FirstOrDefault(u => u.OwnerId == ownerId && trainedIds.Contains(u.Type.Id));
        Assert.NotNull(trained);                                  // it trained a specialist
        Assert.True(trained!.Type.Skill > 0, "the trained unit is not a skilled specialist"); // not a plain recruit/ship
    }

    [Fact]
    public void EuropeSpend_BuysAShip_WhenFlushAndOwningNoCarrier()
    {
        // A flush power with no naval carrier buys the cheapest ship (the transport need) — its own gold, its own unit.
        Game game = ForeignColony(out Colony _, out int ownerId);
        Player power = game.Players.First(p => p.PlayerId == ownerId);
        power.Gold = 100_000;
        Assert.DoesNotContain(game.Units, u => u.OwnerId == ownerId && u.Type.IsNaval); // premise: no carrier yet

        game.EndTurn();

        Assert.Contains(game.Units, u => u.OwnerId == ownerId && u.Type.IsNaval && u.Type.IsCarrier);
    }

    [Fact]
    public void EuropeSpend_StaysWithinTheReserve_WhenPoor()
    {
        // A poor power (below the spend floor) buys nothing in Europe — it keeps its reserve for recruiting/building.
        Game game = ForeignColony(out Colony _, out int ownerId);
        Player power = game.Players.First(p => p.PlayerId == ownerId);
        power.Gold = 100; // below AiEuropeSpendFloor
        int unitsBefore = game.Units.Count(u => u.OwnerId == ownerId);

        game.EndTurn();

        Assert.Equal(unitsBefore, game.Units.Count(u => u.OwnerId == ownerId)); // no Europe purchase/train
    }

    [Fact]
    public void EuropeSpend_DrawsNothingFromTheHumansStream0()
    {
        // The decisive ADR-009 guard: a flush rival spending hard in Europe must leave the human's stream 0 byte-identical.
        Game baseline = Game.New(Classic, seed: 8675309);
        Game perturbed = Game.New(Classic, seed: 8675309);
        for (int turn = 0; turn < 25; turn++)
        {
            foreach (Player rival in perturbed.Players.Where(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial))
            {
                rival.Gold += 5000; // fund the rivals so they train/buy in Europe far more than the baseline
            }
            baseline.EndTurn();
            perturbed.EndTurn();
        }

        Assert.NotEqual(SaveGame.From(baseline).ToJson(), SaveGame.From(perturbed).ToJson()); // the rivals genuinely diverged
        Assert.Equal(baseline.RandomState, perturbed.RandomState);                            // human stream 0 untouched
        Assert.Equal(baseline.HumanPlayer.Gold, perturbed.HumanPlayer.Gold);
    }

    // ── Build-queue planner — buildable UNITS (increment 4b) ─────────────────────────────────────────────────────────

    [Fact]
    public void BuildQueue_UnderDefendedColony_QueuesArtillery()
    {
        // A foreign colony with an armory + materials, no military unit on its tile → it builds artillery to defend itself.
        Game game = ForeignColony(out Colony colony, out int _);
        colony.AddBuilding(Armory);
        colony.AddGoods(Hammers, 192);
        colony.AddGoods(Tools, 40);
        Assert.Contains(game.BuildableUnits(colony), u => u.Id == Artillery); // premise: artillery is buildable now

        game.RunForeignColonyBuildPlan(colony);

        Assert.Equal(Artillery, colony.CurrentBuild); // defence takes precedence over any building
    }

    [Fact]
    public void BuildQueue_DefendedColony_DoesNotQueueArtillery()
    {
        // A military land unit (artillery) of the owner is already standing on the colony tile → not under-defended,
        // so the artillery trigger does NOT fire even though artillery is buildable (armory + materials present).
        Game game = ForeignColony(out Colony colony, out int ownerId);
        colony.Population = 4;
        colony.AddBuilding(Armory);
        colony.AddGoods(Hammers, 192);
        colony.AddGoods(Tools, 40);
        Assert.Contains(game.BuildableUnits(colony), u => u.Id == Artillery); // premise: artillery IS buildable
        game.SpawnUnit(Classic.Unit(Artillery), colony.Position, ownerId);    // …but a defender already stands on the tile

        game.RunForeignColonyBuildPlan(colony);

        Assert.NotEqual(Artillery, colony.CurrentBuild); // defence is satisfied → the artillery trigger is skipped
    }

    [Fact]
    public void BuildQueue_NoUnitTriggers_FallsThroughToABuilding()
    {
        // A coastal (not landlocked), defended colony with no armory: neither the artillery nor the wagon trigger
        // applies, so the existing building plan runs and picks a building (the increment leaves it intact).
        Game game = CoastalForeignColony(out Colony colony, out int ownerId);
        colony.Population = 4; // enough population to unlock a building for the fallback to pick
        game.SpawnUnit(Classic.Unit(Artillery), colony.Position, ownerId); // defended → no artillery
        Assert.True(game.Buildables(colony).Any(), "no buildables — test premise broke");

        game.RunForeignColonyBuildPlan(colony);

        Assert.NotNull(colony.CurrentBuild);                                                     // it queued a build…
        Assert.NotNull(Classic.BuildingTypes.FirstOrDefault(b => b.Id == colony.CurrentBuild));  // …and it is a BUILDING
    }

    [Fact]
    public void BuildQueue_LandlockedColonyWithNoWagon_QueuesAWagonTrain()
    {
        // A landlocked colony (no water neighbour) that owns no wagon train → it builds one for overland transport.
        // It IS defended (a soldier on the tile) so the artillery trigger does not pre-empt the wagon trigger.
        Game game = ForeignColony(out Colony colony, out int ownerId);
        game.SpawnUnit(Classic.Unit(Artillery), colony.Position, ownerId); // defended → skip the artillery branch
        colony.AddGoods(Hammers, 40);
        Assert.DoesNotContain(game.BuildableUnits(colony), u => u.Id == Artillery); // no armory → artillery not buildable anyway
        Assert.Contains(game.BuildableUnits(colony), u => u.Id == WagonTrain); // premise: the wagon is buildable

        game.RunForeignColonyBuildPlan(colony);

        Assert.Equal(WagonTrain, colony.CurrentBuild);
    }

    [Fact]
    public void BuildQueue_OwnerAlreadyHasAWagon_DoesNotQueueASecondOne()
    {
        // A two-colony owner that already owns a wagon train does not queue another (FreeCol builds the wagon for
        // transport, not in bulk) — it falls through to a building instead.
        Game game = TwoForeignColonies(out Colony first, out int ownerId, out Position spareLand);
        game.SpawnUnit(Classic.Unit(Artillery), first.Position, ownerId); // defended → skip artillery
        game.SpawnUnit(Classic.Unit(WagonTrain), spareLand, ownerId);     // the owner already has a wagon
        first.Population = 4; // enough to unlock a building so the fallback has something to pick

        game.RunForeignColonyBuildPlan(first);

        Assert.NotEqual(WagonTrain, first.CurrentBuild); // not a second wagon
    }

    [Fact]
    public void BuildQueue_UnitTriggers_AreDeterministic()
    {
        // Two identical setups must make the identical pick, with no RNG drawn (ADR-009).
        static (Game Game, Colony Colony) Setup()
        {
            Game g = ForeignColony(out Colony c, out _);
            c.AddBuilding(Armory);
            c.AddGoods(Hammers, 192);
            c.AddGoods(Tools, 40);
            return (g, c);
        }

        (Game a, Colony ca) = Setup();
        (Game b, Colony cb) = Setup();
        var beforeA = a.RandomState;
        a.RunForeignColonyBuildPlan(ca);
        b.RunForeignColonyBuildPlan(cb);

        Assert.Equal(ca.CurrentBuild, cb.CurrentBuild); // same pick from the same setup
        Assert.Equal(Artillery, ca.CurrentBuild);
        Assert.Equal(beforeA, a.RandomState);           // the planner drew no randomness
    }

    // ---- Fixtures (foreign-owned colonies on hand-built maps, mirroring BuildUnitTests) ----

    /// <summary>Human (stream 0) + one foreign colonial power (id 1, Dutch) — the player roster every fixture restores with.</summary>
    private static List<SavedPlayer> HumanPlusForeign() =>
    [
        new SavedPlayer(0, NationId: null, IsHuman: true, PlayerType: (int)PlayerType.Colonial),
        new SavedPlayer(1, "model.nation.dutch", IsHuman: false, PlayerType: (int)PlayerType.Colonial),
    ];

    /// <summary>A landlocked 1×1 plains colony handed to the foreign colonial power, idle build queue.</summary>
    private static Game ForeignColony(out Colony colony, out int ownerId) =>
        ForeignColonyFrom(
            new SaveGame
            {
                Turn = 1,
                RandomStateValue = 1,
                RandomIncrement = 1,
                MapWidth = 1,
                MapHeight = 1,
                Terrain = ["model.tile.plains"],
                Units = [],
                Explored = [0],
                Players = HumanPlusForeign(),
                Colonies = [new SavedColony(1, "Forge", 0, 0, 1)],
            },
            out colony, out ownerId);

    /// <summary>A coastal foreign colony: plains at (0,0), ocean at (1,0) — it has a port, so it is not landlocked.</summary>
    private static Game CoastalForeignColony(out Colony colony, out int ownerId) =>
        ForeignColonyFrom(
            new SaveGame
            {
                Turn = 1,
                RandomStateValue = 1,
                RandomIncrement = 1,
                MapWidth = 2,
                MapHeight = 1,
                Terrain = ["model.tile.plains", "model.tile.ocean"],
                Units = [],
                Explored = [0, 1],
                Players = HumanPlusForeign(),
                Colonies = [new SavedColony(1, "Harbor", 0, 0, 1)],
            },
            out colony, out ownerId);

    /// <summary>Two landlocked foreign colonies (cols 0 and 4 of a 5×1 plains strip) plus a spare land tile to spawn a wagon on.</summary>
    private static Game TwoForeignColonies(out Colony first, out int ownerId, out Position spareLand)
    {
        Game game = ForeignColonyFrom(
            new SaveGame
            {
                Turn = 1,
                RandomStateValue = 1,
                RandomIncrement = 1,
                MapWidth = 5,
                MapHeight = 1,
                Terrain = [.. Enumerable.Repeat("model.tile.plains", 5)],
                Units = [],
                Explored = [0, 1, 2, 3, 4],
                Players = HumanPlusForeign(),
                Colonies =
                [
                    new SavedColony(1, "Alpha", 0, 0, 1),
                    new SavedColony(2, "Beta", 4, 0, 1),
                ],
            },
            out first, out ownerId);
        foreach (Colony c in game.Colonies)
        {
            c.OwnerId = ownerId; // both colonies belong to the same foreign power
        }
        spareLand = new Position(2, 0);
        return game;
    }

    /// <summary>Restores <paramref name="save"/>, hands the first colony to the foreign colonial power, returns it.</summary>
    private static Game ForeignColonyFrom(SaveGame save, out Colony colony, out int ownerId)
    {
        Game game = save.Restore(Classic);
        ownerId = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial).PlayerId;
        colony = game.Colonies[0];
        colony.OwnerId = ownerId;
        return game;
    }
}
