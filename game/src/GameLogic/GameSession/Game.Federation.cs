using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.Randomness;
using CrownAndColony.GameLogic.World;

namespace CrownAndColony.GameLogic.GameSession;

/// <summary>
/// The Australian-Federation victory loop (Phase-4a, ADR-021; design docs <c>05_Federation_Victory_System.md</c> and
/// <c>06_Colony_Progression_Prerequisites.md</c>). Replaces the classic War-of-Independence win with Federation of the
/// six colonies by 1901: colonies bank <b>Federation Support</b> from their Civic Voice, the nation accrues
/// <b>Convention Points</b>, and a <see cref="FederationPhase"/> state machine advances toward the Commonwealth
/// proclamation — the human's win.
///
/// <para><b>Off for classic (ADR-009 byte-stability):</b> every path here is gated on
/// <see cref="Specification.Ruleset.VictoryFederation"/>, which is false in the classic ruleset. When off: no Federation
/// Support is banked, no Convention Points accrue, the phase never leaves <see cref="FederationPhase.ColonialMaturity"/>,
/// and the referendum stream is never touched — so the classic soak stays byte-identical and adds no save tokens.</para>
/// </summary>
public sealed partial class Game
{
    // ── RNG ──────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// RNG stream reserved for the Federation <b>referendum</b> roll (Phase-4a) — a high id like the other reserved
    /// streams (<see cref="EventStreamId"/> = 105), so the referendum draw never correlates with or shifts the human's
    /// economy stream 0 (ADR-006/ADR-009). The roll fires only when the ruleset enables the Federation victory (classic
    /// leaves it off → this stream is never touched), and like the event/disaster streams the generator is seeded
    /// per-referendum from the human's own RNG state read WITHOUT advancing it, so nothing on stream 0 shifts.
    /// </summary>
    private const ulong FederationStreamId = 107;

    // ── Thresholds (Phase-4a core; simplified from the design's per-colony targets) ───────────────────────────

    /// <summary>The number of the six colony regions whose Federation Support must cross <see cref="RegionSupportThreshold"/> to call a convention (design Phase 3: "at least 4 colonies above threshold").</summary>
    internal const int RegionsToCallConvention = 4;

    /// <summary>The per-region Federation Support percentage a region must reach to count toward calling a convention (design Phase 3: "above 40% Federation Support").</summary>
    internal const int RegionSupportThreshold = 40;

    /// <summary>Convention Points needed before a convention can be called (design "Convention Points available"; the Normal tier of the design's suggested global thresholds, scaled to the core loop).</summary>
    internal const int ConventionPointsToCallConvention = 200;

    /// <summary>The per-region Federation Support percentage every active region must reach before a referendum may be put (design Phase 5: "each colony has at least 50% Federation Support").</summary>
    internal const int RegionSupportForReferendum = 50;

    /// <summary>The share of each turn's net Civic Voice that also accrues as national Convention Points (a light fraction so points trail support — the design keeps them a separate, slower axis).</summary>
    private const int ConventionPointsPerCivicVoicePercent = 25;

    // ── State (persisted, SaveGame v72; omitted when default so classic stays byte-identical) ─────────────────

    private FederationPhase _federationPhase = FederationPhase.ColonialMaturity;
    private int _conventionPoints;
    private int _referendumAttempts;

    /// <summary>
    /// The current stage of the Federation victory loop (Phase-4a). <see cref="FederationPhase.ColonialMaturity"/> for
    /// every classic game (which never advances it) and for an Australia game that has not yet gathered enough support;
    /// terminal at <see cref="FederationPhase.Commonwealth"/> (the human has won). Persisted (v72, omitted when the
    /// default).
    /// </summary>
    public FederationPhase FederationPhase => _federationPhase;

    /// <summary>
    /// The nation-level <b>Convention Points</b> banked toward drafting the constitution and calling a convention
    /// (Phase-4a). Accrues alongside Federation Support from the human's Civic Voice; 0 for a classic game. Persisted
    /// (v72, omitted when 0).
    /// </summary>
    public int ConventionPoints => _conventionPoints;

    /// <summary>
    /// How many referendums have been <b>held</b> so far (Phase-4a) — a failed referendum increments this and leaves the
    /// phase at <see cref="FederationPhase.Referendum"/> so the movement can try again (design Phase 6, Failure/Retry).
    /// Persisted (v72, omitted when 0).
    /// </summary>
    public int ReferendumAttempts => _referendumAttempts;

    /// <summary>Re-installs the restored Federation phase (save load, v72).</summary>
    internal void SetFederationPhase(FederationPhase phase) => _federationPhase = phase;

