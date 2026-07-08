namespace CrownAndColony.GameLogic.GameSession;

/// <summary>
/// The stages of the Australian-Federation victory loop (Phase-4a, ADR-021; design doc
/// <c>05_Federation_Victory_System.md</c>). A nation-level state machine on <see cref="Game.FederationPhase"/> that
/// advances as banked Federation Support and Convention Points cross thresholds, culminating in the
/// <see cref="Commonwealth"/> win. Ordinal-persisted (SaveGame v72, omitted when <see cref="ColonialMaturity"/> = the
/// default, so a classic game — which never leaves that stage — stays byte-identical).
///
/// <para>The classic game's ruleset carries <see cref="Specification.Ruleset.VictoryFederation"/> = false, so its phase
/// never advances past <see cref="ColonialMaturity"/> and no Federation state is ever accrued (ADR-009). Only an Australia
/// game (option true) walks this machine.</para>
///
/// <para><b>Faithful-subset note (Phase-4a core):</b> the design's six narrative phases (Colonial Maturity → Federation
/// Movement → Convention Process → Draft Constitution → Referendums → Failure/Retry) are collapsed here into the five
/// mechanically-distinct states the core loop needs. The deeper era/constitutional-clause content is deferred (see the
/// Open-issues section of <c>docs/systems/federation-victory.md</c>).</para>
/// </summary>
public enum FederationPhase
{
    /// <summary>
    /// The starting stage (design "Colonial Maturity"): the colonies are growing and banking Federation Support, but the
    /// movement has not yet gathered enough support to call a convention. The default for every game — a classic game
    /// never leaves it. Ordinal 0, so it is the omitted-when-default save value.
    /// </summary>
    ColonialMaturity = 0,

    /// <summary>
    /// A federation convention has been called (design "Convention Process"): enough colony regions have crossed the
    /// support threshold and enough Convention Points have accrued. The constitution is now being drafted.
    /// </summary>
    ConventionCalled = 1,

    /// <summary>
    /// The draft constitution is complete (design "Draft Constitution"): the movement may now put Federation to a
    /// referendum of the colonies.
    /// </summary>
    ConstitutionDrafted = 2,

    /// <summary>
    /// A referendum is in progress or has been held but not yet carried (design "Referendums"): the colonies are voting.
    /// A failed referendum leaves the phase here (with support/momentum consequences) so the movement can try again — it
    /// never ends the game (design Phase 6, Failure and Retry).
    /// </summary>
    Referendum = 3,

    /// <summary>
    /// The referendum has carried and the Commonwealth is proclaimed (design "Commonwealth Proclamation") — the human
    /// wins by Federation. Terminal: <see cref="Game.Winner"/> reports the human once this stage is reached.
    /// </summary>
    Commonwealth = 4,
}
