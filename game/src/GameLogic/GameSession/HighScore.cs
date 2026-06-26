using System;

namespace CrownAndColony.GameLogic.GameSession;

/// <summary>
/// One completed-game leaderboard entry — a faithful port of FreeCol's <c>HighScore</c>
/// (<c>net.sf.freecol.common.model.HighScore</c>). Captured when a game ends (a <see cref="Game.Winner"/> emerges, the
/// human is defeated, or the player <see cref="Game.Retire">voluntarily retires</see>): it records who played, which
/// nation and nation type, the final <see cref="Game.PlayerScore"/>, the honorific <see cref="ScoreLevel"/> that score
/// earns, the difficulty, the end-of-game unit/colony counts, the turn the player retired (and, if they won, the turn
/// independence was declared), and when it was recorded.
/// <para>
/// A pure value object (a <c>record</c>): it holds no <see cref="Game"/> references, draws no randomness, and is
/// serialized to its own JSON store by <see cref="Persistence.HighScoreStore"/> — <b>separately from the game save</b>, exactly as
/// FreeCol keeps high scores in their own file rather than inside a saved game. Recording a score therefore never
/// touches and never bumps the game-save version (the leaderboard is its own file).
/// </para>
/// </summary>
/// <param name="PlayerName">The human player's display name (FreeCol <c>player.getName()</c>; here the nation's display name).</param>
/// <param name="NationId">The ruleset nation id the player led, e.g. <c>model.nation.dutch</c> (FreeCol <c>nationId</c>).</param>
/// <param name="NationTypeId">The ruleset nation-type id (the national advantage), or null if the game had no nation pick.</param>
/// <param name="Score">The final score (FreeCol <c>score</c> — exactly <see cref="Game.PlayerScore"/> for the player).</param>
/// <param name="Level">The honorific the score earns (FreeCol's <c>ScoreLevel</c>: the highest band whose threshold the score meets).</param>
/// <param name="Difficulty">The difficulty-level id the game ran under (FreeCol <c>difficulty</c>, e.g. <c>model.difficulty.medium</c>).</param>
/// <param name="UnitCount">The player's final unit count (FreeCol <c>nUnits</c>).</param>
/// <param name="ColonyCount">The player's final colony count (FreeCol <c>nColonies</c>).</param>
/// <param name="RetirementTurn">The turn the game ended for this player (FreeCol <c>retirementTurn</c>).</param>
/// <param name="IndependenceTurn">The turn independence was declared if the player won it, else <c>-1</c> (FreeCol <c>independenceTurn</c>).</param>
/// <param name="Won"><c>true</c> if this entry is a victory (the player won), <c>false</c> for a defeat — our flag in place of FreeCol's player-type read.</param>
/// <param name="DateUtc">When the score was recorded, in UTC (FreeCol <c>date</c>).</param>
/// <param name="GameId">A per-game identifier used only to de-duplicate (a later, higher score from the <i>same</i> game replaces the earlier one), mirroring FreeCol's <c>gameUUID</c> dedup. Empty if unknown.</param>
public readonly record struct HighScore(
    string PlayerName,
    string NationId,
    string? NationTypeId,
    int Score,
    ScoreLevel Level,
    string Difficulty,
    int UnitCount,
    int ColonyCount,
    int RetirementTurn,
    int IndependenceTurn,
    bool Won,
    DateTime DateUtc,
    string GameId)
{
    /// <summary>The score recorded as a formatted local date-time string for display (FreeCol <c>getDateString</c>).</summary>
    public string DateString => DateUtc.ToLocalTime().ToString("g");
}

/// <summary>
/// The honorific awarded for a final score — a faithful port of FreeCol's <c>HighScore.ScoreLevel</c> enum and its
/// minimum-score thresholds (an object is named in honour of the retiring player; its grandeur scales with the score,
/// from a <see cref="Continent"/> at the top down to a <see cref="ParasiticWorm"/> at the bottom). The level for a
/// score is the highest band whose <see cref="ScoreLevels.MinimumScoreFor"/> the score meets — see <see cref="ScoreLevels.ForScore"/>.
/// </summary>
public enum ScoreLevel
{
    /// <summary>≥ 40000 — a continent named for the player.</summary>
    Continent,

