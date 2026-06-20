namespace CrownAndColony.GameLogic.GameSession.Diplomacy;

/// <summary>
/// A treaty clause transferring goods from one player's colony warehouse to another's (FreeCol
/// <c>common/model/GoodsTradeItem.java</c>): "you give me N of goods X". On apply the goods leave
/// <see cref="SourceColonyId"/> and are added to <see cref="DestinationColonyId"/>.
/// </summary>
/// <remarks>
/// FreeCol's <c>GoodsTradeItem</c> carries a <c>Goods</c> bound to a <em>source settlement</em>; colonial players
/// here hold goods only in colonies (no player-level pool), so this clause names the specific source and destination
/// colonies. <see cref="TradeItem.Source"/>/<see cref="TradeItem.Destination"/> are the colonies' owning players —
/// the clause is valid only while each colony is still owned by its party (a colony lost since the offer invalidates
/// the clause, which <see cref="DiplomaticTrade.Apply"/> then skips).
/// </remarks>
public sealed class GoodsTradeItem : TradeItem
{
    /// <summary>
    /// Creates a clause moving <paramref name="amount"/> of <paramref name="goodsId"/> from the colony
    /// <paramref name="sourceColonyId"/> (owned by <paramref name="source"/>) to <paramref name="destinationColonyId"/>
    /// (owned by <paramref name="destination"/>).
    /// </summary>
    public GoodsTradeItem(
        int source, int destination,
        int sourceColonyId, int destinationColonyId,
        string goodsId, int amount)
        : base(source, destination)
    {
        SourceColonyId = sourceColonyId;
        DestinationColonyId = destinationColonyId;
        GoodsId = goodsId;
        Amount = amount;
    }

    /// <summary>The colony giving up the goods (its <c>Colony.Id</c>), owned by <see cref="TradeItem.Source"/>.</summary>
    public int SourceColonyId { get; }

    /// <summary>The colony receiving the goods (its <c>Colony.Id</c>), owned by <see cref="TradeItem.Destination"/>.</summary>
    public int DestinationColonyId { get; }

    /// <summary>The ruleset goods-type id being traded (FreeCol <c>GoodsTradeItem.getGoods().getType()</c>).</summary>
    public string GoodsId { get; }

    /// <summary>The quantity moved (FreeCol <c>Goods.getAmount</c>).</summary>
    public int Amount { get; }

    /// <summary>Valid when the amount is positive, the goods id is real, both parties are distinct colonial powers each still owning their named colony, and the source colony holds at least <see cref="Amount"/> (FreeCol <c>GoodsTradeItem.isValid</c>).</summary>
    public override bool IsValid(Game game) =>
        game.CanTransferColonyGoods(Source, Destination, SourceColonyId, DestinationColonyId, GoodsId, Amount);

    /// <summary>Moves the goods from the source colony's warehouse to the destination colony's.</summary>
    public override void Apply(Game game) =>
        game.TransferColonyGoods(SourceColonyId, DestinationColonyId, GoodsId, Amount);
}
