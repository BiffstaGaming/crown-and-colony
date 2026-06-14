namespace CrownAndColony.GameLogic.Specification;

/// <summary>
/// A unit a European nation begins the game with (FreeCol <c>&lt;european-nation-type&gt; &lt;unit&gt;</c>):
/// the colonist/soldier/ship a colonial power lands with. <paramref name="Slot"/> groups variants
/// (e.g. a regular soldier and its <see cref="Expert"/> upgrade); <see cref="EuropeanNationType.RegularStartingUnits"/>
/// picks the non-expert one when placing.
/// </summary>
/// <param name="Slot">Role slot id in the spec (<c>pioneer</c> / <c>soldier</c> / <c>ship</c>).</param>
/// <param name="UnitTypeId">The unit type to create, e.g. <c>model.unit.caravel</c>.</param>
/// <param name="RoleId">The military/work role to equip (null = the default role).</param>
/// <param name="Mounted">Whether the unit starts mounted (Spanish dragoon soldier).</param>
/// <param name="Expert">Whether this is the expert-starting-units variant (used only under that game option).</param>
public sealed record EuropeanStartingUnit(string Slot, string UnitTypeId, string? RoleId, bool Mounted, bool Expert);

/// <summary>
/// A European nation type from the ruleset (FreeCol <c>&lt;european-nation-type&gt;</c>): the national
/// "advantage" archetype — the shared <c>default</c>, the four classic powers' types (trade/cooperation/
/// immigration/conquest), the Royal Expeditionary Force (<c>ref</c>), and FreeCol's extra types — with its
/// starting units and its ability/modifier advantages, resolved up the <c>extends</c> chain.
/// </summary>
/// <param name="Id">Ruleset id, e.g. <c>model.nationType.trade</c>.</param>
/// <param name="IsRef">Whether this is a Royal Expeditionary Force type (parsed but inert until Phase 6).</param>
/// <param name="StartingUnits">Starting units (nearest <c>extends</c> level wins per slot; both expert and non-expert variants kept).</param>
/// <param name="Abilities">Granted abilities (e.g. <c>canRecruitUnit</c>, <c>foundsColonies</c>), inherited + own.</param>
/// <param name="Modifiers">Advantage modifiers (e.g. Dutch <c>tradeBonus −50%</c>), inherited + own.</param>
public sealed record EuropeanNationType(
    string Id,
    bool IsRef,
    IReadOnlyList<EuropeanStartingUnit> StartingUnits,
    IReadOnlyList<FatherAbility> Abilities,
    IReadOnlyList<FatherModifier> Modifiers)
{
    /// <summary>Short name derived from the id: <c>model.nationType.trade</c> → <c>trade</c>.</summary>
    public string ShortName => Id[(Id.LastIndexOf('.') + 1)..];

    /// <summary>The starting units actually placed for a new nation — the non-expert variant of each slot.</summary>
    public IEnumerable<EuropeanStartingUnit> RegularStartingUnits => StartingUnits.Where(u => !u.Expert);
}

/// <summary>
/// A European nation from the ruleset (FreeCol <c>&lt;nation&gt;</c>): one of the colonial powers — the
/// classic Dutch, French, English and Spanish (and FreeCol's extra powers) — or a Royal Expeditionary
/// Force. Carries its display name, colour, the <see cref="EuropeanNationType"/> that gives its advantage
/// and starting units, its REF nation id, whether it is selectable, and its per-nation colony-name list.
/// </summary>
/// <param name="Id">Ruleset id, e.g. <c>model.nation.dutch</c>.</param>
/// <param name="DisplayName">Player-facing name derived from the id (e.g. <c>Dutch</c>).</param>
/// <param name="NationType">The resolved nation type (advantage + starting units).</param>
/// <param name="Color">Hex colour string from the spec (e.g. <c>0xff9d3c</c>), or null.</param>
/// <param name="Selectable">Whether the nation is a playable colonial power.</param>
/// <param name="RefNationId">The nation's Royal Expeditionary Force nation id (null for REF nations themselves).</param>
/// <param name="ColonyNames">Colony names used in order when this nation founds colonies (classic FreeCol list; empty if none).</param>
public sealed record EuropeanNation(
    string Id,
    string DisplayName,
    EuropeanNationType NationType,
    string? Color,
    bool Selectable,
    string? RefNationId,
    IReadOnlyList<string> ColonyNames)
{
    /// <summary>Whether this nation is a Royal Expeditionary Force (inert until Phase 6).</summary>
    public bool IsRef => NationType.IsRef;

    /// <summary>Short name derived from the id: <c>model.nation.dutch</c> → <c>dutch</c>.</summary>
    public string ShortName => Id[(Id.LastIndexOf('.') + 1)..];
}
