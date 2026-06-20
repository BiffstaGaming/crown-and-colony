namespace CrownAndColony.GameLogic.GameSession.Diplomacy;

/// <summary>
/// A treaty clause that changes the diplomatic relationship between the two parties (FreeCol
/// <c>common/model/StanceTradeItem.java</c>): on apply it sets their mutual <see cref="GameSession.Stance"/> —
/// in practice <see cref="GameSession.Stance.Peace"/>, <see cref="GameSession.Stance.CeaseFire"/>, or
/// <see cref="GameSession.Stance.Alliance"/> (end a war, agree a truce, or ally).
/// </summary>
/// <remarks>
/// The stance is set symmetrically via <see cref="Game.SetStance"/>, so it applies between the clause's two players
/// the same way contact/attack do. Like every clause this draws no RNG (ADR-009) and carries no tension delta — the
/// FreeCol alliance/peace tension modifiers are a later slice. A stance change between non-colonial players is a
/// no-op (the existing <c>SetStance</c> guard), which makes the clause invalid for such a pair.
/// </remarks>
public sealed class StanceTradeItem : TradeItem
{
    /// <summary>Creates a clause setting the mutual stance between <paramref name="source"/> and <paramref name="destination"/> (their <see cref="Player.PlayerId"/>s) to <paramref name="stance"/>.</summary>
    public StanceTradeItem(int source, int destination, Stance stance)
        : base(source, destination)
    {
        Stance = stance;
    }

    /// <summary>The stance the treaty sets between the two parties (FreeCol <c>StanceTradeItem.getStance</c>).</summary>
    public Stance Stance { get; }

    /// <summary>Valid when the two parties are distinct colonial powers (FreeCol <c>StanceTradeItem.isValid</c> — the only pairs whose stance is tracked); <see cref="Game.SetStance"/> is otherwise a no-op.</summary>
    public override bool IsValid(Game game) => game.CanSetStance(Source, Destination);

    /// <summary>Sets the mutual stance between the two parties (symmetric, via <see cref="Game.SetStance"/>).</summary>
    public override void Apply(Game game) => game.SetStance(Source, Destination, Stance);
}
