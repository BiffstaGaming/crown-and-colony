using System.Xml.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
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
        DifficultyOptions parsed = Ruleset.LoadClassic().Difficulty;
        // The monarch's war-support force is a list (record equality is reference-based) — compare its contents…
        Assert.Equal(
            DifficultyOptions.ClassicMedium.Monarch.WarSupportForce.Select(b => (b.UnitTypeId, b.RoleId, b.Number)),
            parsed.Monarch.WarSupportForce.Select(b => (b.UnitTypeId, b.RoleId, b.Number)));
        // …then equate the whole record by normalising that one list member to the same instance.
        Assert.Equal(
            DifficultyOptions.ClassicMedium,
            parsed with { Monarch = parsed.Monarch with { WarSupportForce = DifficultyOptions.ClassicMedium.Monarch.WarSupportForce } });
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

    // ── Percentage + remaining immigration options (slice 4) ─────────────────────────────────────────────────────

    [Fact]
    public void ParseDifficulty_ReadsPercentageAndImmigrationOptions_ByTheirOwnIds()
    {
        XElement root = XElement.Parse(
            "<freecol-specification><optionGroup id='model.difficulty.medium'>" +
            "  <percentageOption id='model.option.badRumour' value='30' />" +
            "  <percentageOption id='model.option.goodRumour' value='40' />" +
            "  <integerOption id='model.option.crossesIncrement' value='3' />" +
            "  <integerOption id='model.option.recruitPriceIncrease' value='20' />" +
            "  <integerOption id='model.option.lowerCapIncrease' value='5' />" +
            "  <integerOption id='model.option.priceIncrease.artillery' value='150' />" +
            "</optionGroup></freecol-specification>");
        DifficultyOptions d = Ruleset.ParseDifficulty(root);
        Assert.Equal(30, d.RumourBadPercent);          // percentageOption — read via PctOption, not the integer path
        Assert.Equal(40, d.RumourGoodPercent);
        Assert.Equal(3, d.CrossesIncrement);
        Assert.Equal(20, d.RecruitPriceIncrease);
        Assert.Equal(5, d.RecruitLowerCapIncrease);
        Assert.Equal(150, d.ArtilleryPriceIncrease);   // dotted id matched as an opaque string
    }

    [Fact]
    public void ClassicRuleset_ParsesThePercentageAndImmigrationOptions()
    {
        DifficultyOptions d = Ruleset.LoadClassic().Difficulty;
        Assert.Equal(23, d.RumourBadPercent);
        Assert.Equal(48, d.RumourGoodPercent);
        Assert.Equal(2, d.CrossesIncrement);
        Assert.Equal(30, d.RecruitPriceIncrease);
        Assert.Equal(0, d.RecruitLowerCapIncrease);
        Assert.Equal(100, d.ArtilleryPriceIncrease);
    }

    // ── Treasure-transport fee (slice 5) ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ParseDifficulty_ReadsTheTreasureTransportFee_ByItsOwnId()
    {
        XElement root = XElement.Parse(
            "<freecol-specification><optionGroup id='model.difficulty.medium'>" +
            "  <integerOption id='model.option.treasureTransportFee' value='50' />" +
            "</optionGroup></freecol-specification>");
        Assert.Equal(50, Ruleset.ParseDifficulty(root).TreasureTransportFee);
    }

    [Fact]
    public void ClassicRuleset_ParsesTheTreasureTransportFee()
    {
        Assert.Equal(60, Ruleset.LoadClassic().Difficulty.TreasureTransportFee);
    }

    // ── Player-selectable + persisted difficulty level (86d3c9y08) ───────────────────────────────────────────────────

    [Fact]
    public void DifficultyLevels_OffersTheFiveClassicLevels_MediumDefault()
    {
        Assert.Equal(5, DifficultyLevels.All.Count);
        Assert.Equal(DifficultyLevels.DefaultId, DifficultyLevels.Default.Id);
        Assert.Equal("model.difficulty.medium", DifficultyLevels.Default.Id);
        Assert.Equal(DifficultyLevels.Default, DifficultyLevels.All[DifficultyLevels.DefaultIndex]);
    }

    [Fact]
    public void LoadClassic_DefaultsToMedium_RecordingItsLevelId()
    {
        Ruleset r = Ruleset.LoadClassic();
        Assert.Equal("model.difficulty.medium", r.DifficultyLevelId);
        Assert.Equal(40, r.Difficulty.FoundingFatherFactor); // medium value
    }

    [Theory]
    [InlineData("model.difficulty.veryEasy", 24)]
    [InlineData("model.difficulty.hard", 48)]
    [InlineData("model.difficulty.veryHard", 56)]
    public void LoadClassic_WithALevel_AppliesThatLevelsBalance_AndRecordsTheId(string levelId, int factor)
    {
        Ruleset r = Ruleset.LoadClassic(levelId);
        Assert.Equal(levelId, r.DifficultyLevelId);
        Assert.Equal(factor, r.Difficulty.FoundingFatherFactor);
    }

    [Fact]
    public void GameNew_DefaultsTheDifficultyTag_ToMedium()
    {
        Assert.Equal("model.difficulty.medium", Game.New(Ruleset.LoadClassic(), seed: 7).DifficultyLevelId);
    }

    [Fact]
    public void GameNew_RecordsTheChosenDifficultyLevel()
    {
        Ruleset hard = Ruleset.LoadClassic("model.difficulty.hard");
        Game game = Game.New(hard, seed: 7, difficultyLevelId: "model.difficulty.hard");
        Assert.Equal("model.difficulty.hard", game.DifficultyLevelId);
        Assert.Equal(48, game.Ruleset.Difficulty.FoundingFatherFactor); // the hard balance is live
    }

    [Fact]
    public void ChosenDifficultyLevel_RoundTripsThroughSave()
    {
        Ruleset hard = Ruleset.LoadClassic("model.difficulty.hard");
        Game game = Game.New(hard, seed: 7, difficultyLevelId: "model.difficulty.hard");

        SaveGame save = SaveGame.From(game);
        Assert.Equal("model.difficulty.hard", save.DifficultyLevel);
        Assert.Equal("model.difficulty.hard", save.DifficultyLevelOrDefault);

        // A host re-loads the ruleset under the persisted level so the balance matches.
        Game loaded = SaveGame.FromJson(save.ToJson())
            .Restore(Ruleset.LoadClassic(save.DifficultyLevelOrDefault));
        Assert.Equal("model.difficulty.hard", loaded.DifficultyLevelId);
        Assert.Equal(48, loaded.Ruleset.Difficulty.FoundingFatherFactor);
    }

    [Fact]
    public void DefaultDifficulty_OmitsTheLevelToken_AndStaysCurrent()
    {
        // A default (medium) game writes no DifficultyLevel token → byte-identical to a v45 default, and the version is current.
        string json = SaveGame.From(Game.New(Ruleset.LoadClassic(), seed: 5)).ToJson();
        Assert.DoesNotContain("\"DifficultyLevel\"", json);
        Assert.Equal(50, SaveGame.CurrentVersion);
    }

    [Fact]
    public void PreV46Save_LoadsAtTheDefaultMediumLevel()
    {
        // An old save with no DifficultyLevel token loads under the default medium balance.
        SaveGame v45 = SaveGame.From(Game.New(Ruleset.LoadClassic(), seed: 9))
            with { Version = 45, DifficultyLevel = null };
        Assert.Equal("model.difficulty.medium", v45.DifficultyLevelOrDefault);
        Game loaded = SaveGame.FromJson(v45.ToJson()).Restore(Ruleset.LoadClassic());
        Assert.Equal("model.difficulty.medium", loaded.DifficultyLevelId);
    }
}
