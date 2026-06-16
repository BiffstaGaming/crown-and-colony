using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Colonies;

/// <summary>
/// Warehouse capacity + overflow spoilage (<c>86d3c9nnp</c>, FreeCol <c>Settlement.getWarehouseCapacity</c> +
/// <c>csNewTurnWarnings</c> waste): a colony holds only 100 of each storable good (200 with a warehouse, 300 with
/// the expansion); production past the cap is wasted each turn. Bells/crosses/hammers (non-storable) and food are
/// exempt.
/// </summary>
public class WarehouseTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const string Cotton = "model.goods.cotton";
    private const string Food = "model.goods.food";
    private const string Hammers = "model.goods.hammers";
    private const string Depot = "model.building.depot";
    private const string Warehouse = "model.building.warehouse";
    private const string Expansion = "model.building.warehouseExpansion";

    /// <summary>A pop-1 colony on a 1×1 plains map (centre yield: grain 3 + cotton 2), with a depot (free base).</summary>
    private static Game PlainsColony() => new SaveGame
    {
        Turn = 1,
        RandomStateValue = 1,
        RandomIncrement = 1,
        MapWidth = 1,
        MapHeight = 1,
        Terrain = ["model.tile.plains"],
        Units = [],
        Explored = [0],
        Colonies = [new SavedColony(1, "Testville", 0, 0, 1)], // no buildings → re-derives the free base set (depot)
    }.Restore(Classic);

    [Fact]
    public void WarehouseStorage_ParsedCumulativelyUpTheExtendsChain()
    {
        Assert.Equal(100, Classic.Building(Depot).WarehouseStorage);
        Assert.Equal(200, Classic.Building(Warehouse).WarehouseStorage);     // extends depot: 100 + 100
        Assert.Equal(300, Classic.Building(Expansion).WarehouseStorage);     // extends warehouse: 100 + 100 + 100
        Assert.Equal(0, Classic.Building("model.building.townHall").WarehouseStorage);
    }

    [Fact]
    public void WarehouseCapacity_FollowsTheBuiltTier()
    {
        Game game = PlainsColony();
        Colony colony = game.Colonies[0];
        Assert.True(colony.HasBuilding(Depot));
        Assert.Equal(100, game.WarehouseCapacity(colony));

        colony.ReplaceBuilding(Depot, Warehouse);
        Assert.Equal(200, game.WarehouseCapacity(colony));
    }

    [Fact]
    public void StorableGoods_OverTheCap_AreWasted()
    {
        Game game = PlainsColony();
        Colony colony = game.Colonies[0];
        colony.AddGoods(Cotton, 150); // over the depot's 100

        game.EndTurn(); // production (+2 cotton) then spill

        Assert.Equal(100, colony.StoreOf(Cotton)); // clamped to capacity, the rest discarded
    }

    [Fact]
    public void Food_IsExemptFromTheWarehouseCap()
    {
        Game game = PlainsColony();
        Colony colony = game.Colonies[0];
        colony.AddGoods(Food, 150); // below the 200 growth threshold, above the 100 cap

        game.EndTurn(); // +3 food, eat 2, no growth (151 < 200)

        Assert.Equal(151, colony.Food);          // food is never warehouse-capped here
        Assert.True(colony.Food > 100);
    }

    [Fact]
    public void NonStorableGoods_AreExemptFromTheWarehouseCap()
    {
        Game game = PlainsColony();
        Colony colony = game.Colonies[0];
        Assert.False(Classic.Goods(Hammers).IsStorable); // hammers accrue toward a build, never warehoused to a cap
        colony.AddGoods(Hammers, 150);

        game.EndTurn(); // no carpenter staffed → no hammer change; no spill (non-storable)

        Assert.Equal(150, colony.StoreOf(Hammers));
    }

    [Fact]
    public void BuildingAWarehouse_RaisesTheCap()
    {
        Game game = PlainsColony();
        Colony colony = game.Colonies[0];
        colony.ReplaceBuilding(Depot, Warehouse); // capacity 200
        colony.AddGoods(Cotton, 250);

        game.EndTurn();

        Assert.Equal(200, colony.StoreOf(Cotton)); // capped at the warehouse tier, not the depot's 100
    }
}
