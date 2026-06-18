namespace CrownAndColony.GameLogic.World;

/// <summary>
/// A named geographic region of the map (FreeCol <c>Region</c>): a contiguous area of one
/// <see cref="RegionType"/> carrying a discovery <see cref="ScoreValue"/>. The fixed regions — the
/// arctic/antarctic polar bands and the Atlantic/Pacific oceans — carry a predefined <see cref="Key"/>;
/// the dynamically numbered land and mountain regions have a null key. An ocean's north/south leaf
/// quadrants link to their parent ocean via <see cref="ParentId"/> — the only region hierarchy we model.
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
public sealed record Region(int Id, RegionType Type, int ScoreValue, string? Key = null, int? ParentId = null);
