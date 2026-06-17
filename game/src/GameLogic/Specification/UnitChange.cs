namespace CrownAndColony.GameLogic.Specification;

/// <summary>
/// One from→to unit-type transition (FreeCol <c>UnitTypeChange</c>): when a change of a given
/// <see cref="UnitChangeTypeIds">change-type</see> fires, a unit of <see cref="From"/> becomes
/// <see cref="To"/> with the given <see cref="Probability"/> (0–100). Combat uses promotion,
/// demotion and capture; the same data backs education/skill changes (not parsed yet).
/// </summary>
/// <param name="From">The unit type id that changes.</param>
/// <param name="To">The unit type id it becomes.</param>
/// <param name="Probability">Chance the change applies, as a percentage 0–100 (classic combat rows are all 100).</param>
public sealed record UnitChange(string From, string To, int Probability);

/// <summary>The unit-change-type ids combat resolution consults (FreeCol <c>UnitChangeType</c>).</summary>
public static class UnitChangeTypeIds
{
    /// <summary>A winning unit's advance (free colonist → veteran soldier → colonial regular).</summary>
    public const string Promotion = "model.unitChange.promotion";

    /// <summary>A losing unit's step down (veteran soldier → free colonist; artillery → damaged artillery).</summary>
    public const string Demotion = "model.unitChange.demotion";

    /// <summary>An <c>owner-change</c> transition: a captured unit changes side (veteran soldier → free colonist).</summary>
    public const string Capture = "model.unitChange.capture";

    /// <summary>A skill learned by exploring a Lost City Rumour (free colonist / indentured servant / petty criminal → seasoned scout).</summary>
    public const string LostCity = "model.unitChange.lostCity";

    /// <summary>An on-the-job upgrade from accumulated work experience (free colonist → the matching expert for the good it works).</summary>
    public const string Experience = "model.unitChange.experience";
}
