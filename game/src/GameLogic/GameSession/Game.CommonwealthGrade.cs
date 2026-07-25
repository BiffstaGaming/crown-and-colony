using CrownAndColony.GameLogic.Colonies;

namespace CrownAndColony.GameLogic.GameSession;

/// <summary>
/// The <b>grade</b> of a Commonwealth victory (WS3.4, design doc 05 "Optional victory grades"): federating is the win,
/// but <em>how</em> the six colonies were brought together is graded — a bare majority scraped over the line reads very
/// differently from a Commonwealth built on treaty, reform, or an integrated economy.
/// </summary>
public enum CommonwealthGrade
{
    /// <summary>No Commonwealth (a classic game, or an Australia game that has not federated). The scorecard is empty.</summary>
    None = 0,

    /// <summary>
    /// <b>Bare Federation</b> — the colonies passed, but nothing else distinguished the settlement ("legitimacy or First
    /// Nations relations poor"). The floor grade: awarded when no distinguished category reaches its bar.
    /// </summary>
    Bare = 1,

    /// <summary><b>Stable Commonwealth</b> — "strong infrastructure, low debt": low opposition, fed colonies, a solvent treasury.</summary>
    Stable = 2,

    /// <summary><b>Reform Commonwealth</b> — "women's suffrage/reform clauses active, high civic institutions": the reform Pioneers seated, newspapers and schools built.</summary>
    Reform = 3,

    /// <summary><b>Economic Commonwealth</b> — "strong intercolonial trade, rail/telegraph, export economy": a diverse export base, deep infrastructure, a full treasury.</summary>
    Economic = 4,

    /// <summary>
    /// <b>Treaty Commonwealth</b> — "strong First Nations agreements and low frontier violence". The rarest grade: it is
    /// the only one that cannot be bought with production. <b>Not awardable yet</b> — the Agreements system it rewards is
    /// Workstream 5 (unbuilt, ADR-022 consultation gated); see <see cref="Game.GradeFor"/>.
    /// </summary>
    Treaty = 5,
}

/// <summary>
/// The six-category end-of-campaign scorecard behind a <see cref="CommonwealthGrade"/> (WS3.4, design doc 20 "Victory
/// grade scoring"). Every category is an independent 0–100 reading of the final board — they are <b>not</b> weighted
/// into a single number by design, so the victory screen can show <em>where</em> the Commonwealth was strong.
/// </summary>
/// <param name="Federation">Colonies passed and by what margin (doc 20 "Number of colonies passed and margin").</param>
/// <param name="Economy">Export diversity, infrastructure and treasury (doc 20 "Export diversity, debt, infrastructure").</param>
/// <param name="CivicReform">Reform figures seated, newspapers and schools (doc 20 "Suffrage, representation, newspapers, schools").</param>
/// <param name="FirstNations">First Nations relations — interim tension proxy pending WS5 (doc 20 "Respect, agreements, low violence").</param>
/// <param name="Stability">Low opposition, food security, solvency (doc 20 "Low unrest, low debt, food security").</param>
/// <param name="HistoricalBreadth">How many of the campaign's eras were meaningfully lived through (doc 20 "Number of eras meaningfully developed").</param>
/// <param name="Grade">The grade awarded — the highest-scoring category that cleared <see cref="Game.GradeThresholdPercent"/>, else <see cref="CommonwealthGrade.Bare"/>.</param>
public sealed record CommonwealthScorecard(
    int Federation,
    int Economy,
    int CivicReform,
    int FirstNations,
    int Stability,
    int HistoricalBreadth,
    CommonwealthGrade Grade)
{
    /// <summary>The summed category score, 0–600 — a single headline figure for the end-card (the grade itself is <em>not</em> derived from this total).</summary>
    public int Total => Federation + Economy + CivicReform + FirstNations + Stability + HistoricalBreadth;
}

/// <summary>
/// WS3.4 — the five Commonwealth victory grades and the six-category scorecard they are read from (design docs 05 and
/// 20). A pure, RNG-free oracle over the final board: it persists nothing, mutates nothing, and is safe to call at any
/// time (the Federation panel may preview the running grade mid-campaign; the victory screen reads it at the end).
///
/// <para><b>Off for classic (ADR-009 byte-stability):</b> every entry point returns
/// <see cref="CommonwealthGrade.None"/> / an all-zero scorecard unless <see cref="Specification.Ruleset.VictoryFederation"/>
/// is on, so a classic game reads nothing, stores nothing and stays byte-identical.</para>
/// </summary>
public sealed partial class Game
{
    // ── Grade bar ────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The category score a distinguished grade must reach to be awarded (WS3.4). Set so a category has to be a
    /// deliberate campaign focus rather than a by-product of winning at all — a player who merely federates lands on
    /// <see cref="CommonwealthGrade.Bare"/>. First-pass and tunable, like the WS3.7 gate counts.
    /// </summary>
    internal const int GradeThresholdPercent = 70;

