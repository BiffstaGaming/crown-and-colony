using System.Collections.Generic;
using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
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
    private const string Artillery = "model.unit.artillery";
    private const string WagonTrain = "model.unit.wagonTrain";
    private const string Armory = "model.building.armory";

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
