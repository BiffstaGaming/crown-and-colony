using CrownAndColony.GameLogic.World;

namespace CrownAndColony.GameLogic.Colonies;

/// <summary>
/// A record of a natural disaster that struck a human colony during the player's turn in
/// <see cref="GameSession.Game.EndTurn"/> (FreeCol <c>ServerPlayer.csNaturalDisasters</c> → <c>csApplyDisaster</c>).
/// A disaster is rolled per turn only when the ruleset's <c>model.option.naturalDisasters</c> percentage is above 0
/// (classic default 0 → none), so this list is always empty in the classic default game. The roll has no return
/// value the human UI can read, so the game collects these notices and the presentation layer surfaces them after
/// the turn resolves (the sibling of <see cref="Combat.ColonyRaidNotice"/>).
/// </summary>
/// <remarks>
/// Transient per-turn UI scratch: cleared at the start of every <c>EndTurn</c>, never saved or restored (no
/// save-format impact). Fields are raw ids/names — formatting the English message is the presentation layer's job
/// (ADR-006).
/// </remarks>
/// <param name="DisasterId">The disaster type id that struck (e.g. <c>model.disaster.flood</c>).</param>
/// <param name="ColonyName">The struck colony's name.</param>
/// <param name="Position">The tile the colony stands on.</param>
/// <param name="GoldLost">Gold drained from the owner by a loss-of-money effect (0 if none).</param>
/// <param name="GoodsLostId">The goods type a loss-of-goods effect destroyed (e.g. <c>model.goods.tobacco</c>), or <c>null</c> if none.</param>
/// <param name="GoodsLost">How much of <see cref="GoodsLostId"/> was destroyed (0 if none).</param>
/// <param name="ProductionPenaltyApplied">Whether a production-penalty effect fired (a −50% goods penalty this turn).</param>
/// <param name="BuildingLostId">The building a loss-of-building effect razed or downgraded (a building id, e.g. <c>model.building.docks</c>), or <c>null</c> if none.</param>
/// <param name="ColonistLost">Whether a loss-of-unit effect removed a colonist from the colony (the colony may have been destroyed if it was its last).</param>
/// <param name="ShipDamaged">Whether a damaged-unit effect damaged or sank a ship docked at the colony.</param>
public readonly record struct DisasterNotice(
    string DisasterId,
    string ColonyName,
    Position Position,
    int GoldLost,
    string? GoodsLostId,
    int GoodsLost,
    bool ProductionPenaltyApplied,
    string? BuildingLostId = null,
    bool ColonistLost = false,
    bool ShipDamaged = false);
