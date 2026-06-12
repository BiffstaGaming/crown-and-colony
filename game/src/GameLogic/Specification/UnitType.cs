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
public sealed record UnitType(
    string Id,
    int Movement,
    int LineOfSight,
    bool IsNaval,
    bool CanFoundColony)
{
    /// <summary>Short name derived from the id: <c>model.unit.freeColonist</c> → <c>freeColonist</c>.</summary>
    public string ShortName => Id[(Id.LastIndexOf('.') + 1)..];
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
