using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// The minimal foreign-power economy (FP-5, ADR-019): foreign colonial powers produce, bank liberty and
/// immigration, sell surplus to their OWN per-player market and recruit from Europe — each on its own RNG
/// stream so the human's stream 0 and market stay byte-stable. These tests prove the economy runs, the
/// per-player markets are independent and persist, and recruited units belong to the power, not the human.
/// </summary>
public class ForeignPowerEconomyTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    private const string Cotton = "model.goods.cotton";
    private const string Sugar = "model.goods.sugar";

    private static Player AForeignPower(Game game) =>
        game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);

    [Fact]
    public void ForeignPower_RunsAFullEconomy_OnItsOwnStreamAndMarket()
    {
        var game = Game.New(Classic, seed: 7);
        Player power = AForeignPower(game);

        for (int i = 0; i < 30; i++)
        {
            game.EndTurn();
        }

        // It founded and runs a colony (FP-4 + FP-5 production).
        Assert.Contains(game.Colonies, c => c.OwnerId == power.PlayerId);
        // It engaged the Continental Congress with its own liberty (banked, or it already elected a father).
        Assert.True(power.Liberty > 0 || power.Congress.Count > 0, "the power banked no liberty and elected no father");
        // It engaged Europe with its own immigration (accruing now, or it already sent a colonist there).
        Assert.True(
            power.Immigration > 0 || game.Units.Any(u =>
                u.OwnerId == power.PlayerId && u.OwnerNationId is null && u.Location == UnitLocation.InEurope),
            "the power accrued no immigration and no colonist reached its Europe");
        // The human did nothing, so its own market never moved — proof the foreign trade stayed on its own market.
        Assert.Empty(game.Market.SaveDeltas());
    }

    [Fact]
    public void PerPlayerMarkets_AreIndependent_OneTradeMovesOnlyTheSellersMarket()
    {
        var game = Game.New(Classic, seed: 7);
        var colonial = game.Players.Where(p => p.PlayerType == PlayerType.Colonial).ToList(); // human + 3 powers
        Player human = game.HumanPlayer;
        Player seller = colonial.First(p => !p.IsHuman);
        Player other = colonial.Last(p => !p.IsHuman && p.PlayerId != seller.PlayerId);

        Assert.NotSame(human.Market, seller.Market);
        Assert.NotSame(seller.Market, other.Market);

        int humanBefore = human.Market.BidPrice(Cotton);
        int otherBefore = other.Market.BidPrice(Cotton);
        int sellerBefore = seller.Market.BidPrice(Cotton);

        // Flood the seller's own market: enough supply to push its bid price down.
        seller.Market.Sell(Cotton, 5000, taxPercent: 0);

        Assert.True(seller.Market.BidPrice(Cotton) < sellerBefore, "the seller's own price did not fall");
        Assert.Equal(humanBefore, human.Market.BidPrice(Cotton)); // the human's market is untouched
        Assert.Equal(otherBefore, other.Market.BidPrice(Cotton)); // another power's market is untouched
    }

    [Fact]
    public void ForeignPowerMarket_RoundTripsThroughSave()
    {
        var game = Game.New(Classic, seed: 7);
        Player power = AForeignPower(game);

        int seedAmount = power.Market.AmountInMarket(Sugar);
        power.Market.Sell(Sugar, 4000, taxPercent: 0); // move the power's market off its ruleset seed
        int expectedBid = power.Market.BidPrice(Sugar);
        int expectedAmount = power.Market.AmountInMarket(Sugar);
        Assert.True(expectedAmount > seedAmount, "the sell did not move the market");

        Game loaded = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);
        Player loadedPower = loaded.Players.First(p => p.PlayerId == power.PlayerId);

        Assert.Equal(expectedBid, loadedPower.Market.BidPrice(Sugar));
        Assert.Equal(expectedAmount, loadedPower.Market.AmountInMarket(Sugar));
        // The human, which never traded, has no market deltas — its market is independent of the power's.
        Assert.Empty(loaded.Market.SaveDeltas());
    }

    [Fact]
    public void ForeignPower_RecruitsFromEurope_WhenAffordable_AndOwnsTheRecruits()
    {
        var game = Game.New(Classic, seed: 7);
        Player power = AForeignPower(game);
        power.Gold = 1000; // enough to recruit up to the Europe cap

        game.EndTurn();

        var recruits = game.Units.Where(u =>
            u.Location == UnitLocation.InEurope && u.OwnerNationId is null && u.OwnerId == power.PlayerId).ToList();
        Assert.NotEmpty(recruits);                          // it recruited
        Assert.True(power.Gold < 1000, "no gold was spent recruiting");
        Assert.All(recruits, u => Assert.True(u.Type.IsPerson));
        // The recruits belong to the power, never the human (the owner-id fix) — the human gained no Europe units.
        Assert.Empty(game.UnitsInEurope);
    }

    [Fact]
    public void ForeignEconomy_KeepsTheHumansStreamByteStable()
    {
        // Two same-seed games run the full ring (human + foreign economies) → byte-identical: every foreign
        // choice draws from that power's own stream and trades on its own market; the human's stream 0 is
        // never touched. Guards the byte-stability contract (ADR-009) for the whole economy.
        var a = Game.New(Classic, seed: 4242);
        var b = Game.New(Classic, seed: 4242);
        for (int turn = 0; turn < 25; turn++)
        {
            a.EndTurn();
            b.EndTurn();
        }

        Assert.Equal(SaveGame.From(a).ToJson(), SaveGame.From(b).ToJson());
        // A foreign power was economically active (it banked liberty or reached Europe), so the run is meaningful.
        Assert.Contains(a.Players, p => !p.IsHuman && p.PlayerType == PlayerType.Colonial
            && (p.Liberty > 0 || p.Congress.Count > 0
                || a.Units.Any(u => u.OwnerId == p.PlayerId && u.Location == UnitLocation.InEurope)));
    }
}
