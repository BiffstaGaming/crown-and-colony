using CrownAndColony.GameLogic.World;

namespace CrownAndColony.GameLogic.GameSession;

/// <summary>
/// The Australian-Federation <b>constitution drafting</b> system (WS3.3, design doc <c>05_Federation_Victory_System.md</c>
/// Phase 4). Once a convention has been called, the human <b>drafts constitutional clauses</b> — each spends banked
/// <see cref="ConventionPoints"/> (and sometimes gold), applies a one-off effect through the existing Federation levers,
/// and adds its weight to a 0–100% <see cref="ConstitutionProgressPercent"/> meter. When progress reaches
/// <see cref="ConstitutionDraftThresholdPercent"/> (the design's "≥80% drafted" Phase-5 gate), the draft constitution is
/// complete and a referendum may be put (the transition is resolved in <c>Game.Federation.ResolveCommonwealthFederation</c>).
///
/// <para><b>Off for classic (ADR-009):</b> every path is gated on <see cref="Specification.Ruleset.VictoryFederation"/>,
/// so a classic game drafts nothing, the meter reads 0, and the drafted-clause set is empty (omitted from the save →
/// byte-identical). Clause effects are <b>one-off at draft time</b> (mirroring the Pioneer on-election effects): they
/// mutate already-persisted colony support / referendum targets, so nothing re-applies on load.</para>
///
/// <para><b>Sensitive-content note:</b> the design's <i>Immigration Policy</i> clause (the historical White Australia
/// Policy) is deliberately <b>not</b> implemented here — it is held pending Chris's cultural-protocol sign-off (doc 15 /
/// the 4c.11 sensitive-framing gate). The <i>Defence Union</i>, <i>High Court</i> and <i>Railway Standardisation</i>
/// clauses are deferred pending the systems their faithful effects need (see <c>docs/systems/federation-victory.md</c> §5).</para>
/// </summary>
public sealed partial class Game
{
    /// <summary>A curated constitutional clause the human can draft (WS3.3): its stable id, display name, meter weight, and Convention-Point + gold cost.</summary>
    private sealed record ConstitutionClause(string Id, string DisplayName, int WeightPercent, int ConventionPointCost, int GoldCost);

    /// <summary>The <b>Capital Compromise</b> clause id (WS3.3) — also the WS3.6 waiver for the New South Wales mobilisation quota (the 1899 concessions that carried NSW's re-run, incl. siting the federal capital in NSW). Shared so the catalog and the quota check can't drift.</summary>
    internal const string CapitalCompromiseClauseId = "capitalCompromise";

    /// <summary>The design's "≥80% drafted" completion gate (doc 05 Phase 5): the constitution is complete once <see cref="ConstitutionProgressPercent"/> reaches this.</summary>
    internal const int ConstitutionDraftThresholdPercent = 80;

    /// <summary>The constitution-completion gate percentage (design Phase-5 "≥80% drafted") — a public read-only oracle for the panel's progress marker (ADR-006).</summary>
    public int ConstitutionDraftThreshold => ConstitutionDraftThresholdPercent;

    /// <summary>Samuel Griffith's live draft-progress bonus (doc 11: "+30% Draft Constitution progress") — derived from his Congress seat, no save state (the Quick-relief precedent).</summary>
    internal const int GriffithProgressBonusPercent = 30;

    // Curated clause costs + one-off effect magnitudes (WS3.3 first-pass, tunable like the WS3.7 gate counts).
    private const int SenateEqualityCost = 90;
    private const int SenateEqualitySmallColonyBoost = 25;   // SA/Tas/WA gain
    private const int SenateEqualityLargeColonyPenalty = 8;  // NSW/Vic lose (floored at 0)
    private const int CapitalCompromiseCost = 60;
    private const int CapitalCompromiseGold = 600;
    private const int CapitalCompromiseTargetReduction = 3;  // NSW + Vic referendum target −3 each
    private const int FreeTradeCost = 70;
    private const int FreeTradeGold = 900;
    private const int FreeTradeSupportBoost = 12;            // one-off nationwide goodwill (a proxy — see the catalog note)

