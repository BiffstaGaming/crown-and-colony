using System.Collections.Generic;
using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// WS3.4 — the five Commonwealth victory grades and the six-category scorecard they are read from (design docs
/// <c>05_Federation_Victory_System.md</c> "Optional victory grades" and <c>20_Balancing_Notes.md</c> "Victory grade
/// scoring"; <c>docs/systems/federation-victory.md</c>). Verifies:
/// <list type="bullet">
///   <item>classic reads an empty, <see cref="CommonwealthGrade.None"/> scorecard (ADR-009 — nothing read, nothing shifted);</item>
///   <item>each category scores the board it is supposed to score, and saturates at 100;</item>
///   <item>a bare federation lands on <see cref="CommonwealthGrade.Bare"/> — the grade is not a participation award;</item>
///   <item>each distinguished grade is awarded when (and only when) its category clears the bar;</item>
///   <item>the tie-break walks toward the rarer grade;</item>
///   <item>the oracle is pure — calling it never moves the game on.</item>
/// </list>
/// </summary>
public class CommonwealthGradeTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private static readonly Ruleset Australia = GameVariants.Australia.LoadRuleset();
    /// <summary>The same fixed seed <see cref="FederationVictoryTests"/> uses — a board where all six start sites can be
    /// founded without a land claim, so the scorecard is read off an unprovoked board (the oracle itself draws no RNG).</summary>
    private const ulong Seed = 0xFED0A05UL;

    // ─────────────────────────────── classic is untouched ───────────────────────────────

    [Fact]
    public void Classic_ScoresNothing_AndIsGradedNone()
    {
        Game game = Game.New(Classic, Seed);
        Found(game);

        CommonwealthScorecard card = game.CommonwealthScorecardForHuman();

        Assert.Equal(CommonwealthGrade.None, card.Grade);
        Assert.Equal(0, card.Total);
        Assert.Equal(0, card.Federation);
        Assert.Equal(0, card.Economy);
        Assert.Equal(0, card.CivicReform);
        Assert.Equal(0, card.FirstNations);
        Assert.Equal(0, card.Stability);
        Assert.Equal(0, card.HistoricalBreadth);
    }

    // ─────────────────────────────── the oracle is pure ───────────────────────────────

    [Fact]
    public void Scorecard_IsPure_ReadingItTwiceChangesNothing()
    {
        Game game = NewAustralia();
        FoundAllSixRegions(game, out Dictionary<AustraliaColony, Colony> colonies);
        foreach (Colony colony in colonies.Values)
        {
            SetSupportPercent(colony, 60);
        }

        CommonwealthScorecard first = game.CommonwealthScorecardForHuman();
        int turn = game.Turn;
        int points = game.ConventionPoints;
        CommonwealthScorecard second = game.CommonwealthScorecardForHuman();

        Assert.Equal(first, second);          // same board ⇒ same reading (record equality)
        Assert.Equal(turn, game.Turn);        // …and the read moved nothing on
        Assert.Equal(points, game.ConventionPoints);
    }

    // ─────────────────────────────── Federation category (margin against each region's own target) ───────────────────────────────

    [Fact]
    public void FederationCategory_ScoresFiftyAtTarget_AndRisesWithTheMargin()
    {
        Game game = NewAustralia();
        FoundAllSixRegions(game, out Dictionary<AustraliaColony, Colony> colonies);

        // Every region sitting exactly on its own historical target reads as the 50 mid-point.
        foreach ((AustraliaColony key, Colony colony) in colonies)
        {
            SetSupportPercent(colony, game.ReferendumTargetFor(RegionKeyOf(key)));
        }
        int atTarget = game.CommonwealthScorecardForHuman().Federation;

        // Pushing every region to a full 100% support can only raise it.
        foreach (Colony colony in colonies.Values)
        {
            SetSupportPercent(colony, 100);
        }
        int atCeiling = game.CommonwealthScorecardForHuman().Federation;

        Assert.InRange(atTarget, 48, 52); // integer region averaging allows a point of rounding either way
        Assert.True(atCeiling > atTarget, $"a bigger margin must score higher ({atCeiling} vs {atTarget})");
    }

    [Fact]
    public void FederationCategory_ScoresZero_WhenNothingIsSettled()
    {
        Game game = NewAustralia();

        Assert.Equal(0, game.CommonwealthScorecardForHuman().Federation);
    }

    // ─────────────────────────────── Civic reform ───────────────────────────────

    [Fact]
    public void CivicReform_RisesWithNewspapersAndSchools()
    {
        Game game = NewAustralia();
        FoundAllSixRegions(game, out Dictionary<AustraliaColony, Colony> colonies);
        int bare = game.CommonwealthScorecardForHuman().CivicReform;

        foreach (Colony colony in colonies.Values)
        {
            colony.AddBuilding("model.building.newspaper");
            colony.AddBuilding("model.building.schoolhouse");
        }
        int civic = game.CommonwealthScorecardForHuman().CivicReform;

        Assert.True(civic > bare, $"press + schools must lift civic reform ({civic} vs {bare})");
    }

    // ─────────────────────────────── Stability ───────────────────────────────

    [Fact]
    public void Stability_FallsWithAntiFederationOpposition()
    {
        Game game = NewAustralia();
        FoundAllSixRegions(game, out Dictionary<AustraliaColony, Colony> colonies);
        int calm = game.CommonwealthScorecardForHuman().Stability;

        foreach (Colony colony in colonies.Values)
        {
            colony.AntiFederation = 60; // the AntiFederationCap — maximum opposition
        }
        int unrest = game.CommonwealthScorecardForHuman().Stability;

        Assert.True(unrest < calm, $"maximum opposition must cut stability ({unrest} vs {calm})");
    }

    // ─────────────────────────────── grade awarding ───────────────────────────────

    [Fact]
    public void BareFederation_IsTheFloor_WhenNoCategoryClearsTheBar()
    {
        // Six colonies carrying their referendums but nothing else built: the union carries, and no more.
        Game game = NewAustralia();
        FoundAllSixRegions(game, out Dictionary<AustraliaColony, Colony> colonies);
        foreach (Colony colony in colonies.Values)
        {
            SetSupportPercent(colony, 100);
        }

        CommonwealthScorecard card = game.CommonwealthScorecardForHuman();

        Assert.Equal(CommonwealthGrade.Bare, card.Grade);
        Assert.True(card.Federation >= 50, "the federation category itself should still read well");
    }

    [Fact]
    public void ReformCommonwealth_IsAwarded_WhenCivicInstitutionsClearTheBar()
    {
        Game game = NewAustralia();
        FoundAllSixRegions(game, out Dictionary<AustraliaColony, Colony> colonies);
        MakeCivicReformers(game, colonies.Values);

        CommonwealthScorecard card = game.CommonwealthScorecardForHuman();

        Assert.True(card.CivicReform >= 70, $"the civic-reform board should clear the bar (got {card.CivicReform})");
        Assert.Equal(CommonwealthGrade.Reform, card.Grade);
    }

    [Fact]
    public void EconomicCommonwealth_IsAwarded_WhenTheExportEconomyClearsTheBar()
    {
        Game game = NewAustralia();
        FoundAllSixRegions(game, out Dictionary<AustraliaColony, Colony> colonies);
        MakeEconomicPowerhouse(game, colonies.Values);

        CommonwealthScorecard card = game.CommonwealthScorecardForHuman();

        Assert.True(card.Economy >= 70, $"the economy board should clear the bar (got {card.Economy})");
        Assert.Equal(CommonwealthGrade.Economic, card.Grade);
    }

    [Fact]
    public void TieBreak_WalksTowardTheRarerGrade()
    {
        // A board that clears the bar on BOTH civic reform and the economy must be graded Economic, not Reform:
        // the tie-break order is Stable < Reform < Economic (rarest last).
        Game game = NewAustralia();
        FoundAllSixRegions(game, out Dictionary<AustraliaColony, Colony> colonies);
        MakeCivicReformers(game, colonies.Values);
        MakeEconomicPowerhouse(game, colonies.Values);

        CommonwealthScorecard card = game.CommonwealthScorecardForHuman();

        Assert.True(card.CivicReform >= 70 && card.Economy >= 70, "both categories must clear the bar for this to test the tie-break");
        Assert.Equal(CommonwealthGrade.Economic, card.Grade);
    }

    // ─────────────────────────────── First Nations category + the Treaty grade ───────────────────────────────

    [Fact]
    public void DoingNothing_ScoresPoorlyOnFirstNations_AndIsNotAwardedTreaty()
    {
        // The regression this category was rewritten to prevent: when it scored only "harm avoided", a player who never
        // encountered First Nations at all read a perfect 100 and would have been handed the game's RAREST grade for
        // doing nothing. Earned Respect is now half the score, and it starts at zero.
        Game game = NewAustralia();
        FoundAllSixRegions(game, out Dictionary<AustraliaColony, Colony> colonies);
        foreach (Colony colony in colonies.Values)
        {
            SetSupportPercent(colony, 100);
        }

        CommonwealthScorecard card = game.CommonwealthScorecardForHuman();

        Assert.True(card.FirstNations < 70,
            $"an untouched board must not clear the First Nations bar (got {card.FirstNations})");
        Assert.NotEqual(CommonwealthGrade.Treaty, card.Grade);
    }

    [Fact]
    public void EarnedRespect_LiftsTheFirstNationsScore_AndCanWinTheTreatyGrade()
    {
        // Respect only moves through conduct, so this is the honest opposite case: a colonist who has built real trust
        // with every people on the map earns the grade that cannot be bought with production.
        Game game = NewAustralia();
        FoundAllSixRegions(game, out Dictionary<AustraliaColony, Colony> colonies);
        foreach (Colony colony in colonies.Values)
        {
            SetSupportPercent(colony, 100);
        }
        int bare = game.CommonwealthScorecardForHuman().FirstNations;

        foreach (string nation in game.NativeSettlements.Select(s => s.NationTypeId).Distinct())
        {
            game.RecordFirstNationsContact(nation);
            game.ChangeFirstNationsRespect(nation, 100); // clamps at the ceiling
        }

        CommonwealthScorecard card = game.CommonwealthScorecardForHuman();

        Assert.True(card.FirstNations > bare, $"earned Respect must lift the category ({card.FirstNations} vs {bare})");
        Assert.True(card.FirstNations >= 70, $"full Respect on an uninflamed board should clear the bar (got {card.FirstNations})");
        Assert.Equal(CommonwealthGrade.Treaty, card.Grade);
    }

    // ─────────────────────────────── helpers ───────────────────────────────

    private static Game NewAustralia() => Game.New(Australia, Seed, mapSource: MapSource.Australia);

    private static Colony Found(Game game) =>
        game.FoundColony(game.Units.First(u => u.IsOnMap && u.Type.CanFoundColony));

    private static Colony FoundIn(Game game, AustraliaColony colony)
    {
        Position tile = AustraliaColonyStart.StartTile(colony);
        Unit colonist = game.SpawnUnit(Australia.Unit(Colony.FreeColonistTypeId), tile);
        return game.FoundColony(colonist);
    }

    private static void FoundAllSixRegions(Game game, out Dictionary<AustraliaColony, Colony> colonies)
    {
        colonies = new Dictionary<AustraliaColony, Colony>();
        foreach (AustraliaColony colony in AustraliaColonyStart.All)
        {
            colonies[colony] = FoundIn(game, colony);
        }
    }

    /// <summary>The <see cref="Game.FederationRegionKeys"/> entry for a start colony, in the canonical NSW/Vic/Qld/SA/Tas/WA order.</summary>
    private static string RegionKeyOf(AustraliaColony colony) => colony switch
    {
        AustraliaColony.NewSouthWales => "model.region.newSouthWales",
        AustraliaColony.Victoria => "model.region.victoria",
        AustraliaColony.Queensland => "model.region.queensland",
        AustraliaColony.SouthAustralia => "model.region.southAustralia",
        AustraliaColony.Tasmania => "model.region.tasmania",
        _ => "model.region.westernAustralia",
    };

    private static void SetSupportPercent(Colony colony, int percent)
    {
        colony.FederationSupport = 0;
        colony.AddFederationSupport(colony.RebelLibertyDivisor * colony.Population * percent / 100);
    }

    /// <summary>Builds the civic-reform board: both reform Pioneers seated, plus a newspaper and a school in every colony.</summary>
    private static void MakeCivicReformers(Game game, IEnumerable<Colony> colonies)
    {
        Player human = game.Players.First(p => p.IsHuman);
        human.CongressList.Add("model.foundingFather.catherineHelenSpence");
        human.CongressList.Add("model.foundingFather.maryLee");
        foreach (Colony colony in colonies)
        {
            colony.AddBuilding("model.building.newspaper");
            colony.AddBuilding("model.building.schoolhouse");
        }
    }

    /// <summary>Builds the economic board: a full treasury, a diverse warehouse and a deep set of built buildings.</summary>
    private static void MakeEconomicPowerhouse(Game game, IEnumerable<Colony> colonies)
    {
        game.Players.First(p => p.IsHuman).Gold = 20_000;
        string[] exports =
        [
            "model.goods.gold", "model.goods.wool", "model.goods.coal", "model.goods.copper",
            "model.goods.sandalwood", "model.goods.cattle", "model.goods.meat", "model.goods.frozenMeat",
            "model.goods.ore", "model.goods.lumber", "model.goods.furs", "model.goods.tools",
        ];
        string[] buildings =
        [
            "model.building.docks", "model.building.newspaper", "model.building.schoolhouse",
            "model.building.warehouse", "model.building.stables", "model.building.church",
            "model.building.lumberMill", "model.building.blacksmithHouse", "model.building.tobacconistHouse",
            "model.building.weaverHouse", "model.building.distillerHouse", "model.building.furTraderHouse",
            "model.building.printingPress", "model.building.armory", "model.building.stockade",
            "model.building.townHall", "model.building.carpenterHouse", "model.building.chapel",
            "model.building.depot", "model.building.country",
        ];
        foreach (Colony colony in colonies)
        {
            foreach (string goodsId in exports)
            {
                colony.AddGoods(goodsId, 50);
            }
            foreach (string buildingId in buildings)
            {
                colony.AddBuilding(buildingId);
            }
        }
    }
}
