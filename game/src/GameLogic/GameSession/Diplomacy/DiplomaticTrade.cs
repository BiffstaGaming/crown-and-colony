namespace CrownAndColony.GameLogic.GameSession.Diplomacy;

/// <summary>
/// A proposed treaty between two colonial players (FreeCol <c>common/model/DiplomaticTrade.java</c>): who proposed
/// it, who it is offered to, and the list of <see cref="TradeItem"/> clauses (gold, goods, stance changes) that all
/// take effect together when it is accepted. A pure in-memory value object built by the rules/UI and carried out by
/// <see cref="Apply"/>.
/// </summary>
/// <remarks>
/// This backend slice models the treaty and its <see cref="Apply"/> path only — it is <b>not</b> persisted (held in
/// memory; no save change) and the AI does <b>not</b> yet evaluate or counter offers. Applying a trade is purely a
/// sequence of deterministic transfers via the game's existing mutators, so no RNG is drawn (ADR-009). The proposer
/// always appears as one party and the recipient as the other; individual clauses carry their own
/// <see cref="TradeItem.Source"/>/<see cref="TradeItem.Destination"/> so a single treaty can move value both ways
/// (e.g. our gold for their goods).
/// </remarks>
public sealed class DiplomaticTrade
{
    private readonly List<TradeItem> _items = [];

    /// <summary>Creates an empty treaty proposed by <paramref name="proposerId"/> to <paramref name="recipientId"/> (their <see cref="Player.PlayerId"/>s). Add clauses with <see cref="Add"/>.</summary>
    public DiplomaticTrade(int proposerId, int recipientId)
    {
        ProposerId = proposerId;
        RecipientId = recipientId;
    }

    /// <summary>The player offering the treaty (FreeCol <c>DiplomaticTrade.getSender</c>).</summary>
    public int ProposerId { get; }

    /// <summary>The player the treaty is offered to (FreeCol <c>DiplomaticTrade.getRecipient</c>).</summary>
    public int RecipientId { get; }

    /// <summary>The clauses of this treaty, in the order they were added (FreeCol <c>DiplomaticTrade.getItems</c>).</summary>
    public IReadOnlyList<TradeItem> Items => _items;

    /// <summary>Appends a clause to the treaty; returns the same trade so calls can be chained.</summary>
    public DiplomaticTrade Add(TradeItem item)
    {
        _items.Add(item);
        return this;
    }

    /// <summary>
    /// Whether every clause can currently be carried out (FreeCol <c>DiplomaticTrade.isValid</c>): the treaty must
    /// hold at least one clause and each must pass <see cref="TradeItem.IsValid"/>. An empty treaty is not valid.
    /// </summary>
    public bool IsValid(Game game) => _items.Count > 0 && _items.All(i => i.IsValid(game));

    /// <summary>
    /// Settles the treaty (FreeCol <c>InGameController.csAcceptTrade</c>): applies each currently-valid clause in
    /// order. A clause that has become invalid (e.g. a colony was lost since the offer) is skipped rather than
    /// throwing, so the rest of the treaty still settles. Deterministic; draws no RNG (ADR-009).
    /// </summary>
    public void Apply(Game game)
    {
        foreach (TradeItem item in _items)
        {
            if (item.IsValid(game))
            {
                item.Apply(game);
            }
        }
    }
}