    // ── Category targets (first-pass, tunable; the "what does 100 look like" dials) ───────────────────────────

    /// <summary>Distinct goods types stored across the human's colonies that count as a fully diverse export base (Economy).</summary>
    private const int EconomyDiversityTarget = 10;

    /// <summary>Distinct building types built across the human's colonies that count as full infrastructure (Economy).</summary>
    private const int EconomyInfrastructureTarget = 20;

    /// <summary>The treasury that counts as a full-strength economy (Economy). Above this the sub-score saturates.</summary>
    private const int EconomyTreasuryTarget = 10_000;

    /// <summary>Newspapers active across the colonies that count as a full civic press (Civic reform) — twice the Phase-2 gate.</summary>
    private const int CivicNewspaperTarget = 4;

    /// <summary>Education buildings (schoolhouse and its upgrades) that count as full civic institutions (Civic reform).</summary>
    private const int CivicSchoolTarget = 4;

    /// <summary>Stored food a colony needs to count as food-secure (Stability).</summary>
    private const int StabilityFoodSecureStore = 100;

    /// <summary>The native-tension reading that scores 0 for First Nations relations — the incite-war alarm ceiling (<see cref="InciteWarAlarm"/>).</summary>
    private const int FirstNationsTensionCeiling = InciteWarAlarm;

    /// <summary>The college upgrade id — an education building for the Civic-reform score.</summary>
    private const string CollegeBuildingId = "model.building.college";

    /// <summary>The university upgrade id — an education building for the Civic-reform score.</summary>
    private const string UniversityBuildingId = "model.building.university";

    /// <summary>
    /// The reform Pioneers whose seats mark a <see cref="CommonwealthGrade.Reform"/> Commonwealth (docs 11/12): Catherine
    /// Helen Spence (effective voting / fair representation) and Mary Lee (South Australian women's suffrage — the 1894
    /// Act that made SA the first place in Australia where women could both vote and stand). Australia-only ids; a
    /// classic game holds neither, and the whole oracle is gated off there anyway.
    /// </summary>
    private static readonly string[] ReformFatherIds =
    [
        "model.foundingFather.catherineHelenSpence",
        "model.foundingFather.maryLee",
    ];

    // ── The oracle ───────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The human's <b>Commonwealth scorecard</b> (WS3.4): the six design-doc-20 categories, each 0–100, plus the
    /// <see cref="CommonwealthGrade"/> they award. A pure, RNG-free read of the current board — no persisted state, so
    /// it may be called mid-campaign (a running preview) or at the proclamation (the final grade). Returns an all-zero
    /// scorecard graded <see cref="CommonwealthGrade.None"/> for a classic game (ADR-009: nothing read, nothing shifted).
    /// </summary>
    public CommonwealthScorecard CommonwealthScorecardForHuman()
    {
        if (!Ruleset.VictoryFederation)
        {
            return new CommonwealthScorecard(0, 0, 0, 0, 0, 0, CommonwealthGrade.None);
        }

        int federation = FederationCategoryScore();
        int economy = EconomyCategoryScore();
        int civic = CivicReformCategoryScore();
        int firstNations = FirstNationsCategoryScore();
        int stability = StabilityCategoryScore();
        int breadth = HistoricalBreadthCategoryScore();

        CommonwealthGrade grade = GradeFor(economy, civic, firstNations, stability);
        return new CommonwealthScorecard(federation, economy, civic, firstNations, stability, breadth, grade);
    }

