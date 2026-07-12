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

    /// <summary>
    /// The <b>uniform fallback</b> referendum bar (= <see cref="RegionSupportForReferendum"/>, 50) — a read-only oracle
    /// retained for back-compat and as the classic/no-target default. <b>WS3.2 superseded this for the panel and the gate:</b>
    /// use the per-region <see cref="ReferendumTargetFor"/> (which returns this value only for a region the ruleset authors
    /// no target for). Kept so classic — which authors no per-region targets — has a single named bar.
    /// </summary>
    public int ReferendumSupportThreshold => RegionSupportForReferendum;

    /// <summary>
    /// The <b>base</b> per-region referendum target (WS3.2, doc 05): the Federation Support percentage a settled colony
    /// region must reach before a referendum may be put. Australia authors the six historical targets as variant data
    /// (<see cref="Specification.Ruleset.FederationRegionTargets"/> — NSW 57 / Vic 94 / Qld 56 / SA 80 / Tas 94 / WA 70);
    /// a region the ruleset does not specify — and every region of a classic game, whose ruleset specifies none — falls
    /// back to the uniform <see cref="RegionSupportForReferendum"/>, so the default game is byte-identical (ADR-009).
    /// </summary>
    /// <param name="regionKey">One of <see cref="FederationRegionKeys"/> (e.g. <c>model.region.newSouthWales</c>).</param>
    private int BaseReferendumTargetFor(string regionKey) =>
        Ruleset.FederationRegionTargets.TryGetValue(regionKey, out int target) ? target : RegionSupportForReferendum;

    /// <summary>
    /// The <b>effective</b> per-region referendum target (WS3.2): the base target (<see cref="BaseReferendumTargetFor"/>)
    /// less any reduction banked by the Democracy Pioneers who lower a region's bar (Edmund Barton's New South Wales
    /// convention drive, Samuel Griffith's hardest-colony drafting, Mary Lee's South-Australian suffrage advocacy),
    /// clamped 0–100. <b>WS3.6:</b> Western Australia additionally takes a live <see cref="WaLateEntryReduction"/> (−15,
    /// 70 → 55) once its goldfields mature (<see cref="WaGoldfieldsMatured"/>) — a non-banked, non-Pioneer cut stacked on
    /// top of any banked reduction and absorbed by the same 0–100 clamp; gated on the Federation victory (classic is
    /// unaffected). This is the read the referendum gate (<see cref="CheckPutToReferendum"/>) and the Federation panel's
    /// per-region threshold marker + readiness colour consume (ADR-006). Falls back to the uniform bar for a region with
    /// no authored target (so classic — which authors none and never reaches the gate — is byte-identical, ADR-009).
    /// </summary>
    /// <param name="regionKey">One of <see cref="FederationRegionKeys"/> (e.g. <c>model.region.newSouthWales</c>).</param>
    public int ReferendumTargetFor(string regionKey)
    {
        int reduction = _federationTargetReductions.GetValueOrDefault(regionKey);
        // WS3.6: once Western Australia's goldfields mature, its join threshold drops 70 → 55 (a live reduction on top of any
        // banked Pioneer cut). Gated on the Federation victory so classic — which authors no WA target and never matures a
        // goldfields boom in this sense — is byte-identical. The 0–100 clamp absorbs any stack with a Griffith WA reduction.
        if (Ruleset.VictoryFederation && regionKey == WesternAustraliaKey && WaGoldfieldsMatured())
        {
            reduction += WaLateEntryReduction;
        }
        return Math.Clamp(BaseReferendumTargetFor(regionKey) - reduction, 0, 100);
    }

    /// <summary>The share of each turn's net Civic Voice that also accrues as national Convention Points (a light fraction so points trail support — the design keeps them a separate, slower axis).</summary>
    private const int ConventionPointsPerCivicVoicePercent = 25;

    // ── Phase-1/2 prerequisite gate counts (WS3.7, design doc 05 §"Victory phases"; Australia-only, adopted as-is). ──
    // Central + tunable; the design's fixed counts. The two "Civic Voice threshold" criteria are folded into the stronger
    // maturity/movement signals (they carry no design number and the 4-capital gate already implies large civic output).

    /// <summary>Design Phase 1 (Colonial Maturity): the number of colony regions that must hold a <see cref="SettlementMaturity.ColonialCapital"/> before a convention can be called ("at least 4 colony regions have a capital-level settlement").</summary>
    internal const int Phase1CapitalRegions = 4;

    /// <summary>Design Phase 1: the number of colony regions that must be settled ("at least 3 colony regions have Town Hall or equivalent" — every colony has the free base Town Hall, so this reads as settled-region count).</summary>
    internal const int Phase1SettledRegions = 3;

    /// <summary>Design Phase 1: the number of trade routes the human must run ("at least 2 intercolonial trade routes exist" — a raw route count; a true region-spanning "intercolonial" test has no oracle yet and is deferred).</summary>
    internal const int Phase1TradeRoutes = 2;

    /// <summary>Design Phase 2 (Federation Movement): the number of settled colony regions required ("at least 4 colony regions discovered/settled").</summary>
    internal const int Phase2SettledRegions = 4;

    /// <summary>Design Phase 2: the number of active newspapers required ("at least 2 newspapers active").</summary>
    internal const int Phase2Newspapers = 2;

    /// <summary>The Henry Parkes founding-father id — the "Federation champion attained" signal for the Phase-2 movement gate (design doc 05 Phase 2). Australia-only content; the gate is a no-op in classic.</summary>
    private const string HenryParkesFatherId = "model.foundingFather.henryParkes";

    // ── WS3.6 special referendum rules (NSW mobilisation quota + WA goldfields late-entry; all six mandatory) ─────
    // First-pass, tunable (like the WS3.7 gate counts). Both derived live from already-persisted state → no save bump.

    /// <summary>The New South Wales region key — the colony the WS3.6 mobilisation quota applies to.</summary>
    private const string NewSouthWalesKey = "model.region.newSouthWales";

    /// <summary>The Western Australia region key — the WS3.6 late-entry hold-out colony.</summary>
    private const string WesternAustraliaKey = "model.region.westernAustralia";

    /// <summary>
    /// New South Wales' <b>mobilisation quota</b> (WS3.6): the minimum total banked Federation Support (raw points, summed
    /// over the human's NSW colonies) the referendum needs on top of NSW's 57% support target. The historical 1898 NSW
    /// referendum passed on a majority but fell short of the legislated ~80,000-yes floor, forcing the 1899 re-run. A lean
    /// single NSW Colonial Capital (pop 10) at exactly 57% banks ~1140 &lt; 1200, so the quota bites until NSW's civic base
    /// grows (a second town / higher support) or the Capital Compromise clause waives it. Absolute (not %-scaled) on purpose
    /// — that IS the historical mechanic. Tunable; the load-bearing balance dial.
    /// </summary>
    internal const int NswMobilisationQuota = 1200;

    /// <summary>Western Australia's late-entry referendum-target cut (WS3.6): once WA's goldfields mature, its target drops 70 → 55 (design doc 05's "WA at least 55%"), folded live into <see cref="ReferendumTargetFor"/>.</summary>
    internal const int WaLateEntryReduction = 15;

    // ── State (persisted, SaveGame v72; omitted when default so classic stays byte-identical) ─────────────────

    private FederationPhase _federationPhase = FederationPhase.ColonialMaturity;
    private int _conventionPoints;
    private int _conventionPointsHundredths; // ephemeral sub-point carry (see AccrueFederationSupport); never persisted
    private int _referendumAttempts;

    /// <summary>
    /// Per-region referendum-<b>target</b> reductions banked by the Democracy Pioneers (WS3.2): region key → total
    /// percentage points shaved off that region's <see cref="BaseReferendumTargetFor"/> (read via
    /// <see cref="ReferendumTargetFor"/>). Empty for a classic game and for an Australia game that has elected no
    /// target-lowering Pioneer, so it is omitted from the save (byte-identical, ADR-009); a token appears only once such a
    /// Pioneer (Barton / Griffith / Mary Lee) is elected in an Australia game. Griffith's chosen region is state-dependent
    /// at election time, so the reductions genuinely must be persisted rather than re-derived. Persisted (SaveGame v73).
    /// </summary>
    private readonly Dictionary<string, int> _federationTargetReductions = new();

    /// <summary>
    /// The current stage of the Federation victory loop (Phase-4a). <see cref="FederationPhase.ColonialMaturity"/> for
    /// every classic game (which never advances it) and for an Australia game that has not yet gathered enough support;
    /// terminal at <see cref="FederationPhase.Commonwealth"/> (the human has won). Persisted (v72, omitted when the
    /// default).
    /// </summary>
    public FederationPhase FederationPhase => _federationPhase;

    /// <summary>
    /// The current player-facing <b>narrative stage</b> of the Federation campaign (WS3.8) — the design's six named phases
    /// (doc 05) mapped from the mechanical <see cref="FederationPhase"/> plus the calendar. Within
    /// <see cref="FederationPhase.ColonialMaturity"/> the stage is <see cref="FederationStage.FederationMovement"/> once the
    /// movement is under way (<see cref="FederationMovementUnderway"/> — the 1889+ Federation era OR Henry Parkes elected,
    /// WS3.7), else <see cref="FederationStage.ColonialMaturity"/>; the other mechanical states map one-to-one. A pure, RNG-free read
    /// (no persisted state) the Federation panel labels its phase tracker + era indicator from (ADR-006).
    /// <see cref="FederationStage.None"/> for a classic game (no Federation campaign), so nothing is shown there.
    /// </summary>
    public FederationStage CurrentFederationStage
    {
        get
        {
            if (!Ruleset.VictoryFederation)
            {
                return FederationStage.None; // classic — no Federation campaign
            }
            return _federationPhase switch
            {
                FederationPhase.ColonialMaturity =>
                    FederationMovementUnderway ? FederationStage.FederationMovement : FederationStage.ColonialMaturity,
                FederationPhase.ConventionCalled => FederationStage.ConventionProcess,
                FederationPhase.ConstitutionDrafted => FederationStage.DraftConstitution,
                FederationPhase.Referendum => FederationStage.Referendum,
                FederationPhase.Commonwealth => FederationStage.Commonwealth,
                _ => FederationStage.ColonialMaturity,
            };
        }
    }

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

    /// <summary>
    /// Whether the latest referendum has <b>carried</b> (Phase-4a): set the turn a referendum passes, and read by
    /// <see cref="ResolveCommonwealthFederation"/> on the next turn resolution to proclaim the Commonwealth. False for a
    /// classic game (which never holds one) and for a game whose latest referendum failed. Persisted (v72, omitted when
    /// false).
    /// </summary>
    public bool ReferendumCarried => _referendumCarried;

    /// <summary>Re-installs the restored Federation phase (save load, v72).</summary>
    internal void SetFederationPhase(FederationPhase phase) => _federationPhase = phase;

    /// <summary>Re-installs the restored Convention Points (save load, v72).</summary>
    internal void SetConventionPoints(int points) => _conventionPoints = Math.Max(0, points);

    /// <summary>Re-installs the restored referendum-attempt count (save load, v72).</summary>
    internal void SetReferendumAttempts(int attempts) => _referendumAttempts = Math.Max(0, attempts);

    /// <summary>Re-installs the restored "latest referendum carried" flag (save load, v72).</summary>
    internal void SetReferendumCarried(bool carried) => _referendumCarried = carried;

    /// <summary>
    /// Re-installs a restored per-region referendum-target reduction (save load, v73). Ignores non-positive amounts, so a
    /// classic / no-Pioneer save (which stores none) restores an empty map and stays byte-identical (ADR-009).
    /// </summary>
    /// <param name="regionKey">The region key whose target was reduced (one of <see cref="FederationRegionKeys"/>).</param>
    /// <param name="amount">The total percentage-point reduction banked against that region.</param>
    internal void SetFederationTargetReduction(string regionKey, int amount)
    {
        if (amount > 0)
        {
            _federationTargetReductions[regionKey] = amount;
        }
    }

    /// <summary>The banked per-region referendum-target reductions (WS3.2), for save serialisation. Empty unless a target-lowering Pioneer has been elected in an Australia game.</summary>
    internal IReadOnlyDictionary<string, int> FederationTargetReductions => _federationTargetReductions;

    // ── Federation-state boosts (applied by the Australian Democracy-Pioneer on-election effects, Phase-4d.7) ──

    /// <summary>
    /// Banks <paramref name="points"/> extra national <see cref="ConventionPoints"/> for the human's Federation movement
    /// (Phase-4d.7) — the lever the Democracy Pioneers who advance the constitutional groundwork pull (Edmund Barton's
    /// convention drive, Samuel Griffith's drafting boost). <b>A no-op unless the ruleset enables the Federation victory</b>
    /// (classic has none), so a classic game banks nothing and stays byte-identical (ADR-009). Negative amounts are
    /// ignored; the total is never driven below 0. RNG-free.
    /// </summary>
    /// <param name="points">Convention Points to add (values ≤ 0 are ignored).</param>
    internal void AddConventionPoints(int points)
    {
        if (!Ruleset.VictoryFederation || points <= 0)
        {
            return; // classic / non-Federation ruleset, or nothing to add — byte-identical (ADR-009)
        }
        _conventionPoints += points;
    }

    /// <summary>
    /// Adds <paramref name="support"/> Federation Support to every colony <paramref name="player"/> holds (Phase-4d.7) —
    /// the broad boost Henry Parkes' Tenterfield Oration grants across all colony regions. Each colony's banked support is
    /// still clamped to its 100%-support ceiling by <see cref="Colony.AddFederationSupport"/>. <b>A no-op unless the
    /// ruleset enables the Federation victory</b> (classic has none), so a classic game changes nothing and stays
    /// byte-identical (ADR-009). Colonies are visited in id order (deterministic); RNG-free.
    /// </summary>
    /// <param name="player">The player whose colonies gain support (the electing Pioneer's owner).</param>
    /// <param name="support">Federation Support points added to each colony.</param>
    internal void AddFederationSupportToAllColonies(Player player, int support)
    {
        if (!Ruleset.VictoryFederation || support == 0)
        {
            return; // classic / non-Federation ruleset — byte-identical (ADR-009)
        }
        foreach (Colony colony in ColoniesOf(player).OrderBy(c => c.Id))
        {
            colony.AddFederationSupport(support);
        }
    }

    /// <summary>
    /// Adds <paramref name="support"/> Federation Support to every colony <paramref name="player"/> holds that lies in one
    /// of the <b>smaller colony regions</b> — South Australia, Tasmania and Western Australia (Phase-4d.7). This is Catherine
    /// Helen Spence's "Fair Representation" lever: the effective-voting campaigner reassures the small colonies (whose fear
    /// of domination by the populous east was the real Federation obstacle) rather than brute-forcing national support. A
    /// colony's region is resolved via <see cref="GameMap.RegionOf"/>. <b>A no-op unless the ruleset enables the Federation
    /// victory</b> (classic has none), so a classic game changes nothing and stays byte-identical (ADR-009). Colonies are
    /// visited in id order (deterministic); RNG-free.
    /// </summary>
    /// <param name="player">The player whose small-region colonies gain support (the electing Pioneer's owner).</param>
    /// <param name="support">Federation Support points added to each small-region colony.</param>
    internal void AddFederationSupportToSmallColonies(Player player, int support)
    {
        if (!Ruleset.VictoryFederation || support == 0)
        {
            return; // classic / non-Federation ruleset — byte-identical (ADR-009)
        }
        foreach (Colony colony in ColoniesOf(player).OrderBy(c => c.Id))
        {
            string? regionKey = Map.RegionOf(colony.Position)?.Key;
            if (regionKey is not null && SmallFederationRegionKeys.Contains(regionKey))
            {
                colony.AddFederationSupport(support);
            }
        }
    }

    /// <summary>
    /// Adds <paramref name="support"/> Federation Support to every colony <paramref name="player"/> holds in the single
    /// region <paramref name="regionKey"/> (WS4.4 — the region-scoped Pioneer levers: George Fife Angas' South-Australian
    /// credit and Mary Lee's South-Australian suffrage advocacy both lift SA; Edmund Barton's convention drive lifts NSW).
    /// A per-colony <b>target reduction of N</b> is modelled as <b>+N support</b> to that colony — mathematically the same
    /// against the fixed support ceiling, reusing this one lever. <b>A no-op unless the ruleset enables the Federation
    /// victory</b> (classic has none) → byte-identical (ADR-009). Colonies visited in id order (deterministic); RNG-free.
    /// </summary>
    /// <param name="player">The player whose colonies in the region gain support (the electing Pioneer's owner).</param>
    /// <param name="regionKey">The <see cref="Region.Key"/> of the colony region to boost (e.g. <c>model.region.southAustralia</c>).</param>
    /// <param name="support">Federation Support points added to each colony in that region.</param>
    internal void AddFederationSupportToRegion(Player player, string regionKey, int support)
    {
        if (!Ruleset.VictoryFederation || support == 0)
        {
            return; // classic / non-Federation ruleset — byte-identical (ADR-009)
        }
        foreach (Colony colony in ColoniesOf(player).OrderBy(c => c.Id))
        {
            if (Map.RegionOf(colony.Position)?.Key == regionKey)
            {
                colony.AddFederationSupport(support);
            }
        }
    }

    /// <summary>
    /// Reduces the referendum <b>target</b> of the single region <paramref name="regionKey"/> by <paramref name="amount"/>
    /// percentage points (WS3.2 — the honest form of a Democracy Pioneer's "target −N" clause: Edmund Barton's convention
    /// drive lowers New South Wales' bar, Mary Lee's suffrage advocacy lowers South Australia's — replacing WS4.4's
    /// +support proxy). The reduction banks in <see cref="_federationTargetReductions"/> (persisted), stacks with any
    /// other, and is folded into an effective 0–100 target by <see cref="ReferendumTargetFor"/>. <b>A no-op unless the
    /// ruleset enables the Federation victory</b> (classic has none) → byte-identical (ADR-009). Non-positive amounts are
    /// ignored. RNG-free.
    /// </summary>
    /// <param name="regionKey">The <see cref="Region.Key"/> whose referendum target drops (e.g. <c>model.region.newSouthWales</c>).</param>
    /// <param name="amount">Percentage points to shave off the region's referendum target (values ≤ 0 are ignored).</param>
    internal void ReduceFederationTarget(string regionKey, int amount)
    {
        if (!Ruleset.VictoryFederation || amount <= 0)
        {
            return; // classic / non-Federation ruleset — byte-identical (ADR-009)
        }
        _federationTargetReductions[regionKey] = _federationTargetReductions.GetValueOrDefault(regionKey) + amount;
    }

    /// <summary>
    /// Reduces the referendum target of the <b>hardest settled region to carry</b> by <paramref name="amount"/> percentage
    /// points (WS3.2 — Samuel Griffith's clause: as chief drafter he wins over the most reluctant colony, "the hardest
    /// colony's target −5" — replacing WS4.4's +support proxy). The hardest region is the settled region whose support sits
    /// farthest <b>below its own target</b> (the smallest <see cref="RegionFederationSupport"/> − <see cref="ReferendumTargetFor"/>
    /// margin — the honest "hardest", replacing WS4.4's rank-by-raw-percentage); ties break by canonical
    /// <see cref="FederationRegionKeys"/> order (deterministic). The chosen region is captured now (it is state-dependent,
    /// so it must be banked, not re-derived). <b>A no-op unless the ruleset enables the Federation victory</b> (classic has
    /// none) and when the human is settled nowhere → byte-identical (ADR-009). RNG-free.
    /// </summary>
    /// <param name="amount">Percentage points to shave off the hardest region's referendum target (values ≤ 0 are ignored).</param>
    internal void ReduceHardestRegionTarget(int amount)
    {
        if (!Ruleset.VictoryFederation || amount <= 0)
        {
            return; // classic / non-Federation ruleset — byte-identical (ADR-009)
        }
        string? hardest = FederationRegionKeys
            .Where(k => HumanColoniesInRegion(k).Count > 0)
            .OrderBy(k => RegionFederationSupport(k) - ReferendumTargetFor(k))
            .FirstOrDefault();
        if (hardest is not null)
        {
            ReduceFederationTarget(hardest, amount);
        }
    }

    /// <summary>
    /// The three <b>smaller colony regions</b> of the Federation — South Australia, Tasmania and Western Australia. Their
    /// fear of being outvoted by populous New South Wales and Victoria was the design's central small-state obstacle
    /// (docs 05 / 11); Catherine Helen Spence's effect is weighted to exactly these three
    /// (<see cref="AddFederationSupportToSmallColonies"/>).
    /// </summary>
    private static readonly System.Collections.Generic.HashSet<string> SmallFederationRegionKeys =
    [
        "model.region.southAustralia",
        "model.region.tasmania",
        "model.region.westernAustralia",
    ];

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
            // Convention Points trail support: a light fraction of each turn's positive net Civic Voice, national-scope.
            // A hundredths accumulator carries the sub-point remainder forward so a low-output colony (whose net×25 is
            // under 100) still accrues over time rather than truncating every turn to nothing — integer-only, so the
            // whole loop stays deterministic (ADR-009). The remainder is ephemeral (not persisted): it is only ever
            // touched by an Australia game (this method returns early in classic), so it never shifts the classic soak,
            // and losing at most a fraction of a point across a save/reload is immaterial to the slow points axis.
            _conventionPointsHundredths += netCivicVoice * ConventionPointsPerCivicVoicePercent;
            _conventionPoints += _conventionPointsHundredths / 100;
            _conventionPointsHundredths %= 100;
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

    // ── WS3.6 — NSW mobilisation quota + WA goldfields late-entry (derived live; RNG-free) ─────────────────────

    /// <summary>
    /// Whether Western Australia's <b>goldfields have matured</b> enough for WA to join the Federation (WS3.6): the human
    /// holds a <see cref="SettlementMaturity.ColonialCapital"/> — or at least two <see cref="SettlementMaturity.ColonialTown"/>s
    /// (a capital counts as a town) — among its WA colonies. Historically WA was dragged into the Commonwealth as an
    /// <b>original state</b> by its 1890s gold rush (the Kalgoorlie/Coolgardie "t'othersider" agitation for Federation, the
    /// "Auralia" separation threat), not by the eastern colonies acceding it later — so WA's late entry keys on its own
    /// development, not an elapsed clock. Mirrors the <see cref="IsColonyRegionActive"/> activation rule, human-scoped. A
    /// pure, RNG-free read; false in classic (no WA colonies) and until WA's goldfields boom.
    /// </summary>
    private bool WaGoldfieldsMatured()
    {
        List<Colony> wa = HumanColoniesInRegion(WesternAustraliaKey);
        int capitals = wa.Count(c => SettlementMaturityOf(c) == SettlementMaturity.ColonialCapital);
        int towns = wa.Count(c => SettlementMaturityOf(c) is SettlementMaturity.ColonialTown or SettlementMaturity.ColonialCapital);
        return capitals >= 1 || towns >= 2;
    }

    /// <summary>
    /// New South Wales' total <b>mobilisation</b> (WS3.6): the sum of raw banked <see cref="Colony.FederationSupport"/> over
    /// the human's NSW colonies — the absolute "turnout" stock the <see cref="NswMobilisationQuota"/> tests (the historical
    /// 1898 quota was an absolute minimum-YES-vote floor, not a percentage). Raw points, so Anti-Federation Sentiment (which
    /// only bites at the percentage aggregation) never touches it — the quota and the support target stay orthogonal.
    /// </summary>
    private int NswMobilisation() => HumanColoniesInRegion(NewSouthWalesKey).Sum(c => c.FederationSupport);

    /// <summary>
    /// Whether New South Wales would <b>clear its mobilisation quota</b> if a referendum were held now (WS3.6, advisory) —
    /// the panel telegraphs the historical 1898 quota hurdle so it is not a gotcha. <b>No</b> when the human holds NSW but
    /// its mobilisation is below <see cref="NswMobilisationQuota"/> and the Capital Compromise waiver is not drafted;
    /// <b>Yes</b> otherwise. Gated on the Federation victory (never blocks in classic). A pure, RNG-free read.
    /// </summary>
    public MoveCheck CheckNswReferendumQuota()
    {
        if (!Ruleset.VictoryFederation || HumanColoniesInRegion(NewSouthWalesKey).Count == 0)
        {
            return MoveCheck.Yes(0); // not applicable → never blocks
        }
        if (_draftedClauses.Contains(CapitalCompromiseClauseId) || NswMobilisation() >= NswMobilisationQuota)
        {
            return MoveCheck.Yes(0);
        }
        return MoveCheck.No(
            $"New South Wales has the numbers but not the mobilisation quota ({NswMobilisation()}/{NswMobilisationQuota}) — build its civic base or agree the Capital Compromise, or its referendum will fall short.");
    }

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
    /// Whether the <b>Federation movement is under way</b> (WS3.7, design doc 05 Phase 2's first criterion): the year has
    /// reached the 1889+ Federation era (<see cref="CurrentEventEra"/> = <see cref="EventEra.Federation"/>) OR the human has
    /// elected Henry Parkes (the Tenterfield Oration champion). The signal that splits the narrative
    /// <see cref="FederationStage.ColonialMaturity"/> from <see cref="FederationStage.FederationMovement"/> and forms the
    /// first clause of <see cref="CheckFederationMovement"/>. (The design's third OR-branch — "Civic Voice reaches a high
    /// threshold" — carries no design number and is folded away; the year/Parkes signals already open the movement.)
    /// </summary>
    private bool FederationMovementUnderway =>
        CurrentEventEra == EventEra.Federation || PlayerHasFather(_human, HenryParkesFatherId);

    /// <summary>
    /// Whether the colonies have reached <b>Colonial Maturity</b> — design Phase 1 (WS3.7): at least
    /// <see cref="Phase1CapitalRegions"/> colony regions hold a <see cref="SettlementMaturity.ColonialCapital"/>, at least
    /// <see cref="Phase1SettledRegions"/> regions are settled, and the human runs at least <see cref="Phase1TradeRoutes"/>
    /// trade routes. A pure, RNG-free read; <b>a strict No for classic</b> (the Federation victory is off), so it never
    /// affects the default game (ADR-009). Returns a clause-specific reason so the panel can show which prerequisite blocks.
    /// </summary>
    public MoveCheck CheckColonialMaturity()
    {
        if (!Ruleset.VictoryFederation)
        {
            return MoveCheck.No("Federation is not this game's victory path.");
        }
        int capitalRegions = FederationRegionKeys.Count(k =>
            HumanColoniesInRegion(k).Any(c => SettlementMaturityOf(c) == SettlementMaturity.ColonialCapital));
        if (capitalRegions < Phase1CapitalRegions)
        {
            return MoveCheck.No($"At least {Phase1CapitalRegions} colony regions need a Colonial Capital ({capitalRegions} so far).");
        }
        if (SettledRegionCount < Phase1SettledRegions)
        {
            return MoveCheck.No($"At least {Phase1SettledRegions} colony regions must be settled.");
        }
        if (_human.TradeRoutes.Count < Phase1TradeRoutes)
        {
            return MoveCheck.No($"At least {Phase1TradeRoutes} intercolonial trade routes are needed.");
        }
        return MoveCheck.Yes(0);
    }

    /// <summary>
    /// Whether the <b>Federation movement</b> has taken hold — design Phase 2 (WS3.7): the movement is under way
    /// (<see cref="FederationMovementUnderway"/> — 1889+ or Parkes), at least <see cref="Phase2SettledRegions"/> colony
    /// regions are settled, and at least <see cref="Phase2Newspapers"/> newspapers are active. A pure, RNG-free read;
    /// <b>a strict No for classic</b>, so the default game is unaffected (ADR-009). Clause-specific reasons.
    /// </summary>
    public MoveCheck CheckFederationMovement()
    {
        if (!Ruleset.VictoryFederation)
        {
            return MoveCheck.No("Federation is not this game's victory path.");
        }
        if (!FederationMovementUnderway)
        {
            return MoveCheck.No("The Federation movement has not yet stirred (needs 1889, or Henry Parkes).");
        }
        if (SettledRegionCount < Phase2SettledRegions)
        {
            return MoveCheck.No($"At least {Phase2SettledRegions} colony regions must be settled.");
        }
        int newspapers = ColoniesOf(_human).Count(c => c.Buildings.Contains(NewspaperBuildingId));
        if (newspapers < Phase2Newspapers)
        {
            return MoveCheck.No($"At least {Phase2Newspapers} newspapers must be active.");
        }
        return MoveCheck.Yes(0);
    }

    /// <summary>The number of the six Federation regions the human holds at least one colony in (WS3.7 gate helper).</summary>
    private int SettledRegionCount => FederationRegionKeys.Count(k => HumanColoniesInRegion(k).Count > 0);

    /// <summary>
    /// Whether the human may <b>call a federation convention</b> right now (Phase-4a / WS3.7): the Federation victory is
    /// enabled, the phase is still <see cref="FederationPhase.ColonialMaturity"/>, the design's <b>Phase-1 Colonial
    /// Maturity</b> and <b>Phase-2 Federation Movement</b> prerequisites are met (<see cref="CheckColonialMaturity"/> /
    /// <see cref="CheckFederationMovement"/>), at least <see cref="RegionsToCallConvention"/> of the six regions are above
    /// <see cref="RegionSupportThreshold"/> support, and at least <see cref="ConventionPointsToCallConvention"/> Convention
    /// Points have accrued (design Phase 3). A pure, RNG-free read the panel gates its "call convention" action on; the
    /// earliest-unmet prerequisite is surfaced first.
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
        // WS3.7: the colonies must have matured (Phase 1) and the movement must have begun (Phase 2) before a convention.
        MoveCheck maturity = CheckColonialMaturity();
        if (!maturity.Allowed)
        {
            return maturity;
        }
        MoveCheck movement = CheckFederationMovement();
        if (!movement.Allowed)
        {
            return movement;
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
        RecordFederationMilestone("The federation convention has been called.");
        return true;
    }

    /// <summary>
    /// Whether the human may <b>put Federation to a referendum</b> right now (Phase-4a/WS3.2): the Federation victory is
    /// enabled, the constitution has been drafted (phase <see cref="FederationPhase.ConstitutionDrafted"/> or a prior
    /// referendum has failed leaving the phase at <see cref="FederationPhase.Referendum"/>), and every one of the six
    /// regions in which the human holds a colony has reached <b>its own historical target</b> on <b>net</b> support
    /// (<see cref="RegionNetFederationSupport"/> — raw earned support less Anti-Federation Sentiment, WS3.5; opposition bites
    /// here, at the vote) versus <see cref="ReferendumTargetFor"/> — NSW 57 / Vic 94 / Qld 56 / SA 80 / Tas 94 / WA 70 for
    /// Australia; the uniform <see cref="RegionSupportForReferendum"/> as a fallback), design Phase 5. A pure, RNG-free read
    /// the panel gates its "put to referendum" action on.
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
        // Every region the human is settled in must have reached its own referendum target — measured on NET support
        // (raw earned support less Anti-Federation Sentiment, WS3.5): opposition bites here, at the vote. A region with no
        // human colony is not put. Report the region furthest below its target (the one blocking the vote) with its concrete
        // numbers — the panel's per-region gauges + opposition overlay show which colony it is.
        // WS3.6 — all six colonies are mandatory (Chris's decision: no 5-colony victory). Every one of the six regions must
        // be founded before Federation can be put to the vote — otherwise a player could simply never settle Western
        // Australia (or Tasmania) to dodge its steep target. Reported first, with how many regions remain unsettled.
        List<string> settledRegions = FederationRegionKeys.Where(k => HumanColoniesInRegion(k).Count > 0).ToList();
        if (settledRegions.Count < FederationRegionKeys.Count)
        {
            int unsettled = FederationRegionKeys.Count - settledRegions.Count;
            return MoveCheck.No($"All six colonies must be founded to federate ({unsettled} colony region{(unsettled == 1 ? "" : "s")} still unsettled).");
        }
        // WS3.6 — Western Australia holds out (all six are mandatory, so its refusal blocks the whole national vote) until
        // its goldfields grow into an established colony (the historical goldfields-pressure that carried WA in as an
        // original state). Headlines while WA is immature, before the per-region support shortfall.
        if (HumanColoniesInRegion(WesternAustraliaKey).Count > 0 && !WaGoldfieldsMatured())
        {
            return MoveCheck.No("Western Australia is holding out — its goldfields must grow into an established colony before it will join the Federation.");
        }
        string? shortfall = settledRegions
            .Where(k => RegionNetFederationSupport(k) < ReferendumTargetFor(k))
            .OrderBy(k => RegionNetFederationSupport(k) - ReferendumTargetFor(k))
            .FirstOrDefault();
        if (shortfall is not null)
        {
            return MoveCheck.No(
                $"Every settled colony must reach its Federation Support target (one needs {ReferendumTargetFor(shortfall)}%, at {RegionNetFederationSupport(shortfall)}% after opposition).");
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

        // WS3.6 — New South Wales mobilisation quota (the historical 1898→1899 re-run): NSW can pass its 57% support target
        // yet fall short of the legislated minimum-YES quota. If so, the referendum fails on NSW's numbers alone — a
        // DETERMINISTIC pre-empt before the RNG roll (no chance involved). Drafting the Capital Compromise clause (the 1899
        // concessions) waives it. Chris's "the quota-failure costs something" decision: a quota failure also spikes a small,
        // NSW-only, DECAYING Anti-Federation Sentiment (unlike a genuine roll rejection, it is a turnout technicality, so the
        // spike is smaller and confined to NSW — no nationwide drain). Reported ahead of time by CheckNswReferendumQuota.
        if (HumanColoniesInRegion(NewSouthWalesKey).Count > 0
            && NswMobilisation() < NswMobilisationQuota
            && !_draftedClauses.Contains(CapitalCompromiseClauseId))
        {
            _referendumAttempts++;
            foreach (Colony nswColony in HumanColoniesInRegion(NewSouthWalesKey))
            {
                nswColony.AddAntiFederation(NswQuotaFailureSpike, AntiFederationCap);
            }
            RecordFederationMilestone("New South Wales carried on numbers but fell short of the required quota — a second referendum is needed.");
            return false;
        }

        // A dedicated referendum generator seeded off the human's own RNG state — never advancing stream 0 (we read its
        // saved state word read-only). The attempt count mixes in so a retry rolls independently.
        ulong baseState = _random.SaveState().State;
        var rng = new Pcg32Random(baseState ^ ((ulong)Turn << 5) ^ ((ulong)(_referendumAttempts + 1) << 33), FederationStreamId);

        // The vote carries on a 0–99 roll under the average settled-region support (a stronger movement is likelier to
        // pass): support 100 always carries, support 0 never does. John Quick's "Corowa Plan" (Phase-4d.7) lowers the
        // effective pass threshold — the elected-delegates process he designed makes a marginal referendum likelier to
        // carry — by adding a fixed bonus to the support the roll is compared against (clamped to 100). The bonus rides
        // the persisted Congress (no new save state), and only ever applies to an Australia game (the classic path never
        // reaches HoldReferendum), so the default game is byte-identical (ADR-009).
        int averageSupport = Math.Min(100, AverageSettledSupport() + ReferendumThresholdRelief());
        int roll = rng.Next(100);
        _referendumAttempts++;

        if (roll < averageSupport)
        {
            _referendumCarried = true; // ResolveCommonwealthFederation proclaims the Commonwealth on the next EndTurn resolution
            RecordFederationMilestone("The Federation referendum has carried.");
            return true;
        }

        // Failure (design Phase 6): anti-Federation momentum — a one-off Anti-Federation Sentiment spike on every human
        // colony (WS3.5), so the movement loses effective ground and must rebuild before it can retry. This formalises the
        // retired permanent ~10% support shed as a DECAYING drag (Chris's "decaying spike only" decision — faithful to the
        // design's "temporary anti-Federation momentum"): the spike erodes over time (faster with Quick/Barton seated), so
        // repeated failures don't permanently scar the banked support. Opposition bites at the next referendum's net support.
        foreach (Colony colony in ColoniesOf(_human).ToList())
        {
            colony.AddAntiFederation(FailedReferendumSpike, AntiFederationCap);
        }
        RecordFederationMilestone("The Federation referendum has failed. The movement must rebuild support.");
        return false;
    }

    /// <summary>
    /// John Quick's "Corowa Plan" referendum relief (Phase-4d.7): the extra Federation Support added to the referendum
    /// roll's threshold when the human's Congress holds Quick (<see cref="QuickCorowaAbility"/>), otherwise 0. Reads the
    /// persisted Congress via <see cref="HasAbilityFor"/>, so it needs no new save state and only fires for an Australia
    /// game that has elected Quick — the classic path never reaches the referendum, so the default game is byte-identical.
    /// </summary>
    private int ReferendumThresholdRelief() =>
        HasAbilityFor(_human, QuickCorowaAbility) ? QuickReferendumRelief : 0;

    /// <summary>The unweighted average <b>net</b> Federation Support (raw less Anti-Federation Sentiment, WS3.5) across the regions the human is settled in — the strength the referendum roll is compared against (0 when unsettled). Equals the raw average when no opposition has accrued.</summary>
    private int AverageSettledSupport()
    {
        List<int> settled = FederationRegionKeys
            .Where(k => HumanColoniesInRegion(k).Count > 0)
            .Select(RegionNetFederationSupport)
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

        // Convention called → draft the constitution once the drafted clauses reach the design's ≥80% completion gate
        // (WS3.3; `ConstitutionProgressPercent` sums the drafted clause weights + Griffith's derived +30%). Replaces the
        // old flat Convention-Points gate — the player now drafts clauses in `Game.Constitution.cs`.
        if (_federationPhase == FederationPhase.ConventionCalled
            && ConstitutionProgressPercent >= ConstitutionDraftThresholdPercent)
        {
            _federationPhase = FederationPhase.ConstitutionDrafted;
            RecordFederationMilestone("The draft constitution is complete.");
        }

        // A carried referendum flags the win: the phase is at Referendum and _referendumCarried is set. A failed referendum
        // leaves _referendumCarried false (it instead spikes decaying Anti-Federation Sentiment, WS3.5), so this guard is
        // skipped and the phase stays at Referendum for a retry.
        if (_federationPhase == FederationPhase.Referendum && _referendumCarried)
        {
            _federationPhase = FederationPhase.Commonwealth;
            RecordFederationMilestone("The Commonwealth of Australia is proclaimed. Federation is achieved!");
        }
    }

    private bool _referendumCarried;

    /// <summary>
    /// Records a Federation milestone (convention called, constitution drafted, referendum carried/failed, Commonwealth
    /// proclaimed) in the human's history log as a <see cref="HistoryEventKind.HistoricalEvent"/> (a no-op for an empty
    /// message). Only ever reached on the human's Federation path (the loop is a human-only win), so the entry lands in
    /// the human's log; a classic game never calls it.
    /// </summary>
    private void RecordFederationMilestone(string message)
    {
        if (message.Length > 0)
        {
            RecordHistory(HistoryEventKind.HistoricalEvent, message);
        }
    }
}
