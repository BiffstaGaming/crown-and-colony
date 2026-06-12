namespace CrownAndColony.GameLogic.Specification;

/// <summary>
/// A terrain type from the ruleset (e.g. plains, ocean, mixed forest) —
/// immutable rule data, not per-tile state.
/// </summary>
/// <param name="Id">Ruleset id, e.g. <c>model.tile.plains</c>.</param>
/// <param name="MoveCost">
/// Cost to enter this terrain, in FreeCol movement units (3 = one normal move;
/// 6/9 = harder going). Matches the spec's <c>basic-move-cost</c> scale.
/// </param>
/// <param name="WorkTurns">Turns a pioneer needs to improve this terrain (<c>basic-work-turns</c>).</param>
/// <param name="IsForest">Forested terrain (clearable to its base type in later phases).</param>
/// <param name="IsWater">Water terrain — land units cannot enter.</param>
/// <param name="IsElevation">Hills or mountains.</param>
/// <param name="CanSettle">Whether a colony can be founded here.</param>
/// <param name="IsConnected">Water connected to the high seas (ocean yes, lake no).</param>
/// <param name="Productions">What this terrain can produce when worked.</param>
/// <param name="Gen">Climate envelope for map generation; null when the spec defines none.</param>
public sealed record TerrainType(
    string Id,
    int MoveCost,
    int WorkTurns,
    bool IsForest,
    bool IsWater,
    bool IsElevation,
    bool CanSettle,
    bool IsConnected,
    IReadOnlyList<ProductionEntry> Productions,
    GenRanges? Gen)
{
    /// <summary>Short name derived from the id: <c>model.tile.plains</c> → <c>plains</c>.</summary>
    public string ShortName => Id[(Id.LastIndexOf('.') + 1)..];
}

/// <summary>
/// One production option of a terrain type: either what the tile yields with no
/// colonist working it (<paramref name="Unattended"/>) or one choice of attended output.
/// </summary>
/// <param name="Unattended">True for the tile's automatic colony-center yield.</param>
/// <param name="Outputs">Goods produced, e.g. grain 5.</param>
public sealed record ProductionEntry(bool Unattended, IReadOnlyList<GoodsOutput> Outputs);

/// <summary>A quantity of one goods type, e.g. <c>model.goods.grain</c> × 5.</summary>
/// <param name="GoodsId">Ruleset goods id.</param>
/// <param name="Amount">Base amount per turn.</param>
public sealed record GoodsOutput(string GoodsId, int Amount);
