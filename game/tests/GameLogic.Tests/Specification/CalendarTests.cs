using System.Xml.Linq;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Specification;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Specification;

/// <summary>
/// Calendar (<c>86d3c9xvu</c>): turn→year/season mapping faithful to FreeCol <c>Turn.getTurnYear</c>/<c>getTurnSeason</c>
/// (classic 1492/1600/2 — one turn per year until 1600, then a Spring turn and an Autumn turn per year).
/// </summary>
public class CalendarTests
{
    private static readonly Calendar Classic = Calendar.Classic;

    [Theory]
    [InlineData(1, 1492)]    // turn 1 = the first year
    [InlineData(2, 1493)]
    [InlineData(108, 1599)]  // last single-turn year
    [InlineData(109, 1600)]  // first split year — Spring 1600
    [InlineData(110, 1600)]  // Autumn 1600 (same year, second turn)
    [InlineData(111, 1601)]  // Spring 1601
    [InlineData(112, 1601)]  // Autumn 1601
    [InlineData(113, 1602)]
    public void YearForTurn_MatchesFreeCol(int turn, int expectedYear) =>
        Assert.Equal(expectedYear, Classic.YearForTurn(turn));

    [Theory]
    [InlineData(1, -1)]     // pre-1600: no season
    [InlineData(108, -1)]
    [InlineData(109, 0)]    // 1600 Spring
    [InlineData(110, 1)]    // 1600 Autumn
    [InlineData(111, 0)]    // 1601 Spring
    [InlineData(112, 1)]    // 1601 Autumn
    public void SeasonForTurn_MatchesFreeCol(int turn, int expectedSeason) =>
        Assert.Equal(expectedSeason, Classic.SeasonForTurn(turn));

    [Fact]
    public void HasSeason_FlipsAtTheSeasonYear()
    {
        Assert.False(Classic.HasSeason(108)); // 1599
        Assert.True(Classic.HasSeason(109));  // 1600
    }

    [Theory]
    [InlineData(1, "1492")]
    [InlineData(108, "1599")]
    [InlineData(109, "Spring 1600")]
    [InlineData(110, "Autumn 1600")]
    [InlineData(111, "Spring 1601")]
    public void Label_IsBareYearThenSeasonPrefixed(int turn, string expected) =>
        Assert.Equal(expected, Classic.Label(turn));

    [Fact]
    public void SeasonName_IsSpringAutumnForTwoSeasons()
    {
        Assert.Equal("Spring", Classic.SeasonName(0));
        Assert.Equal("Autumn", Classic.SeasonName(1));
    }

    [Fact]
    public void SeasonName_FallsBackToSeasonNForOtherCounts()
    {
        var fourSeasons = new Calendar(StartingYear: 1492, SeasonYear: 1600, Seasons: 4);
        Assert.Equal("Season 1", fourSeasons.SeasonName(0));
        Assert.Equal("Season 4", fourSeasons.SeasonName(3));
    }

    [Fact]
    public void ACustomSeasonCount_SplitsTheYearAccordingly()
    {
        // 4 seasons from 1600: turns 109–112 are all year 1600, turn 113 starts 1601.
        var four = new Calendar(StartingYear: 1492, SeasonYear: 1600, Seasons: 4);
        Assert.Equal(1600, four.YearForTurn(109));
        Assert.Equal(1600, four.YearForTurn(112));
        Assert.Equal(1601, four.YearForTurn(113));
        Assert.Equal(3, four.SeasonForTurn(112)); // 4th season of 1600
        Assert.Equal(0, four.SeasonForTurn(113)); // 1st season of 1601
    }

    // ── Parsed from the spec (not hardcoded) ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void ClassicRuleset_ParsesTheYearOptions()
    {
        Calendar c = Ruleset.LoadClassic().Calendar;
        Assert.Equal(1492, c.StartingYear);
        Assert.Equal(1600, c.SeasonYear);
        Assert.Equal(2, c.Seasons);
    }

    [Fact]
    public void ParseCalendar_ReadsNonDefaultSpecValues()
    {
        // Prove the values come from the parsed gameOptions.years, not a hardcoded 1492/1600/2.
        XElement root = XElement.Parse(
            "<freecol-specification>" +
            "  <options><optionGroup id='gameOptions.years'>" +
            "    <integerOption id='model.option.startingYear' value='1500' />" +
            "    <integerOption id='model.option.seasonYear' value='1700' />" +
            "    <integerOption id='model.option.seasons' value='3' />" +
            "  </optionGroup></options>" +
            "</freecol-specification>");

        Calendar c = Ruleset.ParseCalendar(root);
        Assert.Equal(new Calendar(StartingYear: 1500, SeasonYear: 1700, Seasons: 3), c);
    }

    [Fact]
    public void ParseCalendar_FallsBackToClassicWhenOptionsAbsent()
    {
        XElement root = XElement.Parse("<freecol-specification />");
        Assert.Equal(Calendar.Classic, Ruleset.ParseCalendar(root));
    }

    // ── Wired through Game ───────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Game_ExposesTheCalendarForTheCurrentTurn()
    {
        Game game = Game.New(Ruleset.LoadClassic(), 0xC0FFEEUL);
        Assert.Equal(1, game.Turn);
        Assert.Equal(1492, game.CurrentYear);
        Assert.Equal(-1, game.CurrentSeason);
        Assert.Equal("1492", game.CalendarLabel);
    }

    [Fact]
    public void Game_AdvancesTheYearWithTheTurn()
    {
        Game game = Game.New(Ruleset.LoadClassic(), 0xC0FFEEUL);
        game.EndTurn();
        Assert.Equal(2, game.Turn);
        Assert.Equal(1493, game.CurrentYear);
        Assert.Equal("1493", game.CalendarLabel);
    }
}
