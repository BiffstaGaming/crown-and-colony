using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Randomness;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Trade;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// The home-nation Monarch (independence arc item 1, <c>86d3c9qvr</c>): the weighted action chooser, the validity
/// oracle, and the per-turn tick. The tick uses an ephemeral monarch generator so it draws nothing from the human's
/// stream 0 — existing seeded games stay byte-identical (ADR-009).
/// </summary>
public class MonarchTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 0xC0FFEEUL;

    /// <summary>A fresh game with the human's lone starting colonist turned into a colony, so the King has settlements.</summary>
    private static Game FoundedGame(ulong seed = Seed)
    {
        Game game = Game.New(Classic, seed);
        game.FoundColony(game.Units.First(u => u.IsOnMap && u.Type.CanFoundColony));
        return game;
    }

    private static (Game Game, CrownAndColony.GameLogic.Colonies.Colony Colony) FoundedColony(ulong seed = Seed)
    {
        Game game = Game.New(Classic, seed);
        CrownAndColony.GameLogic.Colonies.Colony colony =
            game.FoundColony(game.Units.First(u => u.IsOnMap && u.Type.CanFoundColony));
        return (game, colony);
    }

    /// <summary>Dequeues scripted ints from <c>Next</c> (the bound is ignored) — for exact-amount monarch rolls.</summary>
    private sealed class ScriptedRandom(params int[] values) : IGameRandom
    {
        private readonly Queue<int> _values = new(values);
        public int Next(int maxExclusive) => _values.Dequeue();
        public int Next(int minInclusive, int maxExclusive) => _values.Dequeue();
        public double NextDouble() => 0;
        public RandomState SaveState() => new(0, 0);
    }

    // ── Weighted pick helper ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void WeightedRandom_PicksProportionally_AndIsDeterministic()
    {
        var choices = new (int, string)[] { (1, "a"), (3, "b") };
        var rng = new Pcg32Random(42);
        var counts = new Dictionary<string, int> { ["a"] = 0, ["b"] = 0 };
        for (int i = 0; i < 4000; i++)
        {
            counts[RandomChoice.WeightedRandom(rng, choices)]++;
        }

        // ~1:3 split; b should land roughly three times as often as a.
        Assert.InRange(counts["b"] / (double)counts["a"], 2.4, 3.6);

        // Determinism: the same seed replays the same picks.
        var r1 = new Pcg32Random(7);
        var r2 = new Pcg32Random(7);
        Assert.Equal(
            Enumerable.Range(0, 50).Select(_ => RandomChoice.WeightedRandom(r1, choices)),
            Enumerable.Range(0, 50).Select(_ => RandomChoice.WeightedRandom(r2, choices)));
    }

    [Fact]
    public void WeightedRandom_NoPositiveWeights_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            RandomChoice.WeightedRandom(new Pcg32Random(1), new (int, int)[] { (0, 1) }));

    // ── Chooser gate + weights ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void GetMonarchActionChoices_IsEmptyBeforeGrace()
    {
        Game game = FoundedGame();
        Assert.Empty(game.GetMonarchActionChoices(29)); // grace = (6 - dx)*10 = 30 at medium
        Assert.NotEmpty(game.GetMonarchActionChoices(30));
    }

    [Fact]
    public void GetMonarchActionChoices_IsEmptyWithoutSettlements()
    {
        Game game = Game.New(Classic, Seed); // no colony founded
        Assert.Empty(game.GetMonarchActionChoices(50));
    }

    [Fact]
    public void GetMonarchActionChoices_OffersNoActionAndTaxRise_WithTheFreeColWeights()
    {
        Game game = FoundedGame();
        var choices = game.GetMonarchActionChoices(50).ToDictionary(c => c.Action, c => c.Weight);

        Assert.Equal(Math.Max(200 - 50, 100), choices[MonarchAction.NoAction]); // max(150,100) = 150
        Assert.Equal(8, choices[MonarchAction.RaiseTaxAct]);  // 5 + dx (dx=3)
        Assert.Equal(8, choices[MonarchAction.RaiseTaxWar]);
        Assert.False(choices.ContainsKey(MonarchAction.SupportLand)); // never offered at medium (dx == 3)
        Assert.False(choices.ContainsKey(MonarchAction.AddToRef));    // REF modelled in item 6
    }

    [Fact]
    public void NoActionWeight_FloorsAt100_LateGame()
    {
        Game game = FoundedGame();
        var choices = game.GetMonarchActionChoices(250).ToDictionary(c => c.Action, c => c.Weight);
        Assert.Equal(100, choices[MonarchAction.NoAction]); // max(200-250, 100) = 100
    }

    // ── Validity oracle ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MonarchActionIsValid_TaxBounds()
    {
        Game game = FoundedGame();
        Player king = game.HumanPlayer;

        king.TaxRate = 64;
        Assert.True(game.MonarchActionIsValid(MonarchAction.RaiseTaxAct));  // 64 < 65
        king.TaxRate = 65;
        Assert.False(game.MonarchActionIsValid(MonarchAction.RaiseTaxAct)); // at the cap

        king.TaxRate = 31;
        Assert.True(game.MonarchActionIsValid(MonarchAction.LowerTaxWar));  // 31 > 30
        king.TaxRate = 30;
        Assert.False(game.MonarchActionIsValid(MonarchAction.LowerTaxWar)); // at the floor+10

        Assert.False(game.MonarchActionIsValid(MonarchAction.ForceTax));    // never chooseable
        Assert.False(game.MonarchActionIsValid(MonarchAction.Displeasure));
        Assert.True(game.MonarchActionIsValid(MonarchAction.NoAction));
    }

    [Fact]
    public void MonarchActionIsValid_HessianNeedsFiveThousandGold()
    {
        Game game = FoundedGame();
        game.HumanPlayer.Gold = 4999;
        Assert.False(game.MonarchActionIsValid(MonarchAction.HessianMercenaries));
        game.HumanPlayer.Gold = 5000;
        Assert.True(game.MonarchActionIsValid(MonarchAction.HessianMercenaries));
    }

    // ── The tick: determinism (stream 0 untouched) ───────────────────────────────────────────────────────

    [Fact]
    public void MonarchTick_IsByteIdenticalAcrossTwinGames_PastGrace()
    {
        // The whole point: two identical founded games run past the grace period stay byte-identical on stream 0,
        // i.e. the monarch's ephemeral RNG never perturbs the human stream.
        Game a = FoundedGame(7777);
        Game b = FoundedGame(7777);
        for (int i = 0; i < 40; i++)
        {
            a.EndTurn();
            b.EndTurn();
        }
        Assert.Equal(a.RandomState, b.RandomState);
        Assert.Equal(a.HumanPlayer.TaxRate, b.HumanPlayer.TaxRate);
    }

    // ── Item 2: RAISE_TAX demand + tax mutation ──────────────────────────────────────────────────────────

    [Fact]
    public void RaiseTaxAmount_AddsOnePlusRoll_CappedAtMax()
    {
        Game game = FoundedGame();
        game.HumanPlayer.TaxRate = 10;
        Assert.Equal(13, game.RaiseTaxAmount(new ScriptedRandom(2))); // 10 + 1 + 2
        game.HumanPlayer.TaxRate = 64;
        Assert.Equal(65, game.RaiseTaxAmount(new ScriptedRandom(5))); // capped at 65
    }

    [Fact]
    public void LowerTaxAmount_SubtractsOnePlusRoll_FlooredAtMin()
    {
        Game game = FoundedGame();
        game.HumanPlayer.TaxRate = 40;
        Assert.Equal(36, game.LowerTaxAmount(new ScriptedRandom(3))); // 40 - 1 - 3
        game.HumanPlayer.TaxRate = 21;
        Assert.Equal(20, game.LowerTaxAmount(new ScriptedRandom(7))); // floored at 20
    }

    [Fact]
    public void GetMostValuableGoods_NullWithoutTradeableStores()
    {
        Game game = Game.New(Classic, Seed); // no colony at all
        Assert.Null(game.GetMostValuableGoods(game.HumanPlayer));
    }

    [Fact]
    public void GetMostValuableGoods_PicksTheTradeableStockpile_CappedAtOneCargo()
    {
        (Game game, var colony) = FoundedColony();
        colony.AddGoods("model.goods.furs", 150); // more than one cargo

        ValuableGoods? best = game.GetMostValuableGoods(game.HumanPlayer);

        Assert.Equal("model.goods.furs", best!.GoodsId);
        Assert.Equal(colony.Id, best.ColonyId);
        Assert.Equal(Market.CargoChunk, best.Amount); // capped at 100
    }

    [Fact]
    public void DispatchRaiseTax_OpensADemand_AcceptRaisesTheTax()
    {
        (Game game, var colony) = FoundedColony();
        colony.AddGoods("model.goods.furs", 100);
        game.HumanPlayer.TaxRate = 10;

        game.DispatchMonarchAction(MonarchAction.RaiseTaxAct, new ScriptedRandom(2)); // raise → 10 + 1 + 2 = 13
        Assert.Equal(13, game.PendingMonarchDemand!.TaxRaise);

        game.RespondToMonarch(accept: true);
        Assert.Equal(13, game.HumanPlayer.TaxRate);
        Assert.Null(game.PendingMonarchDemand);
    }

    [Fact]
    public void RejectTaxDemand_WithGoodsGone_ForcesTaxPlusThree()
    {
        (Game game, var colony) = FoundedColony();
        colony.AddGoods("model.goods.furs", 100);
        game.HumanPlayer.TaxRate = 10;
        game.DispatchMonarchAction(MonarchAction.RaiseTaxAct, new ScriptedRandom(0)); // raise → 11

        colony.AddGoods("model.goods.furs", -100); // the player sold/moved the taxed goods before answering
        game.RespondToMonarch(accept: false);

        Assert.Equal(11 + 3, game.HumanPlayer.TaxRate); // FORCE_TAX surcharge
    }

    [Fact]
    public void DispatchLowerTax_AppliesImmediately_NoDemand()
    {
        Game game = FoundedGame();
        game.HumanPlayer.TaxRate = 40;
        game.DispatchMonarchAction(MonarchAction.LowerTaxWar, new ScriptedRandom(3));
        Assert.Equal(36, game.HumanPlayer.TaxRate); // 40 - 1 - 3
        Assert.Null(game.PendingMonarchDemand);
    }

    [Fact]
    public void RespondToMonarch_ThrowsWhenNothingPending() =>
        Assert.Throws<InvalidOperationException>(() => FoundedGame().RespondToMonarch(true));

    // ── Item 3: Boston Tea Party + boycott/arrears + pay-to-lift ─────────────────────────────────────────

    [Fact]
    public void RejectTaxDemand_WithGoodsPresent_HoldsATeaParty()
    {
        (Game game, var colony) = FoundedColony();
        colony.AddGoods("model.goods.furs", 100);
        int salePrice = game.HumanPlayer.Market.BidPrice("model.goods.furs");
        game.HumanPlayer.TaxRate = 10;
        game.DispatchMonarchAction(MonarchAction.RaiseTaxAct, new ScriptedRandom(2));

        game.RespondToMonarch(accept: false); // tea party (goods still present)

        Assert.Equal(10, game.HumanPlayer.TaxRate);                              // tax NOT raised
        Assert.Equal(0, colony.StoreOf("model.goods.furs"));                     // goods dumped overboard
        Assert.Equal(salePrice * 300, game.HumanPlayer.Market.Arrears("model.goods.furs")); // boycott back-tax
        Assert.False(game.HumanPlayer.Market.CanTrade("model.goods.furs"));      // now boycotted
        Assert.Equal(25, colony.TeaPartyBellTurns);                              // rebel surge armed
    }

    [Fact]
    public void BoycottedGood_CannotBeSold()
    {
        (Game game, var colony) = FoundedColony();
        colony.AddGoods("model.goods.furs", 100);
        game.HumanPlayer.Market.SetArrears("model.goods.furs", 5000);

        Assert.Throws<InvalidMoveException>(() => game.SellColonyGoods(colony, "model.goods.furs", 50));
    }

    [Fact]
    public void PayArrears_LiftsTheBoycott_ForItsFullCost()
    {
        Game game = FoundedGame();
        game.HumanPlayer.Market.SetArrears("model.goods.furs", 1200);
        game.HumanPlayer.Gold = 1500;

        Assert.True(game.CheckPayArrears("model.goods.furs").Allowed);
        game.PayArrears("model.goods.furs");

        Assert.Equal(300, game.HumanPlayer.Gold);                            // 1500 − 1200
        Assert.True(game.HumanPlayer.Market.CanTrade("model.goods.furs"));   // boycott lifted
    }

    [Fact]
    public void PayArrears_RefusedWhenNotBoycottedOrUnaffordable()
    {
        Game game = FoundedGame();
        Assert.False(game.CheckPayArrears("model.goods.furs").Allowed); // not boycotted
        game.HumanPlayer.Market.SetArrears("model.goods.furs", 1200);
        game.HumanPlayer.Gold = 100;
        Assert.False(game.CheckPayArrears("model.goods.furs").Allowed); // can't afford
        Assert.Throws<InvalidMoveException>(() => game.PayArrears("model.goods.furs"));
    }

    [Fact]
    public void TeaPartyBellSurge_DecaysEachTurn()
    {
        (Game game, var colony) = FoundedColony();
        colony.TeaPartyBellTurns = 25;
        Assert.Equal(50, colony.TeaPartyBellBonusPercent); // +50% at full

        game.EndTurn();
        Assert.Equal(24, colony.TeaPartyBellTurns); // decayed one turn
        Assert.Equal(48, colony.TeaPartyBellBonusPercent);
    }

    [Fact]
    public void Boycott_PersistsAcrossSaveLoad()
    {
        (Game game, var colony) = FoundedColony();
        game.HumanPlayer.Market.SetArrears("model.goods.furs", 1500);
        colony.TeaPartyBellTurns = 20;

        Game loaded = CrownAndColony.GameLogic.Persistence.SaveGame
            .FromJson(CrownAndColony.GameLogic.Persistence.SaveGame.From(game).ToJson()).Restore(Classic);

        Assert.Equal(1500, loaded.HumanPlayer.Market.Arrears("model.goods.furs"));
        Assert.Equal(20, loaded.Colonies.First().TeaPartyBellTurns);
    }
}
