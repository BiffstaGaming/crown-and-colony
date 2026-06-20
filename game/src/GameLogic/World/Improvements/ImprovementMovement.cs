namespace CrownAndColony.GameLogic.World.Improvements;

/// <summary>
/// Pure movement rules for tile improvements — the river/road "follow it" travel bonus
/// (FreeCol <c>TileImprovementType.getMoveCost</c> plus <c>Map</c>/<c>Tile</c> connectivity). Costs are in
/// FreeCol movement units (3 = one normal move), matching <c>TerrainType.MoveCost</c>.
/// </summary>
/// <remarks>
/// The river bonus is wired into <c>Game.CheckMove</c>; <see cref="MoveCost"/> generalises it to roads (and any
/// future movement-granting improvement) — a unit moving between two tiles that both carry a connecting
/// movement-bonus improvement pays the reduced enter-cost.
/// </remarks>
public static class ImprovementMovement
{
    /// <summary>
    /// The faithful FreeCol cost-reduction rule (<c>TileImprovementType.getMoveCost(originalCost)</c>): an
    /// improvement's movement cost replaces the base cost only when it is positive <b>and strictly cheaper</b>
    /// than the base; otherwise the base stands. Never returns zero — FreeCol guards against free moves.
    /// </summary>
    /// <param name="improvementMovementCost">The improvement's declared per-enter cost (river = 1).</param>
    /// <param name="baseCost">The terrain's normal cost to enter the destination tile.</param>
    /// <returns>The reduced cost if the improvement is cheaper, else <paramref name="baseCost"/>.</returns>
    public static int ReducedCost(int improvementMovementCost, int baseCost) =>
        improvementMovementCost > 0 && improvementMovementCost < baseCost
            ? improvementMovementCost
            : baseCost;

    /// <summary>
    /// The cost to move from one tile to an adjacent tile, applying a river's "follow the river" bonus.
    /// The bonus applies only when travel runs <i>along</i> the river — i.e. <b>both</b> the origin and the
    /// destination carry a river improvement (FreeCol: "the bonus only applies when you move from one tile
    /// with a river to another"). When either tile lacks a river, the normal terrain cost stands.
    /// </summary>
    /// <param name="from">The river improvement on the tile being left, or <c>null</c> if it has none.</param>
    /// <param name="to">The river improvement on the tile being entered, or <c>null</c> if it has none.</param>
    /// <param name="baseCost">The destination terrain's normal cost to enter (3 = one normal move).</param>
    /// <returns>
    /// The river-reduced cost when both tiles have a river and the river is cheaper; otherwise
    /// <paramref name="baseCost"/>.
    /// </returns>
    /// <remarks>
    /// Foundation-slice fidelity: this models the "both endpoints carry a river" rule. FreeCol additionally
    /// checks the river's per-tile <i>style</i> so the bonus only applies along a river that actually connects
    /// in the direction of travel; that needs the per-tile river style, which depends on placement and is a
    /// deferred follow-up. The reduced cost uses the destination river's movement cost (the cost to enter is a
    /// property of the tile being entered).
    /// </remarks>
    public static int RiverMoveCost(TileImprovementType? from, TileImprovementType? to, int baseCost)
    {
        // The bonus is a "follow the river" bonus: it requires a river on both the tile left and the tile entered.
        if (from is null || to is null || !to.GrantsMovementBonus)
        {
            return baseCost;
        }

        return ReducedCost(to.MovementCost, baseCost);
    }

    /// <summary>
    /// The cost to move from one tile to an adjacent tile, applying any improvement "follow it" bonus generalised
    /// over all the improvements on each tile (river <i>and</i> road). The bonus applies only when travel runs
    /// <i>along</i> a movement-granting feature — i.e. <b>both</b> the origin and the destination carry at least one
    /// improvement that grants a movement bonus (a river or a road). When it applies, the cheapest such bonus on the
    /// destination tile is used (FreeCol takes the lowest connecting move cost). Otherwise the normal terrain cost
    /// stands. A tile with a river and a road thus still connects to a road-only or river-only neighbour.
    /// </summary>
    /// <param name="from">The improvements on the tile being left.</param>
    /// <param name="to">The improvements on the tile being entered.</param>
    /// <param name="baseCost">The destination terrain's normal cost to enter (3 = one normal move).</param>
    /// <returns>The reduced cost when both tiles carry a connecting movement-bonus improvement and it is cheaper; otherwise <paramref name="baseCost"/>.</returns>
    public static int MoveCost(
        IEnumerable<TileImprovementType> from, IEnumerable<TileImprovementType> to, int baseCost)
    {
        if (!from.Any(i => i.GrantsMovementBonus))
        {
            return baseCost; // nothing to follow from the origin tile
        }
        int? cheapest = to
            .Where(i => i.GrantsMovementBonus)
            .Select(i => (int?)i.MovementCost)
            .Min();
        return cheapest is { } cost ? ReducedCost(cost, baseCost) : baseCost;
    }
}
