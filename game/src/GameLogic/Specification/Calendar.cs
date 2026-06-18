namespace CrownAndColony.GameLogic.Specification;

/// <summary>
/// Maps a turn number to an in-game year and (after the season-split year) a season, faithful to FreeCol's
/// <c>Turn.getTurnYear</c>/<c>getTurnSeason</c> (<c>Turn.java</c> 165–194) configured by the spec
/// <c>gameOptions.years</c> group (<c>startingYear</c>=1492, <c>seasonYear</c>=1600, <c>seasons</c>=2 in classic).
/// </summary>
/// <remarks>
/// <para>
/// Early game runs one turn per year (turn 1 = 1492, turn 2 = 1493, …). From <see cref="SeasonYear"/> onward the
/// calendar runs <see cref="Seasons"/> turns per year — in classic two: a Spring turn then an Autumn turn — so a
/// year now spans several turns. A turn before <see cref="SeasonYear"/> has no season (<see cref="SeasonForTurn"/>
/// returns <c>-1</c>); the HUD shows a bare year there and a "Spring 1600" / "Autumn 1600" label afterwards.
/// </para>
/// <para>Pure and engine-free (ADR-009-friendly): no state, no randomness — a turn always maps to the same label.</para>
/// </remarks>
/// <param name="StartingYear">The year of turn 1 (classic 1492).</param>
/// <param name="SeasonYear">The first year that splits into <see cref="Seasons"/> turns (classic 1600).</param>
/// <param name="Seasons">Turns per year from <see cref="SeasonYear"/> on (classic 2 — Spring, Autumn).</param>
public sealed record Calendar(int StartingYear, int SeasonYear, int Seasons)
{
    /// <summary>The classic ruleset defaults, used when a spec omits the <c>gameOptions.years</c> options.</summary>
    public static readonly Calendar Classic = new(StartingYear: 1492, SeasonYear: 1600, Seasons: 2);

    /// <summary>
    /// The in-game year shown for <paramref name="turn"/> (turn 1 = <see cref="StartingYear"/>). Before
    /// <see cref="SeasonYear"/> one turn is one year; from then on <see cref="Seasons"/> turns share a year.
    /// FreeCol <c>Turn.getTurnYear</c>.
    /// </summary>
    public int YearForTurn(int turn)
    {
        int linearYear = turn - 1 + StartingYear;
        return linearYear < SeasonYear
            ? linearYear
            : SeasonYear + (linearYear - SeasonYear) / Seasons;
    }

    /// <summary>
    /// The 0-based season of <paramref name="turn"/> (0 = Spring, 1 = Autumn in classic), or <c>-1</c> for a turn
    /// before <see cref="SeasonYear"/> (one-turn-per-year era, no season). FreeCol <c>Turn.getTurnSeason</c>.
    /// </summary>
    public int SeasonForTurn(int turn)
    {
        int linearYear = turn - 1 + StartingYear;
        return linearYear < SeasonYear ? -1 : (linearYear - SeasonYear) % Seasons;
    }

    /// <summary>Whether <paramref name="turn"/> falls in the multi-season era (a season label applies).</summary>
    public bool HasSeason(int turn) => SeasonForTurn(turn) >= 0;

    /// <summary>
    /// The English name of season index <paramref name="season"/> — Spring/Autumn for the classic two-season year,
    /// else a <c>"Season N"</c> fallback (FreeCol localises these via <c>nameCache.season.*</c>; the actual on-screen
    /// text is a presentation concern, this is the engine-free default).
    /// </summary>
    public string SeasonName(int season) => (Seasons, season) switch
    {
        (2, 0) => "Spring",
        (2, 1) => "Autumn",
        _ => $"Season {season + 1}",
    };

    /// <summary>
    /// The calendar label for <paramref name="turn"/>: a bare year (<c>"1492"</c>) before <see cref="SeasonYear"/>,
    /// or a season-prefixed year (<c>"Spring 1600"</c>) from then on.
    /// </summary>
    public string Label(int turn)
    {
        int season = SeasonForTurn(turn);
        int year = YearForTurn(turn);
        return season < 0 ? year.ToString() : $"{SeasonName(season)} {year}";
    }
}