    /// <summary>Re-installs the restored Convention Points (save load, v72).</summary>
    internal void SetConventionPoints(int points) => _conventionPoints = Math.Max(0, points);

    /// <summary>Re-installs the restored referendum-attempt count (save load, v72).</summary>
    internal void SetReferendumAttempts(int attempts) => _referendumAttempts = Math.Max(0, attempts);

    // ── The six colony regions (canonical Federation order — NSW, Vic, Qld, SA, Tas, WA) ──────────────────────

    /// <summary>
    /// The six referendum region keys of the Australian Federation, in the canonical order the design uses (NSW, Vic,
    /// Qld, SA, Tas, WA). These are the <see cref="Region.Key"/>s the Australia map stamps
    /// (<see cref="AustraliaColonyStart"/>); a colony's region is resolved via <see cref="GameMap.RegionOf"/>.
    /// </summary>
    public static IReadOnlyList<string> FederationRegionKeys { get; } =
    [
        "model.region.newSouthWales",
        "model.region.victoria",
        "model.region.queensland",
        "model.region.southAustralia",
        "model.region.tasmania",
        "model.region.westernAustralia",
    ];

    // ── Accrual (called from AccumulateLibertyAndElectFathers) ────────────────────────────────────────────────

    /// <summary>
    /// Banks this turn's <paramref name="netCivicVoice"/> (the same net, founding-father-modified bell figure the
    /// liberty pool receives) into the colony's <see cref="Colony.FederationSupport"/>, and — for the human — accrues a
    /// fraction as national <see cref="ConventionPoints"/> (Phase-4a). <b>Gated on the ruleset's Federation victory</b>
    /// and the human player, so a classic game (option off) and every AI accrue nothing (ADR-009). RNG-free.
    /// </summary>
    /// <param name="player">The colony's owner (Convention Points accrue only for the human).</param>
    /// <param name="colony">The colony banking support this turn.</param>
    /// <param name="netCivicVoice">The net bells this colony produced this turn (may be negative).</param>
    private void AccrueFederationSupport(Player player, Colony colony, int netCivicVoice)
    {
        if (!Ruleset.VictoryFederation)
        {
            return; // classic / non-Federation ruleset: no Federation state, byte-identical (ADR-009)
        }
        colony.AddFederationSupport(netCivicVoice);
        if (player.IsHuman && netCivicVoice > 0)
        {
            // Convention Points trail support: a light fraction of positive net Civic Voice, national-scope.
            _conventionPoints += netCivicVoice * ConventionPointsPerCivicVoicePercent / 100;
        }
    }

    // ── Per-region aggregate (read for the panel + the phase machine) ─────────────────────────────────────────

    /// <summary>
    /// The human's Federation Support in <paramref name="regionKey"/> (Phase-4a): the unweighted average of the
    /// <see cref="Colony.FederationSupportPercent"/> of every human colony whose tile lies in that region, 0–100 (0 when
    /// the human holds no colony there). Mirrors the unweighted per-colony average <see cref="NationalSonsOfLiberty"/>
    /// uses. A pure, RNG-free read the Federation panel and the phase machine consume.
    /// </summary>
    /// <param name="regionKey">One of <see cref="FederationRegionKeys"/> (e.g. <c>model.region.newSouthWales</c>).</param>
    public int RegionFederationSupport(string regionKey)
    {
        List<Colony> inRegion = HumanColoniesInRegion(regionKey);
        if (inRegion.Count == 0)
        {
            return 0;
        }
        return inRegion.Sum(c => c.FederationSupportPercent) / inRegion.Count;
    }

    /// <summary>The human's colonies whose map tile lies in <paramref name="regionKey"/> (Phase-4a).</summary>
    private List<Colony> HumanColoniesInRegion(string regionKey) =>
        ColoniesOf(_human).Where(c => Map.RegionOf(c.Position)?.Key == regionKey).ToList();

    /// <summary>
    /// Federation Support in each of the six regions (Phase-4a), keyed by <see cref="Region.Key"/> in the canonical
    /// <see cref="FederationRegionKeys"/> order — the data the Federation panel draws as per-region bars. A pure read.
    /// </summary>
    public IReadOnlyList<(string RegionKey, int SupportPercent)> RegionSupportSummary() =>
        FederationRegionKeys.Select(k => (k, RegionFederationSupport(k))).ToList();

    /// <summary>The number of the six regions at or above <paramref name="threshold"/> Federation Support (Phase-4a).</summary>
    private int RegionsAtLeast(int threshold) =>
        FederationRegionKeys.Count(k => RegionFederationSupport(k) >= threshold);

    // ── Phase machine + convention / referendum actions ───────────────────────────────────────────────────────

