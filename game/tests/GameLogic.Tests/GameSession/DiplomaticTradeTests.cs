using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.GameSession.Diplomacy;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// Diplomatic-trade backend (P6, FreeCol <c>DiplomaticTrade</c>/<c>TradeItem</c>): the in-memory treaty model and
/// its <c>Apply</c> path between two colonial players — gold, goods, and stance clauses. No save change and no RNG
/// this slice; the AI does not yet evaluate offers.
/// </summary>
public class DiplomaticTradeTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    private static int ForeignPowerId(Game game) =>
        game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial).PlayerId;

    // ---- Item 1: the container ----

    [Fact]
    public void NewTrade_RecordsProposerAndRecipient_AndStartsEmpty()
    {
        var game = Game.New(Classic, seed: 7);
        int fid = ForeignPowerId(game);

        var trade = new DiplomaticTrade(proposerId: 0, recipientId: fid);

        Assert.Equal(0, trade.ProposerId);
        Assert.Equal(fid, trade.RecipientId);
        Assert.Empty(trade.Items);
    }

    [Fact]
    public void EmptyTrade_IsNotValid_AndSettlingItIsAHarmlessNoOp()
    {
        var game = Game.New(Classic, seed: 7);
        int fid = ForeignPowerId(game);
        var trade = new DiplomaticTrade(0, fid);

        Assert.False(trade.IsValid(game)); // a treaty with no clauses is not a valid offer

        game.SettleTrade(trade); // inert — nothing to apply
    }

    // ---- Item 2: gold + goods clauses ----

    private const string Tobacco = "model.goods.tobacco";

    private static bool FreeLand(Game g, Position p) =>
        g.Map.InBounds(p) && !g.Map.TerrainAt(p).IsWater && g.Map.TerrainAt(p).CanSettle
        && g.ColonyAt(p) is null && g.NativeSettlementAt(p) is null
        && !g.Units.Any(u => u.IsOnMap && u.Position == p);

    private static int Chebyshev(Position a, Position b) =>
        System.Math.Max(System.Math.Abs(a.X - b.X), System.Math.Abs(a.Y - b.Y));

    /// <summary>A foundable land tile with no colony on it or beside it (so <c>FoundColony</c>'s spacing rule passes), away from <paramref name="avoid"/>.</summary>
    private static Position FoundableSite(Game g, Position? avoid = null) =>
        g.Map.AllPositions().First(p =>
            FreeLand(g, p)
            && (avoid is null || Chebyshev(p, avoid.Value) > 2)
            && p.Neighbours().All(n => !g.Map.InBounds(n) || g.ColonyAt(n) is null));

    /// <summary>Founds a colony for <paramref name="ownerId"/> on a fresh tile (away from <paramref name="avoid"/>) via a spawned colonist.</summary>
    private static Colony FoundColonyFor(Game g, int ownerId, Position? avoid = null)
    {
        Unit founder = g.SpawnUnit(Classic.Unit(Colony.FreeColonistTypeId), FoundableSite(g, avoid));
        Colony colony = g.FoundColony(founder);
        colony.OwnerId = ownerId;
        return colony;
    }

    [Fact]
    public void GoldTradeItem_MovesGoldFromSourceToDestination_OnSettle()
    {
        var game = Game.New(Classic, seed: 7);
        int fid = ForeignPowerId(game);
        Player human = game.HumanPlayer;
        Player rival = game.Players.First(p => p.PlayerId == fid);
        human.Gold = 500;
        rival.Gold = 100;

        var trade = new DiplomaticTrade(0, fid).Add(new GoldTradeItem(source: 0, destination: fid, amount: 300));
        Assert.True(trade.IsValid(game));

        game.SettleTrade(trade);

        Assert.Equal(200, human.Gold); // 500 − 300
        Assert.Equal(400, rival.Gold); // 100 + 300
    }

    [Fact]
    public void GoldTradeItem_IsInvalid_AndSkipped_WhenThePayerCannotAfford()
    {
        var game = Game.New(Classic, seed: 7);
        int fid = ForeignPowerId(game);
        game.HumanPlayer.Gold = 50;

        var item = new GoldTradeItem(source: 0, destination: fid, amount: 300);
        Assert.False(item.IsValid(game)); // 300 > 50

        game.SettleTrade(new DiplomaticTrade(0, fid).Add(item)); // skipped, not thrown
        Assert.Equal(50, game.HumanPlayer.Gold); // unchanged
    }

    [Fact]
    public void GoodsTradeItem_MovesGoodsBetweenColonies_OnSettle()
    {
        var game = Game.New(Classic, seed: 7);
        int fid = ForeignPowerId(game);
        Colony human = FoundColonyFor(game, ownerId: 0);
        Colony rival = FoundColonyFor(game, ownerId: fid, avoid: human.Position);
        rival.AddGoods(Tobacco, 100); // the rival's colony holds the goods being given to us

        var trade = new DiplomaticTrade(0, fid)
            .Add(new GoodsTradeItem(
                source: fid, destination: 0,
                sourceColonyId: rival.Id, destinationColonyId: human.Id,
                goodsId: Tobacco, amount: 60));
        Assert.True(trade.IsValid(game));

        game.SettleTrade(trade);

        Assert.Equal(40, rival.StoreOf(Tobacco));  // 100 − 60
        Assert.Equal(60, human.StoreOf(Tobacco));  // 0 + 60
    }

    [Fact]
    public void GoodsTradeItem_IsInvalid_WhenTheSourceColonyLacksTheGoods()
    {
        var game = Game.New(Classic, seed: 7);
        int fid = ForeignPowerId(game);
        Colony human = FoundColonyFor(game, ownerId: 0);
        Colony rival = FoundColonyFor(game, ownerId: fid, avoid: human.Position);
        rival.AddGoods(Tobacco, 10);

        var item = new GoodsTradeItem(fid, 0, rival.Id, human.Id, Tobacco, 60);
        Assert.False(item.IsValid(game)); // only 10 in store

        game.SettleTrade(new DiplomaticTrade(0, fid).Add(item)); // skipped
        Assert.Equal(10, rival.StoreOf(Tobacco));
        Assert.Equal(0, human.StoreOf(Tobacco));
    }

    [Fact]
    public void TwoPowerTreaty_SettlesGoldOneWayAndGoodsTheOther_Atomically()
    {
        // "Our 200 gold for their 80 tobacco" — a treaty with clauses moving value both directions settles as a unit.
        var game = Game.New(Classic, seed: 7);
        int fid = ForeignPowerId(game);
        Player human = game.HumanPlayer;
        Player rival = game.Players.First(p => p.PlayerId == fid);
        human.Gold = 1000;
        rival.Gold = 0;
        Colony ours = FoundColonyFor(game, ownerId: 0);
        Colony theirs = FoundColonyFor(game, ownerId: fid, avoid: ours.Position);
        theirs.AddGoods(Tobacco, 80);

        var trade = new DiplomaticTrade(0, fid)
            .Add(new GoldTradeItem(source: 0, destination: fid, amount: 200))
            .Add(new GoodsTradeItem(fid, 0, theirs.Id, ours.Id, Tobacco, 80));
        Assert.True(trade.IsValid(game));

        game.SettleTrade(trade);

        Assert.Equal(800, human.Gold);            // paid 200
        Assert.Equal(200, rival.Gold);            // received 200
        Assert.Equal(0, theirs.StoreOf(Tobacco)); // gave 80
        Assert.Equal(80, ours.StoreOf(Tobacco));  // received 80
    }

    // ---- Item 3: alliance stance + StanceTradeItem ----

    [Fact]
    public void AllianceOrdinal_IsAppendedAsFour_LeavingEarlierStanceOrdinalsStable()
    {
        // Saved stance ordinals must stay stable across the wave — Alliance is appended, not reordered.
        Assert.Equal(0, (int)Stance.Uncontacted);
        Assert.Equal(1, (int)Stance.Peace);
        Assert.Equal(2, (int)Stance.War);
        Assert.Equal(3, (int)Stance.CeaseFire);
        Assert.Equal(4, (int)Stance.Alliance);
    }

    [Theory]
    [InlineData(Stance.Peace)]
    [InlineData(Stance.CeaseFire)]
    [InlineData(Stance.Alliance)]
    public void StanceTradeItem_SetsThePairsStanceBothWays_OnSettle(Stance agreed)
    {
        var game = Game.New(Classic, seed: 7);
        int fid = ForeignPowerId(game);
        game.SetStance(0, fid, Stance.War); // a war the treaty ends/changes

        var trade = new DiplomaticTrade(0, fid).Add(new StanceTradeItem(source: 0, destination: fid, stance: agreed));
        Assert.True(trade.IsValid(game));

        game.SettleTrade(trade);

        Assert.Equal(agreed, game.StanceBetween(0, fid));
        Assert.Equal(agreed, game.StanceBetween(fid, 0)); // symmetric
    }

    [Fact]
    public void StanceTradeItem_IsInvalid_ForANonColonialPair()
    {
        var game = Game.New(Classic, seed: 7);
        int nid = game.Players.First(p => p.PlayerType == PlayerType.Native).PlayerId;

        var item = new StanceTradeItem(0, nid, Stance.Alliance);
        Assert.False(item.IsValid(game)); // natives stay on the alarm system — stance is not tracked

        game.SettleTrade(new DiplomaticTrade(0, nid).Add(item)); // skipped
        Assert.Equal(Stance.Uncontacted, game.StanceBetween(0, nid));
    }

    [Fact]
    public void Treaty_CanEndAWarWithGoldAndAlliance_InOneSettle()
    {
        // "300 gold and we ally" — gold + stance clauses settle together.
        var game = Game.New(Classic, seed: 7);
        int fid = ForeignPowerId(game);
        Player human = game.HumanPlayer;
        human.Gold = 1000;
        game.SetStance(0, fid, Stance.War);

        var trade = new DiplomaticTrade(0, fid)
            .Add(new GoldTradeItem(0, fid, 300))
            .Add(new StanceTradeItem(0, fid, Stance.Alliance));

        game.SettleTrade(trade);

        Assert.Equal(700, human.Gold);
        Assert.Equal(Stance.Alliance, game.StanceBetween(0, fid));
        Assert.Equal(Stance.Alliance, game.StanceBetween(fid, 0));
    }

    [Fact]
    public void AllianceStance_RoundTripsThroughSave_NoSaveBumpNeeded()
    {
        // The trade container isn't persisted, but the stance it sets is (existing FP-6a save path). Alliance is a
        // new enum value serialized as its ordinal (4); it round-trips on the existing save version (additive).
        var game = Game.New(Classic, seed: 7);
        int fid = ForeignPowerId(game);
        game.SettleTrade(new DiplomaticTrade(0, fid).Add(new StanceTradeItem(0, fid, Stance.Alliance)));

        Game loaded = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);

        Assert.Equal(Stance.Alliance, loaded.StanceBetween(0, fid));
        Assert.Equal(Stance.Alliance, loaded.StanceBetween(fid, 0));
    }
}
