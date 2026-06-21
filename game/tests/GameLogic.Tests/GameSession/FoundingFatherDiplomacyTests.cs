using System.Linq;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.GameSession.Diplomacy;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// The three European-diplomacy founding fathers wired in this slice (86d3c7xu6 / 86d3c7xbk):
/// <list type="bullet">
/// <item><b>Benjamin Franklin</b> — <c>alwaysOfferedPeace</c>: a European power at war with a Franklin holder always
/// accepts/offers peace (the AI never scores his peace/cease-fire/alliance clause as a cost or a refusal).</item>
/// <item><b>Jan de Witt</b> — <c>tradeWithForeignColonies</c> (trade with rivals), <c>customHouseTradesWithForeignCountries</c>
/// (custom houses sell to foreign markets while at peace with a European), and <c>betterForeignAffairsReport</c> (reveals
/// rival nations' stances — the GameLogic oracle; the report UI is a separate P7 task).</item>
/// </list>
/// Each effect is dormant until its father sits in the player's Congress, so the default game is unchanged.
/// </summary>
public class FoundingFatherDiplomacyTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    private const string Franklin = "model.foundingFather.benjaminFranklin";
    private const string DeWitt = "model.foundingFather.janDeWitt";
    private const string VeteranSoldier = "model.unit.veteranSoldier";
    private const string SoldierRole = "model.role.soldier";

    private static int ForeignPowerId(Game game) =>
        game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial).PlayerId;

    private static int SecondForeignPowerId(Game game) =>
        game.Players.Where(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial).Skip(1).First().PlayerId;

    private static void Elect(Game game, int playerId, string fatherId) =>
        game.Players.First(p => p.PlayerId == playerId).CongressList.Add(fatherId);

    private static bool FreeLand(Game g, Position p) =>
        g.Map.InBounds(p) && !g.Map.TerrainAt(p).IsWater && g.Map.TerrainAt(p).CanSettle
        && g.ColonyAt(p) is null && g.NativeSettlementAt(p) is null
        && !g.Units.Any(u => u.IsOnMap && u.Position == p);

    private static Position FoundableSite(Game g) => g.Map.AllPositions().First(p => FreeLand(g, p));

    private static void GiveSoldiers(Game g, int ownerId, int count)
    {
        for (int i = 0; i < count; i++)
        {
            Unit soldier = g.SpawnUnit(Classic.Unit(VeteranSoldier), FoundableSite(g));
            soldier.RoleId = SoldierRole; // role offence > 0 → counts toward land power
            soldier.OwnerId = ownerId;
        }
    }

    // ───────────────────────── parsing ─────────────────────────

    [Fact]
    public void Ruleset_Franklin_CarriesIgnoreEuropeanWars_AndAlwaysOfferedPeace()
    {
        FoundingFather franklin = Classic.Father(Franklin);
        Assert.Contains(franklin.Abilities, a => a.Id == "model.ability.ignoreEuropeanWars" && a.Value);
        Assert.Contains(franklin.Abilities, a => a.Id == "model.ability.alwaysOfferedPeace" && a.Value);
    }

    [Fact]
    public void Ruleset_DeWitt_CarriesAllThreeTradeAndReportAbilities()
    {
        FoundingFather deWitt = Classic.Father(DeWitt);
        Assert.Contains(deWitt.Abilities, a => a.Id == "model.ability.tradeWithForeignColonies" && a.Value);
        Assert.Contains(deWitt.Abilities, a => a.Id == "model.ability.customHouseTradesWithForeignCountries" && a.Value);
        Assert.Contains(deWitt.Abilities, a => a.Id == "model.ability.betterForeignAffairsReport" && a.Value);
    }

    // ───────────────────────── Benjamin Franklin — alwaysOfferedPeace ─────────────────────────

    [Fact]
    public void Franklin_MakesAStrongPowerAcceptHisPeace_ThatItWouldOtherwiseRefuse()
    {
        // fid is much stronger than `other` (ratio > 0.66): normally fid refuses peace with the weaker `other`.
        var game = Game.New(Classic, seed: 7);
        int fid = ForeignPowerId(game);
        int other = SecondForeignPowerId(game);
        GiveSoldiers(game, fid, 10);  // strong
        GiveSoldiers(game, other, 1); // weak → ratio ≈ 0.91 for fid

        var peace = new DiplomaticTrade(other, fid).Add(new StanceTradeItem(other, fid, Stance.Peace));
        Assert.False(game.EvaluateTrade(fid, peace).Accept); // without Franklin: invalid (ratio > 0.66)

        // `other` (the proposer) elects Franklin → the strong fid now always takes the peace (clause scores 0).
        Elect(game, other, Franklin);
        Assert.True(game.EvaluateTrade(fid, peace).Accept);
        Assert.Equal(0, game.EvaluateTradeItem(fid, new StanceTradeItem(other, fid, Stance.Peace)));
    }

    [Fact]
    public void Franklin_DoesNotChangeWarClauses_OnlyPeace()
    {
        var game = Game.New(Classic, seed: 7);
        int fid = ForeignPowerId(game);
        int other = SecondForeignPowerId(game);
        GiveSoldiers(game, fid, 10);
        GiveSoldiers(game, other, 1);
        Elect(game, other, Franklin);

        // A war clause is scored on strength as usual — Franklin only neutralises peace/cease-fire/alliance.
        int warScore = game.EvaluateTradeItem(fid, new StanceTradeItem(fid, other, Stance.War));
        Assert.True(warScore > 0); // fid dominates → war is attractive, unchanged by the other's Franklin
    }

    [Fact]
    public void WithoutFranklin_TheOtherPartyAlwaysOffersPeaceOracle_IsFalse()
    {
        var game = Game.New(Classic, seed: 7);
        int other = SecondForeignPowerId(game);
        Assert.False(game.OtherPartyAlwaysOffersPeace(other));
        Elect(game, other, Franklin);
        Assert.True(game.OtherPartyAlwaysOffersPeace(other));
    }

    // ───────────────────────── Jan de Witt — foreign trade + report ─────────────────────────

    [Fact]
    public void DeWitt_EnablesTradeWithForeignColonies_OnlyWhenElected()
    {
        var game = Game.New(Classic, seed: 7);
        int fid = ForeignPowerId(game);

        Assert.False(game.CanTradeWithForeignColonies(0));   // human, no de Witt
        Assert.False(game.CanTradeWithForeignColonies(fid)); // rival, no de Witt

        Elect(game, 0, DeWitt);
        Assert.True(game.CanTradeWithForeignColonies(0));    // the human may now trade with rivals
        Assert.False(game.CanTradeWithForeignColonies(fid)); // owner-scoped — the rival still cannot
    }

    [Fact]
    public void DeWitt_CustomHouseForeignTrade_NeedsBothTheFatherAndPeaceWithAEuropean()
    {
        var game = Game.New(Classic, seed: 7);
        int fid = ForeignPowerId(game);

        Assert.False(game.CustomHouseTradesWithForeignCountries(0)); // no de Witt yet
        Elect(game, 0, DeWitt);

        // de Witt elected but at war with everyone → no foreign market open.
        foreach (int rival in game.Players.Where(p => p.PlayerId != 0 && p.PlayerType == PlayerType.Colonial).Select(p => p.PlayerId))
        {
            game.SetStance(0, rival, Stance.War);
        }
        Assert.False(game.CustomHouseTradesWithForeignCountries(0));

        // At peace with one European → the foreign market opens.
        game.SetStance(0, fid, Stance.Peace);
        Assert.True(game.CustomHouseTradesWithForeignCountries(0));
    }

    [Fact]
    public void DeWitt_ForeignAffairsReport_FlagFollowsElection()
    {
        var game = Game.New(Classic, seed: 7);
        Assert.False(game.HasBetterForeignAffairsReport(0));
        Elect(game, 0, DeWitt);
        Assert.True(game.HasBetterForeignAffairsReport(0));
    }

    [Fact]
    public void ForeignNationStances_ReportsEveryRivalsStance_InPlayerIdOrder()
    {
        var game = Game.New(Classic, seed: 7);
        int fid = ForeignPowerId(game);
        int other = SecondForeignPowerId(game);
        game.SetStance(0, fid, Stance.War);
        game.SetStance(0, other, Stance.Peace);

        var rows = game.ForeignNationStances(0);

        // Every other colonial power appears exactly once, in ascending id order, with the human's stance toward it.
        Assert.Equal(rows.Select(r => r.PlayerId).OrderBy(id => id), rows.Select(r => r.PlayerId));
        Assert.DoesNotContain(rows, r => r.PlayerId == 0); // the player itself is excluded
        Assert.Equal(Stance.War, rows.First(r => r.PlayerId == fid).Stance);
        Assert.Equal(Stance.Peace, rows.First(r => r.PlayerId == other).Stance);
    }

    [Fact]
    public void DeWittOracles_AreEmptyOrFalse_ForANonColonialPlayer()
    {
        var game = Game.New(Classic, seed: 7);
        int nid = game.Players.First(p => p.PlayerType == PlayerType.Native).PlayerId;
        Assert.Empty(game.ForeignNationStances(nid));
        Assert.False(game.CanTradeWithForeignColonies(nid));
        Assert.False(game.HasBetterForeignAffairsReport(nid));
    }
}
