namespace CrownAndColony.GameLogic.World;

/// <summary>
/// A named geographic region of the map (FreeCol <c>Region</c>): a contiguous area of one
/// <see cref="RegionType"/> carrying a discovery <see cref="ScoreValue"/>. The fixed regions — the
/// arctic/antarctic polar bands and the Atlantic/Pacific oceans — carry a predefined <see cref="Key"/>;
/// the dynamically numbered land and mountain regions have a null key. An ocean's north/south leaf
/// quadrants link to their parent ocean via <see cref="ParentId"/> — the only region hierarchy we model.
///
/// <para><b>Discovery</b> (FreeCol <c>Region.discover</c> / <c>ServerRegion.csDiscover</c>): a region is
/// <em>discoverable</em> while no colonial player has reached it (see <see cref="IsDiscoverable"/>). The first
/// such player to reveal one of its tiles discovers it — that stamps <see cref="DiscoveredBy"/>,
/// <see cref="Name"/> and <see cref="DiscoveredInTurn"/>, after which it is no longer discoverable. Only
/// dynamically-numbered land/mountain regions and the Pacific Ocean are discoverable; the polar bands, the
/// Atlantic, the ocean leaf quadrants and inland lakes never are (their tiles instead discover their
/// discoverable Pacific parent, if any, via <see cref="GameMap.DiscoverableRegionOf"/>).</para>
/// </summary>
/// <param name="Id">Stable region id; equals the region's index in <see cref="GameMap.Regions"/>.</param>
/// <param name="Type">What kind of region this is.</param>
/// <param name="ScoreValue">The flat score FreeCol awards the player who first discovers the region.</param>
/// <param name="Key">
/// The FreeCol predefined key of a fixed region (e.g. <c>model.region.arctic</c>), or null for a
/// generated land/mountain region.
/// </param>
/// <param name="ParentId">
/// The <see cref="Id"/> of the parent region (an ocean quadrant's parent ocean), or null at the top level.
/// </param>
/// <param name="DiscoveredBy">
/// The <see cref="GameSession.Player.PlayerId"/> of the colonial player that first discovered this region,
/// or <c>null</c> while still undiscovered. Mirrors FreeCol <c>Region.discoveredBy</c>.
/// </param>
/// <param name="Name">
/// The player-given name assigned on discovery (e.g. <c>"Land 1"</c>), or <c>null</c> while undiscovered.
/// Mirrors FreeCol <c>Region.name</c>; a fixed region also carries its own <see cref="Key"/> for display.
/// </param>
/// <param name="DiscoveredInTurn">
/// The game turn the region was discovered in, or <c>null</c> while undiscovered. Mirrors FreeCol
/// <c>Region.discoveredIn</c>.
/// </param>
/// <param name="Bounds">
/// The virtual bounding box of a <b>geographic-thirds</b> region (86d3fpxnm; FreeCol <c>ServerRegion.bounds</c>, set
/// only by <c>getStandardRegions</c>' nine thirds boxes for native settlement placement), or <c>null</c> for every
/// tile-bearing region. A bounded region is <b>virtual</b>: it claims no tiles (no tile's region id points at it) and
/// is never discoverable (it always carries a fixed <see cref="Key"/>).
/// </param>
public sealed record Region(
    int Id, RegionType Type, int ScoreValue, string? Key = null, int? ParentId = null,
    int? DiscoveredBy = null, string? Name = null, int? DiscoveredInTurn = null,
    RegionBounds? Bounds = null)
{
    /// <summary>The Pacific Ocean's predefined key — the one ocean region the player can discover (FreeCol <c>Region.PACIFIC_KEY</c>).</summary>
    public const string PacificKey = "model.region.pacific";

    /// <summary>True once a colonial player has discovered this region (FreeCol <c>Region.discoveredBy != null</c>).</summary>
    public bool IsDiscovered => DiscoveredBy is not null;

    /// <summary>
    /// Whether this region can still be discovered (FreeCol <c>ServerRegion</c> discoverability): an
    /// undiscovered, dynamically-numbered <see cref="RegionType.Land"/>/<see cref="RegionType.Mountain"/>/
    /// <see cref="RegionType.River"/> region (one with no fixed <see cref="Key"/> — FreeCol's
    /// <c>new ServerRegion(game, RegionType.RIVER)</c> ctor sets <c>discoverable = true</c> like the land/mountain
    /// ones), or the undiscovered Pacific Ocean. The polar bands, the Atlantic, the ocean leaf quadrants, lakes and
    /// the keyed geographic-thirds boxes are never directly discoverable — their tiles (if any) resolve to the
    /// discoverable Pacific parent at the map level.
    /// </summary>
    public bool IsDiscoverable =>
        !IsDiscovered
        && ((Type is RegionType.Land or RegionType.Mountain or RegionType.River && Key is null)
            || Key == PacificKey);
}

/// <summary>
/// A region's virtual bounding box (FreeCol <c>ServerRegion.bounds</c>, a <c>java.awt.Rectangle</c>): the half-open
/// tile box <c>[X, X+Width) × [Y, Y+Height)</c>. Carried only by the nine geographic-thirds regions (86d3fpxnm); the
/// nine boxes partition the map exactly (every tile lies in exactly one). Consumed the way FreeCol's
/// <c>SimpleMapGenerator</c> uses <c>getBounds()</c> — a <c>Rectangle.contains(x, y)</c> point test.
/// </summary>
/// <param name="X">Left column, inclusive.</param>
/// <param name="Y">Top row, inclusive.</param>
/// <param name="Width">Box width in tiles (right edge <c>X + Width</c> exclusive).</param>
/// <param name="Height">Box height in tiles (bottom edge <c>Y + Height</c> exclusive).</param>
public readonly record struct RegionBounds(int X, int Y, int Width, int Height)
{
    /// <summary>Whether <paramref name="p"/> lies inside this half-open box (Java <c>Rectangle.contains</c> semantics).</summary>
    public bool Contains(Position p) => p.X >= X && p.X < X + Width && p.Y >= Y && p.Y < Y + Height;
}
