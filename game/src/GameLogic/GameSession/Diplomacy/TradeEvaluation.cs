namespace CrownAndColony.GameLogic.GameSession.Diplomacy;

/// <summary>
/// The result of a colonial power weighing up a proposed <see cref="DiplomaticTrade"/> (FreeCol
/// <c>EuropeanAIPlayer.acceptDiplomaticTrade</c>): whether it would accept, and the net value it placed on the deal.
/// </summary>
/// <remarks>
/// A pure scoring snapshot — computing it changes nothing in the game and draws no RNG (ADR-009). This is the
/// decision an AI <em>would</em> make; wiring it into the foreign-power turn (so a power actually answers or proposes
/// treaties) is a later slice that pairs with the negotiation UI. <see cref="Accept"/> is true exactly when no clause
/// is unacceptable to the evaluator and the total <see cref="Value"/> is ≥ 0 (the evaluator never pays out on balance).
/// </remarks>
public readonly record struct TradeEvaluation(bool Accept, int Value, int UnacceptableItems);
