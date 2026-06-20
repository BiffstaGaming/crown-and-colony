namespace CrownAndColony.GameLogic.GameSession.Diplomacy;

/// <summary>
/// A treaty clause that hands a whole colony from one colonial player to another (FreeCol
/// <c>common/model/ColonyTradeItem.java</c>): "you take my colony". On apply the colony — its people, buildings and
/// stores — changes owner to <see cref="TradeItem.Destination"/>, intact (a negotiated handover, not a sacking).
/// </summary>
/// <remarks>
/// The transfer reuses the same capture/ownership path as a wartime seizure (<see cref="Game.TransferColony"/> →
/// <c>CaptureColony</c>), so the former owner's ships caught in the colony's port are resolved the same way — but
/// <b>no plunder is rolled</b>, so the apply path draws no RNG (ADR-009). The clause is valid only while the named
/// colony still exists and is owned by <see cref="TradeItem.Source"/>; a colony lost since the offer invalidates it,
/// which <see cref="DiplomaticTrade.Apply"/> then skips.
/// </remarks>
public sealed class ColonyTradeItem : TradeItem
{
    /// <summary>Creates a clause handing the colony <paramref name="colonyId"/> (owned by <paramref name="source"/>) to <paramref name="destination"/> (their <see cref="Player.PlayerId"/>s).</summary>
    public ColonyTradeItem(int source, int destination, int colonyId)
        : base(source, destination)
    {
        ColonyId = colonyId;
    }

    /// <summary>The colony changing hands (its <c>Colony.Id</c>) — FreeCol <c>ColonyTradeItem.getColony</c>.</summary>
    public int ColonyId { get; }

    /// <summary>Valid when both parties are distinct colonial powers and the colony still exists and is owned by <see cref="TradeItem.Source"/> (FreeCol <c>ColonyTradeItem.isValid</c>).</summary>
    public override bool IsValid(Game game) => game.CanTransferColony(Source, Destination, ColonyId);

    /// <summary>Hands the colony over to the destination owner (via <see cref="Game.TransferColony"/>, intact — no plunder).</summary>
    public override void Apply(Game game) => game.TransferColony(Source, Destination, ColonyId);
}
