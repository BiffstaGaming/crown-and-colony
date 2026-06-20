namespace CrownAndColony.GameLogic.GameSession.Diplomacy;

/// <summary>
/// One clause of a proposed <see cref="DiplomaticTrade"/> between two colonial players (FreeCol
/// <c>common/model/TradeItem.java</c>): a single thing that changes hands — gold, goods, or a stance shift —
/// when the treaty is settled. A pure value object; <see cref="Apply"/> performs the clause's transfer using the
/// game's existing mutators.
/// </summary>
/// <remarks>
/// FreeCol's <c>TradeItem</c> carries a <c>source</c> and <c>destination</c> player; this faithful subset stores
/// the same as <see cref="Source"/>/<see cref="Destination"/> so each clause is self-describing and direction is
/// not lost. The foundation slice ships no concrete items — gold and goods (<c>86d3c9u94</c>) and the stance item
/// (<c>86d3c9u3z</c>) land next. No RNG is ever drawn here (ADR-009): every clause is a deterministic transfer.
/// </remarks>
public abstract class TradeItem
{
    /// <summary>Creates a clause moving value from <paramref name="source"/> to <paramref name="destination"/> (their <see cref="Player.PlayerId"/>s).</summary>
    protected TradeItem(int source, int destination)
    {
        Source = source;
        Destination = destination;
    }

    /// <summary>The player giving up this clause's value (its <see cref="Player.PlayerId"/>) — FreeCol <c>TradeItem.getSource</c>.</summary>
    public int Source { get; }

    /// <summary>The player receiving this clause's value (its <see cref="Player.PlayerId"/>) — FreeCol <c>TradeItem.getDestination</c>.</summary>
    public int Destination { get; }

    /// <summary>
    /// Whether the clause can be carried out in the current game state (FreeCol <c>TradeItem.isValid</c>): both
    /// players exist and the clause-specific preconditions hold. A clause that is not valid is silently skipped by
    /// <see cref="DiplomaticTrade.Apply"/> rather than throwing — a treaty settles only the clauses it still can.
    /// </summary>
    public abstract bool IsValid(Game game);

    /// <summary>Carries out this clause's transfer on <paramref name="game"/> (FreeCol <c>InGameController.csAcceptTrade</c> per item). Caller has already checked <see cref="IsValid"/>.</summary>
    public abstract void Apply(Game game);
}
