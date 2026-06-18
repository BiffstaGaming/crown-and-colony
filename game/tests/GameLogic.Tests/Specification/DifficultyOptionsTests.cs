using System.Xml.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Specification;

/// <summary>
/// Difficulty-level system (<c>86d3c9y08</c>) slice 1: parse the chosen <c>model.difficulty.*</c> level's tuning
/// options (founding-father factor, units-that-use-no-bells) and route the formerly-hardcoded constants through it.
/// Default level is <c>medium</c>; behaviour-preserving (medium values equal the old consts).
/// </summary>
public class DifficultyOptionsTests
{
    /// <summary>A spec with three levels whose founding-father factor differs per level (mirrors the classic 24/40/56).</summary>
    private static XElement ThreeLevelSpec() => XElement.Parse(
        "<freecol-specification><options>" +
        "  <optionGroup id='difficultyLevels' recursive='false'>" +
        "    <optionGroup id='model.difficulty.veryEasy'><optionGroup id='model.difficulty.other'>" +
        "      <integerOption id='model.option.foundingFatherFactor' value='24' />" +
        "      <integerOption id='model.option.unitsThatUseNoBells' value='2' />" +
        "    </optionGroup></optionGroup>" +
        "    <optionGroup id='model.difficulty.medium'><optionGroup id='model.difficulty.other'>" +
        "      <integerOption id='model.option.foundingFatherFactor' value='40' />" +
        "      <integerOption id='model.option.unitsThatUseNoBells' value='2' />" +
        "    </optionGroup></optionGroup>" +
        "    <optionGroup id='model.difficulty.veryHard'><optionGroup id='model.difficulty.other'>" +
        "      <integerOption id='model.option.foundingFatherFactor' value='56' />" +
        "      <integerOption id='model.option.unitsThatUseNoBells' value='3' />" +
        "    </optionGroup></optionGroup>" +
        "  </optionGroup>" +
        "</options></freecol-specification>");

    [Fact]
    public void ParseDifficulty_DefaultsToMedium_NotTheFirstLevel()
    {
        // The risk: an unscoped search would hit veryEasy (the first level) and return 24. It must return medium's 40.
        DifficultyOptions d = Ruleset.ParseDifficulty(ThreeLevelSpec());
        Assert.Equal(40, d.FoundingFatherFactor);
        Assert.Equal(2, d.UnitsThatUseNoBells);
    }

    [Theory]
    [InlineData("model.difficulty.veryEasy", 24, 2)]
    [InlineData("model.difficulty.medium", 40, 2)]
    [InlineData("model.difficulty.veryHard", 56, 3)]
    public void ParseDifficulty_SelectsTheNamedLevelsSubtree(string levelId, int factor, int noBells)
    {
        DifficultyOptions d = Ruleset.ParseDifficulty(ThreeLevelSpec(), levelId);
        Assert.Equal(factor, d.FoundingFatherFactor);
        Assert.Equal(noBells, d.UnitsThatUseNoBells);
    }

    [Fact]
    public void ParseDifficulty_FallsBackToClassicMedium_WhenLevelAbsent()
    {
        Assert.Equal(DifficultyOptions.ClassicMedium, Ruleset.ParseDifficulty(ThreeLevelSpec(), "model.difficulty.nonexistent"));
    }

    [Fact]
    public void ParseDifficulty_FallsBackPerOption_WhenAnOptionIsMissingFromTheLevel()
    {
        // The level exists but omits foundingFatherFactor → that option falls back to the medium default (40).
        XElement root = XElement.Parse(
            "<freecol-specification><optionGroup id='model.difficulty.medium'>" +
            "  <integerOption id='model.option.unitsThatUseNoBells' value='2' />" +
            "</optionGroup></freecol-specification>");
        DifficultyOptions d = Ruleset.ParseDifficulty(root);
        Assert.Equal(DifficultyOptions.ClassicMedium.FoundingFatherFactor, d.FoundingFatherFactor); // 40
        Assert.Equal(2, d.UnitsThatUseNoBells);
    }

    // ── Real classic spec + behaviour preservation ───────────────────────────────────────────────────────────────

