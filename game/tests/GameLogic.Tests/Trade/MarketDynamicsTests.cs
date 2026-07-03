using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Randomness;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Trade;

/// <summary>
/// Market dynamics (Parity Wave: 86d3fpyx3 rival-market trade propagation + 86d3fpyyq per-turn market drift), FreeCol
/// <c>ServerPlayer.propagateToEuropeanMarkets</c> / <c>csYearlyGoodsAdjust</c>.
/// <para>
/// The four <c>Game.cs</c> call-site seams <b>are wired</b> as of the wave integration:
/// <c>SellColonyGoods</c>/<c>SellShipCargo</c>/<c>BuyEuropeGoods</c> each call
/// <c>PropagateTradeToRivalMarkets</c> after the trade completes (FreeCol <c>sellInEurope:1327</c>/<c>buyInEurope:1261</c>),
/// and <c>RunPlayerTurn</c> calls <c>RunYearlyMarketAdjust</c> right after <c>BombardEnemyShips</c> (FreeCol
/// <c>csStartTurn:1813</c>). Most tests below still call <c>PropagateTradeToRivalMarkets</c> / <c>RunYearlyMarketAdjust</c>
/// <b>directly</b> for isolation / white-box pinning (asserting the exact seed-derived deltas); the live end-to-end
/// paths are covered by <see cref="Propagation_NeverTouchesStreamZero_OrTheTradersOwnMarketAndGold"/> (real
/// <c>SellColonyGoods</c>) and <see cref="EndTurn_RunsPropagationAndDriftThroughTheLivePath"/> (real
/// <c>RunPlayerTurn</c> tick). FreeCol's two double-propagation bugs (<c>InGameController.buyGoods:1050</c>;
/// <c>ServerColony:853-855</c>) are deliberately NOT copied — every trade propagates exactly once, so do not add a
/// second propagation call at any trade site.
/// </para>
/// </summary>
public class MarketDynamicsTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    private const string Sugar = "model.goods.sugar";
    private const string Silver = "model.goods.silver";
    private const string Hammers = "model.goods.hammers"; // storable="false" — never propagates

    /// <summary>The rival colonial powers (not the human, not natives) whose markets propagation must reach.</summary>
    private static List<Player> ColonialRivals(Game game) =>
        game.Players.Where(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial).ToList();

    // ── Rival-market trade propagation (86d3fpyx3, FreeCol ServerPlayer.propagateToEuropeanMarkets:1734-1755) ──────

    [Fact]
    public void HumanSale_MovesEveryRivalMarket_ByTheSameFiveToThirtyPercentOfTheChunk()
    {
        var game = Game.New(Classic, seed: 7);
        List<Player> rivals = ColonialRivals(game);
        Assert.Equal(3, rivals.Count); // the default game: human + 3 foreign powers
        int before = rivals[0].Market.AmountInMarket(Sugar);

        game.HumanPlayer.Market.Sell(Sugar, 100, taxPercent: 0);         // the seam order: the trade completes first…
        game.PropagateTradeToRivalMarkets(game.HumanPlayer, Sugar, 100); // …then its raw amount propagates

        // One ≤100 chunk → one 5-30% roll, shared by every recipient (FreeCol applies the same scaled amount to all).
        int delta = rivals[0].Market.AmountInMarket(Sugar) - before;
        Assert.InRange(delta, 5, 30);
        Assert.All(rivals, r => Assert.Equal(before + delta, r.Market.AmountInMarket(Sugar)));
        // Propagation is inventory-only: no recipient's trade accounting moves (Market.java:214-222 — no counters).
        Assert.All(rivals, r => Assert.Equal(0, r.Market.SalesOf(Sugar)));
        Assert.All(rivals, r => Assert.Equal(0, r.Market.IncomeBeforeTaxesOf(Sugar)));
    }

    [Fact]
    public void HumanBuy_PropagatesNegative_DrainingRivalMarkets()
    {
        var game = Game.New(Classic, seed: 7);
        Player rival = ColonialRivals(game)[0];
        int before = rival.Market.AmountInMarket(Silver);

        game.HumanPlayer.Market.Buy(Silver, 100);
        game.PropagateTradeToRivalMarkets(game.HumanPlayer, Silver, -100); // buyInEurope:1261 — the sign is negated

        int delta = rival.Market.AmountInMarket(Silver) - before;
        Assert.InRange(delta, -30, -5); // rivals lose 5-30% of the buy: their supply shrinks, their price firms up
    }

    [Fact]
    public void TinyTrade_ThreeUnitsOrLess_PropagatesNothing()
    {
        // 3 × 30 / 100 = 0 with integer truncation — the roll happens, the scaled amount dies at the zero cutoff
        // (ServerPlayer.java:1745-1747), so a trickle of trade never telegraphs to the rivals.
        var game = Game.New(Classic, seed: 7);
        game.HumanPlayer.Market.Sell(Sugar, 3, taxPercent: 0);
        game.PropagateTradeToRivalMarkets(game.HumanPlayer, Sugar, 3);

        Assert.All(ColonialRivals(game), r => Assert.Empty(r.Market.SaveDeltas()));
    }

    [Fact]
    public void NonStorableGoods_NeverPropagate()
    {
        // ServerPlayer.java:1736: if (!type.isStorable()) return — before any roll or market touch. Hammers are
        // storable="false" (and have no market), so this must be a clean no-op, not a throw.
        var game = Game.New(Classic, seed: 7);
        game.PropagateTradeToRivalMarkets(game.HumanPlayer, Hammers, 100);

        Assert.All(ColonialRivals(game), r => Assert.Empty(r.Market.SaveDeltas()));
    }

    [Fact]
    public void WorkedExample_LargeSale_DecomposesIntoChunksAndTruncates_ExactlyPerTheSeededFormula()
    {
        // White-box pin of the whole propagation formula: a 250-unit sale splits into chunks of 100/100/50
        // (FreeCol's caller-side chunking in sellInEurope), each chunk rolls its own uniform 5..30, and the 50-chunk
        // truncates (50×r₃/100 = r₃/2). The test reconstructs the event generator from the same persisted inputs the
        // engine uses — stream-0 state (read from the save, never advanced), turn, player id 0, the FNV-1a of the
        // goods id and the post-trade counters — so it computes the exact rival delta independently.
        var game = Game.New(Classic, seed: 7);
        List<Player> rivals = ColonialRivals(game);
        int before = rivals[0].Market.AmountInMarket(Sugar);

        game.HumanPlayer.Market.Sell(Sugar, 250, taxPercent: 0); // the trade completes (counters now post-trade)

        ulong seed = SaveGame.From(game).RandomStateValue // the human's stream-0 state word
            ^ ((ulong)game.Turn << 1)
            // ^ playerId 0 << 32 — the human's term is 0
            ^ Fnv1a(Sugar)
            ^ unchecked((ulong)(uint)game.Market.SalesOf(Sugar) * 0x9E3779B97F4A7C15UL)
            ^ unchecked((ulong)(uint)game.Market.IncomeBeforeTaxesOf(Sugar) * 0xC2B2AE3D27D4EB4FUL);
        var rng = new Pcg32Random(seed, 105); // MarketDynamicsStreamId
        int r1 = rng.Next(26) + 5;
        int r2 = rng.Next(26) + 5;
        int r3 = rng.Next(26) + 5;
        int expected = r1 + r2 + 50 * r3 / 100; // 100×r₁/100 + 100×r₂/100 + 50×r₃/100, each truncated

        game.PropagateTradeToRivalMarkets(game.HumanPlayer, Sugar, 250);

        Assert.InRange(expected, 12, 75); // sanity: 5+5+2 ≤ expected ≤ 30+30+15
        Assert.All(rivals, r => Assert.Equal(before + expected, r.Market.AmountInMarket(Sugar)));
    }

    [Fact]
    public void Propagation_IsDeterministic_TwinGamesAgree()
    {
        var a = Game.New(Classic, seed: 4242);
        var b = Game.New(Classic, seed: 4242);
        foreach (Game g in new[] { a, b })
        {
            g.HumanPlayer.Market.Sell(Sugar, 250, taxPercent: 0);
            g.PropagateTradeToRivalMarkets(g.HumanPlayer, Sugar, 250);
        }
        Assert.Equal(SaveGame.From(a).ToJson(), SaveGame.From(b).ToJson());
    }

    [Fact]
    public void NativesAndRetiredPlayers_NeverReceivePropagation()
    {
        // FreeCol's recipients are getLiveEuropeanPlayerList — isEuropean() (COLONIAL/REBEL/INDEPENDENT/ROYAL) only.
        // Natives own a Market instance in our engine but must never feel European trade; a Retired player is out of
        // the game entirely.
        var game = Game.New(Classic, seed: 7);
        Player native = game.Players.First(p => p.PlayerType == PlayerType.Native);
        Player retired = ColonialRivals(game)[0];
        retired.PlayerType = PlayerType.Retired;

        game.HumanPlayer.Market.Sell(Sugar, 100, taxPercent: 0);
        game.PropagateTradeToRivalMarkets(game.HumanPlayer, Sugar, 100);

        Assert.Empty(native.Market.SaveDeltas());  // untouched
        Assert.Empty(retired.Market.SaveDeltas()); // untouched
        Assert.All(ColonialRivals(game), r => Assert.NotEmpty(r.Market.SaveDeltas())); // the live rivals still moved
    }

    [Fact]
    public void TheRef_ReceivesPropagation_AfterIndependenceIsDeclared()
    {
        // Player.isEuropean() (Player.java:761-766) includes ROYAL — the King's market feels colonial trade too
        // (faithful, if inconsequential: the REF never trades or reads prices).
        Game game = RebellionReady(seed: 0xC0FFEEUL);
        game.DeclareIndependence(game.HumanPlayer);
        Player refPlayer = game.Players.Single(p => p.PlayerType == PlayerType.RoyalExpeditionaryForce);
        int before = refPlayer.Market.AmountInMarket(Sugar);

        game.HumanPlayer.Market.Sell(Sugar, 100, taxPercent: 0); // the rebel keeps trading
        game.PropagateTradeToRivalMarkets(game.HumanPlayer, Sugar, 100);

        int delta = refPlayer.Market.AmountInMarket(Sugar) - before;
        Assert.InRange(delta, 5, 30);
        // …and the still-colonial powers moved by the same shared roll.
        Assert.All(ColonialRivals(game), r => Assert.Equal(before + delta, r.Market.AmountInMarket(Sugar)));
    }

    [Fact]
    public void Propagation_NeverTouchesStreamZero_OrTheTradersOwnMarketAndGold()
    {
        // ADR-009 stream-isolation pin, post-seam form: SellColonyGoods now propagates IN-PATH (86d3fpyx3), so a
        // propagation-free twin no longer exists. The pinned properties are the same ones: the sale + its propagation
        // advance NO persisted RNG stream (the roll rides a transient generator), the whole operation is
        // twin-deterministic through the real path, and the ripple lands on the rivals only (the trader is excluded
        // by construction; the worked-example test pins the trader-side numbers).
        var game = Game.New(Classic, seed: 7, startingGold: 100, startingTax: 25);
        var twin = Game.New(Classic, seed: 7, startingGold: 100, startingTax: 25);
        foreach (Game g in new[] { game, twin })
        {
            g.FoundColony(g.Units[0]);
            g.Colonies[0].AddGoods(Silver, 30);
        }

        SaveGame before = SaveGame.From(game);
        foreach (Game g in new[] { game, twin })
        {
            g.SellColonyGoods(g.Colonies[0], Silver, 30); // real path — propagates via the seam
        }

        SaveGame gs = SaveGame.From(game);
        Assert.Equal(before.RandomStateValue, gs.RandomStateValue); // the human's stream 0 never advanced
        Assert.Equal(
            before.Players!.Select(p => (p.RngState, p.RngIncrement)),
            gs.Players!.Select(p => (p.RngState, p.RngIncrement))); // no AI stream advanced either

        // Twin-determinism through the propagating path: the entire game state, rivals included, is byte-identical.
        Assert.Equal(SaveGame.From(twin).ToJson(), gs.ToJson());

        // The propagation did land on every rival (30 × 5..30% ≥ 1).
        Assert.All(ColonialRivals(game), r => Assert.NotEmpty(r.Market.SaveDeltas()));
    }

    [Fact]
    public void PropagatedRivalMarkets_RoundTripThroughSave_ByteIdentically()
    {
        // The rival deltas land in already-persisted per-player market state (SaveDeltas) — no new save field — so a
        // propagated game must round-trip byte-identically.
        var game = Game.New(Classic, seed: 7);
        game.HumanPlayer.Market.Sell(Sugar, 600, taxPercent: 0);
        game.PropagateTradeToRivalMarkets(game.HumanPlayer, Sugar, 600);
        Assert.All(ColonialRivals(game), r => Assert.NotEmpty(r.Market.SaveDeltas()));

        string json = SaveGame.From(game).ToJson();
        Assert.Equal(json, SaveGame.From(SaveGame.FromJson(json).Restore(Classic)).ToJson());
    }

    [Fact]
    public void HugeNegativePropagation_FloorsRivalInventory_AndKeepsPricesInBounds()
    {
        // Market.AddGoodsToMarket floors at MINIMUM_AMOUNT = 100 (Market.java:217-219): even a colossal propagated
        // buy cannot drain a rival's supply to zero (no divide-by-zero in the recompute).
        var game = Game.New(Classic, seed: 7);
        Player rival = ColonialRivals(game)[0];

        game.PropagateTradeToRivalMarkets(game.HumanPlayer, Silver, -100_000);

        Assert.True(rival.Market.AmountInMarket(Silver) >= 100, "the floor must hold");
        Assert.InRange(rival.Market.AskPrice(Silver), 1, 19);
        Assert.InRange(rival.Market.BidPrice(Silver), 1, 19);
    }

    // ── Per-turn market drift (86d3fpyyq, FreeCol ServerPlayer.csYearlyGoodsAdjust:1767-1800) ──────────────────────

    private const string TradeGoods = "model.goods.tradeGoods"; // 3000 @ price 1 — drift never moves its price (no clamp noise)

    [Fact]
    public void Drift_MovesTradedGoodsTowardTheirBaseline_InBothDirections_WithoutTouchingStreamZero()
    {
        // Turn 200 → per-good bound 20 (extra type 41), so any single tick moves a good by at most 40. Sugar was
        // flooded above its seed (drift must subtract), tradeGoods drained below it (drift must add).
        Game game = AtTurn(200, seed: 7);
        game.HumanPlayer.Market.Sell(Sugar, 600, taxPercent: 0); // 1500 → 2100, above baseline
        game.HumanPlayer.Market.Buy(TradeGoods, 300);            // 3000 → 2700, below baseline
        int sugarBefore = game.Market.AmountInMarket(Sugar);
        int tradeBefore = game.Market.AmountInMarket(TradeGoods);
        ulong stream0Before = SaveGame.From(game).RandomStateValue;

        game.RunYearlyMarketAdjust(game.HumanPlayer);

        Assert.InRange(game.Market.AmountInMarket(Sugar), sugarBefore - 40, sugarBefore);      // toward (or at) the seed
        Assert.InRange(game.Market.AmountInMarket(TradeGoods), tradeBefore, tradeBefore + 40); // toward (or at) the seed
        Assert.Equal(stream0Before, SaveGame.From(game).RandomStateValue); // ADR-009: stream 0 never advanced
        // Drift moves inventory only — the trade counters (and so the Trade report) are untouched.
        Assert.Equal(600, game.Market.SalesOf(Sugar));
        Assert.Equal(-300, game.Market.SalesOf(TradeGoods));
    }

    [Fact]
    public void WorkedExample_DriftTick_IsByteExactAgainstAHandReplayOfTheFreeColFormula()
    {
        // Full-fidelity pin of csYearlyGoodsAdjust: the test replays the documented FreeCol algorithm by hand on a
        // twin game — one extra-type draw over the goods list, then per TRADED good (spec order): direction from
        // amount < initial (at EXACTLY initial it subtracts — FreeCol's quirky '<' at :1783), bound Turn/10
        // (2×that+1 for the extra type), a uniform 0..bound-1 draw, AddGoodsToMarket, queue; then one 5-30% roll per
        // queued adjust propagated to every rival (flushExtraTrades) — and demands the twin's save be BYTE-IDENTICAL
        // to the engine's. The setup covers all three directions: sugar above seed, tradeGoods below seed, and
        // silver traded back to exactly its seed (the '<' quirk: it must subtract, not add).
        Game game = AtTurn(200, seed: 11);
        Game twin = AtTurn(200, seed: 11);
        foreach (Game g in new[] { game, twin })
        {
            g.HumanPlayer.Market.Sell(Sugar, 600, taxPercent: 0); // above baseline
            g.HumanPlayer.Market.Buy(TradeGoods, 300);            // below baseline
            g.HumanPlayer.Market.Sell(Silver, 7, taxPercent: 0);  // …
            g.HumanPlayer.Market.Buy(Silver, 7);                  // …back to exactly baseline, counters non-zero
            Assert.Equal(g.Market.InitialAmountOf(Silver), g.Market.AmountInMarket(Silver));
        }

        game.RunYearlyMarketAdjust(game.HumanPlayer); // the engine…

        // …vs the hand replay on the twin, from the same persisted inputs (stream-0 state, turn, player id 0).
        var goods = twin.Market.TradeableGoods.ToList();
        var rng = new Pcg32Random(
            SaveGame.From(twin).RandomStateValue ^ ((ulong)twin.Turn << 1), 105); // MarketDynamicsStreamId
        string extraType = goods[rng.Next(goods.Count)];
        var extras = new List<(string GoodsId, int Amount)>();
        foreach (string g in goods)
        {
            if (!twin.Market.HasBeenTraded(g))
            {
                continue;
            }
            bool add = twin.Market.AmountInMarket(g) < twin.Market.InitialAmountOf(g);
            int bound = twin.Turn / 10;
            if (g == extraType)
            {
                bound = 2 * bound + 1;
            }
            if (bound <= 0)
            {
                continue;
            }
            int amount = rng.Next(bound);
            if (!add)
            {
                amount = -amount;
            }
            twin.Market.AddGoodsToMarket(g, amount);
            extras.Add((g, amount));
        }
        List<Player> rivals = ColonialRivals(twin);
        foreach ((string g, int amount) in extras)
        {
            int r = rng.Next(26) + 5;
            int part = amount * r / 100;
            if (part == 0)
            {
                continue;
            }
            foreach (Player rival in rivals)
            {
                rival.Market.AddGoodsToMarket(g, part);
            }
        }

        Assert.Equal(SaveGame.From(twin).ToJson(), SaveGame.From(game).ToJson()); // byte-identical, rivals included
    }

    [Fact]
    public void Drift_BeforeTurnTen_MovesNothing()
    {
        // bound = Turn/10 = 0 for every good; the extra type's bound is 1, whose only draw is 0 — so the early game
        // never drifts (and the 0-adjust's propagation roll scales to 0 for the rivals too).
        var game = Game.New(Classic, seed: 7); // turn 1
        game.HumanPlayer.Market.Sell(Sugar, 600, taxPercent: 0);
        int before = game.Market.AmountInMarket(Sugar);

        game.RunYearlyMarketAdjust(game.HumanPlayer);

        Assert.Equal(before, game.Market.AmountInMarket(Sugar));
        Assert.All(ColonialRivals(game), r => Assert.Empty(r.Market.SaveDeltas()));
    }

    [Fact]
    public void Drift_NeverAdjustsAnUntradedGood()
    {
        // hasBeenTraded gates the whole adjust (ServerPlayer.java:1781-1782): a market that has seen no trade sits
        // perfectly still even deep into the game — the extra type included.
        Game game = AtTurn(200, seed: 7);

        game.RunYearlyMarketAdjust(game.HumanPlayer);

        Assert.All(game.Players, p => Assert.Empty(p.Market.SaveDeltas()));
    }

    [Fact]
    public void Drift_RunsOnTheDriftingPlayersOwnMarketAndStream()
    {
        // A foreign power's tick drifts ITS market (its own stream state seeds the generator, read without
        // advancing); its adjusts then propagate onward to the other Europeans — the human included — per
        // flushExtraTrades. Neither the power's own stream nor the human's stream 0 advances.
        Game game = AtTurn(200, seed: 7);
        Player power = ColonialRivals(game)[0];
        power.Market.Sell(Sugar, 600, taxPercent: 0); // the power floods ITS OWN sugar market
        int before = power.Market.AmountInMarket(Sugar);
        SaveGame preTick = SaveGame.From(game);

        game.RunYearlyMarketAdjust(power);

        Assert.InRange(power.Market.AmountInMarket(Sugar), before - 40, before); // its market drifted toward the seed
        SaveGame postTick = SaveGame.From(game);
        Assert.Equal(preTick.RandomStateValue, postTick.RandomStateValue); // human stream 0 untouched
        Assert.Equal(
            preTick.Players!.Select(p => (p.RngState, p.RngIncrement)),
            postTick.Players!.Select(p => (p.RngState, p.RngIncrement))); // no player stream advanced — the power's included
    }

    [Fact]
    public void Drift_IsDeterministic_AndSaveRoundTripsByteIdentically()
    {
        var a = AtTurn(50, seed: 4242);
        var b = AtTurn(50, seed: 4242);
        foreach (Game g in new[] { a, b })
        {
            g.HumanPlayer.Market.Sell(Sugar, 600, taxPercent: 0);
            g.RunYearlyMarketAdjust(g.HumanPlayer);
        }
        Assert.Equal(SaveGame.From(a).ToJson(), SaveGame.From(b).ToJson()); // twin determinism

        string json = SaveGame.From(a).ToJson();
        Assert.Equal(json, SaveGame.From(SaveGame.FromJson(json).Restore(Classic)).ToJson()); // no new save field
    }

    [Fact]
    public void EndTurn_RunsPropagationAndDriftThroughTheLivePath()
    {
        // End-to-end coverage of the two wired Game.cs seams (the direct-call tests above pin the exact numbers; this
        // proves the live paths are actually REACHED by a real turn — a guard against someone deleting a seam line):
        //   (1) SellColonyGoods → PropagateTradeToRivalMarkets (FreeCol sellInEurope:1327) — a colony sale ripples to
        //       every rival market in-path, before any EndTurn.
        //   (2) RunPlayerTurn → RunYearlyMarketAdjust (FreeCol csStartTurn:1813) — the human's per-turn drift tick,
        //       reached first in the player ring, pulls its now-traded, above-baseline silver back toward baseline.
        // The human sells via the colony but nothing else touches its Europe silver: the colony's own production lands
        // in its warehouse, not the market, and — asserted below — no rival sells silver this turn, so no propagation
        // flows back into the human's silver. Hence the only thing that can move it across EndTurn is its own drift.
        Game game = AtTurn(200, seed: 7);
        Colony colony = game.FoundColony(game.Units.First(u => u.IsOnMap && !u.Type.IsNaval));
        colony.AddGoods(Silver, 100);

        int rivalSilverBefore = ColonialRivals(game)[0].Market.AmountInMarket(Silver);
        game.SellColonyGoods(colony, Silver, 100); // live sell of one ≤100 chunk → one 5-30% propagation roll
        int baseline = game.Market.InitialAmountOf(Silver);
        int humanSilverAfterSale = game.Market.AmountInMarket(Silver);
        Assert.True(humanSilverAfterSale > baseline, "the sale should push the human's silver above its baseline");
        Assert.All(ColonialRivals(game), r =>
            Assert.InRange(r.Market.AmountInMarket(Silver) - rivalSilverBefore, 5, 30)); // (1) every rival rippled 5-30%

        game.EndTurn(); // (2) the human's RunPlayerTurn runs first in the ring → the drift tick

        Assert.All(ColonialRivals(game), r => Assert.Equal(0, r.Market.SalesOf(Silver))); // no rival sold silver in
        int humanSilverAfterTurn = game.Market.AmountInMarket(Silver);
        Assert.True(humanSilverAfterTurn < humanSilverAfterSale,
            "the live drift tick must pull the above-baseline traded good back down");
        Assert.True(humanSilverAfterTurn >= baseline, "one drift tick never overshoots the baseline");
    }

    /// <summary>A fresh seeded game fast-forwarded to <paramref name="turn"/> via the save layer (no turns played).</summary>
    private static Game AtTurn(int turn, ulong seed) =>
        (SaveGame.From(Game.New(Classic, seed)) with { Turn = turn }).Restore(Classic);

    /// <summary>64-bit FNV-1a over an ASCII ruleset id — mirrors the engine's seed hash (white-box formula pin).</summary>
    private static ulong Fnv1a(string text)
    {
        ulong hash = 14695981039346656037UL;
        foreach (char c in text)
        {
            hash = unchecked((hash ^ (byte)c) * 1099511628211UL);
        }
        return hash;
    }

    /// <summary>A game with one coastal colony at full Sons of Liberty, ready to declare (IndependenceTests pattern).</summary>
    private static Game RebellionReady(ulong seed)
    {
        Game game = Game.New(Classic, seed);
        Position coastal = game.Map.AllPositions().First(p =>
            !game.Map.TerrainAt(p).IsWater
            && game.ColonyAt(p) is null
            && game.NativeSettlementAt(p) is null
            && p.Neighbours().Any(n => game.Map.InBounds(n) && game.Map.TerrainAt(n).IsWater));
        Unit colonist = game.SpawnUnit(Classic.Unit(Game.StartingUnitTypeId), coastal);
        Colony colony = game.FoundColony(colonist);
        colony.Liberty = Colony.LibertyPerRebel * colony.Population; // force national SoL to 100%
        return game;
    }
}
