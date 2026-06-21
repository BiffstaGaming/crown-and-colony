using System.Reflection;
using System.Xml.Linq;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Randomness;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Trade;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
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
        Assert.Equal(13, choices[MonarchAction.AddToRef]);            // 10 + dx — the highest-weighted active action
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

    [Fact]
    public void MonarchTick_DrawsNothingFromStreamZero_AcrossTheGraceBoundary()
    {
        // Stronger than the twin test: with the human idle, stream 0 must not move even though the monarch acts every
        // turn past the grace boundary (turn 30) — the ephemeral monarch RNG is isolated (ADR-009).
        // Seed 27: a founded game whose colony grows slowly enough to stay quiet on stream 0 across the boundary. (Map-gen
        // fidelity changes shift which seeds stay idle — the P5 mountain ranges retargeted this off 7777, and the P5
        // resource-placement change retargeted it off 25; 27 keeps the human genuinely idle through turn 38.)
        Game game = FoundedGame(27);
        for (int i = 0; i < 28; i++)
        {
            game.EndTurn(); // settle into a quiet stream-0 state (just before the grace boundary)
        }
        RandomState frozen = game.RandomState;
        for (int i = 0; i < 10; i++)
        {
            game.EndTurn(); // run past turn 30 — the King now weighs an action every turn
        }
        Assert.Equal(frozen, game.RandomState); // …yet stream 0 is byte-frozen
    }

    [Fact]
    public void FreshGame_OmitsAllOptionalMonarchTokens()
    {
        // omit-when-default for the v37-v39 player tokens: a game with no tea party / displeasure / support carries none.
        string json = CrownAndColony.GameLogic.Persistence.SaveGame.From(Game.New(Classic, Seed)).ToJson();
        Assert.DoesNotContain("\"Arrears\"", json);
        Assert.DoesNotContain("\"TeaPartyBellTurns\"", json);
        Assert.DoesNotContain("\"MonarchDispleasure\"", json);
        Assert.DoesNotContain("\"SupportSeaGranted\"", json);
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
    public void DispatchLowerTax_FiresFromTheChooser_WhenTaxIsAboveTheFloor()
    {
        // The chooser offers LOWER_TAX (war/other) at weight 5 − dx = 2 once the tax is above the floor+10 (FreeCol),
        // and dispatching the offered action lowers the rate. Proves the goodwill tax-cut is reachable end-to-end.
        Game game = FoundedGame();
        game.HumanPlayer.TaxRate = 40;

        var choices = game.GetMonarchActionChoices(50).ToDictionary(c => c.Action, c => c.Weight);
        Assert.Equal(2, choices[MonarchAction.LowerTaxWar]);   // 5 − dx (dx = 3 at medium)
        Assert.Equal(2, choices[MonarchAction.LowerTaxOther]);

        game.DispatchMonarchAction(MonarchAction.LowerTaxOther, new ScriptedRandom(2));
        Assert.Equal(37, game.HumanPlayer.TaxRate); // 40 − 1 − 2
    }

    // ── WAIVE_TAX: the King forgives outstanding boycott back-taxes (86d3c9r68) ──────────────────────────

    [Fact]
    public void DispatchWaiveTax_ForgivesArrears_LiftsEveryBoycott()
    {
        // Our WAIVE_TAX goodwill (a documented deviation from FreeCol's message-only action): the King clears all
        // outstanding boycott back-taxes for free, lifting the boycott — no gold spent (unlike PayArrears).
        Game game = FoundedGame();
        game.HumanPlayer.Market.SetArrears("model.goods.furs", 1500);
        game.HumanPlayer.Market.SetArrears("model.goods.rum", 800);
        int goldBefore = game.HumanPlayer.Gold;

        game.DispatchMonarchAction(MonarchAction.WaiveTax, new ScriptedRandom());

        Assert.Equal(0, game.HumanPlayer.Market.Arrears("model.goods.furs"));
        Assert.Equal(0, game.HumanPlayer.Market.Arrears("model.goods.rum"));
        Assert.True(game.HumanPlayer.Market.CanTrade("model.goods.furs")); // boycott lifted
        Assert.True(game.HumanPlayer.Market.CanTrade("model.goods.rum"));
        Assert.Equal(goldBefore, game.HumanPlayer.Gold);                   // forgiveness is free
        Assert.Null(game.PendingMonarchDemand);                            // immediate, no player choice
    }

    [Fact]
    public void MonarchActionIsValid_WaiveTax_OnlyWhenArrearsOutstanding()
    {
        // WAIVE_TAX is gated on there being a boycott to forgive — so a game with no arrears never offers it,
        // leaving the default weighted table (and every existing seeded game) untouched.
        Game game = FoundedGame();
        Assert.False(game.MonarchActionIsValid(MonarchAction.WaiveTax)); // nothing to forgive

        game.HumanPlayer.Market.SetArrears("model.goods.furs", 1500);
        Assert.True(game.MonarchActionIsValid(MonarchAction.WaiveTax));  // a boycott exists
    }

    [Fact]
    public void WaiveTax_OfferedFromTheChooser_OnlyWhenArrears()
    {
        Game game = FoundedGame();
        // No arrears → not in the table (default-game determinism guard).
        Assert.DoesNotContain(MonarchAction.WaiveTax, game.GetMonarchActionChoices(50).Select(c => c.Action));

        game.HumanPlayer.Market.SetArrears("model.goods.furs", 1500);
        var choices = game.GetMonarchActionChoices(50).ToDictionary(c => c.Action, c => c.Weight);
        Assert.Equal(2, choices[MonarchAction.WaiveTax]); // 5 − dx, mirroring its LOWER_TAX goodwill siblings
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

    // ── Item 4: mercenary offers + DISPLEASURE ───────────────────────────────────────────────────────────

    [Fact]
    public void Mercenaries_Offered_AcceptSpendsGoldAndSpawnsVeteransInEurope()
    {
        Game game = FoundedGame();
        game.HumanPlayer.Gold = 100_000; // plenty to be offered a full force
        game.DispatchMonarchAction(MonarchAction.MonarchMercenaries, new ScriptedRandom(0, 0, 0, 0, 0, 0, 0, 0));

        PendingMonarchDemand? offer = game.PendingMonarchDemand;
        Assert.NotNull(offer);
        Assert.True(offer!.Price > 0);
        Assert.NotEmpty(offer.Offer!);
        int veteransOffered = offer.Offer!.Sum(e => e.Count);
        int goldBefore = game.HumanPlayer.Gold;

        game.RespondToMonarch(accept: true);

        Assert.Equal(goldBefore - offer.Price, game.HumanPlayer.Gold);
        Assert.Equal(veteransOffered, game.UnitsInEurope.Count(u => u.Type.Id == "model.unit.veteranSoldier"));
        Assert.False(game.HumanPlayer.MonarchDispleasure); // accepting keeps the King content
    }

    [Fact]
    public void Mercenaries_DeclinedWhenAffordable_SetsDispleasure_AndGatesFutureOffers()
    {
        Game game = FoundedGame();
        game.HumanPlayer.Gold = 100_000;
        game.DispatchMonarchAction(MonarchAction.MonarchMercenaries, new ScriptedRandom(0, 0, 0, 0, 0, 0, 0, 0));
        Assert.NotNull(game.PendingMonarchDemand);

        game.RespondToMonarch(accept: false); // could afford, but declined

        Assert.True(game.HumanPlayer.MonarchDispleasure);
        Assert.False(game.MonarchActionIsValid(MonarchAction.MonarchMercenaries)); // now gated off
        Assert.False(game.MonarchActionIsValid(MonarchAction.SupportSea));
    }

    [Fact]
    public void Mercenaries_NotOfferedWhenPlayerCannotAffordEvenOne()
    {
        Game game = FoundedGame();
        game.HumanPlayer.Gold = 50; // far below one veteran's mercenary price
        game.DispatchMonarchAction(MonarchAction.MonarchMercenaries, new ScriptedRandom(0, 0, 0, 0, 0, 0, 0, 0));
        Assert.Null(game.PendingMonarchDemand); // no affordable offer → none made
    }

    [Fact]
    public void HessianMercenaries_RequireFiveThousandGold()
    {
        Game game = FoundedGame();
        game.HumanPlayer.Gold = 4999;
        Assert.False(game.MonarchActionIsValid(MonarchAction.HessianMercenaries));
        game.HumanPlayer.Gold = 5000;
        Assert.True(game.MonarchActionIsValid(MonarchAction.HessianMercenaries));
    }

    [Fact]
    public void Displeasure_PersistsAcrossSaveLoad()
    {
        Game game = FoundedGame();
        game.HumanPlayer.MonarchDispleasure = true;

        Game loaded = CrownAndColony.GameLogic.Persistence.SaveGame
            .FromJson(CrownAndColony.GameLogic.Persistence.SaveGame.From(game).ToJson()).Restore(Classic);

        Assert.True(loaded.HumanPlayer.MonarchDispleasure);
    }

    // ── Item 5: SUPPORT_LAND / SUPPORT_SEA free aid ──────────────────────────────────────────────────────

    [Fact]
    public void SupportSea_GrantsAFreeNavalShip_OnceAndForFree()
    {
        Game game = FoundedGame();
        int goldBefore = game.HumanPlayer.Gold;

        game.DispatchMonarchAction(MonarchAction.SupportSea, new Pcg32Random(1));

        Assert.Equal(goldBefore, game.HumanPlayer.Gold);                                  // free aid
        Assert.Equal(1, game.UnitsInEurope.Count(u => u.Type.Id == "model.unit.frigate")); // a frigate on the dock
        Assert.True(game.HumanPlayer.SupportSeaGranted);
        Assert.False(game.MonarchActionIsValid(MonarchAction.SupportSea));                // one-shot — now gated off
    }

    [Fact]
    public void SupportSea_ValidityGate_NeedsPrivateerRaid_NotYetGranted_NotDispleased()
    {
        Game game = FoundedGame();
        Assert.False(game.MonarchActionIsValid(MonarchAction.SupportSea)); // no privateer raid yet
        game.AttackedByPrivateers = true;
        Assert.True(game.MonarchActionIsValid(MonarchAction.SupportSea));
        game.HumanPlayer.MonarchDispleasure = true;
        Assert.False(game.MonarchActionIsValid(MonarchAction.SupportSea)); // displeased King gives nothing
    }

    [Fact]
    public void SupportLand_GrantsTwoMountedVeterans_ButIsNeverOfferedAtMedium()
    {
        Game game = FoundedGame();
        // The handler delivers the level-2 composition (2 mounted veterans), free.
        game.DispatchMonarchAction(MonarchAction.SupportLand, new Pcg32Random(1));
        Assert.Equal(2, game.UnitsInEurope.Count(u =>
            u.Type.Id == "model.unit.veteranSoldier" && u.RoleId == "model.role.dragoon"));

        // …but the chooser never offers it at medium difficulty (dx == 3).
        game.AttackedByPrivateers = true;
        Assert.DoesNotContain(MonarchAction.SupportLand, game.GetMonarchActionChoices(50).Select(c => c.Action));
    }

    [Fact]
    public void SupportSeaGranted_PersistsAcrossSaveLoad()
    {
        Game game = FoundedGame();
        game.HumanPlayer.SupportSeaGranted = true;

        Game loaded = CrownAndColony.GameLogic.Persistence.SaveGame
            .FromJson(CrownAndColony.GameLogic.Persistence.SaveGame.From(game).ToJson()).Restore(Classic);

        Assert.True(loaded.HumanPlayer.SupportSeaGranted);
    }

    // ── Item 6: REF build-up + Force model ───────────────────────────────────────────────────────────────

    [Fact]
    public void BaseRef_HasTheMediumComposition_AndUnbalancedCapacity()
    {
        Game game = FoundedGame();
        Force ref_ = game.EnsureRefForce();

        Assert.Equal(31 + 15 + 14, ref_.LandUnitCount); // 60 land (infantry + cavalry + artillery)
        Assert.Equal(8, ref_.NavalUnitCount);            // 8 men-o-war
        // The base navy can't yet carry the land force, so ADD_TO_REF should grow the navy first.
        Assert.True(ref_.NavalCapacity(Classic) < ref_.SpaceRequired(Classic) * 1.1);
    }

    [Fact]
    public void AddToRef_GrowsTheNavy_WhileItCannotCarryTheLand()
    {
        Game game = FoundedGame();
        int navyBefore = game.EnsureRefForce().NavalUnitCount;

        game.AddToRef(new Pcg32Random(1)); // capacity short → adds a man-o-war (no land-roll consumed)

        Assert.Equal(navyBefore + 1, game.EnsureRefForce().NavalUnitCount);
    }

    [Fact]
    public void AddToRef_GrowsTheLand_OnceTheNavyCanCarryIt()
    {
        Game game = FoundedGame();
        Force ref_ = game.EnsureRefForce();
        // Pile on men-o-war until the navy comfortably exceeds the land it must carry.
        for (int i = 0; i < 50; i++)
        {
            ref_.AddNaval("model.unit.manOWar", null, 1);
        }
        Assert.True(ref_.NavalCapacity(Classic) >= ref_.SpaceRequired(Classic) * 1.1);
        int landBefore = ref_.LandUnitCount;

        game.AddToRef(new Pcg32Random(2)); // now adds 1-3 land units

        Assert.InRange(game.EnsureRefForce().LandUnitCount - landBefore, 1, 3);
    }

    [Fact]
    public void AddToRef_IsOfferedByTheChooser()
    {
        Game game = FoundedGame();
        Assert.True(game.MonarchActionIsValid(MonarchAction.AddToRef));
        Assert.Contains(MonarchAction.AddToRef, game.GetMonarchActionChoices(50).Select(c => c.Action));
    }

    [Fact]
    public void RefForce_PersistsAcrossSaveLoad_AndIsOmittedBeforeGrowth()
    {
        Game fresh = FoundedGame();
        // A game that never grew the REF omits it (byte-identical to v39) and re-derives the base on demand.
        Assert.Null(CrownAndColony.GameLogic.Persistence.SaveGame.From(fresh).RefForce);

        Game game = FoundedGame();
        game.AddToRef(new Pcg32Random(1)); // grow it (a man-o-war)
        int navy = game.EnsureRefForce().NavalUnitCount;

        Game loaded = CrownAndColony.GameLogic.Persistence.SaveGame
            .FromJson(CrownAndColony.GameLogic.Persistence.SaveGame.From(game).ToJson()).Restore(Classic);

        Assert.NotNull(loaded.RefForceOrNull);
        Assert.Equal(navy, loaded.RefForceOrNull!.NavalUnitCount);
        Assert.Equal(game.EnsureRefForce().LandUnitCount, loaded.RefForceOrNull.LandUnitCount);
    }

    // ── Monarch tuning is difficulty-driven (86d3c9rg6) ──────────────────────────────────────────────────────
    //
    // The monarch's tuning numbers (meddling, tax cap/spread, mercenary price, support size, boycott factor, REF
    // base composition) moved out of hardcoded consts into Ruleset.Difficulty.Monarch. These prove the default
    // game is byte-identical (the classic spec parses the old const values) AND that a modified ruleset value
    // genuinely changes the behaviour (so it is data-driven, not still hardcoded).

    /// <summary>The classic spec's medium monarch group parses to exactly the old hardcoded constants (value-preserving).</summary>
    [Fact]
    public void ClassicRuleset_ParsesTheMediumMonarchValues_EqualToTheOldConsts()
    {
        MonarchOptions mon = Classic.Difficulty.Monarch;
        Assert.Equal(2, mon.Meddling);                 // dx = 1 + 2 = 3 → grace 30
        Assert.Equal(65, mon.MaximumTaxRate);
        Assert.Equal(2, mon.TaxAdjustment);
        Assert.Equal(65, mon.MercenaryPricePercent);
        Assert.Equal(2, mon.SupportLandMountedUnits);
        Assert.Equal(31, mon.RefBaseInfantry);
        Assert.Equal(15, mon.RefBaseCavalry);
        Assert.Equal(14, mon.RefBaseArtillery);
        Assert.Equal(8, mon.RefBaseManOWar);
        Assert.Equal(1500, mon.WarSupportGold);
        // The war-support force is a list (record equality is reference-based), so compare its block-by-block contents…
        Assert.Equal(
            MonarchOptions.ClassicMedium.WarSupportForce.Select(b => (b.UnitTypeId, b.RoleId, b.Number)),
            mon.WarSupportForce.Select(b => (b.UnitTypeId, b.RoleId, b.Number)));
        // …then equate the rest of the record by normalising both to the same list instance.
        Assert.Equal(MonarchOptions.ClassicMedium, mon with { WarSupportForce = MonarchOptions.ClassicMedium.WarSupportForce });
    }

    /// <summary>Each monarch integer option is read by its own spec id (non-default values prove no silent fallback).</summary>
    [Fact]
    public void ParseDifficulty_ReadsTheMonarchOptions_ByTheirOwnIds()
    {
        XElement root = XElement.Parse(
            "<freecol-specification><optionGroup id='model.difficulty.medium'>" +
            "  <integerOption id='model.option.monarchMeddling' value='4' />" +
            "  <integerOption id='model.option.maximumTax' value='75' />" +
            "  <integerOption id='model.option.taxAdjustment' value='3' />" +
            "  <integerOption id='model.option.mercenaryPrice' value='80' />" +
            "  <integerOption id='model.option.monarchSupport' value='6' />" +
            "</optionGroup></freecol-specification>");
        MonarchOptions mon = Ruleset.ParseDifficulty(root).Monarch;
        Assert.Equal(4, mon.Meddling);
        Assert.Equal(75, mon.MaximumTaxRate);
        Assert.Equal(3, mon.TaxAdjustment);
        Assert.Equal(80, mon.MercenaryPricePercent);
        Assert.Equal(6, mon.SupportLandMountedUnits);
    }

    /// <summary>The REF base counts are read from the nested <c>refSize</c> unitListOption (each block's <c>number</c>).</summary>
    [Fact]
    public void ParseDifficulty_ReadsTheRefSizeNumbers_FromTheUnitListOption()
    {
        XElement root = XElement.Parse(
            "<freecol-specification><optionGroup id='model.difficulty.medium'>" +
            "  <unitListOption id='model.option.refSize'>" +
            "    <unitOption id='model.option.refSize.soldiers'><number value='40' /></unitOption>" +
            "    <unitOption id='model.option.refSize.dragoons'><number value='20' /></unitOption>" +
            "    <unitOption id='model.option.refSize.artillery'><number value='10' /></unitOption>" +
            "    <unitOption id='model.option.refSize.menOfWar'><number value='12' /></unitOption>" +
            "  </unitListOption>" +
            "</optionGroup></freecol-specification>");
        MonarchOptions mon = Ruleset.ParseDifficulty(root).Monarch;
        Assert.Equal(40, mon.RefBaseInfantry);
        Assert.Equal(20, mon.RefBaseCavalry);
        Assert.Equal(10, mon.RefBaseArtillery);
        Assert.Equal(12, mon.RefBaseManOWar);
    }

    /// <summary>
    /// ArrearsFactor stays the classic-game 300, deliberately NOT read from <c>model.option.arrearsFactor</c> (whose
    /// medium value is 500) — a value-preservation guard so the boycott back-tax is byte-identical to the old const.
    /// </summary>
    [Fact]
    public void ArrearsFactor_StaysAt300_NotReadFromTheSpecOption()
    {
        Assert.Equal(300, Classic.Difficulty.Monarch.ArrearsFactor); // not 500 (the spec's medium arrearsFactor)
        // Even when a spec restates arrearsFactor, the monarch option keeps 300 (the parser does not read that id).
        XElement root = XElement.Parse(
            "<freecol-specification><optionGroup id='model.difficulty.medium'>" +
            "  <integerOption id='model.option.arrearsFactor' value='500' />" +
            "</optionGroup></freecol-specification>");
        Assert.Equal(300, Ruleset.ParseDifficulty(root).Monarch.ArrearsFactor);
    }

    // ── Data-driven behaviour proofs (a modified ruleset changes the monarch's behaviour) ─────────────────────

    /// <summary>The grace period follows <c>monarchMeddling</c>: a higher meddling shrinks the grace, so the chooser wakes earlier.</summary>
    [Fact]
    public void Grace_FollowsRulesetMeddling()
    {
        // meddling 4 → dx = 5 → grace = (6 − 5)·10 = 10 (vs 30 at the default meddling 2).
        Game game = FoundedGame(RulesetWithMonarch(("model.option.monarchMeddling", "4")));
        Assert.Empty(game.GetMonarchActionChoices(9));     // still inside the (now shorter) grace
        Assert.NotEmpty(game.GetMonarchActionChoices(10)); // …and awake at turn 10 (the default game is empty until 30)
    }

    /// <summary>A raise is capped at the ruleset's <c>maximumTax</c>, and validity flips at that cap — not the old hardcoded 65.</summary>
    [Fact]
    public void TaxCap_FollowsRulesetMaximumTax()
    {
        Game game = FoundedGame(RulesetWithMonarch(("model.option.maximumTax", "50")));
        game.HumanPlayer.TaxRate = 48;
        Assert.Equal(50, game.RaiseTaxAmount(new ScriptedRandom(9))); // capped at the ruleset max (50), not 65
        Assert.True(game.MonarchActionIsValid(MonarchAction.RaiseTaxAct));  // 48 < 50
        game.HumanPlayer.TaxRate = 50;
        Assert.False(game.MonarchActionIsValid(MonarchAction.RaiseTaxAct)); // at the ruleset cap
    }

    /// <summary>The base REF composition follows the ruleset's <c>refSize</c> numbers.</summary>
    [Fact]
    public void BaseRef_FollowsRulesetRefSize()
    {
        Game game = FoundedGame(RulesetWithRefSize(soldiers: 40, dragoons: 20, artillery: 10, menOfWar: 12));
        Force ref_ = game.EnsureRefForce();
        Assert.Equal(40 + 20 + 10, ref_.LandUnitCount); // 70 land (vs 60 at the default refSize)
        Assert.Equal(12, ref_.NavalUnitCount);          // 12 men-o-war (vs 8)
    }

    /// <summary>The mercenary offer price follows the ruleset's <c>mercenaryPrice</c> percentage.</summary>
    [Fact]
    public void MercenaryPrice_FollowsRulesetMercenaryPrice()
    {
        // A cheaper percentage (30 vs the default 65) makes the same offer cost proportionally less.
        Game cheap = FoundedGame(RulesetWithMonarch(("model.option.mercenaryPrice", "30")));
        Game dear = FoundedGame(RulesetWithMonarch(("model.option.mercenaryPrice", "60")));
        cheap.HumanPlayer.Gold = dear.HumanPlayer.Gold = 100_000;

        var rolls = new[] { 0, 0, 0, 0, 0, 0, 0, 0 };
        cheap.DispatchMonarchAction(MonarchAction.MonarchMercenaries, new ScriptedRandom(rolls));
        dear.DispatchMonarchAction(MonarchAction.MonarchMercenaries, new ScriptedRandom(rolls));

        // Same scripted force; the 30%-priced offer must be cheaper than the 60%-priced one (proves the % is data-driven).
        Assert.True(cheap.PendingMonarchDemand!.Price < dear.PendingMonarchDemand!.Price);
    }

    // ── Custom-ruleset helper ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads the embedded classic spec, overrides the named integer options in the <em>medium</em> difficulty group,
    /// and re-parses it into a full <see cref="Ruleset"/>. Lets a behaviour test run a real <see cref="Game"/> against
    /// a modified monarch tuning without hand-writing a whole spec.
    /// </summary>
    private static Ruleset RulesetWithMonarch(params (string Id, string Value)[] integerOverrides) =>
        EditedClassicRuleset(medium =>
        {
            foreach ((string id, string value) in integerOverrides)
            {
                medium.Descendants("integerOption").First(o => (string?)o.Attribute("id") == id).SetAttributeValue("value", value);
            }
        });

    /// <summary>The classic ruleset with the medium <c>refSize</c> block numbers overridden.</summary>
    private static Ruleset RulesetWithRefSize(int soldiers, int dragoons, int artillery, int menOfWar) =>
        EditedClassicRuleset(medium =>
        {
            void SetRef(string blockId, int n) =>
                medium.Descendants("unitOption").First(o => (string?)o.Attribute("id") == blockId)
                    .Element("number")!.SetAttributeValue("value", n);
            SetRef("model.option.refSize.soldiers", soldiers);
            SetRef("model.option.refSize.dragoons", dragoons);
            SetRef("model.option.refSize.artillery", artillery);
            SetRef("model.option.refSize.menOfWar", menOfWar);
        });

    /// <summary>Loads the embedded classic spec, applies <paramref name="editMedium"/> to its medium difficulty group, and re-parses it.</summary>
    private static Ruleset EditedClassicRuleset(Action<XElement> editMedium)
    {
        Assembly assembly = typeof(Ruleset).Assembly;
        const string resource = "CrownAndColony.GameLogic.Specification.classic.specification.xml";
        using Stream stream = assembly.GetManifestResourceStream(resource)!;
        XDocument doc = XDocument.Load(stream);

        // The medium difficulty group → the only subtree ParseDifficulty reads (default level).
        XElement medium = doc.Descendants("optionGroup").First(g => (string?)g.Attribute("id") == "model.difficulty.medium");
        editMedium(medium);

        var ms = new MemoryStream();
        doc.Save(ms);
        ms.Position = 0;
        return Ruleset.Load(ms);
    }

    /// <summary>A founded game on a custom ruleset (mirrors <see cref="FoundedGame(ulong)"/> but for a modified spec).</summary>
    private static Game FoundedGame(Ruleset ruleset)
    {
        Game game = Game.New(ruleset, Seed);
        game.FoundColony(game.Units.First(u => u.IsOnMap && u.Type.CanFoundColony));
        return game;
    }

    // ── Monarch declares war / peace (86d3c9r7j) ─────────────────────────────────────────────────────────

    private static List<Player> ColonialRivals(Game game)
    {
        int human = game.HumanPlayer.PlayerId;
        return game.Players
            .Where(p => p.PlayerId != human && p.PlayerType == PlayerType.Colonial)
            .OrderBy(p => p.PlayerId)
            .ToList();
    }

    [Fact]
    public void DeclareWar_PutsTheHumanAtWarWithThePeaceStandingRivalTheKingPicks()
    {
        Game game = FoundedGame();
        int human = game.HumanPlayer.PlayerId;
        List<Player> rivals = ColonialRivals(game);
        Assert.NotEmpty(rivals);
        // Leave exactly one valid (peace-standing) target so the King's pick is determined: the rest are at war.
        foreach (Player p in rivals)
        {
            game.SetStance(human, p.PlayerId, Stance.War);
        }
        Player target = rivals[0];
        game.SetStance(human, target.PlayerId, Stance.Peace);

        // Exactly one eligible target → the pick is determined regardless of the RNG; a real generator also feeds the
        // war-support decision/rolls (which may now also fire on a DECLARE_WAR — covered separately below).
        game.DispatchMonarchAction(MonarchAction.DeclareWar, new Pcg32Random(1));

        Assert.Equal(Stance.War, game.StanceBetween(human, target.PlayerId));
    }

    [Fact]
    public void DeclarePeace_EndsTheWarWithTheRivalTheKingPicks()
    {
        Game game = FoundedGame();
        int human = game.HumanPlayer.PlayerId;
        List<Player> rivals = ColonialRivals(game);
        Assert.NotEmpty(rivals);
        // Leave exactly one valid (war-standing) target; the rest at peace.
        foreach (Player p in rivals)
        {
            game.SetStance(human, p.PlayerId, Stance.Peace);
        }
        Player target = rivals[0];
        game.SetStance(human, target.PlayerId, Stance.War);

        game.DispatchMonarchAction(MonarchAction.DeclarePeace, new ScriptedRandom(0));

        Assert.Equal(Stance.Peace, game.StanceBetween(human, target.PlayerId));
    }

    [Fact]
    public void DeclareWar_WithEveryRivalAlreadyAtWar_IsNotOffered_AndDispatchIsANoOp()
    {
        Game game = FoundedGame();
        int human = game.HumanPlayer.PlayerId;
        List<Player> rivals = ColonialRivals(game);
        foreach (Player p in rivals)
        {
            game.SetStance(human, p.PlayerId, Stance.War);
        }

        // No peace-standing target → the chooser won't offer it…
        Assert.DoesNotContain(MonarchAction.DeclareWar, game.GetMonarchActionChoices(game.Turn).Select(c => c.Action));
        // …and dispatching it anyway draws nothing and changes nothing (safe no-op).
        game.DispatchMonarchAction(MonarchAction.DeclareWar, new ScriptedRandom());
        Assert.All(rivals, p => Assert.Equal(Stance.War, game.StanceBetween(human, p.PlayerId)));
    }

    // ── Benjamin Franklin — ignoreEuropeanWars (86d3c9r7j) ───────────────────────────────────────────────
    //
    // FreeCol Monarch.collectPotentialEnemies returns an empty list when the player has model.ability.ignoreEuropeanWars
    // (Benjamin Franklin), so the King can no longer drag you into his European wars. He can still make *peace* —
    // collectPotentialFriends deliberately does not apply Franklin ("he stops wars, not peace").

    private const string BenjaminFranklin = "model.foundingFather.benjaminFranklin";

    /// <summary>The classic spec really does define Benjamin Franklin carrying the ignoreEuropeanWars ability the gate reads.</summary>
    [Fact]
    public void Spec_DefinesFranklin_WithIgnoreEuropeanWars()
    {
        FoundingFather franklin = Classic.Father(BenjaminFranklin);
        Assert.Contains(franklin.Abilities, a => a.Id == "model.ability.ignoreEuropeanWars" && a.Value);
    }

    [Fact]
    public void Franklin_BlocksTheKingFromDeclaringWar_ButNotFromMakingPeace()
    {
        Game game = FoundedGame();
        int human = game.HumanPlayer.PlayerId;
        List<Player> rivals = ColonialRivals(game);
        Assert.NotEmpty(rivals);

        // Set up a board where DECLARE_WAR *would* be valid (a peace-standing rival) and DECLARE_PEACE too (a war one).
        Player warTarget = rivals[0];
        foreach (Player p in rivals)
        {
            game.SetStance(human, p.PlayerId, Stance.Peace); // every rival at peace → declare-war targets exist
        }
        game.SetStance(human, warTarget.PlayerId, Stance.War); // …and one at war → a declare-peace target exists

        // Both valid before Franklin.
        Assert.True(game.MonarchActionIsValid(MonarchAction.DeclareWar));
        Assert.True(game.MonarchActionIsValid(MonarchAction.DeclarePeace));

        game.HumanPlayer.CongressList.Add(BenjaminFranklin); // elect Franklin

        // The King can no longer impose a war (no potential enemies)…
        Assert.False(game.MonarchActionIsValid(MonarchAction.DeclareWar));
        Assert.DoesNotContain(MonarchAction.DeclareWar, game.GetMonarchActionChoices(game.Turn).Select(c => c.Action));
        // …and a stray DECLARE_WAR dispatch is a no-op (every peace-standing rival stays at peace).
        game.DispatchMonarchAction(MonarchAction.DeclareWar, new ScriptedRandom());
        Assert.All(rivals.Where(p => p.PlayerId != warTarget.PlayerId),
            p => Assert.Equal(Stance.Peace, game.StanceBetween(human, p.PlayerId)));

        // …but he can still make peace (Franklin stops wars, not peace).
        Assert.True(game.MonarchActionIsValid(MonarchAction.DeclarePeace));
        game.DispatchMonarchAction(MonarchAction.DeclarePeace, new ScriptedRandom(0));
        Assert.Equal(Stance.Peace, game.StanceBetween(human, warTarget.PlayerId));
    }

    // ── War support on a King-declared war (86d3c9r7j, FreeCol Monarch.getWarSupport + InGameController) ──────
    //
    // When the King declares a war for you he may also send a one-off force (onto the Europe dock) + gold. He always
    // helps when the rival is at least as strong as you (strength ratio ≤ 0.5 → p ≥ 1, no decision roll), never when
    // you are clearly stronger (ratio > 0.6 → p ≤ 0), and a sliding chance between. Amounts are difficulty-data-driven.

    private const string VeteranSoldier = "model.unit.veteranSoldier";
    private const string SoldierRole = "model.role.soldier";

    /// <summary>
    /// Clears the board's incidental land forces so a strength ratio is controllable: parks every non-native land unit
    /// on <paramref name="parkOwnerId"/> (a colonial player who is neither the human nor the war target), so only the
    /// soldiers a test explicitly hands to the human / enemy count toward <c>LandPowerOf</c>.
    /// </summary>
    private static void ParkLandUnits(Game game, int parkOwnerId)
    {
        foreach (Unit u in game.Units.Where(u => !u.IsNative && !u.Type.IsNaval))
        {
            u.OwnerId = parkOwnerId;
        }
    }

    /// <summary>Gives <paramref name="ownerId"/> armed veteran soldiers (land power, so a strength ratio is testable), placed on a land tile.</summary>
    private static void GiveSoldiers(Game game, int ownerId, int count, Position landTile)
    {
        for (int i = 0; i < count; i++)
        {
            Unit soldier = game.SpawnUnit(Classic.Unit(VeteranSoldier), landTile);
            soldier.RoleId = SoldierRole;
            soldier.OwnerId = ownerId;
        }
    }

    /// <summary>The classic medium war-support amounts parse to 4 veteran soldiers + 1500 gold (not the veryEasy 6/2500).</summary>
    [Fact]
    public void ClassicRuleset_ParsesTheMediumWarSupport_FourVeteransAnd1500Gold()
    {
        MonarchOptions mon = Classic.Difficulty.Monarch;
        Assert.Equal(1500, mon.WarSupportGold);
        MonarchSupportUnit block = Assert.Single(mon.WarSupportForce);
        Assert.Equal(VeteranSoldier, block.UnitTypeId);
        Assert.Equal(SoldierRole, block.RoleId);
        Assert.Equal(4, block.Number);
    }

    [Fact]
    public void GetWarSupport_GrantsTheFullForce_WhenTheEnemyIsAtLeastAsStrong()
    {
        (Game game, var colony) = FoundedColony();
        List<Player> rivals = ColonialRivals(game);
        Player enemy = rivals[0];
        ParkLandUnits(game, rivals[^1].PlayerId);     // clear the board (park on a third rival)
        GiveSoldiers(game, enemy.PlayerId, 2, colony.Position); // enemy has power; human has none → ratio 0 → p ≥ 1

        // p ≥ 1 ⇒ no decision roll; enemy not defenceless ⇒ one count roll per block (here +0 → the base 4).
        var force = game.GetWarSupport(enemy, new ScriptedRandom(1)); // count roll 1 → 4 + 1 − 1 = 4
        ForceEntry block = Assert.Single(force);
        Assert.Equal(VeteranSoldier, block.UnitTypeId);
        Assert.Equal(SoldierRole, block.RoleId);
        Assert.Equal(4, block.Count);
    }

    [Fact]
    public void GetWarSupport_DeclinesToHelp_WhenThePlayerIsMuchStronger()
    {
        (Game game, var colony) = FoundedColony();
        List<Player> rivals = ColonialRivals(game);
        Player enemy = rivals[0];
        ParkLandUnits(game, rivals[^1].PlayerId);              // clear the board
        GiveSoldiers(game, game.HumanPlayer.PlayerId, 3, colony.Position); // human strong, enemy defenceless → ratio 1 → p < 0

        // p ≤ 0 ⇒ never support, no RNG draw at all (the empty ScriptedRandom would throw if any draw were made).
        Assert.Empty(game.GetWarSupport(enemy, new ScriptedRandom()));
    }

    [Fact]
    public void GetWarSupport_AgainstADefencelessEnemy_IsASingleTokenUnit()
    {
        (Game game, _) = FoundedColony();
        List<Player> rivals = ColonialRivals(game);
        Player enemy = rivals[0];
        ParkLandUnits(game, rivals[^1].PlayerId); // enemy now defenceless; human also none → ratio 0 → p ≥ 1 (support)

        var force = game.GetWarSupport(enemy, new ScriptedRandom()); // defenceless ⇒ no count roll
        ForceEntry block = Assert.Single(force);
        Assert.Equal(1, block.Count); // a token single unit
    }

    [Fact]
    public void DeclareWar_WithSupport_LandsTheForceInEurope_AndAddsTheGold()
    {
        (Game game, var colony) = FoundedColony();
        int human = game.HumanPlayer.PlayerId;
        List<Player> rivals = ColonialRivals(game);
        Player target = rivals[0];
        Player park = rivals[^1];
        ParkLandUnits(game, park.PlayerId); // clear the board so the ratio is controlled
        foreach (Player p in rivals)
        {
            game.SetStance(human, p.PlayerId, Stance.War);
        }
        game.SetStance(human, target.PlayerId, Stance.Peace); // exactly one peace-standing target
        GiveSoldiers(game, target.PlayerId, 2, colony.Position); // target not defenceless, human none → ratio 0 → p ≥ 1
        int goldBefore = game.HumanPlayer.Gold;
        int veteransBefore = game.UnitsInEurope.Count(u => u.Type.Id == VeteranSoldier);

        // Draws: [0] target pick (1 eligible → any), [1] force count roll (+0 → 4), [2] gold variance (→ +0).
        game.DispatchMonarchAction(MonarchAction.DeclareWar, new ScriptedRandom(0, 1, 2));

        Assert.Equal(Stance.War, game.StanceBetween(human, target.PlayerId));
        Assert.Equal(veteransBefore + 4, game.UnitsInEurope.Count(u => u.Type.Id == VeteranSoldier));
        Assert.Equal(goldBefore + 1500, game.HumanPlayer.Gold); // 1500 + 150·(2 − 2) = 1500
    }

    [Fact]
    public void WarSupportAmounts_FollowTheRuleset()
    {
        // A modified ruleset proves the force size + gold are data-driven, not hardcoded.
        Game game = FoundedGame(EditedClassicRuleset(medium =>
        {
            medium.Descendants("unitOption").First(o => (string?)o.Attribute("id") == "model.option.warSupportForce.veteranSoldier")
                .Element("number")!.SetAttributeValue("value", "9");
            medium.Descendants("integerOption").First(o => (string?)o.Attribute("id") == "model.option.warSupportGold")
                .SetAttributeValue("value", "999");
        }));
        MonarchOptions mon = game.Ruleset.Difficulty.Monarch;
        Assert.Equal(9, Assert.Single(mon.WarSupportForce).Number);
        Assert.Equal(999, mon.WarSupportGold);
    }

    // ── Pending monarch demand persistence (86d3c9rk6, save v46) ────────────────────────────────────────────

    private static Game RoundTrip(Game game) =>
        CrownAndColony.GameLogic.Persistence.SaveGame.FromJson(
            CrownAndColony.GameLogic.Persistence.SaveGame.From(game).ToJson()).Restore(Classic);

    [Fact]
    public void PendingTaxDemand_SurvivesSaveLoad_AndCanBeAnsweredAfterLoading()
    {
        (Game game, var colony) = FoundedColony();
        colony.AddGoods("model.goods.furs", 100);
        game.HumanPlayer.TaxRate = 10;
        game.DispatchMonarchAction(MonarchAction.RaiseTaxAct, new ScriptedRandom(2)); // demand: raise to 13
        PendingMonarchDemand original = game.PendingMonarchDemand!;

        Game loaded = RoundTrip(game);

        PendingMonarchDemand restored = loaded.PendingMonarchDemand!;
        Assert.NotNull(restored);
        Assert.Equal(original.Action, restored.Action);
        Assert.Equal(original.TaxRaise, restored.TaxRaise);
        Assert.Equal(original.GoodsId, restored.GoodsId);
        Assert.Equal(original.ColonyId, restored.ColonyId);
        Assert.Equal(original.GoodsAmount, restored.GoodsAmount);

        // The demand is answerable after the reload exactly as before — accept raises the tax to the demanded rate.
        loaded.RespondToMonarch(accept: true);
        Assert.Equal(13, loaded.HumanPlayer.TaxRate);
        Assert.Null(loaded.PendingMonarchDemand);
    }

    [Fact]
    public void PendingMercenaryOffer_SurvivesSaveLoad_WithItsForceAndPrice()
    {
        Game game = FoundedGame();
        game.HumanPlayer.Gold = 100_000;
        game.DispatchMonarchAction(MonarchAction.MonarchMercenaries, new ScriptedRandom(0, 0, 0, 0, 0, 0, 0, 0));
        PendingMonarchDemand offer = game.PendingMonarchDemand!;
        Assert.NotEmpty(offer.Offer!);

        Game loaded = RoundTrip(game);

        PendingMonarchDemand restored = loaded.PendingMonarchDemand!;
        Assert.Equal(offer.Action, restored.Action);
        Assert.Equal(offer.Price, restored.Price);
        Assert.Equal(
            offer.Offer!.Select(e => (e.UnitTypeId, e.RoleId, e.Count)),
            restored.Offer!.Select(e => (e.UnitTypeId, e.RoleId, e.Count)));

        // Accepting the restored offer still spends the gold and lands the veterans in Europe.
        int veterans = restored.Offer!.Sum(e => e.Count);
        loaded.RespondToMonarch(accept: true);
        Assert.Equal(veterans, loaded.UnitsInEurope.Count(u => u.Type.Id == "model.unit.veteranSoldier"));
    }

    [Fact]
    public void NoPendingDemand_OmitsTheToken_AndStaysByteStable()
    {
        // A game with no open demand (the common case) writes no PendingMonarchDemand token → byte-identical to v45.
        string json = CrownAndColony.GameLogic.Persistence.SaveGame.From(FoundedGame()).ToJson();
        Assert.DoesNotContain("\"PendingMonarchDemand\"", json);
        Assert.Equal(50, CrownAndColony.GameLogic.Persistence.SaveGame.CurrentVersion);
    }

    [Fact]
    public void PreV46Save_LoadsWithNoPendingDemand()
    {
        (Game game, var colony) = FoundedColony();
        colony.AddGoods("model.goods.furs", 100);
        game.DispatchMonarchAction(MonarchAction.RaiseTaxAct, new ScriptedRandom(2)); // a demand is pending
        // Simulate an old save: drop the token + back-date the version.
        CrownAndColony.GameLogic.Persistence.SaveGame v45 =
            CrownAndColony.GameLogic.Persistence.SaveGame.From(game) with { Version = 45, PendingMonarchDemand = null };
        Game loaded = CrownAndColony.GameLogic.Persistence.SaveGame.FromJson(v45.ToJson()).Restore(Classic);
        Assert.Null(loaded.PendingMonarchDemand);
    }
}