    /// <summary>
    /// Whether the human may <b>call a federation convention</b> right now (Phase-4a): the Federation victory is enabled,
    /// the phase is still <see cref="FederationPhase.ColonialMaturity"/>, at least <see cref="RegionsToCallConvention"/>
    /// of the six regions are above <see cref="RegionSupportThreshold"/> support, and at least
    /// <see cref="ConventionPointsToCallConvention"/> Convention Points have accrued (design Phase 3). A pure, RNG-free
    /// read the panel gates its "call convention" action on.
    /// </summary>
    public MoveCheck CheckCallConvention()
    {
        if (!Ruleset.VictoryFederation)
        {
            return MoveCheck.No("Federation is not this game's victory path.");
        }
        if (_federationPhase != FederationPhase.ColonialMaturity)
        {
            return MoveCheck.No("A convention has already been called.");
        }
        if (RegionsAtLeast(RegionSupportThreshold) < RegionsToCallConvention)
        {
            return MoveCheck.No($"At least {RegionsToCallConvention} colonies must reach {RegionSupportThreshold}% Federation Support.");
        }
        if (_conventionPoints < ConventionPointsToCallConvention)
        {
            return MoveCheck.No($"At least {ConventionPointsToCallConvention} Convention Points are needed.");
        }
        return MoveCheck.Yes(0);
    }

    /// <summary>
    /// <b>Calls the federation convention</b> (Phase-4a): advances the phase to <see cref="FederationPhase.ConventionCalled"/>
    /// (the constitution is now being drafted). A no-op that returns false when <see cref="CheckCallConvention"/> forbids
    /// it. RNG-free.
    /// </summary>
    /// <returns>True when the convention was called; false when the preconditions were not met.</returns>
    public bool CallConvention()
    {
        if (!CheckCallConvention().Allowed)
        {
            return false;
        }
        _federationPhase = FederationPhase.ConventionCalled;
        RecordHistoryIfHuman("The federation convention has been called.");
        return true;
    }

    /// <summary>
    /// Whether the human may <b>put Federation to a referendum</b> right now (Phase-4a): the Federation victory is
    /// enabled, the constitution has been drafted (phase <see cref="FederationPhase.ConstitutionDrafted"/> or a prior
    /// referendum has failed leaving the phase at <see cref="FederationPhase.Referendum"/>), and every one of the six
    /// regions in which the human holds a colony is at or above <see cref="RegionSupportForReferendum"/> support (design
    /// Phase 5). A pure, RNG-free read the panel gates its "put to referendum" action on.
    /// </summary>
    public MoveCheck CheckPutToReferendum()
    {
        if (!Ruleset.VictoryFederation)
        {
            return MoveCheck.No("Federation is not this game's victory path.");
        }
        if (_federationPhase is not (FederationPhase.ConstitutionDrafted or FederationPhase.Referendum))
        {
            return MoveCheck.No("The constitution must be drafted before a referendum can be held.");
        }
        // Every region the human is settled in must be at referendum strength (a region with no human colony is not put).
        List<string> settledRegions = FederationRegionKeys.Where(k => HumanColoniesInRegion(k).Count > 0).ToList();
        if (settledRegions.Count == 0)
        {
            return MoveCheck.No("No colony regions are settled.");
        }
        if (settledRegions.Any(k => RegionFederationSupport(k) < RegionSupportForReferendum))
        {
            return MoveCheck.No($"Every settled colony must reach {RegionSupportForReferendum}% Federation Support.");
        }
        return MoveCheck.Yes(0);
    }

