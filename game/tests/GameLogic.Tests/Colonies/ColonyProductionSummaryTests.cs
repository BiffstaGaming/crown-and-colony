using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Colonies;

/// <summary>
/// The colony production-overview oracle (<see cref="Game.ColonyProductionSummary"/>, ClickUp 86d3f674t): a pure
/// per-good produced / consumed / net breakdown for the turn, folding tile/centre yields, building outputs AND building
/// inputs, and food consumption — the read behind the colony screen's production summary. Cross-checked against the
/// good the colony actually banks over a real <see cref="Game.EndTurn"/> (the net must match the warehouse delta for
/// the steady-state goods, modulo construction/breeding which the summary deliberately omits).
/// </summary>
public class ColonyProductionSummaryTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 0xC0FFEEUL;
    private const string Food = "model.goods.food";
    private const string Bells = "model.goods.bells";
    private const string Lumber = "model.goods.lumber";
    private const string Hammers = "model.goods.hammers";

    private static Colony FoundFreshColony(Game game) =>
        game.FoundColony(game.Units.First(u => u.IsOnMap && u.Type.CanFoundColony));

    [Fact]
    public void Summary_ReportsFoodEatenAsConsumption()
    {
        Game game = Game.New(Classic, Seed);
        Colony colony = FoundFreshColony(game);

        var summary = game.ColonyProductionSummary(colony);

        // A pop-1 colony eats 2 food (FoodPerColonist) — reported as consumption, not a negative produced.
        Assert.True(summary.ContainsKey(Food));
        Assert.Equal(colony.Population * Classic.ColonyConstants.FoodPerColonist, summary[Food].Consumed);
        Assert.Equal(summary[Food].Produced - summary[Food].Consumed, summary[Food].Net);
    }

    [Fact]
    public void Summary_IncludesUnattendedTownHallBells()
    {
        Game game = Game.New(Classic, Seed);
        Colony colony = FoundFreshColony(game);

        var summary = game.ColonyProductionSummary(colony);

        // The town hall is a free starting building producing bells unattended every turn — a building output the
        // tiles-only ColonyNetProduction omits but the full summary must show.
        Assert.True(summary.ContainsKey(Bells), "bells should appear (town hall unattended output)");
        Assert.True(summary[Bells].Produced > 0);
        Assert.Equal(0, summary[Bells].Consumed);
    }

    [Fact]
    public void Summary_FoldsBuildingInputsAndOutputs_LumberToHammers()
    {
        Game game = Game.New(Classic, Seed);
        Colony colony = FoundFreshColony(game);

        // Free the founder (a fresh pop-1 colony is fully assigned to a tile), stock lumber, and staff the carpenter's
        // house so it converts lumber → hammers.
        game.UnassignWork(colony, colony.TileWorkers.Keys.First());
        colony.AddGoods(Lumber, 50);
        game.AssignBuildingWork(colony, "model.building.carpenterHouse");
        Assert.True(colony.BuildingWorkers.GetValueOrDefault("model.building.carpenterHouse") > 0);

        var summary = game.ColonyProductionSummary(colony);

        // The carpenter consumes lumber and produces hammers — both must appear in the breakdown.
        Assert.True(summary.ContainsKey(Hammers) && summary[Hammers].Produced > 0, "carpenter makes hammers");
        Assert.True(summary.ContainsKey(Lumber) && summary[Lumber].Consumed > 0, "carpenter consumes lumber");
    }

    [Fact]
    public void Summary_NetMatchesTheWarehouseDelta_OverARealTurn_ForASteadyStateGood()
    {
        // The summary is a faithful dry-run of the production step: a good with no construction/breeding involvement
        // (lumber, here, with no lumber in the build queue) banks exactly its reported net over a real EndTurn.
        Game game = Game.New(Classic, Seed);
        Colony colony = FoundFreshColony(game);
        Position lumberTile = colony.Position.Neighbours()
            .First(n => game.Map.InBounds(n) && game.TileWorkOptions(n).Any(o => o.GoodsId == Lumber));
        Position founder = colony.TileWorkers.Keys.First();
        game.UnassignWork(colony, founder);
        game.AssignWork(colony, lumberTile, Lumber);
        game.SetBuild(colony, null); // no construction consuming materials, so lumber is pure steady-state

        int predictedNet = game.ColonyProductionSummary(colony)[Lumber].Net;
        int before = colony.StoreOf(Lumber);
        game.EndTurn();
        int actualDelta = colony.StoreOf(Lumber) - before;

        Assert.True(predictedNet > 0);
        Assert.Equal(predictedNet, actualDelta);
    }

    [Fact]
    public void Summary_NetIsProducedMinusConsumed_ForEveryGood()
    {
        Game game = Game.New(Classic, Seed);
        Colony colony = FoundFreshColony(game);
        game.UnassignWork(colony, colony.TileWorkers.Keys.First());
        colony.AddGoods(Lumber, 50);
        game.AssignBuildingWork(colony, "model.building.carpenterHouse");

        foreach ((string _, ColonyGoodFlow flow) in game.ColonyProductionSummary(colony))
        {
            Assert.True(flow.Produced >= 0);
            Assert.True(flow.Consumed >= 0);
            Assert.Equal(flow.Produced - flow.Consumed, flow.Net);
        }
    }
}
