using System.Xml.Linq;
using CrownAndColony.GameLogic.Combat;
using CrownAndColony.GameLogic.Specification;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Specification;

/// <summary>
/// The unattached, top-level combat <c>&lt;modifier&gt;</c> percentages (<c>86d3drpgg</c>): FreeCol's
/// <c>GENERAL_COMBAT_INDEX</c> situational bonuses (attack bonus, the two movement penalties, the
/// amphibious / artillery-in-the-open penalties, the artillery-against-raid + fortified bonuses, and the
/// naval cargo penalty) are parsed once into <see cref="CombatModifiers"/> on <see cref="Ruleset.CombatModifiers"/>
/// and read by <see cref="CombatModel"/>, replacing the hardcoded constants. Pure refactor — the classic values are
/// unchanged, so the default game is byte-identical (ADR-009).
/// </summary>
public class CombatModifiersTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    [Fact]
    public void ClassicRuleset_ParsesEachCombatModifier_FromTheSpecAsAFraction()
    {
        // Pinned against the classic specification.xml top-level <modifiers> section (percentages /100 into fractions).
        CombatModifiers m = Classic.CombatModifiers;
        Assert.Equal(0.50, m.AttackBonus, 5);                   // model.modifier.attackBonus = 50
        Assert.Equal(-0.33, m.SmallMovementPenalty, 5);         // model.modifier.smallMovementPenalty = -33
        Assert.Equal(-0.66, m.BigMovementPenalty, 5);           // model.modifier.bigMovementPenalty = -66
        Assert.Equal(-0.75, m.AmphibiousPenalty, 5);            // model.modifier.amphibiousAttack = -75
        Assert.Equal(-0.75, m.ArtilleryInOpenPenalty, 5);       // model.modifier.artilleryInTheOpen = -75
        Assert.Equal(1.00, m.ArtilleryAgainstRaidBonus, 5);     // model.modifier.artilleryAgainstRaid = 100
        Assert.Equal(0.50, m.FortifiedBonus, 5);                // model.modifier.fortified = 50
        Assert.Equal(-0.125, m.CargoPenalty, 5);                // model.modifier.cargoPenalty = -12.5
        Assert.Equal(0.50, m.BombardBonus, 5);                  // model.modifier.bombardBonus = 50 (on the REF nation type)
    }

    [Fact]
    public void ClassicParsedModifiers_EqualTheHardcodedClassicValues()
    {
        // Guards the duplication: the parsed classic spec values must equal the hardcoded CombatModifiers.Classic
        // fallback, so the per-modifier fallback can never silently diverge from the data.
        Assert.Equal(CombatModifiers.Classic, Classic.CombatModifiers);
    }

    [Fact]
    public void ParseCombatModifiers_ReadsEachModifier_ByItsOwnId()
    {
        // Non-default values prove each id is actually read (not silently falling back to Classic).
        XElement section = XElement.Parse(
            "<modifiers>" +
            "  <modifier id='model.modifier.attackBonus' value='80' type='percentage'/>" +
            "  <modifier id='model.modifier.smallMovementPenalty' value='-20' type='percentage'/>" +
            "  <modifier id='model.modifier.bigMovementPenalty' value='-40' type='percentage'/>" +
            "  <modifier id='model.modifier.amphibiousAttack' value='-50' type='percentage'/>" +
            "  <modifier id='model.modifier.artilleryInTheOpen' value='-60' type='percentage'/>" +
            "  <modifier id='model.modifier.artilleryAgainstRaid' value='150' type='percentage'/>" +
            "  <modifier id='model.modifier.fortified' value='25' type='percentage'/>" +
            "  <modifier id='model.modifier.cargoPenalty' value='-10' type='percentage'/>" +
            "</modifiers>");
        CombatModifiers m = Ruleset.ParseCombatModifiers(section);
        Assert.Equal(0.80, m.AttackBonus, 5);
        Assert.Equal(-0.20, m.SmallMovementPenalty, 5);
        Assert.Equal(-0.40, m.BigMovementPenalty, 5);
        Assert.Equal(-0.50, m.AmphibiousPenalty, 5);
        Assert.Equal(-0.60, m.ArtilleryInOpenPenalty, 5);
        Assert.Equal(1.50, m.ArtilleryAgainstRaidBonus, 5);
        Assert.Equal(0.25, m.FortifiedBonus, 5);
        Assert.Equal(-0.10, m.CargoPenalty, 5);
    }

    [Fact]
    public void ParseCombatModifiers_FallsBackPerModifier_WhenOneIsMissing()
    {
        // The section exists but omits all but one modifier → those fall back to their classic values, the present one is read.
        XElement section = XElement.Parse(
            "<modifiers>" +
            "  <modifier id='model.modifier.fortified' value='90' type='percentage'/>" +
            "</modifiers>");
        CombatModifiers m = Ruleset.ParseCombatModifiers(section);
        Assert.Equal(0.90, m.FortifiedBonus, 5);
        Assert.Equal(CombatModifiers.Classic.AttackBonus, m.AttackBonus, 5);
        Assert.Equal(CombatModifiers.Classic.CargoPenalty, m.CargoPenalty, 5);
    }

    [Fact]
    public void ParseCombatModifiers_FallsBackEntirely_WhenTheSectionIsAbsent()
    {
        // A spec without a <modifiers> section still yields the full classic bundle (byte-identical default game).
        Assert.Equal(CombatModifiers.Classic, Ruleset.ParseCombatModifiers(null));
    }

    [Fact]
    public void ParseCombatModifiers_ReadsBombardBonus_FromTheRefNationTypeValue()
    {
        // bombardBonus lives on the REF nation type in the classic spec (not the top-level <modifiers>), so Load lifts
        // it and passes the percentage through — a non-50 value proves it is actually read, not a classic fallback.
        XElement section = XElement.Parse("<modifiers/>");
        CombatModifiers m = Ruleset.ParseCombatModifiers(section, refBombardBonusPercent: 75);
        Assert.Equal(0.75, m.BombardBonus, 5);
        // A REF type without the modifier (null) falls back to the classic +50%.
        Assert.Equal(0.50, Ruleset.ParseCombatModifiers(section, refBombardBonusPercent: null).BombardBonus, 5);
    }

    [Fact]
    public void ParseCombatModifiers_TopLevelBombardBonus_TakesPrecedenceOverTheRefValue()
    {
        // A variant may relocate bombardBonus into the top-level <modifiers> section; that wins over the REF value.
        XElement section = XElement.Parse(
            "<modifiers><modifier id='model.modifier.bombardBonus' value='30' type='percentage'/></modifiers>");
        CombatModifiers m = Ruleset.ParseCombatModifiers(section, refBombardBonusPercent: 75);
        Assert.Equal(0.30, m.BombardBonus, 5);
    }

    [Fact]
    public void CombatModel_NoArgOverloads_MatchTheClassicRulesetOverloads()
    {
        // Behaviour preservation: feeding the parsed classic ruleset modifiers gives the exact same power as the
        // hardcoded-default no-arg overloads — proving the classic combat path is unchanged when routed through data.
        CombatModifiers classic = Classic.CombatModifiers;

        var atk = new AttackContext(Movement: MovementPenalty.Big, GoodsCarried: 2);
        Assert.Equal(
            CombatModel.AttackPower(7, atk),
            CombatModel.AttackPower(7, atk, classic),
            10);

        var def = new DefenceContext(TerrainDefenceBonus: 100, Fortified: true, ArtilleryAgainstRaid: true, GoodsCarried: 1);
        Assert.Equal(
            CombatModel.DefencePower(5, def),
            CombatModel.DefencePower(5, def, classic),
            10);
    }
}
