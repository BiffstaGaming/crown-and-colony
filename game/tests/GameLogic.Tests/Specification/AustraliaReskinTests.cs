using System.Collections.Generic;
using System.Linq;
using CrownAndColony.GameLogic.Specification;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Specification;

/// <summary>
/// The Australian Federation <b>presentation reskin</b> (P8 tasks 86d3kwty1 goods / 86d3kwtvc units /
/// 86d3mm25r buildings / 86d3mm2nv retarget-pioneers / 86d3kwu0c chrome labels): a display-layer rename only
/// (ADR-018). The variant carries a <see cref="GameVariant.DisplayOverrides"/> map (keyed by ruleset SHORT NAME)
/// and a set of defaulted chrome-label fields; the underlying ruleset ids never change. Classic carries no
/// overrides and every chrome field at its classic default, so classic UI text is byte-identical.
/// Sources: docs/australian_federation_mode_md/17 (goods/units/buildings) and 19 (UI text / lore).
/// </summary>
public class AustraliaReskinTests
{
    // ───────────────────────── DisplayOverrides (entity renames) ─────────────────────────

    [Theory]
    // Goods (86d3kwty1)
    [InlineData("cotton", "Wool")]
    [InlineData("silver", "Gold")]
    [InlineData("bells", "Civic Voice")]
    // Goods: sealing/skins economy replacing the fur trade (playtest gap, 86d3mm2q4)
    [InlineData("furs", "Skins")]
    [InlineData("coats", "Leather")]
    // Units (86d3kwtvc + retarget-pioneers 86d3mm2nv)
    [InlineData("pettyCriminal", "Convict Labourer")]
    [InlineData("indenturedServant", "Emancipist")]
    [InlineData("freeColonist", "Free Settler")]
    [InlineData("expertSilverMiner", "Digger")]     // the gold specialist (silver = Gold stand-in)
    [InlineData("masterCottonPlanter", "Shepherd")] // the wool specialist (cotton = Wool stand-in)
    // Buildings (86d3mm25r)
    [InlineData("depot", "Government Stores")]
    [InlineData("weaverHouse", "Wool Shed")]
    [InlineData("weaverShop", "Shearing Shed")]
    // Buildings: remaining American-flavoured processing chains reskinned (playtest gap, 86d3mm2q4)
    [InlineData("distillerHouse", "Rum Still")]      // rum chain (early NSW "Rum Corps")
    [InlineData("furTraderHouse", "Skinner's Hut")]  // sealskins chain
    [InlineData("furTradingPost", "Tannery")]
    [InlineData("furFactory", "Leather Works")]
    [InlineData("tobacconistHouse", "Tobacco Shed")] // northern tobacco chain
    [InlineData("tobacconistShop", "Tobacco Works")]
    public void Australia_DisplayOverrides_RenameTheKeyEntities(string shortName, string australianName)
    {
        // The map carries the rename…
        Assert.True(GameVariants.Australia.DisplayOverrides.TryGetValue(shortName, out string? mapped),
            $"Australia.DisplayOverrides is missing '{shortName}'");
        Assert.Equal(australianName, mapped);

        // …and the humaniser honours it when given the variant's overrides.
        Assert.Equal(australianName, Naming.Humanize(shortName, GameVariants.Australia.DisplayOverrides));

        // Classic (no overrides) still shows the classic humanised form — NOT the Australian one.
        Assert.NotEqual(australianName, Naming.Humanize(shortName, GameVariants.ClassicAmerica.DisplayOverrides));
        Assert.NotEqual(australianName, Naming.Humanize(shortName)); // the single-arg (classic) overload too
    }

