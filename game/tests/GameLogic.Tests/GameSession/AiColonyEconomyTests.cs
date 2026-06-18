using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
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
}
