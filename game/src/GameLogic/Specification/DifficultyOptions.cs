using System;
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

    /// <summary>
    /// The spec id of FreeCol's <b>custom</b> difficulty group (<c>model.difficulty.custom</c>, FreeCol
    /// <c>DifficultyDialog</c>'s editable clone target; 86d3fq0x7). Deliberately <b>not</b> a member of
    /// <see cref="All"/>: our custom difficulty is a per-session <em>override record</em> layered onto a real base
    /// level via <see cref="Ruleset.WithDifficultyOverrides"/> — the ruleset keeps loading (and the save keeps
    /// recording) the <em>base</em> level's id, so a reloaded game re-derives the base stock values (a persisted
    /// custom level would need a save-format change; documented deviation). The id exists in the classic spec (its
    /// group holds FreeCol's editor-scratch values, not a medium copy) but nothing loads it.
    /// </summary>
    public const string CustomId = "model.difficulty.custom";

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
/// <param name="TreasureTransportFee">FreeCol's flat King's cut (percent) to ship a treasure train to Europe (spec <c>model.option.treasureTransportFee</c>; medium 60). <b>Parsed but unused for the cut</b>: we use the Col1 model where the King's at-colony cut equals the current tax rate, not this flat 60% (see [treasure-train], 86d3fb5mj). Retained so the spec parses unchanged. See [treasure-train].</param>
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
/// <param name="NativeTension">
/// The native-tension (alarm) tuning — the hostile-act tension deltas, the land-taken/surrender alarms, the per-turn
/// alarm decay, and the first-contact gift range / tales-reveal radius. A faithful-subset: FreeCol keeps all of these
/// as engine <c>const</c>s (<c>Tension.java</c> / <c>IndianSettlement.java</c> / <c>ServerPlayer</c>, no
/// <c>model.difficulty.*</c> option), so they are constant across the shipped levels but now data-overridable. See
/// [natives], [combat].
/// </param>
/// <param name="ExpertStartingUnits">
/// Whether each colonial power lands the <b>expert</b> variant of a starting-unit slot instead of its free-colonist
/// version (spec <c>model.option.expertStartingUnits</c> boolean, in the level's <c>immigration</c> sub-group; classic
/// <b>true</b> on the two easiest levels — veryEasy/easy — and <b>false</b> on medium and harder, so the default
/// medium game is unaffected). Routed into <see cref="EuropeanNationType.StartingUnitsFor"/>: when on, e.g. the
/// default nation's soldier upgrades from a free colonist to a veteran soldier. See [players].
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
    AiTuning Ai,
    NativeTensionOptions NativeTension,
    bool ExpertStartingUnits)
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
        Ai: AiTuning.ClassicMedium,
        NativeTension: NativeTensionOptions.ClassicMedium,
        // Medium and harder ship expertStartingUnits=false (only veryEasy/easy enable it) — so the default game keeps
        // the free-colonist roster, byte-identical (ADR-009).
        ExpertStartingUnits: false);
}

/// <summary>
/// One player-editable custom-difficulty field (86d3fq0x7): a display label, the spec option id it mirrors, and (on
/// the typed subclasses) the get/with accessor pair that reads and rewrites the value on a
/// <see cref="DifficultyOptions"/> record. The schema is <b>data, not code</b>: the custom-difficulty editor builds
/// its rows from <see cref="DifficultyFields.All"/> and folds the edited values back through the
/// <c>With</c> delegates, so adding an editable option is a list entry, not an editor change.
/// </summary>
/// <param name="Label">The player-facing row label (e.g. "Founding-father factor").</param>
/// <param name="OptionId">The spec option id the field mirrors (e.g. <c>model.option.foundingFatherFactor</c>).</param>
public abstract record DifficultyField(string Label, string OptionId)
{
    /// <summary>
    /// A Godot-safe node name for the field's editor control: the <see cref="OptionId"/> with its
    /// <c>model.option.</c> prefix stripped, each remaining dot-segment PascalCased and joined, plus <c>Field</c>
    /// (e.g. <c>model.option.refSize.soldiers</c> → <c>RefSizeSoldiersField</c>). Node names cannot contain
    /// <c>'.'</c>; deriving the name here keeps it deterministic and L1-testable (unique per field, no dots).
    /// </summary>
    public string NodeName =>
        string.Concat(
            OptionId.Replace("model.option.", "", StringComparison.Ordinal)
                .Split('.')
                .Select(s => char.ToUpperInvariant(s[0]) + s[1..]))
        + "Field";
}

