using CrownAndColony.GameLogic.World;

namespace CrownAndColony.GameLogic.Colonies;

/// <summary>
/// A record of loyalist (Tory) colonists who <b>abandoned a colony on the declaration of independence</b> — the
/// royalists who would not take up arms against their King fled rather than serve the new nation. Emitted once, per
/// affected colony, by <see cref="GameSession.Game.DeclareIndependence"/> (see
/// <see cref="GameSession.Game.ToryExpulsionNotices"/>).
/// <para><b>Col1-faithful reconstruction.</b> The original Sid Meier's Colonization removed a fraction of each colony's
/// non-rebel population at the Declaration, scaled by that colony's Sons-of-Liberty support — a low-loyalty colony bled
/// more colonists than a fervent one. <b>FreeCol does not model this</b> (its <c>csDeclareIndependence</c> has no such
/// step), so this is our Col1 addition. The English message is the presentation layer's job (ADR-006).</para>
/// </summary>
/// <remarks>
/// Transient UI scratch: the declaration is a one-off player action, so the presentation reads these notices right
/// after <see cref="GameSession.Game.DeclareIndependence"/> returns. Never saved or restored (the colony's reduced
/// population is what persists). Only the declaring nation's colonies produce these.
/// </remarks>
/// <param name="ColonyName">The colony the loyalists deserted.</param>
/// <param name="Position">The tile the colony stands on.</param>
/// <param name="ToriesLost">How many loyalist colonists abandoned the colony (≥ 1; a colony with no loss emits no notice).</param>
/// <param name="PopulationAfter">The colony's population after the loyalists left (≥ 1; at least one colonist always remains).</param>
public readonly record struct ToryExpulsionNotice(
    string ColonyName,
    Position Position,
    int ToriesLost,
    int PopulationAfter);
