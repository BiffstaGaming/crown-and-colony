namespace CrownAndColony.GameLogic.GameSession.Diplomacy;

/// <summary>
/// The situation a <see cref="DiplomaticTrade"/> arises in (FreeCol <c>common/model/DiplomaticTrade.TradeContext</c>):
/// it changes how an offer is weighed. A peace clause offered at <see cref="Contact"/> is <b>free</b> (the two powers
/// are simply formalising "we have just met" — never a cost, never refused, exactly as FreeCol's <c>CONTACT</c> branch
/// of <c>EuropeanAIPlayer.acceptDiplomaticTrade</c> forces such a clause to score 0); a peace/alliance offered at
/// <see cref="Diplomatic"/> is weighed by the usual military strength-ratio rule.
/// </summary>
/// <remarks>
/// Every existing call site defaults to <see cref="Diplomatic"/> — the context under which the established
/// strength-gated evaluation applies — so the default game is byte-identical. The context is an in-memory property of
/// the (un-persisted) trade; it adds no save bytes (ADR — no save bump).
/// </remarks>
public enum TradeContext
{
    /// <summary>First contact between two European powers — a peace/uncontacted stance clause is free (FreeCol <c>CONTACT</c>).</summary>
    Contact,

    /// <summary>A negotiated treaty (a scout/diplomat at the table) — clauses are weighed by the normal rules (FreeCol <c>DIPLOMATIC</c>). The default for an unspecified trade.</summary>
    Diplomatic,

    /// <summary>A trading-carrier deal (FreeCol <c>TRADE</c>). Modelled for faithfulness; weighed like <see cref="Diplomatic"/> here.</summary>
    Trade,

    /// <summary>An offensive unit demanding tribute (FreeCol <c>TRIBUTE</c>). Modelled for faithfulness; weighed like <see cref="Diplomatic"/> here.</summary>
    Tribute,
}
