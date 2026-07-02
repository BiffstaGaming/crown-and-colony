using CrownAndColony.GameLogic.Randomness;
using CrownAndColony.GameLogic.Trade;

namespace CrownAndColony.GameLogic.GameSession;

public sealed partial class Game
{
    /// <summary>
    /// RNG stream reserved for the <b>market dynamics</b> rolls (86d3fpyx3 rival-market propagation +
    /// 86d3fpyyq per-turn drift) — a high id above every per-player stream, like <c>LcrStreamId</c>, so the market
    /// rolls never correlate with or shift the human's economy stream 0 (ADR-009). Both features share this one id:
    /// their generators are <b>transient</b> (one per propagation event / per player drift tick, disaster pattern),
    /// seeded only from persisted inputs — the trading player's own stream <em>state</em> (read via
    /// <c>SaveState()</c> without advancing it), the turn, the player id and (for propagation) the traded good and
    /// its post-trade counters — so a save/load replays them identically with no new save field.
    /// </summary>
    private const ulong MarketDynamicsStreamId = 105;

    /// <summary>
    /// 64-bit FNV-1a over a ruleset id — the stable string hash for RNG seed mixing (ADR-009: never
    /// <c>string.GetHashCode()</c>, which is randomized per process). Ruleset ids are ASCII, so hashing each char
    /// narrowed to a byte is well-defined and platform-stable.
    /// </summary>
    private static ulong Fnv1a(string text)
    {
        ulong hash = 14695981039346656037UL; // FNV offset basis
        foreach (char c in text)
        {
            hash = unchecked((hash ^ (byte)c) * 1099511628211UL); // FNV prime
        }
        return hash;
    }

    /// <summary>
    /// The seed for one trade's propagation generator, built from persisted inputs only (disaster pattern — replays
    /// identically after a save/load): the trader's own stream <em>state</em> (read without advancing), the turn, the
    /// trader id, the traded good, and the good's <b>post-trade</b> cumulative counters — the just-completed trade has
    /// already moved <c>SalesOf</c>/<c>IncomeBeforeTaxesOf</c>, so two same-good trades in one turn seed differently.
    /// The two counters fold in under <b>different</b> odd multipliers rather than a shared hash: with one function a
    /// good whose bid is 1 keeps <c>Sales == IncomeBeforeTaxes</c> forever and the two terms would XOR-cancel to 0,
    /// collapsing repeat same-good trades onto one seed.
    /// </summary>
    private ulong PropagationSeed(Player trader, string goodsId) =>
        RandomFor(trader).SaveState().State
        ^ ((ulong)Turn << 1)
        ^ ((ulong)(uint)trader.PlayerId << 32)
        ^ Fnv1a(goodsId)
        ^ unchecked((ulong)(uint)trader.Market.SalesOf(goodsId) * 0x9E3779B97F4A7C15UL)
        ^ unchecked((ulong)(uint)trader.Market.IncomeBeforeTaxesOf(goodsId) * 0xC2B2AE3D27D4EB4FUL);

    /// <summary>
    /// The European players whose markets feel <paramref name="trader"/>'s Europe trades — FreeCol
    /// <c>Game.getLiveEuropeanPlayerList(this)</c>: every European player (<c>Player.isEuropean()</c>, Player.java:761-766
    /// = COLONIAL, REBEL, INDEPENDENT or ROYAL — the <b>REF included</b>) except the trader itself. FreeCol also
    /// filters dead and unknown-enemy players; we model neither (no player-death model yet — "live" is type-based for
    /// now), and <see cref="PlayerType.Native"/>/<see cref="PlayerType.Retired"/> are not European, so they never
    /// receive. Stable player-ring order.
    /// </summary>
    private List<Player> RivalEuropeanMarketsOf(Player trader) =>
        _players.Where(p => p != trader && p.PlayerType is PlayerType.Colonial or PlayerType.Rebel
            or PlayerType.Independent or PlayerType.RoyalExpeditionaryForce).ToList();

