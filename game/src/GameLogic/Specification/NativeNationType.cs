namespace CrownAndColony.GameLogic.Specification;

/// <summary>
/// How many settlements a native nation founds, relative to the others (FreeCol
/// indian-nation-type <c>number-of-settlements</c>). The map generator turns this
/// band into an actual count scaled to the available land.
/// </summary>
public enum SettlementNumber
{
    /// <summary>Few settlements (e.g. Arawak, Inca, Aztec).</summary>
    Low,

    /// <summary>A typical number (e.g. Apache, Sioux, Cherokee, Iroquois).</summary>
    Average,

    /// <summary>Many settlements (e.g. Tupi).</summary>
    High,
}

/// <summary>
/// A native nation's disposition toward colonists (FreeCol indian-nation-type
/// <c>aggression</c>); informs the tension/raiding model in later slices.
/// </summary>
public enum NativeAggression
{
    /// <summary>Slow to anger (e.g. Tupi, Cherokee, Inca).</summary>
    Low,

    /// <summary>Average temper (e.g. Sioux, Iroquois).</summary>
    Average,

    /// <summary>Quick to anger (e.g. Apache, Arawak, Aztec).</summary>
    High,
}

/// <summary>
/// A skill a native settlement can teach a visiting colonist (FreeCol
/// indian-nation-type <c>&lt;skill&gt;</c>): the expert unit type a settlement of
/// this nation may instruct, weighted by <paramref name="Probability"/>.
/// </summary>
/// <param name="UnitTypeId">Expert unit type taught, e.g. <c>model.unit.expertFarmer</c>.</param>
/// <param name="Probability">Relative weight in the per-settlement skill draw.</param>
public sealed record NativeSkill(string UnitTypeId, int Probability);

/// <summary>
/// One <c>&lt;plunder&gt;</c> range a settlement type yields when sacked (FreeCol
/// <c>RandomRange</c>, <c>continuous=false</c>): the gold is
/// <c>(rnd[0,Maximum−Minimum] + Minimum) × Factor</c>, paid only when a
/// <see cref="Probability"/>% roll passes (a probability of 100 always pays).
/// </summary>
/// <param name="Probability">Percent chance the plunder is non-zero (100 = always).</param>
/// <param name="Minimum">Smallest range multiple.</param>
/// <param name="Maximum">Largest range multiple.</param>
/// <param name="Factor">Gold per range multiple.</param>
/// <param name="RequiresPlunderAbility">
/// True = the richer "extra" range, used when the attacker has
/// <c>model.ability.plunderNatives</c> (Hernán Cortés); false = the base range.
/// </param>
public sealed record SettlementPlunder(
    int Probability, int Minimum, int Maximum, int Factor, bool RequiresPlunderAbility);

/// <summary>
/// A native settlement type from the ruleset (FreeCol <c>&lt;settlement&gt;</c>):
/// the camp / village / city templates — each with a capital variant — that define
/// how big a settlement is, how much land it claims, and how well it defends.
/// </summary>
/// <param name="Id">Ruleset id, e.g. <c>model.settlement.camp</c>.</param>
/// <param name="Capital">Whether this is the nation's capital variant (bigger, better defended).</param>
/// <param name="ClaimableRadius">Tiles around the settlement it owns outright.</param>
/// <param name="ExtraClaimableRadius">Further tiles it will claim under pressure.</param>
/// <param name="MinimumSize">Smallest starting size (resident units).</param>
/// <param name="MaximumSize">Largest starting size.</param>
/// <param name="MinimumGrowth">Smallest land-growth step at creation.</param>
/// <param name="MaximumGrowth">Largest land-growth step at creation.</param>
/// <param name="TradeBonus">Trade-value band at the settlement (used by the native-trade slice).</param>
/// <param name="ConvertThreshold">Alarm needed before a convert may be produced.</param>
/// <param name="DefenceModifier">
/// Percentage defence bonus the settlement grants its defenders (FreeCol
/// <c>model.modifier.defence</c>; camp/village 50, capital 100, city 100, city capital 200).
/// </param>
/// <param name="Plunder">
/// The gold ranges a sacked settlement yields (base + "extra"; empty if it has no
/// <c>&lt;plunder&gt;</c>). The attacker picks one by its <c>plunderNatives</c> status —
/// see <see cref="PlunderRange"/>.
/// </param>
public sealed record SettlementType(
    string Id,
    bool Capital,
    int ClaimableRadius,
    int ExtraClaimableRadius,
    int MinimumSize,
    int MaximumSize,
    int MinimumGrowth,
    int MaximumGrowth,
    int TradeBonus,
    int ConvertThreshold,
    double DefenceModifier,
    IReadOnlyList<SettlementPlunder> Plunder)
{
    /// <summary>Short name derived from the id: <c>model.settlement.camp</c> → <c>camp</c>.</summary>
    public string ShortName => Id[(Id.LastIndexOf('.') + 1)..];

    /// <summary>
    /// The plunder range to use against an attacker with (or without) the
    /// <c>model.ability.plunderNatives</c> ability, or null if the settlement yields no plunder.
    /// </summary>
    public SettlementPlunder? PlunderRange(bool hasPlunderAbility) =>
        Plunder.FirstOrDefault(p => p.RequiresPlunderAbility == hasPlunderAbility);
}

/// <summary>
/// A native nation type from the ruleset (FreeCol <c>&lt;indian-nation-type&gt;</c>):
/// the Apache, Sioux, Tupi, Arawak, Cherokee, Iroquois, Inca and Aztec — each with a
/// settlement template (camp / village / city), how many settlements it founds, its
/// aggression, the skills its settlements teach, and its preferred map regions. Values
/// inherited from the abstract camp/village/city templates are resolved into the record.
/// </summary>
/// <param name="Id">Ruleset id, e.g. <c>model.nationType.apache</c>.</param>
/// <param name="SettlementTypeId">The non-capital settlement template id.</param>
/// <param name="CapitalSettlementTypeId">The capital settlement template id.</param>
/// <param name="NumberOfSettlements">How many settlements this nation founds, relatively.</param>
/// <param name="Aggression">The nation's disposition toward colonists.</param>
/// <param name="Skills">Skills the nation's settlements can teach (inherited + own, weighted).</param>
/// <param name="Regions">Preferred map-region ids (informational until named regions exist).</param>
public sealed record NativeNationType(
    string Id,
    string SettlementTypeId,
    string CapitalSettlementTypeId,
    SettlementNumber NumberOfSettlements,
    NativeAggression Aggression,
    IReadOnlyList<NativeSkill> Skills,
    IReadOnlyList<string> Regions)
{
    /// <summary>Short name derived from the id: <c>model.nationType.apache</c> → <c>apache</c>.</summary>
    public string ShortName => Id[(Id.LastIndexOf('.') + 1)..];
}
