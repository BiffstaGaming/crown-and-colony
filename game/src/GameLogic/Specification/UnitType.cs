namespace CrownAndColony.GameLogic.Specification;

/// <summary>
/// A unit type from the ruleset (free colonist, caravel, …) — immutable rule
/// data with inherited attributes already resolved (the spec uses
/// <c>extends</c> chains; abstract parents are not exposed).
/// </summary>
/// <param name="Id">Ruleset id, e.g. <c>model.unit.freeColonist</c>.</param>
/// <param name="Movement">Movement points per turn (spec scale: 3 = one normal move).</param>
/// <param name="LineOfSight">Sight radius in tiles (fog of war reveal).</param>
/// <param name="IsNaval">Naval unit: moves on water, not land (<c>model.ability.navalUnit</c>).</param>
/// <param name="CanFoundColony">May found a colony (<c>model.ability.foundColony</c>).</param>
/// <param name="RecruitProbability">
/// Relative weight for the Europe recruitment draw (spec <c>recruit-probability</c>);
/// 0 means not recruitable. Classic: free colonist / indentured servant / petty
/// criminal 20, experts 1.
/// </param>
/// <param name="IsPerson">
/// A colonist/person, not a ship or wagon (<c>model.ability.person</c>). Persons
/// idling in Europe suppress immigration (the −4/turn penalty).
/// </param>
/// <param name="Space">Cargo capacity in hold slots when this unit is a carrier (spec <c>space</c>; caravel 2).</param>
/// <param name="SpaceTaken">Raw hold slots this unit occupies when carried (spec <c>spaceTaken</c>; default 1).</param>
/// <param name="Price">Europe purchase/training price in gold (spec <c>price</c>); 0 = not purchasable there.</param>
/// <param name="Offence">
/// Combat offence power (spec <c>offence</c> + the type's own offence <c>&lt;modifier&gt;</c>s folded in):
/// free colonist 0, brave 1, veteran soldier 0 (×1.5 = 0), king's regular 4, artillery 7. Context modifiers
/// (attack bonus, terrain, fortification, …) are applied by the combat model.
/// </param>
/// <param name="Defence">
/// Combat defence power (spec <c>defence</c> + folded defence modifiers): free colonist 1, brave 1,
/// veteran soldier 1.5, king's regular 5, artillery 5.
/// </param>
/// <param name="DisposeOnCombatLoss">Defeating this unit destroys it outright (<c>model.ability.disposeOnCombatLoss</c>; braves, scouts).</param>
/// <param name="CanBeCaptured">This unit can be captured rather than killed when it loses (<c>model.ability.canBeCaptured</c>; free colonist).</param>
/// <param name="CaptureUnits">This unit can capture a defeated enemy (<c>model.ability.captureUnits</c>).</param>
/// <param name="CaptureEquipment">This unit can capture a defeated enemy's role equipment (<c>model.ability.captureEquipment</c>; braves).</param>
/// <param name="DisposeOnAllEquipmentLost">Losing the last equipment destroys this unit (<c>model.ability.disposeOnAllEquipLost</c>; king's regular).</param>
/// <param name="DemoteOnAllEquipmentLost">Losing the last equipment demotes this unit's type (<c>model.ability.demoteOnAllEquipLost</c>; colonial regular).</param>
/// <param name="Bombard">Artillery-style unit (<c>model.ability.bombard</c>): suffers the −75% artillery-in-the-open penalty when fighting outside a settlement.</param>
/// <param name="CaptureGoods">Naval raider (<c>model.ability.captureGoods</c>; frigate, privateer, man-o-war): a win lets it plunder as much of the beaten ship's cargo as its hold can take, before that ship sinks or limps to repair.</param>
/// <param name="Piracy">Privateer (<c>model.ability.piracy</c>): it can attack a rival colonial power <em>without declaring war</em>, and its nationality is hidden from its victims (it flies no flag).</param>
/// <param name="CarryTreasure">Treasure train (<c>model.ability.carryTreasure</c>): it carries plundered/discovered gold (<see cref="Units.Unit.TreasureAmount"/>) to be cashed in at a colony or in Europe.</param>
/// <param name="OffenceAdditive">The pre-role offence base (the attribute + the type's own additive offence modifiers, e.g. king's regular +4), before any percentage. A unit's role additive folds onto this before <see cref="OffenceMultiplier"/>.</param>
/// <param name="DefenceAdditive">The pre-role defence base (attribute + additive defence modifiers), before any percentage.</param>
/// <param name="OffenceMultiplier">The post-role offence multiplier from the type's own percentage modifiers (veteran soldier +50% → 1.5), applied after the role additive.</param>
/// <param name="DefenceMultiplier">The post-role defence multiplier from the type's own percentage modifiers.</param>
/// <param name="MaxHitPoints">Full-health hit points (spec <c>hit-points</c>; all classic ships 6). Today only ships use it: a damaged ship limps to repair at 1 HP and recovers +1/turn, so the repair takes <c>MaxHitPoints − 1</c> turns (5 for every classic ship).</param>
/// <param name="BuildCost">
/// Goods required to construct this unit in a colony (spec <c>required-goods</c>; empty when the unit cannot be
/// built — only trained/recruited). Classic land buildables: artillery (hammers 192 + tools 40), wagon train
/// (hammers 40). Drives colony unit construction (FreeCol <c>BuildableType.getRequiredGoods</c>).
/// </param>
/// <param name="RequiredPopulation">Minimum colony population to build this unit (FreeCol default 1; classic land buildables state none → 1).</param>
/// <param name="RequiredAbilities">
/// Abilities the colony must satisfy to build this unit (spec <c>required-ability</c>, id → required value;
/// empty for the unconditional land buildables). Ships need the shipyard's navalUnit-scoped build ability —
/// not modelled in this slice, so ship construction is deferred.
/// </param>
/// <param name="BuildLimit">
/// A cap on how many of this unit a player may build (spec <c>&lt;limit&gt;</c>). Classic: the wagon train is
/// capped at one per colony (<c>units &lt; settlements</c> at player scope). Null for an uncapped unit.
/// </param>
/// <param name="Skill">
/// The skill level taught/embodied by this unit (spec <c>skill</c>; 0 for a plain colonist, ship or artillery,
/// ≥1 for an expert/specialist). In Europe a priced unit with skill &gt; 0 is <b>trained</b>, one with skill 0 is
/// <b>purchased</b> (FreeCol <c>Specification.getUnitTypesTrainedInEurope</c>/<c>…PurchasedInEurope</c>).
/// </param>
public sealed record UnitType(
    string Id,
    int Movement,
    int LineOfSight,
    bool IsNaval,
    bool CanFoundColony,
    int RecruitProbability = 0,
    bool IsPerson = false,
    int Space = 0,
    int SpaceTaken = 1,
    int Price = 0,
    double Offence = 0,
    double Defence = 0,
    bool DisposeOnCombatLoss = false,
    bool CanBeCaptured = false,
    bool CaptureUnits = false,
    bool CaptureEquipment = false,
    bool DisposeOnAllEquipmentLost = false,
    bool DemoteOnAllEquipmentLost = false,
    bool Bombard = false,
    double OffenceAdditive = 0,
    double DefenceAdditive = 0,
    double OffenceMultiplier = 1,
    double DefenceMultiplier = 1,
    int MaxHitPoints = 1,
    bool CaptureGoods = false,
    bool Piracy = false,
    bool CarryTreasure = false,
    IReadOnlyList<GoodsOutput>? BuildCost = null,
    int RequiredPopulation = 1,
    IReadOnlyDictionary<string, bool>? RequiredAbilities = null,
    UnitBuildLimit? BuildLimit = null,
    int Skill = 0)
{
    private static readonly IReadOnlyList<GoodsOutput> NoCost = [];
    private static readonly IReadOnlyDictionary<string, bool> NoAbilities = new Dictionary<string, bool>();

    /// <summary>Short name derived from the id: <c>model.unit.freeColonist</c> → <c>freeColonist</c>.</summary>
    public string ShortName => Id[(Id.LastIndexOf('.') + 1)..];

    /// <summary>Goods needed to build this unit in a colony (empty when it cannot be built).</summary>
    public IReadOnlyList<GoodsOutput> BuildCostOrEmpty => BuildCost ?? NoCost;

    /// <summary>Abilities required to build this unit, id → required value (empty when unconditional).</summary>
    public IReadOnlyDictionary<string, bool> RequiredAbilitiesOrEmpty => RequiredAbilities ?? NoAbilities;

    /// <summary>True when this unit can be constructed in a colony (it has a build cost).</summary>
    public bool IsBuildable => BuildCostOrEmpty.Count > 0;

    /// <summary>Can this unit carry cargo/passengers (a ship)? (<see cref="Space"/> &gt; 0).</summary>
    public bool IsCarrier => Space > 0;

    /// <summary>Can this unit type be bought/trained in Europe for gold? (<see cref="Price"/> &gt; 0).</summary>
    public bool IsPurchasable => Price > 0;

    /// <summary>A priced unit with a <see cref="Skill"/> — a specialist <b>trained</b> in Europe (expert farmer, master carpenter…), FreeCol <c>getUnitTypesTrainedInEurope</c>.</summary>
    public bool IsTrainedInEurope => Price > 0 && Skill > 0;

    /// <summary>
    /// A priced unit with no skill — <b>purchased</b> in Europe (ships, artillery), FreeCol <c>getUnitTypesPurchasedInEurope</c>.
    /// (FreeCol gates on <c>!hasSkill()</c> — skill <em>attribute absent</em>; we fold absent → 0, so this differs only for a
    /// hypothetical priced unit declared with explicit <c>skill="0"</c>, which no classic unit is. Revisit if variant data adds one.)
    /// </summary>
    public bool IsPurchasedInEurope => Price > 0 && Skill <= 0;

    /// <summary>
    /// Effective hold slots this unit takes when carried (FreeCol
    /// <c>UnitType.getSpaceTaken</c> = <c>max(spaceTaken, space+1)</c>); a colonist is 1.
    /// </summary>
    public int CarrySlots => Math.Max(SpaceTaken, Space + 1);
}

