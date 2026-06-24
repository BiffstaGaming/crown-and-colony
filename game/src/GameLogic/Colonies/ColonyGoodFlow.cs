namespace CrownAndColony.GameLogic.Colonies;

/// <summary>
/// One good's per-turn flow in a colony's production summary (<see cref="GameSession.Game.ColonyProductionSummary"/>):
/// how much the colony <see cref="Produced"/> this turn, how much it <see cref="Consumed"/>, and the resulting
/// <see cref="Net"/>. Both <see cref="Produced"/> and <see cref="Consumed"/> are non-negative amounts (consumption is a
/// positive figure, not a signed delta); <c>Net = Produced − Consumed</c> and may be negative when the colony eats /
/// converts more of a good than it makes. A read-only presentation record — the colony screen renders it (ADR-006).
/// </summary>
/// <param name="Produced">Units of the good produced this turn (tile yields, colony-centre yield, building outputs).</param>
/// <param name="Consumed">Units of the good consumed this turn (food eaten, building inputs burned).</param>
public readonly record struct ColonyGoodFlow(int Produced, int Consumed)
{
    /// <summary>The net change to the warehouse this turn for the good: <see cref="Produced"/> − <see cref="Consumed"/> (may be negative).</summary>
    public int Net => Produced - Consumed;
}
