using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Trade;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Trade;

public class MarketTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    private const string Sugar = "model.goods.sugar";
    private const string Silver = "model.goods.silver";
    private const string Food = "model.goods.food";
    private const string Furs = "model.goods.furs";
    private const string TradeBonus = "model.modifier.tradeBonus";

    [Theory]
    // bid (sell) / ask (buy) seeds, verified directly against the classic spec.
    [InlineData("model.goods.food", 1, 9)]
    [InlineData("model.goods.sugar", 2, 4)]
    [InlineData("model.goods.tobacco", 2, 4)]
    [InlineData("model.goods.ore", 4, 7)]
    [InlineData("model.goods.silver", 16, 18)]
    [InlineData("model.goods.rum", 10, 12)]
    [InlineData("model.goods.muskets", 1, 3)]
    public void InitialPrices_MatchClassicSpec(string goodsId, int bid, int ask)
    {
        var market = new Market(Classic);
        Assert.Equal(bid, market.BidPrice(goodsId));
        Assert.Equal(ask, market.AskPrice(goodsId));
    }

    [Fact]
    public void NonTradeableGoods_AreNotInTheMarket()
    {
        var market = new Market(Classic);
        Assert.False(market.IsTradeable("model.goods.grain")); // stored-as food, not traded
        Assert.False(market.IsTradeable("model.goods.bells"));
        Assert.True(market.IsTradeable(Food));
    }

    [Fact]
    public void Selling_RaisesInventory_AndEventuallyDropsTheBid()
    {
        // Hand-computed: sugar starts amount 1500, bid 2. The supply formula keeps
        // bid at 2 until inventory passes ~2000; selling 600 (six 100-chunks) ends
        // with inventory 2100 and bid 1. At 0% tax the player receives 600×2 = 1200.
        var market = new Market(Classic);

        SaleResult sale = market.Sell(Sugar, 600, taxPercent: 0);

        Assert.Equal(1200, sale.GoldAfterTax);
        Assert.Equal(1200, sale.GoldBeforeTax);
        Assert.Equal(2100, market.AmountInMarket(Sugar));
        Assert.Equal(1, market.BidPrice(Sugar)); // dropped 2 → 1
    }

    [Fact]
    public void SalesTax_IsIntegerTruncatedFromRevenue()
    {
        // Silver's price is stable for a small sale (amount 500 → 507 keeps bid 16).
        // Sell 7 silver: revenue 112; at 33% tax → (67×112)/100 = 75.04 → 75.
        var market = new Market(Classic);

        SaleResult sale = market.Sell(Silver, 7, taxPercent: 33);

        Assert.Equal(112, sale.GoldBeforeTax);
        Assert.Equal(75, sale.GoldAfterTax);
    }

    [Fact]
    public void Prices_NeverLeaveTheHardBounds()
    {
        var market = new Market(Classic);

        // Dump a huge amount of every tradeable good; bid must floor at 1, ask cap at 19.
        foreach (string goodsId in market.TradeableGoods.ToList())
        {
            market.Sell(goodsId, 100_000, taxPercent: 0);
            Assert.InRange(market.BidPrice(goodsId), 1, 19);
            Assert.InRange(market.AskPrice(goodsId), 1, 19);
            Assert.True(market.BidPrice(goodsId) >= 1);
        }
    }

    [Fact]
    public void NewWorldGoods_SalePriceIsCapped()
    {
        // Sugar (a New World good) can't have its sell price pushed above
        // initialPrice + 2 = 4 — even though buying isn't modelled to do that,
        // the cap is part of the recompute. Selling only lowers it, so we assert
        // the bid never exceeds the cap across a long sell-down.
        var market = new Market(Classic);
        for (int i = 0; i < 50; i++)
        {
            market.Sell(Sugar, 100, taxPercent: 0);
            Assert.True(market.BidPrice(Sugar) <= 4, "New World sell price exceeded its cap");
        }
    }

    [Fact]
    public void SellColonyGoods_DeductsStores_CreditsTreasury_AppliesTax()
    {
        var game = Game.New(Classic, seed: 7, startingGold: 100, startingTax: 50);
        game.FoundColony(game.Units[0]);
        var colony = game.Colonies[0];
        colony.AddGoods(Silver, 10);

        int credited = game.SellColonyGoods(colony, Silver, 10);

        // 10 silver × bid 16 = 160 revenue; 50% tax → 80 credited; stores emptied.
        Assert.Equal(80, credited);
        Assert.Equal(180, game.Gold); // 100 starting + 80
        Assert.Equal(0, colony.StoreOf(Silver));
    }

    [Fact]
    public void SellColonyGoods_RejectsUntradeableOrInsufficient()
    {
        var game = Game.New(Classic, seed: 7);
        game.FoundColony(game.Units[0]);
        var colony = game.Colonies[0];

        Assert.Throws<InvalidMoveException>(() => game.SellColonyGoods(colony, "model.goods.grain", 1));
        Assert.Throws<InvalidMoveException>(() => game.SellColonyGoods(colony, Silver, 5)); // none stored
    }

    [Fact]
    public void SaveRoundTrip_PreservesGoldTaxAndMovedMarket()
    {
        var game = Game.New(Classic, seed: 7, startingGold: 500, startingTax: 25);
        game.FoundColony(game.Units[0]);
        game.Colonies[0].AddGoods(Sugar, 600);
        game.SellColonyGoods(game.Colonies[0], Sugar, 600); // moves sugar's market

        Game loaded = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);

        Assert.Equal(game.Gold, loaded.Gold);
        Assert.Equal(game.TaxRate, loaded.TaxRate);
        Assert.Equal(game.Market.AmountInMarket(Sugar), loaded.Market.AmountInMarket(Sugar));
        Assert.Equal(game.Market.BidPrice(Sugar), loaded.Market.BidPrice(Sugar));
    }

    [Fact]
    public void PreV9Save_LoadsWithZeroGold_AndSeededMarket()
    {
        var game = Game.New(Classic, seed: 7, startingGold: 999, startingTax: 30);
        SaveGame v8 = SaveGame.From(game) with { Version = 8, Gold = 0, Tax = 0, MarketState = null };

        Game loaded = SaveGame.FromJson(v8.ToJson()).Restore(Classic);

        Assert.Equal(0, loaded.Gold);
        Assert.Equal(0, loaded.TaxRate);
        Assert.Equal(2, loaded.Market.BidPrice(Sugar)); // reseeded from ruleset
    }

    // ── Dutch trade advantage (86d3...): model.modifier.tradeBonus −50% halves the market's absorption of a sale ─────

    [Fact]
    public void Spec_ExactlyOneNationType_CarriesTheTradeBonus()
    {
        // The Dutch (model.nationType.trade) are the only nation with the trade advantage; its value is −50%.
        var withBonus = Classic.EuropeanNations.Where(n => n.NationType.Modifiers.Any(m => m.TargetId == TradeBonus)).ToList();
        Assert.Single(withBonus);
    }

    [Fact]
    public void Sell_WithATradeAdvantage_AbsorbsLessVolume_SoThePriceHoldsUp()
    {
        var plain = new Market(Classic);
        var dutch = new Market(Classic);

        plain.Sell(Furs, 300, taxPercent: 0);                      // ordinary: the market absorbs all 300
        dutch.Sell(Furs, 300, taxPercent: 0, volumeFactor: 0.5);   // Dutch: the market absorbs only 150

        Assert.True(dutch.AmountInMarket(Furs) < plain.AmountInMarket(Furs)); // strictly less absorbed
        Assert.True(dutch.BidPrice(Furs) >= plain.BidPrice(Furs));            // so the Dutch sell price held up
    }

    [Fact]
    public void Sell_VolumeFactorOne_IsByteIdenticalToTheDefault()
    {
        var a = new Market(Classic);
        var b = new Market(Classic);
        a.Sell(Furs, 250, taxPercent: 10);
        b.Sell(Furs, 250, taxPercent: 10, volumeFactor: 1.0); // the explicit default must match the implicit one
        Assert.Equal(a.AmountInMarket(Furs), b.AmountInMarket(Furs));
        Assert.Equal(a.BidPrice(Furs), b.BidPrice(Furs));
    }

    [Fact]
    public void DutchHuman_DepressesThePriceLessThanANoNationHuman()
    {
        int dutch = MarketAmountAfterColonySale(asDutch: true);
        int plain = MarketAmountAfterColonySale(asDutch: false);
        Assert.True(dutch < plain, $"the Dutch market absorbed {dutch}, a no-nation market {plain} — the advantage should absorb less");
    }

    // ── Buying out of the market lifts the price (86d3dkffz): the mirror of selling (FreeCol buyInEurope) ─────────

    [Fact]
    public void Buying_RemovesInventory_AndRaisesTheAsk()
    {
        var market = new Market(Classic);
        int askBefore = market.AskPrice(Furs);
        int amountBefore = market.AmountInMarket(Furs);

        market.Buy(Furs, 20_000); // drains the supply

        Assert.True(market.AmountInMarket(Furs) < amountBefore, "buying should drain the market's inventory");
        Assert.True(market.AskPrice(Furs) > askBefore, "draining the market should raise the ask price");
    }

    [Fact]
    public void Buying_IsChunked_SoALargeBuyCostsMoreThanAFlatAsk()
    {
        var market = new Market(Classic);
        int askBefore = market.AskPrice(Furs);

        int cost = market.Buy(Furs, 5000); // later chunks are dearer than the first

        Assert.True(cost > 5000 * askBefore, $"chunked cost {cost} should exceed the flat {5000 * askBefore}");
    }

    [Fact]
    public void Buying_FloorsInventory_AndClampsTheAsk_NoDivideByZero()
    {
        var market = new Market(Classic);
        market.Buy(Furs, 1_000_000); // would drain the supply to zero without the floor

        Assert.InRange(market.AskPrice(Furs), 1, 19); // clamped to the hard bounds, no overflow/throw
        Assert.True(market.AmountInMarket(Furs) > 0); // floored above zero
    }

    [Fact]
    public void BuyCost_EqualsBuy_ButLeavesTheMarketUntouched()
    {
        var market = new Market(Classic);
        int amountBefore = market.AmountInMarket(Furs);
        int askBefore = market.AskPrice(Furs);

        int quoted = market.BuyCost(Furs, 800);
        Assert.Equal(amountBefore, market.AmountInMarket(Furs)); // a preview moves nothing
        Assert.Equal(askBefore, market.AskPrice(Furs));

        int actual = market.Buy(Furs, 800);
        Assert.Equal(quoted, actual); // the preview matched the real charge
    }

    [Fact]
    public void Buy_VolumeFactorOne_IsByteIdenticalToTheDefault()
    {
        var a = new Market(Classic);
        var b = new Market(Classic);
        int ca = a.Buy(Furs, 250);
        int cb = b.Buy(Furs, 250, volumeFactor: 1.0); // the explicit default must match the implicit one
        Assert.Equal(ca, cb);
        Assert.Equal(a.AmountInMarket(Furs), b.AmountInMarket(Furs));
        Assert.Equal(a.AskPrice(Furs), b.AskPrice(Furs));
    }

    [Fact]
    public void Buy_WithATradeAdvantage_DrainsLess_SoThePriceRisesLess()
    {
        var plain = new Market(Classic);
        var dutch = new Market(Classic);

        plain.Buy(Furs, 300);                      // ordinary: the market loses all 300
        dutch.Buy(Furs, 300, volumeFactor: 0.5);   // Dutch: the market loses only 150

        Assert.True(dutch.AmountInMarket(Furs) > plain.AmountInMarket(Furs)); // strictly less drained
        Assert.True(dutch.AskPrice(Furs) <= plain.AskPrice(Furs));            // so the Dutch buy price rose less
    }

    // ── Per-good trade accounting (86d3e4bdm): cumulative Sales / IncomeBeforeTaxes / IncomeAfterTaxes for the Trade report ──

    [Fact]
    public void Selling_AccumulatesTheThreeCounters_AcrossATaxChange()
    {
        // Silver: amount 500, bid 16; a 7-unit sale leaves the bid at 16 (round(16×500/507) = 16), so each batch
        // is priced identically. Two batches, the second taxed, exercise the FreeCol sign convention:
        //   modifySales(+a), modifyIncomeBeforeTaxes(+revenue), modifyIncomeAfterTaxes(+post-tax gold) per chunk.
        var market = new Market(Classic);
        Assert.Equal(0, market.SalesOf(Silver)); // untraded → all three start at 0
        Assert.Equal(0, market.IncomeBeforeTaxesOf(Silver));
        Assert.Equal(0, market.IncomeAfterTaxesOf(Silver));

        market.Sell(Silver, 7, taxPercent: 0);   // +7 units, +112 before tax, +112 after tax
        market.Sell(Silver, 7, taxPercent: 33);  // +7 units, +112 before tax, +75 after tax ((67×112)/100 = 75)

        Assert.Equal(14, market.SalesOf(Silver));            // 7 + 7
        Assert.Equal(224, market.IncomeBeforeTaxesOf(Silver)); // 112 + 112 (tax never touches the pre-tax total)
        Assert.Equal(187, market.IncomeAfterTaxesOf(Silver));  // 112 + 75 (the second batch lost 37 to tax)
    }

    [Fact]
    public void Buying_DecrementsTheCounters_FreeColSignConvention()
    {
        // A buy is a NEGATIVE sale (FreeCol buyInEurope negates modifySales/modifyIncome*): it subtracts the units and
        // the cost from all three running totals; FreeCol charges no tax on a buy, so the same cost comes off both incomes.
        var market = new Market(Classic);
        int askBefore = market.AskPrice(Furs); // first chunk priced here

        int cost = market.Buy(Furs, 50); // a single sub-chunk buy (≤100) → priced wholly at the opening ask
        Assert.Equal(50 * askBefore, cost);

        Assert.Equal(-50, market.SalesOf(Furs));               // net units sold went negative
        Assert.Equal(-cost, market.IncomeBeforeTaxesOf(Furs)); // cost subtracted from net pre-tax income
        Assert.Equal(-cost, market.IncomeAfterTaxesOf(Furs));  // …and the same cost off net after-tax income (no buy tax)
    }

    [Fact]
    public void BuyCost_DoesNotTouchTheCounters()
    {
        // BuyCost is a non-mutating quote on a throwaway copy — it must leave the live trade accounting untouched.
        var market = new Market(Classic);
        market.BuyCost(Furs, 800);
        Assert.Equal(0, market.SalesOf(Furs));
        Assert.Equal(0, market.IncomeBeforeTaxesOf(Furs));
        Assert.Equal(0, market.IncomeAfterTaxesOf(Furs));
    }

    [Fact]
    public void SellThenBuy_NetsTheCounters()
    {
        // Sell then buy the same good: the counters read NET (a sell adds, a buy subtracts), so the units net toward zero.
        var market = new Market(Classic);
        market.Sell(Furs, 100, taxPercent: 0);
        int salesAfterSell = market.SalesOf(Furs);
        Assert.Equal(100, salesAfterSell);

        market.Buy(Furs, 40);
        Assert.Equal(60, market.SalesOf(Furs)); // 100 sold − 40 bought back = 60 net units
    }

    [Fact]
    public void TradeAccounting_SurvivesSaveLoad_RoundTrip()
    {
        // L2: the three counters round-trip through a save/load. Sell silver from a colony at a tax, so the human's
        // market carries non-zero Sales/IncomeBeforeTaxes/IncomeAfterTaxes, then save and restore.
        var game = Game.New(Classic, seed: 7, startingGold: 100, startingTax: 25);
        game.FoundColony(game.Units[0]);
        Colony colony = game.Colonies[0];
        colony.AddGoods(Silver, 30);
        game.SellColonyGoods(colony, Silver, 30); // moves silver's market AND its trade accounting

        int sales = game.Market.SalesOf(Silver);
        int before = game.Market.IncomeBeforeTaxesOf(Silver);
        int after = game.Market.IncomeAfterTaxesOf(Silver);
        Assert.Equal(30, sales);
        Assert.True(before > 0 && after > 0 && after < before); // taxed: after-tax strictly below pre-tax

        Game loaded = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);

        Assert.Equal(sales, loaded.Market.SalesOf(Silver));
        Assert.Equal(before, loaded.Market.IncomeBeforeTaxesOf(Silver));
        Assert.Equal(after, loaded.Market.IncomeAfterTaxesOf(Silver));
    }

    [Fact]
    public void DefaultGame_OmitsTradeAccounts_AndIsByteIdentical()
    {
        // Omit-when-default proof: a fresh game has traded nothing, so the per-good TradeAccounts map is omitted —
        // the v56 save is byte-identical to the same game serialized as a v55 save with the field cleared.
        var game = Game.New(Classic, seed: 7);
        string json = SaveGame.From(game).ToJson();

        Assert.DoesNotContain("TradeAccounts", json); // the field is omitted entirely when nothing has been traded

        // A v55 save of the untraded game (TradeAccounts cleared) is byte-identical to the v56 save bar the version int.
        SaveGame v56 = SaveGame.From(game);
        SaveGame asV55 = v56 with
        {
            Version = 55,
            Players = v56.Players!.Select(p => p with { TradeAccounts = null }).ToList(),
        };
        Assert.Equal(asV55.ToJson().Replace("\"Version\": 55", "\"Version\": 56"), v56.ToJson());
    }

    [Fact]
    public void PreV56Save_LoadsWithZeroedCounters()
    {
        // A pre-v56 save carries no TradeAccounts → every counter loads at 0.
        var game = Game.New(Classic, seed: 7, startingGold: 100);
        game.FoundColony(game.Units[0]);
        game.Colonies[0].AddGoods(Silver, 20);
        game.SellColonyGoods(game.Colonies[0], Silver, 20);

        SaveGame pre = SaveGame.From(game) with
        {
            Version = 55,
            Players = SaveGame.From(game).Players!.Select(p => p with { TradeAccounts = null }).ToList(),
        };
        Game loaded = SaveGame.FromJson(pre.ToJson()).Restore(Classic);

        Assert.Equal(0, loaded.Market.SalesOf(Silver)); // counters absent → 0 (the market inventory still round-trips)
        Assert.Equal(0, loaded.Market.IncomeBeforeTaxesOf(Silver));
    }

    /// <summary>Founds a colony for a human (Dutch or no-nation), sells 600 furs from it, and returns the market's resulting absorbed volume.</summary>
    private static int MarketAmountAfterColonySale(bool asDutch)
    {
        SaveGame save = SaveGame.From(Game.New(Classic, seed: 42));
        string? nation = asDutch
            ? Classic.EuropeanNations.First(n => n.NationType.Modifiers.Any(m => m.TargetId == TradeBonus)).Id
            : null;
        Game game = (save with
        {
            Players = save.Players!.Select(p => p.IsHuman ? p with { NationId = nation } : p).ToList(),
        }).Restore(Classic);

        Colony colony = game.FoundColony(game.Units[0]);
        colony.AddGoods(Furs, 600);
        game.SellColonyGoods(colony, Furs, 600);
        return game.HumanPlayer.Market.AmountInMarket(Furs);
    }
}
