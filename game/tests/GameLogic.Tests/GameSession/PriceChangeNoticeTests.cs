using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Trade;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// The player is <b>notified when a good's Europe price changes</b> (`86d3fpz0p` — FreeCol's <c>PRICE_CHANGE</c> property
/// + <c>ServerPlayer.csFlushMarket</c>'s <c>model.market.priceIncrease</c>/<c>priceDecrease</c> "told about price"
/// message). Each <see cref="Game.EndTurn"/> compares every human-market good's buy (ask) price to a per-turn baseline
/// (the prices the player last saw) and emits a <see cref="PriceChangeNotice"/> for each that moved, then re-baselines.
/// The window spans the whole turn — the human's own Europe trades before End Turn included. Transient (rebuilt each turn,
/// never saved), deterministic (the market's stable goods order), RNG-free (ADR-009).
/// </summary>
public class PriceChangeNoticeTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 0xC0FFEEUL;
    private const string Sugar = "model.goods.sugar";
    private const string Tobacco = "model.goods.tobacco";

    private static Colony FoundColony(Game game) =>
        game.FoundColony(game.PlayerUnits.First(u => u.IsOnMap && u.Type.CanFoundColony));

    // ── A sale that moves a price emits exactly the right notice ───────────────────────────────────────────────

    [Fact]
    public void SaleThatMovesAPrice_EmitsTheNotice_WithTheOldAndNewPrice()
    {
        Game game = Game.New(Classic, Seed);
        Colony colony = FoundColony(game);
        colony.AddGoods(Sugar, 2000);

        int askBefore = game.Market.AskPrice(Sugar);
        game.SellColonyGoods(colony, Sugar, 2000); // floods the market → the price falls
        int askAfter = game.Market.AskPrice(Sugar);
        Assert.True(askAfter < askBefore, "the heavy sale should drop the sugar price"); // sanity: the price actually moved

        game.EndTurn(); // the per-turn flush compares live vs. baseline and emits the notice

        PriceChangeNotice notice = Assert.Single(game.PriceChangeNotices, n => n.GoodsId == Sugar);
        Assert.Equal(askBefore, notice.OldPrice);
        Assert.Equal(askAfter, notice.NewPrice);
        Assert.Equal(game.Market.BidPrice(Sugar), notice.SellPrice); // the bid carried for the message
    }

    // ── No change → no notice ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void NoTrade_NoPriceChange_EmitsNothing()
    {
        Game game = Game.New(Classic, Seed);
        FoundColony(game);

        game.EndTurn(); // nobody traded on the human market this turn

        Assert.Empty(game.PriceChangeNotices);
    }

    [Fact]
    public void NoticesAreClearedAndRebuiltEachTurn()
    {
        Game game = Game.New(Classic, Seed);
        Colony colony = FoundColony(game);
        colony.AddGoods(Sugar, 2000);
        game.SellColonyGoods(colony, Sugar, 2000);
        game.EndTurn();
        Assert.NotEmpty(game.PriceChangeNotices); // sugar moved this turn

        game.EndTurn(); // a quiet turn — no human-market trade

        Assert.Empty(game.PriceChangeNotices); // the prior turn's notice is gone, none re-emitted (the price was re-baselined)
    }

    // ── Deterministic order ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MultipleMovedGoods_AreEmittedInTheMarketsStableGoodsOrder()
    {
        Game game = Game.New(Classic, Seed);
        Colony colony = FoundColony(game);
        colony.AddGoods(Sugar, 2000);
        colony.AddGoods(Tobacco, 2000);
        game.SellColonyGoods(colony, Sugar, 2000);
        game.SellColonyGoods(colony, Tobacco, 2000);

        game.EndTurn();

        // The notice order follows Market.TradeableGoods (ruleset goods order), so it matches the order the same two
        // goods appear in that enumeration — a deterministic, reproducible sequence (no RNG).
        var movedIds = game.PriceChangeNotices.Select(n => n.GoodsId).ToList();
        var expectedOrder = game.Market.TradeableGoods.Where(movedIds.Contains).ToList();
        Assert.Equal(expectedOrder, movedIds);
        Assert.Contains(Sugar, movedIds);
        Assert.Contains(Tobacco, movedIds);
    }

    // ── Determinism (ADR-009): the detection must draw no RNG ──────────────────────────────────────────────────

    [Fact]
    public void DetectingPriceChanges_DoesNotPerturbTheRandomStream()
    {
        // Two identical games trade identically; one reads the notices, the other does not. Their post-EndTurn RNG state
        // must be identical — the detection only compares recorded prices, drawing nothing (the soak relies on this).
        Game a = Game.New(Classic, Seed);
        Game b = Game.New(Classic, Seed);
        Colony ca = FoundColony(a);
        Colony cb = FoundColony(b);
        ca.AddGoods(Sugar, 2000);
        cb.AddGoods(Sugar, 2000);
        a.SellColonyGoods(ca, Sugar, 2000);
        b.SellColonyGoods(cb, Sugar, 2000);

        a.EndTurn();
        _ = a.PriceChangeNotices.ToList(); // a reads them
        b.EndTurn();                       // b ignores them

        Assert.Equal(b.RandomState, a.RandomState); // identical future random sequence
    }

    // ── Persistence: the notices are transient (never saved) and a reload does not re-announce ────────────────

    [Fact]
    public void PriceChangeNotices_AreNotSaved_AndAReloadReAnnouncesNothing()
    {
        Game game = Game.New(Classic, Seed);
        Colony colony = FoundColony(game);
        colony.AddGoods(Sugar, 2000);
        game.SellColonyGoods(colony, Sugar, 2000);
        game.EndTurn();
        Assert.NotEmpty(game.PriceChangeNotices);

        // The save carries no price-change notice token — they are pure per-turn scratch.
        string json = SaveGame.From(game).ToJson();
        Assert.DoesNotContain("PriceChange", json);

        // A reloaded game starts with no notices and — because the baseline is re-seeded to the restored (moved) prices —
        // does NOT re-announce the change on its first EndTurn (a quiet turn after load stays silent).
        Game loaded = SaveGame.FromJson(json).Restore(Classic);
        Assert.Empty(loaded.PriceChangeNotices);
        loaded.EndTurn();
        Assert.Empty(loaded.PriceChangeNotices);
    }
}
