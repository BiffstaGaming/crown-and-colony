using System.Xml.Linq;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Specification;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Specification;

/// <summary>
/// Base <c>gameOptions</c>-group bundle (<c>86d3d335r</c>): the immigration trio
/// (<c>model.option.initialImmigration</c> / <c>europeanUnitImmigrationPenalty</c> / <c>playerImmigrationBonus</c>) is
/// parsed once into <see cref="GameOptions"/> on <see cref="Ruleset.GameOptions"/> and read by the consumers, replacing
/// the ad-hoc hardcoded constants. Pure refactor — the classic values (15 / −4 / +2) are unchanged, so the default game
/// is byte-identical (ADR-009).
/// </summary>
public class GameOptionsTests
{
    [Fact]
    public void ParseGameOptions_ReadsEachImmigrationOption_ByItsOwnId()
    {
        // Non-default values prove each id is actually read (not silently falling back to ClassicDefaults).
        XElement root = XElement.Parse(
            "<freecol-specification><optionGroup id='gameOptions'>" +
            "  <integerOption id='model.option.initialImmigration' value='25' />" +
            "  <integerOption id='model.option.europeanUnitImmigrationPenalty' value='-7' />" +
            "  <integerOption id='model.option.playerImmigrationBonus' value='3' />" +
            "</optionGroup></freecol-specification>");
        GameOptions o = Ruleset.ParseGameOptions(root);
        Assert.Equal(25, o.InitialImmigration);
        Assert.Equal(-7, o.EuropeanUnitImmigrationPenalty);
        Assert.Equal(3, o.PlayerImmigrationBonus);
    }

    [Fact]
    public void ParseGameOptions_ReadsTheDefaultValueAttribute_WhenNoValueIsSet()
    {
        // FreeCol options carry a defaultValue; ParseIntOption falls through to it when value is absent.
        XElement root = XElement.Parse(
            "<freecol-specification><optionGroup id='gameOptions'>" +
            "  <integerOption id='model.option.initialImmigration' defaultValue='20' />" +
            "</optionGroup></freecol-specification>");
        Assert.Equal(20, Ruleset.ParseGameOptions(root).InitialImmigration);
    }

    [Fact]
    public void ParseGameOptions_FallsBackPerOption_WhenAnOptionIsMissing()
    {
        // The group exists but omits two options → those fall back to their classic defaults, the present one is read.
        XElement root = XElement.Parse(
            "<freecol-specification><optionGroup id='gameOptions'>" +
            "  <integerOption id='model.option.playerImmigrationBonus' value='9' />" +
            "</optionGroup></freecol-specification>");
        GameOptions o = Ruleset.ParseGameOptions(root);
        Assert.Equal(GameOptions.ClassicDefaults.InitialImmigration, o.InitialImmigration);                       // 15
        Assert.Equal(GameOptions.ClassicDefaults.EuropeanUnitImmigrationPenalty, o.EuropeanUnitImmigrationPenalty); // −4
        Assert.Equal(9, o.PlayerImmigrationBonus);
    }

    [Fact]
    public void ClassicRuleset_ParsesTheImmigrationTrio()
    {
        // Pinned against the classic specification.xml (gameOptions group): 15 / −4 / +2.
        GameOptions o = Ruleset.LoadClassic().GameOptions;
        Assert.Equal(15, o.InitialImmigration);
        Assert.Equal(-4, o.EuropeanUnitImmigrationPenalty);
        Assert.Equal(2, o.PlayerImmigrationBonus);
    }

    [Fact]
    public void ClassicDefaults_MatchTheParsedClassicGroup()
    {
        // Guards the duplication: the hardcoded ClassicDefaults fallback must equal the spec's gameOptions group, so a
        // per-option fallback can never silently diverge from the data.
        Assert.Equal(GameOptions.ClassicDefaults, Ruleset.LoadClassic().GameOptions);
    }

    [Fact]
    public void GameConsts_AliasTheClassicDefaults()
    {
        // The Game.* constants are now classic-default aliases of the bundle (used as static fallbacks) — they must
        // stay equal to the bundle's classic values so the const path and the parsed path agree.
        Assert.Equal(GameOptions.ClassicDefaults.InitialImmigration, Game.InitialImmigration);
        Assert.Equal(GameOptions.ClassicDefaults.EuropeanUnitImmigrationPenalty, Game.EuropeUnitImmigrationPenalty);
        Assert.Equal(GameOptions.ClassicDefaults.PlayerImmigrationBonus, Game.PlayerImmigrationBonus);
    }

    [Fact]
    public void EuropeContribution_IsUnchanged_RoutingThroughTheBundle()
    {
        // Behaviour preservation: a colonyless colonial player accrues exactly the flat +2 player bonus each turn (no
        // person idling in Europe), proving the live formula reads the bundle's PlayerImmigrationBonus.
        var game = Game.New(Ruleset.LoadClassic(), seed: 1); // the starting unit stays on the map, never founds
        game.EndTurn();
        Assert.Equal(Ruleset.LoadClassic().GameOptions.PlayerImmigrationBonus, game.Immigration); // +2
    }
}
