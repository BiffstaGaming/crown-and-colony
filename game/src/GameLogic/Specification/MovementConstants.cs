namespace CrownAndColony.GameLogic.Specification;

/// <summary>
/// The terrain/movement scalar constants FreeCol carries in its spec data or model code, parsed once from the spec and
/// read wherever a hardcoded movement magic number used to live. Carried on <see cref="Ruleset.MovementConstants"/> —
/// the movement analogue of <see cref="ColonyConstants"/> (which holds the colony-economy group) and
/// <see cref="GameOptions"/> (the base <c>gameOptions</c> group).
/// </summary>
/// <remarks>
/// <para>
/// Each value is FreeCol-faithful and identical to the classic constant it replaces, so routing it through the ruleset
/// leaves the default game byte-identical (ADR-009). A spec that omits a source element falls back to its classic value
/// in <see cref="ClassicDefaults"/>. Sources differ by constant — see each parameter — because FreeCol itself keeps the
/// per-step movement costs as spec data (<c>basic-move-cost</c> / a river/road's <c>movement-cost</c>) but the
/// partial-move "round up to a whole move" threshold as a model-code constant in <c>Unit.getMoveCost</c>; we make all
/// three data-overridable for the variant.
/// </para>
/// <para>
/// Note the per-<i>terrain</i> enter cost (plains 3, forest/hills 6, mountains 9) is already fully data-driven on
/// <see cref="TerrainType.MoveCost"/>, and a river/road's per-enter <c>movement-cost</c> on
/// <see cref="World.Improvements.TileImprovementType.MovementCost"/>; line-of-sight on <see cref="UnitType.LineOfSight"/>;
/// terrain defence on <see cref="TerrainType.DefenceBonus"/>. This bundle covers the remaining <em>scalar</em> movement
/// numbers that had no parsed home: the "one normal move" denominator, the cheapest possible step, and the partial-move
/// threshold.
/// </para>
/// <para>
/// Pure and immutable: parsed once, no state, no randomness. <see cref="PartialMoveThreshold"/> is read by
/// <see cref="GameSession.Game"/>'s <c>CheckMove</c> (the partial-move rule); <see cref="MinMoveCost"/> by the
/// <see cref="World.Pathfinder"/> heuristic (the admissible per-step lower bound). <see cref="BaseMoveCost"/> is the
/// FreeCol "3 = one whole move" denominator carried for fidelity and the variant.
/// </para>
/// </remarks>
/// <param name="BaseMoveCost">
/// The movement-point cost of one whole move over normal terrain (FreeCol's <c>TileType</c> <c>basic-move-cost</c> for
/// plains/grassland and the <c>/3</c> denominator <c>Unit.getMovesAsString</c> uses to show "moves left"; classic
/// <b>3</b>). Every unit's <see cref="UnitType.Movement"/> is a multiple of this. Sourced from the plains terrain's
/// parsed <see cref="TerrainType.MoveCost"/> (the canonical normal-terrain cost), else the classic default. See [units-movement].
/// </param>
/// <param name="MinMoveCost">
/// The cheapest possible cost of a single step (FreeCol: the lowest positive move cost — a river or road's
/// <c>movement-cost</c> of <b>1</b>, a third of a normal move). The <see cref="World.Pathfinder"/> heuristic multiplies
/// the remaining Chebyshev distance by this so it never overestimates the true remaining cost (A* admissibility).
/// Sourced from the cheapest positive <c>movement-cost</c> among the parsed improvement types, else the classic default.
/// See [units-movement].
/// </param>
/// <param name="PartialMoveThreshold">
/// The partial-move "round up to a whole move" slack (FreeCol <c>Unit.getMoveCost</c>'s hardcoded <c>+2</c>: "make 1/3
/// and 2/3 move count as 3/3"; classic <b>2</b>). When a unit has fewer movement points left than the next tile costs,
/// the move is still allowed for the whole remainder if the unit is near full movement (lost at most this many points)
/// or the shortfall is within this slack. A model-code constant in FreeCol with no spec element, so it takes the classic
/// default and becomes data-overridable for a variant. See [units-movement].
/// </param>
public sealed record MovementConstants(
    int BaseMoveCost,
    int MinMoveCost,
    int PartialMoveThreshold)
{
    /// <summary>
    /// The classic ruleset's movement constants — the fallback when a spec omits a source, the source of truth for the
    /// default game's movement scalars (3 / 1 / 2), and the value a pathfinder/move check run without a ruleset (tests)
    /// carries so its behaviour is unchanged.
    /// </summary>
    public static readonly MovementConstants ClassicDefaults = new(
        BaseMoveCost: 3,
        MinMoveCost: 1,
        PartialMoveThreshold: 2);
}
