namespace CrownAndColony.GameLogic.GameSession;

/// <summary>
/// A record of a <b>turn-driven stance shift</b> between the human and a rival colonial power during the world-advance
/// band of <see cref="Game.EndTurn"/> — when the tension→stance machine (<see cref="Game.UpdateColonialStances"/>,
/// FP-6b) re-derives a met pair's <see cref="Stance"/> from its cooled tension and finds it changed (a war cooling to a
/// cease-fire, a cease-fire drifting back to peace, or a peace the rival breaks from territorial tension). FreeCol
/// surfaces these automatic relationship changes as model messages; up to now ours happened silently inside
/// <see cref="Game.UpdateColonialStances"/> with no return value the human UI can read, so the game collects these
/// notices and the presentation layer surfaces them after the turn resolves (the diplomacy sibling of the combat/raid
/// notices).
/// </summary>
/// <remarks>
/// Transient per-turn UI scratch: cleared at the start of every <c>EndTurn</c>, never saved or restored (no
/// save-format impact). Only shifts that involve the human are recorded — a turn-driven shift between two foreign powers
/// still happens silently (no player-facing notice). <b>Player-initiated</b> stance changes (a treaty the human signs, a
/// war the human declares) are <em>not</em> recorded here — those already have their own diplomacy-screen feedback; this
/// is strictly the <em>automatic</em>, turn-resolved drift. Fields carry the rival's raw nation id and the old/new
/// stance; formatting the English message is the presentation layer's job (ADR-006). RNG-free and deterministic — it
/// reads the cooled tension, draws no randomness, so the human's stream 0 is untouched (ADR-009).
/// </remarks>
/// <param name="RivalNationId">The other power's nation id (e.g. <c>model.nation.spanish</c>).</param>
/// <param name="Previous">The stance the pair held before this turn's re-derivation.</param>
/// <param name="Current">The stance the pair holds after this turn's re-derivation.</param>
public readonly record struct StanceChangeNotice(string RivalNationId, Stance Previous, Stance Current);
