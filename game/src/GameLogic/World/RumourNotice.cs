namespace CrownAndColony.GameLogic.World;

/// <summary>
/// A record of a Lost City Rumour the human just explored and that resolved <b>immediately</b> (every outcome
/// except strange mounds, which pause for an investigate/decline prompt — see
/// <see cref="GameSession.Game.PendingMounds"/>). The reward applies inside
/// <see cref="GameSession.Game.ExploreRumour"/> with no return value the move handler reads, so the game collects
/// these notices and the presentation layer surfaces them after the move resolves — the rumour-exploration sibling
/// of <see cref="Combat.CombatNotice"/> / <see cref="Combat.ColonyRaidNotice"/>.
/// </summary>
/// <remarks>
/// Transient per-turn UI scratch: cleared at the start of every <c>EndTurn</c> and never saved or restored (no
/// save-format impact). The internal <c>LostCityRumourType</c> enum is kept out of the presentation assembly
/// (ADR-006), so — exactly as the mounds prompt does (<see cref="GameSession.Game.ResolvePendingMounds"/>) — the
/// game pre-formats the one-line player-facing description here; presentation only decides where to show it.
/// </remarks>
/// <param name="Message">The one-line player-facing description of what was found (e.g. "Tribal chiefs share their treasure with you!").</param>
/// <param name="Position">The tile the rumour stood on.</param>
public readonly record struct RumourNotice(string Message, Position Position);
