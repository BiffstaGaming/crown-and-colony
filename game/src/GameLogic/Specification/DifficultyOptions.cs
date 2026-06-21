using System.Collections.Generic;
using System.Linq;

namespace CrownAndColony.GameLogic.Specification;

/// <summary>A selectable new-game difficulty level — the spec <c>model.difficulty.*</c> level id plus a player-facing
/// name (FreeCol's five classic levels). "Data not code" like <see cref="GameVariants"/> / world-shape presets: adding
/// a level is a list entry, no logic change.</summary>
/// <param name="Id">The spec optionGroup id, e.g. <c>model.difficulty.medium</c> (the value persisted in the save and passed to <see cref="Ruleset.ParseDifficulty"/>).</param>
/// <param name="Name">Player-facing label, e.g. "Conquistador".</param>
public sealed record DifficultyLevel(string Id, string Name);

/// <summary>
/// The selectable difficulty levels the build ships with (FreeCol's five classic levels, easiest first). The default
/// is <see cref="Default"/> (<c>medium</c> / "Conquistador"), so a new game that does not choose a level — and every
/// existing entry point and test — gets the historical medium balance (ADR-009: byte-identical default).
/// </summary>
public static class DifficultyLevels
{
    /// <summary>The spec id of the default level (<c>model.difficulty.medium</c>) — what an unspecified new game uses.</summary>
    public const string DefaultId = "model.difficulty.medium";

    /// <summary>The offered levels, easiest first; names follow the classic Colonization difficulty titles.</summary>
    public static IReadOnlyList<DifficultyLevel> All { get; } =
    [
        new("model.difficulty.veryEasy", "Discoverer"),
        new("model.difficulty.easy", "Explorer"),
        new(DefaultId, "Conquistador"),
        new("model.difficulty.hard", "Governor"),
        new("model.difficulty.veryHard", "Viceroy"),
    ];

    /// <summary>Index of the shipped-default level (Conquistador / medium) in <see cref="All"/>.</summary>
    public static int DefaultIndex { get; } = All.ToList().FindIndex(l => l.Id == DefaultId);

    /// <summary>The shipped-default level (Conquistador / <c>model.difficulty.medium</c>).</summary>
    public static DifficultyLevel Default => All[DefaultIndex];

    /// <summary>The player-facing name for a level id, or the bare short id (e.g. <c>medium</c>) if it is not a shipped level.</summary>
    public static string NameOf(string id) =>
        All.FirstOrDefault(l => l.Id == id)?.Name ?? id[(id.LastIndexOf('.') + 1)..];
}

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
/// <param name="NativeConvertProbability">
/// Base percentage chance that winning an assault on a settlement you hold a mission in captures a brave as an Indian
/// Convert (spec <c>model.option.nativeConvertProbability</c>; classic veryEasy→veryHard = 50/40/30/20/10, medium 30).
/// FreeCol <c>Unit.getConvertProbability</c> = 0.01 × this, raised by the captor's <c>nativeConvertBonus</c> modifiers.
/// See [natives].
/// </param>
/// <param name="BurnProbability">
/// Percentage chance that winning such an assault instead burns the attacker's missions across that nation (spec
/// <c>model.option.burnProbability</c>; classic veryEasy→veryHard = 2/4/6/8/10, medium 6). FreeCol
/// <c>Unit.getBurnProbability</c> = 0.01 × this, unscaled by any modifier. See [natives].
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
/// <param name="TreasureTransportFee">The King's cut (percent) to ship a treasure train to Europe (spec <c>model.option.treasureTransportFee</c>; medium 60). See [treasure-train].</param>
/// <param name="ShipTradePenalty">
/// The percentage penalty applied to a <b>ship-borne</b> trader's price when selling to a native settlement (spec
/// <c>model.option.shipTradePenalty</c>, a negative <c>percentage</c> modifier; veryEasy→veryHard = −20/−25/−30/−35/−40,
/// medium −30). A settlement pays a ship 30% less than an overland trader would get. See [natives].
/// </param>
/// <param name="Monarch">
/// The home-nation Monarch's tuning numbers (spec <c>model.difficulty.monarch</c> group + <c>refSize</c>): meddling,
/// tax cap/spread, mercenary pricing, support size, boycott factor and the REF base composition. See [monarchy],
/// [royal-expeditionary-force].
/// </param>
/// <param name="Ai">
/// The foreign-power (rival) AI tuning — the colony cap it expands to, its Europe spend reserve, and its offensive
/// seek/travel range ladder. A faithful-subset: FreeCol hardcodes all three (no <c>model.difficulty.*</c> option), so
/// they are constant across the shipped levels but now data-overridable. See [players].
/// </param>
public sealed record DifficultyOptions(
    int FoundingFatherFactor,
    int UnitsThatUseNoBells,
    GovernmentLimits Government,
    int LandPriceFactor,
    int NativeDemands,
    int NativeConvertProbability,
    int BurnProbability,
    int RumourDifficulty,
    int RumourBadPercent,
    int RumourGoodPercent,
    int CrossesIncrement,
    int RecruitPriceIncrease,
    int RecruitLowerCapIncrease,
    int ArtilleryPriceIncrease,
    int TreasureTransportFee,
    int ShipTradePenalty,
    MonarchOptions Monarch,
    AiTuning Ai)
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
        NativeConvertProbability: 30,
        BurnProbability: 6,
        RumourDifficulty: 2,
        RumourBadPercent: 23,
        RumourGoodPercent: 48,
        CrossesIncrement: 2,
        RecruitPriceIncrease: 30,
        RecruitLowerCapIncrease: 0,
        ArtilleryPriceIncrease: 100,
        TreasureTransportFee: 60,
        ShipTradePenalty: -30,
        Monarch: MonarchOptions.ClassicMedium,
        Ai: AiTuning.ClassicMedium);
}
