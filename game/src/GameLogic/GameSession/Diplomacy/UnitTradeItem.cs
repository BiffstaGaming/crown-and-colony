namespace CrownAndColony.GameLogic.GameSession.Diplomacy;

/// <summary>
/// A treaty clause that hands a single unit from one colonial player to another (FreeCol
/// <c>common/model/UnitTradeItem.java</c>): "you take my colonist/soldier/ship". On apply the unit's owner becomes
/// <see cref="TradeItem.Destination"/>; the unit stays where it is (a pure change of allegiance).
/// </summary>
/// <remarks>
/// FreeCol's <c>UnitTradeItem.isValid</c> is "the unit is the source's <em>and</em> its type is available to the
/// destination"; colonial players here share the one classic ruleset, so the type-availability half always holds and
/// this clause models the ownership half — the unit must still exist and be a (non-native) unit owned by
/// <see cref="TradeItem.Source"/>. Applying it is a deterministic reparent (no RNG, ADR-009); a unit lost or already
/// reassigned since the offer invalidates the clause, which <see cref="DiplomaticTrade.Apply"/> then skips.
/// </remarks>
public sealed class UnitTradeItem : TradeItem
{
    /// <summary>Creates a clause handing the unit <paramref name="unitId"/> (owned by <paramref name="source"/>) to <paramref name="destination"/> (their <see cref="Player.PlayerId"/>s).</summary>
    public UnitTradeItem(int source, int destination, int unitId)
        : base(source, destination)
    {
        UnitId = unitId;
    }

    /// <summary>The unit changing hands (its <c>Unit.Id</c>) — FreeCol <c>UnitTradeItem.getUnit</c>.</summary>
    public int UnitId { get; }

    /// <summary>Valid when both parties are distinct colonial powers and the unit still exists and is a non-native unit owned by <see cref="TradeItem.Source"/> (FreeCol <c>UnitTradeItem.isValid</c>).</summary>
    public override bool IsValid(Game game) => game.CanTransferUnit(Source, Destination, UnitId);

    /// <summary>Reassigns the unit's owner to the destination player (via <see cref="Game.TransferUnit"/>; position unchanged).</summary>
    public override void Apply(Game game) => game.TransferUnit(Source, Destination, UnitId);
}
