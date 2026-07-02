namespace CrownAndColony.GameLogic.World;

/// <summary>
/// The kind of a map <see cref="Region"/> — FreeCol's <c>Region.RegionType</c>. Our generator produces
/// <see cref="Ocean"/>/<see cref="Land"/>/<see cref="Mountain"/>/<see cref="River"/>/<see cref="Lake"/>;
/// <see cref="Coast"/> and <see cref="Desert"/> are <b>reserved, never instantiated</b> — faithful to FreeCol,
/// whose generator also declares but never creates a COAST or DESERT region (they exist only as enum values in
/// <c>common/model/Region.java</c>). Serialized as the explicit enum ordinal, so these values must not be renumbered.
/// </summary>
public enum RegionType
{
    /// <summary>Open water — the Atlantic/Pacific oceans and their north/south quadrants.</summary>
    Ocean = 0,

    /// <summary>Dry land — a contiguous landmass, one of the arctic/antarctic polar bands, or a virtual geographic-thirds box.</summary>
    Land = 1,

    /// <summary>A contiguous block of hill/mountain terrain.</summary>
    Mountain = 2,

    /// <summary>
    /// A connected river system (86d3fpxnm; FreeCol <c>TerrainGenerator.createRivers</c> creates one discoverable
    /// <c>ServerRegion(RegionType.RIVER)</c> per river and <c>River.java</c> adds each river tile to it —
    /// <c>ServerRegion.addTile</c> re-points the tile's region). River tiles are <b>reassigned</b> from their land
    /// region to their river region; score = 2 × the summed per-tile river magnitudes (FreeCol
    /// <c>2 × Σ RiverSection.getSize()</c>).
    /// </summary>
    River = 3,

    /// <summary>
    /// An inland lake: a body of water with no sea route to the open ocean (FreeCol
    /// <c>RegionType.LAKE</c>). Any water the ocean fill cannot reach is an enclosed lake.
    /// </summary>
    Lake = 4,

    /// <summary>Reserved (never instantiated) — FreeCol declares COAST but its generator never creates one; we mirror that.</summary>
    Coast = 5,

    /// <summary>Reserved (never instantiated) — FreeCol declares DESERT but its generator never creates one; we mirror that.</summary>
    Desert = 6,
}