/// <summary>An integer custom-difficulty field — see <see cref="DifficultyField"/>.</summary>
/// <param name="Label">The player-facing row label.</param>
/// <param name="OptionId">The spec option id the field mirrors.</param>
/// <param name="Get">Reads the field's current value off a <see cref="DifficultyOptions"/>.</param>
/// <param name="With">Returns the options with the field rewritten to a new value (everything else unchanged).</param>
public sealed record IntDifficultyField(
    string Label,
    string OptionId,
    Func<DifficultyOptions, int> Get,
    Func<DifficultyOptions, int, DifficultyOptions> With) : DifficultyField(Label, OptionId);

/// <summary>A boolean custom-difficulty field — see <see cref="DifficultyField"/>.</summary>
/// <param name="Label">The player-facing row label.</param>
/// <param name="OptionId">The spec option id the field mirrors.</param>
/// <param name="Get">Reads the field's current value off a <see cref="DifficultyOptions"/>.</param>
/// <param name="With">Returns the options with the field rewritten to a new value (everything else unchanged).</param>
public sealed record BoolDifficultyField(
    string Label,
    string OptionId,
    Func<DifficultyOptions, bool> Get,
    Func<DifficultyOptions, bool, DifficultyOptions> With) : DifficultyField(Label, OptionId);

