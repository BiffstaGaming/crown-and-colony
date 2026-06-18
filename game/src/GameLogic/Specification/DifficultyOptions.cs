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
/// <param name="Government">
/// The Sons-of-Liberty / tory thresholds for the colony production bonus (spec <c>*GovernmentLimit</c> options; the
/// tory-penalty limits tighten on harder levels). See [sons-of-liberty].
/// </param>
/// <param name="LandPriceFactor">
/// Multiplier on a native tile's potential non-food yield in the land price (spec <c>model.option.landPriceFactor</c>;
/// 40/50/60/70/80 by level, medium 60). See [natives].
/// </param>
/// <param name="NativeDemands">
/// The raw native tribute-demand difficulty (spec <c>model.option.nativeDemands</c>; 0–4 by level, medium 2). The
/// demand amount uses <c>dx = NativeDemands + 1</c> and the accept-alarm relief uses <c>(5 − NativeDemands)·50</c> —
/// those transforms stay in code; this stores the raw value. See [natives].
/// </param>
/// <param name="RumourDifficulty">
/// The raw lost-city-rumour difficulty (spec <c>model.option.rumourDifficulty</c>; medium 2). Reward scaling uses
/// <c>dx = 10 − RumourDifficulty</c> (the transform stays in code). See [lost-city-rumours].
/// </param>
/// <param name="RumourBadPercent">Base bad-outcome chance for a lost-city rumour (spec <c>model.option.badRumour</c>, a percentage; medium 23). See [lost-city-rumours].</param>
/// <param name="RumourGoodPercent">Base good-outcome chance for a lost-city rumour (spec <c>model.option.goodRumour</c>, a percentage; medium 48). See [lost-city-rumours].</param>
/// <param name="CrossesIncrement">Added to the immigration target after each emigrant (spec <c>model.option.crossesIncrement</c>; medium 2). See [immigration].</param>
/// <param name="RecruitPriceIncrease">Base recruit-price rise per paid recruit (spec <c>model.option.recruitPriceIncrease</c>; veryEasy 20 else 30). See [europe].</param>
/// <param name="RecruitLowerCapIncrease">Recruit-price-floor rise per paid recruit (spec <c>model.option.lowerCapIncrease</c>; medium 0). See [europe].</param>
/// <param name="ArtilleryPriceIncrease">Added to the artillery purchase price after each artillery bought (spec <c>model.option.priceIncrease.artillery</c>; medium 100). See [europe].</param>
public sealed record DifficultyOptions(
    int FoundingFatherFactor,
    int UnitsThatUseNoBells,
    GovernmentLimits Government,
    int LandPriceFactor,
    int NativeDemands,
    int RumourDifficulty,
    int RumourBadPercent,
    int RumourGoodPercent,
    int CrossesIncrement,
    int RecruitPriceIncrease,
    int RecruitLowerCapIncrease,
    int ArtilleryPriceIncrease)
{
    /// <summary>
    /// The classic <c>model.difficulty.medium</c> values — the fallback when a spec omits the difficulty group or a
    /// specific option, and the source of truth for the default game.
    /// </summary>
    public static readonly DifficultyOptions ClassicMedium = new(
        FoundingFatherFactor: 40,
        UnitsThatUseNoBells: 2,
        Government: GovernmentLimits.ClassicMedium,
        LandPriceFactor: 60,
        NativeDemands: 2,
        RumourDifficulty: 2,
        RumourBadPercent: 23,
        RumourGoodPercent: 48,
        CrossesIncrement: 2,
        RecruitPriceIncrease: 30,
        RecruitLowerCapIncrease: 0,
        ArtilleryPriceIncrease: 100);
}