    /// <summary>
    /// Propagates one player's Europe trade into every rival European market (FreeCol
    /// <c>ServerPlayer.propagateToEuropeanMarkets</c>, ServerPlayer.java:1734-1755, called per ≤100 chunk by
    /// <c>sellInEurope</c>:1327 / <c>buyInEurope</c>:1261): when a player sells (or buys) in Europe, every rival's
    /// market absorbs (or loses) a random 5–30% of it, so heavy trading moves everyone's prices a little.
    /// <paramref name="amount"/> is the <b>raw</b> traded amount, signed — positive for a sell (rivals' inventory
    /// grows, their price falls), negative for a buy (FreeCol negates it at buyInEurope:1261); FreeCol propagates the
    /// raw chunk, <em>not</em> the Dutch <c>TRADE_BONUS</c>-scaled amount, so a Dutch trade propagates at full size.
    /// Mirroring FreeCol's caller-side chunking, the amount is split into ≤<see cref="Market.CargoChunk"/> chunks and
    /// each chunk rolls its own 5–30% (uniform over the 26 values, ServerPlayer.java:1740-1744) with Java-style
    /// integer truncation toward zero — so a trade of 3 or less always propagates nothing (3×30/100 = 0). Non-storable
    /// goods never propagate (ServerPlayer.java:1736). Recipients take the goods with <b>no</b> trade-accounting
    /// change (<see cref="Market.AddGoodsToMarket"/> — no sale happened on their market).
    /// <para>
    /// <b>ADR-009:</b> every roll comes from a transient stream-<see cref="MarketDynamicsStreamId"/> generator seeded
    /// from persisted inputs only (<see cref="PropagationSeed"/>) — the human's stream 0 and each AI's own stream are
    /// read (<c>SaveState()</c>) but never advanced, so seeded games stay byte-stable and a save/load replays the same
    /// propagation. The rival deltas land in already-persisted market state (<c>SaveDeltas</c>), so no save bump.
    /// </para>
    /// <para>
    /// <b>Documented divergences</b> (docs/systems/market.md): FreeCol's server double-propagates a human buy
    /// (<c>InGameController.buyGoods</c>:1050 re-propagates the whole total after <c>buyInEurope</c> already
    /// propagated per chunk — asymmetric with the sell path, judged a FreeCol bug) and a custom-house sale
    /// (<c>ServerColony.java:853-855</c> queues the sale total as an extra trade on top of <c>sellInEurope</c>'s
    /// per-chunk propagation — same family). We propagate every trade exactly once.
    /// </para>
    /// </summary>
    /// <param name="trader">The player whose Europe trade just completed (its own market is already moved).</param>
    /// <param name="goodsId">The good traded.</param>
    /// <param name="amount">The raw units traded: positive = sold to Europe, negative = bought from Europe.</param>
    internal void PropagateTradeToRivalMarkets(Player trader, string goodsId, int amount)
    {
        if (amount == 0 || !Ruleset.Goods(goodsId).IsStorable)
        {
            return; // FreeCol ServerPlayer.java:1736: only storable goods move markets
        }
        List<Player> recipients = RivalEuropeanMarketsOf(trader);
        if (recipients.Count == 0)
        {
            return; // nobody to feel it (the generator is transient, so skipping its rolls is invisible)
        }
        var rng = new Pcg32Random(PropagationSeed(trader, goodsId), MarketDynamicsStreamId);
        int remaining = Math.Abs(amount);
        int sign = Math.Sign(amount);
        while (remaining > 0)
        {
            int chunk = Math.Min(remaining, Market.CargoChunk); // caller-side chunking in FreeCol's sell/buyInEurope
            remaining -= chunk;
            PropagateToMarkets(recipients, goodsId, sign * chunk, rng);
        }
    }

    /// <summary>
    /// The propagation core — FreeCol <c>propagateToEuropeanMarkets</c>'s body for one already-chunked amount: roll
    /// one uniform 5–30% (the roll happens <b>before</b> the zero cutoff, ServerPlayer.java:1742-1747), scale with
    /// integer truncation toward zero (Java and C# agree), and if anything survives add it to every recipient's
    /// market. Also (from 86d3fpyyq) the single-roll path for a drift adjust's onward propagation
    /// (<c>flushExtraTrades</c>, ServerPlayer.java:366-371 — one roll per queued adjust, unchunked, as FreeCol).
    /// </summary>
    private static void PropagateToMarkets(List<Player> recipients, string goodsId, int amount, IGameRandom rng)
    {
        int r = rng.Next(26) + 5;    // uniform 5..30 inclusive (26 values), rolled before the cutoff
        int part = amount * r / 100; // Java int division: truncates toward zero for either sign
        if (part == 0)
        {
            return;
        }
        foreach (Player recipient in recipients)
        {
            recipient.Market.AddGoodsToMarket(goodsId, part);
        }
    }

