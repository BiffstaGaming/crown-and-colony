using System.IO;
using System.Linq;
using System.Xml.Linq;
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

    /// <summary>
    /// The classic ruleset with <c>model.tile.plains</c>'s <b>unattended</b> (colony-centre) production stripped of
    /// its grain output, so a colony founded on plains produces <b>no food</b> at its centre. Everything else is
    /// identical. This is the only way to reach FreeCol's "last colonist starves → colony disposed" rule on an
    /// otherwise-classic map (the real classic centre tile always yields ≥ 2 food, a lone colonist's appetite).
    /// </summary>
    private static readonly Ruleset PlainsNoCentreFood = LoadClassicWithFoodlessPlainsCentre();

    private static Ruleset LoadClassicWithFoodlessPlainsCentre()
    {
        using Stream spec = typeof(Ruleset).Assembly.GetManifestResourceStream(GameVariants.ClassicSpecResource)!;
        XDocument doc = XDocument.Load(spec);
        XElement plains = doc.Descendants("tile-type")
            .Single(t => (string?)t.Attribute("id") == "model.tile.plains");
        foreach (XElement output in plains.Elements("production")
                     .Where(p => (bool?)p.Attribute("unattended") == true)
                     .Elements("output")
                     .Where(o => (string?)o.Attribute("goods-type") == "model.goods.grain")
                     .ToList())
        {
            output.Remove();
        }
        var buffer = new MemoryStream();
        doc.Save(buffer);
        buffer.Position = 0;
        return Ruleset.Load(buffer);
    }

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
    public void SurvivableFamine_AtAHumanColony_RecordsAWarningNotice()
    {
        // Pop 3 on a bare plains square (centre 3 food, appetite 6) starves one colonist but the colony survives →
        // a famine WARNING notice is recorded (distinct from the colony-destroyed notice).
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
            Colonies = [new SavedColony(1, "Hungry", 0, 0, 3)],
        };
        Game game = save.Restore(Classic);

        game.EndTurn(); // +3, eat 6 → shortfall → pop 2 (survived)

        ColonyFamineNotice notice = Assert.Single(game.ColonyFamineNotices);
        Assert.Equal("Hungry", notice.ColonyName);
        Assert.Equal(2, notice.PopulationAfter);
        Assert.Empty(game.ColonyStarvedNotices); // the colony was not destroyed
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

    /// <summary>A pop-1 human colony on the foodless-centre plains, larder empty, with a soldier garrisoned on its tile.</summary>
    private static Game StarvingColony()
    {
        var save = new SaveGame
        {
            Turn = 1,
            RandomStateValue = 1,
            RandomIncrement = 1,
            MapWidth = 1,
            MapHeight = 1,
            Terrain = ["model.tile.plains"],
            // A garrison unit standing on the colony tile (0,0) — it must survive the colony's destruction.
            Units = [new SavedUnit(1, "model.unit.freeColonist", 0, 0, 1)],
            Explored = [0],
            Colonies = [new SavedColony(1, "Doomed", 0, 0, 1)],
        };
        return save.Restore(PlainsNoCentreFood);
    }

    [Fact]
    public void LastColonistStarves_ColonyIsDestroyed_OwnerNotified_HistoryRecorded()
    {
        // FreeCol ServerColony.csNewTurn: a colony whose food production + stored carryover can't feed its LAST
        // colonist is disposed (csDisposeSettlement) — its tile clears and the colony is removed.
        Game game = StarvingColony();
        Colony colony = game.Colonies[0];
        colony.AddGoods(Food, -colony.Food); // empty the larder; the centre produces no food this turn
        Position where = colony.Position;

        game.EndTurn();

        Assert.Empty(game.Colonies);                                  // the colony is gone (tile cleared = absent)
        Assert.Null(game.ColonyAt(where));                            // nothing stands where it was
        ColonyStarvedNotice notice = Assert.Single(game.ColonyStarvedNotices); // the human owner is notified
        Assert.Equal("Doomed", notice.ColonyName);
        Assert.Equal(where, notice.Position);
        HistoryEvent destroyed = Assert.Single(game.History,          // a destruction history event is recorded (feeds E2 score)
            h => h.Kind == HistoryEventKind.ColonyDestroyed);
        Assert.Equal(0, destroyed.Score);                             // no score itself today
        // The garrison unit on the colony tile survives on the now-empty land (FreeCol leaves tile units in place).
        Assert.Single(game.Units, u => u.Position == where && u.IsOnMap);
    }

    [Fact]
    public void DestroyedColony_IsAbsentAfterSaveLoadRoundTrip()
    {
        // No persisted ghost: a destroyed colony is simply not in the save (no new save state).
        Game game = StarvingColony();
        game.Colonies[0].AddGoods(Food, -game.Colonies[0].Food);
        game.EndTurn();
        Assert.Empty(game.Colonies);

        Game reloaded = SaveGame.From(game).ToJson() is var json
            ? SaveGame.FromJson(json).Restore(PlainsNoCentreFood)
            : throw new System.InvalidOperationException();
        Assert.Empty(reloaded.Colonies);
    }

    [Fact]
    public void StarvationToDeath_OnlyTakesTheLastColonist_NotASizeTwoColony()
    {
        // A pop-2 colony in the same foodless scenario loses ONE colonist (down to 1), it is not destroyed —
        // disposal is reserved for the LAST colonist (the pop>1 branch still fires first).
        Game game = StarvingColony();
        Colony colony = game.Colonies[0];
        colony.Population = 2;
        colony.AddGoods(Food, -colony.Food);

        game.EndTurn();

        Colony survivor = Assert.Single(game.Colonies);
        Assert.Equal(1, survivor.Population);          // one starved, the colony lives
        Assert.Empty(game.ColonyStarvedNotices);       // not destroyed yet
        Assert.DoesNotContain(game.History, h => h.Kind == HistoryEventKind.ColonyDestroyed);

        // …and the very next turn the last colonist can't be fed either → now the colony is destroyed.
        game.EndTurn();
        Assert.Empty(game.Colonies);
        Assert.Single(game.ColonyStarvedNotices);
        Assert.Single(game.History, h => h.Kind == HistoryEventKind.ColonyDestroyed);
    }

    [Fact]
    public void ClassicPlainsColony_NeverStarvesToDeath_NoNotice()
    {
        // The default game keeps its colonies: a classic plains centre yields 3 food ≥ a lone colonist's appetite,
        // so a pop-1 colony survives indefinitely with no starvation notice and no destruction history event.
        Game game = PlainsColony();
        for (int i = 0; i < 50; i++)
        {
            game.EndTurn();
        }
        Assert.Single(game.Colonies);
        Assert.Empty(game.ColonyStarvedNotices);
        Assert.DoesNotContain(game.History, h => h.Kind == HistoryEventKind.ColonyDestroyed);
    }
}
