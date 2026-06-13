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
/// <param name="OffenceAdditive">The pre-role offence base (the attribute + the type's own additive offence modifiers, e.g. king's regular +4), before any percentage. A unit's role additive folds onto this before <see cref="OffenceMultiplier"/>.</param>
/// <param name="DefenceAdditive">The pre-role defence base (attribute + additive defence modifiers), before any percentage.</param>
/// <param name="OffenceMultiplier">The post-role offence multiplier from the type's own percentage modifiers (veteran soldier +50% → 1.5), applied after the role additive.</param>
/// <param name="DefenceMultiplier">The post-role defence multiplier from the type's own percentage modifiers.</param>
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
    double DefenceMultiplier = 1)
{
    /// <summary>Short name derived from the id: <c>model.unit.freeColonist</c> → <c>freeColonist</c>.</summary>
    public string ShortName => Id[(Id.LastIndexOf('.') + 1)..];

    /// <summary>Can this unit carry cargo/passengers (a ship)? (<see cref="Space"/> &gt; 0).</summary>
    public bool IsCarrier => Space > 0;

    /// <summary>Can this unit type be bought/trained in Europe for gold? (<see cref="Price"/> &gt; 0).</summary>
    public bool IsPurchasable => Price > 0;

    /// <summary>
    /// Effective hold slots this unit takes when carried (FreeCol
    /// <c>UnitType.getSpaceTaken</c> = <c>max(spaceTaken, space+1)</c>); a colonist is 1.
    /// </summary>
    public int CarrySlots => Math.Max(SpaceTaken, Space + 1);
}

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