    /// <summary>≥ 35000 — a country.</summary>
    Country,

    /// <summary>≥ 30000 — a state.</summary>
    State,

    /// <summary>≥ 25000 — a city.</summary>
    City,

    /// <summary>≥ 20000 — a mountain range.</summary>
    MountainRange,

    /// <summary>≥ 15000 — a river.</summary>
    River,

    /// <summary>≥ 12000 — an institute.</summary>
    Institute,

    /// <summary>≥ 10000 — a university.</summary>
    University,

    /// <summary>≥ 8000 — a street.</summary>
    Street,

    /// <summary>≥ 7000 — a school.</summary>
    School,

    /// <summary>≥ 6000 — a bird of prey.</summary>
    BirdOfPrey,

    /// <summary>≥ 5000 — a tree.</summary>
    Tree,

    /// <summary>≥ 4000 — a flower.</summary>
    Flower,

    /// <summary>≥ 3200 — a rodent.</summary>
    Rodent,

    /// <summary>≥ 2400 — a foul-smelling plant.</summary>
    FoulSmellingPlant,

    /// <summary>≥ 1600 — a poisonous plant.</summary>
    PoisonousPlant,

    /// <summary>≥ 800 — a slime-mold beetle.</summary>
    SlimeMoldBeetle,

    /// <summary>≥ 400 — a blood-sucking insect.</summary>
    BloodSuckingInsect,

    /// <summary>≥ 200 — an infectious disease.</summary>
    InfectiousDisease,

    /// <summary>≥ 0 — a parasitic worm (the floor).</summary>
    ParasiticWorm,
}

/// <summary>Helpers over <see cref="ScoreLevel"/> (the score thresholds, ported from FreeCol).</summary>
public static class ScoreLevels
{
    /// <summary>The minimum score for a level (FreeCol <c>ScoreLevel.getMinimumScore</c>).</summary>
    public static int MinimumScoreFor(ScoreLevel level) => level switch
    {
        ScoreLevel.Continent => 40000,
        ScoreLevel.Country => 35000,
        ScoreLevel.State => 30000,
        ScoreLevel.City => 25000,
        ScoreLevel.MountainRange => 20000,
        ScoreLevel.River => 15000,
        ScoreLevel.Institute => 12000,
        ScoreLevel.University => 10000,
        ScoreLevel.Street => 8000,
        ScoreLevel.School => 7000,
        ScoreLevel.BirdOfPrey => 6000,
        ScoreLevel.Tree => 5000,
        ScoreLevel.Flower => 4000,
        ScoreLevel.Rodent => 3200,
        ScoreLevel.FoulSmellingPlant => 2400,
        ScoreLevel.PoisonousPlant => 1600,
        ScoreLevel.SlimeMoldBeetle => 800,
        ScoreLevel.BloodSuckingInsect => 400,
        ScoreLevel.InfectiousDisease => 200,
        ScoreLevel.ParasiticWorm => 0,
        _ => 0,
    };

    /// <summary>
    /// The honorific a score earns — the highest band whose <see cref="MinimumScoreFor"/> the score meets, exactly as
    /// FreeCol picks the level (<c>find(values, sl -&gt; sl.getMinimumScore() &lt;= score, PARASITIC_WORM)</c>). A score
    /// below every threshold (only possible if negative) falls back to <see cref="ScoreLevel.ParasiticWorm"/>.
    /// </summary>
    public static ScoreLevel ForScore(int score)
    {
        foreach (ScoreLevel level in Enum.GetValues<ScoreLevel>())
        {
            if (MinimumScoreFor(level) <= score)
            {
                return level;
            }
        }
        return ScoreLevel.ParasiticWorm;
    }
}
