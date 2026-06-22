namespace CrownAndColony.GameLogic.GameSession;

/// <summary>
/// A record of an <b>immediate</b> action the home-nation Monarch took on the human's behalf during the world-advance
/// band of <see cref="Game.EndTurn"/> — one that applies at once with no accept/reject choice, unlike the tax-rise and
/// mercenary <em>demands</em> that surface through <see cref="PendingMonarchDemand"/> / the Monarch dialog. These are
/// FreeCol's message-only / auto-applied monarch actions: lowering or waiving the tax, declaring war or peace with a
/// rival on the player's behalf, granting free military support, and growing the Royal Expeditionary Force. The action
/// resolves inside the monarch tick with no return value the human UI can read, so the game collects these notices and
/// the presentation layer surfaces them after the turn resolves (the King's-decree sibling of the combat/raid notices).
/// </summary>
/// <remarks>
/// Transient per-turn UI scratch: cleared at the start of every <c>EndTurn</c>, never saved or restored (no
/// save-format impact). Only the human's King produces these. The monarch tick is gated behind a grace period and only
/// fires past it, so this list is empty in the early game. Fields carry the action and the few parameters the message
/// needs (the affected rival's nation id, a tax rate, a unit count); formatting the English message is the
/// presentation layer's job (ADR-006).
/// </remarks>
/// <param name="Action">The immediate monarch action that fired (never a tax-rise or mercenary <em>demand</em>).</param>
/// <param name="RivalNationId">The rival's nation id for a war/peace decree (e.g. <c>model.nation.french</c>), or <c>null</c>.</param>
/// <param name="TaxRate">The new tax rate after a lower-tax decree (0 when not a tax decree).</param>
/// <param name="UnitCount">The number of units involved (REF growth or a free-support force; 0 when none).</param>
/// <param name="Gold">Gold the Crown granted alongside a war-support force (0 when none).</param>
public readonly record struct MonarchDecreeNotice(
    MonarchAction Action,
    string? RivalNationId = null,
    int TaxRate = 0,
    int UnitCount = 0,
    int Gold = 0);