    [Fact]
    public void ClassicAmerica_HasNoDisplayOverrides()
    {
        // Classic is byte-identical: its override map is empty, so every name humanises the classic way.
        Assert.Empty(GameVariants.ClassicAmerica.DisplayOverrides);
        Assert.Equal("Cotton", Naming.Humanize("cotton", GameVariants.ClassicAmerica.DisplayOverrides));
        Assert.Equal("Free Colonist", Naming.Humanize("freeColonist", GameVariants.ClassicAmerica.DisplayOverrides));
        Assert.Equal("Bells", Naming.Humanize("bells", GameVariants.ClassicAmerica.DisplayOverrides));
    }

    [Fact]
    public void Overrides_AreKeyedByShortName_NotTheDottedId()
    {
        // The override map keys on the id suffix (short name), never the full ruleset id — the id is the
        // transposability anchor and is never a key. A full id must miss the map and fall through to the split.
        Assert.False(GameVariants.Australia.DisplayOverrides.ContainsKey("model.goods.cotton"));
        Assert.True(GameVariants.Australia.DisplayOverrides.ContainsKey("cotton"));
    }

    [Fact]
    public void Overrides_DoNotLeakIntoClassic_TransposabilityAnchorsUnchanged()
    {
        // Selecting a variant is the only thing that changes the display — the two maps share no state.
        Assert.NotSame(GameVariants.Australia.DisplayOverrides, GameVariants.ClassicAmerica.DisplayOverrides);
        // A short name the Australia map does NOT rename still humanises the same in both variants.
        Assert.Equal("Expert Farmer", Naming.Humanize("expertFarmer", GameVariants.Australia.DisplayOverrides));
        Assert.Equal("Expert Farmer", Naming.Humanize("expertFarmer", GameVariants.ClassicAmerica.DisplayOverrides));
    }

    [Theory]
    // The raw/processed goods a reskinned building chain works on are DELIBERATELY left classic (no forced rename):
    // sugar cane (Queensland) and northern tobacco are already historically Australian, and rum/cigars read fine, so
    // renaming them would only be noise. This guards that decision — these must NOT appear in the override map, and so
    // must humanise the classic way even under Australia (a building agreeing with an un-renamed good is intentional).
    [InlineData("sugar", "Sugar")]      // rum is made from sugar (kept classic)
    [InlineData("tobacco", "Tobacco")]  // cigars are made from tobacco (kept classic)
    [InlineData("rum", "Rum")]          // the Rum Still's output, already Australian-reading
    [InlineData("cigars", "Cigars")]    // the Tobacco Works' output
    [InlineData("cloth", "Cloth")]      // woven from Wool; classic name kept (doc 17 "Cloth/Textiles")
    public void Australia_LeavesConsistentGoodsClassic_NoForcedRename(string shortName, string classicName)
    {
        Assert.False(GameVariants.Australia.DisplayOverrides.ContainsKey(shortName),
            $"'{shortName}' should be left classic (no forced rename), not in the override map");
        Assert.Equal(classicName, Naming.Humanize(shortName, GameVariants.Australia.DisplayOverrides));
        Assert.Equal(classicName, Naming.Humanize(shortName, GameVariants.ClassicAmerica.DisplayOverrides));
    }

    // ───────────────────────── Chrome labels (86d3kwu0c) ─────────────────────────

    [Fact]
    public void CommonwealthVictoryText_IsSetForAustralia_WithTheHonestAddendum_AndNullForClassic()
    {
        // WS1.2: the Federation victory-screen text is variant-scoped (ADR-018). Australia carries the doc-19
        // proclamation + the BINDING honest addendum; classic carries none (→ the victory panel keeps its classic
        // "{winner} is victorious!" title + reason).
        GameVariant aus = GameVariants.Australia;
        Assert.Equal("The Commonwealth of Australia is proclaimed", aus.CommonwealthVictoryTitle);
        Assert.Contains("voted to federate", aus.CommonwealthProclamation);
        Assert.Contains("Aboriginal and Torres Strait Islander peoples", aus.CommonwealthAddendum);

        GameVariant classic = GameVariants.ClassicAmerica;
        Assert.Null(classic.CommonwealthVictoryTitle);
        Assert.Null(classic.CommonwealthProclamation);
        Assert.Null(classic.CommonwealthAddendum);
    }

