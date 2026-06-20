using CrownAndColony.GameLogic.Specification;

namespace CrownAndColony.GameLogic.Trade;

/// <summary>
/// The European market: a supply-and-demand price model for every tradeable good,
/// ported faithfully from FreeCol's <c>MarketData.price()</c>. Each good has a
/// running inventory (<see cref="AmountInMarket"/>); the more of a good the market
/// holds, the lower its sell (bid) price. <b>Selling</b> adds to the inventory and pushes
/// the price down; <b>buying</b> removes from it and pushes the price up (FreeCol
/// <c>addGoodsToMarket(type, ±amount)</c>); the spread between bid and ask is fixed per good.
/// </summary>
public sealed class Market
{
    /// <summary>Goods are traded in batches of this size so a big sale/purchase can't crash/spike the price in one jump.</summary>
    public const int CargoChunk = 100;

    private const int MinimumPrice = 1;
    private const int MaximumPrice = 19;

    /// <summary>The market never holds less than this of a good (FreeCol <c>MINIMUM_AMOUNT</c>) — so heavy buying can't drive the supply to zero (a divide-by-zero in <see cref="Recompute"/>).</summary>
    private const int MinimumAmountInMarket = 100;

    private sealed class Datum
    {
        public required GoodsType Goods { get; init; }
        public required bool NewWorldCapped { get; init; }
        public int AmountInMarket { get; set; }
        public int Bid { get; set; }  // paidForSale — what the player receives
        public int Ask { get; set; }  // costToBuy — what the player pays
    }

    private readonly Dictionary<string, Datum> _data = [];

    /// <summary>Builds a market seeded from the ruleset's per-good market data.</summary>
    public Market(Ruleset ruleset)
    {
        foreach (GoodsType goods in ruleset.GoodsTypes)
        {
            if (goods.Market is not { } m)
            {
                continue;
            }
            // New World goods, and goods refined from them, have their price rises capped.
            bool capped = goods.IsNewWorldGoods
                || (goods.MadeFrom is { } raw && ruleset.GoodsTypes
                        .FirstOrDefault(g => g.Id == raw)?.IsNewWorldGoods == true);
            _data[goods.Id] = new Datum
            {
                Goods = goods,
                NewWorldCapped = capped,
                AmountInMarket = m.InitialAmount,
                Bid = m.InitialPrice,
                Ask = m.InitialAskPrice,
            };
        }
    }

    /// <summary>All tradeable goods ids, in ruleset order.</summary>
    public IEnumerable<string> TradeableGoods => _data.Keys;

    /// <summary>Whether a good can be traded.</summary>
    public bool IsTradeable(string goodsId) => _data.ContainsKey(goodsId);

    /// <summary>The current sell (bid) price per unit — what the player receives. 0 if untradeable.</summary>
    public int BidPrice(string goodsId) => _data.TryGetValue(goodsId, out var d) ? d.Bid : 0;

    /// <summary>The current buy (ask) price per unit — what the player pays. 0 if untradeable.</summary>
    public int AskPrice(string goodsId) => _data.TryGetValue(goodsId, out var d) ? d.Ask : 0;

    /// <summary>The inventory currently in the European market for a good.</summary>
    public int AmountInMarket(string goodsId) => _data.TryGetValue(goodsId, out var d) ? d.AmountInMarket : 0;

    /// <summary>
    /// Sells goods into the European market, applying <paramref name="taxPercent"/>
    /// to the revenue and pushing the price down as the inventory grows. Sales are
    /// chunked (<see cref="CargoChunk"/>) so each batch is priced after the previous
    /// one moved the market.
    /// </summary>
    /// <param name="goodsId">The good being sold.</param>
    /// <param name="amount">How much to sell.</param>
    /// <param name="taxPercent">The tax withheld from the revenue.</param>
    /// <param name="volumeFactor">
    /// How much of each chunk the market absorbs (FreeCol <c>Modifier.TRADE_BONUS</c>): 1.0 normally, 0.5 for the
    /// Dutch <c>model.nationType.trade</c> advantage (−50%), so their price falls half as fast as they sell. The
    /// seller still receives the full chunk revenue — only the market's price-moving inventory is scaled.
    /// </param>
    /// <returns>The pre-tax and post-tax gold for the whole sale.</returns>
    public SaleResult Sell(string goodsId, int amount, int taxPercent, double volumeFactor = 1.0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        if (!_data.TryGetValue(goodsId, out Datum? d))
        {
            throw new ArgumentException($"'{goodsId}' is not tradeable.", nameof(goodsId));
        }

        int beforeTax = 0, afterTax = 0;
        int remaining = amount;
        while (remaining > 0)
        {
            int chunk = Math.Min(remaining, CargoChunk);
            int chunkRevenue = chunk * d.Bid;        // priced at the current bid…
            beforeTax += chunkRevenue;
            afterTax += (100 - taxPercent) * chunkRevenue / 100; // integer truncation, as FreeCol
            d.AmountInMarket += (int)MathF.Round(chunk * (float)volumeFactor, MidpointRounding.AwayFromZero); // …the market absorbs the goods (a trade advantage absorbs less)…
            Recompute(d);                            // …and the price falls for the next chunk.
            remaining -= chunk;
        }
        return new SaleResult(beforeTax, afterTax);
    }

