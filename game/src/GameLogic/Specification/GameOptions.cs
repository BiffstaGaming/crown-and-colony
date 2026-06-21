namespace CrownAndColony.GameLogic.Specification;

/// <summary>
/// The base game-option tuning numbers — FreeCol's <c>gameOptions</c> group (its
/// <see href="https://github.com/FreeCol/freecol/blob/master/src/net/sf/freecol/common/option/GameOptions.java">GameOptions</see>
/// constants), parsed once from the spec and read wherever a hardcoded balance constant used to live. Carried on
/// <see cref="Ruleset.GameOptions"/> — the base-options analogue of <see cref="DifficultyOptions"/> (which holds the
/// per-difficulty-level group instead).
/// </summary>
/// <remarks>
/// <para>
/// Unlike <see cref="DifficultyOptions"/>, these options are NOT restated per difficulty level — they live once in the
/// base <c>gameOptions</c> group, so each is read by a plain document-wide lookup of its <c>value</c> (else
/// <c>defaultValue</c>), the same way <c>model.option.lastColonialYear</c> is. A spec that omits an option falls back
/// to its classic value below, so the default game stays byte-identical (ADR-009).
/// </para>
/// <para>
/// Pure and immutable: parsed once, no state, no randomness. Properties are added slice by slice as constants are
/// routed through the system; the first slice carries the immigration trio.
/// </para>
/// </remarks>
/// <param name="InitialImmigration">
/// Immigration points needed for a colonial player's first emigrant (spec <c>model.option.initialImmigration</c>,
/// classic <b>15</b>; the per-emigrant <c>crossesIncrement</c> rise lives in <see cref="DifficultyOptions"/>). The raw
/// (pre nation-modifier) target a fresh player starts at. See [immigration].
/// </param>
/// <param name="EuropeanUnitImmigrationPenalty">
/// Immigration lost per person idling on the Europe dock each turn (spec
/// <c>model.option.europeanUnitImmigrationPenalty</c>, a negative value, classic <b>−4</b>). Applied in the per-turn
/// Europe contribution, clamped so the turn's total immigration is never negative. See [immigration].
/// </param>
/// <param name="PlayerImmigrationBonus">
/// Flat immigration every colonial player gains each turn just for being a colonial power (spec
/// <c>model.option.playerImmigrationBonus</c>, classic <b>+2</b>). See [immigration].
/// </param>
public sealed record GameOptions(
    int InitialImmigration,
    int EuropeanUnitImmigrationPenalty,
    int PlayerImmigrationBonus)
{
    /// <summary>
    /// The classic ruleset's <c>gameOptions</c> values — the fallback when a spec omits an option, and the source of
    /// truth for the default game's base immigration numbers (15 / −4 / +2).
    /// </summary>
    public static readonly GameOptions ClassicDefaults = new(
        InitialImmigration: 15,
        EuropeanUnitImmigrationPenalty: -4,
        PlayerImmigrationBonus: 2);
}
