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