    [Fact]
    public void ClassicRuleset_ParsesTheMediumDifficultyValues()
    {
        DifficultyOptions d = Ruleset.LoadClassic().Difficulty;
        Assert.Equal(40, d.FoundingFatherFactor); // medium (would be 24 if veryEasy were wrongly selected)
        Assert.Equal(2, d.UnitsThatUseNoBells);
    }

    [Fact]
    public void ClassicMedium_MatchesTheParsedMediumLevel()
    {
        // Guards the duplication: the hardcoded ClassicMedium fallback must equal the spec's medium level, so a
        // per-option fallback can never silently diverge from the data as more options are routed through.
        Assert.Equal(DifficultyOptions.ClassicMedium, Ruleset.LoadClassic().Difficulty);
    }

    [Fact]
    public void FoundingFatherCost_IsUnchanged_RoutingThroughDifficulty()
    {
        // The first father costs `factor` (40) — same as before the const was routed through Ruleset.Difficulty.
        Game game = Game.New(Ruleset.LoadClassic(), 0xC0FFEEUL);
        Assert.Equal(40, game.TotalFoundingFatherCost());
    }

    // ── Government limits (slice 2) ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ParseDifficulty_ReadsGovernmentLimits_ByTheirOwnIds()
    {
        // Non-default values prove each of the four ids is actually read (not silently falling back to ClassicMedium).
        XElement root = XElement.Parse(
            "<freecol-specification><optionGroup id='model.difficulty.medium'>" +
            "  <integerOption id='model.option.veryGoodGovernmentLimit' value='99' />" +
            "  <integerOption id='model.option.goodGovernmentLimit' value='55' />" +
            "  <integerOption id='model.option.badGovernmentLimit' value='5' />" +
            "  <integerOption id='model.option.veryBadGovernmentLimit' value='9' />" +
            "</optionGroup></freecol-specification>");
        Assert.Equal(new GovernmentLimits(VeryGood: 99, Good: 55, Bad: 5, VeryBad: 9), Ruleset.ParseDifficulty(root).Government);
    }

    [Fact]
    public void ClassicRuleset_ParsesTheMediumGovernmentLimits()
    {
        Assert.Equal(new GovernmentLimits(VeryGood: 100, Good: 50, Bad: 6, VeryBad: 10), Ruleset.LoadClassic().Difficulty.Government);
    }

    [Fact]
    public void GovernmentLimits_DriveTheColonyProductionBonus()
    {
        // 6 tories, SoL 0. At medium (bad limit 6) that is not "bad government" (6 is not > 6) → bonus 0.
        var colony = new Colony(1, "Gov", new Position(0, 0), population: 6);
        Assert.Equal(0, colony.ProductionBonus);

        // A harder level tightens the bad limit to 5 → 6 tories now trip "bad government" → −1. Proves the routing.
        colony.Government = new GovernmentLimits(VeryGood: 100, Good: 50, Bad: 5, VeryBad: 9);
        Assert.Equal(-1, colony.ProductionBonus);
    }

    // ── Natives group (slice 3) ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ParseDifficulty_ReadsTheNativesGroupOptions_ByTheirOwnIds()
    {
        // Non-default values prove each id is read (the derived dx/relief transforms stay in code, not the option).
        XElement root = XElement.Parse(
            "<freecol-specification><optionGroup id='model.difficulty.medium'>" +
            "  <integerOption id='model.option.landPriceFactor' value='70' />" +
            "  <integerOption id='model.option.nativeDemands' value='3' />" +
            "  <integerOption id='model.option.rumourDifficulty' value='1' />" +
            "</optionGroup></freecol-specification>");
        DifficultyOptions d = Ruleset.ParseDifficulty(root);
        Assert.Equal(70, d.LandPriceFactor);
        Assert.Equal(3, d.NativeDemands);  // raw — the +1 demand-dx and (5−x)·50 relief transforms live in Game
        Assert.Equal(1, d.RumourDifficulty); // raw — the 10−x reward-dx transform lives in Game
    }

    [Fact]
    public void ClassicRuleset_ParsesTheNativesGroupOptions()
    {
        DifficultyOptions d = Ruleset.LoadClassic().Difficulty;
        Assert.Equal(60, d.LandPriceFactor);
        Assert.Equal(2, d.NativeDemands);
        Assert.Equal(2, d.RumourDifficulty);
    }
}
