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
}
