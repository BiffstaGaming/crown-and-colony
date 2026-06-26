namespace CrownAndColony.GameLogic.GameSession;

/// <summary>
/// A record of the human player's <b>first contact</b> with a rival colonial power during the world-advance band of
/// <see cref="Game.EndTurn"/> — the turn the human's explored fog first covers that power's unit or colony, flipping the
/// pair from <see cref="Stance.Uncontacted"/> to <see cref="Stance.Peace"/> (FreeCol <c>makeContact</c> / the
/// <c>EventPanel.FIRST_CONTACT</c> message). Today the flip happens silently inside
/// <see cref="Game.DetectColonialContacts"/> with no return value the human UI can read, so the game collects these
/// notices and the presentation layer surfaces them after the turn resolves (the diplomacy sibling of the combat/raid
/// notices).
/// </summary>
/// <remarks>
/// Transient per-turn UI scratch: cleared at the start of every <c>EndTurn</c>, never saved or restored (no
/// save-format impact). Only contacts that involve the human are recorded — two foreign powers meeting each other still
/// flips silently (no player-facing notice). The field is the met power's raw nation id; formatting the English message
/// (and resolving the nation name) is the presentation layer's job (ADR-006). RNG-free and deterministic: it reads the
/// existing fog, draws no randomness, so the human's stream 0 is untouched (ADR-009).
/// </remarks>
/// <param name="RivalNationId">The newly-met colonial power's nation id (e.g. <c>model.nation.french</c>).</param>
public readonly record struct FirstContactNotice(string RivalNationId);