    public static IEnumerable<object[]> ChromeLabels()
    {
        // (accessor, Australian value, classic default). Mirrors the CongressName test in AustralianPioneersTests.
        yield return new object[] { Field(v => v.CongressName), "Federation Convention", "Continental Congress" };
        yield return new object[] { Field(v => v.RebelSentimentName), "Federationists", "Sons of Liberty" };
        yield return new object[] { Field(v => v.RebelColonistName), "Federationist", "Rebel" };
        yield return new object[] { Field(v => v.LoyalistColonistName), "Imperial Loyalist", "Tory" };
        yield return new object[] { Field(v => v.RebelFactionName), "Federationists", "Rebels" };
        yield return new object[] { Field(v => v.LoyalistFactionName), "Imperial Loyalists", "Royalists" };
        yield return new object[] { Field(v => v.ExpeditionaryForceName), "Imperial Pressure", "Royal Expeditionary Force" };
        yield return new object[] { Field(v => v.ExpeditionaryForceAbbrev), "Imperial Pressure", "REF" };
        yield return new object[] { Field(v => v.DeclareIndependenceName), "Put Constitution to Referendum", "Declare Independence" };
        yield return new object[] { Field(v => v.WarOfIndependenceName), "Federation Referendum", "War of Independence" };
        yield return new object[] { Field(v => v.NativesName), "First Nations", "Native nations" };
        yield return new object[] { Field(v => v.NativeSettlementName), "First Nations community", "Native settlement" };
        yield return new object[] { Field(v => v.LibertyName), "Civic Voice", "liberty" };
    }

    [Theory]
    [MemberData(nameof(ChromeLabels))]
    public void ChromeLabel_IsAustralianForAustralia_AndClassicDefaultForClassic(
        System.Func<GameVariant, string> accessor, string australianValue, string classicDefault)
    {
        Assert.Equal(australianValue, accessor(GameVariants.Australia));
        Assert.Equal(classicDefault, accessor(GameVariants.ClassicAmerica));
    }

    /// <summary>Boxes a chrome-field accessor for the MemberData rows (keeps the delegate strongly typed).</summary>
    private static System.Func<GameVariant, string> Field(System.Func<GameVariant, string> accessor) => accessor;

    // ───────────────────────── Opening-cinematic beats (variant-aware intro) ─────────────────────────

    [Fact]
    public void OpeningBeats_AreAustralianForAustralia_AndClassicForClassic_BothNonEmpty()
    {
        IReadOnlyList<string> australia = GameVariants.Australia.OpeningBeats;
        IReadOnlyList<string> classic = GameVariants.ClassicAmerica.OpeningBeats;

        // Both variants ship a real, non-empty beat list…
        Assert.NotEmpty(australia);
        Assert.NotEmpty(classic);
        Assert.All(australia, beat => Assert.False(string.IsNullOrWhiteSpace(beat)));
        Assert.All(classic, beat => Assert.False(string.IsNullOrWhiteSpace(beat)));

        // …and the Australian intro is genuinely different from the classic 1492 one (the bug this fixes: the American
        // 1492 story used to play for every variant). Sequence-equality proves they diverge in content, not just count.
        Assert.False(australia.SequenceEqual(classic), "Australia must not reuse the classic 1492 beats");

        // Classic keeps the faithful 1492 opening (the byte-identical default); Australia opens on the 1788 First Fleet.
        Assert.Contains("1492", classic[0]);
        Assert.Contains("1788", australia[0]);
    }

    [Fact]
    public void ClassicOpeningBeats_AreTheSharedDefault_ForVariantsThatSupplyNone()
    {
        // A variant that supplies no beats of its own falls back to the shared classic default (same instance),
        // so classic intro text stays byte-identical no matter how variants are declared.
        Assert.Same(GameVariants.ClassicOpeningBeats, GameVariants.ClassicAmerica.OpeningBeats);
    }
}
