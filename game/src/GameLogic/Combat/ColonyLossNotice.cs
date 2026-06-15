using CrownAndColony.GameLogic.World;

namespace CrownAndColony.GameLogic.Combat;

/// <summary>
/// A record of a human colony captured by a foreign power during the AI phase of
/// <see cref="GameSession.Game.EndTurn"/> (1c-3f) — the colony-loss sibling of <see cref="CombatNotice"/>.
/// A foreign power at war whose armed land unit stands beside an undefended human colony takes it
/// (FreeCol <c>ServerPlayer.csCaptureColony</c>); that handover has no return value the human UI can read,
/// so the game collects these notices and the presentation layer surfaces them after the turn resolves.
/// </summary>
/// <remarks>
/// This is transient per-turn UI scratch: it is cleared at the start of every <c>EndTurn</c> and is never
/// saved or restored (no save-format impact). Fields are raw ids/names/positions — formatting the English
/// message is the presentation layer's job (ADR-006).
/// </remarks>
/// <param name="AttackerNationId">The capturing power's nation id (e.g. <c>model.nation.dutch</c>).</param>
/// <param name="ColonyName">The captured colony's name (captured before handover, while it was still the human's).</param>
/// <param name="Position">The tile the colony stands on.</param>
public readonly record struct ColonyLossNotice(
    string AttackerNationId,
    string ColonyName,
    Position Position);
