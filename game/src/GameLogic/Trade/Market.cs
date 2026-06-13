using CrownAndColony.GameLogic.Specification;

namespace CrownAndColony.GameLogic.Trade;

/// <summary>
/// The European market: a supply-and-demand price model for every tradeable good,
/// ported faithfully from FreeCol's <c>MarketData.price()</c>. Each good has a
/// running inventory (<see cref="AmountInMarket"/>); the more of a good the market
/// holds, the lower its sell (bid) price. Selling adds to the inventory and pushes
/// the price down; the spread between bid and ask is fixed per good.
/// </summary>
public sealed class Market
{
    /// <summary>Goods are sold in batches of this size so a big sale can't crash the price in one jump.</summary>
    public const int CargoChunk = 100;

    private const int MinimumPrice = 1;
    private const int MaximumPrice = 19;

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
    /// <returns>The pre-tax and post-tax gold for the whole sale.</returns>
    public SaleResult Sell(string goodsId, int amount, int taxPercent)
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
            d.AmountInMarket += chunk;               // …then the market absorbs the goods…
            Recompute(d);                            // …and the price falls for the next chunk.
            remaining -= chunk;
        }
        return new SaleResult(beforeTax, afterTax);
    }

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
