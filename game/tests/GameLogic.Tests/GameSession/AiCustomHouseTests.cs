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
/// AI custom houses (<c>86d3fpz8d</c>): a foreign colonial power whose colony has built a custom house (it elected
/// Peter Stuyvesant through the same election the human uses) now <b>flags its tradeable surplus for export</b>, so the
/// existing foreign-power custom-house auto-sell path (<see cref="Game"/>'s <c>AutoSellExports</c>, already run for every
/// colonial player in the colony turn) actually fires: each turn the custom house ships the colony's surplus above the
/// AI's reserve line to the power's OWN European market. Before this, the AI never flagged a good for export, so the
/// PerGood-default auto-sell never sold anything for a foreign power even with a custom house built.
///
/// <para>Determinism (ADR-009): flagging is an RNG-free state write and the sale draws no RNG (the price move is
/// deterministic), all on the power's own market/stream — so the human's stream 0 is untouched and same-seed games stay
/// byte-identical. The export flags round-trip through the existing save v28/v67 tokens (no new save version).</para>
/// </summary>
public class AiCustomHouseTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    private const string CustomHouse = "model.building.customHouse";
    private const string Stuyvesant = "model.foundingFather.peterStuyvesant";
    private const string Sugar = "model.goods.sugar";

    /// <summary>Human (stream 0) + one foreign colonial power (id 1) that has elected Stuyvesant (so its colonies may raise a custom house).</summary>
    private static List<SavedPlayer> HumanPlusStuyvesantPower() =>
    [
        new SavedPlayer(0, NationId: null, IsHuman: true, PlayerType: (int)PlayerType.Colonial),
        new SavedPlayer(1, "model.nation.dutch", IsHuman: false, PlayerType: (int)PlayerType.Colonial,
            Congress: [Stuyvesant]),
    ];

    /// <summary>
    /// A 3×3 all-plains map with one foreign colony (id 1, owned by the Stuyvesant power) that already holds a
    /// <paramref name="buildings"/> list and the given <paramref name="stores"/>. Returns the game, that colony, and the
    /// power. Mirrors <c>ForeignDefenceArmingTests.ForeignFixture</c>.
    /// </summary>
    private static (Game Game, Colony Colony, Player Power) ForeignCustomHouseColony(
        IReadOnlyList<string> buildings, IReadOnlyDictionary<string, int> stores)
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
            Players = HumanPlusStuyvesantPower(),
            Colonies = [new SavedColony(1, "Nieuw Amsterdam", 1, 1, 1, Stores: stores, Buildings: buildings, OwnerId: 1)],
        };
        Game game = save.Restore(Classic);
        Player power = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);
        Colony colony = game.Colonies[0];
        foreach (Position p in game.Map.AllPositions())
        {
            power.ExploredSet.Add(p);
        }
        return (game, colony, power);
    }

    [Fact]
    public void AiColonyWithACustomHouse_FlagsItsSurplus_AndTheCustomHouseAutoSellsIt()
    {
        // A foreign colony with a custom house and a big sugar stockpile. On the first foreign turn the AI flags sugar
        // for export (keep-level 0 — its usual "sell it all" reserve); on the next turn the custom house auto-sells the
        // surplus to the power's own market, so the colony drains and the power's treasury rises.
        (Game game, Colony colony, Player power) = ForeignCustomHouseColony(
            buildings: [CustomHouse],
            stores: new Dictionary<string, int> { [Sugar] = 300 });

        Assert.True(game.ColonyHasCustomHouse(colony));
        int goldBefore = power.Gold;

        game.EndTurn(); // colony turn (nothing flagged yet → no sale) then foreign economy → sugar flagged for export
        Assert.True(colony.ExportOf(Sugar).Exported, "the AI did not flag its surplus good for custom-house export");

        int stockAfterFlagTurn = colony.StoreOf(Sugar);
        game.EndTurn(); // colony turn: the custom house now auto-sells the flagged surplus

        Assert.True(colony.StoreOf(Sugar) < stockAfterFlagTurn,
            "the custom house did not ship the flagged surplus out of the colony");
        Assert.True(power.Gold > goldBefore, "the auto-sale credited no gold to the power");
        Assert.True(power.Market.SaveDeltas().Count > 0, "the power's own market never moved — the sale did not go through it");
    }

    [Fact]
    public void AiColonyWithoutACustomHouse_FlagsNothing_AndSellsThroughTheOrdinaryEconomyLoop()
    {
        // The same colony WITHOUT a custom house: the AI must not flag exports (there is no custom house to sell them),
        // and its ordinary economy sell loop still cashes the surplus to its own market — the pre-existing behaviour.
        (Game game, Colony colony, Player power) = ForeignCustomHouseColony(
            buildings: [],
            stores: new Dictionary<string, int> { [Sugar] = 300 });

        Assert.False(game.ColonyHasCustomHouse(colony));
        int goldBefore = power.Gold;

        game.EndTurn();

        Assert.False(colony.ExportOf(Sugar).Exported, "a colony with no custom house must not flag exports");
        Assert.True(power.Gold > goldBefore, "the ordinary economy loop did not sell the surplus");
    }

    [Fact]
    public void AiCustomHouse_NeverDrivesStoresOrGoldNegative_OverManyTurns()
    {
        // Invariant guard mirroring the soak: run a custom-house foreign colony for many turns and assert no store and
        // no treasury ever goes negative, and the turn always advances (no softlock). The custom house only sells
        // surplus above the reserve, so it can never overdraw a store or the market.
        (Game game, Colony colony, Player power) = ForeignCustomHouseColony(
            buildings: [CustomHouse],
            stores: new Dictionary<string, int> { [Sugar] = 500 });

        for (int turn = 0; turn < 40; turn++)
        {
            int before = game.Turn;
            game.EndTurn();
            Assert.Equal(before + 1, game.Turn);
            Assert.All(game.Colonies, c => Assert.All(c.Stores.Values, v => Assert.True(v >= 0)));
            Assert.All(game.Players, p => Assert.True(p.Gold >= 0));
        }

        Assert.True(colony.Population >= 1, "the colony starved out");
    }

    [Fact]
    public void AiCustomHouseExports_RoundTripThroughSave_WithNoVersionBump()
    {
        // The AI-set export flags ride the existing v28/v67 Exports tokens — a game with a foreign custom house that has
        // flagged its surplus round-trips byte-identically, and the save version is unchanged.
        (Game game, _, _) = ForeignCustomHouseColony(
            buildings: [CustomHouse],
            stores: new Dictionary<string, int> { [Sugar] = 300 });

        game.EndTurn(); // flags sugar for export on the foreign power's colony

        string json = SaveGame.From(game).ToJson();
        Assert.Equal(json, SaveGame.From(SaveGame.FromJson(json).Restore(Classic)).ToJson());
        Assert.Equal(69, SaveGame.CurrentVersion); // no new save version was needed
    }

    [Fact]
    public void AiCustomHouseEconomy_IsDeterministic_TwinGamesStayByteIdentical()
    {
        // Twin same-seed games, each with a foreign custom-house colony, run for several turns with the new
        // export-flagging + auto-sell active: they must end byte-identical (ADR-009). Flagging and the sale draw no RNG
        // and act on the power's own market, so determinism holds.
        Game a = TwinGame();
        Game b = TwinGame();

        for (int turn = 0; turn < 20; turn++)
        {
            a.EndTurn();
            b.EndTurn();
        }

        Assert.Equal(SaveGame.From(a).ToJson(), SaveGame.From(b).ToJson());
        // The new path was genuinely active: the foreign colony flagged a good for export.
        Assert.Contains(a.Colonies, c => c.Exports.Values.Any(e => e.Exported));

        static Game TwinGame()
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
                Players = HumanPlusStuyvesantPower(),
                Colonies =
                [
                    new SavedColony(1, "Nieuw Amsterdam", 1, 1, 1,
                        Stores: new Dictionary<string, int> { [Sugar] = 300 }, Buildings: [CustomHouse], OwnerId: 1),
                ],
            };
            Game game = save.Restore(Classic);
            Player power = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);
            foreach (Position p in game.Map.AllPositions())
            {
                power.ExploredSet.Add(p);
            }
            return game;
        }
    }
}
