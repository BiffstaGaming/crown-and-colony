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
    public void LongRun_ColonyGrowsOnce_ThenHoldsAtEquilibrium()
    {
        // L2 scenario: net +1 food/turn at pop 1 → growth on the 200th tick;
        // at pop 2 the square's 3 grain can't feed 4 appetite, food floors at 0,
        // and (starvation deliberately deferred) population holds at 2.
        Game game = PlainsColony();
        Colony colony = game.Colonies[0];

        for (int i = 0; i < 199; i++)
        {
            game.EndTurn();
        }
        Assert.Equal(1, colony.Population);
        Assert.Equal(199, colony.Food);

        game.EndTurn();
        Assert.Equal(2, colony.Population);
        Assert.Equal(0, colony.Food);

        for (int i = 0; i < 50; i++)
        {
            game.EndTurn();
        }
        Assert.Equal(2, colony.Population);
        Assert.Equal(0, colony.Food);
        Assert.Equal(2 * 250, colony.StoreOf(Cotton)); // 250 ticks, cotton untouched by appetite
    }
}