    /// <summary>
    /// Runs one European player's per-turn market drift (FreeCol <c>ServerPlayer.csYearlyGoodsAdjust</c>,
    /// ServerPlayer.java:1767-1800, called from <c>csStartTurn</c>:1813 for <b>every European player every turn</b> —
    /// the "yearly" in FreeCol's name is historical, not a real once-a-year gate): each good the player has actually
    /// traded (<see cref="Market.HasBeenTraded"/>) gets a small random nudge back toward its ruleset baseline, so a
    /// flooded market slowly recovers and a drained one refills. Per traded good, in the market's stable spec order:
    /// <list type="bullet">
    /// <item>direction: <b>add</b> when the inventory is strictly below its seed (<see cref="Market.InitialAmountOf"/>),
    /// else subtract — at <em>exactly</em> the seed it subtracts (FreeCol's <c>&lt;</c> at :1783, a faithful quirk);</item>
    /// <item>magnitude bound: <c>Turn / 10</c> (integer division — no drift at all before turn 10), except the one
    /// uniformly-drawn <b>extra type</b> whose bound is <c>2 × (Turn / 10) + 1</c> (:1785-1786); a bound ≤ 0 skips the
    /// good; the draw is uniform 0..bound−1 (:1788), so a drift tick can be 0;</item>
    /// <item>the adjust lands via <see cref="Market.AddGoodsToMarket"/> (no counters move) and is queued;</item>
    /// <item>finally every queued adjust propagates onward to the rival European markets with one 5–30% roll each
    /// (<c>flushExtraTrades</c>:366-371 → <c>propagateToEuropeanMarkets</c>) from the same generator — even a 0
    /// adjust rolls (the roll precedes the cutoff), exactly as FreeCol.</item>
    /// </list>
    /// The extra-type draw runs over the market's goods list, which in the classic spec is <b>exactly</b> FreeCol's
    /// storable-goods list (every good with a <c>&lt;market&gt;</c> is storable and vice versa); on a hypothetical
    /// ruleset where a storable good had no market the draw pool would differ (documented in docs/systems/market.md).
    /// <para>
    /// <b>ADR-009:</b> one transient stream-<see cref="MarketDynamicsStreamId"/> generator per player per turn, seeded
    /// disaster-style from persisted inputs only (the player's own stream state read without advancing, the turn and
    /// the player id) — the human's stream 0 never advances, and a save/load replays the same tick. All movement lands
    /// in already-persisted market state, so no save bump. Called (once wired) from <c>RunPlayerTurn</c> right after
    /// <c>BombardEnemyShips</c>, matching FreeCol's <c>csStartTurn</c> order (bombard → yearly adjust → fathers); the
    /// REF branch returns before the colonial path, so the REF's market does not drift — a documented divergence
    /// (the REF never trades or reads prices, so its market is inert anyway).
    /// </para>
    /// </summary>
    /// <param name="player">The European (colonial/rebel/independent) player whose market drifts this turn.</param>
    internal void RunYearlyMarketAdjust(Player player)
    {
        Market market = player.Market;
        List<string> goods = market.TradeableGoods.ToList(); // classic: == FreeCol's storable list, in spec order
        if (goods.Count == 0)
        {
            return;
        }
        ulong baseState = RandomFor(player).SaveState().State; // read-only: never advances the underlying stream
        var rng = new Pcg32Random(
            baseState ^ ((ulong)Turn << 1) ^ ((ulong)(uint)player.PlayerId << 32), MarketDynamicsStreamId);

        string extraType = goods[rng.Next(goods.Count)]; // one uniform draw (ServerPlayer.java:1777-1778)
        var extraTrades = new List<(string GoodsId, int Amount)>();
        foreach (string goodsId in goods)
        {
            if (!market.HasBeenTraded(goodsId))
            {
                continue; // only goods this player has actually traded drift (ServerPlayer.java:1781-1782)
            }
            bool add = market.AmountInMarket(goodsId) < market.InitialAmountOf(goodsId); // at exactly initial: subtract
            int bound = Turn / 10; // int division, as FreeCol's turn/10
            if (goodsId == extraType)
            {
                bound = 2 * bound + 1;
            }
            if (bound <= 0)
            {
                continue;
            }
            int amount = rng.Next(bound); // uniform 0..bound-1 (can be 0 — FreeCol still applies + queues it)
            if (!add)
            {
                amount = -amount;
            }
            market.AddGoodsToMarket(goodsId, amount);
            extraTrades.Add((goodsId, amount));
        }
        // flushExtraTrades (ServerPlayer.java:366-371): each queued adjust propagates onward to the rival European
        // markets — one 5-30% roll per adjust from the same generator, unchunked and even for a 0 adjust (the roll
        // precedes the cutoff), exactly as FreeCol's propagateToEuropeanMarkets is called once per queued trade.
        List<Player> recipients = RivalEuropeanMarketsOf(player);
        foreach ((string goodsId, int amount) in extraTrades)
        {
            PropagateToMarkets(recipients, goodsId, amount, rng);
        }
    }
}
