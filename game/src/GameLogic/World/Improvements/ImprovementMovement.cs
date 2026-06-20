namespace CrownAndColony.GameLogic.World.Improvements;

/// <summary>
/// Pure movement rules for tile improvements — the river/road "follow it" travel bonus
/// (FreeCol <c>TileImprovementType.getMoveCost</c> plus <c>Map</c>/<c>Tile</c> connectivity). Costs are in
/// FreeCol movement units (3 = one normal move), matching <c>TerrainType.MoveCost</c>.
/// </summary>
/// <remarks>
/// This is a <b>pure rule layer, not yet wired</b>. Hooking this into <c>Game</c> unit movement and the
/// <c>Pathfinder</c> (so a unit actually pays the reduced cost when stepping along a river) is a deferred
/// follow-up slice (see <c>docs/systems/rivers-tile-improvements.md</c> §5). Until then this helper is
/// unit-tested in isolation.
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
}