    /// <summary>
    /// The grade the distinguished categories award (WS3.4): the highest-scoring category at or above
    /// <see cref="GradeThresholdPercent"/> wins, and <see cref="CommonwealthGrade.Bare"/> is the floor when none clears
    /// it. Ties break toward the <b>rarer</b> achievement in the order Stable &lt; Reform &lt; Economic.
    /// <see cref="CommonwealthScorecard.Federation"/> and <see cref="CommonwealthScorecard.HistoricalBreadth"/>
    /// deliberately award no grade of their own: every winner cleared the Federation bar (that is the win), and breadth
    /// is a record of the campaign rather than a way of governing.
    ///
    /// <para><b><see cref="CommonwealthGrade.Treaty"/> is deliberately not awardable yet.</b> Its design requirement is
    /// "strong First Nations agreements and low frontier violence" — but the Agreements half of that is Workstream 5,
    /// which is unbuilt and cultural-consultation gated (ADR-022). Only the <em>absence of harm</em> can be measured
    /// today, and a player who never encountered First Nations at all scores a full
    /// <see cref="FirstNationsCategoryScore"/> — so awarding the rarest grade on that reading would hand out "relations
    /// kept intact" for having done nothing. The category is still <b>scored and shown</b> on the scorecard (an honest
    /// reading of the harm done); the grade unlocks when WS5 lands the agreements it is actually meant to reward.</para>
    /// </summary>
    private static CommonwealthGrade GradeFor(int economy, int civic, int firstNations, int stability)
    {
        _ = firstNations; // scored and displayed, but cannot award Treaty until WS5 — see the remarks above.

        // Ordered rarest-last so a >= comparison walks toward the rarer grade on a tie.
        (CommonwealthGrade Grade, int Score)[] candidates =
        [
            (CommonwealthGrade.Stable, stability),
            (CommonwealthGrade.Reform, civic),
            (CommonwealthGrade.Economic, economy),
        ];

        CommonwealthGrade best = CommonwealthGrade.Bare;
        int bestScore = GradeThresholdPercent - 1; // nothing below the bar can win
        foreach ((CommonwealthGrade grade, int score) in candidates)
        {
            if (score >= GradeThresholdPercent && score >= bestScore)
            {
                best = grade;
                bestScore = score;
            }
        }
        return best;
    }

    // ── Category scores (each a pure 0–100 read) ─────────────────────────────────────────────────────────────

    /// <summary>
    /// <b>Federation</b> (doc 20 "Number of colonies passed and margin"): the average over the six regions of that
    /// region's margin against <em>its own</em> historical target — 50 at exactly the target, rising with the surplus and
    /// falling with the shortfall, clamped 0–100. Measured on <see cref="RegionNetFederationSupport"/> (raw support less
    /// Anti-Federation Sentiment), the same net the referendum gate uses, so the score reflects the vote that was
    /// actually carried. An unsettled region scores 0 — federating requires all six, so only a mid-campaign preview
    /// ever sees that.
    /// </summary>
    private int FederationCategoryScore()
    {
        int sum = 0;
        foreach (string regionKey in FederationRegionKeys)
        {
            if (HumanColoniesInRegion(regionKey).Count == 0)
            {
                continue; // unsettled contributes 0
            }
            int margin = RegionNetFederationSupport(regionKey) - ReferendumTargetFor(regionKey);
            sum += Math.Clamp(50 + margin, 0, 100);
        }
        return sum / FederationRegionKeys.Count;
    }

    /// <summary>
    /// <b>Economy</b> (doc 20 "Export diversity, debt, infrastructure"): the mean of three sub-scores — the number of
    /// distinct goods types held across the colonies against <see cref="EconomyDiversityTarget"/>, the number of distinct
    /// building types built against <see cref="EconomyInfrastructureTarget"/> (the rail/telegraph/works axis, since the
    /// designed Federation-era buildings are ordinary buildings), and the treasury against
    /// <see cref="EconomyTreasuryTarget"/> (doc 20's "debt" axis read from the positive side — a bankrupt nation scores 0
    /// here and again on <see cref="StabilityCategoryScore"/>).
    /// </summary>
    private int EconomyCategoryScore()
    {
        List<Colony> colonies = ColoniesOf(_human).ToList();
        var goods = new HashSet<string>();
        var buildings = new HashSet<string>();
        foreach (Colony colony in colonies)
        {
            foreach ((string goodsId, int amount) in colony.Stores)
            {
                if (amount > 0)
                {
                    goods.Add(goodsId);
                }
            }
            foreach (string buildingId in colony.Buildings)
            {
                buildings.Add(buildingId);
            }
        }

        int diversity = ScaledPercent(goods.Count, EconomyDiversityTarget);
        int infrastructure = ScaledPercent(buildings.Count, EconomyInfrastructureTarget);
        int treasury = ScaledPercent(Math.Max(0, _human.Gold), EconomyTreasuryTarget);
        return (diversity + infrastructure + treasury) / 3;
    }

