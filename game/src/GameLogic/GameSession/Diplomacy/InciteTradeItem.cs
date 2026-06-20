namespace CrownAndColony.GameLogic.GameSession.Diplomacy;

/// <summary>
/// A treaty clause that incites war against a third power (FreeCol <c>common/model/InciteTradeItem.java</c>): "go to
/// war with the Spanish for me". On apply the clause's <see cref="TradeItem.Destination"/> (the power being incited)
/// and the named <see cref="Victim"/> are set to <see cref="Stance.War"/>; the gold the inciter pays for the favour
/// travels as a separate <see cref="GoldTradeItem"/> clause in the same treaty.
/// </summary>
/// <remarks>
/// FreeCol's <c>InciteTradeItem</c> carries a victim distinct from both <c>source</c> (the inciter) and
/// <c>destination</c> (the power being paid to fight) — exactly the validity rule here. Settling it changes only the
/// destination↔victim stance (symmetric, via <see cref="Game.Incite"/>); like every clause it draws no RNG (ADR-009).
/// The war-inciter tension spike (FreeCol <c>TENSION_ADD_WAR_INCITER</c> = 250) lands with the stance-change
/// tension-modifier table (86d3c9u3z), not here. Valid only while all three are distinct colonial powers — natives
/// stay on the alarm system — so an incitement naming a native or either party is skipped by
/// <see cref="DiplomaticTrade.Apply"/>.
/// </remarks>
public sealed class InciteTradeItem : TradeItem
{
    /// <summary>Creates a clause inciting <paramref name="destination"/> to war against <paramref name="victim"/>, offered by <paramref name="source"/> (their <see cref="Player.PlayerId"/>s).</summary>
    public InciteTradeItem(int source, int destination, int victim)
        : base(source, destination)
    {
        Victim = victim;
    }

    /// <summary>The third power the destination is incited to fight (its <see cref="Player.PlayerId"/>) — FreeCol <c>InciteTradeItem.getVictim</c>.</summary>
    public int Victim { get; }

    /// <summary>Valid when the victim is a colonial power distinct from both parties, and both parties are distinct colonial powers (FreeCol <c>InciteTradeItem.isValid</c>).</summary>
    public override bool IsValid(Game game) => game.CanIncite(Source, Destination, Victim);

    /// <summary>Sets the destination and the victim to war (symmetric, via <see cref="Game.Incite"/>).</summary>
    public override void Apply(Game game) => game.Incite(Source, Destination, Victim);
}
