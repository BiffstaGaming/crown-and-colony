using CrownAndColony.GameLogic.World;

namespace CrownAndColony.GameLogic.Combat;

/// <summary>
/// A record of one combat the human was involved in but did not initiate — a native brave (1b) or a foreign
/// power at war (1c-2) attacking a human unit during the AI phase of <see cref="GameSession.Game.EndTurn"/>.
/// The human-driven attack paths
/// report their outcome through the return value of <c>Attack</c>; an AI-initiated attack has no such return,
/// so the game collects these notices and the presentation layer surfaces them after the turn resolves.
/// </summary>
/// <remarks>
/// This is transient per-turn UI scratch: it is cleared at the start of every <c>EndTurn</c> and is never
/// saved or restored (no save-format impact). Fields are raw ids/positions — formatting the English message
/// is the presentation layer's job (ADR-006), seen from the human defender's perspective (a native
/// <see cref="CombatResult.Win"/> means the human lost).
/// </remarks>
/// <param name="AttackerNationId">The attacker's nation id — a native nation type (e.g. <c>model.nationType.apache</c>) or a European power (e.g. <c>model.nation.dutch</c>).</param>
/// <param name="DefenderUnitTypeId">The unit type id of the human unit that was attacked.</param>
/// <param name="Outcome">The graded combat result from the <em>attacker's</em> perspective.</param>
/// <param name="Position">The tile the defending human unit stood on.</param>
public readonly record struct CombatNotice(
    string AttackerNationId,
    string DefenderUnitTypeId,
    CombatResult Outcome,
    Position Position);