    /// <summary>
    /// The curated constitutional clauses (WS3.3). Weights sum to exactly 100, so drafting all of them reaches 100%; the
    /// design's held/blocked clauses are simply absent from the meter (adding one later is a data re-weight, save-compatible
    /// because the drafted set is stored by id). <b>Intercolonial Free Trade is a first-pass one-off proxy</b> (gold out for
    /// forgone tariff, support in for goodwill) — the faithful standing trade-bonus/tariff-penalty modifier needs a hook that
    /// does not exist yet (deferred, §5).
    /// </summary>
    private static readonly IReadOnlyList<ConstitutionClause> ConstitutionClauseCatalog =
    [
        new("senateEquality", "Senate Equality", 34, SenateEqualityCost, 0),
        new("capitalCompromise", "Capital Compromise", 33, CapitalCompromiseCost, CapitalCompromiseGold),
        new("intercolonialFreeTrade", "Intercolonial Free Trade", 33, FreeTradeCost, FreeTradeGold),
    ];

    /// <summary>The clauses drafted into the constitution so far (WS3.3), in draft order. Empty for classic and for an Australia game that has drafted none. Persisted (SaveGame v74, omitted when empty).</summary>
    private readonly List<string> _draftedClauses = [];

    /// <summary>The constitutional clauses drafted so far (WS3.3), in draft order — read by the save layer and the panel.</summary>
    public IReadOnlyList<string> DraftedClauses => _draftedClauses;

    /// <summary>Re-installs a restored drafted clause (save load, v74). Ignores unknown or duplicate ids (forward-compatible: a clause dropped from the catalog is skipped).</summary>
    /// <param name="clauseId">The drafted clause id.</param>
    internal void SetDraftedClause(string clauseId)
    {
        if (ConstitutionClauseCatalog.Any(c => c.Id == clauseId) && !_draftedClauses.Contains(clauseId))
        {
            _draftedClauses.Add(clauseId);
        }
    }

    /// <summary>
    /// Spends <paramref name="points"/> banked <see cref="ConventionPoints"/> (WS3.3 clause drafting). <b>A no-op unless the
    /// ruleset enables the Federation victory</b> (classic accrues none) → byte-identical (ADR-009); never drives the total
    /// below 0. Non-positive amounts are ignored. RNG-free.
    /// </summary>
    /// <param name="points">Convention Points to deduct (values ≤ 0 are ignored).</param>
    internal void SpendConventionPoints(int points)
    {
        if (!Ruleset.VictoryFederation || points <= 0)
        {
            return; // classic / non-Federation ruleset — byte-identical (ADR-009)
        }
        _conventionPoints = Math.Max(0, _conventionPoints - points);
    }

    /// <summary>Samuel Griffith's derived draft-progress bonus (WS3.3): <see cref="GriffithProgressBonusPercent"/> while he sits in the human's Congress, else 0. Read live from the persisted Congress (no new save state).</summary>
    private int GriffithProgressBonus() =>
        HasAbilityFor(_human, DraftConstitutionAbility) ? GriffithProgressBonusPercent : 0;

    /// <summary>
    /// The draft-constitution progress, 0–100% (WS3.3): the summed <see cref="ConstitutionClause.WeightPercent"/> of the
    /// drafted clauses plus Griffith's derived <see cref="GriffithProgressBonus"/>, clamped. 0 for a classic game. The
    /// panel draws this as a gauge with the <see cref="ConstitutionDraftThresholdPercent"/> gate marked.
    /// </summary>
    public int ConstitutionProgressPercent =>
        !Ruleset.VictoryFederation
            ? 0
            : Math.Clamp(_draftedClauses.Sum(id => ConstitutionClauseCatalog.First(c => c.Id == id).WeightPercent) + GriffithProgressBonus(), 0, 100);

    /// <summary>
    /// The curated constitution clauses for the panel (WS3.3): each clause's id, display name, meter weight, Convention-Point
    /// and gold cost, whether it is already drafted, and its <see cref="CheckDraftClause"/> gate (so the panel can enable /
    /// disable-with-reason each Draft button). Empty for a classic game.
    /// </summary>
    public IReadOnlyList<(string Id, string DisplayName, int WeightPercent, int ConventionPointCost, int GoldCost, bool IsDrafted, MoveCheck DraftCheck)> ConstitutionClauses() =>
        Ruleset.VictoryFederation
            ? ConstitutionClauseCatalog
                .Select(c => (c.Id, c.DisplayName, c.WeightPercent, c.ConventionPointCost, c.GoldCost, _draftedClauses.Contains(c.Id), CheckDraftClause(c.Id)))
                .ToList()
            : [];