    /// <summary>
    /// Buys goods out of the European market, pushing the price <b>up</b> as the inventory shrinks (FreeCol
    /// <c>buyInEurope</c> → <c>addGoodsToMarket(type, −amount)</c>). Buys are chunked (<see cref="CargoChunk"/>) so each
    /// batch is priced after the previous one moved the market, and the inventory floors at
    /// <see cref="MinimumAmountInMarket"/> so a huge purchase can't drain the supply to zero.
    /// </summary>
    /// <param name="goodsId">The good being bought.</param>
    /// <param name="amount">How much to buy.</param>
    /// <param name="volumeFactor">
    /// How much of each chunk the market loses (FreeCol <c>Modifier.TRADE_BONUS</c>): 1.0 normally, 0.5 for the Dutch
    /// trade advantage (−50%), so their buying lifts the price half as fast. The buyer still pays the full chunk price.
    /// </param>
    /// <returns>The gold cost of the whole purchase.</returns>
    public int Buy(string goodsId, int amount, double volumeFactor = 1.0) =>
        ChunkedBuy(DatumOf(goodsId), amount, volumeFactor);

    /// <summary>
    /// What <see cref="Buy"/> would charge for <paramref name="amount"/> right now, <b>without</b> moving the market —
    /// for the affordability check (the cost rises across chunks, so a flat ask × amount under-quotes a large buy).
    /// </summary>
    public int BuyCost(string goodsId, int amount, double volumeFactor = 1.0)
    {
        Datum d = DatumOf(goodsId);
        var preview = new Datum
        {
            Goods = d.Goods,
            NewWorldCapped = d.NewWorldCapped,
            AmountInMarket = d.AmountInMarket,
            Bid = d.Bid,
            Ask = d.Ask,
        };
        return ChunkedBuy(preview, amount, volumeFactor); // mutates the throwaway copy only
    }