/// <summary>
/// The custom-difficulty field schema (86d3fq0x7): every per-level scalar a player may edit on the
/// custom-difficulty screen — the 15 top-level <see cref="DifficultyOptions"/> integers, the
/// <see cref="DifficultyOptions.ExpertStartingUnits"/> boolean, the four <see cref="GovernmentLimits"/> thresholds
/// and the 11 <see cref="MonarchOptions"/> scalars (31 fields). <b>Excluded, deliberately and documented</b>
/// (docs/systems/difficulty.md §2): the two Monarch <em>unit lists</em> (<c>warSupportForce</c>/<c>mercenaryForce</c>
/// — roster editing needs its own UI), <see cref="AiTuning"/> and <see cref="NativeTensionOptions"/> (faithful-subset
/// bundles FreeCol keeps as engine constants, not spec options), and the intervention trio
/// (<c>interventionBells</c>/<c>Turns</c>/<c>Force</c> — parsed outside <see cref="DifficultyOptions"/>, see
/// <see cref="Ruleset.InterventionBells"/>).
/// </summary>
public static class DifficultyFields
{
    /// <summary>Every editable field, in editor row order (the top-level ints, the expert-units flag, the government limits, then the monarch scalars).</summary>
    public static IReadOnlyList<DifficultyField> All { get; } =
    [
        // ── Top-level DifficultyOptions integers (15) ──────────────────────────────────────────────────────────
        new IntDifficultyField("Founding-father factor", "model.option.foundingFatherFactor",
            d => d.FoundingFatherFactor, (d, v) => d with { FoundingFatherFactor = v }),
        new IntDifficultyField("Colonists free of bell upkeep", "model.option.unitsThatUseNoBells",
            d => d.UnitsThatUseNoBells, (d, v) => d with { UnitsThatUseNoBells = v }),
        new IntDifficultyField("Land price factor", "model.option.landPriceFactor",
            d => d.LandPriceFactor, (d, v) => d with { LandPriceFactor = v }),
        new IntDifficultyField("Native demands", "model.option.nativeDemands",
            d => d.NativeDemands, (d, v) => d with { NativeDemands = v }),
        new IntDifficultyField("Native convert chance (%)", "model.option.nativeConvertProbability",
            d => d.NativeConvertProbability, (d, v) => d with { NativeConvertProbability = v }),
        new IntDifficultyField("Mission burn chance (%)", "model.option.burnProbability",
            d => d.BurnProbability, (d, v) => d with { BurnProbability = v }),
        new IntDifficultyField("Rumour difficulty", "model.option.rumourDifficulty",
            d => d.RumourDifficulty, (d, v) => d with { RumourDifficulty = v }),
        new IntDifficultyField("Bad rumour chance (%)", "model.option.badRumour",
            d => d.RumourBadPercent, (d, v) => d with { RumourBadPercent = v }),
        new IntDifficultyField("Good rumour chance (%)", "model.option.goodRumour",
            d => d.RumourGoodPercent, (d, v) => d with { RumourGoodPercent = v }),
        new IntDifficultyField("Crosses increment", "model.option.crossesIncrement",
            d => d.CrossesIncrement, (d, v) => d with { CrossesIncrement = v }),
        new IntDifficultyField("Recruit price increase", "model.option.recruitPriceIncrease",
            d => d.RecruitPriceIncrease, (d, v) => d with { RecruitPriceIncrease = v }),
        new IntDifficultyField("Recruit price floor increase", "model.option.lowerCapIncrease",
            d => d.RecruitLowerCapIncrease, (d, v) => d with { RecruitLowerCapIncrease = v }),
        new IntDifficultyField("Artillery price increase", "model.option.priceIncrease.artillery",
            d => d.ArtilleryPriceIncrease, (d, v) => d with { ArtilleryPriceIncrease = v }),
        new IntDifficultyField("Treasure transport fee (%)", "model.option.treasureTransportFee",
            d => d.TreasureTransportFee, (d, v) => d with { TreasureTransportFee = v }),
        new IntDifficultyField("Ship trade penalty (%)", "model.option.shipTradePenalty",
            d => d.ShipTradePenalty, (d, v) => d with { ShipTradePenalty = v }),

        // ── The expert-starting-units flag (1) ─────────────────────────────────────────────────────────────────
        new BoolDifficultyField("Expert starting units", "model.option.expertStartingUnits",
            d => d.ExpertStartingUnits, (d, v) => d with { ExpertStartingUnits = v }),

        // ── Government limits (4) ──────────────────────────────────────────────────────────────────────────────
        new IntDifficultyField("Very-good government SoL limit (%)", "model.option.veryGoodGovernmentLimit",
            d => d.Government.VeryGood, (d, v) => d with { Government = d.Government with { VeryGood = v } }),
        new IntDifficultyField("Good government SoL limit (%)", "model.option.goodGovernmentLimit",
            d => d.Government.Good, (d, v) => d with { Government = d.Government with { Good = v } }),
        new IntDifficultyField("Bad government tory limit", "model.option.badGovernmentLimit",
            d => d.Government.Bad, (d, v) => d with { Government = d.Government with { Bad = v } }),
        new IntDifficultyField("Very-bad government tory limit", "model.option.veryBadGovernmentLimit",
            d => d.Government.VeryBad, (d, v) => d with { Government = d.Government with { VeryBad = v } }),

        // ── Monarch scalars (11; the two unit lists are excluded — see the class summary) ─────────────────────
        new IntDifficultyField("Monarch meddling", "model.option.monarchMeddling",
            d => d.Monarch.Meddling, (d, v) => d with { Monarch = d.Monarch with { Meddling = v } }),
        new IntDifficultyField("Maximum tax rate (%)", "model.option.maximumTax",
            d => d.Monarch.MaximumTaxRate, (d, v) => d with { Monarch = d.Monarch with { MaximumTaxRate = v } }),
        new IntDifficultyField("Tax adjustment", "model.option.taxAdjustment",
            d => d.Monarch.TaxAdjustment, (d, v) => d with { Monarch = d.Monarch with { TaxAdjustment = v } }),
        new IntDifficultyField("Mercenary price (%)", "model.option.mercenaryPrice",
            d => d.Monarch.MercenaryPricePercent, (d, v) => d with { Monarch = d.Monarch with { MercenaryPricePercent = v } }),
        new IntDifficultyField("Monarch support level", "model.option.monarchSupport",
            d => d.Monarch.MonarchSupportLevel, (d, v) => d with { Monarch = d.Monarch with { MonarchSupportLevel = v } }),
        new IntDifficultyField("Boycott arrears factor", "model.option.arrearsFactor",
            d => d.Monarch.ArrearsFactor, (d, v) => d with { Monarch = d.Monarch with { ArrearsFactor = v } }),
        new IntDifficultyField("REF infantry", "model.option.refSize.soldiers",
            d => d.Monarch.RefBaseInfantry, (d, v) => d with { Monarch = d.Monarch with { RefBaseInfantry = v } }),
        new IntDifficultyField("REF cavalry", "model.option.refSize.dragoons",
            d => d.Monarch.RefBaseCavalry, (d, v) => d with { Monarch = d.Monarch with { RefBaseCavalry = v } }),
        new IntDifficultyField("REF artillery", "model.option.refSize.artillery",
            d => d.Monarch.RefBaseArtillery, (d, v) => d with { Monarch = d.Monarch with { RefBaseArtillery = v } }),
        new IntDifficultyField("REF men-o-war", "model.option.refSize.menOfWar",
            d => d.Monarch.RefBaseManOWar, (d, v) => d with { Monarch = d.Monarch with { RefBaseManOWar = v } }),
        new IntDifficultyField("War support gold", "model.option.warSupportGold",
            d => d.Monarch.WarSupportGold, (d, v) => d with { Monarch = d.Monarch with { WarSupportGold = v } }),
    ];
}
