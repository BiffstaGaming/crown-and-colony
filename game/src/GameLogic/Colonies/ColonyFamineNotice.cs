using CrownAndColony.GameLogic.World;

namespace CrownAndColony.GameLogic.Colonies;

/// <summary>
/// A record of a human colony that <b>lost a colonist to famine</b> during the player's turn in
/// <see cref="GameSession.Game.EndTurn"/> — its food ran short, so one colonist starved while the colony itself
/// survived (FreeCol <c>ServerColony.csNewTurn</c>'s <c>model.colony.colonyStarving</c> per-turn famine victim,
/// distinct from the <see cref="ColonyStarvedNotice"/> total-destruction case). The loss happens inside the colony's
/// economy tick with no return value the human UI can read, so the game collects these notices and the presentation
/// layer surfaces them after the turn resolves (the survivable-famine sibling of <see cref="ColonyStarvedNotice"/>).
/// </summary>
/// <remarks>
/// Transient per-turn UI scratch: cleared at the start of every <c>EndTurn</c>, never saved or restored (no
/// save-format impact). Only human-owned colonies are recorded. In the classic default game the colony-centre tile
/// always yields ≥ 2 food (a lone colonist's appetite) and a larger colony only starves when production falls below
/// its appetite (e.g. after a disaster), so this list is normally empty. Fields are the raw colony name, position and
/// the surviving population after the loss; formatting the English message is the presentation layer's job (ADR-006).
/// </remarks>
/// <param name="ColonyName">The starving colony's name.</param>
/// <param name="Position">The tile the colony stands on.</param>
/// <param name="PopulationAfter">The colony's population after the famine victim was lost (≥ 1; the colony survived).</param>
public readonly record struct ColonyFamineNotice(
    string ColonyName,
    Position Position,
    int PopulationAfter);
