using CrownAndColony.GameLogic.World;

namespace CrownAndColony.GameLogic.Colonies;

/// <summary>
/// A record of a storable good a human colony produced <b>past its warehouse capacity</b> during the player's turn
/// in <see cref="GameSession.Game.EndTurn"/>, so the surplus was wasted (FreeCol <c>ServerColony.csNewTurn</c>'s
/// warehouse-overflow warning, <c>model.building.warehouseSoonFull</c>/<c>warehouseOverflow</c>). The spill happens
/// inside the colony's economy tick with no return value the human UI can read, so the game collects these notices
/// and the presentation layer surfaces them after the turn resolves (the sibling of <see cref="DisasterNotice"/>).
/// </summary>
/// <remarks>
/// Transient per-turn UI scratch: cleared at the start of every <c>EndTurn</c>, never saved or restored (no
/// save-format impact). Only human-owned colonies' overflows are recorded — foreign powers' warehouses still spill,
/// they just produce no player-facing notice. Fields are raw ids/names/amounts — formatting the English message is
/// the presentation layer's job (ADR-006).
/// </remarks>
/// <param name="ColonyName">The overflowing colony's name.</param>
/// <param name="Position">The tile the colony stands on.</param>
/// <param name="GoodsId">The goods type that overflowed (e.g. <c>model.goods.sugar</c>).</param>
/// <param name="Wasted">How much of that good was discarded over the warehouse cap this turn.</param>
public readonly record struct WarehouseOverflowNotice(
    string ColonyName,
    Position Position,
    string GoodsId,
    int Wasted);
