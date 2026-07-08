using CrownAndColony.GameLogic.Specification;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Specification;

/// <summary>
/// L1 for <see cref="Naming.Humanize"/> — the single shared display-name humaniser every UI site uses
/// (the panels' private duplicates were folded into it, 2026-07-08).
/// </summary>
public class NamingTests
{
    [Theory]
    [InlineData("freeColonist", "Free Colonist")]
    [InlineData("tobacconistHouse", "Tobacconist House")]
    [InlineData("expertOreMiner", "Expert Ore Miner")]
    [InlineData("food", "Food")]
    [InlineData("", "")]
    public void Humanize_SplitsCamelCase_AndCapitalises(string shortName, string expected) =>
        Assert.Equal(expected, Naming.Humanize(shortName));

    [Fact]
    public void Humanize_DisplaysTheBasePasture_AsPasture_NotCountry()
    {
        // FreeCol's model.building.country is the base horse pasture — "Country" is meaningless to a player
        // (Chris 2026-07-08). Display-only: the ruleset id stays "country" (data fidelity).
        Assert.Equal("Pasture", Naming.Humanize("country"));
    }

    [Fact]
    public void Humanize_LeavesTheUpgrades_UnderTheirOwnNames()
    {
        // Only the base pasture is overridden; its upgrade tier keeps its literal name.
        Assert.Equal("Stables", Naming.Humanize("stables"));
        Assert.Equal("Depot", Naming.Humanize("depot"));
    }
}
