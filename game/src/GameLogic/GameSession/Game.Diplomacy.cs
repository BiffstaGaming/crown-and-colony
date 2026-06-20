using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession.Diplomacy;

namespace CrownAndColony.GameLogic.GameSession;

/// <summary>
/// Diplomatic-trade backend (P6, FreeCol <c>InGameController.csAcceptTrade</c>): the thin <see cref="Game"/> seam
/// that settles a proposed <see cref="DiplomaticTrade"/> between two colonial players. The treaty model itself
/// lives in <c>GameSession/Diplomacy/</c>; this partial only wires its <see cref="DiplomaticTrade.Apply"/> to the
/// game's existing mutators (gold/goods transfer, <see cref="SetStance"/>).
/// </summary>
/// <remarks>
/// In-memory only this slice — the trade is not persisted (no save change) and the AI does not yet evaluate or
/// counter offers (that lives in the foreign-power turn, a separate lane). Settling a trade draws no RNG (ADR-009):
/// every clause is a deterministic transfer.
/// </remarks>
public sealed partial class Game
{
    /// <summary>
    /// Settles an accepted treaty (FreeCol <c>csAcceptTrade</c>): applies each of its currently-valid clauses — gold,
    /// goods, and (once it lands) stance changes. The stable seam the rules/UI call once a <see cref="DiplomaticTrade"/>
    /// is agreed; clause application reaches the gold/goods/stance helpers below.
    /// </summary>
    public void SettleTrade(DiplomaticTrade trade) => trade.Apply(this);

    // ---- Gold clause (86d3c9u94) ----

    /// <summary>
    /// Whether a <see cref="Diplomacy.GoldTradeItem"/> could pay <paramref name="amount"/> gold from
    /// <paramref name="fromId"/> to <paramref name="toId"/> right now: both are distinct colonial powers, the amount
    /// is positive, and the payer can afford it (FreeCol <c>GoldTradeItem.isValid</c>).
    /// </summary>
    internal bool CanTransferGold(int fromId, int toId, int amount) =>
        amount > 0 && fromId != toId && IsColonialPlayer(fromId) && IsColonialPlayer(toId)
        && PlayerById(fromId)!.Gold >= amount;

    /// <summary>
    /// Moves <paramref name="amount"/> gold from <paramref name="fromId"/>'s treasury to <paramref name="toId"/>'s
    /// (the treaty gold-clause mutator). A no-op unless <see cref="CanTransferGold"/> holds, so an over-large or
    /// non-colonial transfer never drives a treasury negative.
    /// </summary>
    internal void TransferGold(int fromId, int toId, int amount)
    {
        if (!CanTransferGold(fromId, toId, amount))
        {
            return;
        }
        PlayerById(fromId)!.Gold -= amount;
        PlayerById(toId)!.Gold += amount;
    }

    // ---- Goods clause (86d3c9u94) ----
    // (Colony lookup reuses the existing private ColonyById in Game.Monarch.cs.)

    /// <summary>
    /// Whether a <see cref="Diplomacy.GoodsTradeItem"/> could move <paramref name="amount"/> of
    /// <paramref name="goodsId"/> from <paramref name="fromColonyId"/> to <paramref name="toColonyId"/>: the amount is
    /// positive, the goods id is a real ruleset type, both parties are distinct colonial powers each owning their
    /// named colony, and the source colony holds at least <paramref name="amount"/> (FreeCol <c>GoodsTradeItem.isValid</c>).
    /// </summary>
    internal bool CanTransferColonyGoods(
        int fromPlayerId, int toPlayerId,
        int fromColonyId, int toColonyId,
        string goodsId, int amount)
    {
        if (amount <= 0 || fromPlayerId == toPlayerId
            || !IsColonialPlayer(fromPlayerId) || !IsColonialPlayer(toPlayerId)
            || !Ruleset.GoodsTypes.Any(g => g.Id == goodsId))
        {
            return false;
        }
        Colony? from = ColonyById(fromColonyId);
        Colony? to = ColonyById(toColonyId);
        return from is not null && to is not null
            && from.OwnerId == fromPlayerId && to.OwnerId == toPlayerId
            && from.StoreOf(goodsId) >= amount;
    }

    /// <summary>
    /// Moves <paramref name="amount"/> of <paramref name="goodsId"/> from <paramref name="fromColonyId"/>'s warehouse
    /// to <paramref name="toColonyId"/>'s (the treaty goods-clause mutator). A no-op unless the colonies exist, so a
    /// stale clause never throws; the source is debited and the destination credited via <c>Colony.AddGoods</c>.
    /// </summary>
    internal void TransferColonyGoods(int fromColonyId, int toColonyId, string goodsId, int amount)
    {
        if (ColonyById(fromColonyId) is not { } from || ColonyById(toColonyId) is not { } to)
        {
            return;
        }
        from.AddGoods(goodsId, -amount);
        to.AddGoods(goodsId, amount);
    }

    // ---- Stance clause (86d3c9u3z) ----

    /// <summary>
    /// Whether a <see cref="Diplomacy.StanceTradeItem"/> could set a stance between <paramref name="a"/> and
    /// <paramref name="b"/>: they must be distinct colonial powers — the only pairs whose stance is tracked and the
    /// exact pairs for which <see cref="SetStance"/> is not a no-op (FreeCol <c>StanceTradeItem.isValid</c>).
    /// </summary>
    internal bool CanSetStance(int a, int b) => a != b && IsColonialPlayer(a) && IsColonialPlayer(b);
}
