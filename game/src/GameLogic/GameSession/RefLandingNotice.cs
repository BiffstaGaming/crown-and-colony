namespace CrownAndColony.GameLogic.GameSession;

/// <summary>
/// A one-off warning that the <b>Royal Expeditionary Force has first come ashore</b> after the human declared
/// independence (FreeCol's REF-landing turn message). Fired once � the first time any REF unit lands on the map � by
/// <see cref="Game.LandRefUnits"/> during the REF's turn, so the human gets a single "the King's army has landed"
/// notice rather than a fresh warning on every staggered reinforcement wave. The later landing waves
/// (<see cref="Game.LandRefUnits"/>'s echeloned cadence) do <b>not</b> re-warn.
/// </summary>
/// <remarks>
/// Transient per-turn UI scratch (the <see cref="MonarchDecreeNotice"/> sibling): cleared at the start of every
/// <c>EndTurn</c>, never saved or restored (no save-format impact). Only the human's rebellion produces it. The field
/// carries the name of the rebel colony nearest the beachhead so the presentation can phrase a located warning;
/// formatting the English message is the presentation layer's job (ADR-006).
/// </remarks>
/// <param name="NearestColonyName">The rebel colony nearest the REF's first landing (empty when the rebel holds none).</param>
public readonly record struct RefLandingNotice(string NearestColonyName);