/// <summary>
/// A build cap on a unit type (spec <c>&lt;limit&gt;</c>): the player may build it only while
/// <c>Left Operator Right</c> holds, with each operand counted at player scope. Classic uses exactly one — the
/// wagon-train cap, <c>units lt settlements</c> (the player's wagon trains must stay fewer than their colonies,
/// i.e. at most one wagon train per colony). Only the <c>units</c>/<c>settlements</c> operands are evaluated;
/// any other limit form is treated as no cap (FreeCol <c>Limit</c>/<c>Operand</c>).
/// </summary>
/// <param name="Operator">Comparison the limit must satisfy to permit a build: <c>lt</c>, <c>le</c>, <c>gt</c>, <c>ge</c>, or <c>eq</c>.</param>
/// <param name="LeftOperand">The left-hand count's operand type (classic: <c>units</c> — the player's units of this type).</param>
/// <param name="RightOperand">The right-hand count's operand type (classic: <c>settlements</c> — the player's colonies).</param>
public sealed record UnitBuildLimit(string Operator, string LeftOperand, string RightOperand);

/// <summary>
/// Map-generation climate envelope of a terrain type (the spec's <c>&lt;gen&gt;</c>
/// element): this terrain appears where humidity (0–100), temperature (−20–40)
/// and altitude (ocean &lt; 0, lowland 1–3, hills 10–19, mountains 20–30) all
/// fall inside the ranges.
/// </summary>
public sealed record GenRanges(
    int HumidityMin, int HumidityMax,
    int TemperatureMin, int TemperatureMax,
    int AltitudeMin, int AltitudeMax)
{
    /// <summary>True when the climate triple falls inside all three ranges.</summary>
    public bool Contains(int humidity, int temperature, int altitude) =>
        humidity >= HumidityMin && humidity <= HumidityMax &&
        temperature >= TemperatureMin && temperature <= TemperatureMax &&
        altitude >= AltitudeMin && altitude <= AltitudeMax;
}
