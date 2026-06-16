using CrownAndColony.GameLogic.World;

namespace CrownAndColony.GameLogic.Combat;

/// <summary>
/// A record of a human colony pillaged by a native brave during the AI phase of
/// <see cref="GameSession.Game.EndTurn"/> (native colony pillage) — the goods-raid sibling of
/// <see cref="CombatNotice"/> and <see cref="ColonyLossNotice"/>. A hostile brave beside an undefended,
/// pillageable human colony storms it and carries off a slice of one goods stack (FreeCol
/// <c>ServerPlayer.csPillageColony</c>); the colony is <em>not</em> captured or destroyed (natives cannot take
/// colonies). The raid has no return value the human UI can read, so the game collects these notices and the
/// presentation layer surfaces them after the turn resolves.
/// </summary>
/// <remarks>
/// This is transient per-turn UI scratch: it is cleared at the start of every <c>EndTurn</c> and is never
/// saved or restored (no save-format impact). Fields are raw ids/names/positions — formatting the English
/// message is the presentation layer's job (ADR-006).
/// </remarks>
/// <param name="AttackerNationId">The raiding native nation type id (e.g. <c>model.nationType.apache</c>).</param>
/// <param name="ColonyName">The pillaged colony's name.</param>
/// <param name="GoodsId">The goods type carried off (e.g. <c>model.goods.tobacco</c>), or <c>null</c> when the raid stole <b>gold</b>.</param>
/// <param name="Amount">How much of that goods (or gold) was stolen.</param>
/// <param name="Position">The tile the colony stands on.</param>
public readonly record struct ColonyRaidNotice(
    string AttackerNationId,
    string ColonyName,
    string? GoodsId,
    int Amount,
    Position Position);
