using CrownAndColony.GameLogic.World;

namespace CrownAndColony.GameLogic.Natives;

/// <summary>
/// A pending native tribute demand (FreeCol <c>IndianDemandMission</c>): a brave of <see cref="DemandingNationId"/>
/// has demanded tribute from the human colony <see cref="ColonyId"/> — either <see cref="Amount"/> of the goods
/// <see cref="GoodsId"/>, or, when <see cref="GoodsId"/> is null, <see cref="Amount"/> gold. The human accepts
/// (pays) or refuses; the choice is routed through
/// <see cref="GameSession.Game.AcceptPendingDemand"/> / <see cref="GameSession.Game.RefusePendingDemand"/>.
/// <para>
/// Transient (never saved): the demand is created during a native turn (inside
/// <see cref="GameSession.Game.EndTurn"/>) and answered at the start of the human's next turn, auto-refused if the
/// human ignores it — the same one-turn lifetime as the combat/colony notice channels, so it carries no
/// save-format change.
/// </para>
/// </summary>
/// <param name="DemandingNationId">The native nation making the demand (a brave's <c>OwnerNationId</c>).</param>
/// <param name="ColonyId">The human colony demanded of (re-looked-up by id when the demand is answered).</param>
/// <param name="ColonyName">The colony's name at demand time, for the UI message.</param>
/// <param name="GoodsId">The goods demanded, or null for a gold demand.</param>
/// <param name="Amount">The amount of goods (or gold) demanded.</param>
/// <param name="ColonyPosition">The colony's tile, for the UI message / map focus.</param>
public sealed record NativeDemand(
    string DemandingNationId,
    int ColonyId,
    string ColonyName,
    string? GoodsId,
    int Amount,
    Position ColonyPosition);
