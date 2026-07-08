using CrownAndColony.GameLogic.Specification;

namespace CrownAndColony.GameLogic.World;

/// <summary>
/// The six selectable starting colonies of the Australian Federation variant (Mode 3 — "Colony Start Scenarios",
/// design doc <c>04_Playable_Powers_and_Scenario_Modes.md</c>; task <c>86d3mm2ug</c>). Each corresponds to one of the
/// six historical colonies that federated in 1901 and to a named <see cref="Region"/> on the authored Australia map
/// (<c>data/maps/australia.txt</c>): picking one lands the human on that colony's coast instead of the map's default
/// First-Fleet landfall (New South Wales).
///
/// <para>This is the <b>logic-level</b> selection API only (engine/data — task <c>86d3mm2ug</c> is explicitly not the
/// Godot UI): it resolves a chosen colony to the coastal start <see cref="AustraliaColonyStart.StartTile">tile</see>
/// and to a <see cref="MapImportResult"/> the caller feeds to <see cref="GameSession.Game.New"/> via its
/// <c>importOverride</c> seam — the same seam a fixed <c>[starts]</c> section uses. The New-Game UI wiring is a later
/// task.</para>
/// </summary>
public enum AustraliaColony
{
    /// <summary>New South Wales — the First-Fleet landfall and the map's default start (Sydney Cove, 1788).</summary>
    NewSouthWales,

    /// <summary>Victoria — the far south-east corner (Port Phillip).</summary>
    Victoria,

    /// <summary>Queensland — the north-east (Moreton Bay).</summary>
    Queensland,

    /// <summary>South Australia — the south-central coast (free-settlement colony).</summary>
    SouthAustralia,

    /// <summary>Tasmania — the southern island / peninsula (Van Diemen's Land).</summary>
    Tasmania,

    /// <summary>Western Australia — the western third (Swan River).</summary>
    WesternAustralia,
}

/// <summary>
/// Resolves an <see cref="AustraliaColony"/> to its starting site on the authored Australia map — the coastal tile the
/// human lands on, and a ready-to-boot <see cref="MapImportResult"/> for <see cref="GameSession.Game.New"/>. The six
/// start tiles are the <b>same coastal seeds</b> the region generator uses to carve the six colony
/// <see cref="Region"/>s (<c>scripts/generate-australia-regions.py</c>), so a colony's start always lies inside its own
/// named region. Engine-free (ADR-006) and RNG-free: the start tile is a fixed lookup, so choosing a colony perturbs no
/// RNG stream (ADR-009) — it only relocates the human's landfall, exactly as a scenario's <c>[starts] human X Y</c> does.
/// </summary>
public static class AustraliaColonyStart
{
    /// <summary>
    /// The six colonies' start data: the FreeCol i18n region <see cref="Region.Key"/> (whose camelCase suffix humanises
    /// to the display name, e.g. <c>newSouthWales</c> → "New South Wales") and the coastal landing
    /// <see cref="StartTile"/>. The tiles are verified settleable, coastal land tiles on <c>australia.txt</c> and match
    /// the region generator's seeds one-for-one. <b>Must stay in sync</b> with that generator (the same six coordinates).
    /// </summary>
    private static readonly IReadOnlyDictionary<AustraliaColony, (string RegionKey, Position StartTile)> Sites =
        new Dictionary<AustraliaColony, (string, Position)>
        {
            [AustraliaColony.NewSouthWales] = ("model.region.newSouthWales", new Position(50, 24)),
            [AustraliaColony.Victoria] = ("model.region.victoria", new Position(46, 30)),
            [AustraliaColony.Queensland] = ("model.region.queensland", new Position(49, 10)),
            [AustraliaColony.SouthAustralia] = ("model.region.southAustralia", new Position(36, 28)),
            [AustraliaColony.Tasmania] = ("model.region.tasmania", new Position(41, 33)),
            [AustraliaColony.WesternAustralia] = ("model.region.westernAustralia", new Position(12, 20)),
        };

    /// <summary>The default starting colony — New South Wales, the historical First-Fleet landfall the map itself fixes.</summary>
    public const AustraliaColony Default = AustraliaColony.NewSouthWales;

    /// <summary>Every selectable starting colony, in the canonical Federation order (NSW, Vic, Qld, SA, Tas, WA).</summary>
    public static IReadOnlyList<AustraliaColony> All { get; } =
    [
        AustraliaColony.NewSouthWales,
        AustraliaColony.Victoria,
        AustraliaColony.Queensland,
        AustraliaColony.SouthAustralia,
        AustraliaColony.Tasmania,
        AustraliaColony.WesternAustralia,
    ];

    /// <summary>The coastal tile the human lands on when starting in <paramref name="colony"/> (its region's seed tile on the Australia map).</summary>
    /// <param name="colony">The chosen starting colony.</param>
    /// <exception cref="System.ArgumentOutOfRangeException"><paramref name="colony"/> is not a defined colony.</exception>
    public static Position StartTile(AustraliaColony colony) =>
        Sites.TryGetValue(colony, out (string RegionKey, Position StartTile) site)
            ? site.StartTile
            : throw new System.ArgumentOutOfRangeException(nameof(colony), colony, "Unknown Australia colony.");

    /// <summary>The region <see cref="Region.Key"/> naming <paramref name="colony"/> on the map (e.g. <c>model.region.newSouthWales</c>).</summary>
    /// <param name="colony">The chosen starting colony.</param>
    /// <exception cref="System.ArgumentOutOfRangeException"><paramref name="colony"/> is not a defined colony.</exception>
    public static string RegionKey(AustraliaColony colony) =>
        Sites.TryGetValue(colony, out (string RegionKey, Position StartTile) site)
            ? site.RegionKey
            : throw new System.ArgumentOutOfRangeException(nameof(colony), colony, "Unknown Australia colony.");

    /// <summary>
    /// The <see cref="MapImportResult"/> for a game starting in <paramref name="colony"/>: the authored Australia map
    /// (terrain + the six-colony region layer) with the human's landing tile set to that colony's coast. Pass the result
    /// straight to <see cref="GameSession.Game.New"/>'s <c>importOverride</c> parameter — it overrides only the
    /// <see cref="MapImportResult.HumanStart"/>, so every other layer (regions, REF entry, settlements) is the map's own.
    /// For <see cref="AustraliaColony.NewSouthWales"/> this equals the unmodified import (the map already fixes NSW as the
    /// First-Fleet landfall), so the default colony boots byte-identically to an ordinary Australia game.
    /// </summary>
    /// <param name="colony">The chosen starting colony.</param>
    /// <param name="ruleset">The Australia ruleset whose ids the map resolves against.</param>
    /// <exception cref="System.ArgumentOutOfRangeException"><paramref name="colony"/> is not a defined colony.</exception>
    public static MapImportResult ImportFor(AustraliaColony colony, Ruleset ruleset)
    {
        System.ArgumentNullException.ThrowIfNull(ruleset);
        MapImportResult imported = FixedMap.ImportAustralia(ruleset);
        return imported with { HumanStart = StartTile(colony) };
    }
}