    /// <summary>
    /// Whether the human may <b>draft</b> constitutional clause <paramref name="clauseId"/> right now (WS3.3): the Federation
    /// victory is enabled, a convention has been called (phase <see cref="FederationPhase.ConventionCalled"/>), the clause is
    /// a real un-drafted catalog clause, and the human has enough banked Convention Points and gold. A pure, RNG-free read
    /// the panel gates its per-clause Draft button on; clause-specific reasons.
    /// </summary>
    /// <param name="clauseId">The clause id (one of the curated catalog).</param>
    public MoveCheck CheckDraftClause(string clauseId)
    {
        if (!Ruleset.VictoryFederation)
        {
            return MoveCheck.No("Federation is not this game's victory path.");
        }
        if (_federationPhase != FederationPhase.ConventionCalled)
        {
            return MoveCheck.No("Clauses are drafted after the convention is called.");
        }
        ConstitutionClause? clause = ConstitutionClauseCatalog.FirstOrDefault(c => c.Id == clauseId);
        if (clause is null)
        {
            return MoveCheck.No("Unknown constitutional clause.");
        }
        if (_draftedClauses.Contains(clauseId))
        {
            return MoveCheck.No($"{clause.DisplayName} is already drafted.");
        }
        if (_conventionPoints < clause.ConventionPointCost)
        {
            return MoveCheck.No($"{clause.DisplayName} needs {clause.ConventionPointCost} Convention Points.");
        }
        if (clause.GoldCost > 0 && _human.Gold < clause.GoldCost)
        {
            return MoveCheck.No($"{clause.DisplayName} needs {clause.GoldCost} gold.");
        }
        return MoveCheck.Yes(0);
    }

    /// <summary>
    /// <b>Drafts</b> constitutional clause <paramref name="clauseId"/> (WS3.3): deducts its Convention-Point and gold cost,
    /// records it in the drafted set, and applies its one-off effect. A no-op that returns false when
    /// <see cref="CheckDraftClause"/> forbids it. RNG-free; the constitution completes automatically at end of turn once the
    /// drafted weight reaches <see cref="ConstitutionDraftThresholdPercent"/>.
    /// </summary>
    /// <param name="clauseId">The clause id to draft.</param>
    /// <returns>True when the clause was drafted; false when the preconditions were not met.</returns>
    public bool DraftClause(string clauseId)
    {
        if (!CheckDraftClause(clauseId).Allowed)
        {
            return false;
        }
        ConstitutionClause clause = ConstitutionClauseCatalog.First(c => c.Id == clauseId);
        SpendConventionPoints(clause.ConventionPointCost);
        if (clause.GoldCost > 0)
        {
            _human.Gold -= clause.GoldCost;
        }
        _draftedClauses.Add(clauseId);
        ApplyClauseEffect(clauseId);
        RecordFederationMilestone($"The {clause.DisplayName} clause is drafted into the constitution.");
        return true;
    }

    /// <summary>
    /// Applies a drafted clause's <b>one-off effect</b> through the existing Federation levers (WS3.3). Effects are applied
    /// once, at draft time (like the Pioneer on-election effects), and mutate already-persisted colony support / referendum
    /// targets — so nothing is re-applied on load. Each lever is itself Federation-gated (a no-op in classic).
    /// </summary>
    /// <param name="clauseId">The just-drafted clause id.</param>
    private void ApplyClauseEffect(string clauseId)
    {
        switch (clauseId)
        {
            case "senateEquality":
                // The small-state equal-Senate bargain: the small colonies gain support at the large colonies' mild expense.
                AddFederationSupportToSmallColonies(_human, SenateEqualitySmallColonyBoost);
                AddFederationSupportToRegion(_human, AustraliaColonyStart.RegionKey(AustraliaColony.NewSouthWales), -SenateEqualityLargeColonyPenalty);
                AddFederationSupportToRegion(_human, AustraliaColonyStart.RegionKey(AustraliaColony.Victoria), -SenateEqualityLargeColonyPenalty);
                break;
            case "capitalCompromise":
                // The Canberra siting compromise: resolves the NSW/Victoria rivalry by lowering both their referendum targets.
                ReduceFederationTarget(AustraliaColonyStart.RegionKey(AustraliaColony.NewSouthWales), CapitalCompromiseTargetReduction);
                ReduceFederationTarget(AustraliaColonyStart.RegionKey(AustraliaColony.Victoria), CapitalCompromiseTargetReduction);
                break;
            case "intercolonialFreeTrade":
                // A single customs union: one-off nationwide goodwill (a first-pass proxy for the standing trade bonus, §5).
                AddFederationSupportToAllColonies(_human, FreeTradeSupportBoost);
                break;
        }
    }
}
