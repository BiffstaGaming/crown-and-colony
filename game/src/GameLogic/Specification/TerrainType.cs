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
/// <param name="Resources">Bonus resources that can appear on this terrain, with pick weights.</param>
/// <param name="DefenceBonus">
/// Percentage defence bonus a unit gains while standing here in combat (spec
/// <c>model.modifier.defence</c>): plains 0, marsh/swamp 25, most forest 50, rainForest 75, hills 100.
/// </param>
/// <param name="AmbushTerrain">
/// Concealing terrain that enables an ambush (spec <c>model.ability.ambushTerrain</c>: all forests and hills).
/// A native attacker striking from — or at a defender standing on — such terrain negates the defender's terrain
/// cover by gaining it as offence (FreeCol <c>Unit.canAmbush</c>; see [combat](combat.md)).
/// </param>
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
    GenRanges? Gen,
    IReadOnlyList<ResourceChance> Resources,
    double DefenceBonus = 0,
    bool AmbushTerrain = false)
{
    /// <summary>Short name derived from the id: <c>model.tile.plains</c> → <c>plains</c>.</summary>
    public string ShortName => Id[(Id.LastIndexOf('.') + 1)..];
}

/// <summary>
/// One production option of a terrain or building type: either automatic
/// (<paramref name="Unattended"/>) or per assigned worker. Buildings may consume
/// inputs (lumber 3 → hammers 3); tiles never do.
/// </summary>
/// <param name="Unattended">True for automatic production (colony-centre tile, town hall bell).</param>
/// <param name="Outputs">Goods produced, e.g. grain 5.</param>
/// <param name="Inputs">Goods consumed to produce the outputs (empty for tiles).</param>
public sealed record ProductionEntry(
    bool Unattended,
    IReadOnlyList<GoodsOutput> Outputs,
    IReadOnlyList<GoodsOutput> Inputs)
{
    /// <summary>Tile-style entry without inputs.</summary>
    public ProductionEntry(bool unattended, IReadOnlyList<GoodsOutput> outputs)
        : this(unattended, outputs, []) { }
}

/// <summary>A bonus resource a terrain can host (e.g. prime grain on plains), with its pick weight.</summary>
/// <param name="ResourceId">Ruleset resource id, e.g. <c>model.resource.grain</c>.</param>
/// <param name="Probability">Relative weight when the generator picks among the terrain's resources.</param>
public sealed record ResourceChance(string ResourceId, int Probability);

/// <summary>A quantity of one goods type, e.g. <c>model.goods.grain</c> × 5.</summary>
/// <param name="GoodsId">Ruleset goods id.</param>
/// <param name="Amount">Base amount per turn.</param>
public sealed record GoodsOutput(string GoodsId, int Amount);
