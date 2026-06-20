using CrownAndColony.GameLogic.Specification;

namespace CrownAndColony.GameLogic.World.Improvements;

/// <summary>
/// A tile-improvement type from the ruleset (FreeCol <c>TileImprovementType</c>) — immutable rule data
/// describing a feature laid on a tile, not per-tile state. This foundation slice models the river
/// (FreeCol <c>model.improvement.river</c>): the production bonuses it confers, the per-enter movement
/// cost it grants, and its <see cref="Magnitude"/> styling (small vs. large river).
/// </summary>
/// <remarks>
/// <para>
/// This is the <b>model foundation only</b>. There is no map placement, no save persistence, and no wiring
/// into <c>Game.TileYield</c> or <c>Game</c> movement yet — those are explicit follow-up slices owned by a
/// later wave (see <c>docs/systems/rivers-tile-improvements.md</c> §5). The type can be constructed directly
/// (as in tests) or parsed via <see cref="FromModifiers"/>; <c>Ruleset</c> wiring is deliberately deferred so
/// this slice touches no shared files.
/// </para>
/// <para>
/// Only the river improvement is modelled here. Other FreeCol improvement types (road, plowed/cleared,
/// fish-bonus) are out of scope for this foundation and are noted as follow-ups in the system doc.
/// </para>
/// </remarks>
/// <param name="Id">Ruleset id, e.g. <c>model.improvement.river</c>.</param>
/// <param name="Magnitude">
/// The improvement's magnitude/style band. For rivers this is the river size: <c>1</c> = a small (minor)
/// river, <c>2</c> = a large (major) river. The classic spec declares the river type with magnitude 1; a
/// per-tile large river is the same type instanced at magnitude 2. Magnitude scales nothing in this slice —
/// it is carried for faithful styling and future tuning, and the river's flat production bonuses apply at
/// any magnitude (matching FreeCol, whose river <see cref="Modifiers"/> are flat additives).
/// </param>
/// <param name="MovementCost">
/// Cost (in FreeCol movement units, 3 = one normal move) to enter a tile <i>along</i> this improvement —
/// the river/road "follow it for a reduced cost" bonus. The classic river declares <c>1</c> (a third of a
/// normal move). <c>0</c> (or negative) means the improvement grants no movement bonus. The reduced cost is
/// only ever <i>applied</i> by <see cref="ImprovementMovement"/> when travel actually runs along the feature
/// between two tiles that both carry it; this field is just the rule datum.
/// </param>
/// <param name="AddWorkTurns">
/// Extra pioneer work-turns this improvement adds on top of the terrain's base (FreeCol
/// <c>add-work-turns</c>). The classic river is <c>0</c> (rivers are natural, not pioneer-built). Carried for
/// completeness; pioneer building is a later slice.
/// </param>
/// <param name="Modifiers">
/// The production bonuses this improvement confers on goods worked from the tile (FreeCol
/// <c>&lt;modifier&gt;</c> children). The classic river adds to several goods; see
/// <see cref="ImprovementProduction"/> for the yield-delta helper that consumes these.
/// </param>
public sealed record TileImprovementType(
    string Id,
    int Magnitude,
    int MovementCost,
    int AddWorkTurns,
    IReadOnlyList<ImprovementModifier> Modifiers)
{
    /// <summary>The classic ruleset id of the river improvement type.</summary>
    public const string RiverId = "model.improvement.river";

    /// <summary>Short name derived from the id: <c>model.improvement.river</c> → <c>river</c>.</summary>
    public string ShortName => Id[(Id.LastIndexOf('.') + 1)..];

    /// <summary>True when this improvement grants a reduced cost to travel along it (river/road bonus).</summary>
    public bool GrantsMovementBonus => MovementCost > 0;

    /// <summary>
    /// Builds a tile-improvement type from a flat list of additive goods bonuses (the common river/road shape),
    /// defaulting <see cref="ImprovementModifier.Type"/> to additive. Convenience for constructing the model in
    /// tests and (later) when the <c>Ruleset</c> parser is wired; it does not read the ruleset itself.
    /// </summary>
    /// <param name="id">Ruleset id.</param>
    /// <param name="magnitude">Magnitude/style band (1 = small river, 2 = large river).</param>
    /// <param name="movementCost">Per-enter movement cost along the feature (3 = one normal move).</param>
    /// <param name="addWorkTurns">Extra pioneer work-turns.</param>
    /// <param name="goodsDeltas">(goods id, flat additive delta) pairs.</param>
    public static TileImprovementType FromModifiers(
        string id,
        int magnitude,
        int movementCost,
        int addWorkTurns,
        IEnumerable<(string GoodsId, int Delta)> goodsDeltas)
    {
        var mods = goodsDeltas
            .Select(d => new ImprovementModifier(d.GoodsId, ModifierType.Additive, d.Delta))
            .ToArray();
        return new TileImprovementType(id, magnitude, movementCost, addWorkTurns, mods);
    }

    /// <summary>
    /// The classic FreeCol river improvement type (<c>model.improvement.river</c>), constructed verbatim from
    /// <c>data/rules/classic/specification.xml</c>: movement-cost 1, add-work-turns 0, magnitude 1, with its
    /// flat additive goods bonuses. Provided so this self-contained slice has a canonical instance to test
    /// against without depending on the (deferred) ruleset parser.
    /// </summary>
    /// <param name="magnitude">River size band to stamp on the returned instance (1 = small, 2 = large).</param>
    public static TileImprovementType ClassicRiver(int magnitude = 1) => FromModifiers(
        RiverId,
        magnitude,
        movementCost: 1,
        addWorkTurns: 0,
        goodsDeltas:
        [
            ("model.goods.grain", 1),
            ("model.goods.sugar", 1),
            ("model.goods.tobacco", 1),
            ("model.goods.cotton", 1),
            ("model.goods.furs", 2),
            ("model.goods.lumber", 2),
            ("model.goods.ore", 1),
            ("model.goods.silver", 1),
        ]);
}

/// <summary>
/// A production bonus a tile improvement confers on one goods type (FreeCol improvement-type
/// <c>&lt;modifier&gt;</c>): e.g. a river's +2 furs or +1 grain. Mirrors <c>ResourceModifier</c> but for
/// improvements rather than bonus-resources. The classic river's modifiers are all flat additives.
/// </summary>
/// <param name="GoodsId">The goods this changes (e.g. <c>model.goods.furs</c>).</param>
/// <param name="Type">How it combines with the tile's running yield (river bonuses are additive).</param>
/// <param name="Value">The modifier value (a flat delta for additive, a factor for multiplicative).</param>
public sealed record ImprovementModifier(string GoodsId, ModifierType Type, double Value)
{
    /// <summary>Applies this modifier to a running yield value (shared FreeCol modifier arithmetic).</summary>
    public double ApplyTo(double value) => ModifierMath.Apply(Type, value, Value);
}
