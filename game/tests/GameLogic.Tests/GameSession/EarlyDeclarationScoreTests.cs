using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// The early-declaration score bonus (<c>86d3fq0dc</c>): FreeCol's <c>csDeclareIndependence</c> stores
/// <c>max(0, independenceTurn − turn)</c> on the <c>DECLARE_INDEPENDENCE</c> history event and folds it into the
/// player score (<c>HistoryEvent.getScore</c>). The earlier a nation breaks away, the larger the bonus; declaring at
/// or after the historical independence turn yields none. Pure and RNG-free (ADR-009).
/// </summary>
public class EarlyDeclarationScoreTests
{
    private const ulong Seed = 0xEA12_DEC1UL;

    /// <summary>The classic ruleset reloaded with <c>model.option.independenceTurn</c> overridden to <paramref name="turn"/>.</summary>
    private static Ruleset ClassicWithIndependenceTurn(int turn)
    {
        var assembly = typeof(Ruleset).Assembly;
        using Stream raw = assembly.GetManifestResourceStream(GameVariants.ClassicSpecResource)!;
        XDocument doc = XDocument.Load(raw);
        doc.Descendants("integerOption")
            .Single(o => (string?)o.Attribute("id") == "model.option.independenceTurn")
            .SetAttributeValue("value", turn);
        using var patched = new MemoryStream(Encoding.UTF8.GetBytes(doc.ToString()));
        return Ruleset.Load(patched);
    }

    /// <summary>A rebellion-ready game (one full-SoL coastal colony) on the supplied ruleset.</summary>
    private static Game RebellionReady(Ruleset ruleset)
    {
        Game game = Game.New(ruleset, Seed);
        foreach (Position p in game.Map.AllPositions().Where(p =>
            !game.Map.TerrainAt(p).IsWater && game.ColonyAt(p) is null && game.NativeSettlementAt(p) is null
            && p.Neighbours().Any(n => game.Map.InBounds(n) && game.Map.TerrainAt(n).IsWater)
            && !game.Units.Any(u => u.IsOnMap && u.Position == p)))
        {
            Unit colonist = game.SpawnUnit(ruleset.Unit(Game.StartingUnitTypeId), p);
            if (game.CheckFoundColony(colonist).Allowed)
            {
                try
                {
                    Colony colony = game.FoundColony(colonist); // throws LandClaimRequiredException on native land
                    colony.Liberty = Colony.LibertyPerRebel * colony.Population; // national SoL → 100%
                    return game;
                }
                catch (LandClaimRequiredException)
                {
                    // native-owned tile — fall through and try the next
                }
            }
            game.Disband(colonist); // unfoundable site (mountain / native land) — try the next
        }
        throw new InvalidOperationException("No foundable coastal tile on this map.");
    }

    [Fact]
    public void DeclaringBeforeTheIndependenceTurn_RecordsThePositiveMargin()
    {
        // independenceTurn = 10; declaring at turn 1 → bonus max(0, 10 − 1) = 9, carried on the DECLARE_INDEPENDENCE
        // history event and summed into the human's history score.
        Game game = RebellionReady(ClassicWithIndependenceTurn(10));
        Assert.Equal(1, game.Turn);
        int historyBefore = game.HistoryEventScore;

        game.DeclareIndependence(game.HumanPlayer);

        HistoryEvent declare = game.History.Single(h => h.Kind == HistoryEventKind.DeclaredIndependence);
        Assert.Equal(9, declare.Score);
        Assert.Equal(historyBefore + 9, game.HistoryEventScore);
        Assert.Equal(9, game.ScoreBreakdown(game.HumanPlayer).HistoryPoints - historyBefore);
    }

    [Fact]
    public void DeclaringEarlier_YieldsALargerBonus_ThanDeclaringLater()
    {
        // Same ruleset (independenceTurn = 50); one game declares at turn 1, another after advancing a few turns.
        Ruleset ruleset = ClassicWithIndependenceTurn(50);

        Game early = RebellionReady(ruleset);
        int earlyTurn = early.Turn;
        early.DeclareIndependence(early.HumanPlayer);
        int earlyScore = early.History.Single(h => h.Kind == HistoryEventKind.DeclaredIndependence).Score;

        Game later = RebellionReady(ruleset);
        later.EndTurn();
        later.EndTurn();
        later.EndTurn(); // advance several turns so the same declaration is "later"
        int laterTurn = later.Turn;
        later.DeclareIndependence(later.HumanPlayer);
        int laterScore = later.History.Single(h => h.Kind == HistoryEventKind.DeclaredIndependence).Score;

        Assert.True(laterTurn > earlyTurn);
        Assert.Equal(50 - earlyTurn, earlyScore);
        Assert.Equal(50 - laterTurn, laterScore);
        Assert.True(earlyScore > laterScore); // declaring earlier scores more
    }

    [Fact]
    public void DeclaringAtOrAfterTheIndependenceTurn_YieldsNoEarlyBonus()
    {
        // independenceTurn = 1; at turn 1 the margin max(0, 1 − 1) = 0 — no early bonus once the historical turn arrives.
        Game game = RebellionReady(ClassicWithIndependenceTurn(1));
        Assert.Equal(1, game.Turn);

        game.DeclareIndependence(game.HumanPlayer);

        Assert.Equal(0, game.History.Single(h => h.Kind == HistoryEventKind.DeclaredIndependence).Score);
    }
}
