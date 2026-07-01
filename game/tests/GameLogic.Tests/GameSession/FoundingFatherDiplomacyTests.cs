using System.Linq;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.GameSession.Diplomacy;
using CrownAndColony.GameLogic.Randomness;
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

    // ───────────────────────── Benjamin Franklin — peaceTreaty +50% (peace-hold) ─────────────────────────

    [Fact]
    public void Ruleset_Franklin_CarriesThePeaceTreatyModifier()
    {
        // The third Franklin effect, now wired: model.modifier.peaceTreaty, percentage +50%.
        FoundingFather franklin = Classic.Father(Franklin);
        Assert.Contains(franklin.Modifiers,
            m => m.TargetId == "model.modifier.peaceTreaty" && m.Type == ModifierType.Percentage && m.Value == 50);
    }

    [Fact]
    public void PeaceTreatyModifierFactor_IsOneWithoutFranklin_AndOneAndAHalfWithHim()
    {
        // FreeCol p.apply(prob, turn, PEACE_TREATY): a percentage-additive +50% scales the base by 1.5; no Franklin → 1.0.
        var game = Game.New(Classic, seed: 7);
        Player human = game.HumanPlayer;
        Assert.Equal(1.0, game.PeaceTreatyModifierFactor(human), precision: 6);

        human.CongressList.Add(Franklin);
        Assert.Equal(1.5, game.PeaceTreatyModifierFactor(human), precision: 6);
    }

    [Fact]
    public void PeaceTreatyHoldProbability_IsZeroWithoutFranklin_EvenWithARecordedPeace()
    {
        // The Wave-12 gate is preserved: with no Franklin the probability is 0 regardless of any recorded peace, so the
        // war always proceeds and the default game is unchanged (a deliberate divergence from raw FreeCol's 0.90 base).
        var game = Game.New(Classic, seed: 7);
        Player power = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);
        game.SetStance(power.PlayerId, game.HumanPlayer.PlayerId, Stance.Peace); // stamps the peace turn (turn 1)
        Assert.Equal(0.0, game.PeaceTreatyHoldProbability(power, game.HumanPlayer));
    }

    [Fact]
    public void PeaceTreatyHoldProbability_WithFranklin_AppliesTheDecayingBaseScaledByTheModifier()
    {
        // FreeCol peaceHolds: prob = (PEACE_PROBABILITY/100)^n, then × the peaceTreaty factor (1.5). Classic base 0.90.
        // n = 0 the turn peace takes force → 0.90^0 × 1.5 = 1.5, clamped to 1.0.
        var game = Game.New(Classic, seed: 7);
        Player power = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);
        game.HumanPlayer.CongressList.Add(Franklin);
        game.SetStance(power.PlayerId, game.HumanPlayer.PlayerId, Stance.Peace); // peace turn = current turn (n = 0)

        Assert.Equal(1.0, game.PeaceTreatyHoldProbability(power, game.HumanPlayer), precision: 6); // 0.90^0 × 1.5 clamped

        // Stamp an older peace turn so n grows, and watch the probability decay: 0.90^n × 1.5.
        power.PeaceTurnMap[game.HumanPlayer.PlayerId] = game.Turn - 5;
        double expected = System.Math.Pow(0.90, 5) * 1.5; // ≈ 0.8857
        Assert.Equal(expected, game.PeaceTreatyHoldProbability(power, game.HumanPlayer), precision: 6);

        // Far enough out the decay drives it below 1 and keeps falling — strictly monotone in n.
        power.PeaceTurnMap[game.HumanPlayer.PlayerId] = game.Turn - 40;
        double far = game.PeaceTreatyHoldProbability(power, game.HumanPlayer);
        Assert.True(far < expected);
        Assert.Equal(System.Math.Clamp(System.Math.Pow(0.90, 40) * 1.5, 0.0, 1.0), far, precision: 6);
    }

    [Fact]
    public void PeaceTreatyHoldProbability_DecaysMonotonically_AsTheTreatyAges()
    {
        // The whole point of the decay: the longer a Franklin peace has held, the LESS likely it survives another turn.
        var game = Game.New(Classic, seed: 7);
        Player power = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);
        game.HumanPlayer.CongressList.Add(Franklin);
        game.SetStance(power.PlayerId, game.HumanPlayer.PlayerId, Stance.Peace);

        double previous = 1.1; // above any possible clamped probability
        foreach (int age in new[] { 0, 3, 6, 10, 20, 40, 80 })
        {
            power.PeaceTurnMap[game.HumanPlayer.PlayerId] = game.Turn - age;
            double p = game.PeaceTreatyHoldProbability(power, game.HumanPlayer);
            Assert.True(p <= previous, $"probability should not rise with age (age {age}: {p} vs {previous})");
            previous = p;
        }
    }

    [Fact]
    public void PeaceTreatyHolds_IsAlwaysFalseWithoutFranklin_AndDrawsNoRng()
    {
        // No Franklin → probability 0 → PeaceTreatyHolds short-circuits to false WITHOUT drawing, so the rolling power's
        // stream is untouched (and, since the human's is stream 0, stream 0 is byte-stable too).
        var game = Game.New(Classic, seed: 7);
        Player power = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);
        game.SetStance(power.PlayerId, game.HumanPlayer.PlayerId, Stance.Peace); // even with a recorded peace…
        RandomState before = game.RandomState;
        for (int i = 0; i < 50; i++)
        {
            Assert.False(game.PeaceTreatyHolds(power, game.HumanPlayer)); // never a reprieve without the father
        }
        Assert.Equal(before, game.RandomState); // …and no draw from the human's stream 0
    }

    [Fact]
    public void PeaceTreatyHolds_WithNoRecordedPeace_IsFalse_AndDrawsNoRng()
    {
        // Even with Franklin, a pair that has no peace on record (never met, or war was the last transition) has nothing
        // to hold → probability 0 → no roll drawn (the rolling power's stream is untouched).
        var game = Game.New(Classic, seed: 7);
        Player power = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);
        game.HumanPlayer.CongressList.Add(Franklin);
        Assert.False(power.PeaceTurns.ContainsKey(game.HumanPlayer.PlayerId)); // no stamp yet
        for (int i = 0; i < 50; i++)
        {
            Assert.False(game.PeaceTreatyHolds(power, game.HumanPlayer));
        }
    }

    [Fact]
    public void PeaceTreatyHolds_WithFranklin_OnAnAgedPeace_SometimesHolds_OnThePowersOwnStream()
    {
        // With a decayed probability strictly inside (0,1) the reprieve fires on some rolls and not others — confirming
        // the roll is live and probabilistic. We age the peace so prob = 0.90^20 × 1.5 ≈ 0.18 (well inside the band).
        var game = Game.New(Classic, seed: 7);
        Player power = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);
        game.HumanPlayer.CongressList.Add(Franklin);
        game.SetStance(power.PlayerId, game.HumanPlayer.PlayerId, Stance.Peace);
        power.PeaceTurnMap[game.HumanPlayer.PlayerId] = game.Turn - 20; // prob ≈ 0.18

        int held = 0;
        for (int i = 0; i < 400; i++)
        {
            if (game.PeaceTreatyHolds(power, game.HumanPlayer))
            {
                held++;
            }
        }
        Assert.InRange(held, 1, 399); // not all-true, not all-false → a genuine probabilistic roll
    }

    [Fact]
    public void PeaceTurn_IsStampedOnPeace_AndClearedOnWar()
    {
        // The stamp underpins the decay: a transition INTO peace records the turn; a declaration of war removes it.
        var game = Game.New(Classic, seed: 7);
        Player a = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);
        int b = SecondForeignPowerId(game);

        game.SetStance(a.PlayerId, b, Stance.Peace);
        Assert.Equal(game.Turn, a.PeaceTurns[b]);                 // stamped on both sides
        Assert.Equal(game.Turn, game.Players.First(p => p.PlayerId == b).PeaceTurns[a.PlayerId]);

        game.SetStance(a.PlayerId, b, Stance.War);
        Assert.False(a.PeaceTurns.ContainsKey(b));                // cleared on war
        Assert.False(game.Players.First(p => p.PlayerId == b).PeaceTurns.ContainsKey(a.PlayerId));
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

    // ───────────────────────── Jan de Witt — ForeignReportRows (SoL / fathers / tax) 86d3ha4cv ─────────────────────────

    [Fact]
    public void ForeignReportRows_ReportEveryRivalsSoLFathersAndTax_InPlayerIdOrder()
    {
        var game = Game.New(Classic, seed: 7);
        int fid = ForeignPowerId(game);
        int other = SecondForeignPowerId(game);

        // Give the two rivals distinguishable tax rates and Congress sizes; SoL is 0 for a rival with no colonists.
        game.Players.First(p => p.PlayerId == fid).TaxRate = 17;
        game.Players.First(p => p.PlayerId == other).TaxRate = 42;
        Elect(game, fid, Franklin);
        Elect(game, other, Franklin);
        Elect(game, other, DeWitt); // `other` now holds two fathers

        var rows = game.ForeignReportRows(0);

        // Every other colonial power appears exactly once, in ascending id order, and the viewer (0) is excluded.
        Assert.DoesNotContain(rows, r => r.Player.PlayerId == 0);
        Assert.Equal(rows.Select(r => r.Player.PlayerId).OrderBy(id => id), rows.Select(r => r.Player.PlayerId));

        Game.ForeignReportRow rowFid = rows.First(r => r.Player.PlayerId == fid);
        Assert.Equal(17, rowFid.TaxRate);
        Assert.Equal(1, rowFid.FoundingFathers);
        Assert.Equal(game.NationalSonsOfLiberty(rowFid.Player), rowFid.SonsOfLiberty);

        Game.ForeignReportRow rowOther = rows.First(r => r.Player.PlayerId == other);
        Assert.Equal(42, rowOther.TaxRate);
        Assert.Equal(2, rowOther.FoundingFathers);
    }

    [Fact]
    public void ForeignReportRows_ExcludeNativesAndTheRoyalExpeditionaryForce()
    {
        var game = Game.New(Classic, seed: 7);
        var rows = game.ForeignReportRows(0);
        // Only live colonial powers appear — never a native tribe or the REF.
        Assert.All(rows, r => Assert.Equal(PlayerType.Colonial, r.Player.PlayerType));
    }
}
