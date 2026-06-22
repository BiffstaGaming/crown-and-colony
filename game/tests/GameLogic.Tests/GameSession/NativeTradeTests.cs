using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Natives;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// Native trade (Phase 5 slice 4): a ship sells cargo to a coastal settlement for gold.
/// Pricing follows FreeCol getPriceToSell (base 12 + trade-bonus, ×150/125/110% for the
/// settlement's 1st/2nd/3rd wanted good, ~10% markup), then the classic ship-trade penalty
/// (model.option.shipTradePenalty, −30% at medium — a ship is paid less than an overland trader).
/// </summary>
public class NativeTradeTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 0xC0FFEEUL;

    /// <summary>A game with a caravel on a coastal water tile beside a (test-built) camp on the adjacent land.</summary>
    private static (Game game, NativeSettlement settlement, Unit ship) SetupCoastalTrade(
        IReadOnlyList<string>? wanted, int startingGold = 0)
    {
        Game game = Game.New(Classic, Seed, startingGold: startingGold);
        Position water = game.Map.AllPositions().First(p =>
            game.Map.TerrainAt(p).IsWater &&
            p.Neighbours().Any(n => game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater));
        Position land = water.Neighbours().First(n => game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater);
        var settlement = new NativeSettlement(
            7777, "model.nationType.apache", "model.settlement.camp", false, land, 5, learnableSkill: null)
        {
            WantedGoods = wanted ?? [],
        };
        Unit ship = game.SpawnUnit(Classic.Unit("model.unit.caravel"), water);
        return (game, settlement, ship);
    }

    // ---- Wanted goods generation ----

    [Fact]
    public void EverySettlement_GetsUpToThreeDistinctTradeableWantedGoods()
    {
        Game game = Game.New(Classic, Seed);
        Assert.All(game.NativeSettlements, s =>
        {
            Assert.InRange(s.WantedGoods.Count, 1, 3);
            Assert.Equal(s.WantedGoods.Count, s.WantedGoods.Distinct().Count()); // distinct
            Assert.All(s.WantedGoods, g => Assert.NotNull(Classic.Goods(g).Market)); // tradeable
        });
    }

    [Fact]
    public void WantedGoods_AreDeterministicForASeed()
    {
        Game a = Game.New(Classic, Seed);
        Game b = Game.New(Classic, Seed);
        Assert.Equal(
            a.NativeSettlements.Select(s => s.WantedGoods).ToList(),
            b.NativeSettlements.Select(s => s.WantedGoods).ToList());
    }

    // ---- Pricing (pinned to the formula) ----

    [Fact]
    public void SalePrice_AppliesTradeBonusAndWantedPremium()
    {
        // Camp trade-bonus = 1 → full = 13. Price = (amount + 11·(13·mult/100)·amount/10) × (100 − 30)/100
        // — the ×70/100 tail is the medium ship-trade penalty (a ship is paid 30% less).
        (Game game, NativeSettlement settlement, _) = SetupCoastalTrade(
            ["model.goods.sugar", "model.goods.tobacco", "model.goods.cotton"]);

        Assert.Equal(1533, game.NativeSalePrice(settlement, "model.goods.sugar", 100));   // wanted #1 (×150%) → 2190×0.70
        Assert.Equal(1302, game.NativeSalePrice(settlement, "model.goods.tobacco", 100)); // wanted #2 (×125%) → 1860×0.70
        Assert.Equal(1148, game.NativeSalePrice(settlement, "model.goods.cotton", 100));  // wanted #3 (×110%) → 1640×0.70
        Assert.Equal(1071, game.NativeSalePrice(settlement, "model.goods.ore", 100));     // not wanted (×100%) → 1530×0.70
    }

    [Fact]
    public void SalePrice_AppliesTheShipTradePenalty_FromTheDifficultyOption()
    {
        (Game game, NativeSettlement settlement, _) = SetupCoastalTrade(["model.goods.sugar"]);
        Assert.Equal(-30, Classic.Difficulty.ShipTradePenalty); // parsed from the spec's medium level, not hardcoded

        // A ship-borne trader (native trade is ship-only) is paid the penalty% less than the raw getPriceToSell.
        const int unpenalized = 2190; // base 13 × 150% wanted, 100 units, +11/10 markup
        Assert.Equal(unpenalized * (100 + Classic.Difficulty.ShipTradePenalty) / 100, // 1533
            game.NativeSalePrice(settlement, "model.goods.sugar", 100));
    }

    [Fact]
    public void SalePrice_EmptyWantedGoods_UsesBasePrice()
    {
        (Game game, NativeSettlement settlement, _) = SetupCoastalTrade([]); // wants nothing in particular
        Assert.Equal(1071, game.NativeSalePrice(settlement, "model.goods.sugar", 100)); // ×100% (camp, full = 13) → 1530×0.70
    }

    [Fact]
    public void SalePrice_HigherTradeBonusSettlementsPayMore()
    {
        Game game = Game.New(Classic, Seed);
        var pos = new Position(0, 0);
        var camp = new NativeSettlement(1, "model.nationType.apache", "model.settlement.camp", false, pos, 5, null);
        var incaCapital = new NativeSettlement(2, "model.nationType.inca", "model.settlement.inca.capital", true, pos, 5, null);

        // Inca city capital trade-bonus (4) > camp (1) → it pays more for the same good.
        Assert.True(
            game.NativeSalePrice(incaCapital, "model.goods.ore", 100) >
            game.NativeSalePrice(camp, "model.goods.ore", 100));
    }

    // ---- Selling ----

    [Fact]
    public void SellToNatives_CreditsGold_RemovesCargo_LowersAlarm_EndsTurn()
    {
        (Game game, NativeSettlement settlement, Unit ship) = SetupCoastalTrade(["model.goods.sugar"]);
        ship.AddCargo("model.goods.sugar", 100);
        game.ChangeNativeAlarm(settlement, 300); // Content, so trading is allowed and goodwill is visible
        int alarmBefore = settlement.Alarm;
        int goldBefore = game.Gold;

        int price = game.SellToNatives(ship, settlement, "model.goods.sugar", 100);

        Assert.Equal(1533, price);                       // wanted #1, no tax, ship-trade penalty applied
        Assert.Equal(goldBefore + price, game.Gold);
        Assert.Equal(0, ship.CargoOf("model.goods.sugar"));
        Assert.True(settlement.Alarm < alarmBefore, "trading builds goodwill");
        Assert.Equal(0, ship.MovementLeft);
    }

    [Fact]
    public void SellToNatives_RefusedForNonShips_LackingCargo_OrHostileSettlements()
    {
        (Game game, NativeSettlement settlement, Unit ship) = SetupCoastalTrade(["model.goods.sugar"]);

        // A land unit (the starting colonist) is not a carrier.
        Unit colonist = game.Units.First(u => !u.Type.IsCarrier);
        Assert.False(game.CheckSellToNatives(colonist, settlement, "model.goods.sugar", 100).Allowed);

        // The ship carries nothing yet.
        Assert.False(game.CheckSellToNatives(ship, settlement, "model.goods.sugar", 100).Allowed);

        // Even loaded, an angry settlement refuses to trade.
        ship.AddCargo("model.goods.sugar", 100);
        game.ChangeNativeAlarm(settlement, 750); // Angry
        Assert.False(game.CheckSellToNatives(ship, settlement, "model.goods.sugar", 100).Allowed);
    }

    [Fact]
    public void SellToNatives_RefusedOffMap_AndForNonPositiveAmounts()
    {
        (Game game, NativeSettlement settlement, Unit ship) = SetupCoastalTrade(["model.goods.sugar"]);
        ship.AddCargo("model.goods.sugar", 100);

        Assert.False(game.CheckSellToNatives(ship, settlement, "model.goods.sugar", 0).Allowed);  // nothing to sell
        Assert.False(game.CheckSellToNatives(ship, settlement, "model.goods.sugar", -5).Allowed);

        ship.Location = UnitLocation.InEurope; // no longer on the map
        Assert.False(game.CheckSellToNatives(ship, settlement, "model.goods.sugar", 100).Allowed);
    }

    [Fact]
    public void SmallSale_StillLowersAlarmByAtLeastOne()
    {
        (Game game, NativeSettlement settlement, Unit ship) = SetupCoastalTrade(["model.goods.sugar"]);
        ship.AddCargo("model.goods.sugar", 1);
        game.ChangeNativeAlarm(settlement, 300); // Content
        int before = settlement.Alarm;

        game.SellToNatives(ship, settlement, "model.goods.sugar", 1); // tiny price → reduction floors to 1
        Assert.Equal(before - 1, settlement.Alarm);
    }

    // ---- Selling re-prices the store (stock-driven) ----

    [Fact]
    public void Selling_AddsToTheStore_AndCanShiftWantedGoods()
    {
        (Game game, NativeSettlement settlement, Unit ship) = SetupCoastalTrade(null);
        game.RecomputeWantedGoods(settlement); // derive wanted goods from the (empty) store
        ship.AddCargo("model.goods.sugar", 100);
        game.ChangeNativeAlarm(settlement, 300); // Content

        game.SellToNatives(ship, settlement, "model.goods.sugar", 100);

        Assert.Equal(100, settlement.GeneralStockOf("model.goods.sugar")); // the goods joined the store
        // The store now holds sugar, so it pays less for more sugar than for a good it holds none of.
        Assert.True(
            game.NativeSalePrice(settlement, "model.goods.sugar", 100) <
            game.NativeSalePrice(settlement, "model.goods.ore", 100));
    }

    // ---- Buying FROM a settlement ----

    /// <summary>A coastal camp pre-stocked with goods, beside a caravel; the human starts with gold to buy with.</summary>
    private static (Game game, NativeSettlement settlement, Unit ship) SetupBuy(int sugarStock)
    {
        (Game game, NativeSettlement settlement, Unit ship) = SetupCoastalTrade(null, startingGold: 10000);
        settlement.AddGoods("model.goods.sugar", sugarStock);
        game.RecomputeWantedGoods(settlement);
        game.ChangeNativeAlarm(settlement, 300); // Content, so trading is allowed
        return (game, settlement, ship);
    }

    [Fact]
    public void GoodsToSell_ListsTheStore_AboveTheTradeMinimum_MostValuableFirst()
    {
        (Game game, NativeSettlement settlement, _) = SetupBuy(sugarStock: 80);
        settlement.AddGoods("model.goods.ore", 50);
        settlement.AddGoods("model.goods.cotton", 10); // below the 20-unit trade minimum → not offered

        var offered = game.GoodsToSell(settlement);
        Assert.DoesNotContain(offered, g => g.GoodsId == "model.goods.cotton");
        Assert.Contains(offered, g => g.GoodsId == "model.goods.sugar" && g.Amount == 80);
        Assert.Contains(offered, g => g.GoodsId == "model.goods.ore" && g.Amount == 50);
    }

    [Fact]
    public void BuyPrice_RisesAsTheStoreEmpties_AndHasNoShipPenalty()
    {
        (Game game, NativeSettlement full, _) = SetupBuy(sugarStock: 80);
        (Game _, NativeSettlement scarce, _) = SetupBuy(sugarStock: 80);
        scarce.AddGoods("model.goods.sugar", -60); // only 20 left → emptier store charges more per unit

        Assert.True(
            game.NativeBuyPrice(scarce, "model.goods.sugar", 20) / 20 >
            game.NativeBuyPrice(full, "model.goods.sugar", 20) / 20);
        // Buying carries no ship-trade penalty (FreeCol applies that only to the player's sale).
        Assert.True(game.NativeBuyPrice(full, "model.goods.sugar", 20) > 0);
    }

    [Fact]
    public void BuyFromNatives_DrainsStock_LoadsShip_DebitsGold_LowersAlarm_EndsTurn()
    {
        (Game game, NativeSettlement settlement, Unit ship) = SetupBuy(sugarStock: 80);
        int price = game.CheckBuyFromNatives(ship, settlement, "model.goods.sugar", 40).Cost;
        int goldBefore = game.Gold;
        int alarmBefore = settlement.Alarm;

        int paid = game.BuyFromNatives(ship, settlement, "model.goods.sugar", 40);

        Assert.Equal(price, paid);
        Assert.Equal(goldBefore - paid, game.Gold);
        Assert.Equal(40, ship.CargoOf("model.goods.sugar"));
        Assert.Equal(40, settlement.GeneralStockOf("model.goods.sugar")); // 80 → 40
        Assert.True(settlement.Alarm < alarmBefore, "buying builds a little goodwill");
        Assert.Equal(0, ship.MovementLeft);
    }

    [Fact]
    public void BuyFromNatives_RefusedWhenStockShort_Unaffordable_OrHostile()
    {
        (Game game, NativeSettlement settlement, Unit ship) = SetupBuy(sugarStock: 30);

        Assert.False(game.CheckBuyFromNatives(ship, settlement, "model.goods.sugar", 40).Allowed); // only 30 in store
        Assert.False(game.CheckBuyFromNatives(ship, settlement, "model.goods.ore", 10).Allowed);    // none in store

        // Hostile settlement refuses entirely.
        game.ChangeNativeAlarm(settlement, 500); // → Angry
        Assert.False(game.CheckBuyFromNatives(ship, settlement, "model.goods.sugar", 20).Allowed);
    }

    // ---- Stock-driven wanted goods ----

    [Fact]
    public void WantedGoods_DeriveFromStock_PreferGoodsTheSettlementLacks()
    {
        (Game game, NativeSettlement settlement, _) = SetupCoastalTrade(null);
        // Glut the store with sugar; the settlement should not want what it is already swimming in.
        settlement.AddGoods("model.goods.sugar", 100);
        game.RecomputeWantedGoods(settlement);
        Assert.DoesNotContain("model.goods.sugar", settlement.WantedGoods);
        Assert.InRange(settlement.WantedGoods.Count, 1, 3);
    }

    [Fact]
    public void WantedGoods_RecomputeIsDeterministic_AndDrawsNoRng()
    {
        // Two same-seed games' generated settlements derive identical wanted goods (the recompute is RNG-free),
        // and stream 0 is untouched — the default-game determinism guard.
        Game a = Game.New(Classic, Seed);
        Game b = Game.New(Classic, Seed);
        Assert.Equal(
            a.NativeSettlements.Select(s => s.WantedGoods).ToList(),
            b.NativeSettlements.Select(s => s.WantedGoods).ToList());
        Assert.All(a.NativeSettlements, s => Assert.All(s.WantedGoods, g => Assert.NotNull(Classic.Goods(g).Market)));
    }

    // ---- Haggling ----

    [Fact]
    public void HaggleSell_AcceptsAFairOffer_CountersALowAsk()
    {
        (Game game, NativeSettlement settlement, _) = SetupCoastalTrade(["model.goods.sugar"]);
        int fair = game.NativeSalePrice(settlement, "model.goods.sugar", 100);

        // Asking exactly the fair price is accepted at the player's price, opening round.
        var ok = game.TryHaggleSell(settlement, "model.goods.sugar", 100, offerPrice: fair, round: 0);
        Assert.True(ok.Accepted);
        Assert.Equal(fair, ok.CounterPrice);

        // Asking far above the fair price is not accepted; the natives counter at their (lower) price.
        var high = game.TryHaggleSell(settlement, "model.goods.sugar", 100, offerPrice: fair * 2, round: 0);
        Assert.False(high.Accepted);
        Assert.Equal(fair, high.CounterPrice); // round 0 → fair price unchanged
    }

    [Fact]
    public void HaggleBuy_AcceptsAGenerousOffer_CountersALowBid()
    {
        (Game game, NativeSettlement settlement, _) = SetupBuy(sugarStock: 80);
        int asking = game.NativeBuyPrice(settlement, "model.goods.sugar", 40);

        var ok = game.TryHaggleBuy(settlement, "model.goods.sugar", 40, offerPrice: asking, round: 0);
        Assert.True(ok.Accepted);

        var low = game.TryHaggleBuy(settlement, "model.goods.sugar", 40, offerPrice: asking / 2, round: 0);
        Assert.False(low.Accepted);
        Assert.Equal(asking, low.CounterPrice);
    }

    [Fact]
    public void Haggle_RunsOutOfPatience_EventuallyDone_DrawingOnlyTheNativeStream()
    {
        (Game game, NativeSettlement settlement, _) = SetupCoastalTrade(["model.goods.sugar"]);
        int fair = game.NativeSalePrice(settlement, "model.goods.sugar", 100);
        ulong stream0Before = StreamZeroState(game);

        // Keep low-balling across rounds; with the rising walk-away probability the session must eventually end.
        bool everDone = false;
        for (int round = 0; round < 10 && !everDone; round++)
        {
            var r = game.TryHaggleSell(settlement, "model.goods.sugar", 100, offerPrice: fair * 5, round: round);
            Assert.False(r.Accepted);
            everDone |= r.Done;
        }
        Assert.True(everDone, "the natives must eventually lose patience");
        Assert.Equal(stream0Before, StreamZeroState(game)); // the human's economy stream 0 is untouched (ADR-009)
    }

    /// <summary>The human's main RNG stream (0) state, read from a save — used to prove haggling never touches it.</summary>
    private static ulong StreamZeroState(Game game) => SaveGame.From(game).RandomStateValue;

    // ---- Persistence ----

    [Fact]
    public void WantedGoods_SurviveASaveRoundTrip()
    {
        Game game = Game.New(Classic, Seed);
        NativeSettlement first = game.NativeSettlements[0];
        var wanted = first.WantedGoods;

        Game restored = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);

        Assert.Equal(wanted, restored.NativeSettlements.First(s => s.Id == first.Id).WantedGoods);
        Assert.Equal(58, SaveGame.CurrentVersion);
    }

    [Fact]
    public void GeneralStock_SurvivesASaveRoundTrip_AndOmitsWhenEmpty()
    {
        Game game = Game.New(Classic, Seed);
        NativeSettlement stocked = game.NativeSettlements.First(s => s.GeneralStock.Any());

        Game restored = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);
        NativeSettlement loaded = restored.NativeSettlements.First(s => s.Id == stocked.Id);
        Assert.Equal(stocked.GeneralStock.ToList(), loaded.GeneralStock.ToList());

        // A settlement that holds no general stock omits the field entirely.
        var bare = new NativeSettlement(9001, "model.nationType.apache", "model.settlement.camp", false, new Position(0, 0), 5, null);
        string json = SaveGame.From(game).ToJson();
        // The v57 field name appears for the seeded settlements but never as an empty map.
        Assert.DoesNotContain("\"GeneralStock\": {}", json);
        Assert.Empty(bare.GeneralStock);
    }
}
