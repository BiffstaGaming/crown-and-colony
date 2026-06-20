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
/// <param name="IsNatural">
/// True for a natural feature placed by the world (rivers), false for a pioneer-built improvement
/// (road/plow/clear-forest). FreeCol <c>natural</c>; only a non-natural improvement is buildable.
/// </param>
/// <param name="RequiredRoleId">
/// The role a unit must hold to build this improvement (FreeCol <c>required-role</c>) — the pioneer role
/// (<c>model.role.pioneer</c>) for road/plow/clear-forest; null for a natural feature nothing builds.
/// </param>
/// <param name="ExpendedAmount">
/// How many of the required role's equipment multiples this improvement consumes on completion (FreeCol
/// <c>expended-amount</c>): 1 for every pioneer improvement — one pioneer count = 20 tools. 0 for a natural
/// feature.
/// </param>
/// <param name="ExposeResourcePercent">
/// Percent chance the improvement exposes a bonus resource on completion (FreeCol
/// <c>expose-resource-percent</c>; only the tile-type-change clear-forest carries one). Carried for fidelity;
/// resource exposure is a documented deferred follow-up (it would need a world-RNG draw).
/// </param>
/// <param name="Scopes">
/// The applicability scopes (FreeCol <c>&lt;scope&gt;</c> children): a tile's terrain must satisfy <b>all</b>
/// of them for the improvement to be allowed there (e.g. plow requires not-water and not-forested). Null/empty
/// means "any terrain".
/// </param>
/// <param name="TileTypeChanges">
/// The terrain transformations this improvement performs on completion (FreeCol <c>&lt;tile-type-change&gt;</c>),
/// keyed by the <i>from</i> terrain id — clear-forest maps each forest to its cleared base terrain and the
/// one-off goods (lumber) it delivers. Null/empty for an improvement that lays on top of the terrain (road/plow).
/// </param>
public sealed record TileImprovementType(
    string Id,
    int Magnitude,
    int MovementCost,
    int AddWorkTurns,
    IReadOnlyList<ImprovementModifier> Modifiers,
    bool IsNatural = false,
    string? RequiredRoleId = null,
    int ExpendedAmount = 0,
    int ExposeResourcePercent = 0,
    IReadOnlyList<ImprovementScope>? Scopes = null,
    IReadOnlyDictionary<string, ImprovementTypeChange>? TileTypeChanges = null)
{
    private static readonly IReadOnlyList<ImprovementScope> NoScopes = [];
    private static readonly IReadOnlyDictionary<string, ImprovementTypeChange> NoChanges =
        new Dictionary<string, ImprovementTypeChange>();

    /// <summary>The classic ruleset id of the river improvement type.</summary>
    public const string RiverId = "model.improvement.river";

    /// <summary>The classic ruleset id of the road improvement type (pioneer-built; speeds movement).</summary>
    public const string RoadId = "model.improvement.road";

    /// <summary>The classic ruleset id of the plowed-field improvement type (pioneer-built; +1 farmed goods).</summary>
    public const string PlowId = "model.improvement.plow";

    /// <summary>The classic ruleset id of the clear-forest improvement type (pioneer-built; forest → cleared base + lumber).</summary>
    public const string ClearForestId = "model.improvement.clearForest";

    /// <summary>Short name derived from the id: <c>model.improvement.river</c> → <c>river</c>.</summary>
    public string ShortName => Id[(Id.LastIndexOf('.') + 1)..];

    /// <summary>True when this improvement grants a reduced cost to travel along it (river/road bonus).</summary>
    public bool GrantsMovementBonus => MovementCost > 0;

    /// <summary>True when this is the road improvement.</summary>
    public bool IsRoad => Id == RoadId;

    /// <summary>True when this is the plowed-field improvement.</summary>
    public bool IsPlow => Id == PlowId;

    /// <summary>True when this is the clear-forest improvement.</summary>
    public bool IsClearForest => Id == ClearForestId;

    /// <summary>True when this improvement transforms the underlying terrain on completion (FreeCol <c>isChangeType</c>) — clear-forest.</summary>
    public bool ChangesTerrain => TileTypeChanges is { Count: > 0 };

    /// <summary>The applicability scopes (never null).</summary>
    public IReadOnlyList<ImprovementScope> ScopesOrEmpty => Scopes ?? NoScopes;

    /// <summary>The terrain transformations keyed by from-terrain id (never null).</summary>
    public IReadOnlyDictionary<string, ImprovementTypeChange> TileTypeChangesOrEmpty => TileTypeChanges ?? NoChanges;

    /// <summary>
    /// Whether this improvement is allowed on the given terrain in principle (FreeCol
    /// <c>TileImprovementType.isTileTypeAllowed</c>): every scope must apply. Disregards whether the tile already
    /// carries the improvement — that is the caller's per-tile check.
    /// </summary>
    /// <param name="terrain">The terrain to test.</param>
    public bool AppliesTo(Specification.TerrainType terrain) =>
        ScopesOrEmpty.All(s => s.AppliesTo(terrain));

    /// <summary>
    /// The terrain transformation this improvement performs on the given from-terrain (clear-forest's forest →
    /// cleared base + lumber), or null when it does not transform that terrain.
    /// </summary>
    /// <param name="fromTerrainId">The terrain the improvement is built on.</param>
    public ImprovementTypeChange? ChangeFrom(string fromTerrainId) =>
        TileTypeChangesOrEmpty.GetValueOrDefault(fromTerrainId);

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

/// <summary>
/// One applicability scope of an improvement type (FreeCol <c>&lt;scope&gt;</c>): either a terrain predicate
/// (<paramref name="MethodName"/> + <paramref name="MethodValue"/>, e.g. <c>isWater=false</c>) or a specific
/// terrain id that must (or, when negated, must not) match (<paramref name="TileTypeId"/> +
/// <paramref name="MatchNegated"/>, e.g. exclude <c>model.tile.hills</c>). An improvement applies to a terrain
/// only when <b>every</b> scope is satisfied (FreeCol <c>Scope.appliesTo</c>).
/// </summary>
/// <param name="MethodName">
/// The terrain predicate to test (<c>isWater</c>, <c>isForested</c>, <c>isElevation</c>), or null for a
/// terrain-id scope. FreeCol invokes the named getter; we map it to the matching <see cref="Specification.TerrainType"/> flag.
/// </param>
/// <param name="MethodValue">The value the predicate must equal (FreeCol <c>method-value</c>).</param>
/// <param name="TileTypeId">The terrain id this scope matches (FreeCol scope <c>type</c>), or null for a predicate scope.</param>
/// <param name="MatchNegated">When true, a terrain-id scope is satisfied by <i>not</i> being that terrain (FreeCol <c>match-negated</c>).</param>
public sealed record ImprovementScope(string? MethodName, bool MethodValue, string? TileTypeId, bool MatchNegated)
{
    /// <summary>Whether the given terrain satisfies this scope (FreeCol <c>Scope.appliesTo</c>).</summary>
    /// <param name="terrain">The terrain to test.</param>
    /// <exception cref="System.NotSupportedException">An unrecognised <see cref="MethodName"/> (spec drift).</exception>
    public bool AppliesTo(Specification.TerrainType terrain)
    {
        if (TileTypeId is { } id)
        {
            // A type scope: satisfied when the terrain is (or, negated, is not) that exact terrain.
            return (terrain.Id == id) != MatchNegated;
        }
        bool actual = MethodName switch
        {
            "isWater" => terrain.IsWater,
            "isForested" => terrain.IsForest,
            "isElevation" => terrain.IsElevation,
            _ => throw new System.NotSupportedException(
                $"Improvement scope method-name '{MethodName}' is not mapped to a terrain predicate."),
        };
        return actual == MethodValue;
    }
}

/// <summary>
/// A terrain transformation an improvement performs on completion (FreeCol <c>&lt;tile-type-change&gt;</c>):
/// the underlying terrain becomes <paramref name="ToTerrainId"/> and a one-off batch of
/// <paramref name="ProductionGoodsId"/> (lumber, for clear-forest) is delivered to the owning colony.
/// </summary>
/// <param name="ToTerrainId">The cleared/changed terrain id (e.g. <c>model.tile.plains</c> from mixed forest).</param>
/// <param name="ProductionGoodsId">The goods delivered on the change (e.g. <c>model.goods.lumber</c>), or null when none.</param>
/// <param name="ProductionAmount">The amount of <paramref name="ProductionGoodsId"/> delivered (0 when none).</param>
public sealed record ImprovementTypeChange(string ToTerrainId, string? ProductionGoodsId, int ProductionAmount);
