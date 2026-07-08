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

    [Fact]
    public void Humanize_HonoursAVariantOverride_BeforeTheGlobalRules()
    {
        // The Australian Federation relabels ids at the display layer only (ADR-018) — the engine still keys on
        // the id (model.goods.bells / cotton), so a variant override renames what the player reads, not the data.
        var aus = new Dictionary<string, string> { ["bells"] = "Civic Voice", ["cotton"] = "Wool" };
        Assert.Equal("Civic Voice", Naming.Humanize("bells", aus));
        Assert.Equal("Wool", Naming.Humanize("cotton", aus));
        // Anything the variant does not name humanises exactly as before…
        Assert.Equal("Free Colonist", Naming.Humanize("freeColonist", aus));
        // …and the global override still applies through the variant path.
        Assert.Equal("Pasture", Naming.Humanize("country", aus));
    }

    [Fact]
    public void Humanize_WithNoVariantOverrides_IsByteIdenticalToTheClassicText()
    {
        // Classic passes no overrides (or an empty map) → identical output to the single-arg humaniser.
        Assert.Equal("Bells", Naming.Humanize("bells", null));
        Assert.Equal("Bells", Naming.Humanize("bells", new Dictionary<string, string>()));
        Assert.Equal(Naming.Humanize("tobacco"), Naming.Humanize("tobacco", null));
    }

    [Fact]
    public void ClassicVariant_CarriesNoDisplayOverrides()
    {
        // The transposability seam: classic renames nothing, so its UI text is unchanged (byte-identical).
        Assert.Empty(GameVariants.ClassicAmerica.DisplayOverrides);
    }
}
