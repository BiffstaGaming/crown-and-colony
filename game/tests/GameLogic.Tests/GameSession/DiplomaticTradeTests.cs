using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.GameSession.Diplomacy;
using CrownAndColony.GameLogic.Specification;
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
}
