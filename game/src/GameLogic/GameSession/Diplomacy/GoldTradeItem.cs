namespace CrownAndColony.GameLogic.GameSession.Diplomacy;

/// <summary>
/// A treaty clause transferring gold from one colonial player's treasury to another's (FreeCol
/// <c>common/model/GoldTradeItem.java</c>): "I pay you N gold". On apply, N gold leaves <see cref="TradeItem.Source"/>
/// and lands in <see cref="TradeItem.Destination"/>.
/// </summary>
public sealed class GoldTradeItem : TradeItem
{
    /// <summary>Creates a clause paying <paramref name="amount"/> gold from <paramref name="source"/> to <paramref name="destination"/> (their <see cref="Player.PlayerId"/>s).</summary>
    public GoldTradeItem(int source, int destination, int amount)
        : base(source, destination)
    {
        Amount = amount;
    }

    /// <summary>The gold paid (FreeCol <c>GoldTradeItem.getGold</c>).</summary>
    public int Amount { get; }

    /// <summary>Valid when both players are distinct colonial powers, the amount is positive, and the source can afford it (FreeCol <c>GoldTradeItem.isValid</c>: <c>0 ≤ gold ≤ source.getGold()</c>).</summary>
    public override bool IsValid(Game game) => game.CanTransferGold(Source, Destination, Amount);

    /// <summary>Moves the gold from source to destination (FreeCol applies <c>modifyGold</c> on each side).</summary>
    public override void Apply(Game game) => game.TransferGold(Source, Destination, Amount);
}