    /// <summary>The chunked buy loop shared by <see cref="Buy"/> (on the live datum) and <see cref="BuyCost"/> (on a copy).</summary>
    private static int ChunkedBuy(Datum d, int amount, double volumeFactor)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        int cost = 0;
        int remaining = amount;
        while (remaining > 0)
        {
            int chunk = Math.Min(remaining, CargoChunk);
            cost += chunk * d.Ask; // priced at the current ask…
            d.AmountInMarket = Math.Max(
                d.AmountInMarket - (int)MathF.Round(chunk * (float)volumeFactor, MidpointRounding.AwayFromZero),
                MinimumAmountInMarket); // …buying removes supply (a trade advantage removes less)…
            Recompute(d);              // …and the price rises for the next chunk.
            remaining -= chunk;
        }
        return cost;
    }

    private Datum DatumOf(string goodsId) =>
        _data.TryGetValue(goodsId, out Datum? d)
            ? d
            : throw new ArgumentException($"'{goodsId}' is not tradeable.", nameof(goodsId));

    /// <summary>Recalculates a good's bid/ask from its inventory (FreeCol <c>MarketData.price()</c>).</summary>
    private static void Recompute(Datum d)
    {
        GoodsMarket m = d.Goods.Market!;
        int diff = m.PriceDifference;

        float amountPrice = m.InitialPrice * (m.InitialAmount / (float)d.AmountInMarket);
        int newSalePrice = (int)MathF.Round(amountPrice, MidpointRounding.AwayFromZero);
        int newPrice = newSalePrice + diff;

        // Cap price rises for New World goods and their manufactures.
        if (newSalePrice > m.InitialPrice + 2 && d.NewWorldCapped)
        {
            newSalePrice = m.InitialPrice + 2;
            newPrice = newSalePrice + diff;
        }

        // Limit how fast the price can move in a single recalculation, pushing the
        // inventory back to stay consistent (anti-exploit, FreeCol).
        if (d.Ask > 0)
        {
            if (newPrice > d.Ask + diff)
            {
                amountPrice -= newPrice - (d.Ask + diff);
                d.AmountInMarket = (int)MathF.Round(m.InitialAmount * (m.InitialPrice / amountPrice),
                    MidpointRounding.AwayFromZero);
                newPrice = d.Ask + diff;
            }
            else if (newPrice < d.Ask - diff)
            {
                amountPrice += (d.Ask - diff) - newPrice;
                d.AmountInMarket = (int)MathF.Round(m.InitialAmount * (m.InitialPrice / amountPrice),
                    MidpointRounding.AwayFromZero);
                newPrice = d.Ask - diff;
            }
            newSalePrice = newPrice - diff;
        }

        // Clamp to the hard bounds.
        if (newPrice > MaximumPrice)
        {
            newPrice = MaximumPrice;
            newSalePrice = newPrice - diff;
        }
        else if (newSalePrice < MinimumPrice)
        {
            newSalePrice = MinimumPrice;
            newPrice = newSalePrice + diff;
        }

        d.Bid = newSalePrice;
        d.Ask = newPrice;
    }

    private readonly Dictionary<string, int> _arrears = []; // per-good back-tax owed after a boycott (FreeCol MarketData.arrears); >0 = boycotted

    /// <summary>The back-tax owed on a boycotted good (FreeCol <c>getArrears</c>); 0 = freely tradeable.</summary>
    public int Arrears(string goodsId) => _arrears.GetValueOrDefault(goodsId);

    /// <summary>True when a good may be sold (not under boycott) — FreeCol <c>Player.canTrade</c>.</summary>
    public bool CanTrade(string goodsId) => Arrears(goodsId) == 0;

    /// <summary>Sets (or clears, when 0) the boycott arrears for a good — a tea party sets it, paying it lifts it.</summary>
    internal void SetArrears(string goodsId, int amount)
    {
        if (amount <= 0)
        {
            _arrears.Remove(goodsId);
        }
        else
        {
            _arrears[goodsId] = amount;
        }
    }

    /// <summary>The non-zero boycott arrears by good (for the save; empty when nothing is boycotted).</summary>
    internal IReadOnlyDictionary<string, int> SaveArrears() => new Dictionary<string, int>(_arrears);

    /// <summary>Restores boycott arrears from a save.</summary>
    internal void LoadArrears(IReadOnlyDictionary<string, int> arrears)
    {
        foreach ((string goodsId, int amount) in arrears)
        {
            SetArrears(goodsId, amount);
        }
    }

    /// <summary>
    /// Lifts every boycott on the market in one stroke, leaving prices and inventory untouched (FreeCol
    /// <c>model.event.boycottsLifted</c>): Jacob Fugger's election clears all back-tax arrears so the goods trade
    /// freely again — unlike <see cref="Reinitialise"/>, the price model is not reset.
    /// </summary>
    internal void LiftAllBoycotts() => _arrears.Clear();

    /// <summary>
    /// Resets the market to its ruleset baseline — clears every boycott and price/inventory drift (FreeCol
    /// <c>Player.reinitialiseMarket</c>): a new nation trades on a clean market on declaring independence.
    /// </summary>
    internal void Reinitialise()
    {
        _arrears.Clear();
        foreach (Datum d in _data.Values)
        {
            d.AmountInMarket = d.Goods.Market!.InitialAmount;
            d.Ask = -1; // disable the jump-clamp for the recompute (as LoadDeltas does)
            Recompute(d);
        }
    }

    /// <summary>Captures the inventory of every good whose market has moved from its seed.</summary>
    internal IReadOnlyDictionary<string, int> SaveDeltas() =>
        _data.Values
            .Where(d => d.AmountInMarket != d.Goods.Market!.InitialAmount)
            .ToDictionary(d => d.Goods.Id, d => d.AmountInMarket);

    /// <summary>Restores moved inventories (from a save) and recomputes their prices.</summary>
    internal void LoadDeltas(IReadOnlyDictionary<string, int> deltas)
    {
        foreach ((string goodsId, int amount) in deltas)
        {
            if (_data.TryGetValue(goodsId, out Datum? d))
            {
                d.AmountInMarket = amount;
                // Recompute without the jump-clamp (Ask <= 0 disables it), as FreeCol's update().
                d.Ask = -1;
                Recompute(d);
            }
        }
    }
}

/// <summary>The proceeds of a sale.</summary>
/// <param name="GoldBeforeTax">Revenue before sales tax.</param>
/// <param name="GoldAfterTax">Gold actually credited to the treasury.</param>
public readonly record struct SaleResult(int GoldBeforeTax, int GoldAfterTax);
