using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Colonies;

public class ColonyEconomyTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    private const string Food = Colony.FoodId;
    private const string Cotton = "model.goods.cotton";

    /// <summary>A pop-1 colony on a 1×1 plains map (plains centre yield: grain 3 + cotton 2).</summary>
    private static Game PlainsColony()
    {
        var save = new SaveGame
        {
            Turn = 1,
            RandomStateValue = 1,
            RandomIncrement = 1,
            MapWidth = 1,
            MapHeight = 1,
            Terrain = ["model.tile.plains"],
            Units = [],
            Explored = [0],
            Colonies = [new SavedColony(1, "Testville", 0, 0, 1)],
        };
        return save.Restore(Classic);
    }

    [Fact]
    public void ColonyNetProduction_FoldsCentreYieldLessFoodEaten()
    {
        // The shared oracle behind the colony screen's production bar and the empire colony report.
        Game game = PlainsColony();
        Colony colony = game.Colonies[0];

        System.Collections.Generic.IReadOnlyDictionary<string, int> net = game.ColonyNetProduction(colony);

        Assert.Equal(1, net[Food]);   // plains centre grain 3 -> food, minus 1 colonist eating 2
        Assert.Equal(2, net[Cotton]); // plains centre cotton 2 (unattended), no tile workers on a 1x1 map
    }

    [Fact]
    public void EndTurn_ColonySquareProduces_AndColonistsEat()
    {
        Game game = PlainsColony();
        Colony colony = game.Colonies[0];

        game.EndTurn();

        // Plains centre: +3 grain (stored as food) +2 cotton; 1 colonist eats 2.
        Assert.Equal(1, colony.StoreOf(Food));
        Assert.Equal(2, colony.StoreOf(Cotton));

        game.EndTurn();
        Assert.Equal(2, colony.StoreOf(Food));
        Assert.Equal(4, colony.StoreOf(Cotton));
    }

    [Fact]
    public void FoodSurplusOf200_RaisesANewColonist()
    {
        Game game = PlainsColony();
        Colony colony = game.Colonies[0];
        colony.AddGoods(Food, 199);

        game.EndTurn(); // +3 → 202, eat 2 → 200 → growth consumes 200

        Assert.Equal(2, colony.Population);
        Assert.Equal(0, colony.Food);
    }

    [Fact]
    public void ConsumeFood_ReportsShortfall_AndFloorsAtZero()
    {
        Game game = PlainsColony();
        Colony colony = game.Colonies[0];
        colony.AddGoods(Food, 4);

        Assert.Equal(0, colony.ConsumeFood(4));
        Assert.Equal(0, colony.Food);
        Assert.Equal(6, colony.ConsumeFood(6)); // empty store: full shortfall
        Assert.Equal(0, colony.Food);
    }

    [Fact]
    public void GrainAndFish_StoreAsFood_PerSpec()
    {
        // The spec's stored-as model: raw food goods normalize into one
        // warehouse entry (also applied to legacy saves on load).
        Assert.Equal(Food, Ruleset.LoadClassic().StorageIdOf("model.goods.grain"));
        Assert.Equal(Food, Ruleset.LoadClassic().StorageIdOf("model.goods.fish"));
        Assert.Equal("model.goods.cotton", Ruleset.LoadClassic().StorageIdOf("model.goods.cotton"));

        GoodsType rum = Classic.Goods("model.goods.rum");
        Assert.Equal("model.goods.sugar", rum.MadeFrom);
        Assert.False(rum.IsFarmed);
        Assert.True(Classic.Goods(Food).IsFood);
    }

    [Fact]
    public void SaveRoundTrip_PreservesStores_AndPreV4LoadsEmpty()
    {
        Game game = PlainsColony();
        game.EndTurn();
        game.EndTurn();

        Game loaded = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);
        Assert.Equal(2, loaded.Colonies[0].StoreOf(Food));
        Assert.Equal(4, loaded.Colonies[0].StoreOf(Cotton));

        // Legacy stores holding raw grain normalize to food on load.
        SaveGame legacy = SaveGame.From(game) with
        {
            Colonies = [new SavedColony(1, "Testville", 0, 0, 1,
                new Dictionary<string, int> { ["model.goods.grain"] = 7 })],
        };
        Assert.Equal(7, SaveGame.FromJson(legacy.ToJson()).Restore(Classic).Colonies[0].Food);

        // v3 save: colonies without stores.
        SaveGame v3 = SaveGame.From(game) with
        {
            Version = 3,
            Colonies = [new SavedColony(1, "Testville", 0, 0, 1)],
        };
        Game oldLoad = SaveGame.FromJson(v3.ToJson()).Restore(Classic);
        Assert.Empty(oldLoad.Colonies[0].Stores);
    }

    [Fact]
    public void FoodShortfall_StarvesAColonist_ButNeverTheLast()
    {
        // Pop 3 on a bare plains square: produces 3 food, appetite 6 → shortfall
        // every turn → starve down to pop 1 (3 produced ≥ 2 appetite: stable).
        var save = new SaveGame
        {
            Turn = 1,
            RandomStateValue = 1,
            RandomIncrement = 1,
            MapWidth = 1,
            MapHeight = 1,
            Terrain = ["model.tile.plains"],
            Units = [],
            Explored = [0],
            Colonies = [new SavedColony(1, "Starving", 0, 0, 3)],
        };
        Game game = save.Restore(Classic);
        Colony colony = game.Colonies[0];

        game.EndTurn(); // +3, eat 6 → shortfall → pop 2
        Assert.Equal(2, colony.Population);
        game.EndTurn(); // +3, eat 4 → shortfall → pop 1
        Assert.Equal(1, colony.Population);

        for (int i = 0; i < 10; i++)
        {
            game.EndTurn();
        }
        Assert.Equal(1, colony.Population); // the last colonist never starves
    }

    [Fact]
    public void Starvation_TrimsExcessAssignments()
    {
        // Pop 2, both assigned (1 field, 1 workshop). Starvation to pop 1 must
        // pull the building worker first, keeping assignments ≤ population.
        var game = Game.New(Classic, seed: 424242);
        game.FoundColony(game.Units[0]);
        Colony colony = game.Colonies[0];
        colony.Population = 2;
        if (colony.TileWorkers.Count == 0)
        {
            game.AssignWork(colony, colony.Position.Neighbours()
                .First(n => game.CheckAssignWork(colony, n, "model.goods.grain").Allowed), "model.goods.grain");
        }
        game.AssignBuildingWork(colony, "model.building.carpenterHouse");
        Assert.Equal(0, colony.IdleColonists);

        // Drain the larder and starve one colonist.
        colony.AddGoods(Colony.FoodId, -colony.Food);
        // Remove field food production so the shortfall is guaranteed.
        foreach (var tile in colony.TileWorkers.Keys.ToList())
        {
            game.UnassignWork(colony, tile);
        }
        game.AssignBuildingWork(colony, "model.building.carpenterHouse"); // both in the workshop now

        game.EndTurn();

        Assert.Equal(1, colony.Population);
        Assert.True(colony.IdleColonists >= 0, "assignments must never exceed population");
        Assert.True(colony.BuildingWorkers.GetValueOrDefault("model.building.carpenterHouse") <= 1);
    }

    [Fact]
    public void LongRun_BareColonyCycles_GrowThenStarve()
    {
        // L2 scenario: net +1 food/turn at pop 1 → growth on the 200th tick;
        // a bare square's 3 food can't feed pop 2's appetite of 4, so the
        // newborn starves next turn — a boom-bust cycle around pop 1. (Real
        // colonies escape it by farming surrounding tiles.)
        Game game = PlainsColony();
        Colony colony = game.Colonies[0];

        for (int i = 0; i < 199; i++)
        {
            game.EndTurn();
        }
        Assert.Equal(1, colony.Population);
        Assert.Equal(199, colony.Food);

        game.EndTurn();
        Assert.Equal(2, colony.Population); // born…

        game.EndTurn();
        Assert.Equal(1, colony.Population); // …and starved on the bare square

        for (int i = 0; i < 49; i++)
        {
            game.EndTurn();
        }
        Assert.Equal(1, colony.Population);
        // Cotton (2/turn, untouched by appetite) piles up to the depot's warehouse capacity (100) and the
        // overflow then spills each turn — FreeCol's warehouse waste — so it stabilises at the cap, not 2×250.
        Assert.Equal(100, colony.StoreOf(Cotton));
    }
}
