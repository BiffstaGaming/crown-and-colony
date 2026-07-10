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
/// One selectable starting colony's full descriptor (Mode 3, doc 04): its player-facing <paramref name="DisplayName"/>
/// and the doc-04 <paramref name="DifficultyTier"/> + <paramref name="Identity"/> the New-Game dropdown shows, plus the
/// engine data — the FreeCol i18n region <paramref name="RegionKey"/> and the coastal landing <paramref name="StartTile"/>.
/// The tier + identity are <b>displayed framing</b> (the difficulty is delivered by the geography — an island start is
/// isolated, a remote coast is far from help); an explicit mechanical difficulty modifier is a later balance-pass decision.
/// </summary>
/// <param name="Colony">Which colony this describes.</param>
/// <param name="DisplayName">The dropdown label's colony name (e.g. "Western Australia (Swan River)").</param>
/// <param name="DifficultyTier">The doc-04 difficulty framing (Standard / Medium / Medium-Hard / Hard).</param>
/// <param name="Identity">The doc-04 one-line gameplay identity.</param>
/// <param name="RegionKey">The map region key naming this colony (e.g. <c>model.region.newSouthWales</c>).</param>
/// <param name="StartTile">The coastal tile the human lands on (this region's seed tile on the Australia map).</param>
public sealed record AustraliaColonyInfo(
    AustraliaColony Colony,
    string DisplayName,
    string DifficultyTier,
    string Identity,
    string RegionKey,
    Position StartTile);

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
    /// The six colonies' full descriptors (Mode 3, doc 04): the display name, difficulty tier, gameplay identity, the
    /// FreeCol i18n region <see cref="Region.Key"/> (whose camelCase suffix humanises to the plain colony name, e.g.
    /// <c>newSouthWales</c> → "New South Wales") and the coastal landing <see cref="AustraliaColonyInfo.StartTile"/>. The
    /// tiles are verified settleable, coastal land tiles on <c>australia.txt</c> and match the region generator's seeds
    /// one-for-one. <b>The tiles must stay in sync</b> with that generator (the same six coordinates). Tier + identity are
    /// verbatim from doc 04's Mode 3 table.
    /// </summary>
    private static readonly IReadOnlyDictionary<AustraliaColony, AustraliaColonyInfo> Infos =
        new Dictionary<AustraliaColony, AustraliaColonyInfo>
        {
            [AustraliaColony.NewSouthWales] = new(AustraliaColony.NewSouthWales, "New South Wales", "Standard",
                "Survival, convicts, first settlement, later the political centre.", "model.region.newSouthWales", new Position(50, 24)),
            [AustraliaColony.Victoria] = new(AustraliaColony.Victoria, "Victoria (Port Phillip)", "Medium",
                "Gold-rush power, high immigration, democratic pressure.", "model.region.victoria", new Position(46, 30)),
            [AustraliaColony.Queensland] = new(AustraliaColony.Queensland, "Queensland (Moreton Bay)", "Medium-Hard",
                "Distance, pastoral expansion, tropical resources, low Federation support.", "model.region.queensland", new Position(49, 10)),
            [AustraliaColony.SouthAustralia] = new(AustraliaColony.SouthAustralia, "South Australia", "Medium",
                "Free settlement, planning, reform, suffrage, agriculture.", "model.region.southAustralia", new Position(36, 28)),
            [AustraliaColony.Tasmania] = new(AustraliaColony.Tasmania, "Tasmania (Van Diemen's Land)", "Hard",
                "Island logistics, seal and fish, a harsher frontier.", "model.region.tasmania", new Position(41, 33)),
            [AustraliaColony.WesternAustralia] = new(AustraliaColony.WesternAustralia, "Western Australia (Swan River)", "Hard",
                "Distance, low population, later gold and Federation hesitation.", "model.region.westernAustralia", new Position(12, 20)),
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

    /// <summary>The full descriptor (display name, difficulty tier, identity, region key, start tile) of <paramref name="colony"/>.</summary>
    /// <param name="colony">The chosen starting colony.</param>
    /// <exception cref="System.ArgumentOutOfRangeException"><paramref name="colony"/> is not a defined colony.</exception>
    public static AustraliaColonyInfo Info(AustraliaColony colony) =>
        Infos.TryGetValue(colony, out AustraliaColonyInfo? info)
            ? info
            : throw new System.ArgumentOutOfRangeException(nameof(colony), colony, "Unknown Australia colony.");

    /// <summary>The player-facing colony name for the New-Game dropdown (e.g. "Western Australia (Swan River)").</summary>
    /// <param name="colony">The chosen starting colony.</param>
    public static string DisplayName(AustraliaColony colony) => Info(colony).DisplayName;

    /// <summary>The doc-04 difficulty framing (Standard / Medium / Medium-Hard / Hard) shown beside the colony name.</summary>
    /// <param name="colony">The chosen starting colony.</param>
    public static string DifficultyTier(AustraliaColony colony) => Info(colony).DifficultyTier;

    /// <summary>The doc-04 one-line gameplay identity shown for the colony.</summary>
    /// <param name="colony">The chosen starting colony.</param>
    public static string Identity(AustraliaColony colony) => Info(colony).Identity;

    /// <summary>The coastal tile the human lands on when starting in <paramref name="colony"/> (its region's seed tile on the Australia map).</summary>
    /// <param name="colony">The chosen starting colony.</param>
    /// <exception cref="System.ArgumentOutOfRangeException"><paramref name="colony"/> is not a defined colony.</exception>
    public static Position StartTile(AustraliaColony colony) => Info(colony).StartTile;

    /// <summary>The region <see cref="Region.Key"/> naming <paramref name="colony"/> on the map (e.g. <c>model.region.newSouthWales</c>).</summary>
    /// <param name="colony">The chosen starting colony.</param>
    /// <exception cref="System.ArgumentOutOfRangeException"><paramref name="colony"/> is not a defined colony.</exception>
    public static string RegionKey(AustraliaColony colony) => Info(colony).RegionKey;

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
