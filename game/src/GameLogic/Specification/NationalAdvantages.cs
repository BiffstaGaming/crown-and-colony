namespace CrownAndColony.GameLogic.Specification;

/// <summary>
/// How a game treats the European powers' <b>national advantages</b> — FreeCol's <c>model.option.nationalAdvantages</c>
/// (<c>NationOptions.Advantages</c>), chosen at New Game (86d3fq0za). Each colonial nation normally carries an
/// "advantage" archetype (the Dutch trade bonus, the Spanish conquest bonus, the English immigration bonus, …); this
/// dial decides whether those advantages are in play at all.
/// </summary>
/// <remarks>
/// Faithful subset of FreeCol's three modes. We implement the gameplay-meaningful split — advantages <b>on</b>
/// (<see cref="Selectable"/> / <see cref="Fixed"/>) vs <b>off</b> (<see cref="None"/>) — because our nation roster is
/// already player-selected at New Game, so FreeCol's <c>fixed</c> (the engine assigns each nation, the player can't
/// change it) and <c>selectable</c> (the player picks) differ only in <em>who</em> picks the nation, not in whether the
/// advantage applies. Both therefore keep the advantage; only <see cref="None"/> suppresses it. The default is
/// <see cref="Selectable"/> (FreeCol's default), so a default new game is unchanged (ADR-009).
/// </remarks>
public enum NationalAdvantages
{
    /// <summary>The player picks the nation and its advantage applies (FreeCol <c>SELECTABLE</c>, the default).</summary>
    Selectable,

    /// <summary>The nation's advantage applies but is engine-assigned, not player-chosen (FreeCol <c>FIXED</c>). Gameplay-identical to <see cref="Selectable"/> here (we already let the player pick), kept for parity.</summary>
    Fixed,

    /// <summary>No national advantages: every nation plays with the neutral default — no advantage modifiers, no expert/nation-specific starting-unit upgrades (FreeCol <c>NONE</c>).</summary>
    None,
}
