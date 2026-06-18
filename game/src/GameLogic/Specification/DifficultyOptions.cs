namespace CrownAndColony.GameLogic.Specification;

/// <summary>
/// The tuning numbers a difficulty level sets — FreeCol's <c>model.difficulty.*</c> option groups, parsed once from
/// the chosen level and read wherever a hardcoded balance constant used to live. Carried on
/// <see cref="Ruleset.Difficulty"/>.
/// </summary>
/// <remarks>
/// <para>
/// The classic ruleset has five levels (<c>veryEasy</c>, <c>easy</c>, <c>medium</c>, <c>hard</c>, <c>veryHard</c>),
/// each restating the full option set with its own values; the default is <c>medium</c>. FreeCol overlays the chosen
/// level onto the base options at load (<c>Specification.applyDifficultyLevel</c>); we parse the selected level's
/// values straight into this record.
/// </para>
/// <para>
/// Properties are added slice by slice as constants are routed through the system. Pure and immutable (ADR-009):
/// parsed once, no state, no randomness. Re-derived from the ruleset + selected level at load, so nothing extra is
/// persisted while the level is fixed at the default (a player-selectable, saved level is a later slice).
/// </para>
/// </remarks>
/// <param name="FoundingFatherFactor">
/// Liberty multiplier in the founding-father cost formula (spec <c>model.option.foundingFatherFactor</c>; classic
/// veryEasy→veryHard = 24/32/40/48/56, medium 40).
/// </param>
/// <param name="UnitsThatUseNoBells">
/// Colonists who consume no bell upkeep — beyond this each eats 1 bell/turn (spec
/// <c>model.option.unitsThatUseNoBells</c>; 2 on every classic level).
/// </param>
public sealed record DifficultyOptions(
    int FoundingFatherFactor,
    int UnitsThatUseNoBells)
{
    /// <summary>
    /// The classic <c>model.difficulty.medium</c> values — the fallback when a spec omits the difficulty group or a
    /// specific option, and the source of truth for the default game.
    /// </summary>
    public static readonly DifficultyOptions ClassicMedium = new(
        FoundingFatherFactor: 40,
        UnitsThatUseNoBells: 2);
}