    /// <summary>
    /// <b>Civic reform</b> (doc 20 "Suffrage, representation, newspapers, schools"): the mean of three sub-scores — the
    /// <see cref="ReformFatherIds"/> seated in Congress (the suffrage/representation axis, since no reform
    /// <em>constitutional clause</em> exists in the curated WS3.3 catalogue yet), newspapers active against
    /// <see cref="CivicNewspaperTarget"/>, and education buildings (schoolhouse/college/university) against
    /// <see cref="CivicSchoolTarget"/>.
    /// </summary>
    private int CivicReformCategoryScore()
    {
        int reformers = ReformFatherIds.Count(id => PlayerHasFather(_human, id));
        List<Colony> colonies = ColoniesOf(_human).ToList();
        int newspapers = colonies.Count(c => c.Buildings.Contains(NewspaperBuildingId));
        int schools = colonies.Count(c =>
            c.Buildings.Contains(SchoolhouseBuildingId)
            || c.Buildings.Contains(CollegeBuildingId)
            || c.Buildings.Contains(UniversityBuildingId));

        int suffrage = ScaledPercent(reformers, ReformFatherIds.Length);
        int press = ScaledPercent(newspapers, CivicNewspaperTarget);
        int education = ScaledPercent(schools, CivicSchoolTarget);
        return (suffrage + press + education) / 3;
    }

    /// <summary>
    /// <b>First Nations relations</b> (doc 20 "Respect, agreements, low violence") — an <b>interim proxy</b>: 100 less the
    /// average tension the First Nations players hold toward the human, scaled against
    /// <see cref="FirstNationsTensionCeiling"/>. The designed Respect / Agreements / Country-Pressure axes are Workstream
    /// 5, which is cultural-consultation gated (ADR-022) and unbuilt — so this scores the one honest signal that does
    /// exist today, the inherited alarm engine, and <b>will be replaced</b> when WS5 lands. Scores a full 100 when the
    /// game has no First Nations players at all (nothing to have damaged), which is also the classic shape.
    /// </summary>
    private int FirstNationsCategoryScore()
    {
        List<Player> natives = Players.Where(p => p.PlayerType == PlayerType.Native).ToList();
        if (natives.Count == 0)
        {
            return 100;
        }
        int totalTension = natives.Sum(n => Math.Clamp(TensionBetween(n.PlayerId, _human.PlayerId), 0, FirstNationsTensionCeiling));
        int averageTension = totalTension / natives.Count;
        return 100 - ScaledPercent(averageTension, FirstNationsTensionCeiling);
    }

    /// <summary>
    /// <b>Stability</b> (doc 20 "Low unrest, low debt, food security"): the mean of three sub-scores — 100 less the
    /// average <see cref="Colony.AntiFederation"/> opposition scaled against <see cref="AntiFederationCap"/>, the share of
    /// colonies holding at least <see cref="StabilityFoodSecureStore"/> food, and solvency (a full score while the
    /// treasury is not in the red, 0 once it is — the bankruptcy the upkeep system can drive).
    /// </summary>
    private int StabilityCategoryScore()
    {
        List<Colony> colonies = ColoniesOf(_human).ToList();
        if (colonies.Count == 0)
        {
            return 0;
        }
        int averageOpposition = colonies.Sum(c => Math.Clamp(c.AntiFederation, 0, AntiFederationCap)) / colonies.Count;
        int unrest = 100 - ScaledPercent(averageOpposition, AntiFederationCap);
        int fed = ScaledPercent(colonies.Count(c => c.StoreOf(Colony.FoodId) >= StabilityFoodSecureStore), colonies.Count);
        int solvency = _human.Gold >= 0 ? 100 : 0;
        return (unrest + fed + solvency) / 3;
    }

    /// <summary>
    /// <b>Historical breadth</b> (doc 20 "Number of eras meaningfully developed"): the share of the campaign's six
    /// Australian eras (<see cref="EventEra.Survival"/> through <see cref="EventEra.Federation"/>) in which at least one
    /// historical event actually fired. A player who rushed to Federation through an empty 19th century scores low; one
    /// who lived the whole 1788–1901 arc scores high. <see cref="EventEra.Pre"/> is excluded — it is the inert band
    /// outside the Australian window.
    /// </summary>
    private int HistoricalBreadthCategoryScore()
    {
        var eras = new HashSet<EventEra>();
        foreach (int firedTurn in _eventLastFiredTurn.Values)
        {
            EventEra era = EraForYear(Ruleset.Calendar.YearForTurn(firedTurn));
            if (era != EventEra.Pre)
            {
                eras.Add(era);
            }
        }
        int australianEras = Enum.GetValues<EventEra>().Length - 1; // every era but Pre
        return ScaledPercent(eras.Count, australianEras);
    }

    /// <summary><paramref name="value"/> as a 0–100 percentage of <paramref name="target"/>, saturating at 100 (a non-positive target scores 0).</summary>
    private static int ScaledPercent(int value, int target) =>
        target <= 0 ? 0 : Math.Clamp(value * 100 / target, 0, 100);
}
