using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Colonies;

/// <summary>
/// L1 unit tests for the colony preferred-size advisory (FreeCol <c>Colony.getPreferredSizeChange</c> /
/// <c>getUnitsToAdd</c> / <c>getUnitsToRemove</c> / <c>governmentChange</c>): the projected-population government
/// direction (+1/0/−1), the room-to-grow / crowd-to-shed counts (bounded at
/// <see cref="Colony.PreferredSizeChangeUpperBound"/> = 10), and the signed preferred-size hint. All derived from the
/// colony's banked <see cref="Colony.Liberty"/> at medium-difficulty government limits (Good 50 / Very-Good 100 SoL,
/// Bad 6 / Very-Bad 10 tories) — read-only, RNG-free (ADR-006/009).
/// </summary>
public class ColonyPreferredSizeTests
{
    private static Colony ColonyWith(int population, int liberty)
    {
        var colony = new Colony(1, "Test", new Position(0, 0), population);
        colony.Liberty = liberty;
        return colony;
    }

    // ── A. governmentChange direction at a projected population ──────────────────────────────────────────────

    [Fact]
    public void GovernmentChange_SamePopulation_IsZero()
    {
        // Projecting the colony's own current population changes nothing.
        Colony colony = ColonyWith(population: 5, liberty: 900); // SoL 90 → +1 bonus
        Assert.Equal(0, colony.GovernmentChange(5));
    }

    [Fact]
    public void GovernmentChange_GrowingPastGoodGovernment_Deteriorates()
    {
        // pop 5, liberty 900 → SoL 90 (Good). Spread over 10 the same liberty → SoL 45 (< Good): the +1 bonus is lost.
        Colony colony = ColonyWith(population: 5, liberty: 900);
        Assert.Equal(90, colony.SonsOfLiberty);
        Assert.Equal(-1, colony.GovernmentChange(10)); // 900·100/(200·10) = 45 < Good(50)
    }

    [Fact]
    public void GovernmentChange_ShrinkingOutOfVeryBad_Improves()
    {
        // pop 12, no liberty → 12 tories (Very-Bad, > 10). Shrink to 10 → 10 tories (not Very-Bad): government improves.
        Colony colony = ColonyWith(population: 12, liberty: 0);
        Assert.Equal(-2, colony.ProductionBonus);
        Assert.Equal(1, colony.GovernmentChange(10));
    }

    // ── B. UnitsToAdd — room to grow before the bonus is first lost ──────────────────────────────────────────

    [Fact]
    public void UnitsToAdd_StopsOneShortOfTheFirstDeterioration()
    {
        // pop 5, liberty 900 (SoL 90, Good). Growing dilutes SoL: it stays ≥ Good through pop 9 (SoL 50) and drops to
        // 45 at pop 10 — so 4 colonists can be added before the +1 government bonus is lost.
        Colony colony = ColonyWith(population: 5, liberty: 900);
        Assert.Equal(4, colony.UnitsToAdd());
    }

    [Fact]
    public void UnitsToAdd_IsBoundedByTheUpperBound()
    {
        // A tiny high-liberty colony never deteriorates within the 10-unit look-ahead → the bound caps the answer.
        Colony colony = ColonyWith(population: 1, liberty: 100_000); // SoL pinned at 100 across the whole look-ahead
        Assert.Equal(Colony.PreferredSizeChangeUpperBound, colony.UnitsToAdd());
    }

    // ── C. UnitsToRemove — crowd to shed to recover the bonus ────────────────────────────────────────────────

    [Fact]
    public void UnitsToRemove_ReturnsTheFirstImprovingReduction()
    {
        // pop 12, no liberty → 12 tories (Very-Bad). Removing 2 → 10 tories (out of Very-Bad): the first improvement.
        Colony colony = ColonyWith(population: 12, liberty: 0);
        Assert.Equal(2, colony.UnitsToRemove());
    }

    [Fact]
    public void UnitsToRemove_IsZeroWhenNoReductionHelps()
    {
        // A colony already at full SoL (Very-Good government, +2) is at its best — shrinking cannot improve it.
        Colony colony = ColonyWith(population: 5, liberty: 1000); // SoL 100
        Assert.Equal(100, colony.SonsOfLiberty);
        Assert.Equal(0, colony.UnitsToRemove());
    }

    // ── D. PreferredSizeChange — the signed hint ─────────────────────────────────────────────────────────────

    [Fact]
    public void PreferredSizeChange_HealthyColony_IsPositiveRoomToGrow()
    {
        // Bonus ≥ 0 → the colony wants to grow: the (positive) units-to-add.
        Colony colony = ColonyWith(population: 5, liberty: 900);
        Assert.True(colony.ProductionBonus >= 0);
        Assert.Equal(colony.UnitsToAdd(), colony.PreferredSizeChange());
        Assert.Equal(4, colony.PreferredSizeChange());
    }

    [Fact]
    public void PreferredSizeChange_OvercrowdedLowSoL_IsNegativeCrowdToShed()
    {
        // Bonus < 0 → the colony wants to shrink: the negation of the units-to-remove.
        Colony colony = ColonyWith(population: 12, liberty: 0);
        Assert.Equal(-2, colony.ProductionBonus);
        Assert.Equal(-2, colony.PreferredSizeChange()); // −UnitsToRemove (2)
    }

    [Fact]
    public void PreferredSizeChange_EmptyColony_MatchesFreeColMath()
    {
        // An edge case FreeCol never actually hits (real colonies have ≥ 1 colonist): with no population and no
        // liberty the government stays neutral until a 7th projected tory would trip Bad-government, so getUnitsToAdd
        // reports 6. Documents the bit-exact governmentChange loop rather than asserting a designed value.
        Colony colony = ColonyWith(population: 0, liberty: 0);
        Assert.Equal(0, colony.ProductionBonus);
        Assert.Equal(6, colony.PreferredSizeChange());
    }
}
