namespace CrownAndColony.GameLogic.World.Improvements;

/// <summary>
/// Pure production-modifier rules for tile improvements (FreeCol applies improvement
/// <c>&lt;modifier&gt;</c> children to a tile's worked yield). Given a tile's river/road improvement and a
/// goods id, these helpers return the <i>delta</i> the improvement adds to that goods' yield — independent
/// of the engine and of any per-tile state.
/// </summary>
/// <remarks>
/// This is a <b>pure rule layer, not yet wired</b>. The eventual <c>Game.TileYield</c> integration — folding
/// this delta in alongside terrain base, resource bonuses and unit/father modifiers, in the correct FreeCol
/// modifier order — is a deferred follow-up slice (see <c>docs/systems/rivers-tile-improvements.md</c> §5).
/// Until then these functions stand alone and are unit-tested in isolation.
/// </remarks>
public static class ImprovementProduction
{
    /// <summary>
    /// The yield delta a single improvement adds to <paramref name="goodsId"/>: the sum of the values of its
    /// modifiers whose <see cref="ImprovementModifier.GoodsId"/> matches, applied to a running base of zero
    /// (so additive modifiers return their flat bonus). Goods the improvement does not affect return <c>0</c>.
    /// </summary>
    /// <param name="improvement">The improvement on the tile, or <c>null</c> if the tile has none.</param>
    /// <param name="goodsId">The goods being produced, e.g. <c>model.goods.furs</c>.</param>
    /// <returns>The additive yield delta (may be negative if a modifier subtracts; classic river never does).</returns>
    public static double YieldDelta(TileImprovementType? improvement, string goodsId)
    {
        if (improvement is null)
        {
            return 0;
        }

        double delta = 0;
        foreach (ImprovementModifier mod in improvement.Modifiers)
        {
            if (mod.GoodsId == goodsId)
            {
                // Apply to a zero base so an additive modifier yields its flat value and a percentage modifier
                // contributes nothing on its own (percentage scales an existing yield, folded in by the caller later).
                delta += mod.ApplyTo(0);
            }
        }

        return delta;
    }

    /// <summary>
    /// The combined yield delta several improvements on one tile add to <paramref name="goodsId"/> (their
    /// per-improvement deltas summed). Convenience for the future case of a tile carrying both a river and a
    /// road; <c>null</c> entries are ignored.
    /// </summary>
    /// <param name="improvements">The improvements on the tile.</param>
    /// <param name="goodsId">The goods being produced.</param>
    public static double YieldDelta(IEnumerable<TileImprovementType?> improvements, string goodsId)
    {
        double total = 0;
        foreach (TileImprovementType? imp in improvements)
        {
            total += YieldDelta(imp, goodsId);
        }

        return total;
    }
}
