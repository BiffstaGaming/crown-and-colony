namespace CrownAndColony.GameLogic.World;

/// <summary>
/// The kind of a map <see cref="Region"/> — the faithful subset of FreeCol's <c>Region.RegionType</c>
/// that our generator actually produces. <see cref="River"/> is declared for save-format stability but
/// is unused until rivers are generated (FreeCol's COAST/DESERT types are likewise deferred).
/// Serialized as the explicit enum ordinal, so these values must not be renumbered.
/// </summary>
public enum RegionType
{
    /// <summary>Open water — the Atlantic/Pacific oceans and their north/south quadrants.</summary>
    Ocean = 0,

    /// <summary>Dry land — a contiguous landmass, or one of the arctic/antarctic polar bands.</summary>
    Land = 1,

    /// <summary>A contiguous block of hill/mountain terrain.</summary>
    Mountain = 2,

    /// <summary>A river system. Reserved — not produced until rivers exist.</summary>
    River = 3,

    /// <summary>
    /// An inland lake: a body of water with no sea route to the open ocean (FreeCol
    /// <c>RegionType.LAKE</c>). Any water the ocean fill cannot reach is an enclosed lake.
    /// </summary>
    Lake = 4,
}
