namespace CrownAndColony.GameLogic.GameSession.Diplomacy;

/// <summary>
/// A treaty clause that incites war against a third power (FreeCol <c>common/model/InciteTradeItem.java</c>): "go to
/// war with the Spanish for me". On apply the clause's <see cref="TradeItem.Source"/> (the power that agrees to fight)
/// and the named <see cref="Victim"/> are set to <see cref="Stance.War"/>, and the victim's grudge against the
/// <see cref="TradeItem.Destination"/> (the power who asked for the favour) rises; the gold paid for the favour travels
/// as a separate <see cref="GoldTradeItem"/> clause in the same treaty.
/// </summary>
/// <remarks>
/// FreeCol's <c>InciteTradeItem</c> carries a victim distinct from both <c>source</c> and <c>destination</c> — exactly
/// the validity rule here. Faithful to FreeCol <c>ServerGame.csAcceptTrade</c>, settling it puts the <b>source</b>
/// (not the destination) at war with the victim — <c>source.csChangeStance(WAR, victim)</c>: in a normal offer the
/// recipient of the treaty agrees to make war as its half of the deal, so the <em>recipient</em> is the source of this
/// clause. The change is symmetric and carries the war stance-change tension modifier, plus the war-inciter spike
/// (FreeCol <c>TENSION_ADD_WAR_INCITER</c> = 250) on the victim's view of the destination (via <see cref="Game.Incite"/>).
/// Like every clause it draws no RNG (ADR-009). Valid only while all three are distinct colonial powers — natives stay
/// on the alarm system — so an incitement naming a native or either party is skipped by
/// <see cref="DiplomaticTrade.Apply"/>.
/// </remarks>
public sealed class InciteTradeItem : TradeItem
{
    /// <summary>Creates a clause in which <paramref name="source"/> (the power that will make war) goes to war against <paramref name="victim"/> for <paramref name="destination"/> (the power who asked) — their <see cref="Player.PlayerId"/>s.</summary>
    public InciteTradeItem(int source, int destination, int victim)
        : base(source, destination)
    {
        Victim = victim;
    }

    /// <summary>The third power the source is incited to fight (its <see cref="Player.PlayerId"/>) — FreeCol <c>InciteTradeItem.getVictim</c>.</summary>
    public int Victim { get; }

    /// <summary>Valid when the victim is a colonial power distinct from both parties, and both parties are distinct colonial powers (FreeCol <c>InciteTradeItem.isValid</c>).</summary>
    public override bool IsValid(Game game) => game.CanIncite(Source, Destination, Victim);

    /// <summary>Sets the source and the victim to war (with tension), and raises the victim's grudge against the destination (via <see cref="Game.Incite"/>).</summary>
    public override void Apply(Game game) => game.Incite(Source, Destination, Victim);
}
