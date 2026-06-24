using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Specification;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Scenarios;

/// <summary>
/// L2 scenario test (docs/TESTING.md): runs many turns through the real engine loop and asserts the King's
/// <b>tax-raise cadence</b> stays in the FreeCol-classic band rather than the runaway behaviour reported in playtest
/// (ClickUp 86d3f674b). The bug was that, like FreeCol, our King had <em>no</em> inter-raise grace period, so the
/// per-turn weighted roll could demand a raise turn after turn and slam the tax into its 65% cap by the mid-game —
/// the FreeCol team's own acknowledged "more aggressive with tax increases than Col1 monarchs were". The fix gates the
/// two RAISE_TAX actions out of the chooser for <see cref="Game.TaxRaiseGraceTurns"/> turns after a demand, leaving the
/// per-turn probability and the increment formula FreeCol-exact. This test pins the corrected cadence so a regression
/// (e.g. dropping the cooldown) fails loudly.
/// </summary>
public class MonarchTaxCadenceScenarioTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    /// <summary>
    /// A player who founds one colony then always accepts every tax demand the King makes, played for 300 turns.
    /// Asserts the number of raises and the per-raise increment land in FreeCol's expected band — never the runaway
    /// (a raise nearly every turn, cap reached by the mid-game). Multiple seeds guard against a lucky single run.
    /// </summary>
    [Theory]
    [Trait("Category", "Scenario")]
    [InlineData(1UL)]
    [InlineData(7UL)]
    [InlineData(42UL)]
    [InlineData(0xC0FFEEUL)]
    public void OverThreeHundredTurns_TaxRaisesStayInTheFreeColBand(ulong seed)
    {
        Game game = Game.New(Classic, seed);
        game.FoundColony(game.Units.First(u => u.IsOnMap && u.Type.CanFoundColony));
        game.HumanPlayer.Gold = 10_000; // realistic: the full monarch action set is valid, diluting raises as in a real game

        int raises = 0;
        int maxSingleIncrement = 0;
        int minGapBetweenRaises = int.MaxValue;
        int lastRaiseTurn = -1;

        for (int i = 0; i < 300; i++)
        {
            game.EndTurn();
            if (game.PendingMonarchDemand is not { } demand)
            {
                continue;
            }
            if (demand.Action is MonarchAction.RaiseTaxAct or MonarchAction.RaiseTaxWar)
            {
                int from = game.HumanPlayer.TaxRate;
                game.RespondToMonarch(accept: true);
                int increment = game.HumanPlayer.TaxRate - from;
                raises++;
                maxSingleIncrement = Math.Max(maxSingleIncrement, increment);
                if (lastRaiseTurn >= 0)
                {
                    minGapBetweenRaises = Math.Min(minGapBetweenRaises, game.Turn - lastRaiseTurn);
                }
                lastRaiseTurn = game.Turn;
            }
            else
            {
                game.RespondToMonarch(accept: false); // a mercenary/Hessian offer etc — decline to clear it
            }
        }

        // Cadence: with the medium 9-turn inter-raise grace, ~12-20 raises land over 300 turns (vs the ~25-31 runaway
        // with no grace, which reached the 65% cap by the mid-game). The band is generous so seed variance never flakes.
        Assert.InRange(raises, 8, 22);

        // Increment: each raise is FreeCol's min(tax + 1 + rnd[0, 3 + turn/40), 65). Over 300 turns turn/40 ≤ 7, so the
        // largest possible single raise is 1 + 9 = 10. We never exceed that (the formula is untouched by the fix).
        Assert.InRange(maxSingleIncrement, 1, 10);

        // No clustering: consecutive raises are always at least the grace period apart (the heart of the fix). With at
        // least two raises in every run, the minimum gap must respect TaxRaiseGraceTurns.
        Assert.True(raises >= 2, "the King should raise tax at least twice over 300 turns");
        Assert.True(minGapBetweenRaises >= game.TaxRaiseGraceTurns,
            $"raises {minGapBetweenRaises} turns apart violate the {game.TaxRaiseGraceTurns}-turn inter-raise grace");

        // The tax should climb gradually, not slam the cap: by turn 300 it is well short of the 65% maximum.
        Assert.InRange(game.HumanPlayer.TaxRate, 1, 64);
    }
}
