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

}
