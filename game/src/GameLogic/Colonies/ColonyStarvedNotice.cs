using CrownAndColony.GameLogic.World;

namespace CrownAndColony.GameLogic.Colonies;

/// <summary>
/// A record of a human colony <b>destroyed by starvation</b> during the player's turn in
/// <see cref="GameSession.Game.EndTurn"/> — its last colonist could not be fed, so the colony was disposed
/// (FreeCol <c>ServerColony.csNewTurn</c>'s <c>model.colony.colonyStarved</c> branch → <c>ServerPlayer.csDisposeSettlement</c>).
/// The disposal happens inside the colony's economy tick with no return value the human UI can read, so the game
/// collects these notices and the presentation layer surfaces them after the turn resolves (the sibling of
/// <see cref="DisasterNotice"/> / <see cref="Combat.ColonyLossNotice"/>).
/// </summary>
/// <remarks>
/// Transient per-turn UI scratch: cleared at the start of every <c>EndTurn</c>, never saved or restored (no
/// save-format impact — a destroyed colony is simply absent from the save). Fields are the raw colony name and
/// position; formatting the English message is the presentation layer's job (ADR-006). In the classic default game
/// the colony-centre tile always yields ≥ 2 food (a lone colonist's appetite), so a size-1 colony never starves and
/// this list stays empty (the soak keeps every colony — see [colonies] §4).
/// </remarks>
/// <param name="ColonyName">The starved colony's name (captured before disposal).</param>
/// <param name="Position">The tile the colony stood on (now cleared).</param>
public readonly record struct ColonyStarvedNotice(
    string ColonyName,
    Position Position);
