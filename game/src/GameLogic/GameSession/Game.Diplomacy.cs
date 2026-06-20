using CrownAndColony.GameLogic.GameSession.Diplomacy;

namespace CrownAndColony.GameLogic.GameSession;

/// <summary>
/// Diplomatic-trade backend (P6, FreeCol <c>InGameController.csAcceptTrade</c>): the thin <see cref="Game"/> seam
/// that settles a proposed <see cref="DiplomaticTrade"/> between two colonial players. The treaty model itself
/// lives in <c>GameSession/Diplomacy/</c>; this partial only wires its <see cref="DiplomaticTrade.Apply"/> to the
/// game's existing mutators (gold/goods transfer, <see cref="SetStance"/>).
/// </summary>
/// <remarks>
/// In-memory only this slice — the trade is not persisted (no save change) and the AI does not yet evaluate or
/// counter offers (that lives in the foreign-power turn, a separate lane). Settling a trade draws no RNG (ADR-009):
/// every clause is a deterministic transfer.
/// </remarks>
public sealed partial class Game
{
    /// <summary>
    /// Settles an accepted treaty (FreeCol <c>csAcceptTrade</c>): applies each of its currently-valid clauses. The
    /// foundation slice has no clause kinds yet, so this is inert until gold/goods/stance items land — it is the
    /// stable seam the rules/UI call once a <see cref="DiplomaticTrade"/> is agreed.
    /// </summary>
    public void SettleTrade(DiplomaticTrade trade) => trade.Apply(this);
}
