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
/// routed through the system. The first slice carries the immigration trio; the second adds the
/// <c>gameOptions.years</c> year/turn-gate cluster (<see cref="MandatoryColonyYear"/> / <see cref="LastColonialYear"/>
/// / <see cref="IndependenceTurn"/>).
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
/// <param name="MandatoryColonyYear">
/// The in-game year by which a colonial power must have a foothold in the New World (spec
/// <c>model.option.mandatoryColonyYear</c>, <c>gameOptions.years</c> group, classic <b>1600</b>). Before this year a
/// player with no colonies, no surviving units, and a stalled immigration pool is rescued by a survival auto-recruit;
/// from this year on there is no such rescue (FreeCol <c>ServerPlayer.checkForDeath</c>). See [immigration].
/// </param>
/// <param name="LastColonialYear">
/// The last in-game year a colonial power may declare independence (spec <c>model.option.lastColonialYear</c>,
/// <c>gameOptions.years</c> group, classic <b>1800</b>). Once the current year passes this it is too late to declare
/// (FreeCol <c>model.limit.independence.year</c>, the <c>year ≤ lastColonialYear</c> limit). Surfaced on
/// <see cref="Ruleset.LastColonialYear"/>, which the independence and limit-engine consumers read.
/// </param>
/// <param name="IndependenceTurn">
/// The turn cutoff after which declaring independence earns no Col1 score bonus (spec
/// <c>model.option.independenceTurn</c>, <c>gameOptions.years</c> group, classic <b>468</b> — the turn at Spring 1780).
/// Parsed into the bundle so the whole <c>gameOptions.years</c> gate cluster has a single home; the early-independence
/// scoring cutoff that reads it is not yet wired (our independence bonus is currently order-based, see
/// <see cref="GameSession.Game"/> score components), so a missing option simply falls back to the classic 468.
/// </param>
/// <param name="PeaceProbability">
/// The per-turn <b>percentage</b> chance (0–100) that an angry AI keeps honouring a recent peace treaty (spec
/// <c>model.option.peaceProbability</c>, a <c>percentageOption</c>, classic <b>90</b>). This is the <em>base</em> of
/// FreeCol's decaying peace-hold: a power whose grievance would flip a recent peace back to war first rolls against
/// <c>(PeaceProbability/100)^turnsSincePeace</c> (FreeCol <c>EuropeanAIPlayer.peaceHolds</c>), so the longer the peace
/// has held the more likely it eventually breaks. Exposed as a 0–1 multiplier by <see cref="PeaceProbabilityMultiplier"/>
/// (FreeCol <c>getPercentageMultiplier</c> = <c>0.01 × percentage</c>). Benjamin Franklin's <c>peaceTreaty +50%</c>
/// then scales the rolled probability. See [founding-fathers], [diplomacy].
/// </param>
/// <param name="FogOfWar">
/// Whether fog of war is in effect (spec <c>model.option.fogOfWar</c>, a <c>booleanOption</c> in the
/// <c>gameOptions.map</c> group, classic default <b>true</b>). With fog <b>on</b> (the classic default) a tile the
/// player has explored but no longer has a unit/colony watching is "remembered" — drawn dimmed, its contents (foreign
/// units) hidden until looked at again, i.e. visibility is just the union of current lines of sight (FreeCol
/// <c>Player.getVisibleTileSet</c>'s fog branch). With fog <b>off</b>, every explored tile stays permanently visible
/// (FreeCol's else branch returns all explored tiles), so the player keeps a live view of everywhere they have ever
/// been. Read by <see cref="GameSession.Game.CurrentlyVisible"/> / <see cref="GameSession.Game.IsVisible"/>. See
/// [fog-of-war].
/// </param>
/// <param name="CustomIgnoreBoycott">
/// Whether a colony's <b>custom house</b> sells goods that are under boycott (spec
/// <c>model.option.customIgnoreBoycott</c>, a <c>booleanOption</c> in the <c>gameOptions.colony</c> group, classic
/// default <b>true</b>). In classic Colonization the custom house is a smuggling operation — it <b>always</b> moves a
/// colony's surplus, boycott or not (FreeCol <c>Player.canTrade(type, CUSTOM_HOUSE)</c>: a boycotted good still trades
/// through a custom house when this option is on). The sale is otherwise an ordinary market sale — tax is withheld and
/// the price still moves; classic applies no extra smuggling penalty. With this option <b>off</b>, a boycotted good is
/// simply skipped by the custom-house auto-sell (the player must lift the boycott to resume selling it). Read by
/// <see cref="GameSession.Game"/>'s custom-house auto-sell (<c>AutoSellExports</c>). See [custom-house], [market].
/// </param>
public sealed record GameOptions(
    int InitialImmigration,
    int EuropeanUnitImmigrationPenalty,
    int PlayerImmigrationBonus,
    int MandatoryColonyYear,
    int LastColonialYear,
    int IndependenceTurn,
    int PeaceProbability,
    bool FogOfWar,
    bool CustomIgnoreBoycott)
{
    /// <summary>
    /// The classic ruleset's <c>gameOptions</c> values — the fallback when a spec omits an option, and the source of
    /// truth for the default game's base numbers: the immigration trio (15 / −4 / +2), the
    /// <c>gameOptions.years</c> gate cluster (mandatoryColonyYear 1600 / lastColonialYear 1800 / independenceTurn 468),
    /// the peace-hold base (peaceProbability 90), fog of war (on), and custom-house boycott smuggling (on).
    /// </summary>
    public static readonly GameOptions ClassicDefaults = new(
        InitialImmigration: 15,
        EuropeanUnitImmigrationPenalty: -4,
        PlayerImmigrationBonus: 2,
        MandatoryColonyYear: 1600,
        LastColonialYear: 1800,
        IndependenceTurn: 468,
        PeaceProbability: 90,
        FogOfWar: true,
        CustomIgnoreBoycott: true);

    /// <summary>
    /// The peace-hold base as a 0–1 multiplier (FreeCol <c>Specification.getPercentageMultiplier</c> =
    /// <c>0.01 × percentage</c>): the per-turn probability that a recent peace holds before any decay or father
    /// modifier. Classic <b>0.90</b>.
    /// </summary>
    public double PeaceProbabilityMultiplier => 0.01 * PeaceProbability;
}
