using System.Xml.Linq;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Specification;
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
}
