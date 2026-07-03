using CrownAndColony.GameLogic.World;

namespace CrownAndColony.GameLogic.Colonies;

/// <summary>Which warehouse water-mark a stored good crossed this turn (FreeCol's <c>warehouseFull</c>/<c>warehouseEmpty</c> messages).</summary>
public enum WarehouseLevelKind
{
    /// <summary>The good rose past the <b>high</b> water-mark — the warehouse is filling and will soon overflow (FreeCol <c>model.colony.warehouseFull</c>).</summary>
    Filling,

    /// <summary>The good fell below the <b>low</b> water-mark — the colony is running short of it (FreeCol <c>model.colony.warehouseEmpty</c>).</summary>
    RunningLow,
}

/// <summary>
/// A record of a human colony's storable good <b>crossing a warehouse water-mark</b> during the player's turn in
/// <see cref="GameSession.Game.EndTurn"/> (FreeCol <c>ServerColony.csNewTurn</c>'s <c>warehouseFull</c>/<c>warehouseEmpty</c>
/// warnings). It is <b>edge-triggered</b> — recorded only on the turn the good crosses the mark (comparing the amount at
/// turn start to the amount after production), so a good that sits high/low does not re-warn every turn. The threshold
/// is a percentage of the colony's warehouse capacity (<see cref="GameSession.Game.WarehouseHighWaterMarkPercent"/> /
/// <see cref="GameSession.Game.WarehouseLowWaterMarkPercent"/>, FreeCol's <c>ExportData</c> defaults 90/10). The sibling
/// of <see cref="WarehouseOverflowNotice"/> (which fires on actual over-capacity spoilage).
/// </summary>
/// <remarks>
/// Transient per-turn UI scratch: cleared at the start of every <c>EndTurn</c>, never saved or restored (no
/// save-format impact). Only human-owned colonies record notices. Fields are raw ids/names/amounts — formatting the
/// English message is the presentation layer's job (ADR-006).
/// </remarks>
/// <param name="ColonyName">The colony whose warehouse crossed a mark.</param>
/// <param name="Position">The tile the colony stands on.</param>
/// <param name="GoodsId">The goods type that crossed a mark (e.g. <c>model.goods.sugar</c>).</param>
/// <param name="Kind">Whether the good rose past the high mark (<see cref="WarehouseLevelKind.Filling"/>) or fell below the low mark (<see cref="WarehouseLevelKind.RunningLow"/>).</param>
/// <param name="Amount">How much of the good the colony now holds.</param>
/// <param name="Level">The water-mark amount that was crossed (an absolute stock level, not a percentage).</param>
public readonly record struct WarehouseLevelNotice(
    string ColonyName,
    Position Position,
    string GoodsId,
    WarehouseLevelKind Kind,
    int Amount,
    int Level);
