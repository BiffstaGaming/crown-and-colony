using CrownAndColony.GameLogic.Trade;

namespace CrownAndColony.GameLogic.GameSession;

public sealed partial class Game
{
    /// <summary>
    /// Re-derives the per-turn <see cref="PriceChangeNotices"/> and re-baselines the price snapshot, mirroring FreeCol's
    /// <c>ServerPlayer.csFlushMarket</c> (called once per turn). For every human-market good whose buy (ask) price now
    /// differs from the baseline (<see cref="_priceSnapshot"/> — the prices as the human <em>last saw them</em>, i.e. the
    /// values recorded at the end of the previous turn), emits one <see cref="PriceChangeNotice"/> carrying the old/new
    /// ask and the current sell (bid) price (FreeCol <c>Market.makePriceChangeMessage</c>'s <c>%buy%</c>/<c>%sell%</c>),
    /// then resets the baseline to the current prices (FreeCol <c>flushPriceChange</c> → <c>setOldPrice(costToBuy)</c>) so
    /// the next turn compares against today's. The window therefore spans the <b>whole turn</b> — the human's own Europe
    /// sells/buys made before End Turn <em>and</em> the AI-phase market activity — exactly once per turn.
    /// </summary>
    /// <remarks>
    /// Walks the human market's <b>stable goods order</b>, so the notice sequence is deterministic. The baseline is
    /// seeded at game creation / load (<see cref="SeedPriceBaseline"/>), so the very first turn's trades are caught (a
    /// turn-1 Europe sell compares the dropped price against the ruleset seed). <b>RNG-free</b> (ADR-009): it compares two
    /// recorded prices and copies prices, drawing no randomness, so the human's stream stays byte-stable whether or not
    /// the UI reads the notices.
    /// </remarks>
    private void DetectMarketPriceChanges()
    {
        Market market = Market;
        foreach (string goodsId in market.TradeableGoods)
        {
            int newAsk = market.AskPrice(goodsId);
            if (_priceSnapshot.TryGetValue(goodsId, out int oldAsk) && newAsk != oldAsk)
            {
                _priceChangeNotices.Add(new PriceChangeNotice(goodsId, oldAsk, newAsk, market.BidPrice(goodsId)));
            }
            _priceSnapshot[goodsId] = newAsk; // re-baseline (FreeCol flushPriceChange): next turn compares against today's price
        }
    }

    /// <summary>
    /// Seeds the price-change baseline to the human market's <b>current</b> buy (ask) prices — the prices the player is
    /// considered to have last seen (FreeCol's <c>oldPrice</c> initialised to <c>costToBuy</c>). Called once at game
    /// creation (the ruleset seed prices) and on load (the restored, already-moved prices), so the first
    /// <see cref="EndTurn"/> compares against a real baseline rather than reporting every good as "changed". On load this
    /// makes the reload silent for prices that have not moved since the save — a reloaded game does not re-announce old
    /// changes. <b>RNG-free</b>.
    /// </summary>
    internal void SeedPriceBaseline()
    {
        Market market = Market;
        _priceSnapshot.Clear();
        foreach (string goodsId in market.TradeableGoods)
        {
            _priceSnapshot[goodsId] = market.AskPrice(goodsId);
        }
    }
}