    /// <summary>
    /// <b>Holds a Federation referendum</b> (Phase-4a): a seeded pass/fail roll (design Phase 5/6). The referendum
    /// <em>carries</em> when the average settled-region support beats a seeded threshold; on success the phase advances
    /// to <see cref="FederationPhase.Referendum"/> and <see cref="ResolveCommonwealthFederation"/> proclaims the
    /// Commonwealth on the next turn resolution. On failure the phase stays at <see cref="FederationPhase.Referendum"/>,
    /// <see cref="ReferendumAttempts"/> increments, and a temporary anti-Federation momentum penalty is registered (the
    /// Boston-Tea-Party precedent) so the movement must rebuild before retrying. Uses the injected RNG on a reserved
    /// stream, seeded from the human's saved state read WITHOUT advancing stream 0 (ADR-009), so a classic game — which
    /// never reaches this method — draws nothing and stays byte-identical.
    /// </summary>
    /// <returns>True when the referendum carried; false when it failed (retry permitted).</returns>
    public bool HoldReferendum()
    {
        if (!CheckPutToReferendum().Allowed)
        {
            return false;
        }
        _federationPhase = FederationPhase.Referendum;

        // A dedicated referendum generator seeded off the human's own RNG state — never advancing stream 0 (we read its
        // saved state word read-only). The attempt count mixes in so a retry rolls independently.
        ulong baseState = _random.SaveState().State;
        var rng = new Pcg32Random(baseState ^ ((ulong)Turn << 5) ^ ((ulong)(_referendumAttempts + 1) << 33), FederationStreamId);

        // The vote carries on a 0–99 roll under the average settled-region support (a stronger movement is likelier to
        // pass): support 100 always carries, support 0 never does.
        int averageSupport = AverageSettledSupport();
        int roll = rng.Next(100);
        _referendumAttempts++;

        if (roll < averageSupport)
        {
            _referendumCarried = true; // ResolveCommonwealthFederation proclaims the Commonwealth on the next EndTurn resolution
            RecordHistoryIfHuman("The Federation referendum has carried.");
            return true;
        }

        // Failure (design Phase 6): anti-Federation momentum — a temporary support penalty on every settled colony, so
        // the movement loses ground and must rebuild before it can retry. Modelled with the timed-modifier machinery
        // (the Boston-Tea-Party precedent) but applied as a direct one-off support drain (Phase-4a core keeps it simple).
        foreach (Colony colony in ColoniesOf(_human).ToList())
        {
            colony.AddFederationSupport(-(colony.FederationSupport / 10)); // shed ~10% of banked support
        }
        RecordHistoryIfHuman("The Federation referendum has failed. The movement must rebuild support.");
        return false;
    }

    /// <summary>The unweighted average Federation Support across the regions the human is settled in (0 when unsettled).</summary>
    private int AverageSettledSupport()
    {
        List<int> settled = FederationRegionKeys
            .Where(k => HumanColoniesInRegion(k).Count > 0)
            .Select(RegionFederationSupport)
            .ToList();
        return settled.Count == 0 ? 0 : settled.Sum() / settled.Count;
    }

    // ── Turn resolution (called from EndTurn, beside ResolveWarOfIndependence) ────────────────────────────────

    /// <summary>
    /// Advances the Federation state machine at end of turn (Phase-4a), beside <see cref="ResolveWarOfIndependence"/>:
    /// <list type="bullet">
    /// <item>Drafts the constitution automatically once a convention has been called and enough Convention Points have
    /// accrued (<see cref="FederationPhase.ConventionCalled"/> → <see cref="FederationPhase.ConstitutionDrafted"/>).</item>
    /// <item>Proclaims the Commonwealth — the human's win — once a referendum has carried
    /// (<see cref="FederationPhase.Referendum"/> → <see cref="FederationPhase.Commonwealth"/>, the terminal state
    /// <see cref="Winner"/> reads).</item>
    /// </list>
    /// A strict no-op when the ruleset's Federation victory is off (classic), so the default game is byte-identical and
    /// draws no RNG (ADR-009). The convention-call and referendum <em>actions</em> are player-driven (the panel), not
    /// resolved here — this only advances the automatic transitions.
    /// </summary>
    internal void ResolveCommonwealthFederation()
    {
        if (!Ruleset.VictoryFederation)
        {
            return; // classic: no Federation loop, byte-identical (ADR-009)
        }

        // Convention called → draft the constitution once enough points have banked (a second Convention-Points gate).
        if (_federationPhase == FederationPhase.ConventionCalled
            && _conventionPoints >= ConventionPointsToDraftConstitution)
        {
            _federationPhase = FederationPhase.ConstitutionDrafted;
            RecordHistoryIfHuman("The draft constitution is complete.");
        }

        // A carried referendum flags the win: the phase is at Referendum and every settled region still holds referendum
        // strength (a failed referendum shed support and so fails this guard, leaving the phase at Referendum for a retry).
        if (_federationPhase == FederationPhase.Referendum && _referendumCarried)
        {
            _federationPhase = FederationPhase.Commonwealth;
            RecordHistoryIfHuman("The Commonwealth of Australia is proclaimed. Federation is achieved!");
        }
    }

    /// <summary>Convention Points needed to complete the draft constitution (design "Draft Constitution completion"; double the call-convention gate so drafting trails calling).</summary>
    internal const int ConventionPointsToDraftConstitution = ConventionPointsToCallConvention * 2;

    private bool _referendumCarried;

    /// <summary>Records a Federation milestone in the human's history log (a no-op for an empty message).</summary>
    private void RecordHistoryIfHuman(string message)
    {
        if (message.Length > 0)
        {
            RecordHistory(HistoryEventKind.FatherElected, message);
        }
    }
}
