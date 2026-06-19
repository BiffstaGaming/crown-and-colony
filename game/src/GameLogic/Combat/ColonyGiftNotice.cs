using CrownAndColony.GameLogic.World;

namespace CrownAndColony.GameLogic.Combat;

/// <summary>
/// A record of a friendly native tribe bringing a <b>gift</b> of goods to a human colony during the AI phase of
/// <see cref="GameSession.Game.EndTurn"/> — the goodwill counterpart of <see cref="ColonyRaidNotice"/> (FreeCol
/// <c>IndianBringGiftMission</c>). A brave from a settlement that is <b>Happy</b> toward the human, standing beside
/// one of its colonies, leaves a parcel of goods in its warehouse. Like the raid notice, the delivery has no return
/// value the human UI can read, so the game collects these notices and the presentation surfaces them after the turn.
/// </summary>
/// <remarks>
/// Transient per-turn UI scratch: cleared at the start of every <c>EndTurn</c>, never saved or restored (no
/// save-format impact). Fields are raw ids/names/positions — formatting the English message is the presentation
/// layer's job (ADR-006).
/// </remarks>
/// <param name="GiverNationId">The gifting native nation type id (e.g. <c>model.nationType.apache</c>).</param>
/// <param name="ColonyName">The colony that received the gift.</param>
/// <param name="GoodsId">The goods type delivered (e.g. <c>model.goods.food</c>).</param>
/// <param name="Amount">How much of that good was delivered.</param>
/// <param name="Position">The tile the colony stands on.</param>
public readonly record struct ColonyGiftNotice(
    string GiverNationId,
    string ColonyName,
    string GoodsId,
    int Amount,
    Position Position);
