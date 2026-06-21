using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.Combat;
using CrownAndColony.GameLogic.GameSession.Diplomacy;
using CrownAndColony.GameLogic.Natives;
using CrownAndColony.GameLogic.Randomness;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Trade;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using CrownAndColony.GameLogic.World.Improvements;

namespace CrownAndColony.GameLogic.GameSession;

/// <summary>
/// One running game: the map, the units, exploration state, and the turn counter.
/// All mutations of game state go through methods on this class so rules are
/// enforced in one place.
/// </summary>
public sealed partial class Game
{
    /// <summary>The starting unit's type for a new game.</summary>
    public const string StartingUnitTypeId = "model.unit.freeColonist";

    /// <summary>The native warrior unit type spawned to garrison native settlements (FreeCol <c>model.unit.brave</c>).</summary>
    public const string BraveUnitTypeId = "model.unit.brave";

    /// <summary>The treasure-train unit spawned when a sacked settlement (or a lost-city find) yields treasure (FreeCol <c>model.unit.treasureTrain</c>).</summary>
    public const string TreasureTrainUnitTypeId = "model.unit.treasureTrain";

    /// <summary>
    /// Braves spawned per native settlement (Phase 5 slice 5b — a documented simplification; FreeCol
    /// scales a settlement's military strength to its size). They are native-owned defenders.
    /// </summary>
    private const int BravesPerSettlement = 1;

    /// <summary>Default colony names, used in founding order (nation-specific lists come with nations).</summary>
    private static readonly string[] ColonyNames =
    [
        "Jamestown", "Plymouth", "Boston", "New Amsterdam", "Charlesfort",
        "Salem", "Penobscot", "Roanoke", "New Haven", "Providence",
    ];

    /// <summary>The warehouse goods id for liberty bells.</summary>
    private const string BellsId = "model.goods.bells";

    /// <summary>The terrain a ship sets sail to Europe from (the map's outer edge).</summary>
    private const string HighSeasId = "model.tile.highSeas";

    /// <summary>The warehouse goods id for religious crosses (immigration points).</summary>
    private const string CrossesId = "model.goods.crosses";

    /// <summary>Immigration points needed for the first emigrant (spec <c>model.option.initialImmigration</c>, classic 15).</summary>
    public const int InitialImmigration = 15;

    /// <summary>Immigration lost per person idling in Europe each turn (spec <c>europeanUnitImmigrationPenalty</c>, classic −4).</summary>
    public const int EuropeUnitImmigrationPenalty = -4;

    /// <summary>Flat immigration a colonial player gains each turn (spec <c>playerImmigrationBonus</c>, classic +2).</summary>
    public const int PlayerImmigrationBonus = 2;

    /// <summary>Recruit slots on the Europe dock (FreeCol <c>MigrationType.MIGRANT_COUNT</c>).</summary>
    public const int RecruitSlots = 3;

    /// <summary>Starting base recruit price (FreeCol <c>Europe.RECRUIT_PRICE_INITIAL</c>, classic 200).</summary>
    public const int InitialRecruitPrice = 200;

    /// <summary>Recruit-price floor (FreeCol <c>Europe.LOWER_CAP_INITIAL</c>, classic 80).</summary>
    public const int InitialRecruitLowerCap = 80;


    /// <summary>
    /// RNG stream id for native settlement placement (ADR-009). A separate stream
    /// from the main game (stream 0) keeps placement deterministic without shifting
    /// the economy/father/immigration draws.
    /// </summary>
    private const ulong NativeStreamId = 1;

    /// <summary>RNG stream reserved for Lost City Rumour placement — a high id above every per-player stream
    /// (<c>Player.RngStreamId</c> = playerId + 1) so rumour scatter never correlates with or shifts another stream.</summary>
    private const ulong LcrStreamId = 100;

    /// <summary>RNG stream reserved for rolling each bonus resource's starting quantity (86d3c9wbp) — a high id like
    /// <see cref="LcrStreamId"/>, so it never correlates with or shifts the human's economy stream 0 (ADR-009).</summary>
    private const ulong ResourceQuantityStreamId = 102;

    private readonly List<Unit> _units = [];
    private readonly List<Colony> _colonies = [];
    private readonly List<NativeSettlement> _nativeSettlements = [];
    private readonly List<Player> _players = [];
    private readonly List<CombatNotice> _combatNotices = []; // transient: the most recent turn's AI-vs-human raids (not saved)
    private readonly List<ColonyLossNotice> _colonyLossNotices = []; // transient: the most recent turn's AI captures of human colonies (not saved)
    private readonly List<ColonyRaidNotice> _colonyRaidNotices = []; // transient: the most recent turn's native pillages of human colonies (not saved)
    private readonly List<ColonyGiftNotice> _colonyGiftNotices = []; // transient: the most recent turn's friendly native gifts to human colonies (not saved)
    private readonly List<CustomHouseSaleNotice> _customHouseSaleNotices = []; // transient: the most recent turn's custom-house auto-sales from human colonies (not saved)
    private readonly List<RumourNotice> _rumourNotices = []; // transient: Lost City Rumours the human resolved this turn (non-mounds outcomes; not saved)
    private NativeDemand? _pendingDemand; // transient: a native tribute demand awaiting the human's accept/refuse (not saved)
    private PendingMoundsDecision? _pendingMounds; // transient: a strange-mounds rumour awaiting the human's investigate/decline (not saved)
    private FountainResult _lastFountainResult; // transient: how the most recent FoY burst was handled — picks the player-facing message in ExploreRumour
    private readonly Player _human;
    private readonly Pcg32Random _random;
    private int _nextUnitId = 1;
    private int _nextColonyId = 1;
    private int _nextSettlementId = 1;
    private int _currentPlayerIndex; // whose turn it is in the ring (the human, index 0, between turns)

    private Game(Ruleset ruleset, GameMap map, Pcg32Random random, int turn, Player human)
    {
        Ruleset = ruleset;
        DifficultyLevelId = ruleset.DifficultyLevelId; // the loaded level; New/Restore may override the persisted tag
        Map = map;
        _random = random;
        Turn = turn;
        _human = human;
        _players.Add(human);
    }

    /// <summary>The rule data this game plays by.</summary>
    public Ruleset Ruleset { get; }

    /// <summary>
    /// The spec id of the difficulty level this game plays under (e.g. <c>model.difficulty.medium</c>), persisted in
    /// the save so it reloads under the same balance (86d3c9y08). Defaults to the ruleset's loaded level
    /// (<see cref="Ruleset.DifficultyLevelId"/>) — set by <see cref="New"/> / <see cref="Restore"/>.
    /// </summary>
    public string DifficultyLevelId { get; private init; }

    /// <summary>The game world.</summary>
    public GameMap Map { get; }

    /// <summary>Current turn number, starting at 1.</summary>
    public int Turn { get; private set; }

    /// <summary>
    /// The in-game year of the current turn (turn 1 = 1492), per the ruleset <see cref="Specification.Calendar"/>.
    /// One turn per year until 1600, then two turns per year.
    /// </summary>
    public int CurrentYear => Ruleset.Calendar.YearForTurn(Turn);

    /// <summary>
    /// The 0-based season of the current turn (0 = Spring, 1 = Autumn in classic), or <c>-1</c> in the
    /// one-turn-per-year era before 1600 (no season).
    /// </summary>
    public int CurrentSeason => Ruleset.Calendar.SeasonForTurn(Turn);

    /// <summary>
    /// The calendar label for the current turn — a bare year (<c>"1492"</c>) before 1600, or a season-prefixed
    /// year (<c>"Spring 1600"</c>) afterwards. The HUD shows this in place of a bare turn counter.
    /// </summary>
    public string CalendarLabel => Ruleset.Calendar.Label(Turn);

    /// <summary>
    /// How custom houses decide what to auto-sell (a game-wide play preference; <see cref="GameSession.AutoExportMode.PerGood"/>
    /// by default — opt-in per good, faithful to FreeCol). Set via <see cref="SetAutoExportMode"/> (a settings hook; UI deferred).
    /// </summary>
    public AutoExportMode AutoExportMode { get; private set; } = AutoExportMode.PerGood;

    /// <summary>Sets the game-wide custom-house auto-export mode (a settings operation; persisted in the save).</summary>
    public void SetAutoExportMode(AutoExportMode mode) => AutoExportMode = mode;

    /// <summary>All players in the game (the human today; foreign powers and natives join from FP-3).</summary>
    public IReadOnlyList<Player> Players => _players;

    /// <summary>The local human player. Player-scoped state is reached through here, never by list index (ADR-019).</summary>
    public Player HumanPlayer => _human;

    /// <summary>The player whose turn it currently is (the human between turns; the ring advances during <see cref="EndTurn"/>).</summary>
    public Player CurrentPlayer => _players[_currentPlayerIndex];

    /// <summary>The next index in the player ring after <paramref name="index"/> (wraps to the start).</summary>
    private int NextPlayerIndex(int index) => (index + 1) % _players.Count;

    // ===== Owner model (FP-2, ADR-019): the human-vs-native binary generalises to owner-inequality + stance.
    // In FP-2 the only colonial player is the human, so these are behaviourally identical to the old !IsNative
    // tests; they install the seam so foreign colonial powers (FP-3b) slot in without re-touching these rules.

    /// <summary>The player with the given id, or null.</summary>
    private Player? PlayerById(int id) => _players.FirstOrDefault(p => p.PlayerId == id);

    /// <summary>The RNG a player draws from: the human uses the game's main stream 0; other players use their own (ADR-009).</summary>
    private IGameRandom RandomFor(Player player) => player.IsHuman ? _random : player.Rng!;

    /// <summary>Whether <paramref name="unit"/> is owned by the given colonial player (not a native).</summary>
    private static bool IsOwnedBy(Unit unit, Player player) => unit.OwnerNationId is null && unit.OwnerId == player.PlayerId;

    /// <summary>Whether <paramref name="unit"/> is owned by the human colonial player.</summary>
    private bool IsHumanOwned(Unit unit) => IsOwnedBy(unit, _human);

    /// <summary>Whether <paramref name="colony"/> is owned by the human colonial player.</summary>
    private bool IsHumanOwned(Colony colony) => colony.OwnerId == _human.PlayerId;

    /// <summary>Two units share an owner iff the same native nation, or the same colonial player.</summary>
    private static bool SameOwner(Unit a, Unit b) => a.OwnerNationId == b.OwnerNationId && a.OwnerId == b.OwnerId;

    /// <summary>
    /// Whether two units are combat/fog enemies. A shared owner is never an enemy. Beyond that the rule splits by
    /// player kind (86d3drn45, gating combat legality on <see cref="Stance"/>):
    /// <list type="bullet">
    /// <item>If <b>either</b> unit is native, owner-inequality alone decides — natives are not on the colonial stance
    /// system but on per-settlement alarm (<see cref="NativeSettlement.Alarm"/>), so a brave and a colonist are
    /// enemies exactly as before. This keeps native raids / colonist-vs-brave combat unaffected.</item>
    /// <item>If <b>both</b> are colonial powers, the pair is an enemy only at <see cref="Stance.War"/> — or while
    /// <see cref="Stance.Uncontacted"/>, the FreeCol edge case where the two haven't formally met (the human starts
    /// Uncontacted with every rival, and an attack-while-uncontacted is what declares the war). At
    /// <see cref="Stance.Peace"/>, <see cref="Stance.CeaseFire"/> or <see cref="Stance.Alliance"/> they are <b>not</b>
    /// enemies: an attack is rejected and neither may move into the other's tile until war is declared.</item>
    /// </list>
    /// Used by <see cref="DefenderAt"/> (and through it <see cref="CheckAttack"/>/<see cref="CheckMove"/>), so making
    /// it stance-aware is the single seam that stops a power attacking — or being attacked at — peace. Pure; no RNG.
    /// </summary>
    private bool AreEnemies(Unit a, Unit b)
    {
        if (SameOwner(a, b))
        {
            return false;
        }
        // A native on either side is off the colonial stance system: owner-inequality decides (raids unaffected).
        if (a.OwnerNationId is not null || b.OwnerNationId is not null)
        {
            return true;
        }
        // Two distinct colonial powers: hostile only at War, or while still Uncontacted (the FreeCol edge case where
        // the attack itself makes first contact + declares the war). Peace/CeaseFire/Alliance are NOT hostile.
        Stance stance = StanceBetween(a.OwnerId, b.OwnerId);
        return stance is Stance.War or Stance.Uncontacted;
    }

    // ===== Diplomacy (FP-6a, ADR-019): colonial-player ↔ colonial-player stance + tension, RECORDED only.
    // Each player holds its own directional view (FreeCol Player.stance/tension maps). Natives stay on the
    // per-settlement alarm system; native player ids are silently ignored here. No path draws RNG.

    /// <summary>Maximum tension (FreeCol <c>Tension.Level.HATEFUL.limit + 100</c>); mirrors the native-alarm scale.</summary>
    internal const int MaxTension = 1100;

    /// <summary>Tension added to a colonial pair by an act of war — the FreeCol WAR modifier (<c>HATEFUL.limit</c>).</summary>
    internal const int TensionWar = 1000;

    /// <summary>
    /// Per-turn territorial tension a foreign power gains toward the human for each human colony encroaching near one
    /// of its own colonies (FreeCol <c>Tension.TENSION_ADD_LAND_TAKEN</c> = 200, the land-encroachment modifier). This
    /// is the non-attack tension source that makes <see cref="StanceFromTension"/>'s Peace→War branch reachable in
    /// normal play (86d3c9udb): without it colonial tension only ever rose on an attack, which already set War directly.
    /// </summary>
    internal const int TensionLandTaken = 200;

    /// <summary>Chebyshev range, in tiles from a foreign power's colony, within which a human colony counts as territorial encroachment — the colony's 3×3 work footprint (radius 1) plus a 2-tile buffer (cf. <see cref="NativeAlarmRadius"/>, the native-alarm footprint). A colony carries no settlement type, so this is a flat constant rather than a per-colony claimable radius.</summary>
    private const int ColonialTensionRadius = 3;

    // FreeCol Tension.Level limits + DELTA, used by the tension→stance machine (Stance.getStanceFromTension).
    private const int TensionContentLimit = 600; // CONTENT band
    private const int TensionHappyLimit = 100;   // HAPPY band
    private const int TensionDelta = 10;         // hysteresis

    /// <summary>
    /// The stance a colonial pair should hold given the <paramref name="current"/> stance and its
    /// <paramref name="tension"/> — FreeCol <c>Stance.getStanceFromTension</c> (DELTA hysteresis): a war cools to
    /// <see cref="Stance.CeaseFire"/> at ≤ 590, a cease-fire warms to <see cref="Stance.Peace"/> at ≤ 90, and
    /// peace flares to <see cref="Stance.War"/> above 1010. <see cref="Stance.Uncontacted"/> never changes here
    /// (only first contact promotes it). Returns <paramref name="current"/> when no threshold is crossed.
    /// </summary>
    internal static Stance StanceFromTension(Stance current, int tension) => current switch
    {
        Stance.War when tension <= TensionContentLimit - TensionDelta => Stance.CeaseFire,
        Stance.CeaseFire when tension <= TensionHappyLimit - TensionDelta => Stance.Peace,
        Stance.CeaseFire or Stance.Peace when tension > TensionWar + TensionDelta => Stance.War,
        _ => current,
    };

    /// <summary><paramref name="a"/>'s diplomatic stance toward <paramref name="b"/> (their <see cref="Player.PlayerId"/>s); <see cref="Stance.Uncontacted"/> if unrecorded or either is non-colonial.</summary>
    public Stance StanceBetween(int a, int b) =>
        PlayerById(a) is { } pa ? pa.Stances.GetValueOrDefault(b) : Stance.Uncontacted;

    /// <summary><paramref name="a"/>'s tension toward <paramref name="b"/> (0 if unrecorded).</summary>
    public int TensionBetween(int a, int b) =>
        PlayerById(a) is { } pa ? pa.Tensions.GetValueOrDefault(b) : 0;

    /// <summary>Whether the player with this id exists and is a colonial power (diplomacy only tracks colonial pairs).</summary>
    private bool IsColonialPlayer(int id) => PlayerById(id) is { PlayerType: PlayerType.Colonial };

    /// <summary>
    /// Records <paramref name="a"/>'s stance toward <paramref name="b"/> (and, when <paramref name="symmetric"/>,
    /// <paramref name="b"/>'s toward <paramref name="a"/>). A no-op unless both are distinct colonial players.
    /// </summary>
    internal void SetStance(int a, int b, Stance stance, bool symmetric = true)
    {
        if (a == b || !IsColonialPlayer(a) || !IsColonialPlayer(b))
        {
            return;
        }
        // A transition *into* war that involves the human is a notable history event (recorded once, on the change).
        bool wasWar = PlayerById(a)!.StanceMap.GetValueOrDefault(b) == Stance.War;
        if (stance == Stance.War && !wasWar && (a == _human.PlayerId || b == _human.PlayerId))
        {
            int rival = a == _human.PlayerId ? b : a;
            RecordHistory(HistoryEventKind.WarDeclared, $"War broke out with the {NationDisplayName(rival)}.");
        }
        PlayerById(a)!.StanceMap[b] = stance;
        if (symmetric)
        {
            PlayerById(b)!.StanceMap[a] = stance;
        }
    }

    /// <summary>
    /// Adjusts <paramref name="a"/>'s tension toward <paramref name="b"/> by <paramref name="delta"/> (clamped to
    /// [0, <see cref="MaxTension"/>]), symmetrically by default. A no-op unless both are distinct colonial players.
    /// </summary>
    internal void ChangeTension(int a, int b, int delta, bool symmetric = true)
    {
        if (a == b || !IsColonialPlayer(a) || !IsColonialPlayer(b))
        {
            return;
        }
        Player pa = PlayerById(a)!;
        pa.TensionMap[b] = Math.Clamp(pa.Tensions.GetValueOrDefault(b) + delta, 0, MaxTension);
        if (symmetric)
        {
            Player pb = PlayerById(b)!;
            pb.TensionMap[a] = Math.Clamp(pb.Tensions.GetValueOrDefault(a) + delta, 0, MaxTension);
        }
    }

    /// <summary>
    /// Raises <paramref name="power"/>'s tension toward the human by <see cref="TensionLandTaken"/> for each of the
    /// human's colonies that crowds one of the power's own colonies — within <see cref="ColonialTensionRadius"/> tiles
    /// (FreeCol's land-encroachment tension, <c>TENSION_ADD_LAND_TAKEN</c>).
    /// This is the non-attack tension source (86d3c9udb) that makes <see cref="StanceFromTension"/>'s Peace→War branch
    /// reachable in play: before it, colonial tension only ever rose on an attack (which already set War directly), so a
    /// power never declared war on its own. <b>Directional</b> — only the power's view of the human rises (FreeCol: the
    /// territory owner gains the tension), so it never touches the human's own tension/stance toward the power, and (like
    /// all diplomacy) draws no RNG, leaving the human's stream 0 byte-identical (ADR-009). A no-op for a power with no
    /// colonies, or one already at war (war tension is already maxed). Deterministic — stable colony iteration.
    /// <c>internal</c> so the RNG-free accrual can be asserted directly against stream 0 (ADR-006).
    /// </summary>
    internal void AccrueTerritorialTension(Player power)
    {
        if (power.Stances.GetValueOrDefault(_human.PlayerId) is not (Stance.Peace or Stance.CeaseFire))
        {
            return; // uncontacted (not yet met) or already at war → no territorial escalation to apply
        }
        int encroachment = 0;
        foreach (Colony own in ColoniesOf(power))
        {
            encroachment += _colonies.Count(h =>
                IsHumanOwned(h) && ChebyshevDistance(h.Position, own.Position) <= ColonialTensionRadius);
        }
        if (encroachment > 0)
        {
            ChangeTension(power.PlayerId, _human.PlayerId, encroachment * TensionLandTaken, symmetric: false);
        }
    }

    /// <summary>True when a Founding Father elected to <paramref name="player"/>'s Congress grants the ability.</summary>
    private bool HasAbilityFor(Player player, string abilityId) =>
        player.Congress.Select(Ruleset.Father).SelectMany(f => f.Abilities).Any(a => a.Id == abilityId && a.Value);

    /// <summary>True when <paramref name="unit"/>'s owning colonial player has the combat ability (a native owner has none wired yet).</summary>
    private bool AbilityForUnit(Unit unit, string abilityId) =>
        unit.OwnerNationId is null && PlayerById(unit.OwnerId) is { } owner && HasAbilityFor(owner, abilityId);

    /// <summary>True when at least one in-bounds neighbour of the colony's tile is water (it has a port / is coastal).</summary>
    private bool IsColonyCoastal(Colony colony) =>
        colony.Position.Neighbours().Any(n => Map.InBounds(n) && Map.TerrainAt(n).IsWater);

    /// <summary>
    /// True when the colony satisfies a build-gating ability: <c>hasPort</c> resolves to its coastal status, every
    /// other ability to a Founding Father granted to the colony's owner (FreeCol aggregates colony abilities; the
    /// build gate only consults the port + father sources today — see [docs]/systems/colonies.md).
    /// </summary>
    private bool ColonyHasAbility(Colony colony, string abilityId) =>
        abilityId == HasPortAbility
            ? IsColonyCoastal(colony)
            : PlayerById(colony.OwnerId) is { } owner && HasAbilityFor(owner, abilityId);

    /// <summary>True when the colony has a building that grants the auto-export ability (a custom house) — a per-colony capability, not a Congress perk.</summary>
    private bool ColonyHasExportAbility(Colony colony) =>
        colony.Buildings.Any(b => Ruleset.Building(b).GrantsExport);

    /// <summary>The colonies owned by <paramref name="player"/> (the human owns all colonies until foreign powers found their own).</summary>
    private IEnumerable<Colony> ColoniesOf(Player player) => _colonies.Where(c => c.OwnerId == player.PlayerId);

    /// <summary>
    /// The colony-name list the colonial player <paramref name="ownerId"/> founds by: its European nation's
    /// names (FP-3a data) if it has one, else the default list. The human is nation-less for now, so it uses
    /// the default — keeping its colony names unchanged (FP-3b); foreign powers found by their own names.
    /// </summary>
    private IReadOnlyList<string> ColonyNamesFor(int ownerId) =>
        PlayerById(ownerId)?.NationId is { } nationId
        && Ruleset.EuropeanNations.FirstOrDefault(n => n.Id == nationId) is { ColonyNames.Count: > 0 } nation
            ? nation.ColonyNames
            : ColonyNames;

    /// <summary>The human player's treasury in gold.</summary>
    public int Gold => _human.Gold;

    /// <summary>The human player's sales tax as a percentage (0–100) deducted from European sales.</summary>
    public int TaxRate => _human.TaxRate;

    /// <summary>The human player's European market (trade prices).</summary>
    public Market Market => _human.Market;

    /// <summary>Liberty points banked toward the human player's next Founding Father.</summary>
    public int Liberty => _human.Liberty;

    /// <summary>Founding Fathers elected to the human player's Continental Congress, in election order.</summary>
    public IReadOnlyList<string> Congress => _human.Congress;

    /// <summary>The father the human player is currently recruiting (null = none chosen).</summary>
    public string? CurrentFather => _human.CurrentFather;

    /// <summary>The fathers offered to the human player this round — one per category with an eligible candidate.</summary>
    public IReadOnlyList<string> OfferedFathers => _human.OfferedFathers;

    /// <summary>Immigration points banked toward the human player's next emigrant (crosses + the Europe contribution).</summary>
    public int Immigration => _human.Immigration;

    /// <summary>Immigration points needed to produce the human player's next emigrant — the stored target reduced by the player's religious-unrest advantage (rises by the difficulty's <see cref="Specification.DifficultyOptions.CrossesIncrement"/> each time).</summary>
    public int ImmigrationRequired => EffectiveImmigrationRequired(_human);

    /// <summary>The unit types waiting on the human player's Europe recruitment dock (one id per <see cref="RecruitSlots"/> slot).</summary>
    public IReadOnlyList<string> RecruitDock => _human.RecruitDock;

    /// <summary>
    /// Current gold price to buy one recruit from the dock (FreeCol
    /// <c>Europe.getCurrentRecruitPrice</c>): <c>max(base·max(required−immigration,0)/required, floor)</c>.
    /// Falls toward the floor as immigration approaches the target, then jumps after each paid recruit.
    /// </summary>
    public int RecruitPrice => _human.RecruitPrice;

    /// <summary>The escalating base used in the recruit-price formula (persisted; FreeCol <c>baseRecruitPrice</c>).</summary>
    internal int BaseRecruitPrice => _human.BaseRecruitPrice;

    /// <summary>The recruit-price floor (persisted; FreeCol <c>recruitLowerCap</c>).</summary>
    internal int RecruitLowerCap => _human.RecruitLowerCap;

    /// <summary>
    /// Game age (1–3) used to weight which fathers are offered, keyed off the calendar <see cref="CurrentYear"/>
    /// against the spec age thresholds (classic 1600/1700) — FreeCol <c>Specification.getAge</c>. So age 1 runs to
    /// 1599, age 2 from 1600, age 3 from 1700 (turns 1–108 / 109–308 / 309+).
    /// </summary>
    public int CurrentAge => Ruleset.AgeForYear(CurrentYear);

    /// <summary>
    /// Liberty needed to elect the next father (FreeCol <c>getTotalFoundingFatherCost</c>):
    /// the first is free-ish at <paramref name="factor"/>, later ones cost
    /// <c>2·(elected+1)·factor + 1</c>.
    /// </summary>
    public static int FoundingFatherCost(int electedCount, int factor) =>
        electedCount == 0 ? factor : 2 * (electedCount + 1) * factor + 1;

    /// <summary>Liberty needed to elect the human player's next father.</summary>
    public int TotalFoundingFatherCost() => TotalFoundingFatherCost(_human);

    /// <summary>Liberty needed to elect <paramref name="player"/>'s next father.</summary>
    private int TotalFoundingFatherCost(Player player) =>
        FoundingFatherCost(player.Congress.Count, Ruleset.Difficulty.FoundingFatherFactor);

    /// <summary>Chooses which offered father the human player recruits toward.</summary>
    /// <exception cref="InvalidMoveException">The father is not currently offered.</exception>
    public void ChooseFather(string fatherId)
    {
        if (!_human.OfferedFathers.Contains(fatherId))
        {
            throw new InvalidMoveException($"{fatherId} is not currently offered.");
        }
        _human.CurrentFather = fatherId;
    }

    // ── Player score (FreeCol ServerPlayer.updateScore) ─────────────────────────────────────────────────
    //
    // A faithful, pure re-implementation of FreeCol's player-score formula. The victory and high-score screens
    // (separate P7 tasks) consume this read; nothing here mutates state, so the default game stays byte-identical
    // (ADR-009) and the score is NOT persisted — it is recomputed from current state each time it is read, exactly
    // as FreeCol recomputes it (FreeCol caches the result in a serialized field for the network protocol; we have no
    // such need, so we keep it a pure read and add no save field — no save-version bump).

    /// <summary>Score bonus for each founding father (FreeCol <c>ServerPlayer.SCORE_FOUNDING_FATHER</c> = 5; Col1).</summary>
    private const int ScoreFoundingFather = 5;

    /// <summary>Gold-to-score rate: 1 point per 1000 gold (FreeCol <c>ServerPlayer.SCORE_GOLD</c> = 0.001; Col1).</summary>
    private const double ScoreGold = 0.001;

    /// <summary>Percentage score bonus for being the first power to win independence (FreeCol <c>SCORE_INDEPENDENCE_BONUS_FIRST</c> = 100; Col1).</summary>
    private const int ScoreIndependenceBonusFirst = 100;

    /// <summary>Percentage score bonus for the second power to win independence (FreeCol <c>SCORE_INDEPENDENCE_BONUS_SECOND</c> = 50; Col1).</summary>
    private const int ScoreIndependenceBonusSecond = 50;

    /// <summary>Percentage score bonus for the third power to win independence (FreeCol <c>SCORE_INDEPENDENCE_BONUS_THIRD</c> = 25; Col1).</summary>
    private const int ScoreIndependenceBonusThird = 25;

    /// <summary>
    /// Per-unit-type score values from the classic FreeCol ruleset (the <c>score-value</c> attribute on each
    /// <c>unit-type</c> in <c>freecol/data/rules/classic/specification.xml</c>, lines 1814–2213). FreeCol reads these off
    /// the parsed <see cref="Specification.UnitType"/> (<c>UnitType.getScoreValue</c>); our <see cref="Specification.UnitType"/>
    /// does not yet carry that attribute, so this scoring section holds the table directly — keeping the formula faithful and
    /// self-contained without touching the spec parser. A unit type absent from the table scores 0 (FreeCol's default for a
    /// type with no <c>score-value</c>). Update this table if the unit ruleset's score values change.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, int> UnitScoreValues = new Dictionary<string, int>
    {
        // People (specification.xml lines 1814–2003)
        ["model.unit.freeColonist"] = 3,
        ["model.unit.expertFarmer"] = 4,
        ["model.unit.expertFisherman"] = 4,
        ["model.unit.expertFurTrapper"] = 4,
        ["model.unit.expertSilverMiner"] = 4,
        ["model.unit.expertLumberJack"] = 4,
        ["model.unit.expertOreMiner"] = 4,
        ["model.unit.masterSugarPlanter"] = 5,
        ["model.unit.masterCottonPlanter"] = 5,
        ["model.unit.masterTobaccoPlanter"] = 5,
        ["model.unit.firebrandPreacher"] = 6,
        ["model.unit.elderStatesman"] = 6,
        ["model.unit.masterCarpenter"] = 4,
        ["model.unit.masterDistiller"] = 5,
        ["model.unit.masterWeaver"] = 5,
        ["model.unit.masterTobacconist"] = 5,
        ["model.unit.masterFurTrader"] = 5,
        ["model.unit.masterBlacksmith"] = 5,
        ["model.unit.masterGunsmith"] = 5,
        ["model.unit.seasonedScout"] = 4,
        ["model.unit.hardyPioneer"] = 4,
        ["model.unit.veteranSoldier"] = 5,
        ["model.unit.jesuitMissionary"] = 5,
        ["model.unit.indenturedServant"] = 2,
        ["model.unit.pettyCriminal"] = 1,
        ["model.unit.indianConvert"] = 3,
        // Ships and land vehicles (specification.xml lines 2102–2213)
        ["model.unit.caravel"] = 3,
        ["model.unit.frigate"] = 6,
        ["model.unit.galleon"] = 5,
        ["model.unit.manOWar"] = 8,
        ["model.unit.merchantman"] = 4,
        ["model.unit.privateer"] = 4,
        ["model.unit.artillery"] = 2,
        ["model.unit.damagedArtillery"] = 1,
        ["model.unit.treasureTrain"] = 1,
        ["model.unit.wagonTrain"] = 1,
    };

    /// <summary>The classic-ruleset score value of one unit (FreeCol <c>UnitType.getScoreValue</c>); 0 for a type with no score value.</summary>
    private static int UnitScoreValue(Unit unit) => UnitScoreValues.GetValueOrDefault(unit.Type.Id, 0);

    /// <summary>The score value of the human player (a convenience over <see cref="PlayerScore"/>, mirroring <see cref="Gold"/>/<see cref="Liberty"/>).</summary>
    public int Score => PlayerScore(_human);

    /// <summary>
    /// The player's final score, recomputed from current state exactly as FreeCol does
    /// (<c>ServerPlayer.updateScore</c>). A <b>pure, RNG-free read</b>: it mutates nothing and draws no randomness, so a
    /// score read leaves the game byte-identical (ADR-009) and the value is never persisted — the victory/high-score
    /// screens (P7) recompute it on demand.
    /// <para>
    /// The sum is: Σ(unit score values) + Σ(colony liberty) + 5·(founding fathers) + ⌊0.001·gold⌋, then the
    /// independence percentage bonus is applied to that subtotal — <c>subtotal + subtotal·bonus/100</c> — where the
    /// bonus is 100/50/25% for the first/second/third power to win independence.
    /// </para>
    /// <para><b>Faithful-subset notes vs. FreeCol's <c>updateScore</c>:</b>
    /// (1) FreeCol's <c>sum(getUnits(), Unit::getScoreValue)</c> counts every owned unit including colony workers; our
    /// colony workers are not in the unit list (they are colony population), so only map/Europe units contribute today —
    /// the same modelling deviation noted on the continental-army muster.
    /// (2) FreeCol also folds in per-event history scores (region discovery, lost-city finds, settlement/nation destruction
    /// penalties) via <c>HistoryEvent.getScore()</c>; our <see cref="HistoryEvent"/> carries no numeric score and is not yet
    /// persisted, so those summands are omitted until the history log gains scores (follow-up).
    /// (3) FreeCol derives the independence ordinal from an INDEPENDENCE history event's stored place (0/1/2); with a single
    /// human player, an <see cref="PlayerType.Independent"/> nation is the first to win, so it takes the 100% first-place bonus.</para>
    /// </summary>
    /// <param name="player">The player to score.</param>
    /// <returns>The player's score (may be negative once destruction penalties land; today it is ≥ 0).</returns>
    public int PlayerScore(Player player)
    {
        int score = _units.Where(u => IsOwnedBy(u, player)).Sum(UnitScoreValue)
            + ColoniesOf(player).Sum(c => c.Liberty)
            + ScoreFoundingFather * player.Congress.Count
            + (int)Math.Floor(ScoreGold * player.Gold);

        int bonus = IndependenceScoreBonusPercent(player);
        score += score * bonus / 100;
        return score;
    }

    /// <summary>
    /// The independence percentage bonus for <paramref name="player"/> (FreeCol's INDEPENDENCE history-event switch): a
    /// nation that has won independence takes the bonus for the order in which it did so. With one human player an
    /// independent nation is always the first, so it takes <see cref="ScoreIndependenceBonusFirst"/> (100%); a player that
    /// has not won independence gets 0. The second/third constants exist for the multi-power future
    /// (<see cref="ScoreIndependenceBonusSecond"/>/<see cref="ScoreIndependenceBonusThird"/>).
    /// </summary>
    private int IndependenceScoreBonusPercent(Player player)
    {
        if (player.PlayerType != PlayerType.Independent)
        {
            return 0;
        }
        // The order in which independence was won, among all independent nations (FreeCol's stored 0/1/2 ordinal).
        int place = _players
            .Where(p => p.PlayerType == PlayerType.Independent)
            .OrderBy(p => p.DeclaredIndependenceTurn ?? int.MaxValue)
            .ThenBy(p => p.PlayerId)
            .ToList()
            .IndexOf(player);
        return place switch
        {
            0 => ScoreIndependenceBonusFirst,
            1 => ScoreIndependenceBonusSecond,
            2 => ScoreIndependenceBonusThird,
            _ => 0,
        };
    }

    /// <summary>All units in the game — the player's and the natives' (braves).</summary>
    public IReadOnlyList<Unit> Units => _units;

    /// <summary>The human colonial player's units (resolved by owner, so foreign powers' units are excluded — FP-2).</summary>
    public IEnumerable<Unit> PlayerUnits => _units.Where(IsHumanOwned);

    /// <summary>The native braves owned by a nation.</summary>
    public IEnumerable<Unit> NativeUnits => _units.Where(u => u.IsNative);

    /// <summary>
    /// Combat the human was the victim of (not the initiator) during the most recent <see cref="EndTurn"/> —
    /// native braves raiding the human's units. Transient per-turn UI scratch (cleared each <c>EndTurn</c>,
    /// never saved); the presentation reads it after the turn resolves to notify the player.
    /// </summary>
    public IReadOnlyList<CombatNotice> CombatNotices => _combatNotices;

    /// <summary>
    /// Human colonies captured by a foreign power during the most recent <see cref="EndTurn"/> (1c-3f). Transient
    /// per-turn UI scratch (cleared each <c>EndTurn</c>, never saved); the presentation reads it after the turn
    /// resolves to tell the player "X captured your colony Y".
    /// </summary>
    public IReadOnlyList<ColonyLossNotice> ColonyLossNotices => _colonyLossNotices;

    /// <summary>
    /// Human colonies pillaged by a native brave during the most recent <see cref="EndTurn"/> (native colony
    /// pillage). Transient per-turn UI scratch (cleared each <c>EndTurn</c>, never saved); the presentation reads
    /// it after the turn resolves to tell the player "X raided your colony Y and carried off N goods".
    /// </summary>
    public IReadOnlyList<ColonyRaidNotice> ColonyRaidNotices => _colonyRaidNotices;

    /// <summary>
    /// Friendly native gifts delivered to human colonies during the most recent <see cref="EndTurn"/> (native
    /// bring-gift AI). Transient — cleared each turn, never saved; the presentation reads it after the turn to tell
    /// the player "the X brought N goods to your colony Y".
    /// </summary>
    public IReadOnlyList<ColonyGiftNotice> ColonyGiftNotices => _colonyGiftNotices;

    /// <summary>
    /// Goods a human colony's custom house auto-sold to Europe during the most recent <see cref="EndTurn"/> — one
    /// entry per good sold. Transient per-turn UI scratch (cleared each <c>EndTurn</c>, never saved); the
    /// presentation reads it after the turn to tell the player "your custom house in X sold N goods for G gold".
    /// </summary>
    public IReadOnlyList<CustomHouseSaleNotice> CustomHouseSaleNotices => _customHouseSaleNotices;

    /// <summary>
    /// Lost City Rumours the human explored since the last <see cref="EndTurn"/> that resolved immediately — every
    /// outcome except strange mounds (which pause for <see cref="PendingMounds"/>). The reward is applied inside
    /// <see cref="ExploreRumour"/> during the human's own move with no return value the move handler reads, so the
    /// game collects these and the presentation surfaces them after the move (FreeCol shows a model message per
    /// rumour). Transient per-turn UI scratch — cleared each <c>EndTurn</c>, never saved; presentation drains it
    /// (each entry's <see cref="RumourNotice.Message"/> is pre-formatted to keep the internal outcome enum out of
    /// the UI, ADR-006).
    /// </summary>
    public IReadOnlyList<RumourNotice> RumourNotices => _rumourNotices;

    /// <summary>Drains and clears the collected <see cref="RumourNotices"/> (the presentation reads them once, after a move that explored a rumour).</summary>
    public IReadOnlyList<RumourNotice> TakeRumourNotices()
    {
        var taken = _rumourNotices.ToList();
        _rumourNotices.Clear();
        return taken;
    }

    /// <summary>
    /// The native tribute demand currently awaiting the human's accept/refuse, or null if none. Transient per-turn
    /// UI state — created during a native turn (<see cref="RunNativeTurn"/>), answered via
    /// <see cref="AcceptPendingDemand"/>/<see cref="RefusePendingDemand"/>, and auto-refused at the next
    /// <see cref="EndTurn"/> if ignored; never saved.
    /// </summary>
    public NativeDemand? PendingDemand => _pendingDemand;

    /// <summary>A strange-mounds rumour awaiting the human's investigate/decline choice: the exploring unit + its tile.</summary>
    /// <param name="UnitId">The unit standing on the mounds (it bears the risk if the expedition vanishes).</param>
    /// <param name="Tile">The rumour tile to investigate or leave be.</param>
    public sealed record PendingMoundsDecision(int UnitId, Position Tile);

    /// <summary>
    /// The strange-mounds rumour currently awaiting the human's investigate/decline choice, or null if none.
    /// Transient per-turn UI state — set when a human unit steps onto a strange-mounds rumour
    /// (<see cref="TryExploreRumour"/>), answered via <see cref="InvestigatePendingMounds"/> /
    /// <see cref="DeclinePendingMounds"/>; never saved (a save mid-prompt reloads with the rumour un-explored).
    /// (An AI explorer auto-investigates and never sets this.)
    /// </summary>
    public PendingMoundsDecision? PendingMounds => _pendingMounds;

    /// <summary>
    /// Investigates the <see cref="PendingMounds"/> rumour on the human's stream and clears the pending state,
    /// returning the resolved outcome (FreeCol's "investigate these strange mounds?" → yes). A no-op returning
    /// <see cref="LostCityRumourType.Nothing"/> if nothing is pending or the unit is gone.
    /// </summary>
    internal LostCityRumourType InvestigatePendingMounds()
    {
        if (_pendingMounds is not { } pending)
        {
            return LostCityRumourType.Nothing;
        }
        _pendingMounds = null;
        if (_units.FirstOrDefault(u => u.Id == pending.UnitId) is not { } unit)
        {
            Map.RemoveRumour(pending.Tile); // the explorer is gone — just consume the rumour
            return LostCityRumourType.Nothing;
        }
        return InvestigateMounds(unit, pending.Tile, _random);
    }

    /// <summary>
    /// Resolves the pending strange-mounds prompt for the human and returns a short, player-facing description of
    /// what happened (the presentation entry point, ADR-006 — it keeps the internal outcome enum out of the UI
    /// assembly). <paramref name="investigate"/> true digs the mounds (a re-rolled Lost-City outcome on the human's
    /// stream); false leaves them be. Empty string if nothing is pending.
    /// </summary>
    public string ResolvePendingMounds(bool investigate)
    {
        if (_pendingMounds is null)
        {
            return "";
        }
        if (!investigate)
        {
            DeclinePendingMounds();
            return "You leave the strange mounds undisturbed.";
        }
        return DescribeMoundsOutcome(InvestigatePendingMounds());
    }

    /// <summary>A one-line player-facing description of an investigated strange-mounds (Lost City) outcome.</summary>
    private static string DescribeMoundsOutcome(LostCityRumourType outcome) => outcome switch
    {
        LostCityRumourType.ExpeditionVanishes => "The expedition vanishes without a trace!",
        LostCityRumourType.TribalChief => "Tribal chiefs share their treasure with you!",
        LostCityRumourType.Learn => "Your explorer learns the ways of a seasoned scout!",
        LostCityRumourType.Colonist => "A band of colonists joins your expedition!",
        LostCityRumourType.FountainOfYouth => "A Fountain of Youth! Settlers flock to your docks.",
        LostCityRumourType.Ruins => "You uncover ancient ruins — treasure!",
        LostCityRumourType.Cibola => "You have found one of the Seven Cities of Cibola — a vast treasure!",
        LostCityRumourType.BurialGround => "You have desecrated a native burial ground — the natives are enraged!",
        _ => "You find nothing of note.",
    };

    /// <summary>Declines the <see cref="PendingMounds"/> rumour (removes it, no effect) and clears the pending state (FreeCol decline).</summary>
    internal void DeclinePendingMounds()
    {
        if (_pendingMounds is { } pending)
        {
            _pendingMounds = null;
            DeclineMounds(pending.Tile);
        }
    }

    /// <summary>
    /// Sentinel <see cref="CombatNotice.AttackerNationId"/> for a raider that hides its flag (a privateer —
    /// FreeCol's <c>model.nation.unknownEnemy</c>, which is a no-owner pseudo-nation, not a real one). The
    /// presentation renders it as an anonymous "privateer" rather than naming the nation behind it.
    /// </summary>
    public const string UnknownEnemyNationId = "model.nation.unknownEnemy";

    /// <summary>All colonies, in founding order.</summary>
    public IReadOnlyList<Colony> Colonies => _colonies;

    /// <summary>
    /// Whether the human has been wiped out — <b>no colonies and no units anywhere</b> (on map, in Europe, or
    /// aboard). Once true it stays true: a player with nothing left can found nothing and fight no one. The
    /// presentation surfaces it as a defeat banner; <em>stopping</em> the game on defeat (disabling End Turn / a
    /// game-over screen) is a presentation-layer follow-up — <see cref="EndTurn"/> deliberately does <b>not</b>
    /// short-circuit, so the human's stream 0 stays independent of AI actions (ADR-009 byte-stability). This is a
    /// deliberately <b>conservative</b>
    /// subset of FreeCol's <c>checkForDeath</c>: we keep the human alive on <em>any</em> surviving unit of any
    /// type in any location, where FreeCol can declare a colonist-less colonial player dead past 1600 even with a
    /// unit stranded in Europe (and conversely grants a pre-1600 free recruit). Erring toward "still alive" means
    /// we never declare defeat falsely; the year/port/free-recruit nuances are not modelled. Pure/computed: no
    /// stored state, no save-format impact; the presentation surfaces it after a turn resolves.
    /// </summary>
    public bool IsHumanDefeated => !_colonies.Any(IsHumanOwned) && !_units.Any(IsHumanOwned);

    /// <summary>The colony on a tile, or null.</summary>
    public Colony? ColonyAt(Position p) => _colonies.FirstOrDefault(c => c.Position == p);

    /// <summary>
    /// The percentage defence bonus a colony's fortifications grant a unit defending in it (FreeCol
    /// <c>model.modifier.defence</c> on the colony's buildings): stockade +100, fort +150, fortress +200. A colony
    /// holds at most one fortification tier (fort upgrades the stockade, fortress the fort), so summing the
    /// per-building bonuses yields just the tier present (0 with no fortification).
    /// </summary>
    public int ColonyDefenceBonus(Colony colony) => colony.Buildings.Sum(b => Ruleset.Building(b).DefenceBonus);

    /// <summary>
    /// The percentage bell-output bonus from a colony's bell-press buildings (FreeCol <c>model.goods.bells</c>):
    /// printing press +50, newspaper +100. A colony holds at most one (newspaper upgrades the press), so the sum
    /// is the tier present (0 with neither). Boosts the bells that become Sons-of-Liberty + founding-father liberty.
    /// </summary>
    public int BellProductionBonus(Colony colony) => colony.Buildings.Sum(b => Ruleset.Building(b).BellBonus);

    /// <summary>The fortification defence bonus of the colony on a tile (0 if no colony / no fortification) — applied to whoever defends there.</summary>
    private int ColonyDefenceBonusAt(Position p) => ColonyAt(p) is { } colony ? ColonyDefenceBonus(colony) : 0;

    /// <summary>All native settlements on the map.</summary>
    public IReadOnlyList<NativeSettlement> NativeSettlements => _nativeSettlements;

    /// <summary>The native settlement on a tile, or null.</summary>
    public NativeSettlement? NativeSettlementAt(Position p) =>
        _nativeSettlements.FirstOrDefault(s => s.Position == p);

    /// <summary>
    /// Re-derives native land ownership from the current settlements (FreeCol <c>Tile.owner</c>): the tiles in each
    /// settlement's <see cref="Specification.SettlementType.ClaimableRadius"/> become owned by its nation. Pure +
    /// deterministic (no RNG) — so rather than being saved it is run at game start, on load, and whenever the
    /// settlements change (e.g. one is destroyed in combat, releasing its claim). <b>Idempotent</b>: it clears the
    /// existing claims first, so calling it again rebuilds the same map. See <see cref="World.NativeLandClaimGenerator"/>.
    /// Tiles the player has already <b>bought or taken</b> (<see cref="World.GameMap.ClaimedFromNatives"/>) are
    /// subtracted afterwards, so a purchased tile never reverts to native ownership across re-derivation.
    /// Consumed by the Lost City Rumour burial-ground gate and native land purchase (<c>86d3c9tha</c>).
    /// </summary>
    private void ClaimNativeLand()
    {
        Map.ClearNativeOwners(); // rebuild from scratch so a removed settlement's claim is dropped
        foreach ((Position p, string nation) in NativeLandClaimGenerator.Claim(Map, _nativeSettlements, Ruleset))
        {
            Map.SetNativeOwner(p, nation);
        }
        foreach (Position p in Map.ClaimedFromNatives)
        {
            Map.ClearNativeOwner(p); // a bought/taken tile stays the player's, never re-claimed by the natives
        }
    }

    // ===== Native land purchase (86d3c9tha) ===================================================================
    // A native-owned tile is bought (gold to the tribe) or taken (+alarm) before the player uses it; Peter Minuit
    // makes it free + peaceful (FreeCol Player.getLandPrice / ServerPlayer.csClaimLand). The found/work TRIGGER is
    // deferred — the pay-vs-steal choice is a UI dialog — so these are explicit operations for now.

    /// <summary>The flat addend in the land price (FreeCol <c>getLandPrice</c> <c>+ 100</c>).</summary>
    private const int LandPriceBase = 100;

    /// <summary>Alarm added to the robbed nation when land is <em>taken</em> rather than bought (FreeCol <c>Tension.TENSION_ADD_LAND_TAKEN</c>).</summary>
    private const int LandTakenAlarm = 200;

    /// <summary>The father modifier id scaling the land price — Peter Minuit's −100% makes native land free.</summary>
    private const string LandPaymentModifierId = "model.modifier.landPaymentModifier";

    /// <summary>The gold price for the human to buy the native-owned <paramref name="tile"/> (0 if it is not native land).</summary>
    public int LandPrice(Position tile) => LandPrice(_human, tile);

    /// <summary>
    /// What <paramref name="player"/> must pay a native nation for <paramref name="tile"/> (FreeCol
    /// <c>Player.getLandPrice</c>): the difficulty's <see cref="Specification.DifficultyOptions.LandPriceFactor"/> ×
    /// the tile's potential yield of every good <em>except the primary food aggregate</em> (FreeCol
    /// <c>gt != getPrimaryFoodType()</c> — only <c>model.goods.food</c> is dropped; <b>grain and fish are counted</b>,
    /// since farmland and fisheries are worth paying for) + <see cref="LandPriceBase"/>, then the player's
    /// <see cref="LandPaymentModifierId"/>
    /// modifier (Peter Minuit −100% → 0). Returns <b>0</b> when the tile is not native-owned (unclaimed or already
    /// bought; natives are never a colonial player, so the buyer can never already own it).
    /// </summary>
    internal int LandPrice(Player player, Position tile)
    {
        if (!Map.IsNativeOwned(tile))
        {
            return 0;
        }
        // The price values the land's POTENTIAL output (FreeCol getPotentialProduction with a null owner) — NOT
        // the acting player's father-boosted yield, so e.g. Henry Hudson's +furs never inflates what land costs.
        // Only the primary-food aggregate (model.goods.food) is excluded, matching FreeCol's gt != getPrimaryFoodType();
        // grain/fish (also is-food) ARE summed — dropping them would undervalue farmland and fisheries.
        int raw = (Ruleset.Difficulty.LandPriceFactor * Ruleset.GoodsTypes.Where(g => g.Id != Colony.FoodId).Sum(g => TileYieldPotential(tile, g.Id)))
                  + LandPriceBase;
        // The landPaymentModifier scales the price — Peter Minuit's −100% makes it free. ApplyGoodsModifiers stacks
        // every matching modifier by index (FreeCol applyModifiers), consistent with the rest of the codebase.
        return ApplyGoodsModifiers(player, LandPaymentModifierId, raw);
    }

    /// <summary>Whether the human may claim (buy or take) the native-owned <paramref name="tile"/>; the check's cost is the buy price.</summary>
    public MoveCheck CheckClaimLand(Position tile) => CheckClaimLand(_human, tile);

    /// <summary>Whether <paramref name="player"/> may claim <paramref name="tile"/> from the natives (it must be native-owned).</summary>
    internal MoveCheck CheckClaimLand(Player player, Position tile)
    {
        if (!Map.InBounds(tile))
        {
            return MoveCheck.No("Tile is off the map.");
        }
        if (!Map.IsNativeOwned(tile))
        {
            return MoveCheck.No("That land is not claimed by a native nation.");
        }
        if (NativeSettlementAt(tile) is not null)
        {
            return MoveCheck.No("The natives will not sell the ground their settlement stands on."); // FreeCol: a settlement tile is not for sale
        }
        return MoveCheck.Yes(LandPrice(player, tile));
    }

    /// <summary>Buys the native-owned <paramref name="tile"/> for the human: pays the <see cref="LandPrice(Position)"/> (free under Peter Minuit), and the tile becomes the player's for good.</summary>
    /// <exception cref="InvalidMoveException">Not native land, or not enough gold.</exception>
    public void ClaimLandByPaying(Position tile) => ClaimLandByPaying(_human, tile);

    /// <summary>Buys <paramref name="tile"/> from the natives for <paramref name="player"/> (see <see cref="ClaimLandByPaying(Position)"/>).</summary>
    internal void ClaimLandByPaying(Player player, Position tile)
    {
        MoveCheck check = CheckClaimLand(player, tile);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }
        int price = LandPrice(player, tile);
        if (player.Gold < price)
        {
            throw new InvalidMoveException($"Not enough gold to buy this land (need {price}).");
        }
        player.Gold -= price; // FreeCol credits the native owner; we keep no native treasury, so the gold simply leaves
        Map.ClaimFromNatives(tile);
    }

    /// <summary>Takes the native-owned <paramref name="tile"/> for the human by force: no gold changes hands, but the robbed nation's settlements gain <see cref="LandTakenAlarm"/> alarm.</summary>
    /// <exception cref="InvalidMoveException">Not native land.</exception>
    public void ClaimLandByStealing(Position tile) => ClaimLandByStealing(_human, tile);

    /// <summary>Takes <paramref name="tile"/> from the natives for <paramref name="player"/> (see <see cref="ClaimLandByStealing(Position)"/>).</summary>
    internal void ClaimLandByStealing(Player player, Position tile)
    {
        MoveCheck check = CheckClaimLand(player, tile);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }
        string nation = Map.NativeOwnerOf(tile)!; // the robbed nation, recorded before the claim clears ownership
        Map.ClaimFromNatives(tile);
        foreach (NativeSettlement settlement in _nativeSettlements.Where(s => s.NationTypeId == nation))
        {
            ChangeNativeAlarm(settlement, LandTakenAlarm); // FreeCol TENSION_ADD_LAND_TAKEN, nation-wide (ownership is tracked per nation)
        }
    }

    // ===== Treasure-train cash-in (86d3c9rzu) =================================================================
    // Escort a treasure train to a colony (or Europe) to bank its gold (FreeCol Unit.canCashInTreasureTrain /
    // getTransportFee + the cash-in handler): at a colony the King ships it across for a transport cut, then the
    // monarch's tax applies; carry it to Europe yourself (a galleon) to skip the King's fee.

    /// <summary>The father modifier id scaling the transport fee — Hernán Cortés's −100% ships treasure for free.</summary>
    private const string TreasureTransportFeeModifierId = "model.modifier.treasureTransportFee";

    /// <summary>The King's fee to ship <paramref name="train"/>'s treasure to Europe: the difficulty's
    /// <see cref="Specification.DifficultyOptions.TreasureTransportFee"/>% of the amount (medium 60), less Hernán
    /// Cortés's <c>treasureTransportFee</c> modifier (−100% → free).</summary>
    private int TransportFee(Player owner, Unit train) =>
        ApplyGoodsModifiers(owner, TreasureTransportFeeModifierId, Ruleset.Difficulty.TreasureTransportFee * train.TreasureAmount / 100);

    /// <summary>
    /// The gold <paramref name="owner"/> nets cashing in <paramref name="train"/>: the carried amount less the King's
    /// <see cref="TransportFee"/> (0 if the train is already in Europe — you carried it yourself), then the monarch's
    /// tax on the remainder. Integer-truncated, like the rest of the economy.
    /// </summary>
    private int CashInValue(Player owner, Unit train)
    {
        int fee = train.Location == UnitLocation.InEurope ? 0 : TransportFee(owner, train);
        return (train.TreasureAmount - fee) * (100 - owner.TaxRate) / 100;
    }

    /// <summary>The gold the human would net by cashing in <paramref name="train"/> where it stands (0 if it can't here).</summary>
    public int CashInValue(Unit train) =>
        CheckCashInTreasureTrain(train).Allowed && PlayerById(train.OwnerId) is { } owner ? CashInValue(owner, train) : 0;

    /// <summary>
    /// Whether <paramref name="train"/> may be cashed in where it stands: it must be a treasure-carrying unit with
    /// gold aboard, standing at a colony its owner holds (FreeCol requires a port connected to Europe — we have no
    /// connectivity graph, so any owned colony qualifies) or docked in Europe. The check's cost carries the net gold.
    /// </summary>
    public MoveCheck CheckCashInTreasureTrain(Unit train)
    {
        if (!train.Type.CarryTreasure)
        {
            return MoveCheck.No("That is not a treasure train.");
        }
        if (train.TreasureAmount <= 0)
        {
            return MoveCheck.No("The treasure train carries no gold.");
        }
        bool atOwnColony = train.IsOnMap && ColonyAt(train.Position) is { } colony && colony.OwnerId == train.OwnerId;
        bool inEurope = train.Location == UnitLocation.InEurope;
        if (!atOwnColony && !inEurope)
        {
            return MoveCheck.No("Bring the treasure train to one of your colonies (or to Europe) to cash it in.");
        }
        return PlayerById(train.OwnerId) is { } owner ? MoveCheck.Yes(CashInValue(owner, train)) : MoveCheck.No("The treasure train has no owner.");
    }

    /// <summary>
    /// Cashes in <paramref name="train"/>: banks the net gold (<see cref="CashInValue(Unit)"/>) to its owner and the
    /// train leaves the game (FreeCol disposes it on cash-in). At a colony the King takes his transport cut; in Europe
    /// there is no fee. The monarch's tax applies to the remainder either way.
    /// </summary>
    /// <exception cref="InvalidMoveException">Not cashable here; see <see cref="CheckCashInTreasureTrain"/>.</exception>
    public void CashInTreasureTrain(Unit train)
    {
        MoveCheck check = CheckCashInTreasureTrain(train);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }
        if (PlayerById(train.OwnerId) is { } owner)
        {
            owner.Gold += CashInValue(owner, train);
        }
        train.SetTreasureAmount(0); // the treasure is spent — also stops a stale reference re-cashing it (it credits gold)
        _units.Remove(train); // the treasure train leaves the game
    }

    /// <summary>
    /// Tiles a settlement's chief reveals when you first speak ("tales of nearby lands";
    /// scaled down from FreeCol's <c>TALES_RADIUS</c> = 6 for our smaller default map).
    /// </summary>
    public const int TalesRevealRadius = 3;

    /// <summary>Min/max gold in a settlement's first-contact gift (FreeCol <c>IndianSettlement.GIFT_MINIMUM/MAXIMUM</c>).</summary>
    private const int GiftMinimum = 10;
    private const int GiftMaximum = 80;

    /// <summary>The scout role id — a scout-role unit gets the full chief audience (FreeCol <c>scoutSpeakToChief</c>); other colonists get the basic visit.</summary>
    private const string ScoutRoleId = "model.role.scout";

    /// <summary>The expert-scout unit type (FreeCol <c>model.ability.expertScout</c>) a chief may train a scout into — the scout role's expert unit.</summary>
    private const string SeasonedScoutUnitTypeId = "model.unit.seasonedScout";

    /// <summary>
    /// Unit types that can be taught a skill at a native settlement (FreeCol: the free
    /// colonist and indentured servant only — experts and petty criminals cannot). A
    /// documented simplification pending FreeCol <c>unit-change-types</c> (NATIVES) data.
    /// </summary>
    private static readonly string[] SkillLearnerTypeIds =
        ["model.unit.freeColonist", "model.unit.indenturedServant"];

    /// <summary>
    /// Changes a native settlement's alarm toward the player, clamped to
    /// [0, <see cref="NativeSettlement.MaxAlarm"/>]. The mutation point hostile acts
    /// (combat, taking land) will call in later slices (FreeCol <c>csModifyAlarm</c>).
    /// </summary>
    public void ChangeNativeAlarm(NativeSettlement settlement, int delta) =>
        settlement.Alarm = Math.Clamp(settlement.Alarm + delta, 0, NativeSettlement.MaxAlarm);

    /// <summary>Whether <paramref name="unit"/> may speak with <paramref name="settlement"/>'s chief now.</summary>
    public MoveCheck CheckVisit(Unit unit, NativeSettlement settlement)
    {
        if (!unit.IsOnMap)
        {
            return MoveCheck.No("The unit is at sea or in Europe.");
        }
        if (!unit.Type.IsPerson)
        {
            return MoveCheck.No($"A {unit.Type.ShortName} cannot speak with a settlement's chief.");
        }
        if (unit.MovementLeft <= 0)
        {
            return MoveCheck.No("No movement left this turn.");
        }
        if (unit.Position != settlement.Position && !unit.Position.IsAdjacentTo(settlement.Position))
        {
            return MoveCheck.No("Move next to the settlement to speak with its chief.");
        }
        if (settlement.HasBeenVisitedBy(unit.OwnerId))
        {
            return MoveCheck.No("You have already spoken with this settlement's chief.");
        }
        return MoveCheck.Yes(0);
    }

    /// <summary>
    /// Speaks with a settlement's chief (first contact). A <b>scout-role</b> unit gets FreeCol's full
    /// <c>scoutSpeakToChief</c> outcomes (a hateful tribe kills it; else a chance to be trained into a seasoned
    /// scout, otherwise "tales" — a wider map reveal — or "beads" — a gold gift from the settlement type's
    /// <c>&lt;gifts&gt;</c> range, +10% for an expert scout). Any other colonist gets the basic visit (tales + a
    /// small flat gift unless hateful — a documented simplification; FreeCol reserves chief audiences for scouts).
    /// Marks the settlement visited and ends the unit's turn.
    /// </summary>
    /// <returns>The gold gained (0 for tales/expert/nothing, or a slain scout).</returns>
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckVisit"/>.</exception>
    public int Visit(Unit unit, NativeSettlement settlement) => Visit(_human, unit, settlement);

    /// <summary>Speaks with a settlement's chief on behalf of <paramref name="player"/> (the unit's owner), on its own stream.</summary>
    internal int Visit(Player player, Unit unit, NativeSettlement settlement) =>
        Visit(player, unit, settlement, RandomFor(player));

    /// <summary>Speaks with a settlement's chief, drawing from the supplied <see cref="IGameRandom"/> (the per-owner stream; the overload exists for scripted tests, like <see cref="Attack(Unit, Position, IGameRandom)"/>).</summary>
    internal int Visit(Player player, Unit unit, NativeSettlement settlement, IGameRandom random)
    {
        MoveCheck check = CheckVisit(unit, settlement);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }

        settlement.MarkVisitedBy(player.PlayerId); // per-player first contact (FreeCol's per-player hasVisited)
        return unit.RoleId == ScoutRoleId
            ? ScoutSpeakToChief(player, unit, settlement, random)
            : VisitAsColonist(player, unit, settlement, random);
    }

    /// <summary>The role ability a unit must carry to found a mission (FreeCol <c>model.role.missionary</c> grants it).</summary>
    private const string EstablishMissionAbility = "model.ability.establishMission";

    /// <summary>Alarm a settlement sheds when a mission is established (FreeCol <c>ServerIndianSettlement.ALARM_NEW_MISSIONARY</c> = −100 goodwill).</summary>
    private const int AlarmNewMissionary = 100;

    /// <summary>The native unit a mission converts (FreeCol <c>model.unit.indianConvert</c>).</summary>
    public const string IndianConvertUnitTypeId = "model.unit.indianConvert";

    /// <summary>Bartolomé de las Casas's ability: on election every native convert the player holds upgrades to a free colonist (FreeCol <c>model.ability.upgradeConvert</c>).</summary>
    private const string UpgradeConvertAbility = "model.ability.upgradeConvert";

    /// <summary>Flat convert progress a mission accrues per turn (FreeCol <c>model.modifier.conversionSkill</c> +6, on the colonist base type).</summary>
    private const int ConversionSkillBonus = 6;

    /// <summary>The expert (jesuit) missionary's extra skill term (FreeCol jesuit <c>skill</c> 3; an ordinary colonist is 0).</summary>
    private const int JesuitConversionSkill = 3;

    /// <summary>Father Jean de Brébeuf's ability: every one of the player's missionaries converts as an expert jesuit (FreeCol <c>model.ability.expertMissionary</c>).</summary>
    private const string ExpertMissionaryAbility = "model.ability.expertMissionary";

    /// <summary>Percent of the settlement's alarm added to convert progress each turn (FreeCol <c>model.modifier.conversionAlarmRate</c> +2%).</summary>
    private const int ConversionAlarmRatePercent = 2;

    /// <summary>Furthest a colony may be from a converting settlement to receive the convert (FreeCol <c>ServerIndianSettlement.MAX_CONVERT_DISTANCE</c> = 10, Chebyshev).</summary>
    private const int MaxConvertDistance = 10;

    /// <summary>Base chance (percent) that winning an assault on a settlement you hold a mission in captures a brave as a convert (FreeCol <c>model.option.nativeConvertProbability</c> = 50).</summary>
    private const int NativeConvertProbabilityPercent = 50;

    /// <summary>The convert-capture modifier (FreeCol <c>model.modifier.nativeConvertBonus</c>): Juan de Sepúlveda's +20% and the Spanish <c>conquest</c> nation type's +200% raise the capture-convert chance.</summary>
    private const string NativeConvertBonusId = "model.modifier.nativeConvertBonus";

    /// <summary>Chance (percent) that winning an assault on a settlement you hold a mission in instead burns the attacker's missions across that nation (FreeCol <c>model.option.burnProbability</c> = 2; no modifier scales it).</summary>
    private const int NativeBurnProbabilityPercent = 2;

    /// <summary>
    /// Whether <paramref name="unit"/> may attempt to establish a mission at <paramref name="settlement"/> (FreeCol
    /// <c>InGameController.establishMission</c>): an on-map unit in the missionary role, with movement left, on or
    /// adjacent to the settlement. The settlement's <b>alarm does not gate the command</b> — establishing at an
    /// Angry/Hateful tribe is a legal action that simply gets the missionary killed (mirrors how a hateful tribe
    /// legally kills a visiting scout); <see cref="EstablishMission(Player, Unit, NativeSettlement)"/> decides
    /// install-vs-destroy. An existing mission (even another player's) is replaced.
    /// </summary>
    public MoveCheck CheckEstablishMission(Unit unit, NativeSettlement settlement)
    {
        if (!unit.IsOnMap)
        {
            return MoveCheck.No("The unit is at sea or in Europe.");
        }
        if (!Ruleset.Role(unit.RoleId).GrantedAbilities.GetValueOrDefault(EstablishMissionAbility))
        {
            return MoveCheck.No($"A {unit.Type.ShortName} is not a missionary.");
        }
        if (unit.MovementLeft <= 0)
        {
            return MoveCheck.No("No movement left this turn.");
        }
        if (unit.Position != settlement.Position && !unit.Position.IsAdjacentTo(settlement.Position))
        {
            return MoveCheck.No("Move next to the settlement to establish a mission.");
        }
        return MoveCheck.Yes(0);
    }

    /// <summary>
    /// Establishes a mission at <paramref name="settlement"/> with the human's missionary <paramref name="unit"/>
    /// (FreeCol <c>InGameController.establishMission</c>). If the tribe is <b>Angry or Hateful</b> the missionary is
    /// <b>killed</b> (consumed, no mission); otherwise the mission is installed (the settlement records the owner +
    /// whether the missionary was a jesuit), the settlement's <b>alarm eases by 100</b> as goodwill (FreeCol
    /// <c>ALARM_NEW_MISSIONARY</c>), the surrounding tiles are revealed at the missionary's line of sight, and the
    /// missionary is consumed into the settlement (FreeCol holds it as the settlement's missionary, not an on-map
    /// unit). Draws <b>no</b> randomness (ADR-009) — the whole mission mechanic is RNG-free.
    /// </summary>
    /// <returns><c>true</c> if the mission was installed; <c>false</c> if the missionary was killed.</returns>
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckEstablishMission"/>.</exception>
    public bool EstablishMission(Unit unit, NativeSettlement settlement) => EstablishMission(_human, unit, settlement);

    /// <summary>Establishes a mission on behalf of <paramref name="player"/> (the unit's owner). RNG-free.</summary>
    internal bool EstablishMission(Player player, Unit unit, NativeSettlement settlement)
    {
        MoveCheck check = CheckEstablishMission(unit, settlement);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }

        if (settlement.AlarmLevel >= AlarmLevel.Angry)
        {
            _units.Remove(unit); // an Angry/Hateful tribe kills the missionary (FreeCol csRemove)
            return false;
        }

        settlement.MissionOwnerId = player.PlayerId;
        settlement.MissionIsExpert = unit.Type.Id == Ruleset.Role(unit.RoleId).ExpertUnit; // jesuit (the role's expert unit) vs ordinary colonist
        ChangeNativeAlarm(settlement, -AlarmNewMissionary); // a new mission eases tension (FreeCol ALARM_NEW_MISSIONARY −100, clamped at 0)
        RevealAround(player, settlement.Position, LineOfSightOf(unit)); // missionary line-of-sight reveal
        _units.Remove(unit); // the missionary is installed as the settlement's resident, not left on the map
        return true;
    }

    /// <summary>
    /// Per-turn convert accrual for every installed mission (FreeCol <c>ServerIndianSettlement.csStartTurn</c>): a
    /// settlement with a mission gains <c>(missionary skill + 6) + 2% of its alarm</c> convert progress, and when
    /// that reaches the settlement type's threshold (classic 100) — with the settlement still big enough (size &gt; 2)
    /// and a colony of the mission's owner within <see cref="MaxConvertDistance"/> — a brave converts: the progress
    /// resets, the settlement shrinks by one, and an <see cref="IndianConvertUnitTypeId"/> musters at that colony for
    /// the owner. Otherwise the progress banks. Runs in <see cref="EndTurn"/> before the alarm decay (so it reads the
    /// alarm the turn produced, matching FreeCol). <b>Draws no randomness</b> (ADR-009): we don't pick an individual
    /// brave (we have no brave-resident model), so the whole step is deterministic — the human's stream 0 is untouched.
    /// </summary>
    internal void ProcessMissions()
    {
        foreach (NativeSettlement settlement in _nativeSettlements)
        {
            if (!settlement.HasMission || PlayerById(settlement.MissionOwnerId!.Value) is not { } owner)
            {
                continue;
            }

            // Father Jean de Brébeuf makes every one of the owner's missionaries count as an expert jesuit.
            bool expert = settlement.MissionIsExpert || HasAbilityFor(owner, ExpertMissionaryAbility);
            int skill = expert ? JesuitConversionSkill : 0;
            int alarm = Math.Min(settlement.Alarm, NativeSettlement.MaxAlarm);
            settlement.ConvertProgress += (skill + ConversionSkillBonus) + alarm * ConversionAlarmRatePercent / 100;

            int threshold = Ruleset.Settlement(settlement.SettlementTypeId).ConvertThreshold;
            if (settlement.ConvertProgress < threshold || settlement.Size <= 2
                || NearestColonyOf(owner, settlement.Position, MaxConvertDistance) is not { } colony)
            {
                continue; // bank the accrued progress (FreeCol's below-threshold / too-small / no-colony path)
            }

            settlement.ConvertProgress = 0;
            settlement.Size -= 1; // a brave converts and leaves (we don't model individual braves — a documented simplification)
            SpawnUnit(Ruleset.Unit(IndianConvertUnitTypeId), colony.Position, owner.PlayerId); // musters at the colony, lifts the owner's fog
        }
    }

    /// <summary>The basic chief visit for a non-scout colonist: reveal the surrounding lands + a small flat gift unless hateful.</summary>
    private int VisitAsColonist(Player player, Unit unit, NativeSettlement settlement, IGameRandom random)
    {
        RevealAround(player, settlement.Position, TalesRevealRadius); // tales of nearby lands
        int gift = 0;
        if (settlement.AlarmLevel != AlarmLevel.Hateful)
        {
            gift = random.Next(GiftMinimum, GiftMaximum + 1); // the visitor's own stream (the human is 0)
            player.Gold += gift;
        }
        unit.MovementLeft = 0; // speaking ends the unit's turn
        return gift;
    }

    /// <summary>
    /// A scout's audience with the chief (FreeCol <c>InGameController.scoutSpeakToChief</c>): a <b>hateful</b> tribe
    /// slays the scout; otherwise one "scouting" roll decides — the scout may be <b>trained</b> into a seasoned scout
    /// (always if the chief teaches scouting, else a 1-in-10 chance), else <b>tales</b> (a wider reveal — taken 1-in-3
    /// of the time or when the type gives no beads) or <b>beads</b> (gold from the type's <c>&lt;gifts&gt;</c> range,
    /// +10% for an already-expert scout). Draws from <paramref name="random"/>. (We don't deduct the gold from a native
    /// treasury — natives hold none, as with treasure plunder.)
    /// </summary>
    private int ScoutSpeakToChief(Player player, Unit unit, NativeSettlement settlement, IGameRandom random)
    {
        // Hateful natives kill the scout outright.
        if (settlement.AlarmLevel == AlarmLevel.Hateful)
        {
            _units.Remove(unit);
            return 0;
        }

        unit.MovementLeft = 0; // the audience ends the scout's turn
        SettlementType type = Ruleset.Settlement(settlement.SettlementTypeId);
        int rnd = random.Next(10); // FreeCol "scouting" roll

        // Trained into a seasoned scout — always if this chief teaches scouting, otherwise a 1-in-10 chance.
        bool teachesScouting = settlement.LearnableSkill == SeasonedScoutUnitTypeId;
        if (unit.Type.Id != SeasonedScoutUnitTypeId && (teachesScouting || rnd == 0))
        {
            UpgradeUnitType(unit, SeasonedScoutUnitTypeId);
            RevealAround(player, settlement.Position, TalesRevealRadius);
            return 0;
        }

        // Otherwise beads (gold) or tales (a wider reveal). Tales when there are no beads or 1-in-3 of the time.
        int gold = GiftsAmount(type, random);
        if (gold <= 0 || rnd <= 3)
        {
            RevealAround(player, settlement.Position, TalesRevealRadius); // "tales of nearby lands"
            return 0;
        }
        if (unit.Type.Id == SeasonedScoutUnitTypeId)
        {
            gold = gold * 11 / 10; // an expert scout haggles 10% more (FreeCol)
        }
        player.Gold += gold;
        RevealAround(player, settlement.Position, TalesRevealRadius);
        return gold;
    }

    /// <summary>
    /// The chief's "beads" gift from a settlement type's <c>&lt;gifts&gt;</c> range, gated by the probability roll
    /// (0 when the type has no gifts). FreeCol's <c>scoutSpeakToChief</c> uses the <b>continuous</b> RandomRange path
    /// (<c>getAmount(…, true)</c>): a fine-grained roll across the whole <c>[Min×Factor, (Max+1)×Factor)</c> band —
    /// unlike the <b>discrete</b> plunder path (<see cref="ComputePlunder"/>, <c>(rnd+Min)×Factor</c>), so gifts and
    /// plunder draw differently on purpose.
    /// </summary>
    private static int GiftsAmount(SettlementType type, IGameRandom random)
    {
        if (type.Gifts is not { } gifts)
        {
            return 0;
        }
        if (gifts.Probability < 100 && (gifts.Probability <= 0 || random.Next(100) >= gifts.Probability))
        {
            return 0;
        }
        return random.Next((gifts.Maximum - gifts.Minimum + 1) * gifts.Factor) + (gifts.Minimum * gifts.Factor);
    }

    /// <summary>Whether <paramref name="unit"/> may learn <paramref name="settlement"/>'s skill now.</summary>
    public MoveCheck CheckLearnSkill(Unit unit, NativeSettlement settlement)
    {
        if (!unit.IsOnMap)
        {
            return MoveCheck.No("The unit is at sea or in Europe.");
        }
        if (unit.Position != settlement.Position && !unit.Position.IsAdjacentTo(settlement.Position))
        {
            return MoveCheck.No("Move next to the settlement to learn from it.");
        }
        if (settlement.LearnableSkill is null)
        {
            return MoveCheck.No("This settlement has no skill to teach.");
        }
        if (settlement.SkillConsumed)
        {
            return MoveCheck.No("This settlement has already taught its skill.");
        }
        if (!SkillLearnerTypeIds.Contains(unit.Type.Id))
        {
            return MoveCheck.No($"A {unit.Type.ShortName} cannot learn a new skill here.");
        }
        if (settlement.AlarmLevel >= AlarmLevel.Angry)
        {
            return MoveCheck.No("The settlement is too hostile to teach you.");
        }
        return MoveCheck.Yes(0);
    }

    /// <summary>
    /// Learns the settlement's skill: the colonist is taught the expert profession
    /// (e.g. free colonist → expert farmer). The settlement's skill is then consumed —
    /// unless it is a capital, which teaches indefinitely (FreeCol). Ends the unit's turn.
    /// </summary>
    /// <returns>The upgraded unit (a new unit of the expert type, keeping the id and place).</returns>
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckLearnSkill"/>.</exception>
    public Unit LearnSkill(Unit unit, NativeSettlement settlement)
    {
        MoveCheck check = CheckLearnSkill(unit, settlement);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }

        Unit expert = UpgradeUnitType(unit, settlement.LearnableSkill!);
        expert.MovementLeft = 0; // learning ends the unit's turn
        if (!settlement.IsCapital)
        {
            settlement.SkillConsumed = true; // capitals never run out
        }
        return expert;
    }

    /// <summary>Base price per good unit in native trade (FreeCol <c>IndianSettlement.GOODS_BASE_PRICE</c>).</summary>
    private const int NativeGoodsBasePrice = 12;

    /// <summary>
    /// What a settlement pays for <paramref name="amount"/> of <paramref name="goodsId"/> you sell it
    /// (FreeCol <c>getPriceToSell</c> ≈ <c>amount + 11·getPriceToBuy/10</c>): a per-unit base of
    /// <c>12 + the settlement's trade bonus</c>, times a wanted-goods premium (150 / 125 / 110% for its
    /// 1st / 2nd / 3rd wanted good), then the classic <b>ship-trade penalty</b> (a settlement pays a
    /// ship-borne trader less — <see cref="DifficultyOptions.ShipTradePenalty"/>, −30% at medium; FreeCol
    /// <c>model.option.shipTradePenalty</c> applied with <c>sense=true</c> for the player's sale). Native
    /// trade here is ship-only (<see cref="CheckSellToNatives"/>), so the penalty always applies. Settlement
    /// goods stock — which lowers the price as they fill up — is not modelled yet, so the price assumes they
    /// still want it.
    /// </summary>
    public int NativeSalePrice(NativeSettlement settlement, string goodsId, int amount)
    {
        int full = NativeGoodsBasePrice + Ruleset.Settlement(settlement.SettlementTypeId).TradeBonus;
        int wantedMultiplier = settlement.WantedSlot(goodsId) switch
        {
            0 => 150,
            1 => 125,
            2 => 110,
            _ => 100,
        };
        int perUnit = full * wantedMultiplier / 100;
        int price = amount + (11 * perUnit * amount) / 10;
        // The ship-trade penalty is a percentage modifier on the whole sale price (FreeCol applyModifiers).
        return price * (100 + Ruleset.Difficulty.ShipTradePenalty) / 100;
    }

    /// <summary>Whether <paramref name="ship"/> may sell <paramref name="amount"/> of a good to <paramref name="settlement"/> now.</summary>
    public MoveCheck CheckSellToNatives(Unit ship, NativeSettlement settlement, string goodsId, int amount)
    {
        if (!ship.Type.IsCarrier || !ship.IsOnMap)
        {
            return MoveCheck.No("Only a ship on the map can trade with a settlement.");
        }
        if (ship.Position != settlement.Position && !ship.Position.IsAdjacentTo(settlement.Position))
        {
            return MoveCheck.No("The ship must be next to the settlement to trade.");
        }
        if (settlement.AlarmLevel >= AlarmLevel.Angry)
        {
            return MoveCheck.No("The settlement is too hostile to trade.");
        }
        if (amount <= 0)
        {
            return MoveCheck.No("Nothing to sell.");
        }
        if (ship.CargoOf(goodsId) < amount)
        {
            return MoveCheck.No($"The ship is not carrying {amount} {goodsId}.");
        }
        return MoveCheck.Yes(NativeSalePrice(settlement, goodsId, amount));
    }

    /// <summary>
    /// Sells goods from a ship's hold to an adjacent native settlement for gold (no European tax),
    /// at the native price. Trading builds goodwill (lowers the settlement's alarm) and ends the
    /// ship's turn. (Buying from settlements needs a settlement goods-stock model — a later slice;
    /// inland settlements need wagon trains, also later, so only coastal settlements are reachable today.)
    /// </summary>
    /// <returns>The gold received.</returns>
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckSellToNatives"/>.</exception>
    public int SellToNatives(Unit ship, NativeSettlement settlement, string goodsId, int amount) =>
        SellToNatives(_human, ship, settlement, goodsId, amount);

    /// <summary>Sells goods to a native settlement on behalf of <paramref name="player"/> (the ship's owner).</summary>
    internal int SellToNatives(Player player, Unit ship, NativeSettlement settlement, string goodsId, int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);
        MoveCheck check = CheckSellToNatives(ship, settlement, goodsId, amount);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }
        int price = check.Cost;
        ship.AddCargo(goodsId, -amount);
        player.Gold += price; // natives pay in gold; no European market tax
        ChangeNativeAlarm(settlement, -Math.Max(1, price / 50)); // goodwill (FreeCol ALARM_BONUS_SELL ≈ 20% → price/50; min 1 per trade)
        ship.MovementLeft = 0; // opening a trade session ends the ship's turn
        return price;
    }

    // ===== Combat (Phase 5 slice 5b): roles/equipment + the attack action =====

    private const string AutomaticPromotionAbility = "model.ability.automaticPromotion"; // George Washington
    private const string AutomaticEquipmentAbility = "model.ability.automaticEquipment";  // Paul Revere
    private const string CaptureUnitsAbility = "model.ability.captureUnits";
    private const string CaptureEquipmentAbility = "model.ability.captureEquipment";
    private const string PlunderNativesAbility = "model.ability.plunderNatives"; // Hernán Cortés
    private const string RumoursAlwaysPositiveAbility = "model.ability.rumoursAlwaysPositive"; // Hernando de Soto
    private const string OffenceModifierId = "model.modifier.offence"; // Francis Drake (+50%, scoped to privateers)
    private const string DefenceModifierId = "model.modifier.defence";
    private const string HasPortAbility = "model.ability.hasPort"; // a coastal colony — docks/drydock/shipyard gate

    /// <summary>
    /// A unit's offence base for combat: the type's pre-role additive plus its role's offence, then the
    /// type's own percentage multiplier (so a veteran soldier's +50% applies to base <em>and</em> role —
    /// FreeCol's single index-ordered fold; the situational percentages come later in <see cref="CombatModel"/>),
    /// finally the owner's scoped Founding-Father combat factor (Francis Drake +50% for privateers).
    /// </summary>
    internal double OffenceBase(Unit unit) =>
        (unit.Type.OffenceAdditive + Ruleset.Role(unit.RoleId).Offence) * unit.Type.OffenceMultiplier
        * FatherCombatFactor(unit, OffenceModifierId);

    /// <summary>A unit's defence base for combat: the type's pre-role additive plus its (effective) role's defence, the type's percentage multiplier, then the owner's scoped Founding-Father combat factor (Drake).</summary>
    internal double DefenceBase(Unit unit) =>
        (unit.Type.DefenceAdditive + Ruleset.Role(EffectiveCombatRole(unit, defending: true)).Defence)
        * unit.Type.DefenceMultiplier
        * FatherCombatFactor(unit, DefenceModifierId);

    /// <summary>
    /// The combat-power factor from the unit owner's elected Founding Fathers whose scoped offence/defence
    /// modifier (<paramref name="targetId"/>) applies to this unit's type — <b>Francis Drake</b>'s +50% for
    /// privateers is the only one in the classic ruleset. Folded as a power <em>multiplier</em>: every
    /// <see cref="CombatModel"/> situational factor is multiplicative, so an index-50 percentage commutes with
    /// the base and its position is immaterial (correct for percentage/multiplicative modifiers — the only one
    /// is Drake). Returns 1.0 for a native-owned unit (no Congress) or when no matching father is elected.
    /// </summary>
    private double FatherCombatFactor(Unit unit, string targetId)
    {
        if (unit.OwnerNationId is not null || PlayerById(unit.OwnerId) is not { } owner)
        {
            return 1.0;
        }
        double factor = 1.0;
        foreach (FatherModifier modifier in owner.Congress.Select(Ruleset.Father)
            .SelectMany(f => f.Modifiers)
            .Where(m => m.TargetId == targetId && m.AppliesTo(unit.Type.Id))
            .OrderBy(m => m.Index))
        {
            factor = modifier.ApplyTo(factor);
        }
        return factor;
    }

    private const string OffenceAgainstId = "model.modifier.offenceAgainst"; // Spanish conquest (+50% vs natives, scope isIndian)

    /// <summary>
    /// The contextual offence factor the attacker's <b>nation type</b> grants against a <b>native</b> defender (FreeCol
    /// <c>model.modifier.offenceAgainst</c> scoped <c>isIndian</c>) — the Spanish <c>conquest</c> advantage, <b>+50%
    /// vs natives</b>. 1.0 when the defender is not native, or the attacker has no European nation / no such modifier
    /// (the human defaults to no nation). The modifier's <c>isIndian</c> scope is satisfied by the
    /// <see cref="Unit.IsNative"/> gate here. Folded multiplicatively, like every situational combat factor.
    /// </summary>
    private double OffenceAgainstNativeFactor(Unit attacker, Unit defender)
    {
        if (!defender.IsNative || attacker.OwnerNationId is not null || PlayerById(attacker.OwnerId) is not { } owner)
        {
            return 1.0;
        }
        double factor = 1.0;
        foreach (FatherModifier modifier in NationTypeModifiers(owner, OffenceAgainstId))
        {
            factor = modifier.ApplyTo(factor);
        }
        return factor;
    }

    /// <summary>
    /// The probability (0–1) that winning an assault on a settlement the attacker holds a mission in captures a brave
    /// as an Indian Convert (FreeCol <c>Unit.getConvertProbability</c> = 0.01 × the <c>nativeConvertProbability</c>
    /// option (50), raised by the attacker's <see cref="NativeConvertBonusId"/> modifiers — <b>Juan de Sepúlveda</b>
    /// +20% (a founding-father modifier) and the Spanish <b>conquest</b> nation type +200%, stacked index-ordered like
    /// every modifier fold; capped at 1.0). Returns the bare base for a player with no such modifier (the human's
    /// default), so an ordinary captor's chance is unchanged.
    /// </summary>
    private double NativeConvertProbability(Player owner)
    {
        double percent = NativeConvertProbabilityPercent;
        foreach (FatherModifier modifier in owner.Congress.Select(Ruleset.Father)
                     .SelectMany(f => f.Modifiers)
                     .Where(m => m.TargetId == NativeConvertBonusId)
                     .Concat(NationTypeModifiers(owner, NativeConvertBonusId))
                     .OrderBy(m => m.Index))
        {
            percent = modifier.ApplyTo(percent);
        }
        return Math.Min(1.0, percent / 100.0);
    }

    /// <summary>
    /// The natives burn every mission <paramref name="ownerId"/> holds across <paramref name="nationId"/>'s settlements
    /// (FreeCol <c>ServerPlayer.csBurnMissions</c>): for each settlement of that nation whose resident missionary is the
    /// attacker's, the mission is cleared (owner/expert/convert-progress reset). RNG-free.
    /// </summary>
    private void BurnMissionsOf(int ownerId, string nationId)
    {
        foreach (NativeSettlement s in _nativeSettlements.Where(s => s.NationTypeId == nationId && s.MissionOwnerId == ownerId))
        {
            s.MissionOwnerId = null;
            s.MissionIsExpert = false;
            s.ConvertProgress = 0;
        }
    }

    /// <summary>
    /// The role a unit fights in. A defender may be automatically equipped for the fight (FreeCol
    /// <c>getAutomaticRole</c>): an unarmed unit in a colony whose owner has the automatic-equipment
    /// ability (Paul Revere → soldier) and whose colony stocks the equipment. (5b applies the resulting
    /// defence bonus only; it does not permanently consume the goods — that persistence is a later slice.)
    /// </summary>
    internal string EffectiveCombatRole(Unit unit, bool defending)
    {
        if (!defending || !unit.HasDefaultRole)
        {
            return unit.RoleId;
        }
        if (ColonyAt(unit.Position) is not { } colony)
        {
            return unit.RoleId; // auto-equip only inside a friendly colony
        }
        foreach (string roleId in AutoEquipRoleScopes(unit))
        {
            RoleType role = Ruleset.Role(roleId);
            if (role.RequiredGoods.All(g => colony.StoreOf(Ruleset.StorageIdOf(g.GoodsId)) >= g.Amount))
            {
                return roleId;
            }
        }
        return unit.RoleId;
    }

    /// <summary>
    /// The roles a unit's owner can be automatically equipped into when defending. A colonial player draws on
    /// its own elected Continental Congress (Paul Revere scopes the soldier role); native auto-equipment is
    /// deferred until native settlements stock goods, so a native-owned unit yields none for now.
    /// </summary>
    private IEnumerable<string> AutoEquipRoleScopes(Unit unit) =>
        unit.OwnerNationId is not null || PlayerById(unit.OwnerId) is not { } owner
            ? []
            : owner.Congress.Select(Ruleset.Father)
                .SelectMany(f => f.Abilities)
                .Where(a => a.Id == AutomaticEquipmentAbility && a.Value)
                .SelectMany(a => a.ScopeTypes);

    /// <summary>
    /// The strongest enemy of <paramref name="attacker"/> standing on a tile, or null. Ranked by full <em>computed</em>
    /// defence power (terrain / fortify / settlement / naval-cargo), not raw base defence — FreeCol
    /// <c>Unit.betterDefender</c> picks the best actual defender, so a dug-in or walled weaker unit can outrank a
    /// stronger one in the open. Ties broken by unit id for determinism (ADR-009).
    /// </summary>
    internal Unit? DefenderAt(Unit attacker, Position p) =>
        _units.Where(u => u.IsOnMap && AreEnemies(attacker, u) && u.Position == p)
            .OrderByDescending(u => DefencePowerOf(attacker, u, p))
            .ThenBy(u => u.Id)
            .FirstOrDefault();

    /// <summary>
    /// The full defence power a unit would defend <paramref name="target"/> with against <paramref name="attacker"/>
    /// (FreeCol <c>SimpleCombatModel.getDefencePower</c>): the same context the attack resolution builds — terrain
    /// cover (none on water / in a settlement), the dig-in bonus, the colony's fortification bonus, the artillery
    /// in-the-open penalty / against-raid bonus, and the naval cargo penalty.
    /// </summary>
    private double DefencePowerOf(Unit attacker, Unit defender, Position target)
    {
        bool naval = defender.Type.IsNaval;
        bool inColony = !naval && ColonyAt(target) is not null;
        var context = new DefenceContext(
            TerrainDefenceBonus: (naval || inColony) ? 0 : Map.TerrainAt(target).DefenceBonus,
            Fortified: !naval && defender.IsFortified,
            SettlementDefenceBonus: naval ? 0 : ColonyDefenceBonusAt(target),
            ArtilleryInOpen: !naval && defender.Type.Bombard && !inColony && !defender.IsFortified,
            ArtilleryAgainstRaid: !naval && inColony && defender.Type.Bombard && attacker.IsNative,
            GoodsCarried: naval ? GoodsSlotsUsed(defender) : 0);
        return CombatModel.DefencePower(DefenceBase(defender), context);
    }

    /// <summary>A free, in-bounds land tile adjacent to a centre (no settlement or unit on it), or null.</summary>
    private Position? FreeAdjacentLand(Position centre) =>
        centre.Neighbours()
            .Where(n => Map.InBounds(n) && !Map.TerrainAt(n).IsWater
                        && ColonyAt(n) is null && NativeSettlementAt(n) is null
                        && !_units.Any(u => u.IsOnMap && u.Position == n))
            .Cast<Position?>()
            .FirstOrDefault();

    /// <summary>
    /// Applies a combat tension change (FreeCol <c>defenderTension</c>) to every settlement of a native
    /// nation — its alarm toward the player (positive after a European win, negative after a repelled
    /// attack). FreeCol propagates the full delta to all the nation's settlements (<c>csModifyTension</c>).
    /// </summary>
    private void ApplyNativeCombatTension(string nationTypeId, int defenderTension)
    {
        if (defenderTension == 0)
        {
            return;
        }
        // Combat tension is applied RAW (FreeCol): the nativeAlarmModifier (Pocahontas −50%) damps only the
        // per-turn ambient proximity alarm — see ApplyAmbientNativeAlarm.
        foreach (NativeSettlement s in _nativeSettlements.Where(s => s.NationTypeId == nationTypeId))
        {
            ChangeNativeAlarm(s, defenderTension);
        }
    }

    /// <summary>
    /// The native combat tension a European victory or defeat inflicts on the defending nation
    /// (FreeCol <c>defenderTension</c>): a win adds the slain defender's slaughter tension (+ a minor
    /// insult); a loss subtracts a minor insult, and a further <c>NORMAL</c> if the attacker was slain.
    /// </summary>
    private static int DefenderCombatTension(bool attackerWon, int slaughterTension, bool attackerSlain) =>
        attackerWon
            ? slaughterTension + NativeSettlement.TensionAddMinor
            : -(NativeSettlement.TensionAddMinor + (attackerSlain ? NativeSettlement.TensionAddNormal : 0));

    /// <summary>The attacker's movement-spent penalty (FreeCol: 1 point left → big, 2 → small).</summary>
    private static MovementPenalty MovementPenaltyFor(Unit attacker) => attacker.MovementLeft switch
    {
        1 => MovementPenalty.Big,
        2 => MovementPenalty.Small,
        _ => MovementPenalty.None,
    };

    /// <summary>Whether <paramref name="colony"/> can ordain a missionary — it holds a building granting <c>model.ability.dressMissionary</c> (a church or cathedral).</summary>
    private bool ColonyDressesMissionary(Colony colony) =>
        colony.Buildings.Any(b => Ruleset.Building(b).DressesMissionary);

    /// <summary>Whether <paramref name="unit"/> may equip into <paramref name="targetRoleId"/> at <paramref name="colony"/> now.</summary>
    public MoveCheck CheckEquipRole(Unit unit, Colony colony, string targetRoleId)
    {
        if (unit.IsNative)
        {
            return MoveCheck.No("Native units are not equipped this way.");
        }
        if (!unit.IsOnMap)
        {
            return MoveCheck.No("The unit is at sea or in Europe.");
        }
        if (!unit.Type.IsPerson)
        {
            return MoveCheck.No($"A {unit.Type.ShortName} cannot change its equipment.");
        }
        if (unit.Position != colony.Position)
        {
            return MoveCheck.No("Stand in the colony to change equipment.");
        }
        RoleType target = Ruleset.Role(targetRoleId);
        if (target.RequiresNative || target.RequiresRef)
        {
            return MoveCheck.No($"A colonist cannot take the {target.ShortName} role.");
        }
        // The missionary role needs the colony to ordain it — only a church or cathedral grants dressMissionary
        // (FreeCol's role required-ability resolved against the colony's buildings), so a chapel-only or church-less
        // colony cannot dress a missionary.
        if (target.RequiresDressMissionary && !ColonyDressesMissionary(colony))
        {
            return MoveCheck.No("Only a church or cathedral can ordain a missionary — build one first.");
        }
        foreach ((string goodsId, int amount) in RoleGoodsDelta(unit, target))
        {
            if (amount > 0 && colony.StoreOf(Ruleset.StorageIdOf(goodsId)) < amount)
            {
                return MoveCheck.No($"The colony lacks {amount} {goodsId[(goodsId.LastIndexOf('.') + 1)..]}.");
            }
        }
        return MoveCheck.Yes(0);
    }

    /// <summary>
    /// Equips a colonist into a role, consuming the required-goods difference from the colony store
    /// (and refunding goods the change drops). Arming a free colonist into the soldier role spends
    /// 50 muskets; into the dragoon role, 50 muskets and 50 horses.
    /// </summary>
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckEquipRole"/>.</exception>
    public void EquipRole(Unit unit, Colony colony, string targetRoleId)
    {
        MoveCheck check = CheckEquipRole(unit, colony, targetRoleId);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }
        RoleType target = Ruleset.Role(targetRoleId);
        foreach ((string goodsId, int amount) in RoleGoodsDelta(unit, target))
        {
            colony.AddGoods(Ruleset.StorageIdOf(goodsId), -amount); // consume positive deltas, refund negative
        }
        ChangeRole(unit, targetRoleId, targetRoleId == RoleType.DefaultRoleId ? 0 : 1);
    }

    /// <summary>
    /// The per-good change in equipment to move from a unit's current role to <paramref name="target"/>
    /// (at a single equipment count): positive = consumed from the store, negative = refunded.
    /// </summary>
    private IEnumerable<(string GoodsId, int Amount)> RoleGoodsDelta(Unit unit, RoleType target)
    {
        var delta = new Dictionary<string, int>();
        foreach (RoleRequiredGoods g in target.RequiredGoods)
        {
            delta[g.GoodsId] = delta.GetValueOrDefault(g.GoodsId) + g.Amount;
        }
        foreach (RoleRequiredGoods g in Ruleset.Role(unit.RoleId).RequiredGoods)
        {
            delta[g.GoodsId] = delta.GetValueOrDefault(g.GoodsId) - (g.Amount * Math.Max(1, unit.RoleCount));
        }
        return delta.Where(kv => kv.Value != 0).Select(kv => (kv.Key, kv.Value));
    }

    /// <summary>Sets a unit's role and equipment count (the default role always has count 0).</summary>
    private static void ChangeRole(Unit unit, string roleId, int count)
    {
        unit.RoleId = roleId;
        unit.RoleCount = roleId == RoleType.DefaultRoleId ? 0 : Math.Max(1, count);
    }

    /// <summary>
    /// Whether <paramref name="unit"/> can clear its learned speciality back to a free colonist now (FreeCol
    /// <c>InGameController.clearSpeciality</c>): an on-map colonial specialist that has a <c>clearSkill</c> unit-change
    /// (every expert/master/preacher/etc. — not a free colonist, servant, criminal or non-person). FreeCol forbids
    /// clearing a <em>teacher</em>'s speciality; our teachers are in-colony building workers, never on-map units, so the
    /// on-map gate already covers that case.
    /// </summary>
    public MoveCheck CheckClearSkill(Unit unit)
    {
        if (unit.IsNative)
        {
            return MoveCheck.No("Native units have no learned speciality to clear.");
        }
        if (!unit.IsOnMap)
        {
            return MoveCheck.No("The unit is at sea or in Europe.");
        }
        if (Ruleset.GetUnitChange(UnitChangeTypeIds.ClearSkill, unit.Type.Id) is null)
        {
            return MoveCheck.No($"A {unit.Type.ShortName} has no speciality to clear.");
        }
        return MoveCheck.Yes(0);
    }

    /// <summary>
    /// Clears <paramref name="unit"/>'s speciality, reverting a specialist (expert farmer, master carpenter, …) to a
    /// plain free colonist (FreeCol <c>clearSpeciality</c>). RNG-free; no save change (a unit type swap).
    /// </summary>
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckClearSkill"/>.</exception>
    public void ClearSkill(Unit unit)
    {
        MoveCheck check = CheckClearSkill(unit);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }
        UpgradeUnitType(unit, Ruleset.GetUnitChange(UnitChangeTypeIds.ClearSkill, unit.Type.Id)!.To);
    }

    /// <summary>
    /// Whether <paramref name="attacker"/> may attack the strongest enemy on <paramref name="target"/> now. An "enemy"
    /// is decided by <see cref="AreEnemies"/>: a native pair is hostile on owner-inequality (native units may attack
    /// from slice 1b — restricting a brave to human targets is the native AI's job, <see cref="NearestHumanUnit"/>,
    /// not this legality check), while a colonial pair is hostile only at <see cref="Stance.War"/> or
    /// <see cref="Stance.Uncontacted"/> (86d3drn45). So an attack on a colonial power you are at <see cref="Stance.Peace"/>,
    /// <see cref="Stance.CeaseFire"/> or <see cref="Stance.Alliance"/> with returns "no enemy to attack there" — you
    /// must declare war first; an attack while Uncontacted is still allowed and is what declares the war (FreeCol).
    /// </summary>
    public MoveCheck CheckAttack(Unit attacker, Position target)
    {
        if (!attacker.IsOnMap)
        {
            return MoveCheck.No("The unit is at sea or in Europe.");
        }
        if (!Map.InBounds(target))
        {
            return MoveCheck.No("Target is off the map.");
        }
        if (!attacker.Position.IsAdjacentTo(target))
        {
            return MoveCheck.No("Attack an adjacent tile.");
        }
        if (attacker.MovementLeft <= 0)
        {
            return MoveCheck.No("No movement left this turn.");
        }
        if (OffenceBase(attacker) <= 0)
        {
            return MoveCheck.No($"A {attacker.Type.ShortName} has no offensive strength — arm it first.");
        }
        if (DefenderAt(attacker, target) is not { } defender)
        {
            return MoveCheck.No("There is no enemy to attack there.");
        }
        if (defender.Type.IsNaval != attacker.Type.IsNaval)
        {
            // Ships and land units don't fight each other directly (FreeCol Unit.canAttack); a ship attacks ships,
            // a land unit attacks land units. (Ship bombardment of land / forts firing on ships is a later slice.)
            return MoveCheck.No("Ships and land units cannot attack each other directly.");
        }
        return MoveCheck.Yes(0);
    }

    /// <summary>The two final combat powers and the attacker's win probability for a pre-combat preview (`86d3c9xmw`).</summary>
    /// <param name="AttackPower">The attacker's folded offence power (the same figure <c>Attack</c> resolves with).</param>
    /// <param name="DefencePower">The best defender's folded defence power.</param>
    /// <param name="WinProbability">The attacker's win chance, <c>attack / (attack + defence)</c>.</param>
    public sealed record CombatOdds(double AttackPower, double DefencePower, double WinProbability);

    /// <summary>
    /// The combat odds an attack on <paramref name="target"/> would face — the attacker's and the strongest
    /// defender's final powers plus the win probability — or <c>null</c> when there is no legal defender. A pure,
    /// <b>side-effect-free preview</b> for the pre-combat odds dialog: it draws no RNG, declares no war, and spends
    /// no movement, yet returns exactly the figures <c>Attack</c> resolves with (both share
    /// <see cref="CombatPowers"/>). ADR-006 read oracle.
    /// </summary>
    public CombatOdds? CombatOddsAgainst(Unit attacker, Position target)
    {
        if (DefenderAt(attacker, target) is not { } defender)
        {
            return null;
        }
        (double attack, double defence) = CombatPowers(attacker, defender, target);
        return new CombatOdds(attack, defence, CombatModel.WinProbability(attack, defence));
    }

    /// <summary>
    /// The attacker's and the best defender's final combat powers — the single shared computation behind
    /// <c>Attack</c> and <see cref="CombatOddsAgainst"/>, folding naval / colony-defence / ambush /
    /// artillery-in-the-open / cargo context and the Spanish-conquest offence-vs-native factor. No side effects
    /// (no RNG, no mutation), so the preview and the resolved attack can never drift apart.
    /// </summary>
    private (double Attack, double Defence) CombatPowers(Unit attacker, Unit defender, Position target)
    {
        bool naval = defender.Type.IsNaval;
        bool inColony = !naval && ColonyAt(target) is not null;
        bool attackerInColony = ColonyAt(attacker.Position) is not null;
        bool ambush = !naval && !inColony && !attackerInColony && attacker.IsNative && !defender.IsFortified
            && (Map.TerrainAt(attacker.Position).AmbushTerrain || Map.TerrainAt(target).AmbushTerrain);
        var ctx = new AttackContext(
            Movement: MovementPenaltyFor(attacker),
            ArtilleryInOpen: !naval && attacker.Type.Bombard && !attackerInColony && !attacker.IsFortified && !inColony,
            AmbushBonus: ambush ? Map.TerrainAt(target).DefenceBonus : 0,
            GoodsCarried: naval ? GoodsSlotsUsed(attacker) : 0);
        double attack = CombatModel.AttackPower(OffenceBase(attacker) * OffenceAgainstNativeFactor(attacker, defender), ctx);
        double defence = DefencePowerOf(attacker, defender, target);
        return (attack, defence);
    }

    /// <summary>
    /// Resolves an attack on the strongest defender at <paramref name="target"/> through the pure
    /// <see cref="CombatModel"/> (drawing from the game's main saved RNG, so combat is resume-deterministic),
    /// applies the graded outcome, raises the attacked nation's alarm, and ends the attacker's turn.
    /// Open-field unit combat only (Phase 5 slice 5b): assaulting the settlement itself is slice 5c.
    /// </summary>
    /// <returns>The graded combat result.</returns>
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckAttack"/>.</exception>
    public CombatResult Attack(Unit attacker, Position target) => Attack(attacker, target, _random);

    /// <summary>
    /// The attack resolution, drawing from an explicit RNG. Production uses the game's main saved RNG
    /// (resume-deterministic); tests inject a fixed RNG to force a chosen outcome band.
    /// </summary>
    internal CombatResult Attack(Unit attacker, Position target, IGameRandom random)
    {
        MoveCheck check = CheckAttack(attacker, target);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }

        Unit defender = DefenderAt(attacker, target)!;
        int attackerId = attacker.Id; // ids survive a promotion/demotion swap; the object reference may not
        int defenderId = defender.Id;
        string? defenderNation = defender.OwnerNationId;

        // Attacking a rival colonial player's unit declares war and spikes tension, both ways (FreeCol: Europeans
        // go to war on the act of attacking, win or lose). A no-op for native defenders, who stay on the alarm
        // system handled below. This only records the relationship — it does not gate the attack (FP-6a).
        // Privateers (model.ability.piracy) are the exception: raiding with one — or being raided by one — provokes
        // NO stance change (FreeCol csCombat), so privateers can plunder rival shipping deniably (1c-3d).
        if (defenderNation is null && !attacker.Type.Piracy && !defender.Type.Piracy)
        {
            SetStance(attacker.OwnerId, defender.OwnerId, Stance.War);
            ChangeTension(attacker.OwnerId, defender.OwnerId, TensionWar);
        }

        // Naval combat (ship vs ship): the defender may evade; a land unit can't stand on water, so a naval
        // defender means ship-vs-ship resolution below. The two final combat powers (terrain/fortify/settlement/
        // ambush/artillery/cargo all folded in) come from the shared, side-effect-free CombatPowers — the same
        // figures the pre-combat odds preview shows (see CombatOddsAgainst).
        bool naval = defender.Type.IsNaval;
        (double attackPower, double defencePower) = CombatPowers(attacker, defender, target);

        // Attacking ends the attacker's turn now — before any promotion/demotion that swaps the unit
        // object (UpgradeUnitType copies MovementLeft, so the swapped unit inherits the spent turn).
        attacker.MovementLeft = 0;

        double winProbability = CombatModel.WinProbability(attackPower, defencePower);
        CombatResult result = naval
            ? CombatModel.ResolveNaval(winProbability, random)
            : CombatModel.Resolve(winProbability, random);
        if (result == CombatResult.Evade)
        {
            return result; // the defender dodged: nobody hurt, the attacker's turn already spent (FreeCol csEvadeAttack)
        }
        bool attackerWon = result is CombatResult.GreatWin or CombatResult.Win;
        bool great = result is CombatResult.GreatWin or CombatResult.GreatLoss;
        Unit winner = attackerWon ? attacker : defender;
        Unit loser = attackerWon ? defender : attacker;

        ResolveLoserOutcome(winner, loser, great);
        ApplyWinnerPromotion(winner, great, random);

        // Native alarm shifts across the defender's whole nation by FreeCol's defenderTension: a European
        // win raises it (the slain brave in the open + a minor insult); a repelled attack lowers it.
        // (FreeCol also short-circuits this for a piracy attacker, but a privateer can't reach a native today —
        // the naval-vs-land gate blocks it and natives have no ships — so no `!attacker.Type.Piracy` guard yet.)
        if (defenderNation is not null)
        {
            int slaughter = _units.Any(u => u.Id == defenderId) ? 0 : NativeSettlement.TensionAddUnitDestroyed;
            bool attackerSlain = !_units.Any(u => u.Id == attackerId);
            ApplyNativeCombatTension(defenderNation, DefenderCombatTension(attackerWon, slaughter, attackerSlain));
        }

        return result;
    }

    /// <summary>Whether <paramref name="attacker"/> may assault the native settlement on <paramref name="target"/> now.</summary>
    public MoveCheck CheckAttackSettlement(Unit attacker, Position target)
    {
        if (!attacker.IsOnMap)
        {
            return MoveCheck.No("The unit is at sea or in Europe.");
        }
        if (attacker.IsNative)
        {
            return MoveCheck.No("Native units do not assault settlements."); // braves raid units (1b), not settlements
        }
        if (!Map.InBounds(target))
        {
            return MoveCheck.No("Target is off the map.");
        }
        if (!attacker.Position.IsAdjacentTo(target))
        {
            return MoveCheck.No("Attack an adjacent tile.");
        }
        if (attacker.MovementLeft <= 0)
        {
            return MoveCheck.No("No movement left this turn.");
        }
        if (OffenceBase(attacker) <= 0)
        {
            return MoveCheck.No($"A {attacker.Type.ShortName} has no offensive strength — arm it first.");
        }
        if (NativeSettlementAt(target) is null)
        {
            return MoveCheck.No("There is no native settlement to attack there.");
        }
        return MoveCheck.Yes(0);
    }

    /// <summary>
    /// Assaults the native settlement on <paramref name="target"/> through the pure <see cref="CombatModel"/>
    /// (main saved RNG, resume-deterministic). The settlement is defended by an implicit garrison (a brave's
    /// defence with the settlement's defence bonus); a win sacks it — plunder gold, alarm raised on the
    /// nation's other settlements, the settlement destroyed — while a loss disarms/demotes the attacker.
    /// Natives-only (Phase 5 slice 5c); naval and foreign-European combat are the foreign-powers slice.
    /// </summary>
    /// <returns>The graded combat result.</returns>
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckAttackSettlement"/>.</exception>
    public CombatResult AttackSettlement(Unit attacker, Position target) => AttackSettlement(attacker, target, _random);

    /// <summary>The settlement-assault resolution drawing from an explicit RNG (tests inject a fixed RNG).</summary>
    internal CombatResult AttackSettlement(Unit attacker, Position target, IGameRandom random)
    {
        MoveCheck check = CheckAttackSettlement(attacker, target);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }

        NativeSettlement settlement = NativeSettlementAt(target)!;
        SettlementType type = Ruleset.Settlement(settlement.SettlementTypeId);
        // The settlement's implicit garrison defender: a brave with the settlement's defence bonus
        // (FreeCol suppresses the open-tile terrain bonus and ambush inside a settlement).
        var defender = new Unit(0, Ruleset.Unit(BraveUnitTypeId), target) { OwnerNationId = settlement.NationTypeId };

        var attackContext = new AttackContext(Movement: MovementPenaltyFor(attacker));
        var defenceContext = new DefenceContext(SettlementDefenceBonus: type.DefenceModifier);
        // Spanish conquest +50% vs natives (the settlement's implicit defender is a brave — always native).
        double attackPower = CombatModel.AttackPower(OffenceBase(attacker) * OffenceAgainstNativeFactor(attacker, defender), attackContext);
        double defencePower = CombatModel.DefencePower(DefenceBase(defender), defenceContext);

        bool hasPlunderAbility = AbilityForUnit(attacker, PlunderNativesAbility); // Cortés
        int attackerId = attacker.Id;
        string nation = settlement.NationTypeId;
        bool capital = settlement.IsCapital;
        attacker.MovementLeft = 0; // attacking ends the attacker's turn (before any promotion swap)

        CombatResult result = CombatModel.Resolve(CombatModel.WinProbability(attackPower, defencePower), random);
        bool attackerWon = result is CombatResult.GreatWin or CombatResult.Win;
        bool great = result is CombatResult.GreatWin or CombatResult.GreatLoss;

        if (attackerWon)
        {
            ApplyWinnerPromotion(attacker, great, random); // promotion draw (if any) before the plunder draws
            // Sacking the settlement yields treasure as a TREASURE TRAIN on the razed tile (FreeCol csDestroySettlement),
            // NOT instant gold — the attacker must escort it to a colony to cash it in. The plunder draw is unchanged
            // (same RNG sequence as the old instant-gold path), so determinism/byte-stability holds (ADR-009).
            int plunder = ComputePlunder(type, hasPlunderAbility, random);
            if (plunder > 0)
            {
                SpawnTreasureTrain(target, attacker.OwnerId, plunder);
            }
            // Capture-convert (low roll) OR burn-missions (high roll) — FreeCol's two `SimpleCombatModel`
            // CAPTURE_CONVERT / BURN_MISSIONS branches off the SAME post-win roll, fired only when the attacker holds
            // THIS settlement's mission (so an ordinary assault draws nothing — ADR-009, no churn). A low roll converts
            // a brave onto the attacker's tile (chance raised by Juan de Sepúlveda / the Spanish conquest type's
            // nativeConvertBonus); a high roll makes the natives burn the attacker's missions across this whole nation.
            if (settlement.HasMission && settlement.MissionOwnerId == attacker.OwnerId && settlement.Size >= 1
                && PlayerById(attacker.OwnerId) is { } captor)
            {
                double roll = random.NextDouble();
                if (roll < NativeConvertProbability(captor))
                {
                    SpawnUnit(Ruleset.Unit(IndianConvertUnitTypeId), attacker.Position, attacker.OwnerId); // a brave converts
                }
                else if (roll >= 1.0 - NativeBurnProbabilityPercent / 100.0)
                {
                    BurnMissionsOf(attacker.OwnerId, nation); // the natives burn the attacker's missions across this nation
                }
            }
            _nativeSettlements.Remove(settlement); // destroyed
            ClaimNativeLand(); // the razed settlement releases its land claim (surviving same-nation settlements keep theirs)

            if (capital)
            {
                // Burning a native capital makes the nation surrender — its surviving settlements drop to peace.
                foreach (NativeSettlement s in _nativeSettlements.Where(s => s.NationTypeId == nation))
                {
                    s.Alarm = NativeSettlement.SurrenderedAlarm;
                }
            }
            else
            {
                // In-settlement defender slaughtered (+500) + the settlement destroyed (+300 MAJOR) + a
                // minor insult (+100) = +900, propagated to the nation's surviving settlements.
                ApplyNativeCombatTension(nation, NativeSettlement.TensionAddSettlementAttacked
                    + NativeSettlement.TensionAddMajor + NativeSettlement.TensionAddMinor);
            }
        }
        else
        {
            // The attacker loses to the garrison: disarm/demote/destroy it via the shared precedence.
            // A repelled assault lowers the nation's alarm (the natives prevailed) — across all its settlements.
            // (The attacker is a land unit here, so the naval damage/sink branch never applies.)
            ResolveLoserOutcome(defender, attacker, great);
            bool attackerSlain = !_units.Any(u => u.Id == attackerId);
            ApplyNativeCombatTension(nation, DefenderCombatTension(attackerWon: false, slaughterTension: 0, attackerSlain));
        }
        return result;
    }

    /// <summary>Whether <paramref name="attacker"/> may assault (to capture) the rival colony on <paramref name="target"/> now.</summary>
    public MoveCheck CheckAttackColony(Unit attacker, Position target)
    {
        if (!attacker.IsOnMap)
        {
            return MoveCheck.No("The unit is at sea or in Europe.");
        }
        if (attacker.IsNative)
        {
            return MoveCheck.No("Native units do not capture colonies."); // braves raid units (1b); colony pillage is a native-AI follow-up
        }
        if (attacker.Type.IsNaval)
        {
            return MoveCheck.No("A ship cannot assault a colony — land a soldier beside it.");
        }
        if (!Map.InBounds(target))
        {
            return MoveCheck.No("Target is off the map.");
        }
        if (!attacker.Position.IsAdjacentTo(target))
        {
            return MoveCheck.No("Attack an adjacent tile.");
        }
        if (attacker.MovementLeft <= 0)
        {
            return MoveCheck.No("No movement left this turn.");
        }
        if (OffenceBase(attacker) <= 0)
        {
            return MoveCheck.No($"A {attacker.Type.ShortName} has no offensive strength — arm it first.");
        }
        if (ColonyAt(target) is not { } colony || colony.OwnerId == attacker.OwnerId)
        {
            return MoveCheck.No("There is no rival colony to assault there.");
        }
        if (_units.Any(u => u.IsOnMap && u.Position == target))
        {
            return MoveCheck.No("Defeat the colony's defenders first."); // a garrison stands on the tile → attack it as a unit
        }
        return MoveCheck.Yes(0);
    }

    /// <summary>
    /// Assaults the rival colony on <paramref name="target"/> to capture it (FreeCol <c>csCaptureColony</c>).
    /// The colony's last-resort defender is an unarmed colonist (the abstracted population); a win hands the
    /// colony — its people, buildings and stores — to the attacker's owner (<see cref="CaptureColony"/>), a loss
    /// disarms/demotes the repelled attacker. Land-only, and only when the colony has no garrison unit on the tile
    /// (a garrison is fought first via the unit-attack path). Assaulting a rival is an act of war (recorded both
    /// ways before ownership flips). Used by both directions: the human (stream 0) and a foreign power capturing
    /// an undefended human colony at war (1c-3f, the power's own stream). The defender gets the colony's
    /// fortification bonus (<see cref="ColonyDefenceBonus"/> — a stockade/fort/fortress makes the colony resist),
    /// and a win sacks the colony's treasury (<see cref="PlunderColony"/>). Revere auto-equip of the last defender
    /// is a later sub-slice.
    /// </summary>
    /// <returns>The graded combat result.</returns>
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckAttackColony"/>.</exception>
    public CombatResult AttackColony(Unit attacker, Position target) => AttackColony(attacker, target, _random);

    /// <summary>The colony-assault resolution drawing from an explicit RNG (the human's stream 0 by default; a foreign power its own; tests inject a fixed RNG).</summary>
    internal CombatResult AttackColony(Unit attacker, Position target, IGameRandom random)
    {
        MoveCheck check = CheckAttackColony(attacker, target);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }

        Colony colony = ColonyAt(target)!;
        int formerOwner = colony.OwnerId;
        // The colony's last-resort defender: an unarmed colonist standing for the abstracted population, shielded
        // by the colony's fortification bonus below (FreeCol also Revere-auto-equips it — deferred).
        var defender = new Unit(0, Ruleset.Unit(StartingUnitTypeId), target) { OwnerId = formerOwner };

        var attackContext = new AttackContext(Movement: MovementPenaltyFor(attacker));
        double attackPower = CombatModel.AttackPower(OffenceBase(attacker), attackContext);
        double defencePower = CombatModel.DefencePower(DefenceBase(defender), new DefenceContext(SettlementDefenceBonus: ColonyDefenceBonus(colony)));
        attacker.MovementLeft = 0; // assaulting ends the attacker's turn (before any promotion swap)

        // Assaulting a rival colony is an act of war, recorded both ways before the colony changes hands.
        SetStance(attacker.OwnerId, formerOwner, Stance.War);
        ChangeTension(attacker.OwnerId, formerOwner, TensionWar);

        CombatResult result = CombatModel.Resolve(CombatModel.WinProbability(attackPower, defencePower), random);
        bool attackerWon = result is CombatResult.GreatWin or CombatResult.Win;
        bool great = result is CombatResult.GreatWin or CombatResult.GreatLoss;

        if (attackerWon)
        {
            ApplyWinnerPromotion(attacker, great, random);
            PlunderColony(colony, attacker.OwnerId, random); // sack the treasury before the colony changes hands
            CaptureColony(colony, attacker.OwnerId);
        }
        else
        {
            ResolveLoserOutcome(defender, attacker, great); // the repelled attacker is disarmed/demoted/destroyed
        }
        return result;
    }

    /// <summary>
    /// Hands <paramref name="colony"/> to <paramref name="newOwnerId"/> (FreeCol <c>csChangeOwner</c>): its people,
    /// buildings and stores transfer intact with the ownership change, then the former owner's <b>ships caught at the
    /// falling colony</b> are resolved (<see cref="ResolveCaughtShips"/>). Plunder gold is handled by the caller
    /// (<see cref="PlunderColony"/>), before the handover, while the treasury is still the former owner's.
    /// </summary>
    private void CaptureColony(Colony colony, int newOwnerId)
    {
        int formerOwnerId = colony.OwnerId;
        colony.OwnerId = newOwnerId;
        ResolveCaughtShips(colony, formerOwnerId);
    }

    /// <summary>
    /// Resolves the former owner's ships caught in port as a colony falls (FreeCol <c>csDamageColonyShips</c> /
    /// <c>csSinkColonyShips</c>): every naval unit of <paramref name="formerOwnerId"/> moored at the colony is
    /// <b>damaged</b> — limping to its repair location (the nearest owned drydock/shipyard colony, else Europe via
    /// <see cref="DamageShip"/>) — or, when it has nowhere to repair, <b>sunk</b> (FreeCol picks sink when
    /// <c>getRepairLocation() == null</c>). Because a naval unit can't occupy the colony's own land tile in our model,
    /// "in port" is a ship on a <b>water tile adjacent to the colony</b> — FreeCol's
    /// <c>colony.getTile().getNavalUnits()</c> adapted to our ships-on-water representation. Run <em>after</em> the
    /// ownership flip, so the just-lost colony is no longer a valid repair berth (a ship cannot repair at the port just
    /// taken from it). Classic <c>captureUnitsUnderRepair</c> is <c>false</c>, so a ship already under repair here is
    /// processed too. Deterministic: ships are taken in id order and the damage/sink path draws no RNG. (Caught
    /// <em>land</em> units — non-combatants sharing the tile — transfer with the colony in FreeCol; we model only ships.)
    /// </summary>
    private void ResolveCaughtShips(Colony colony, int formerOwnerId)
    {
        HashSet<Position> port = colony.Position.Neighbours()
            .Where(n => Map.InBounds(n) && Map.TerrainAt(n).IsWater)
            .ToHashSet();
        List<Unit> caught = _units
            .Where(u => u.OwnerId == formerOwnerId && u.Type.IsNaval && u.IsOnMap && port.Contains(u.Position))
            .OrderBy(u => u.Id)
            .ToList();
        foreach (Unit ship in caught)
        {
            if (RepairBerthFor(ship) is not null || CanRepairAtEurope(ship))
            {
                DamageShip(ship); // limps to its repair location
            }
            else
            {
                SinkShip(ship);   // nowhere to repair → goes down with the colony
            }
        }
    }

    /// <summary>
    /// Sacks a captured colony's treasury (FreeCol <c>csCaptureColony</c>): the former owner loses, and the
    /// captor's owner gains, <see cref="ColonyPlunderAmount"/> gold — computed and drawn (from
    /// <paramref name="random"/>, the captor's stream) <em>before</em> the colony changes hands, while the gold is
    /// still the former owner's. Capped at the victim's purse so it can't go negative. A no-op if either side is
    /// not a real player or the victim has no gold. (Plunder on a native <em>pillage</em> — the smaller
    /// <c>getPlunder/5</c> — stays deferred.)
    /// </summary>
    private void PlunderColony(Colony colony, int captorOwnerId, IGameRandom random)
    {
        if (PlayerById(colony.OwnerId) is not { } victim || PlayerById(captorOwnerId) is not { } captor)
        {
            return;
        }
        int plunder = Math.Min(ColonyPlunderAmount(colony, victim, random), victim.Gold);
        if (plunder <= 0)
        {
            return;
        }
        victim.Gold -= plunder;
        captor.Gold += plunder;
    }

    /// <summary>
    /// The gold a captured colony yields (FreeCol <c>Colony.getPlunderRange</c> → <c>RandomRange.getAmount</c>,
    /// <c>continuous=false</c>): with <c>upper = ownerGold × (colonyPop + 1) / (ownerColoniesPop + 1)</c>, the
    /// payout is <c>rnd[0, upper] + 1</c> (probability 100 — always pays) when <c>upper &gt; 0</c>, else 0; 0 too
    /// when the owner is broke (FreeCol <c>canBePlundered = checkGold(1)</c>). A single-colony owner's <c>upper</c>
    /// equals its whole purse, so its treasury can be emptied; a multi-colony owner loses only that colony's share.
    /// Draws once from <paramref name="random"/> (the captor's stream).
    /// </summary>
    private int ColonyPlunderAmount(Colony colony, Player owner, IGameRandom random)
    {
        if (owner.Gold < 1)
        {
            return 0;
        }
        int totalColoniesPop = ColoniesOf(owner).Sum(c => c.Population);
        int upper = owner.Gold * (colony.Population + 1) / (totalColoniesPop + 1);
        return upper <= 0 ? 0 : random.Next(upper + 1) + 1; // RandomRange(probability 100, min 1, max upper+1, factor 1)
    }

    /// <summary>The most goods a single native pillage carries off from one stack (FreeCol <c>csPillageColony</c>: <c>min(amount/2, 50)</c>).</summary>
    private const int PillageGoodsCap = 50;

    /// <summary>A colony's lootable goods stacks (non-empty and <c>storable</c>), in stable goods-id order — the targets a native pillage can steal from (FreeCol <c>Colony.getLootableGoodsList</c> = the storable stored goods; this excludes hammers/bells/crosses, which accrue in stores but are <c>storable="false"</c>).</summary>
    private IEnumerable<KeyValuePair<string, int>> PillageableGoods(Colony colony) =>
        colony.Stores.Where(kv => kv.Value > 0 && Ruleset.Goods(kv.Key).IsStorable).OrderBy(kv => kv.Key, StringComparer.Ordinal);

    /// <summary>Whether <paramref name="brave"/> may pillage the human colony on <paramref name="target"/> now (FreeCol <c>Colony.canBePillaged</c> + native attacker).</summary>
    public MoveCheck CheckPillageColony(Unit brave, Position target)
    {
        if (!brave.IsOnMap)
        {
            return MoveCheck.No("The unit is at sea or in Europe.");
        }
        if (!brave.IsNative)
        {
            return MoveCheck.No("Only native braves pillage colonies."); // model.ability.pillageUnprotectedColony is the brave's; colonial units capture (AttackColony)
        }
        if (!Map.InBounds(target))
        {
            return MoveCheck.No("Target is off the map.");
        }
        if (!brave.Position.IsAdjacentTo(target))
        {
            return MoveCheck.No("Pillage an adjacent tile.");
        }
        if (brave.MovementLeft <= 0)
        {
            return MoveCheck.No("No movement left this turn.");
        }
        if (OffenceBase(brave) <= 0)
        {
            return MoveCheck.No("The brave has no offensive strength.");
        }
        if (ColonyAt(target) is not { } colony || !IsHumanOwned(colony))
        {
            return MoveCheck.No("There is no human colony to pillage there."); // natives raid the human only (the sole target contract, as NearestHumanUnit)
        }
        if (_units.Any(u => u.IsOnMap && u.Position == target))
        {
            return MoveCheck.No("The colony is defended — its garrison must be beaten first."); // canBePillaged: unprotected only
        }
        if (!PillageableGoods(colony).Any())
        {
            return MoveCheck.No("The colony has nothing worth pillaging."); // canBePillaged: something to take (we model goods loot only)
        }
        return MoveCheck.Yes(0);
    }

    /// <summary>
    /// A native brave pillages the undefended human colony on <paramref name="target"/> (FreeCol
    /// <c>csPillageColony</c> / the <c>PILLAGE_COLONY</c> combat effect — a native win over a colony's unarmed
    /// last-resort defender). The defender is a transient unarmed colonist (defence 1, the abstracted population);
    /// on a brave **win** the brave carries off <c>min(amount/2, 50)</c> of one randomly-chosen lootable goods
    /// stack (the colony keeps its buildings, people and ownership — natives never capture a colony), recording a
    /// <see cref="ColonyRaidNotice"/>; on a **loss** the brave is slain (dispose-on-combat-loss). The whole path
    /// draws from <paramref name="random"/> (the nation's own stream when driven by the AI) — combat band, then
    /// the goods-stack pick — never the human's stream 0 (ADR-009).
    /// </summary>
    /// <remarks>
    /// Faithful subset: the goods loot is destroyed (the brave does not carry it off — no native
    /// goods-hauling/settlement-restock model; FreeCol's brave does <c>attacker.add(goods)</c>). We model the
    /// **goods-stack** and **gold** pillage options (gold = FreeCol <c>max(1, colony.getPlunder/5)</c>, capped at the
    /// owner's purse, via <see cref="ColonyPlunderAmount"/>); FreeCol's other uniform choices — a building to burn or
    /// a ship on the colony tile to sink/damage — are deferred (no building-destruction model). We also pillage on **any**
    /// native win, where FreeCol gates pillage on a non-great win and lets a **great** win kill a colonist or
    /// destroy the colony — so our great win is *gentler* than FreeCol's (a tribe never destroys a colony here);
    /// the colonist-kill/destroy path and the attacker's tension easing are deferred (no population-on-combat
    /// decrement; no nation-level native-tension store).
    /// </remarks>
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckPillageColony"/>.</exception>
    internal void PillageColony(Unit brave, Position target, IGameRandom random)
    {
        MoveCheck check = CheckPillageColony(brave, target);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }

        Colony colony = ColonyAt(target)!;
        // The colony's last-resort defender: an unarmed colonist standing for the abstracted population (defence 1).
        var defender = new Unit(0, Ruleset.Unit(StartingUnitTypeId), target) { OwnerId = colony.OwnerId };

        var attackContext = new AttackContext(Movement: MovementPenaltyFor(brave));
        double attackPower = CombatModel.AttackPower(OffenceBase(brave), attackContext);
        double defencePower = CombatModel.DefencePower(DefenceBase(defender), new DefenceContext(SettlementDefenceBonus: ColonyDefenceBonus(colony)));
        brave.MovementLeft = 0; // raiding ends the brave's turn

        CombatResult result = CombatModel.Resolve(CombatModel.WinProbability(attackPower, defencePower), random);
        if (result is CombatResult.GreatWin or CombatResult.Win)
        {
            // FreeCol's "Pillage choice": one option, uniformly, over {burnable buildings, ships, lootable goods
            // stacks, + gold if the owner can be plundered}. We model the goods stacks + the gold option (buildings
            // and ships are deferred); gold is the LAST option (matching FreeCol's order), added only when the owner
            // has gold to take, so a pick of index < goodsCount is a goods stack (the existing pillage tests' index 0).
            var loot = PillageableGoods(colony).ToList();
            bool canPlunderGold = HumanPlayer.Gold > 0; // the colony is human-owned (CheckPillageColony gated)
            int pick = random.Next(loot.Count + (canPlunderGold ? 1 : 0));
            if (pick < loot.Count)
            {
                (string goodsId, int amount) = loot[pick];
                int take = Math.Min(amount / 2, PillageGoodsCap);
                if (take > 0) // a 1-unit stack yields 0 (amount/2 == 0): the raid won but carried nothing off — no notice
                {
                    colony.AddGoods(goodsId, -take);
                    _colonyRaidNotices.Add(new ColonyRaidNotice(brave.OwnerNationId!, colony.Name, goodsId, take, target));
                }
            }
            else
            {
                // Steal gold: FreeCol max(1, colony.getPlunder/5), capped at the owner's purse (no negative balance).
                // ColonyPlunderAmount draws from the nation's stream (the same `random`) — never the human's stream 0.
                int plunder = Math.Min(Math.Max(1, ColonyPlunderAmount(colony, HumanPlayer, random) / 5), HumanPlayer.Gold);
                HumanPlayer.Gold -= plunder;
                _colonyRaidNotices.Add(new ColonyRaidNotice(brave.OwnerNationId!, colony.Name, null, plunder, target));
            }
        }
        else
        {
            ResolveLoserOutcome(defender, brave, result is CombatResult.GreatLoss); // the brave is dispose-on-combat-loss → slain
        }
    }

    /// <summary>
    /// The gold a sacked settlement yields (FreeCol <c>RandomRange.getAmount</c>, <c>continuous=false</c>):
    /// the range selected by the attacker's <c>plunderNatives</c> status pays, on a <c>Probability%</c>
    /// roll, <c>(rnd[0,max−min] + min) × factor</c>. Draws from <paramref name="random"/> (the probability
    /// roll only when below 100, then the range roll).
    /// </summary>
    private static int ComputePlunder(SettlementType type, bool hasPlunderAbility, IGameRandom random)
    {
        if (type.PlunderRange(hasPlunderAbility) is not { } range)
        {
            return 0;
        }
        if (range.Probability < 100 && (range.Probability <= 0 || random.Next(100) >= range.Probability))
        {
            return 0;
        }
        int roll = random.Next(range.Maximum - range.Minimum + 1);
        return (roll + range.Minimum) * range.Factor;
    }

    /// <summary>
    /// Applies what happens to the losing unit (FreeCol <c>SimpleCombatModel.resolveAttack</c>, land
    /// open-field path), in precedence order: destroyed if doomed-on-loss; else an armed role is disarmed
    /// (the winner may capture the equipment to arm itself) and may then be killed/demoted on losing its
    /// last equipment; else a capturable unit changes side; else a demotable type steps down; else it dies.
    /// </summary>
    private void ResolveLoserOutcome(Unit winner, Unit loser, bool greatLoss)
    {
        // 0. A defeated ship: a naval raider winner first loots what its hold can take (1c-3c), then the ship is
        // damaged and limps to repair (1c-3b) — UNLESS the defeat was decisive (a great loss) or it has nowhere
        // to repair, in which case it sinks (FreeCol resolveAttack: loot, then sink on great/no repair
        // location/beached, otherwise damage). Either way it loses its remaining cargo and everyone aboard.
        if (loser.Type.IsNaval)
        {
            LootShip(winner, loser);
            if (greatLoss || !CanRepairAtEurope(loser))
            {
                SinkShip(loser);
            }
            else
            {
                DamageShip(loser);
            }
            return;
        }

        RoleType loserRole = Ruleset.Role(loser.RoleId);

        // 1. Doomed on any combat loss (braves, scouts) → destroyed.
        if (DisposeOnCombatLoss(loser))
        {
            _units.Remove(loser);
            return;
        }

        // 2. An offensive (armed) role is disarmed.
        if (loserRole.IsOffensive)
        {
            bool killsOnLastLost = loser.Type.DisposeOnAllEquipmentLost && loserRole.Downgrade is null;
            bool demotesOnLastLost = loser.Type.DemoteOnAllEquipmentLost && loserRole.Downgrade is null;
            if (CanCaptureEquipment(winner)
                && Ruleset.CaptureRole(winner.RoleId, loser.RoleId, winner.IsNative) is { } captured)
            {
                ChangeRole(winner, captured.Id, 1); // the winner captures the equipment and arms itself
            }
            ChangeRole(loser, loserRole.Downgrade ?? RoleType.DefaultRoleId, loserRole.Downgrade is null ? 0 : 1);
            if (killsOnLastLost)
            {
                _units.Remove(loser);
            }
            else if (demotesOnLastLost
                && Ruleset.GetUnitChange(UnitChangeTypeIds.Demotion, loser.Type.Id) is { } d)
            {
                UpgradeUnitType(loser, d.To);
            }
            return;
        }

        // 3. A capturable unit changes side (and may downgrade its type on capture). FreeCol also requires
        // !combatIsAmphibious here (a unit beaten by an assault fired straight off a ship is slain, not captured) —
        // omitted because we model no amphibious assault yet (CheckAttack requires the attacker be on the map, not
        // aboard), so this branch is never reached amphibiously. The guard lands with amphibious assault (86d3c9tzv).
        if (loser.Type.CanBeCaptured && CanCaptureUnits(winner))
        {
            CaptureUnit(loser, winner);
            return;
        }

        // 4. A demotable type steps down; otherwise the loser is destroyed.
        if (Ruleset.GetUnitChange(UnitChangeTypeIds.Demotion, loser.Type.Id) is { } demotion)
        {
            UpgradeUnitType(loser, demotion.To);
            return;
        }

        _units.Remove(loser);
    }

    /// <summary>
    /// Sinks a defeated ship: removes the hull <em>and</em> everyone aboard (passengers drown — FreeCol
    /// <c>csSinkShip</c>); its goods cargo vanishes with the object (drowned, not looted — looting is a later
    /// slice). Removing the carried units first also fixes a latent orphan: a bare <c>_units.Remove(ship)</c>
    /// would otherwise strand passengers with a dangling <c>CarrierId</c> and a stale position.
    /// </summary>
    private void SinkShip(Unit ship)
    {
        _units.RemoveAll(u => u.CarrierId == ship.Id); // passengers go down with the ship
        _units.Remove(ship);
    }

    /// <summary>
    /// Damages a defeated ship (FreeCol <c>csDamageShip</c>): it loses its cargo and everyone aboard (just
    /// as if sunk), then limps to its repair location, where it sits under forced repair for
    /// <see cref="RepairTurnsFor"/> turns before returning to service. FreeCol's <c>getRepairLocation</c> is
    /// the nearest owned colony with a drydock/shipyard, falling back to Europe: we now model both — a damaged
    /// ship teleports to a water tile beside that colony (still on the map) if its owner has one (see
    /// <see cref="RepairBerthFor"/>), otherwise it limps off to Europe.
    /// </summary>
    private void DamageShip(Unit ship)
    {
        Position? berth = RepairBerthFor(ship);         // chosen from where it was beaten, before it moves
        _units.RemoveAll(u => u.CarrierId == ship.Id);  // passengers are lost when the ship is crippled
        ship.ClearCargo();                              // and so is the hold's cargo
        if (berth is { } tile)
        {
            ship.Location = UnitLocation.OnMap;          // repairs in its own port (teleports to the colony's dock)
            ship.Position = tile;
        }
        else
        {
            ship.Location = UnitLocation.InEurope;       // no home drydock → limps off the map to Europe
        }
        ship.SailTurnsRemaining = 0;
        ship.RepairTurnsRemaining = RepairTurnsFor(ship.Type);
        ship.MovementLeft = 0;                           // spent — and pinned to 0 while repairing (see EndTurn)
    }

    /// <summary>True when the colony can repair ships — it has a drydock or shipyard (<c>model.ability.repairUnits</c>).</summary>
    private bool ColonyRepairsShips(Colony colony) =>
        colony.Buildings.Any(b => Ruleset.Building(b).RepairsNavalUnits);

    /// <summary>
    /// The water tile a damaged ship limps to for repair: one beside the nearest owned colony with a
    /// drydock/shipyard (Chebyshev distance from where it was beaten, ties broken by colony id). Null when the
    /// owner has no repairing colony — then the ship repairs in Europe (FreeCol <c>Unit.getRepairLocation</c>:
    /// the nearest repairing colony, else Europe). A repairing colony is always coastal (drydock requires
    /// <c>hasPort</c>), so a water neighbour always exists.
    /// </summary>
    private Position? RepairBerthFor(Unit ship)
    {
        if (PlayerById(ship.OwnerId) is not { } owner)
        {
            return null;
        }
        Colony? nearest = ColoniesOf(owner)
            .Where(ColonyRepairsShips)
            .OrderBy(c => Chebyshev(c.Position, ship.Position))
            .ThenBy(c => c.Id)
            .FirstOrDefault();
        if (nearest is null)
        {
            return null;
        }
        foreach (Position n in nearest.Position.Neighbours())
        {
            if (Map.InBounds(n) && Map.TerrainAt(n).IsWater)
            {
                return n;
            }
        }
        return null; // unreachable: a drydock colony is coastal
    }

    /// <summary>Turns a damaged ship of this type spends repairing: <c>MaxHitPoints − 1</c> (it limps in at 1 HP, heals +1/turn), floored at 1. Every classic ship is 6 HP → 5 turns.</summary>
    private static int RepairTurnsFor(UnitType type) => Math.Max(1, type.MaxHitPoints - 1);

    /// <summary>
    /// A naval raider (<c>captureGoods</c> — frigate/privateer/man-o-war) plunders the beaten ship's hold before
    /// it sinks or limps off (FreeCol <c>csLootShip</c>): goods move into the winner's free hold, by stable goods
    /// order, as much as fits; anything that doesn't fit is lost when the loser goes down. A no-op for a winner
    /// without the ability or with no room. Deterministic — no RNG (we auto-take what fits; FreeCol's loot-cargo
    /// chooser dialog is a single-player nicety with no AI equivalent).
    /// </summary>
    private void LootShip(Unit winner, Unit loser)
    {
        // FreeCol gates on winner.isNaval() && canCaptureGoods(); the IsNaval check is redundant today (a naval
        // loser implies a naval winner — cross-domain attack is blocked) but states the invariant at the call site.
        if (!winner.Type.IsNaval || !winner.Type.CaptureGoods)
        {
            return;
        }
        foreach ((string goodsId, int amount) in loser.Cargo.OrderBy(kv => kv.Key).ToList())
        {
            int free = CargoSlotsFree(winner);
            if (free <= 0)
            {
                break;
            }
            // Units of this good that fit: the slack in the winner's existing partial stack of it + brand-new slots.
            int partial = SlotsFor(winner.CargoOf(goodsId)) * CargoSlotSize - winner.CargoOf(goodsId);
            int take = Math.Min(amount, partial + (free * CargoSlotSize));
            if (take <= 0)
            {
                continue;
            }
            winner.AddCargo(goodsId, take);
            loser.AddCargo(goodsId, -take);
        }
    }

    /// <summary>
    /// Whether a defeated ship has somewhere to repair (FreeCol <c>getRepairLocation != null</c>). We model
    /// Europe only, which every colonial power has, so this is true for any ship; a hypothetical native-owned
    /// ship (none exist in the classic ruleset) has no Europe and would sink instead.
    /// </summary>
    private bool CanRepairAtEurope(Unit ship) => PlayerById(ship.OwnerId) is { PlayerType: PlayerType.Colonial };

    // ===== Colony fort/fortress bombardment of adjacent enemy ships (86d3c9tkk) ==============================
    // At the start of each colonial player's turn, every colony with a fort/fortress (the bombardShips ability) and
    // artillery on its tile fires on adjacent enemy/pirate ships at sea — one-sided, no counterattack (FreeCol
    // ServerPlayer.csBombardEnemyShips). Reuses the naval damage/sink path; draws from the owner's own RNG stream.

    /// <summary>FreeCol <c>SimpleCombatModel.MAXIMUM_BOMBARD_POWER</c>: the on-tile artillery offence is capped here.</summary>
    private const int MaxBombardPower = 48;

    /// <summary>
    /// Each of <paramref name="player"/>'s fort/fortress colonies with artillery on its tile bombards every adjacent
    /// enemy-or-pirate ship at sea (FreeCol <c>csBombardEnemyShips</c>, run at turn start). Bombard power is the summed
    /// offence of the on-tile artillery, capped at <see cref="MaxBombardPower"/>; the ship is damaged or sunk with no
    /// return fire. Deterministic on the owner's stream (the human's 0, an AI power's own); a landlocked colony has no
    /// adjacent water (so no targets), and a colony with no artillery has 0 power (so it is skipped).
    /// </summary>
    private void BombardEnemyShips(Player player) => BombardEnemyShips(player, RandomFor(player));

    /// <summary>Colony bombardment drawing from an explicit RNG (tests inject a fixed RNG; the turn path uses the owner's stream).</summary>
    internal void BombardEnemyShips(Player player, IGameRandom random)
    {
        foreach (Colony colony in ColoniesOf(player).OrderBy(c => c.Id).ToList())
        {
            if (!colony.Buildings.Any(b => Ruleset.Building(b).BombardsShips))
            {
                continue; // no fort/fortress → no bombardment
            }
            int power = Math.Min(MaxBombardPower, (int)_units
                .Where(u => u.IsOnMap && u.Position == colony.Position && IsOwnedBy(u, player) && u.Type.Bombard)
                .Sum(OffenceBase));
            if (power <= 0)
            {
                continue; // a fort with no artillery on its tile cannot bombard
            }
            // Targets in a stable order (neighbour-tile order, then unit id): collected first, since a hit removes the ship.
            var targets = colony.Position.Neighbours()
                .Where(n => Map.InBounds(n) && Map.TerrainAt(n).IsWater)
                .SelectMany(n => _units.Where(u => u.IsOnMap && u.Position == n && IsBombardTarget(player, u)))
                .OrderBy(u => u.Id)
                .ToList();
            foreach (Unit ship in targets)
            {
                BombardShip(ship, power, random);
            }
        }
    }

    /// <summary>Whether <paramref name="ship"/> is a valid bombard target for <paramref name="colonyOwner"/>: an enemy naval unit — at war, or a pirate (privateer) regardless of stance — not the owner's own.</summary>
    private bool IsBombardTarget(Player colonyOwner, Unit ship) =>
        ship.Type.IsNaval
        && !IsOwnedBy(ship, colonyOwner)
        && (ship.Type.Piracy || StanceBetween(colonyOwner.PlayerId, ship.OwnerId) == Stance.War);

    /// <summary>
    /// Resolves one bombard against <paramref name="ship"/> (one-sided — no counterattack): <paramref name="power"/>
    /// vs the ship's defence (with its cargo penalty). A win damages the ship (it limps to repair); a great win — or a
    /// ship with nowhere to repair — sinks it (FreeCol <c>csBombard</c> → damage/sink, no loot). A miss leaves it unharmed.
    /// </summary>
    private void BombardShip(Unit ship, int power, IGameRandom random)
    {
        double defence = CombatModel.DefencePower(DefenceBase(ship), new DefenceContext(GoodsCarried: GoodsSlotsUsed(ship)));
        CombatResult result = CombatModel.ResolveNaval(CombatModel.WinProbability(power, defence), random);
        if (result is CombatResult.GreatWin or CombatResult.Win)
        {
            if (result is CombatResult.GreatWin || !CanRepairAtEurope(ship))
            {
                SinkShip(ship);
            }
            else
            {
                DamageShip(ship);
            }
        }
    }

    /// <summary>Captures a defeated unit: it changes to the winner's side (with the capture type-change, if any) and is disarmed.</summary>
    private void CaptureUnit(Unit loser, Unit winner)
    {
        Unit captive = Ruleset.GetUnitChange(UnitChangeTypeIds.Capture, loser.Type.Id) is { } change
            ? UpgradeUnitType(loser, change.To) // e.g. veteran soldier → free colonist on capture
            : loser;
        captive.OwnerNationId = winner.OwnerNationId;
        // For a native winner OwnerNationId is the authoritative owner, so the captive's OwnerId is unused (0);
        // never copy the brave's OwnerId (which is 0 == the human's id) or capture would hand the unit back to
        // the human. Braves cannot capture units in the classic ruleset today, so this is defensive (dormant).
        captive.OwnerId = winner.IsNative ? 0 : winner.OwnerId;
        ChangeRole(captive, RoleType.DefaultRoleId, 0);
    }

    /// <summary>
    /// Promotes the winner if its type can advance and either it has automatic promotion (George
    /// Washington) or this was a decisive (great) win and the promotion roll succeeds (FreeCol
    /// <c>SimpleCombatModel</c>; the promotion draw is a second main-RNG draw, taken only when needed).
    /// </summary>
    private void ApplyWinnerPromotion(Unit winner, bool great, IGameRandom random)
    {
        if (!_units.Contains(winner)
            || Ruleset.GetUnitChange(UnitChangeTypeIds.Promotion, winner.Type.Id) is not { } change)
        {
            return;
        }
        bool automatic = AbilityForUnit(winner, AutomaticPromotionAbility);
        if (automatic || (great && 100 * random.NextDouble() <= change.Probability))
        {
            UpgradeUnitType(winner, change.To);
        }
    }

    /// <summary>True when defeating <paramref name="unit"/> destroys it outright (type or role <c>disposeOnCombatLoss</c>).</summary>
    private bool DisposeOnCombatLoss(Unit unit) =>
        unit.Type.DisposeOnCombatLoss || Ruleset.Role(unit.RoleId).DisposeOnCombatLoss;

    /// <summary>True when <paramref name="unit"/> can capture a defeated enemy's role equipment (type or role).</summary>
    private bool CanCaptureEquipment(Unit unit) =>
        unit.Type.CaptureEquipment
        || Ruleset.Role(unit.RoleId).GrantedAbilities.GetValueOrDefault(CaptureEquipmentAbility);

    /// <summary>True when <paramref name="unit"/> can capture a defeated enemy unit (type or role).</summary>
    private bool CanCaptureUnits(Unit unit) =>
        unit.Type.CaptureUnits
        || Ruleset.Role(unit.RoleId).GrantedAbilities.GetValueOrDefault(CaptureUnitsAbility);

    /// <summary>
    /// Replaces a unit with one of a new type, keeping its id, position, location, carrier,
    /// owner, role and cargo (units are immutable in their <see cref="Unit.Type"/>, so an upgrade is a swap).
    /// </summary>
    private Unit UpgradeUnitType(Unit unit, string newTypeId)
    {
        var upgraded = new Unit(unit.Id, Ruleset.Unit(newTypeId), unit.Position)
        {
            Location = unit.Location,
            SailTurnsRemaining = unit.SailTurnsRemaining,
            CarrierId = unit.CarrierId,
            MovementLeft = unit.MovementLeft,
            OwnerNationId = unit.OwnerNationId, // a promotion/demotion/capture keeps the side and role
            OwnerId = unit.OwnerId,
            RoleId = unit.RoleId,
            RoleCount = unit.RoleCount,
        };
        foreach ((string goodsId, int amount) in unit.Cargo)
        {
            upgraded.AddCargo(goodsId, amount);
        }
        upgraded.SetTreasureAmount(unit.TreasureAmount); // carry any treasure across the swap (defensive; treasure trains have no type change today)
        int index = _units.IndexOf(unit);
        if (index >= 0)
        {
            _units[index] = upgraded;
        }
        else
        {
            _units.Add(upgraded);
        }
        return upgraded;
    }

    /// <summary>Tiles the human player has ever seen (permanent fog of war — stays on the map once revealed).</summary>
    public IReadOnlySet<Position> Explored => _human.Explored;

    /// <summary>Whether a tile has ever been revealed to the human player.</summary>
    public bool IsExplored(Position p) => _human.Explored.Contains(p);

    /// <summary>How far a colony sees (FreeCol settlements carry a line of sight): its 3×3 surroundings.</summary>
    public const int ColonySightRadius = 1;

    /// <summary>
    /// Tiles the player can see <em>right now</em> — within the line of sight of an
    /// on-map unit or a colony. Always a subset of <see cref="Explored"/>; recomputed
    /// from current positions (not stored, never stale). Explored-but-not-visible tiles
    /// are "remembered" (drawn dimmed); foreign units there are hidden.
    /// </summary>
    public IReadOnlySet<Position> CurrentlyVisible
    {
        get
        {
            var visible = new HashSet<Position>();
            foreach (Unit unit in _units)
            {
                if (unit.IsOnMap && IsHumanOwned(unit)) // only the human's own units lift the human's fog
                {
                    visible.UnionWith(TilesInRange(unit.Position, LineOfSightOf(unit)));
                }
            }
            foreach (Colony colony in _colonies)
            {
                if (IsHumanOwned(colony))
                {
                    visible.UnionWith(TilesInRange(colony.Position, ColonySightRadius));
                }
            }
            return visible;
        }
    }

    /// <summary>Whether a tile is currently in sight (not merely explored).</summary>
    public bool IsVisible(Position p) =>
        _units.Any(u => u.IsOnMap && IsHumanOwned(u) && InSight(u.Position, p, LineOfSightOf(u)))
        || _colonies.Any(c => IsHumanOwned(c) && InSight(c.Position, p, ColonySightRadius));

    private static bool InSight(Position centre, Position p, int radius) =>
        Math.Abs(centre.X - p.X) <= radius && Math.Abs(centre.Y - p.Y) <= radius;

    /// <summary>
    /// Starts a new game: generates a <paramref name="mapWidth"/>×<paramref name="mapHeight"/> map (with
    /// <paramref name="landMassFraction"/> of it grown into land) from the seed and places one starting colonist on
    /// the first settleable land tile, revealing its surroundings. The map-shape parameters default to the shipped
    /// world (36×24, 45% land), so omitting them yields the historical default game; the new-game options forward
    /// the player's chosen world size / land mass here (<see cref="World.WorldSizeOptions"/>).
    /// </summary>
    /// <param name="ruleset">The rule data to play by (its <see cref="Ruleset.Difficulty"/> supplies the balance numbers).</param>
    /// <param name="seed">The world seed; the game is fully determined by it (ADR-009).</param>
    /// <param name="mapWidth">Map width in tiles (default 36 — the shipped world).</param>
    /// <param name="mapHeight">Map height in tiles (default 24 — the shipped world).</param>
    /// <param name="startingGold">The human's starting treasury.</param>
    /// <param name="startingTax">The human's starting sales tax.</param>
    /// <param name="landMassFraction">The fraction of the map grown into land (default the shipped 45%).</param>
    /// <param name="difficultyLevelId">
    /// The difficulty level to record in the save (default <c>model.difficulty.medium</c> → byte-identical default).
    /// The balance numbers themselves come from <paramref name="ruleset"/> (which must have been loaded with the same
    /// level); this only tags the game so a reload re-loads the matching balance (86d3c9y08).
    /// </param>
    /// <param name="mapSource">
    /// Which map to play on (default <see cref="MapSource.Random"/> → a procedurally generated New World, the historical
    /// default game). <see cref="MapSource.America"/> loads FreeCol's fixed 40×180 America terrain instead; on a fixed
    /// map the <paramref name="mapWidth"/>/<paramref name="mapHeight"/>/<paramref name="landMassFraction"/> shape
    /// parameters are ignored (the loaded grid sets the dimensions) and rivers/resources/regions are laid on top.
    /// </param>
    /// <param name="humanNationId">
    /// The European nation the human plays (e.g. <c>model.nation.dutch</c>; FreeCol's New-Game nation pick — see
    /// [players]). Seeds the human <see cref="Player.NationId"/>, so the human gets that nation's <b>advantage</b> (the
    /// same nation-type modifiers/abilities the foreign powers already fold — Dutch trade, French native-alarm, Spanish
    /// offence-vs-natives, English immigration) and that nation's <b>colony-name</b> list. It is also excluded from the
    /// foreign-power roster so the human's nation is never duplicated by a rival. <b>Default null = the classic
    /// nation-less human</b>, so an unpicked new game is byte-identical to before (ADR-009). An unknown/non-selectable id
    /// is treated as null (no advantage).
    /// </param>
    public static Game New(
        Ruleset ruleset, ulong seed, int mapWidth = 36, int mapHeight = 24,
        int startingGold = 0, int startingTax = 0,
        double landMassFraction = MapGenerator.DefaultLandMassFraction,
        string difficultyLevelId = DifficultyLevels.DefaultId,
        MapSource mapSource = MapSource.Random,
        string? humanNationId = null)
    {
        // A picked nation must be a real, selectable, non-REF European power; anything else (null, an unknown id, a
        // native/REF id) falls back to the nation-less classic human — so the default new game stays byte-identical.
        string? humanNation = humanNationId is { } nid
            && ruleset.EuropeanNations.Any(n => n.Id == nid && n.Selectable && !n.IsRef)
            ? humanNationId
            : null;
        var random = new Pcg32Random(seed);

        // The map: either FreeCol's fixed America terrain (decorated with our rivers/resources/regions) or, by
        // default, a procedurally grown New World. A fixed map sets its own dimensions, so the world-size args apply
        // only to the random path. Both draw from the same stream-0 RNG, so the default (Random) game is unchanged.
        GameMap? fixedTerrain = FixedMap.TryLoad(mapSource, ruleset);
        GameMap map = fixedTerrain is null
            ? MapGenerator.Generate(ruleset, mapWidth, mapHeight, random, landMassFraction)
            : MapGenerator.DecorateFixedMap(fixedTerrain, ruleset, random);

        // The single human player (stream 0; foreign powers and natives become players in FP-3). Its nation is the
        // validated pick (null for the classic nation-less default), which seeds the human's national advantage +
        // colony names through the existing nation-id-driven seams (NationTypeModifiers / ColonyNamesFor).
        var human = new Player(playerId: 0, nationId: humanNation, isHuman: true, PlayerType.Colonial, new Market(ruleset))
        {
            Gold = startingGold,
            TaxRate = startingTax,
        };
        var game = new Game(ruleset, map, random, turn: 1, human) { DifficultyLevelId = difficultyLevelId };

        // Give every placed finite (min/max-ranged) bonus resource a rolled starting quantity (FreeCol
        // `new Resource(game, tile, type)` ⇒ RandomRange(min, max)). Rolled on the dedicated, reserved
        // ResourceQuantity stream (102), seeded off the world seed — never the human's economy stream 0 — so
        // wiring this in does NOT shift any stream-0 draw and the human's future random sequence is byte-stable
        // (ADR-009). It does add a ResourceQuantities token to the default save (the classic map places finite
        // minerals/ore/silver deposits), so the L4 map golden and soak baseline are regenerated this wave.
        game.RollResourceQuantities(seed);

        // Start on settleable land, preferring temperate latitudes (nearest the equator row) over a polar landfall,
        // and — so the human can land its starting caravel beside its colonists (FreeCol's coastal arrival) — a
        // COASTAL tile: settleable land with both a water neighbour (a berth for the ship) and a land neighbour (room
        // to expand, not a 1-tile islet). Falls back to any non-islet settleable tile, then any settleable tile.
        bool Settleable(Position p)
        {
            TerrainType t = map.TerrainAt(p);
            return !t.IsWater && t.CanSettle;
        }
        bool HasWaterNeighbour(Position p) => p.Neighbours().Any(n => map.InBounds(n) && map.TerrainAt(n).IsWater);
        bool HasLandNeighbour(Position p) => p.Neighbours().Any(n => map.InBounds(n) && !map.TerrainAt(n).IsWater);
        int equator = map.Height / 2; // map.Height == mapHeight for Random; a fixed map sets its own height
        var settleable = map.AllPositions().Where(Settleable).OrderBy(p => Math.Abs(p.Y - equator)).ToList();
        Position start =
            settleable.Where(p => HasWaterNeighbour(p) && HasLandNeighbour(p)).Cast<Position?>().FirstOrDefault()
            ?? settleable.Where(HasLandNeighbour).Cast<Position?>().FirstOrDefault()
            ?? settleable.First();
        game.SpawnHumanStartingUnits(ruleset, start);

        // The REF's entry tile: the nearest water tile to the human's start (FreeCol picks a non-land tile within
        // distance 10 of the start and stores it as the REF player's entry tile). Chosen deterministically — no RNG
        // draw, so the human's stream 0 stays byte-stable; on independence the King's fleet arrives here. Persisted.
        game.SetRefEntryTile(game.NearestWaterTile(start));

        // Native settlements, on their own RNG stream so placement does not shift the
        // economy/father/immigration draws. They keep clear of the player's landing.
        var nativeRandom = new Pcg32Random(seed, NativeStreamId);
        var excluded = new HashSet<Position>(start.Neighbours().Append(start));
        foreach (NativeSettlement settlement in
                 NativeSettlementGenerator.Place(ruleset, map, nativeRandom, excluded))
        {
            game._nativeSettlements.Add(settlement);
            game._nextSettlementId = Math.Max(game._nextSettlementId, settlement.Id + 1);
        }

        game.ClaimNativeLand(); // each native settlement claims the land in its radius (FreeCol Tile.owner)

        // Garrison each settlement with native braves on adjacent open land (unarmed defenders the
        // player can attack in the open field; the settlement tile itself is assaulted in slice 5c).
        // Placement is deterministic and consumes no RNG draw, so neither the economy stream (0) nor
        // the native placement stream (1) is shifted.
        if (ruleset.UnitTypes.Any(u => u.Id == BraveUnitTypeId))
        {
            UnitType braveType = ruleset.Unit(BraveUnitTypeId);
            foreach (NativeSettlement settlement in game._nativeSettlements)
            {
                for (int i = 0; i < BravesPerSettlement; i++)
                {
                    Position? spot = game.FreeAdjacentLand(settlement.Position);
                    if (spot is { } p)
                    {
                        game.SpawnUnit(braveType, p, settlement.NationTypeId);
                    }
                }
            }
        }

        game.SpawnRivalsAndNatives(ruleset, start, humanNation); // foreign powers (landed) + native nations as players (FP-3b/FP-4); the human's own nation is excluded from the rival roster

        // Each non-human player draws from its own independent PCG stream (ADR-009); created here from the
        // same seed so the human's stream 0 is untouched (foreign units already placed, drawing nothing). A
        // foreign colonial power also gets its own Europe recruit dock (FP-5), drawn from its own stream.
        foreach (Player ai in game._players.Where(p => !p.IsHuman))
        {
            ai.Rng = new Pcg32Random(seed, ai.RngStreamId);
            if (ai.PlayerType == PlayerType.Colonial)
            {
                game.InitRecruitDock(ai);
            }
        }

        // Lost City Rumours, on their own reserved RNG stream (gen-time only, never resumed — like map gen and
        // native placement, so the human's stream 0 stays byte-identical). Kept clear of every settlement, unit,
        // and the player's 3×3 start area (FreeCol removes rumours around a starting colony). The reward is rolled
        // only when a unit explores one (a later slice) — placement just marks the tiles.
        var lcrRandom = new Pcg32Random(seed, LcrStreamId);
        var lcrExcluded = new HashSet<Position>(start.Neighbours().Append(start));
        lcrExcluded.UnionWith(game._nativeSettlements.Select(s => s.Position));
        lcrExcluded.UnionWith(game._units.Where(u => u.IsOnMap).Select(u => u.Position));
        foreach (Position p in LostCityRumourGenerator.Place(map, lcrExcluded, lcrRandom))
        {
            map.AddRumour(p);
        }

        game.GenerateOffers(human); // Congress choices available from the first turn
        game.InitRecruitDock(human); // three recruits waiting on the Europe dock from turn 1

        return game;
    }

    /// <summary>
    /// Assigns each placed bonus resource a starting quantity (FreeCol <c>new Resource(game, tile, type)</c> ⇒
    /// <c>RandomRange(minValue, maxValue)</c>): a finite (min/max-ranged) resource gets a rolled amount; a limitless
    /// one (no range — most classic resources) gets none. Rolled on the dedicated <see cref="ResourceQuantityStreamId"/>
    /// stream (seeded off the game seed) so the human's economy stream 0 is never shifted (ADR-009). Iterates resources
    /// in row-major order for a stable draw sequence.
    /// <para>Called from <see cref="New"/> after map generation (86d3c9wbp), so every placed finite deposit carries a
    /// rolled quantity from turn 1. The classic default 36×24 map places finite minerals/ore/silver deposits, so a
    /// default save now writes a <c>ResourceQuantities</c> token — byte-stability with the pre-roll save is intentionally
    /// broken (the L4 map golden and soak baseline were regenerated this wave). Because it draws only on the reserved
    /// stream 102, it shifts no stream-0 draw, so the human's economy/turn sequence is unchanged. We persist the rolled
    /// amount now; the deposit-depletes-when-worked rule is a later slice.</para>
    /// </summary>
    internal void RollResourceQuantities(ulong seed)
    {
        var rng = new Pcg32Random(seed, ResourceQuantityStreamId);
        foreach (Position p in Map.Resources.Keys
                     .OrderBy(p => p.Y * Map.Width + p.X)
                     .ToList())
        {
            ResourceType type = Ruleset.Resource(Map.Resources[p]);
            if (type.HasQuantityRange)
            {
                Map.SetResourceQuantity(p, type.RollQuantity(rng));
            }
        }
    }

    /// <summary>The number of foreign colonial powers spawned alongside the human (the classic four minus the human's slot).</summary>
    private const int ForeignPowerCount = 3;

    /// <summary>How far (Chebyshev) a foreign power lands from the human's start, so rivals stay outside the human's view.</summary>
    private const int ForeignLandingMinDistance = 6;

    /// <summary>Colonies a foreign power's AI founds before its remaining colonists explore instead (FP-4 minimal AI).
    /// Lifted from 1 to 3 once the colony economy (`86d3c9vmr`: food-first tile plan + building-worker fill) was proven
    /// to keep multiple AI colonies fed and solvent under the soak (no starvation / negative treasury at 25 seeds ×
    /// 200 turns). Bounded in practice by each power's handful of founder colonists. <c>internal</c> so AI tests can
    /// drive a power to its cap without hard-coding the number.</summary>
    internal const int MaxAiColonies = 3;

    /// <summary>
    /// Minimum spacing (Chebyshev) between two foreign powers' landing anchors, so the rivals spread along the coast
    /// instead of clustering at the single farthest corner (FreeCol <c>EuropeanStartingPositionsGenerator</c>'s
    /// <c>MINIMUM_DISTANCE_BETWEEN_PLAYERS = 10</c>, scaled to our smaller default 36×24 world). Relaxed automatically
    /// on a crowded/small map: a power that can't honour it lands at the best tile it can (placement never fails).
    /// </summary>
    private const int MinDistanceBetweenPowers = 8;

    private static int Chebyshev(Position a, Position b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    /// <summary>
    /// Registers the native nations and the foreign colonial powers as players (ADR-019). Each distinct
    /// native nation present becomes a <see cref="PlayerType.Native"/> player (its units/settlements
    /// reference it by nation id; its braves act via <see cref="RunNativeTurn"/> from slice 1b); the foreign powers are the first
    /// <see cref="ForeignPowerCount"/> classic playable European nations, <b>landed on the map</b> far from
    /// the human (FP-4) with their starting units, <b>spread along the coast</b> at least
    /// <see cref="MinDistanceBetweenPowers"/> apart (faithful to FreeCol's per-player spacing). The human's own
    /// <paramref name="humanNationId"/> (the New-Game nation pick, null for the classic default) is excluded from the
    /// foreign roster so the human's nation is never duplicated by a rival (FreeCol removes the human's nation from the
    /// AI pool). Placement draws no RNG (the human's stream 0 stays byte-stable); player ids are allocated densely in a
    /// stable order (human 0, then natives, then powers).
    /// </summary>
    private void SpawnRivalsAndNatives(Ruleset ruleset, Position humanStart, string? humanNationId = null)
    {
        foreach (string nationType in _nativeSettlements.Select(s => s.NationTypeId).Distinct().OrderBy(n => n))
        {
            _players.Add(new Player(_players.Count, nationType, isHuman: false, PlayerType.Native, new Market(ruleset)));
        }

        var taken = new HashSet<Position>(); // tiles claimed by foreign landings (keeps powers off each other's units)
        var anchors = new List<Position>();  // each placed power's landing anchor (keeps powers spread along the coast)
        foreach (EuropeanNation nation in ruleset.EuropeanNations
                     .Where(n => n.Selectable && !n.IsRef && n.Id != humanNationId).Take(ForeignPowerCount))
        {
            var power = new Player(_players.Count, nation.Id, isHuman: false, PlayerType.Colonial, new Market(ruleset));
            _players.Add(power);
            if (LandForeignPower(ruleset, power, nation, humanStart, taken, anchors) is { } anchor)
            {
                anchors.Add(anchor);
            }
        }
    }

    /// <summary>
    /// FreeCol's classic European starting roster (<c>model.nationType.default</c> <c>RegularStartingUnits</c>): a
    /// <b>pioneer</b> (free colonist + tools) and a <b>soldier</b> (free colonist + muskets), and a <b>caravel</b>.
    /// The veteran soldier is the <c>expert-starting-units</c> variant (excluded from the regular roster), so the
    /// regular start uses a free-colonist soldier — the iconic, neutral start, matching what a default-nation rival gets.
    /// </summary>
    private static readonly (string TypeId, string? RoleId)[] HumanStartingRoster =
    [
        ("model.unit.freeColonist", "model.role.pioneer"),
        ("model.unit.freeColonist", "model.role.soldier"),
        ("model.unit.caravel", null),
    ];

    /// <summary>
    /// Places the human's starting units around <paramref name="start"/> (FreeCol's classic landed roster — see
    /// <see cref="HumanStartingRoster"/>): the land units (pioneer, soldier) on <paramref name="start"/> and its free
    /// land neighbours, the caravel on a free adjacent water tile. Deterministic — draws no RNG, so the human's
    /// stream 0 stays byte-stable (ADR-009); each unit lifts the human's fog (<see cref="RevealForOwner"/>). A unit
    /// type a ruleset variant omits, or a ship with no adjacent water (a landlocked start), is simply skipped.
    /// </summary>
    private void SpawnHumanStartingUnits(Ruleset ruleset, Position start)
    {
        var taken = new HashSet<Position>();
        bool Free(Position p, bool water) =>
            Map.InBounds(p) && Map.TerrainAt(p).IsWater == water
            && !taken.Contains(p) && !_units.Any(u => u.IsOnMap && u.Position == p);
        Position? Place(bool water) =>
            Free(start, water) ? start
            : start.Neighbours().Where(n => Free(n, water)).Cast<Position?>().FirstOrDefault();

        foreach ((string typeId, string? roleId) in HumanStartingRoster)
        {
            if (!ruleset.UnitTypes.Any(u => u.Id == typeId))
            {
                continue; // a variant may omit a starting unit type
            }
            UnitType type = ruleset.Unit(typeId);
            if (Place(type.IsNaval) is not { } pos)
            {
                continue; // no room (e.g. a landlocked start has no water for the caravel) → skip it
            }
            string role = roleId ?? RoleType.DefaultRoleId;
            var unit = new Unit(_nextUnitId++, type, pos)
            {
                OwnerId = 0,
                RoleId = role,
                RoleCount = role == RoleType.DefaultRoleId ? 0 : 1,
            };
            _units.Add(unit);
            taken.Add(pos);
            RevealForOwner(unit); // the human's own units lift its fog around the landing
        }
    }

    /// <summary>
    /// Lands a foreign power on the map far from the human (FP-4): its colonists on settleable land and its
    /// ship on adjacent water, around a deterministic coastal anchor — the farthest free coastal tile from the
    /// human that is also at least <see cref="MinDistanceBetweenPowers"/> from every already-placed power, so the
    /// rivals spread along the coast rather than clustering at one corner (FreeCol
    /// <c>EuropeanStartingPositionsGenerator</c>). On a crowded/small map where no tile honours the spacing the
    /// constraint is relaxed to the best available tile, then to docking in Europe — placement never fails. Reveals
    /// the power's own fog. Deterministic — draws no RNG.
    /// </summary>
    /// <returns>The chosen landing anchor (so the caller keeps the next power away from it), or null if the power had to dock in Europe.</returns>
    private Position? LandForeignPower(
        Ruleset ruleset, Player power, EuropeanNation nation, Position humanStart,
        HashSet<Position> taken, IReadOnlyList<Position> placedAnchors)
    {
        bool FreeLand(Position p) => Map.InBounds(p) && Map.TerrainAt(p).CanSettle && !Map.TerrainAt(p).IsWater
            && ColonyAt(p) is null && NativeSettlementAt(p) is null
            && !_units.Any(u => u.IsOnMap && u.Position == p) && !taken.Contains(p);
        bool FreeWater(Position p) => Map.InBounds(p) && Map.TerrainAt(p).IsWater
            && !_units.Any(u => u.IsOnMap && u.Position == p) && !taken.Contains(p);
        Position? FirstFree(Position anchor, Func<Position, bool> free) =>
            free(anchor) ? anchor : anchor.Neighbours().Where(free).Cast<Position?>().FirstOrDefault();

        // Candidate coastal land anchors far from the human, ordered farthest-first (stable tie-break). FreeCol spreads
        // powers by a minimum distance between their starts; we honour it when a candidate satisfies the spacing for
        // every already-placed power, and relax to the best available tile when the map is too crowded to.
        var candidates = Map.AllPositions()
            .Where(p => FreeLand(p) && Chebyshev(p, humanStart) >= ForeignLandingMinDistance && p.Neighbours().Any(FreeWater))
            .OrderByDescending(p => Chebyshev(p, humanStart)).ThenBy(p => p.Y).ThenBy(p => p.X)
            .ToList();
        Position? anchor =
            candidates.Cast<Position?>().FirstOrDefault(
                p => placedAnchors.All(a => Chebyshev(p!.Value, a) >= MinDistanceBetweenPowers))
            ?? candidates.Cast<Position?>().FirstOrDefault(); // relax the spacing on a crowded map

        foreach (EuropeanStartingUnit start in nation.NationType.RegularStartingUnits)
        {
            if (!ruleset.UnitTypes.Any(u => u.Id == start.UnitTypeId))
            {
                continue; // a variant may omit a starting unit type
            }
            UnitType type = ruleset.Unit(start.UnitTypeId);
            string roleId = start.RoleId ?? RoleType.DefaultRoleId;
            Position? spot = anchor is { } a ? FirstFree(a, type.IsNaval ? FreeWater : FreeLand) : null;
            var unit = new Unit(_nextUnitId++, type, spot ?? new Position(0, 0))
            {
                Location = spot is null ? UnitLocation.InEurope : UnitLocation.OnMap,
                OwnerId = power.PlayerId,
                RoleId = roleId,
                RoleCount = roleId == RoleType.DefaultRoleId ? 0 : 1,
            };
            _units.Add(unit);
            if (spot is { } s)
            {
                taken.Add(s);
                Reveal(power, unit); // the power lifts its own fog around its landing
            }
        }
        // Report the anchor only if at least one unit actually landed there (otherwise the power docked in Europe).
        return anchor is { } a2 && _units.Any(u => u.OwnerId == power.PlayerId && u.IsOnMap) ? a2 : null;
    }

    /// <summary>
    /// Restores a game from saved state (see <see cref="Persistence.SaveGame"/>). The per-player state
    /// arrives as one <see cref="RestoredPlayer"/> per player (a single human today); the world — units,
    /// colonies, native settlements — stays as flat global lists referenced by owner id.
    /// </summary>
    internal static Game Restore(
        Ruleset ruleset, GameMap map, RandomState randomState, int turn,
        IReadOnlyList<RestoredPlayer> players,
        IEnumerable<(int id, UnitType type, Position position, int movementLeft,
            UnitLocation location, int sailTurns, IReadOnlyDictionary<string, int>? cargo,
            int? carrierId, string? ownerNationId, string? roleId, int roleCount, int ownerId,
            int repairTurns, UnitOrders orders, int treasureAmount, Position? destination,
            int? tradeRouteId, int tradeRouteStopIndex,
            string? workImprovementId, int workTurnsLeft)> units,
        IEnumerable<Colony>? colonies = null,
        IEnumerable<NativeSettlement>? nativeSettlements = null,
        AutoExportMode autoExportMode = AutoExportMode.PerGood,
        string? difficultyLevelId = null)
    {
        Player human = BuildPlayer(ruleset, players.Single(p => p.IsHuman), randomState);
        var game = new Game(ruleset, map, Pcg32Random.FromState(randomState), turn, human)
        {
            AutoExportMode = autoExportMode,
            // The save's level tag; pre-v46 saves (null) keep the ruleset's loaded level (the load path re-loads the
            // ruleset under the persisted level, so this stays consistent).
            DifficultyLevelId = difficultyLevelId ?? ruleset.DifficultyLevelId,
        };
        foreach (RestoredPlayer rp in players.Where(p => !p.IsHuman))
        {
            game._players.Add(BuildPlayer(ruleset, rp, randomState));
        }

        // Top up each colonial player's dock to a full set: a no-op when the save held all slots (so the RNG
        // sequence is preserved); draws a fresh dock for an older save that had none (a pre-v12 human, or a
        // pre-FP-5 foreign power). A foreign power draws from its own restored stream, never the human's stream 0.
        game.InitRecruitDock(human);
        foreach (Player power in game._players.Where(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial))
        {
            game.InitRecruitDock(power);
        }

        foreach ((int id, UnitType type, Position position, int movementLeft,
                  UnitLocation location, int sailTurns, IReadOnlyDictionary<string, int>? cargo,
                  int? carrierId, string? ownerNationId, string? roleId, int roleCount, int ownerId,
                  int repairTurns, UnitOrders orders, int treasureAmount, Position? destination,
                  int? tradeRouteId, int tradeRouteStopIndex,
                  string? workImprovementId, int workTurnsLeft) in units)
        {
            var unit = new Unit(id, type, position)
            {
                MovementLeft = movementLeft,
                Location = location,
                SailTurnsRemaining = sailTurns,
                CarrierId = carrierId,
                OwnerNationId = ownerNationId,
                OwnerId = ownerId,
                RoleId = roleId ?? RoleType.DefaultRoleId,
                RoleCount = roleCount,
                RepairTurnsRemaining = repairTurns,
                Orders = orders,
                Destination = destination,
                TradeRouteId = tradeRouteId,
                TradeRouteStopIndex = tradeRouteStopIndex,
                WorkImprovementId = workImprovementId,
                WorkTurnsLeft = workTurnsLeft,
            };
            unit.SetTreasureAmount(treasureAmount); // internal method, like AddCargo — set after the initializer
            foreach ((string goodsId, int amount) in cargo ?? new Dictionary<string, int>())
            {
                unit.AddCargo(goodsId, amount);
            }
            game._units.Add(unit);
            game._nextUnitId = Math.Max(game._nextUnitId, id + 1);
        }

        foreach (Colony colony in colonies ?? [])
        {
            game._colonies.Add(colony);
            game._nextColonyId = Math.Max(game._nextColonyId, colony.Id + 1);
        }

        foreach (NativeSettlement settlement in nativeSettlements ?? [])
        {
            game._nativeSettlements.Add(settlement);
            game._nextSettlementId = Math.Max(game._nextSettlementId, settlement.Id + 1);
        }

        game.ClaimNativeLand(); // re-derive native tile ownership from the restored settlements (never saved)

        // Fog: union each player's explored tiles; a pre-fog save (v1, explored null) re-derives its
        // fog by revealing around that player's units, mirroring the original load fallback.
        foreach (RestoredPlayer rp in players)
        {
            Player player = game._players.Single(p => p.PlayerId == rp.PlayerId);
            if (rp.Explored is not null)
            {
                player.ExploredSet.UnionWith(rp.Explored.Where(map.InBounds));
            }
            else
            {
                foreach (Unit unit in game._units.Where(u => IsOwnedBy(u, player)))
                {
                    game.Reveal(player, unit);
                }
            }
        }

        game.RefreshSonsOfLibertyModifiers(); // re-derive each colony's standing SoL bonus from its owner's Congress (Bolívar)
        return game;
    }

    /// <summary>
    /// Rebuilds a player and its market from saved per-player state (its fog is applied later, once units
    /// exist). A non-human player's own RNG stream is restored from the save, or — for a pre-FP-4 save that
    /// has none — re-derived deterministically from the restored main-stream state and its reserved id.
    /// </summary>
    private static Player BuildPlayer(Ruleset ruleset, RestoredPlayer saved, RandomState mainStream)
    {
        var market = new Market(ruleset);
        if (saved.MarketDeltas is { Count: > 0 })
        {
            market.LoadDeltas(saved.MarketDeltas);
        }
        if (saved.Arrears is { Count: > 0 })
        {
            market.LoadArrears(saved.Arrears); // boycott back-taxes (v37)
        }
        var player = new Player(saved.PlayerId, saved.NationId, saved.IsHuman, saved.PlayerType, market)
        {
            Gold = saved.Gold,
            TaxRate = saved.TaxRate,
            Liberty = saved.Liberty,
            CurrentFather = saved.CurrentFather,
            Immigration = saved.Immigration,
            ImmigrationRequired = saved.ImmigrationRequired,
            BaseRecruitPrice = saved.BaseRecruitPrice,
            RecruitLowerCap = saved.RecruitLowerCap,
            MonarchDispleasure = saved.MonarchDispleasure,
            SupportSeaGranted = saved.SupportSeaGranted,
            DeclaredIndependenceTurn = saved.DeclaredIndependenceTurn,
            InterventionBells = saved.InterventionBells,
        };
        if (saved.Congress is not null)
        {
            player.CongressList.AddRange(saved.Congress);
        }
        if (saved.OfferedFathers is not null)
        {
            player.OfferedFathersList.AddRange(saved.OfferedFathers);
        }
        if (saved.RecruitDock is not null)
        {
            player.RecruitDockList.AddRange(saved.RecruitDock);
        }
        if (saved.TradeRoutes is { Count: > 0 })
        {
            player.TradeRoutesList.AddRange(saved.TradeRoutes);
        }
        // Restore the monotonic id counter exactly (v45+) so ids are never reused after a delete-then-reload; a
        // pre-v45 save lacks it, so fall back to max(restored id) + 1 (or 1 when route-free), the old behaviour.
        player.NextTradeRouteId = saved.NextTradeRouteId
            ?? (saved.TradeRoutes is { Count: > 0 } restoredRoutes ? restoredRoutes.Max(r => r.Id) + 1 : 1);
        if (saved.Stances is not null)
        {
            foreach ((int otherId, Stance stance) in saved.Stances)
            {
                player.StanceMap[otherId] = stance;
            }
        }
        if (saved.Tensions is not null)
        {
            foreach ((int otherId, int tension) in saved.Tensions)
            {
                player.TensionMap[otherId] = tension;
            }
        }
        if (saved.UnitPrices is not null)
        {
            foreach ((string unitTypeId, int price) in saved.UnitPrices)
            {
                player.UnitPriceMap[unitTypeId] = price; // escalated Europe purchase prices (artillery)
            }
        }
        if (!saved.IsHuman)
        {
            player.Rng = saved.Rng is { } rng
                ? Pcg32Random.FromState(rng)
                : new Pcg32Random(mainStream.State, player.RngStreamId); // pre-FP-4 / synthesized row
        }
        return player;
    }

    /// <summary>The game's RNG state, captured for saving.</summary>
    internal RandomState RandomState => _random.SaveState();

    /// <summary>
    /// Creates a new unit at a position. A player unit reveals its surroundings; a native-owned unit
    /// (a brave, <paramref name="ownerNationId"/> set) does not — natives don't lift the player's fog.
    /// </summary>
    public Unit SpawnUnit(UnitType type, Position position, string? ownerNationId = null) =>
        SpawnUnit(type, position, ownerId: 0, ownerNationId);

    /// <summary>
    /// Spawns a unit owned by a specific colonial player (FreeCol gives a colony-built unit the colony's owner).
    /// The owner is set <em>before</em> fog is lifted, so a foreign power's new unit reveals its own fog, not the
    /// human's. The public overload keeps the default human owner (0).
    /// </summary>
    internal Unit SpawnUnit(UnitType type, Position position, int ownerId, string? ownerNationId = null)
    {
        if (!Map.InBounds(position))
        {
            throw new ArgumentOutOfRangeException(nameof(position), position, "Off the map.");
        }
        if (Map.TerrainAt(position).IsWater != type.IsNaval)
        {
            throw new InvalidMoveException(type.IsNaval
                ? "Naval units must be placed on water."
                : "Land units cannot be placed on water.");
        }

        var unit = new Unit(_nextUnitId++, type, position) { OwnerId = ownerId, OwnerNationId = ownerNationId };
        _units.Add(unit);
        RevealForOwner(unit); // a unit lifts its own owner's fog (the human's, or a foreign power's; natives none)
        return unit;
    }

    /// <summary>
    /// A unit's movement points for a fresh turn: its unit-type base plus its role's movement bonus
    /// (FreeCol <c>Unit.getInitialMovesLeft</c> folding <c>model.modifier.movementBonus</c>) — e.g. a
    /// dragoon/scout/cavalry/mounted brave gets +9 (one extra "move" is 3 points). For a <b>naval</b> unit it
    /// also folds the owner's Congress <c>movementBonus</c> — among fathers only <b>Ferdinand Magellan</b> (+3,
    /// scoped to naval units) carries it, so the <c>IsNaval</c> gate is Magellan's scope. The role lookup is
    /// null-safe so minimal rulesets without role data simply get the base. (Per-nation movement bonuses are a
    /// separate scoped modifier, still deferred.)
    /// </summary>
    private int InitialMovement(Unit unit)
    {
        int moves = unit.Type.Movement + (int)(Ruleset.Roles.FirstOrDefault(r => r.Id == unit.RoleId)?.MovementBonus ?? 0);
        if (unit.Type.IsNaval && PlayerById(unit.OwnerId) is { } owner)
        {
            moves = ApplyGoodsModifiers(owner, MovementBonusId, moves); // Magellan +3 (naval-scoped)
        }
        return moves;
    }

    /// <summary>
    /// Whether <paramref name="unit"/> may move to <paramref name="target"/> right now, and why not if not. A tile
    /// held by an <em>enemy</em> unit (<see cref="AreEnemies"/>) routes to "attack it instead"; a tile held by a
    /// foreign power's unit you are <b>not</b> at war with — a colonial rival at <see cref="Stance.Peace"/>,
    /// <see cref="Stance.CeaseFire"/> or <see cref="Stance.Alliance"/> (86d3drn45) — blocks the move outright: you
    /// cannot enter a peaceful neighbour's territory (nor attack them) until war is declared.
    /// </summary>
    public MoveCheck CheckMove(Unit unit, Position target)
    {
        if (!unit.IsOnMap)
        {
            return MoveCheck.No("The unit is at sea or in Europe.");
        }
        if (!Map.InBounds(target))
        {
            return MoveCheck.No("Destination is off the map.");
        }
        if (!unit.Position.IsAdjacentTo(target))
        {
            return MoveCheck.No("Units move one tile at a time.");
        }

        TerrainType terrain = Map.TerrainAt(target);
        if (terrain.IsWater && !unit.Type.IsNaval)
        {
            return MoveCheck.No("Land units cannot enter water.");
        }
        if (!terrain.IsWater && unit.Type.IsNaval)
        {
            return MoveCheck.No("Ships cannot move onto land.");
        }
        if (DefenderAt(unit, target) is not null)
        {
            return MoveCheck.No("An enemy unit holds that tile — attack it instead.");
        }
        // A non-enemy foreign unit (a colonial rival at Peace/CeaseFire/Alliance, now that AreEnemies gates on stance)
        // still occupies the tile — you cannot share it or attack them, so the move is blocked (FreeCol: you may not
        // enter a tile a power you are not at war with stands on). A friendly stack of your OWN units does not block.
        if (_units.Any(u => u.IsOnMap && u.Position == target && !SameOwner(unit, u)))
        {
            return MoveCheck.No("A foreign power's unit holds that tile — you are not at war with them.");
        }
        if (!unit.IsNative && NativeSettlementAt(target) is not null)
        {
            return MoveCheck.No("A native settlement holds that tile — attack, trade or speak with it from beside it.");
        }
        if (ColonyAt(target) is { } colony && (unit.IsNative || colony.OwnerId != unit.OwnerId))
        {
            // You may only enter a colony you own (to garrison/join). A native brave owns none, so it is kept
            // off every colony — including the human's, whose OwnerId 0 would otherwise collide with a brave's
            // unused OwnerId 0; a colonial unit is blocked from a rival's colony. Capturing a colony is a later
            // slice, and DefenderAt above already routes a garrisoned tile to "attack instead".
            return MoveCheck.No("An enemy colony holds that tile — attack it from beside it.");
        }

        int movesLeft = unit.MovementLeft;
        if (movesLeft <= 0)
        {
            return MoveCheck.No("No movement left this turn.");
        }

        // River/road "follow it" bonus (FreeCol TileImprovementType.getMoveCost + Tile connectivity): a land unit
        // moving between two tiles that BOTH carry a movement-granting improvement (a river or a pioneer-built road)
        // pays the reduced enter-cost (1) instead of the terrain's normal cost, when that is cheaper. Ships never get
        // it (rivers/roads are land features). The cost to enter is a property of the destination's improvements —
        // see ImprovementMovement.MoveCost (generalised over a tile's improvements).
        int cost = terrain.MoveCost;
        if (!unit.Type.IsNaval)
        {
            cost = ImprovementMovement.MoveCost(
                Map.ImprovementsAt(unit.Position), Map.ImprovementsAt(target), cost);
        }
        // FreeCol's partial-movement rule (Unit.getMoveCost): when the terrain
        // costs more than the unit has left, the move is still allowed — for the
        // full remainder — only if the unit is near full movement (lost at most
        // 2/3 of a move) or the shortfall is small. (A settlement target also
        // qualifies; none exist yet.) Otherwise the unit must wait.
        if (cost > movesLeft)
        {
            bool allowed = movesLeft + 2 >= InitialMovement(unit) || cost <= movesLeft + 2;
            if (!allowed)
            {
                return MoveCheck.No("Not enough movement left this turn.");
            }
            cost = movesLeft;
        }
        return MoveCheck.Yes(cost);
    }

    /// <summary>Moves a unit one tile, spending movement and revealing new ground.</summary>
    /// <exception cref="InvalidMoveException">The move is not allowed; see <see cref="CheckMove"/>.</exception>
    public void MoveUnit(Unit unit, Position target)
    {
        MoveCheck check = CheckMove(unit, target);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }

        unit.Position = target;
        unit.MovementLeft -= check.Cost;
        unit.Orders = UnitOrders.Active; // moving wakes a fortified/sentry unit (FreeCol clears the state on a move)
        unit.Destination = null;         // a manual move cancels any standing goto (FreeCol setDestination(null))
        RevealForOwner(unit); // the mover lifts its own owner's fog (mirrors SpawnUnit)
        if (unit.Type.IsCarrier)
        {
            SyncPassengers(unit); // any colonists aboard move with the ship
        }
        TryExploreRumour(unit, target); // a land unit stepping onto a Lost City Rumour investigates it (may consume/transform the unit)
    }

    /// <summary>
    /// Whether <paramref name="unit"/> may be ordered to fortify (dig in for a +50% defence bonus). A land
    /// unit on the map, not under repair; ships sentry instead of fortifying (FreeCol fortify is a land order).
    /// </summary>
    public MoveCheck CheckFortify(Unit unit)
    {
        if (!unit.IsOnMap)
        {
            return MoveCheck.No("The unit is at sea or in Europe.");
        }
        if (unit.Type.IsNaval)
        {
            return MoveCheck.No("A ship cannot fortify (set it to sentry instead).");
        }
        if (unit.Orders is UnitOrders.Fortifying or UnitOrders.Fortified)
        {
            return MoveCheck.No("The unit is already fortifying.");
        }
        return MoveCheck.Yes(0);
    }

    /// <summary>
    /// Orders a unit to fortify: it spends the rest of its turn digging in (FreeCol FORTIFYING), and at the
    /// next turn it becomes <see cref="UnitOrders.Fortified"/> — a +50% defence bonus until it is moved.
    /// </summary>
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckFortify"/>.</exception>
    public void Fortify(Unit unit)
    {
        MoveCheck check = CheckFortify(unit);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }
        unit.Orders = UnitOrders.Fortifying;
        unit.MovementLeft = 0; // fortifying consumes the turn (you can't dig in and then march)
    }

    /// <summary>Whether <paramref name="unit"/> may be set to sentry (rest until something happens).</summary>
    public MoveCheck CheckSentry(Unit unit) =>
        unit.IsOnMap ? MoveCheck.Yes(0) : MoveCheck.No("The unit is at sea or in Europe.");

    /// <summary>Sets a unit to sentry — it rests (skipped when cycling units) until woken by a move or cleared.</summary>
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckSentry"/>.</exception>
    public void Sentry(Unit unit)
    {
        MoveCheck check = CheckSentry(unit);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }
        unit.Orders = UnitOrders.Sentry;
        unit.MovementLeft = 0; // resting consumes the turn
    }

    /// <summary>Clears a unit's standing order back to active (it does not refund the spent movement). Also cancels an in-progress tile improvement (the work is abandoned; tools already committed are not refunded — FreeCol keeps the partial progress lost).</summary>
    public void ClearOrders(Unit unit)
    {
        unit.Orders = UnitOrders.Active;
        unit.WorkImprovementId = null;
        unit.WorkTurnsLeft = 0;
    }

    /// <summary>
    /// Whether <paramref name="unit"/> may start building the tile improvement <paramref name="improvementId"/>
    /// (FreeCol <c>InGameController.askImprove</c> / <c>TileImprovementType.isWorkerAllowed</c> +
    /// <c>Tile.isImprovementAllowed</c>): a tooled pioneer (a colonial land unit whose role grants
    /// <c>improveTerrain</c> and still holds equipment) standing on a land tile whose terrain the improvement applies
    /// to, where that improvement is not already present, and which is not currently building/garrisoned in a way
    /// that forbids it. Returns the reason when not allowed.
    /// </summary>
    /// <param name="unit">The unit ordered to build.</param>
    /// <param name="improvementId">The improvement type id (<c>model.improvement.road</c>/<c>.plow</c>/<c>.clearForest</c>).</param>
    public MoveCheck CheckBuildImprovement(Unit unit, string improvementId)
    {
        if (unit.IsNative)
        {
            return MoveCheck.No("Native units do not build tile improvements.");
        }
        if (!unit.IsOnMap)
        {
            return MoveCheck.No("The unit is at sea or in Europe.");
        }
        if (!Ruleset.ImprovementTypes.Any(i => i.Id == improvementId))
        {
            return MoveCheck.No($"Unknown improvement '{improvementId}'.");
        }
        TileImprovementType improvement = Ruleset.Improvement(improvementId);
        if (improvement.IsNatural)
        {
            return MoveCheck.No("A river is a natural feature — it cannot be built.");
        }
        // The unit must hold a role that can improve terrain (the pioneer role) and still have tools to spend.
        if (!Ruleset.Role(unit.RoleId).CanImproveTerrain || unit.RoleCount <= 0)
        {
            return MoveCheck.No("Only a pioneer carrying tools can build improvements.");
        }
        TerrainType terrain = Map.TerrainAt(unit.Position);
        if (!improvement.AppliesTo(terrain))
        {
            return MoveCheck.No($"A {improvement.ShortName} cannot be built on {terrain.ShortName}.");
        }
        // Already present? A road/plow laid on top can't be re-laid; a terrain-changing improvement (clear-forest)
        // is gated by applicability instead (you can't clear a non-forest), so it needs no presence check.
        if (!improvement.ChangesTerrain && Map.HasImprovement(unit.Position, improvementId))
        {
            return MoveCheck.No($"This tile already has a {improvement.ShortName}.");
        }
        if (unit.IsImproving && unit.WorkImprovementId == improvementId)
        {
            return MoveCheck.No($"The pioneer is already building a {improvement.ShortName} here.");
        }
        if (unit.MovementLeft <= 0)
        {
            return MoveCheck.No("No movement left this turn.");
        }
        return MoveCheck.Yes(0);
    }

    /// <summary>
    /// Orders a pioneer to start building a tile improvement: it commits to the work (spending the rest of its turn)
    /// and accrues <see cref="WorkTurnsToComplete"/> turns of work; the improvement lands and tools are consumed when
    /// the work finishes (see <see cref="ProcessImprovements"/>). Re-ordering switches the target improvement.
    /// </summary>
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckBuildImprovement"/>.</exception>
    public void BuildImprovement(Unit unit, string improvementId)
    {
        MoveCheck check = CheckBuildImprovement(unit, improvementId);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }
        TileImprovementType improvement = Ruleset.Improvement(improvementId);
        unit.Orders = UnitOrders.Active;        // building supersedes a fortify/sentry order
        unit.WorkImprovementId = improvementId;
        unit.WorkTurnsLeft = WorkTurnsToComplete(Map.TerrainAt(unit.Position), improvement);
        unit.MovementLeft = 0;                  // the pioneer is now busy for the rest of the turn
    }

    /// <summary>
    /// The total turns of work to complete <paramref name="improvement"/> on <paramref name="terrain"/> (FreeCol
    /// <c>TileImprovement</c> ctor: the terrain's basic work-turns plus the improvement's add-work-turns). A hardy
    /// pioneer works this down at 2/turn, a regular pioneer at 1/turn (see <see cref="ProcessImprovements"/>).
    /// </summary>
    /// <param name="terrain">The terrain being improved.</param>
    /// <param name="improvement">The improvement type.</param>
    public static int WorkTurnsToComplete(TerrainType terrain, TileImprovementType improvement) =>
        terrain.WorkTurns + improvement.AddWorkTurns;

    /// <summary>The work a unit does per turn on a tile improvement: 2 for a hardy (expert) pioneer, 1 otherwise (FreeCol <c>Ability.EXPERT_PIONEER</c>).</summary>
    private int ImprovementWorkPerTurn(Unit unit) =>
        unit.WorkImprovementId is { } id
        && Ruleset.Improvement(id).RequiredRoleId is { } roleId
        && Ruleset.Role(roleId).ExpertUnit == unit.Type.Id
            ? 2
            : 1;

    /// <summary>
    /// Advances every one of <paramref name="player"/>'s pioneers currently building a tile improvement (id order,
    /// for determinism), completing those whose work finishes this turn. Called on the player's turn before the
    /// world's movement reset. A no-op (no RNG drawn) for a player with no improving units, so the human's stream 0
    /// stays byte-identical when nobody is pioneering (ADR-009).
    /// </summary>
    internal void ProcessImprovements(Player player)
    {
        foreach (Unit unit in _units
            .Where(u => u.OwnerId == player.PlayerId && !u.IsNative && u.IsOnMap && u.IsImproving)
            .OrderBy(u => u.Id)
            .ToList()) // materialise: completion can change terrain / deliver goods
        {
            unit.WorkTurnsLeft = Math.Max(0, unit.WorkTurnsLeft - ImprovementWorkPerTurn(unit));
            if (unit.WorkTurnsLeft <= 0)
            {
                CompleteImprovement(unit);
            }
        }
    }

    /// <summary>
    /// Completes a pioneer's tile improvement (FreeCol <c>ServerUnit.csImproveTile</c>): lays a road/plow on the
    /// tile (or, for clear-forest, changes the terrain to its cleared base type and delivers the one-off lumber to
    /// the owning colony), consumes the role's expended tools (reverting the pioneer to a plain colonist when its
    /// tools run out), and clears the work order.
    /// </summary>
    private void CompleteImprovement(Unit unit)
    {
        TileImprovementType improvement = Ruleset.Improvement(unit.WorkImprovementId!);
        Position pos = unit.Position;

        if (improvement.ChangeFrom(Map.TerrainAt(pos).Id) is { } change)
        {
            // Terrain-changing improvement (clear-forest): retype the tile and deliver the one-off production
            // (lumber) to the colony that works the tile, if any. Improvements already on the tile (a river) survive.
            Map.SetTerrain(pos, Ruleset.Terrain(change.ToTerrainId));
            if (change.ProductionGoodsId is { } goodsId && change.ProductionAmount > 0
                && OwningColonyOf(unit.OwnerId, pos) is { } colony)
            {
                colony.AddGoods(Ruleset.StorageIdOf(goodsId), change.ProductionAmount);
            }
        }
        else
        {
            // A road/plow laid on top of the existing terrain (and any river already there).
            Map.AddImprovement(pos, improvement);
        }

        // Expend the role's tools (FreeCol changeRoleCount(-expendedAmount)); a pioneer out of tools reverts to a colonist.
        int remaining = unit.RoleCount - improvement.ExpendedAmount;
        if (remaining <= 0)
        {
            ChangeRole(unit, RoleType.DefaultRoleId, 0);
        }
        else
        {
            unit.RoleCount = remaining;
        }

        unit.WorkImprovementId = null;
        unit.WorkTurnsLeft = 0;
    }

    /// <summary>
    /// The colony of <paramref name="ownerId"/> that works the tile at <paramref name="tile"/> (its centre or one of
    /// its eight neighbours — the colony's 3×3 footprint), nearest first then by id for determinism, or null when
    /// none. FreeCol delivers a cleared forest's lumber to the tile's owning settlement; this is our footprint-based
    /// equivalent (we have no per-tile ownership layer for colonies).
    /// </summary>
    private Colony? OwningColonyOf(int ownerId, Position tile) =>
        _colonies
            .Where(c => c.OwnerId == ownerId && Chebyshev(c.Position, tile) <= 1)
            .OrderBy(c => Chebyshev(c.Position, tile))
            .ThenBy(c => c.Id)
            .FirstOrDefault();

    /// <summary>
    /// Whether <paramref name="unit"/> may be disbanded (permanently removed). A carrier still holding
    /// passengers cannot be disbanded — unload them first so they are not orphaned at sea.
    /// </summary>
    public MoveCheck CheckDisband(Unit unit)
    {
        if (unit.Type.IsCarrier && _units.Any(u => u.CarrierId == unit.Id))
        {
            return MoveCheck.No("Unload the ship's passengers before disbanding it.");
        }
        return MoveCheck.Yes(0);
    }

    /// <summary>Disbands a unit — it leaves the game for good (its hold, if any, is lost with it).</summary>
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckDisband"/>.</exception>
    public void Disband(Unit unit)
    {
        MoveCheck check = CheckDisband(unit);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }
        _units.Remove(unit);
    }

    /// <summary>
    /// Whether <paramref name="unit"/> may found a colony where it stands, and why
    /// not if not.
    /// </summary>
    public MoveCheck CheckFoundColony(Unit unit)
    {
        if (!unit.IsOnMap)
        {
            return MoveCheck.No("The unit is at sea or in Europe.");
        }
        if (!unit.Type.CanFoundColony)
        {
            return MoveCheck.No($"A {unit.Type.ShortName} cannot found a colony.");
        }
        TerrainType terrain = Map.TerrainAt(unit.Position);
        if (!terrain.CanSettle)
        {
            return MoveCheck.No($"A colony cannot be built on {terrain.ShortName}.");
        }
        if (ColonyAt(unit.Position) is not null)
        {
            return MoveCheck.No("There is already a colony here.");
        }
        // Minimum colony spacing (FreeCol Player.canClaimToFoundSettlementReason: tile.getAdjacentColonies()
        // must be empty): no colony may be founded on a tile adjacent to an existing colony, so colony
        // footprints never touch. Native-owned tiles do not block founding here: the land-claim API exists
        // (LandPrice / ClaimLandByPaying / ClaimLandByStealing), but the founding/working TRIGGER — being forced
        // to buy-or-steal the tile first — is deferred pending the pay-vs-steal UI choice (see natives.md).
        if (unit.Position.Neighbours().Any(n => Map.InBounds(n) && ColonyAt(n) is not null))
        {
            return MoveCheck.No("A colony cannot be founded next to another colony.");
        }
        return MoveCheck.Yes(0);
    }

    /// <summary>
    /// Founds a colony where the unit stands. The founding unit settles down and
    /// becomes the colony's first colonist (it leaves the map).
    /// </summary>
    /// <exception cref="InvalidMoveException">Founding is not allowed; see <see cref="CheckFoundColony"/>.</exception>
    public Colony FoundColony(Unit unit)
    {
        MoveCheck check = CheckFoundColony(unit);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }

        IReadOnlyList<string> names = ColonyNamesFor(unit.OwnerId);
        string name = names[(_nextColonyId - 1) % names.Count];
        var colony = new Colony(_nextColonyId++, name, unit.Position, population: 1, ownerId: unit.OwnerId)
        {
            Government = Ruleset.Difficulty.Government, // production-bonus thresholds from the difficulty level
        };

        // Every colony starts with the free base buildings (no build cost, not
        // an upgrade) — town hall, carpenter's house, the artisan houses, etc.
        foreach (BuildingType building in Ruleset.BuildingTypes
                     .Where(b => b.BuildCost.Count == 0 && b.UpgradesFrom is null))
        {
            colony.AddBuilding(building.Id);
        }

        _colonies.Add(colony);
        colony.SolModifierBonus = SolModifierFor(PlayerById(colony.OwnerId) ?? _human); // inherit the owner's standing SoL bonus (Bolívar)
        _units.Remove(unit);
        colony.AddIdleColonist(unit.Type.Id); // the founding colonist keeps its identity (an expert founds as an expert)
        // The colony keeps its surroundings explored — for its owner (the human, or a foreign founder; FP-4).
        RevealAround(PlayerById(colony.OwnerId) ?? _human, colony.Position, ColonySightRadius);
        AutoAssignIdleToFood(colony);
        if (colony.OwnerId == _human.PlayerId)
        {
            RecordHistory(HistoryEventKind.ColonyFounded, $"Founded {colony.Name}.");
        }
        return colony;
    }

    /// <summary>
    /// Whether the colonist <paramref name="unit"/> may join <paramref name="colony"/> —
    /// it must be a person on the map, standing on or next to the colony.
    /// </summary>
    public MoveCheck CheckJoinColony(Unit unit, Colony colony)
    {
        if (!unit.IsOnMap)
        {
            return MoveCheck.No("The unit is at sea or in Europe.");
        }
        if (!unit.Type.IsPerson)
        {
            return MoveCheck.No($"A {unit.Type.ShortName} cannot join a colony.");
        }
        if (unit.Position != colony.Position && !unit.Position.IsAdjacentTo(colony.Position))
        {
            return MoveCheck.No("The unit must be at the colony to join it.");
        }
        return MoveCheck.Yes(0);
    }

    /// <summary>Adds a colonist to a colony's population (the unit leaves the map; the newcomer is put to work).</summary>
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckJoinColony"/>.</exception>
    public void JoinColony(Unit unit, Colony colony)
    {
        MoveCheck check = CheckJoinColony(unit, colony);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }
        colony.Population++;
        colony.AddIdleColonist(unit.Type.Id); // the joining colonist keeps its identity
        _units.Remove(unit);
        AutoAssignIdleToFood(colony);
    }

    /// <summary>Whether a colonist may be detached from <paramref name="colony"/> (it must keep at least one).</summary>
    public MoveCheck CheckLeaveColony(Colony colony) =>
        colony.Population > 1
            ? MoveCheck.Yes(0)
            : MoveCheck.No("A colony must keep at least one colonist.");

    /// <summary>
    /// Detaches a colonist from a colony onto the colony's own tile. The departing colonist's <b>real unit type</b> is
    /// emitted (86d3b6nrz slice 6): a free colonist if the colony has one to spare (specialists keep working), else a
    /// specialist walks out as its own type — so an all-expert colony never silently turns an expert into a free
    /// colonist. <see cref="Colony.RemoveOneColonist"/> picks the colonist and keeps the colony's counts + overlay
    /// consistent.
    /// </summary>
    /// <returns>The detached unit, standing on the colony tile.</returns>
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckLeaveColony"/>.</exception>
    public Unit LeaveColony(Colony colony)
    {
        MoveCheck check = CheckLeaveColony(colony);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }
        string departingType = colony.RemoveOneColonist(); // pops the colonist + vacates its job, returning its type
        var unit = new Unit(_nextUnitId++, Ruleset.Unit(departingType), colony.Position)
        {
            OwnerId = colony.OwnerId, // the detached colonist belongs to the colony's owner (the human is 0)
        };
        _units.Add(unit);
        RevealForOwner(unit); // lifts the owning player's fog (the human's, or a foreign power's)
        return unit;
    }

    /// <summary>Renames a colony (FreeCol <c>renameObject</c> on a <c>Nameable</c>); the name must be non-blank.</summary>
    /// <exception cref="ArgumentException">The name is blank.</exception>
    public void RenameColony(Colony colony, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A colony name cannot be blank.", nameof(name));
        }
        colony.Name = name.Trim();
    }

    /// <summary>
    /// Whether a colony may be abandoned (given up and disposed). Faithful to FreeCol: you abandon the **last**
    /// colonist (reduce a bigger colony first with <see cref="LeaveColony"/>), and a colony with a fortification
    /// (stockade/fort/fortress) cannot be abandoned.
    /// </summary>
    public MoveCheck CheckAbandonColony(Colony colony)
    {
        if (colony.Population > 1)
        {
            return MoveCheck.No("Send the other colonists out before abandoning the colony.");
        }
        if (ColonyDefenceBonus(colony) > 0)
        {
            return MoveCheck.No("A fortified colony (stockade/fort/fortress) cannot be abandoned.");
        }
        return MoveCheck.Yes(0);
    }

    /// <summary>
    /// Abandons a colony: it is removed from the map and its last colonist walks out on the colony's tile as its
    /// <b>real unit type</b> — a lone expert farmer leaves as an expert farmer, not a free colonist (86d3b6nrz slice 6;
    /// FreeCol <c>abandonSettlement</c>).
    /// </summary>
    /// <returns>The departed colonist, standing where the colony was.</returns>
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckAbandonColony"/>.</exception>
    public Unit AbandonColony(Colony colony)
    {
        MoveCheck check = CheckAbandonColony(colony);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }
        string departingType = colony.RemoveOneColonist(); // the last colonist's real type
        var unit = new Unit(_nextUnitId++, Ruleset.Unit(departingType), colony.Position)
        {
            OwnerId = colony.OwnerId,
        };
        DisposeColony(colony);
        _units.Add(unit);
        RevealForOwner(unit);
        return unit;
    }

    /// <summary>Removes a colony from the game (its tile-work assignments go with it; any garrison units stay on the tile).</summary>
    private void DisposeColony(Colony colony) => _colonies.Remove(colony);

    /// <summary>
    /// Whether an idle colonist may be put to work in one of the colony's buildings.
    /// </summary>
    public MoveCheck CheckAssignBuildingWork(Colony colony, string buildingId)
    {
        if (!colony.HasBuilding(buildingId))
        {
            return MoveCheck.No("The colony does not have that building.");
        }
        if (colony.IdleColonists <= 0)
        {
            return MoveCheck.No("No idle colonists.");
        }
        BuildingType building = Ruleset.Building(buildingId);
        if (colony.BuildingWorkers.GetValueOrDefault(buildingId) >= building.Workplaces)
        {
            return MoveCheck.No($"The {building.ShortName} is fully staffed.");
        }
        return MoveCheck.Yes(0);
    }

    /// <summary>Puts an idle colonist to work in a building.</summary>
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckAssignBuildingWork"/>.</exception>
    public void AssignBuildingWork(Colony colony, string buildingId)
    {
        MoveCheck check = CheckAssignBuildingWork(colony, buildingId);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }
        colony.AssignBuildingWorker(buildingId, PickIdleBuildingWorker(colony));
    }

    /// <summary>
    /// Puts a <b>specific</b> idle colonist type to work in a building (the AI best-worker seam, FreeCol
    /// <c>getBestWorker</c>): when <paramref name="type"/> is a specialist it must currently be in the colony's idle
    /// overlay, so the caller has already established the colonist is available; a free-colonist <paramref name="type"/>
    /// falls back to the implicit free pool. Used by <see cref="PlanColonyBuildingWork"/> to assign the marginal
    /// best worker (its matching expert) to a building, rather than always a free colonist.
    /// </summary>
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckAssignBuildingWork"/>.</exception>
    internal void AssignBuildingWork(Colony colony, string buildingId, string type)
    {
        MoveCheck check = CheckAssignBuildingWork(colony, buildingId);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }
        // A specialist must be idle to be placed by type; otherwise fall back to a free colonist (AssignBuildingWorker
        // treats an unknown specialist as a free colonist — RemoveOneIdle is a no-op — but the guard keeps intent clear).
        string assigned = type != Colony.FreeColonistTypeId && !colony.IdleWorkerTypes.Contains(type)
            ? PickIdleBuildingWorker(colony)
            : type;
        colony.AssignBuildingWorker(buildingId, assigned);
    }

    /// <summary>Returns one of a building's workers to the idle pool.</summary>
    public void UnassignBuildingWork(Colony colony, string buildingId) =>
        colony.UnassignBuildingWorker(buildingId);

    /// <summary>
    /// Sells goods from a colony's warehouse to the European market, crediting the
    /// treasury after tax and moving the market price. (Phase 4 slice 1: an abstract
    /// "shipped to Europe" sale; slice 3 will require an actual ship to carry it.)
    /// </summary>
    /// <returns>The gold credited to the treasury after tax.</returns>
    /// <exception cref="InvalidMoveException">The good is untradeable or the colony lacks the amount.</exception>
    public int SellColonyGoods(Colony colony, string goodsId, int amount) =>
        SellColonyGoods(_human, colony, goodsId, amount);

    /// <summary>Sells a colony's goods to <paramref name="player"/>'s European market (the colony's owner today).</summary>
    internal int SellColonyGoods(Player player, Colony colony, string goodsId, int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);
        if (!player.Market.IsTradeable(goodsId))
        {
            throw new InvalidMoveException($"{goodsId} cannot be sold in Europe.");
        }
        if (!player.Market.CanTrade(goodsId))
        {
            throw new InvalidMoveException($"{goodsId} is under boycott — pay the back taxes to lift it.");
        }
        if (colony.StoreOf(goodsId) < amount)
        {
            throw new InvalidMoveException($"The colony does not have {amount} {goodsId} to sell.");
        }

        colony.AddGoods(goodsId, -amount);
        SaleResult sale = player.Market.Sell(goodsId, amount, player.TaxRate, MarketVolumeFactor(player));
        player.Gold += sale.GoldAfterTax;
        return sale.GoldAfterTax;
    }

    /// <summary>
    /// Sets a colony's custom-house export setting for a good (FreeCol <c>setGoodsLevels</c>): whether its surplus
    /// auto-sells and the level to retain (<paramref name="exportLevel"/> null keeps the current level). The good
    /// must be storable and tradeable (have a European market) — <b>food included</b>, which FreeCol's custom house
    /// can export (opt-in; it defaults off). Non-tradeable goods (hammers/bells/crosses) and non-storables are refused.
    /// Used in <see cref="AutoExportMode.PerGood"/> mode; the export-all mode ignores per-good settings (and protects
    /// food). (Setting is allowed regardless of whether the custom house is built — the auto-sell only acts when it is.)
    /// </summary>
    /// <exception cref="InvalidMoveException">The good cannot be exported (non-tradeable / non-storable).</exception>
    public void SetColonyExport(Colony colony, string goodsId, bool exported, int? exportLevel = null)
    {
        GoodsType goods = Ruleset.Goods(goodsId);
        if (!goods.IsTradeable || !goods.IsStorable)
        {
            throw new InvalidMoveException($"{goodsId} cannot be exported through the custom house.");
        }
        colony.SetExport(goodsId, exported, exportLevel ?? colony.ExportOf(goodsId).ExportLevel);
    }

    /// <summary>Base turns a naval unit spends crossing the high seas each way (FreeCol TURNS_TO_SAIL).</summary>
    public const int SailTurns = 3;

    /// <summary>The crossing length for a ship's owner: <see cref="SailTurns"/> shortened by the owner's Congress <c>sailHighSeas</c> modifier (Ferdinand Magellan −1), floored at 1.</summary>
    private int SailTurnsFor(Player? owner) =>
        owner is null ? SailTurns : Math.Max(1, ApplyGoodsModifiers(owner, SailHighSeasId, SailTurns));

    /// <summary>The human player's units currently docked in Europe (resolved by owner — FP-2).</summary>
    public IEnumerable<Unit> UnitsInEurope => _units.Where(u => u.Location == UnitLocation.InEurope && IsHumanOwned(u));

    /// <summary>Whether a naval unit may set sail for Europe from where it is.</summary>
    public MoveCheck CheckSailToEurope(Unit unit)
    {
        if (!unit.Type.IsNaval)
        {
            return MoveCheck.No("Only ships can sail to Europe.");
        }
        if (!unit.IsOnMap)
        {
            return MoveCheck.No("The ship is not on the map.");
        }
        if (unit.IsUnderRepair)
        {
            // A ship under forced repair cannot act — consistent with SailToNewWorld/CheckBoard/CheckBuyEuropeGoods
            // (FreeCol isReadyToTrade). Defence-in-depth: a damaged ship is relocated off the high seas, so this is
            // unreachable in normal play, but the guard keeps the sail oracle from contradicting its peers.
            return MoveCheck.No($"The ship is under repair for {unit.RepairTurnsRemaining} more turn(s).");
        }
        if (Map.TerrainAt(unit.Position).Id != HighSeasId)
        {
            return MoveCheck.No("Ships sail to Europe from the high seas (the map edge).");
        }
        return MoveCheck.Yes(0);
    }

    /// <summary>Sends a ship across the high seas to Europe (arrives in <see cref="SailTurns"/> turns).</summary>
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckSailToEurope"/>.</exception>
    public void SailToEurope(Unit unit)
    {
        MoveCheck check = CheckSailToEurope(unit);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }
        unit.Location = UnitLocation.SailingToEurope;
        unit.SailTurnsRemaining = SailTurnsFor(PlayerById(unit.OwnerId)); // Magellan shortens the crossing
        SyncPassengers(unit);
    }

    /// <summary>Sends a docked ship back to the New World (re-enters at its departure high-seas tile).</summary>
    /// <exception cref="InvalidMoveException">The ship is not in Europe.</exception>
    public void SailToNewWorld(Unit unit)
    {
        if (unit.IsAboard)
        {
            throw new InvalidMoveException("A unit aboard a ship cannot sail on its own.");
        }
        if (unit.Location != UnitLocation.InEurope)
        {
            throw new InvalidMoveException("Only a ship in Europe can sail to the New World.");
        }
        if (unit.IsUnderRepair)
        {
            throw new InvalidMoveException($"The ship is under repair for {unit.RepairTurnsRemaining} more turn(s).");
        }
        unit.Location = UnitLocation.SailingToNewWorld;
        unit.SailTurnsRemaining = SailTurnsFor(PlayerById(unit.OwnerId)); // Magellan shortens the crossing
        SyncPassengers(unit);
    }

    /// <summary>
    /// Loads goods from a colony's warehouse into an adjacent <b>carrier</b> — a ship <em>or</em> a wagon train
    /// (any unit with cargo space). The carrier must be on the colony's tile or next to it.
    /// </summary>
    /// <exception cref="InvalidMoveException">Not a carrier on the map, not adjacent, the colony lacks the goods, or no room in the hold.</exception>
    public void LoadFromColony(Unit carrier, Colony colony, string goodsId, int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);
        if (!carrier.Type.IsCarrier || !carrier.IsOnMap)
        {
            throw new InvalidMoveException("Only a carrier (a ship or wagon train) on the map can carry cargo.");
        }
        if (!carrier.Position.IsAdjacentTo(colony.Position) && carrier.Position != colony.Position)
        {
            throw new InvalidMoveException("The carrier must be next to the colony to load cargo.");
        }
        if (colony.StoreOf(goodsId) < amount)
        {
            throw new InvalidMoveException($"The colony does not have {amount} {goodsId}.");
        }
        if (ExtraGoodsSlots(carrier, goodsId, amount) > CargoSlotsFree(carrier))
        {
            throw new InvalidMoveException("The carrier has no room for that cargo.");
        }
        colony.AddGoods(goodsId, -amount);
        carrier.AddCargo(goodsId, amount);
    }

    /// <summary>
    /// Unloads goods from a <b>carrier</b> (a ship or a wagon train) into an adjacent colony's warehouse — the
    /// delivery half of overland/coastal haulage (FreeCol unload-at-settlement). The carrier must be on the colony's
    /// tile or next to it; warehouse overflow is handled by the colony's end-of-turn spoilage cap (<c>86d3c9nnp</c>).
    /// </summary>
    /// <exception cref="InvalidMoveException">Not a carrier on the map, not adjacent, or not carrying the goods.</exception>
    public void UnloadToColony(Unit carrier, Colony colony, string goodsId, int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);
        if (!carrier.Type.IsCarrier || !carrier.IsOnMap)
        {
            throw new InvalidMoveException("Only a carrier (a ship or wagon train) on the map can unload cargo.");
        }
        if (!carrier.Position.IsAdjacentTo(colony.Position) && carrier.Position != colony.Position)
        {
            throw new InvalidMoveException("The carrier must be next to the colony to unload cargo.");
        }
        if (carrier.CargoOf(goodsId) < amount)
        {
            throw new InvalidMoveException($"The carrier is not carrying {amount} {goodsId}.");
        }
        carrier.AddCargo(goodsId, -amount);
        colony.AddGoods(goodsId, amount);
    }

    // ===== Trade routes (86d3c9rq1) =========================================================================
    // A player defines named routes (an ordered ring of colony stops, each listing the goods to load there); a
    // carrier assigned to a route auto-hauls along it each turn — delivering what a stop doesn't want, loading what
    // it does, then heading for the next stop. Reuses LoadFromColony/UnloadToColony (the carrier-haulage seam).

    /// <summary>
    /// Creates a trade route for <paramref name="player"/> from an ordered list of stops and returns it (FreeCol
    /// <c>Player.newTradeRoute</c>). Every stop must name a colony <paramref name="player"/> owns. The route is given
    /// the next per-player id and added to <see cref="Player.TradeRoutes"/>.
    /// </summary>
    /// <exception cref="InvalidMoveException">A stop names a colony the player does not own.</exception>
    public TradeRoute CreateTradeRoute(Player player, string name, IReadOnlyList<TradeRouteStop> stops)
    {
        foreach (TradeRouteStop stop in stops)
        {
            if (_colonies.FirstOrDefault(c => c.Id == stop.ColonyId) is not { } colony || colony.OwnerId != player.PlayerId)
            {
                throw new InvalidMoveException("A trade-route stop must be one of your own colonies.");
            }
        }
        var route = new TradeRoute(player.NextTradeRouteId++, name, stops.ToList());
        player.TradeRoutesList.Add(route);
        return route;
    }

    /// <summary>Whether <paramref name="unit"/> may run trade route <paramref name="routeId"/>: it must be a carrier (cargo space) owned by a player that has the route.</summary>
    public MoveCheck CheckAssignTradeRoute(Unit unit, int routeId)
    {
        if (!unit.Type.IsCarrier)
        {
            return MoveCheck.No("Only a carrier (a ship or wagon train) can run a trade route.");
        }
        if (PlayerById(unit.OwnerId) is not { } owner || owner.TradeRoutes.All(r => r.Id != routeId))
        {
            return MoveCheck.No("No such trade route for this unit's owner.");
        }
        return MoveCheck.Yes(0);
    }

    /// <summary>Assigns <paramref name="unit"/> to a trade route, starting at its first stop (FreeCol <c>Unit.setTradeRoute</c>).</summary>
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckAssignTradeRoute"/>.</exception>
    public void AssignTradeRoute(Unit unit, int routeId)
    {
        MoveCheck check = CheckAssignTradeRoute(unit, routeId);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }
        unit.TradeRouteId = routeId;
        unit.TradeRouteStopIndex = 0;
    }

    /// <summary>Takes <paramref name="unit"/> off its trade route (FreeCol <c>Unit.setTradeRoute(null)</c>).</summary>
    public void ClearTradeRoute(Unit unit)
    {
        unit.TradeRouteId = null;
        unit.TradeRouteStopIndex = 0;
    }

    /// <summary>
    /// Deletes trade route <paramref name="routeId"/> from <paramref name="player"/> and un-assigns every carrier that
    /// was running it (FreeCol <c>Player.removeTradeRoute</c> + the units' <c>setTradeRoute(null)</c>). No-op if the
    /// player has no such route.
    /// </summary>
    public void RemoveTradeRoute(Player player, int routeId)
    {
        if (player.TradeRoutesList.RemoveAll(r => r.Id == routeId) == 0)
        {
            return;
        }
        foreach (Unit unit in _units.Where(u => u.OwnerId == player.PlayerId && u.TradeRouteId == routeId))
        {
            ClearTradeRoute(unit);
        }
    }

    /// <summary>
    /// Runs <paramref name="player"/>'s trade-route carriers for the turn (FreeCol's trade-route haul): each assigned
    /// carrier heads for its current stop's colony; on arrival it <b>delivers</b> everything it holds that the stop
    /// doesn't list to load (<see cref="UnloadToColony"/>), <b>loads</b> the stop's goods up to its hold
    /// (<see cref="LoadFromColony"/>), and advances to the next stop (wrapping). A carrier whose route was deleted is
    /// dropped; a stop whose colony is gone is skipped. The step uses <see cref="StepToward"/> on the owner's stream —
    /// a route-less player iterates nothing, so it never perturbs the human's stream 0 or churns goldens (ADR-009).
    /// </summary>
    private void ProcessTradeRoutes(Player player)
    {
        foreach (Unit unit in _units
            .Where(u => u.OwnerId == player.PlayerId && u.IsOnTradeRoute && u.IsOnMap)
            .OrderBy(u => u.Id).ToList())
        {
            if (player.TradeRoutes.FirstOrDefault(r => r.Id == unit.TradeRouteId) is not { Stops.Count: > 0 } route)
            {
                ClearTradeRoute(unit); // the route was deleted (or is empty) → drop the assignment
                continue;
            }
            int stopIndex = unit.TradeRouteStopIndex % route.Stops.Count;
            TradeRouteStop stop = route.Stops[stopIndex];
            if (_colonies.FirstOrDefault(c => c.Id == stop.ColonyId) is not { } colony)
            {
                unit.TradeRouteStopIndex = (stopIndex + 1) % route.Stops.Count; // the stop's colony is gone → skip it
                continue;
            }
            if (unit.Position == colony.Position || unit.Position.IsAdjacentTo(colony.Position))
            {
                ServeTradeRouteStop(unit, colony, stop);
                unit.TradeRouteStopIndex = (stopIndex + 1) % route.Stops.Count;
            }
            else if (StepToward(player, unit, colony.Position) is { } step)
            {
                MoveUnit(unit, step);
            }
        }
    }

    /// <summary>Serves one trade-route stop: deliver everything the carrier holds that <paramref name="stop"/> doesn't load, then load the stop's goods up to the carrier's free hold.</summary>
    private void ServeTradeRouteStop(Unit carrier, Colony colony, TradeRouteStop stop)
    {
        foreach ((string goodsId, int amount) in carrier.Cargo.Where(c => !stop.LoadGoodsIds.Contains(c.Key)).ToList())
        {
            UnloadToColony(carrier, colony, goodsId, amount); // deliver what this stop doesn't want
        }
        foreach (string goodsId in stop.LoadGoodsIds)
        {
            int available = colony.StoreOf(goodsId);
            int partial = SlotsFor(carrier.CargoOf(goodsId)) * CargoSlotSize - carrier.CargoOf(goodsId); // slack in the current stack
            int load = Math.Min(available, partial + CargoSlotsFree(carrier) * CargoSlotSize);
            if (load > 0)
            {
                LoadFromColony(carrier, colony, goodsId, load);
            }
        }
    }

    /// <summary>Sells goods from a docked ship's hold to the European market, crediting the treasury after tax.</summary>
    /// <returns>The gold credited after tax.</returns>
    /// <exception cref="InvalidMoveException">The ship isn't in Europe, the good is untradeable, or the hold lacks it.</exception>
    public int SellShipCargo(Unit ship, string goodsId, int amount) =>
        SellShipCargo(_human, ship, goodsId, amount);

    /// <summary>Sells a docked ship's cargo to <paramref name="player"/>'s European market (the ship's owner today).</summary>
    internal int SellShipCargo(Player player, Unit ship, string goodsId, int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);
        if (ship.Location != UnitLocation.InEurope)
        {
            throw new InvalidMoveException("Goods are sold once the ship reaches Europe.");
        }
        if (!player.Market.IsTradeable(goodsId))
        {
            throw new InvalidMoveException($"{goodsId} cannot be sold in Europe.");
        }
        if (!player.Market.CanTrade(goodsId))
        {
            throw new InvalidMoveException($"{goodsId} is under boycott — pay the back taxes to lift it.");
        }
        if (ship.CargoOf(goodsId) < amount)
        {
            throw new InvalidMoveException($"The ship is not carrying {amount} {goodsId}.");
        }
        ship.AddCargo(goodsId, -amount);
        SaleResult sale = player.Market.Sell(goodsId, amount, player.TaxRate, MarketVolumeFactor(player));
        player.Gold += sale.GoldAfterTax;
        return sale.GoldAfterTax;
    }

    /// <summary>
    /// Whether the player can buy <paramref name="amount"/> of a good in Europe for the docked
    /// <paramref name="ship"/>. The quoted cost is the <b>chunked</b> price (buying lifts the ask as it drains the
    /// market, FreeCol <c>buyInEurope</c>), so a large buy costs more than a flat ask × amount.
    /// </summary>
    public MoveCheck CheckBuyEuropeGoods(Unit ship, string goodsId, int amount) =>
        CheckBuyEuropeGoods(_human, ship, goodsId, amount);

    /// <summary>Whether <paramref name="player"/> can buy <paramref name="amount"/> of a good for the docked <paramref name="ship"/>.</summary>
    internal MoveCheck CheckBuyEuropeGoods(Player player, Unit ship, string goodsId, int amount)
    {
        if (ship.Location != UnitLocation.InEurope)
        {
            return MoveCheck.No("Goods are bought once the ship reaches Europe.");
        }
        if (ship.IsUnderRepair)
        {
            return MoveCheck.No("The ship is under repair and cannot take on cargo."); // FreeCol: not ready to trade
        }
        if (!player.Market.IsTradeable(goodsId))
        {
            return MoveCheck.No($"{goodsId} is not sold in Europe.");
        }
        int cost = player.Market.BuyCost(goodsId, amount, MarketVolumeFactor(player));
        if (player.Gold < cost)
        {
            return MoveCheck.No($"Not enough gold (need {cost}).");
        }
        if (ExtraGoodsSlots(ship, goodsId, amount) > CargoSlotsFree(ship))
        {
            return MoveCheck.No("The ship has no room for that cargo.");
        }
        return MoveCheck.Yes(cost);
    }

    /// <summary>Buys goods in Europe into a docked ship's hold, debiting the treasury and lifting the market's ask price.</summary>
    /// <returns>The gold spent.</returns>
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckBuyEuropeGoods(Unit, string, int)"/>.</exception>
    public int BuyEuropeGoods(Unit ship, string goodsId, int amount) =>
        BuyEuropeGoods(_human, ship, goodsId, amount);

    /// <summary>Buys goods in Europe for <paramref name="player"/> into a docked ship's hold (the ship's owner today).</summary>
    internal int BuyEuropeGoods(Player player, Unit ship, string goodsId, int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);
        MoveCheck check = CheckBuyEuropeGoods(player, ship, goodsId, amount);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }
        int cost = player.Market.Buy(goodsId, amount, MarketVolumeFactor(player)); // moves the market (the ask rises)
        player.Gold -= cost;
        ship.AddCargo(goodsId, amount);
        return cost;
    }

    /// <summary>
    /// A high-seas tile a ship bought in Europe enters the New World at (the map's
    /// entry edge). Falls back to (0,0) on maps with no high seas (test fixtures).
    /// </summary>
    private Position EuropeEntryTile() =>
        Map.AllPositions().FirstOrDefault(p => Map.TerrainAt(p).Id == HighSeasId, new Position(0, 0));

    /// <summary>
    /// The water tile nearest <paramref name="origin"/> (Chebyshev), or null when the map has no water (test fixtures).
    /// Used to fix the REF's entry tile at the human's coast (FreeCol stores a non-land entry tile near each start).
    /// Deterministic (stable distance + row/column tie-break); draws no RNG.
    /// </summary>
    private Position? NearestWaterTile(Position origin) =>
        Map.AllPositions()
            .Where(p => Map.TerrainAt(p).IsWater)
            .OrderBy(p => Chebyshev(p, origin)).ThenBy(p => p.Y).ThenBy(p => p.X)
            .Cast<Position?>()
            .FirstOrDefault();

    /// <summary>Whether the player can buy a <paramref name="unitTypeId"/> in Europe right now.</summary>
    public MoveCheck CheckBuyUnit(string unitTypeId) => CheckBuyUnit(_human, unitTypeId);

    /// <summary>The Europe unit whose purchase price escalates (FreeCol <c>priceIncreasePerType</c> — artillery is the only classic one).</summary>
    private const string ArtilleryUnitTypeId = "model.unit.artillery";


    /// <summary>This player's current Europe price for a unit type — its escalated override (artillery) or the ruleset base.</summary>
    private static int EuropeUnitPrice(Player player, UnitType type) =>
        player.UnitPriceOverrides.GetValueOrDefault(type.Id, type.Price);

    /// <summary>The unit types <b>trained</b> in this player's Europe (priced specialists, skill &gt; 0), in ruleset order (FreeCol <c>getUnitTypesTrainedInEurope</c>).</summary>
    public IReadOnlyList<UnitType> UnitTypesTrainedInEurope() => [.. Ruleset.UnitTypes.Where(t => t.IsTrainedInEurope)];

    /// <summary>The unit types <b>purchased</b> in this player's Europe (priced, no skill — ships + artillery), in ruleset order (FreeCol <c>getUnitTypesPurchasedInEurope</c>).</summary>
    public IReadOnlyList<UnitType> UnitTypesPurchasedInEurope() => [.. Ruleset.UnitTypes.Where(t => t.IsPurchasedInEurope)];

    /// <summary>Whether <paramref name="player"/> can buy a <paramref name="unitTypeId"/> in Europe right now.</summary>
    internal MoveCheck CheckBuyUnit(Player player, string unitTypeId)
    {
        UnitType type = Ruleset.Unit(unitTypeId);
        if (!type.IsPurchasable)
        {
            return MoveCheck.No($"A {type.ShortName} cannot be bought in Europe.");
        }
        int price = EuropeUnitPrice(player, type);
        if (player.Gold < price)
        {
            return MoveCheck.No($"Not enough gold (need {price}).");
        }
        return MoveCheck.Yes(price);
    }

    /// <summary>
    /// Buys a unit in Europe for gold; it appears docked there. A ship enters at the
    /// high-seas tile so it can sail to the New World; a land unit waits on the dock to board one.
    /// </summary>
    /// <returns>The purchased unit, in Europe.</returns>
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckBuyUnit(string)"/>.</exception>
    public Unit BuyUnit(string unitTypeId) => BuyUnit(_human, unitTypeId);

    /// <summary>Buys a unit in Europe for <paramref name="player"/> (the human today; bought units carry no native owner).</summary>
    internal Unit BuyUnit(Player player, string unitTypeId)
    {
        MoveCheck check = CheckBuyUnit(player, unitTypeId);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }
        player.Gold -= check.Cost;
        UnitType type = Ruleset.Unit(unitTypeId);
        // Artillery's price ratchets +100 for this player per purchase (FreeCol increasePrice; ships/specialists stay
        // flat). The override starts from the price just paid, so 500 → 600 → 700…
        if (unitTypeId == ArtilleryUnitTypeId)
        {
            player.UnitPriceMap[unitTypeId] = check.Cost + Ruleset.Difficulty.ArtilleryPriceIncrease;
        }
        var unit = new Unit(_nextUnitId++, type, type.IsNaval ? EuropeEntryTile() : new Position(0, 0))
        {
            Location = UnitLocation.InEurope,
            OwnerId = player.PlayerId, // the bought unit belongs to its buyer (the human is 0; a foreign power its own id)
        };
        _units.Add(unit);
        return unit;
    }

    /// <summary>Whether <paramref name="player"/> can train the specialist <paramref name="unitTypeId"/> in Europe right now.</summary>
    internal MoveCheck CheckTrain(Player player, string unitTypeId)
    {
        UnitType type = Ruleset.Unit(unitTypeId);
        if (!type.IsTrainedInEurope)
        {
            return MoveCheck.No($"A {type.ShortName} cannot be trained in Europe.");
        }
        int price = EuropeUnitPrice(player, type);
        if (player.Gold < price)
        {
            return MoveCheck.No($"Not enough gold (need {price}).");
        }
        return MoveCheck.Yes(price);
    }

    /// <summary>
    /// Trains a specialist (a priced, skill &gt; 0 unit — expert farmer, master carpenter…) in <paramref name="player"/>'s
    /// Europe for gold; it appears docked there as a person, ready to board a ship (FreeCol <c>Europe.train</c> /
    /// <c>trainAIUnitInEurope</c>). Classic specialists carry a flat price (no per-type escalation), so unlike
    /// <see cref="BuyUnit(Player, string)"/>'s artillery there is no price ratchet. The trained unit belongs to
    /// <paramref name="player"/> (a foreign power its own id), never the human.
    /// </summary>
    /// <returns>The trained specialist, docked in Europe.</returns>
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckTrain"/>.</exception>
    internal Unit TrainUnit(Player player, string unitTypeId)
    {
        MoveCheck check = CheckTrain(player, unitTypeId);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }
        player.Gold -= check.Cost;
        return CreateEuropeRecruit(player, unitTypeId); // a docked person, owner-stamped, never on the map
    }

    /// <summary>Goods that pack into one cargo slot (FreeCol <c>GoodsContainer.CARGO_SIZE</c>).</summary>
    private const int CargoSlotSize = 100;

    /// <summary>Hold slots a goods amount occupies (each goods type packs in 100s, rounded up).</summary>
    private static int SlotsFor(int amount) => (amount + CargoSlotSize - 1) / CargoSlotSize;

    /// <summary>Extra slots needed to add <paramref name="amount"/> more of a goods type already partly aboard.</summary>
    private static int ExtraGoodsSlots(Unit ship, string goodsId, int amount) =>
        SlotsFor(ship.CargoOf(goodsId) + amount) - SlotsFor(ship.CargoOf(goodsId));

    /// <summary>A ship's total cargo capacity in hold slots (FreeCol <c>getCargoCapacity</c> = unit <c>space</c>).</summary>
    public int CargoCapacity(Unit ship) => ship.Type.Space;

    /// <summary>Hold slots a ship is using — goods (packed in 100s) plus carried units.</summary>
    public int CargoSlotsUsed(Unit ship) =>
        ship.Cargo.Sum(kv => SlotsFor(kv.Value))
        + _units.Where(u => u.CarrierId == ship.Id).Sum(u => u.Type.CarrySlots);

    /// <summary>Hold slots a ship's <em>goods</em> occupy (FreeCol <c>getGoodsSpaceTaken</c>) — excludes passengers. The naval cargo combat penalty applies to goods only.</summary>
    public int GoodsSlotsUsed(Unit ship) => ship.Cargo.Sum(kv => SlotsFor(kv.Value));

    /// <summary>Hold slots still free on a ship (FreeCol <c>getSpaceLeft</c>).</summary>
    public int CargoSlotsFree(Unit ship) => CargoCapacity(ship) - CargoSlotsUsed(ship);

    /// <summary>The units a ship is carrying as passengers.</summary>
    public IEnumerable<Unit> Passengers(Unit ship) => _units.Where(u => u.CarrierId == ship.Id);

    private Unit? UnitById(int id) => _units.FirstOrDefault(u => u.Id == id);

    /// <summary>Keeps a carrier's passengers at the carrier's location and tile.</summary>
    private void SyncPassengers(Unit carrier)
    {
        foreach (Unit passenger in _units.Where(u => u.CarrierId == carrier.Id))
        {
            passenger.Location = carrier.Location;
            passenger.Position = carrier.Position;
        }
    }

    /// <summary>
    /// Whether <paramref name="unit"/> may board <paramref name="ship"/> now — they must be
    /// together (both in Europe, or the unit next to the ship on the map) and the ship must have room.
    /// </summary>
    public MoveCheck CheckBoard(Unit unit, Unit ship)
    {
        if (!ship.Type.IsCarrier)
        {
            return MoveCheck.No($"A {ship.Type.ShortName} cannot carry units.");
        }
        if (unit.Type.IsNaval)
        {
            return MoveCheck.No("A ship cannot be carried.");
        }
        if (unit.IsAboard)
        {
            return MoveCheck.No("The unit is already aboard a ship.");
        }
        if (ship.IsUnderRepair)
        {
            return MoveCheck.No("The ship is under repair and cannot be boarded."); // FreeCol: not ready to trade
        }
        bool together =
            (unit.Location == UnitLocation.InEurope && ship.Location == UnitLocation.InEurope)
            || (unit.IsOnMap && ship.IsOnMap
                && (unit.Position == ship.Position || unit.Position.IsAdjacentTo(ship.Position)));
        if (!together)
        {
            return MoveCheck.No("The unit must be with the ship in Europe, or next to it on the map.");
        }
        if (CargoSlotsFree(ship) < unit.Type.CarrySlots)
        {
            return MoveCheck.No("The ship has no room.");
        }
        return MoveCheck.Yes(0);
    }

    /// <summary>Loads a unit aboard a ship as a passenger (it then travels with the ship).</summary>
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckBoard"/>.</exception>
    public void Board(Unit unit, Unit ship)
    {
        MoveCheck check = CheckBoard(unit, ship);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }
        unit.CarrierId = ship.Id;
        unit.Location = ship.Location;
        unit.Position = ship.Position;
        unit.MovementLeft = 0; // boarding ends the unit's turn
    }

    /// <summary>Whether a carried unit may disembark onto <paramref name="target"/> (a land tile next to its ship).</summary>
    public MoveCheck CheckDisembark(Unit unit, Position target)
    {
        if (!unit.IsAboard)
        {
            return MoveCheck.No("The unit is not aboard a ship.");
        }
        Unit? ship = UnitById(unit.CarrierId!.Value);
        if (ship is null || !ship.IsOnMap)
        {
            return MoveCheck.No("The ship must be on the map to put the unit ashore.");
        }
        if (!Map.InBounds(target))
        {
            return MoveCheck.No("Destination is off the map.");
        }
        if (!target.IsAdjacentTo(ship.Position))
        {
            return MoveCheck.No("Disembark onto a tile next to the ship.");
        }
        if (Map.TerrainAt(target).IsWater)
        {
            return MoveCheck.No("Land units disembark onto land.");
        }
        return MoveCheck.Yes(0);
    }

    /// <summary>Puts a carried unit ashore on an adjacent land tile (it ends its turn there).</summary>
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckDisembark"/>.</exception>
    public void Disembark(Unit unit, Position target)
    {
        MoveCheck check = CheckDisembark(unit, target);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }
        unit.CarrierId = null;
        unit.Location = UnitLocation.OnMap;
        unit.Position = target;
        unit.MovementLeft = 0; // disembarking ends the unit's turn
        Reveal(unit);
        TryExploreRumour(unit, target); // an amphibious landing onto a Lost City Rumour investigates it too (FreeCol explores on any move)
    }

    /// <summary>Takes a carried unit off its ship back onto the Europe dock.</summary>
    /// <exception cref="InvalidMoveException">The unit isn't aboard a ship that is in Europe.</exception>
    public void DisembarkToDock(Unit unit)
    {
        if (!unit.IsAboard)
        {
            throw new InvalidMoveException("The unit is not aboard a ship.");
        }
        Unit? ship = UnitById(unit.CarrierId!.Value);
        if (ship is null || ship.Location != UnitLocation.InEurope)
        {
            throw new InvalidMoveException("The ship must be in Europe to put the unit on the dock.");
        }
        unit.CarrierId = null;
        unit.Location = UnitLocation.InEurope;
    }

    // ===== Lost City Rumours — outcome resolution (86d3c9uhj) =================================================
    // Walking a colonial land unit onto a rumour tile investigates it (FreeCol ServerUnit.csMove fires
    // csExploreLostCityRumour when newTile.hasLostCityRumour() && owner.isEuropean()). The type is rolled now
    // from a weighted table (FreeCol LostCityRumour.chooseType) and resolved (csExploreLostCityRumour), then the
    // rumour is consumed (one-shot). Placement is the prior slice (86d3c9uex); see docs/systems/lost-city-rumours.md.

    /// <summary>The Lost City Rumour outcomes resolved so far (a subset of FreeCol's <c>RumourType</c>).
    /// Deferred to their own slices and so absent from the weighted table here: <c>MOUNDS</c> + native-owned
    /// tiles + the strange-mounds prompt (<c>86d3c9umy</c>). <c>BURIAL_GROUND</c> is likewise absent until native
    /// tile ownership wiring lands — with no native-owned rumour tiles the bad side is only the vanishing
    /// expedition, exactly as FreeCol degrades it. (The treasure finds <c>RUINS</c>/<c>CIBOLA</c> now ship — they
    /// spawn a treasure train via the v27 amount.)</summary>
    internal enum LostCityRumourType
    {
        /// <summary>Nothing of note — the rumour is spent for no effect.</summary>
        Nothing,

        /// <summary>The expedition vanishes — the exploring unit is lost.</summary>
        ExpeditionVanishes,

        /// <summary>A tribal chief's gift of gold.</summary>
        TribalChief,

        /// <summary>The unit learns a skill (free colonist / indentured servant / petty criminal → seasoned scout).</summary>
        Learn,

        /// <summary>A band of colonists joins — a free colonist musters on the tile.</summary>
        Colonist,

        /// <summary>A Fountain of Youth — a burst of <c>dx</c> immigrants arrives on the owner's Europe dock.</summary>
        FountainOfYouth,

        /// <summary>Ancient ruins — a modest find: gold if small, otherwise a treasure train.</summary>
        Ruins,

        /// <summary>A city of gold (Cibola) — a large treasure train.</summary>
        Cibola,

        /// <summary>Strange mounds on native-owned land — the explorer may investigate (a re-rolled outcome) or leave them be (decline). Native tiles only.</summary>
        Mounds,

        /// <summary>A desecrated native burial ground — the owning nation's settlements turn hateful (max alarm). Native tiles only.</summary>
        BurialGround,
    }


    /// <summary>
    /// FreeCol's <c>dx = 10 − rumourDifficulty</c> reward-scale, from the difficulty level: classic <b>medium</b> sets
    /// <c>model.option.rumourDifficulty=2</c>, so <c>dx = 8</c>. The raw option lives on
    /// <see cref="Specification.DifficultyOptions.RumourDifficulty"/>; the <c>10 − x</c> transform stays here.
    /// </summary>
    private int RumourDifficultyDx => 10 - Ruleset.Difficulty.RumourDifficulty;

    /// <summary>The unit a COLONIST rumour musters: the only classic unit with <c>model.ability.foundInLostCity</c>.</summary>
    private const string FoundInLostCityUnitTypeId = "model.unit.freeColonist";

    /// <summary>Below this a RUINS find pays straight gold; at or above it spawns a treasure train (FreeCol <c>csExploreLostCityRumour</c> RUINS, the <c>&lt; 500</c> branch).</summary>
    private const int RumourSmallRuinsThreshold = 500;

    /// <summary>
    /// Investigates a Lost City Rumour if <paramref name="unit"/> just stepped onto one: a colonial (non-native)
    /// <b>land</b> unit on a tile that holds a rumour. Draws from the owner's RNG stream (<see cref="RandomFor"/>)
    /// — the human's stream 0, an AI power's own stream — so one player exploring never shifts another's economy
    /// (ADR-009). A naval unit can never stand on a (land) rumour tile, so the gate is belt-and-braces.
    /// </summary>
    private void TryExploreRumour(Unit unit, Position target)
    {
        if (unit.IsNative || unit.Type.IsNaval || !Map.HasRumour(target))
        {
            return; // natives never explore (FreeCol: European only); ships can't reach a land rumour
        }
        if (PlayerById(unit.OwnerId) is not { } owner)
        {
            return;
        }
        if (owner.IsHuman && _pendingMounds is not null)
        {
            return; // the human still owes an investigate/decline answer on an earlier strange-mounds — don't roll another
                    // rumour (or re-roll this one) until it's resolved. AI explorers never set _pendingMounds, so they're unaffected.
        }
        if (ExploreRumour(unit, target, RandomFor(owner)) != LostCityRumourType.Mounds)
        {
            return; // a non-mounds rumour is fully resolved by the peek
        }
        // Strange mounds need an investigate/decline decision. A human is prompted (the rumour waits on the tile);
        // an AI / foreign power has no UI, so it auto-investigates inline on its own stream (keeps the soak headless).
        if (owner.IsHuman)
        {
            _pendingMounds = new PendingMoundsDecision(unit.Id, target);
        }
        else
        {
            InvestigateMounds(unit, target, RandomFor(owner));
        }
    }

    /// <summary>
    /// Rolls and resolves the rumour on <paramref name="target"/> for the exploring <paramref name="unit"/>,
    /// then consumes the rumour (one-shot — FreeCol <c>removeLostCityRumour</c>). Returns the outcome (for tests).
    /// The reward roll draws from the supplied <see cref="IGameRandom"/> (combat is the precedent: per-owner
    /// streams). May remove the unit (an expedition vanishes) or replace it (a learned skill upgrades its type),
    /// so callers must not reuse the reference afterwards.
    /// </summary>
    internal LostCityRumourType ExploreRumour(Unit unit, Position target, IGameRandom random)
    {
        LostCityRumourType outcome = ChooseRumourType(unit, target, random);

        // Resolve-time native gate (FreeCol ServerUnit.csExploreLostCityRumour): MOUNDS / BURIAL_GROUND on a tile
        // the natives don't own degrade to NOTHING. Belt-and-braces — conditional-add already keeps them off the
        // table on non-native tiles, but this matches FreeCol and guards a future gen-time mounds pre-stamp.
        if ((outcome is LostCityRumourType.Mounds or LostCityRumourType.BurialGround) && !Map.IsNativeOwned(target))
        {
            outcome = LostCityRumourType.Nothing;
        }

        // Strange mounds: stop here. The explorer must choose investigate vs decline (the caller prompts the human
        // or auto-investigates an AI) — the rumour is left in place and nothing is resolved on this peek.
        if (outcome == LostCityRumourType.Mounds)
        {
            return LostCityRumourType.Mounds;
        }

        // Capture the explorer's owner before ResolveOutcome — a vanished expedition removes the unit, so the
        // reference must not be read afterwards.
        bool ownerIsHuman = PlayerById(unit.OwnerId) is { IsHuman: true };

        ResolveOutcome(unit, target, outcome, random);
        Map.RemoveRumour(target); // consumed regardless of outcome

        // Surface the resolved outcome to the human as a transient notice (the move handler has no return value to
        // read for a rumour, like the AI-phase combat/raid notices). An AI/foreign explorer has no UI, so its
        // rumour outcomes are never recorded — only the human's land in the player-facing list. The strange-mounds
        // path returned above; its description comes via ResolvePendingMounds. A Fountain of Youth's message is
        // context-aware (generated / queued for the recruit-choice / no Europe), so it owns its own description.
        if (ownerIsHuman)
        {
            string message = outcome == LostCityRumourType.FountainOfYouth
                ? DescribeFountainOutcome(_lastFountainResult)
                : DescribeMoundsOutcome(outcome);
            _rumourNotices.Add(new RumourNotice(message, target));
        }
        return outcome;
    }

    /// <summary>A one-line player-facing description of how a Fountain-of-Youth burst was handled (the recruit-choice / no-Europe variants of the generic FoY line).</summary>
    private static string DescribeFountainOutcome(FountainResult result) => result switch
    {
        FountainResult.QueuedForChoice => "A Fountain of Youth! Choose the settlers who flock to your docks.",
        FountainResult.NoEurope => "A Fountain of Youth — but with no Europe to receive them, the settlers cannot come.",
        _ => "A Fountain of Youth! Settlers flock to your docks.",
    };

    /// <summary>
    /// Applies a resolved rumour outcome's effect (shared by the direct <see cref="ExploreRumour"/> and the
    /// strange-mounds <see cref="InvestigateMounds"/>). Does <b>not</b> remove the rumour (the caller does) and never
    /// receives <see cref="LostCityRumourType.Mounds"/> (that is the prompt sentinel, never a resolved effect).
    /// </summary>
    private void ResolveOutcome(Unit unit, Position target, LostCityRumourType outcome, IGameRandom random)
    {
        switch (outcome)
        {
            case LostCityRumourType.ExpeditionVanishes:
                _units.Remove(unit); // the expedition is lost
                break;
            case LostCityRumourType.TribalChief:
                // FreeCol: randomInt(0, dx·10) + dx·5 → medium dx=8 gives 40–119 gold to the owner.
                int gift = random.Next(RumourDifficultyDx * 10) + RumourDifficultyDx * 5;
                if (PlayerById(unit.OwnerId) is { } owner)
                {
                    owner.Gold += gift;
                }
                break;
            case LostCityRumourType.Learn:
                // Only learnable units reach LEARN (gated in ChooseRumourType); the change is guaranteed present.
                if (Ruleset.GetUnitChange(UnitChangeTypeIds.LostCity, unit.Type.Id) is { } change)
                {
                    UpgradeUnitType(unit, change.To); // free colonist / indentured servant / petty criminal → seasoned scout
                }
                break;
            case LostCityRumourType.Colonist:
                SpawnUnit(Ruleset.Unit(FoundInLostCityUnitTypeId), target, unit.OwnerId); // a found colonist musters on the tile
                break;
            case LostCityRumourType.FountainOfYouth:
                // A burst of dx immigrants: generated directly (AI / non-Brewster) or routed to the human's
                // select-recruit choice. The result picks the player-facing message in ExploreRumour.
                _lastFountainResult = GenerateFountainRecruits(unit.OwnerId, random);
                break;
            case LostCityRumourType.Ruins:
                // FreeCol: rand(0, dx·2)·300 + 50 → a small find (< 500) is gold; a larger one becomes a treasure train.
                int ruins = (random.Next(RumourDifficultyDx * 2) * 300) + 50;
                if (ruins < RumourSmallRuinsThreshold)
                {
                    if (PlayerById(unit.OwnerId) is { } ruinsOwner)
                    {
                        ruinsOwner.Gold += ruins;
                    }
                }
                else
                {
                    SpawnTreasureTrain(target, unit.OwnerId, ruins);
                }
                break;
            case LostCityRumourType.Cibola:
                // FreeCol: a city of gold → rand(0, dx·600) + dx·300 as a treasure train (medium dx=8 → 2400–7199).
                SpawnTreasureTrain(target, unit.OwnerId, random.Next(RumourDifficultyDx * 600) + (RumourDifficultyDx * 300));
                break;
            case LostCityRumourType.BurialGround:
                ApplyBurialGround(target); // the owning nation turns hateful
                break;
            case LostCityRumourType.Nothing:
            default:
                break; // no effect (the player-facing message is presentation)
        }
    }

    /// <summary>
    /// Investigates a strange-mounds rumour (FreeCol <c>csExploreLostCityRumour</c>'s mounds degradation loop):
    /// re-rolls the rumour table until it lands an outcome mounds can yield, then resolves it and consumes the
    /// rumour. The exploring unit's owner stream supplies the draws (per-owner determinism, ADR-009).
    /// </summary>
    internal LostCityRumourType InvestigateMounds(Unit unit, Position target, IGameRandom random)
    {
        if (!Map.HasRumour(target))
        {
            return LostCityRumourType.Nothing; // already consumed (e.g. by another unit before the decision was answered) — no reward, no draw
        }
        LostCityRumourType outcome = DegradeMounds(unit, target, random);
        ResolveOutcome(unit, target, outcome, random);
        Map.RemoveRumour(target);
        return outcome;
    }

    /// <summary>
    /// The FreeCol mounds degradation loop (<c>ServerUnit</c> lines 474–508): re-rolls <see cref="ChooseRumourType"/>
    /// until an acceptable outcome appears. NOTHING is accepted only the <b>second</b> time it is rolled; the
    /// vanishing expedition and the tribal chief are accepted at once; RUINS is accepted and draws one extra
    /// "ruins+burial" value (a faithful quirk — the draw never changes the outcome, it stays RUINS); a burial ground
    /// is accepted on native land (always true inside mounds); FoY / LEARN / colonist / mounds / Cibola are re-rolled.
    /// Uncapped, exactly as FreeCol (it terminates quickly — most outcomes accept immediately).
    /// </summary>
    private LostCityRumourType DegradeMounds(Unit unit, Position target, IGameRandom random)
    {
        bool sawNothing = false;
        while (true)
        {
            LostCityRumourType t = ChooseRumourType(unit, target, random);
            switch (t)
            {
                case LostCityRumourType.Nothing:
                    if (sawNothing)
                    {
                        return LostCityRumourType.Nothing; // accept the second NOTHING
                    }
                    sawNothing = true; // the first NOTHING re-rolls
                    break;
                case LostCityRumourType.ExpeditionVanishes:
                case LostCityRumourType.TribalChief:
                    return t;
                case LostCityRumourType.Ruins:
                    // FreeCol draws a "Ruins+Burial" value here; whether it passes or falls through, the outcome
                    // still resolves as RUINS (the fall-through sets only `done`, never the type). We consume the
                    // draw for byte-fidelity and return RUINS regardless.
                    random.Next(100);
                    return LostCityRumourType.Ruins;
                case LostCityRumourType.BurialGround:
                    if (Map.IsNativeOwned(target))
                    {
                        return LostCityRumourType.BurialGround; // always reached inside mounds (native-owned)
                    }
                    break; // (unreachable here) re-roll
                default:
                    break; // FoY / Learn / Colonist / Mounds / Cibola → re-roll
            }
        }
    }

    /// <summary>
    /// Declines to investigate a strange-mounds rumour (FreeCol <c>InGameController.declineMounds</c>): the rumour is
    /// simply removed, with no effect and <b>no RNG draw</b>. (Unlike the generic explore confirm, declining mounds
    /// consumes the rumour — you don't get a second look.)
    /// </summary>
    internal void DeclineMounds(Position target) => Map.RemoveRumour(target);

    /// <summary>
    /// A desecrated burial ground (FreeCol <c>csNativeBurialGround</c>): the natives who own the tile turn hateful.
    /// FreeCol sets the nation's tension to HATEFUL and forces war; we have no native-vs-colonial stance model, so we
    /// raise every settlement of the owning nation to maximum alarm — the nation-wide hostility analogue used by the
    /// other land-grievance acts (cf. <c>ClaimLandByStealing</c>). No gold or unit change.
    /// </summary>
    private void ApplyBurialGround(Position target)
    {
        if (Map.NativeOwnerOf(target) is not { } nation)
        {
            return; // gated upstream to native-owned tiles; guard is belt-and-braces
        }
        foreach (NativeSettlement settlement in _nativeSettlements.Where(s => s.NationTypeId == nation))
        {
            ChangeNativeAlarm(settlement, NativeSettlement.MaxAlarm); // clamps to max (hateful)
        }
    }

    /// <summary>Musters a treasure train on <paramref name="target"/> carrying <paramref name="amount"/> gold, owned by <paramref name="ownerId"/> (FreeCol spawns a treasure train for a rich plunder/find — see [treasure-train.md]).</summary>
    private void SpawnTreasureTrain(Position target, int ownerId, int amount) =>
        SpawnUnit(Ruleset.Unit(TreasureTrainUnitTypeId), target, ownerId).SetTreasureAmount(amount);

    /// <summary>How a Fountain-of-Youth burst was handled — drives the player-facing message (<see cref="ExploreRumour"/>).</summary>
    internal enum FountainResult
    {
        /// <summary>The <c>dx</c> immigrants were generated directly onto the owner's Europe dock (the AI / non-choosing path).</summary>
        Generated,

        /// <summary>A select-recruit human: the <c>dx</c> picks were armed as a <see cref="PendingEmigration"/> choice (the human picks each).</summary>
        QueuedForChoice,

        /// <summary>The owner has no Europe to receive them (a rebel/independent player, or a ruleset with no recruitable units) — nothing happens (FreeCol's <c>noEurope</c> gate).</summary>
        NoEurope,
    }

    /// <summary>
    /// A Fountain of Youth: a burst of <see cref="RumourDifficultyDx"/> fresh immigrants for the owner's Europe
    /// (FreeCol <c>ServerUnit.csExploreLostCityRumour</c> FOUNTAIN_OF_YOUTH).
    /// <list type="bullet">
    /// <item><b>No Europe</b> (a rebel/independent player whose dock is closed, or a ruleset with no recruitable
    /// units) → nothing happens, returning <see cref="FountainResult.NoEurope"/> (FreeCol's <c>europe == null</c>
    /// → <c>noEurope</c> message gate).</item>
    /// <item><b>A select-recruit human</b> (William Brewster, <c>model.ability.selectRecruit</c>) → the <c>dx</c>
    /// picks are armed as a Fountain-of-Youth <see cref="PendingEmigration"/> choice so the human hand-picks each
    /// immigrant from the dock (FreeCol <c>setRemainingEmigrants(dx)</c> + the <c>selectRecruit</c> prompts); no
    /// recruits are generated here and <b>no RNG is drawn off <paramref name="random"/></b> — the per-pick draws
    /// run later, on the player's own stream, inside <see cref="ChooseEmigrant"/>. (If a normal Brewster emigrant
    /// is already pending — a rare same-instant overlap — falls back to the direct path so neither is lost.)</item>
    /// <item><b>Everyone else</b> (the AI, foreign powers, and a human <em>without</em> Brewster) → the <c>dx</c>
    /// immigrants are generated directly, each an independent weighted draw
    /// (<see cref="DrawRecruitType(Player, IGameRandom)"/> → <see cref="CreateEuropeRecruit"/> off
    /// <paramref name="random"/>, the explorer's owner stream), exactly as before — so every non-choosing player's
    /// RNG path is byte-identical (ADR-009).</item>
    /// </list>
    /// The immigrants arrive as units <em>in Europe</em> (not as the three dock candidates), so the owner still
    /// ships them over.
    /// </summary>
    private FountainResult GenerateFountainRecruits(int ownerId, IGameRandom random)
    {
        if (PlayerById(ownerId) is not { } owner
            || owner.RecruitDock.Count == 0
            || !Ruleset.UnitTypes.Any(t => IsRecruitable(owner, t)))
        {
            return FountainResult.NoEurope; // no Europe (closed dock / minimal ruleset) — the FreeCol noEurope gate
        }

        if (owner.IsHuman && HasAbilityFor(owner, SelectRecruitAbility) && _pendingEmigration is null)
        {
            // Route the burst through the shipped select-recruit seam: arm dx FoY picks the human resolves one by one
            // (no immigration consumed; no draw here — ChooseEmigrant draws each refill on the player's own stream).
            _pendingEmigration = new PendingEmigrationChoice(
                owner.PlayerId, owner.RecruitDock.ToList(), IsFountainOfYouth: true, Remaining: RumourDifficultyDx);
            return FountainResult.QueuedForChoice;
        }

        for (int i = 0; i < RumourDifficultyDx; i++)
        {
            CreateEuropeRecruit(owner, DrawRecruitType(owner, random));
        }
        return FountainResult.Generated;
    }

    /// <summary>
    /// Picks a rumour type by the FreeCol <c>LostCityRumour.chooseType</c> weighted split (good / bad / neutral).
    /// Good outcomes are weighted ×<c>percentGood</c>: FOUNTAIN_OF_YOUTH (2, colonial explorer); then a learnable
    /// unit LEARN (30) / TRIBAL_CHIEF (30) / COLONIST (20), a non-learnable one TRIBAL_CHIEF (50) / COLONIST (30);
    /// strange MOUNDS (8, native land only); then the treasure finds RUINS (6) and CIBOLA (4). The bad sub-list —
    /// BURIAL_GROUND (25, native land only) and EXPEDITION_VANISHES (75, unless an expert scout) — is normalised to
    /// 100; NOTHING takes the neutral remainder ×100. The difficulty's base good/bad percentages
    /// (<see cref="Specification.DifficultyOptions.RumourGoodPercent"/> 48 / <see cref="Specification.DifficultyOptions.RumourBadPercent"/> 23)
    /// are tilted by three modifiers, exactly as FreeCol: an <b>expert scout</b>
    /// never vanishes (and if that removes all bad, the bad chance drops to 0); <b>Hernando de Soto</b>
    /// (<c>rumoursAlwaysPositive</c>) forces 100% good; otherwise a unit's <see cref="UnitType.ExploreLostCityRumourBonus"/>
    /// scales good ×<c>(1+bonus/100)</c> and bad ÷ it (seasoned scout +10%).
    /// </summary>
    private LostCityRumourType ChooseRumourType(Unit unit, Position target, IGameRandom random)
    {
        bool canLearn = Ruleset.GetUnitChange(UnitChangeTypeIds.LostCity, unit.Type.Id) is not null;
        bool allowBurial = Map.IsNativeOwned(target); // burial ground (and strange mounds) need native-owned land
        bool allowVanish = !unit.Type.ExpertScout;    // an expert scout (seasoned scout) never vanishes

        int percentBad = Ruleset.Difficulty.RumourBadPercent;
        int percentGood = Ruleset.Difficulty.RumourGoodPercent;
        if (!allowBurial && !allowVanish)
        {
            percentBad = 0; // no bad outcome is possible — an expert scout off native land (FreeCol degenerate case)
        }
        else if (AbilityForUnit(unit, RumoursAlwaysPositiveAbility))
        {
            percentBad = 0; // Hernando de Soto: every rumour is good
            percentGood = 100;
        }
        else
        {
            // The unit's own exploration bonus (seasoned scout +10%): good ×mod, bad ÷mod (Java Math.round = floor(x+0.5)).
            double mod = 1.0 + unit.Type.ExploreLostCityRumourBonus / 100.0;
            percentBad = (int)Math.Floor(percentBad / mod + 0.5);
            percentGood = (int)Math.Floor(percentGood * mod + 0.5);
        }
        int neutral = Math.Max(0, 100 - percentBad - percentGood);

        var choices = new List<(LostCityRumourType Type, int Weight)>();
        if (percentGood > 0)
        {
            // Fountain of Youth (weight 2, listed first to mirror FreeCol's chooseType). FreeCol allowFoY is
            // "owner is COLONIAL"; today every explorer here is colonial (natives are excluded upstream, and the
            // only player types are colonial + native), so it is unconditional. When the REF lands (P6) — European
            // but not colonial — this must gate on a colonial owner (an IsColonialPlayer(unit.OwnerId) check).
            choices.Add((LostCityRumourType.FountainOfYouth, 2 * percentGood));
            if (canLearn)
            {
                choices.Add((LostCityRumourType.Learn, 30 * percentGood));
                choices.Add((LostCityRumourType.TribalChief, 30 * percentGood));
                choices.Add((LostCityRumourType.Colonist, 20 * percentGood));
            }
            else
            {
                choices.Add((LostCityRumourType.TribalChief, 50 * percentGood));
                choices.Add((LostCityRumourType.Colonist, 30 * percentGood));
            }
            // The treasure finds are available to any explorer (FreeCol adds them outside the learn split): ancient
            // RUINS (6) and a city of gold CIBOLA (4).
            choices.Add((LostCityRumourType.Ruins, 6 * percentGood));
            choices.Add((LostCityRumourType.Cibola, 4 * percentGood));
            // Strange MOUNDS (FreeCol weight 8): added UNCONDITIONALLY (FreeCol chooseType has no native-owned gate
            // here). Off native-owned land a MOUNDS roll degrades to NOTHING at resolve (ExploreRumour, with no extra
            // RNG draw) — so its 8·good weight stays in the denominator and the full distribution matches FreeCol
            // exactly, rather than inflating the other outcomes by omitting it.
            choices.Add((LostCityRumourType.Mounds, 8 * percentGood));
        }
        if (percentBad > 0)
        {
            // The bad sub-list, normalised to a total of 100 (FreeCol RandomChoice.normalize): burial ground only on
            // native land, the vanishing expedition unless an expert scout. With both, 25 / 75; with one, that one
            // takes the whole 100 (so off native land a regular unit's bad is the lone vanishing expedition at 100,
            // and an expert scout on native land can only ever hit the burial ground).
            var bad = new List<(LostCityRumourType Type, int Weight)>();
            if (allowBurial)
            {
                bad.Add((LostCityRumourType.BurialGround, 25 * percentBad));
            }
            if (allowVanish)
            {
                bad.Add((LostCityRumourType.ExpeditionVanishes, 75 * percentBad));
            }
            choices.AddRange(NormalizeTo100(bad));
        }
        if (neutral > 0)
        {
            choices.Add((LostCityRumourType.Nothing, 100 * neutral));
        }

        return WeightedPick(choices, random);
    }

    /// <summary>
    /// Rescales a weighted sub-list so its weights sum to 100, rounding each element (FreeCol
    /// <c>RandomChoice.normalize(list, 100)</c>). Blends the bad-outcome sub-list (burial ground + vanishing
    /// expedition) into the main rumour table at the same 100-weight footprint the flat vanishing expedition occupies
    /// off native land — so adding the burial ground never shifts the bad total, only the good side's mounds does.
    /// </summary>
    private static List<(LostCityRumourType Type, int Weight)> NormalizeTo100(
        IReadOnlyList<(LostCityRumourType Type, int Weight)> sub)
    {
        int subtotal = sub.Sum(c => c.Weight);
        return subtotal <= 0
            ? [.. sub]
            : [.. sub.Select(c => (c.Type, (int)Math.Round(100.0 * c.Weight / subtotal)))];
    }

    /// <summary>
    /// Cumulative weighted draw (FreeCol <c>RandomChoice.getWeightedRandom</c>): a single choice is returned with
    /// no RNG draw; otherwise <c>random.Next(total)</c> selects proportionally. An empty/zero-weight list (no
    /// possible outcome) falls back to NOTHING.
    /// </summary>
    private static LostCityRumourType WeightedPick(
        IReadOnlyList<(LostCityRumourType Type, int Weight)> choices, IGameRandom random)
    {
        int total = choices.Sum(c => c.Weight);
        if (total <= 0)
        {
            return LostCityRumourType.Nothing;
        }
        if (choices.Count == 1)
        {
            return choices[0].Type;
        }
        int roll = random.Next(total);
        int cumulative = 0;
        foreach ((LostCityRumourType type, int weight) in choices)
        {
            cumulative += weight;
            if (roll < cumulative)
            {
                return type;
            }
        }
        return choices[^1].Type;
    }

    /// <summary>Advances sailing units; arrivals dock in Europe or re-enter the map.</summary>
    private void AdvanceSailing()
    {
        foreach (Unit unit in _units.Where(u =>
            u.Location is UnitLocation.SailingToEurope or UnitLocation.SailingToNewWorld))
        {
            if (--unit.SailTurnsRemaining > 0)
            {
                continue;
            }
            if (unit.Location == UnitLocation.SailingToEurope)
            {
                unit.Location = UnitLocation.InEurope;
            }
            else
            {
                unit.Location = UnitLocation.OnMap; // re-enters at its departure high-seas tile
                RevealForOwner(unit); // the arriving ship lifts its own owner's fog
            }
            SyncPassengers(unit); // carried colonists arrive with the ship
        }
    }

    /// <summary>Counts down repair on damaged ships; one finishing this turn returns to service (in Europe, or on the map at its home drydock colony).</summary>
    private void AdvanceRepairs()
    {
        foreach (Unit unit in _units.Where(u => u.RepairTurnsRemaining > 0))
        {
            unit.RepairTurnsRemaining--;
        }
    }

    /// <summary>
    /// A resolved construction target — a building <em>or</em> a buildable land unit — exposing the facts the
    /// build path needs (FreeCol <c>BuildableType</c>). A queued id resolves to one of these via
    /// <see cref="ResolveBuildable"/>; buildings and units then share one gate (<see cref="BuildRefusal"/>).
    /// </summary>
    private sealed record Buildable(
        string Id,
        string ShortName,
        IReadOnlyList<GoodsOutput> BuildCost,
        int RequiredPopulation,
        IReadOnlyDictionary<string, bool> RequiredAbilities,
        bool IsUnit,
        string? UpgradesFrom,
        UnitType? Unit);

    /// <summary>
    /// Resolves a construction id to a building or a buildable unit (land <em>or</em> naval); <c>null</c> when the
    /// id is neither (unknown or a non-buildable type). A ship is buildable only where a shipyard grants the
    /// water-scoped build ability (<see cref="ColonyCanBuildUnit"/>) and launches into a water berth beside the
    /// colony on completion (<see cref="RunConstruction"/>).
    /// </summary>
    private Buildable? ResolveBuildable(string id)
    {
        if (Ruleset.FindBuilding(id) is { } b)
        {
            return new Buildable(b.Id, b.ShortName, b.BuildCost, b.RequiredPopulation, b.RequiredAbilitiesOrEmpty, IsUnit: false, b.UpgradesFrom, Unit: null);
        }
        if (Ruleset.FindBuildableUnit(id) is { } u)
        {
            return new Buildable(u.Id, u.ShortName, u.BuildCostOrEmpty, u.RequiredPopulation, u.RequiredAbilitiesOrEmpty, IsUnit: true, UpgradesFrom: null, Unit: u);
        }
        return null;
    }

    /// <summary>
    /// The reason a colony cannot start <paramref name="target"/> (FreeCol <c>Colony.getNoBuildReason</c>), or
    /// <c>null</c> when it may. Buildings are one-per-colony and may be an upgrade; a unit can be built any number
    /// of times (so the already-built/already-queued and upgrade gates apply to buildings only). <paramref name="queueing"/>
    /// also accepts an upgrade predecessor that sits earlier in the queue (not yet standing).
    /// </summary>
    private string? BuildRefusal(Colony colony, Buildable target, bool queueing)
    {
        if (target.BuildCost.Count == 0)
        {
            return $"The {target.ShortName} cannot be constructed.";
        }
        if (!target.IsUnit)
        {
            if (colony.HasBuilding(target.Id))
            {
                return $"The colony already has a {target.ShortName}.";
            }
            if (queueing && colony.BuildQueue.Contains(target.Id))
            {
                return $"The {target.ShortName} is already queued.";
            }
            if (target.UpgradesFrom is { } parent
                && !colony.HasBuilding(parent) && !(queueing && colony.BuildQueue.Contains(parent)))
            {
                return $"A {target.ShortName} upgrades a building the colony has not built or queued.";
            }
        }
        if (colony.Population < target.RequiredPopulation)
        {
            return $"The {target.ShortName} needs a population of {target.RequiredPopulation}.";
        }
        if (RequiredAbilityRefusal(colony, target.ShortName, target.RequiredAbilities) is { } abilityReason)
        {
            return abilityReason;
        }
        if (target.IsUnit && target.Unit is { } unit)
        {
            // LIMIT_EXCEEDED then MISSING_BUILD_ABILITY (FreeCol getNoBuildReason order, after MISSING_ABILITY).
            if (!UnitBuildLimitOk(colony, unit))
            {
                return $"You cannot build more {target.ShortName}s — one per colony.";
            }
            if (!ColonyCanBuildUnit(colony, unit))
            {
                BuildingType? enabler = Ruleset.BuildingTypes.FirstOrDefault(b => b.BuildableUnitTypeIdsOrEmpty.Contains(unit.Id));
                return enabler is not null
                    ? $"A {target.ShortName} needs a {enabler.ShortName} in the colony."
                    : $"The {target.ShortName} cannot be built in this colony.";
            }
        }
        return null;
    }

    /// <summary>
    /// True when one of the colony's buildings grants the build ability scoped to <paramref name="unit"/> (FreeCol
    /// <c>UnitType.canBeBuiltInColony</c>): carpenter's house → wagon train (a free base building, so every colony
    /// qualifies), armory → artillery, shipyard → any naval unit. A unit with no enabling building is MISSING_BUILD_ABILITY.
    /// A <b>ship</b> additionally needs the colony to be <b>coastal</b> — it launches into an adjacent water berth, and
    /// FreeCol gates the shipyard itself on <c>hasPort</c>, so a landlocked shipyard can never build one.
    /// </summary>
    private bool ColonyCanBuildUnit(Colony colony, UnitType unit)
    {
        if (unit.IsNaval)
        {
            return IsColonyCoastal(colony) && colony.Buildings.Select(Ruleset.Building).Any(b => b.BuildsNavalUnits);
        }
        return colony.Buildings.Select(Ruleset.Building).Any(b => b.BuildableUnitTypeIdsOrEmpty.Contains(unit.Id));
    }

    /// <summary>
    /// True when building one more <paramref name="unit"/> stays within its spec <c>&lt;limit&gt;</c> (FreeCol
    /// <c>Limit.evaluate</c>): the classic wagon-train cap is <c>units lt settlements</c> at player scope — the
    /// owner's wagon trains must stay fewer than their colonies (at most one per colony). Unlimited unit / a limit
    /// over operands we do not evaluate → always allowed.
    /// </summary>
    private bool UnitBuildLimitOk(Colony colony, UnitType unit)
    {
        if (unit.BuildLimit is not { } limit || PlayerById(colony.OwnerId) is not { } owner)
        {
            return true;
        }
        if (limit.LeftOperand != "units" || limit.RightOperand != "settlements")
        {
            return true; // only the classic units-vs-settlements form is evaluated; anything else is treated as no cap
        }
        int owned = _units.Count(u => u.OwnerId == owner.PlayerId && u.OwnerNationId is null && u.Type.Id == unit.Id);
        int colonies = ColoniesOf(owner).Count();
        return limit.Operator switch
        {
            "lt" => owned < colonies,
            "le" => owned <= colonies,
            "gt" => owned > colonies,
            "ge" => owned >= colonies,
            "eq" => owned == colonies,
            _ => true,
        };
    }

    /// <summary>
    /// A reason the colony fails one of the buildable's <c>required-ability</c> gates (factory tier → Adam
    /// Smith's <c>buildFactory</c>, custom house → Stuyvesant's <c>buildCustomHouse</c>, docks/drydock/shipyard →
    /// a coastal colony's <c>hasPort</c>); <c>null</c> when met (FreeCol MISSING_ABILITY/COASTAL).
    /// </summary>
    private string? RequiredAbilityRefusal(Colony colony, string shortName, IReadOnlyDictionary<string, bool> requiredAbilities)
    {
        foreach ((string abilityId, bool required) in requiredAbilities)
        {
            if (ColonyHasAbility(colony, abilityId) != required)
            {
                if (abilityId == HasPortAbility)
                {
                    return $"A {shortName} can only be built in a coastal colony.";
                }
                FoundingFather? father = Ruleset.FoundingFathers
                    .FirstOrDefault(f => f.Abilities.Any(a => a.Id == abilityId && a.Value));
                return father is not null
                    ? $"The {shortName} needs the {father.ShortName} Founding Father."
                    : $"The {shortName} requires an ability the colony lacks ({abilityId}).";
            }
        }
        return null;
    }

    /// <summary>Whether the colony may start constructing a building or buildable unit right now.</summary>
    public MoveCheck CheckSetBuild(Colony colony, string buildableId) =>
        ResolveBuildable(buildableId) is { } target && BuildRefusal(colony, target, queueing: false) is var reason
            ? (reason is null ? MoveCheck.Yes(0) : MoveCheck.No(reason))
            : MoveCheck.No($"'{buildableId}' cannot be constructed.");

    /// <summary>Sets the colony's construction to a single building or unit (null stops construction), replacing the queue.</summary>
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckSetBuild"/>.</exception>
    public void SetBuild(Colony colony, string? buildableId)
    {
        if (buildableId is not null)
        {
            MoveCheck check = CheckSetBuild(colony, buildableId);
            if (!check.Allowed)
            {
                throw new InvalidMoveException(check.Reason!);
            }
        }
        colony.SetBuildQueue(buildableId is null ? [] : [buildableId]);
    }

    /// <summary>
    /// Whether a building or unit may be appended to the colony's construction queue. Same gate as
    /// <see cref="CheckSetBuild"/>, but an upgrade predecessor may sit earlier in the queue; a building already
    /// built or queued is refused, while a unit may be queued any number of times (build three artillery).
    /// </summary>
    public MoveCheck CheckEnqueueBuild(Colony colony, string buildableId) =>
        ResolveBuildable(buildableId) is { } target && BuildRefusal(colony, target, queueing: true) is var reason
            ? (reason is null ? MoveCheck.Yes(0) : MoveCheck.No(reason))
            : MoveCheck.No($"'{buildableId}' cannot be constructed.");

    /// <summary>Appends a building or unit to the colony's construction queue (built after the items already queued).</summary>
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckEnqueueBuild"/>.</exception>
    public void EnqueueBuild(Colony colony, string buildableId)
    {
        MoveCheck check = CheckEnqueueBuild(colony, buildableId);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }
        colony.EnqueueBuild(buildableId);
    }

    /// <summary>
    /// Removes the queued buildable at <paramref name="index"/> from the colony's construction queue (a no-op if out
    /// of range). Reordering/removal is unrestricted: any resulting bad order self-heals at build time — a front item
    /// whose upgrade predecessor is gone is skipped without spending (<see cref="RunConstruction"/>).
    /// </summary>
    public void RemoveFromBuildQueue(Colony colony, int index) => colony.RemoveFromBuildQueue(index);

    /// <summary>
    /// Moves the queued buildable at <paramref name="index"/> by <paramref name="delta"/> places (−1 = up/earlier,
    /// +1 = down/later); a no-op if either end is out of range. As with removal, a resulting out-of-order upgrade is
    /// simply skipped when reached (<see cref="RunConstruction"/>), not rejected here.
    /// </summary>
    public void MoveBuildQueueItem(Colony colony, int index, int delta) => colony.MoveBuildQueueItem(index, delta);

    /// <summary>
    /// Display facts for a queued construction id — a building <em>or</em> a buildable unit — for the colony build UI;
    /// <c>null</c> if the id is not a buildable type. Lets the UI render a queue item (name/cost/kind) without knowing
    /// whether it is a building or a unit.
    /// </summary>
    public BuildableInfo? DescribeBuildable(string id) =>
        ResolveBuildable(id) is { } t ? new BuildableInfo(t.Id, t.ShortName, t.IsUnit, t.BuildCost) : null;

    /// <summary>A construction target's display facts (building or unit) — see <see cref="DescribeBuildable"/>.</summary>
    public sealed record BuildableInfo(string Id, string ShortName, bool IsUnit, IReadOnlyList<GoodsOutput> BuildCost);

    /// <summary>Building types the colony could start constructing right now.</summary>
    public IEnumerable<BuildingType> Buildables(Colony colony) =>
        Ruleset.BuildingTypes.Where(b => CheckSetBuild(colony, b.Id).Allowed);

    /// <summary>Unit types the colony could start constructing right now (artillery, wagon train, and — at a shipyard colony — ships).</summary>
    public IEnumerable<UnitType> BuildableUnits(Colony colony) =>
        Ruleset.BuildableUnitTypes.Where(u => CheckSetBuild(colony, u.Id).Allowed);

    /// <summary>
    /// Advances the colony's construction queue (FreeCol <c>csNextBuildable</c> + completion): any front item that
    /// is no longer buildable — an unknown id, a building already built or whose upgrade predecessor is gone, a
    /// unit over its limit or missing its build-ability — is skipped without spending materials (same gate as
    /// <see cref="CheckSetBuild"/>); then the front item is completed if the stores cover its cost, one per turn.
    /// A completed <b>unit</b> musters on the colony tile under the colony's owner; a building is added (or
    /// replaces its predecessor). The queue then advances — except a <b>lone queued unit repeats</b> (FreeCol
    /// <c>CompletionAction.REMOVE_EXCEPT_LAST</c>): the colony keeps churning it out until stopped or capped.
    /// </summary>
    private void RunConstruction(Colony colony)
    {
        while (colony.CurrentBuild is { } buildableId)
        {
            if (ResolveBuildable(buildableId) is not { } target)
            {
                colony.AdvanceBuild(); // an unresolvable id can never build — drop it and try the next
                continue;
            }
            if (target.BuildCost.Any(c => colony.StoreOf(Ruleset.StorageIdOf(c.GoodsId)) < c.Amount))
            {
                return; // not yet affordable — keep saving (FreeCol re-validates the front item only once its goods are in store)
            }
            if (BuildRefusal(colony, target, queueing: false) is not null)
            {
                colony.AdvanceBuild(); // affordable but no longer buildable (already built, upgrade gone, unit capped/un-enabled) → skip without spending
                continue;
            }
            foreach (GoodsOutput cost in target.BuildCost)
            {
                colony.AddGoods(Ruleset.StorageIdOf(cost.GoodsId), -cost.Amount);
            }
            bool repeats = target.IsUnit && colony.BuildQueue.Count == 1; // a lone queued unit is rebuilt next turn
            if (target.IsUnit)
            {
                // A land unit musters on the colony tile; a ship launches into the colony's port — a water tile beside it
                // (a shipyard colony is coastal, so a water neighbour always exists), since a ship can't sit on land.
                Position berth = target.Unit is { IsNaval: true }
                    ? colony.Position.Neighbours().First(n => Map.InBounds(n) && Map.TerrainAt(n).IsWater)
                    : colony.Position;
                SpawnUnit(Ruleset.Unit(target.Id), berth, colony.OwnerId);
            }
            else if (target.UpgradesFrom is { } upgraded)
            {
                colony.ReplaceBuilding(upgraded, target.Id);
            }
            else
            {
                colony.AddBuilding(target.Id);
            }
            if (!repeats)
            {
                colony.AdvanceBuild(); // front complete → the next item becomes current (a lone unit stays to repeat)
            }
            return; // one build per turn
        }
    }

    /// <summary>
    /// Ends the current turn: colonies produce, eat, and grow; units regain
    /// movement; the turn counter advances. (Turn-step order matters and grows
    /// with each economy slice — documented in docs/systems/turns.md.)
    /// </summary>
    public void EndTurn()
    {
        // One full round around the player ring: each player takes its turn in order (the human, the foreign
        // powers, and the native nations all act), then the shared world advances once. The ring pointer
        // completes the loop back to the player it started on. (A defeated human does NOT short-circuit here: that
        // would freeze the human's stream-0 evolution and so break the ADR-009 byte-stability invariant — a game
        // where the AI wipes the human out would diverge from one where it doesn't. Stopping the game on defeat is
        // a presentation-layer concern, deferred; EndTurn stays AI-action-independent for the human's stream 0.)
        _combatNotices.Clear();     // this turn's AI-initiated raids on the human are collected fresh each round
        _colonyLossNotices.Clear(); // and this turn's AI captures of human colonies
        _colonyRaidNotices.Clear(); // and this turn's native pillages of human colonies
        _colonyGiftNotices.Clear(); // and this turn's friendly native gifts to human colonies
        _customHouseSaleNotices.Clear(); // and this turn's custom-house auto-sales from human colonies
        _rumourNotices.Clear(); // and any rumour outcomes the human explored this turn (normally drained by the UI mid-turn; cleared here belt-and-braces)
        RefusePendingDemand();      // a tribute demand the human ended the turn without answering counts as a refusal (FreeCol session timeout = reject)
        DeclinePendingMounds();     // an unanswered strange-mounds prompt counts as "leave them be" — clears it before the AI turns so it can't strand or block exploration across the round
        int startIndex = _currentPlayerIndex;
        do
        {
            RunPlayerTurn(_players[_currentPlayerIndex]);
            _currentPlayerIndex = NextPlayerIndex(_currentPlayerIndex);
        }
        while (_currentPlayerIndex != startIndex);

        AdvanceSailing();
        AdvanceRepairs();           // damaged ships heal a turn; one finishing now regains its movement below
        DetectColonialContacts();   // first sight of a rival colonial power → Peace (FP-6a)
        DecayColonialTension();     // colonial-pair tension cools each turn (mirrors native alarm)
        UpdateColonialStances();    // stance follows tension: war → cease-fire → peace as it cools (FP-6b)
        RunMonarchTick();           // the player's King may act (tax/REF/war/mercenaries) — ephemeral RNG, stream 0 untouched (P6)
        ResolveWarOfIndependence(); // a rebel that has broken the REF wins its independence (P6)
        RunSpanishSuccession();     // from 1600, a fading European AI is absorbed by the dominant one (P6)
        ApplyAmbientNativeAlarm();   // natives resent the human's nearby colonies/troops (FreeCol csNewTurn) — before the calm-down
        ProcessMissions();           // missions accrue converts on the alarm this turn produced (FreeCol csStartTurn) — before the decay
        foreach (NativeSettlement settlement in _nativeSettlements)
        {
            DecayNativeAlarm(settlement);
        }
        foreach (Unit unit in _units)
        {
            if (unit.Orders == UnitOrders.Fortifying)
            {
                unit.Orders = UnitOrders.Fortified; // a turn spent digging in completes (FreeCol ages FORTIFYING → FORTIFIED)
            }
            // A ship still under repair stays pinned at 0 moves (FreeCol forced repair); everyone else resets.
            unit.MovementLeft = unit.IsUnderRepair ? 0 : InitialMovement(unit); // base + role bonus (dragoon/scout +9)
        }
        Turn++;
    }

    /// <summary>
    /// Runs one player's turn. A native nation runs its raid/wander AI (<see cref="RunNativeTurn"/>). A colonial
    /// player's colonies produce/eat/grow and it accrues liberty and immigration (FP-5); a foreign power then
    /// runs its AI — the economy (pursue a father, sell surplus, recruit) and the FP-4 unit AI
    /// (move/explore/found). The human draws only from stream 0 and every non-human player only from its own
    /// stream (<see cref="RandomFor"/>), so the human's game stays byte-stable (ADR-009).
    /// </summary>
    private void RunPlayerTurn(Player player)
    {
        if (player.PlayerType == PlayerType.Native)
        {
            RunNativeTurn(player); // braves raid the human when alarmed, else wander — on the nation's own stream
            return;
        }
        if (player.PlayerType == PlayerType.RoyalExpeditionaryForce)
        {
            RunRefTurn(player); // the King's army sails in and assaults the rebel — on its own stream
            return;
        }
        // Colonial powers and the rebel/independent nation all run the colonial economy path below.
        if (player.PlayerType is not (PlayerType.Colonial or PlayerType.Rebel or PlayerType.Independent))
        {
            return;
        }

        BombardEnemyShips(player); // fort/fortress colonies fire on adjacent enemy ships first (FreeCol csStartTurn)

        ProcessImprovements(player); // advance any pioneers building tile improvements; land completed ones (no-op + RNG-free when none)

        ProcessGotos(player); // walk any units on a standing goto toward their destination (no-op when none — RNG-free)

        foreach (Colony colony in ColoniesOf(player))
        {
            RunColonyTurn(player, colony);
        }
        AccumulateLibertyAndElectFathers(player);
        ApplyFreeBuildings(player); // La Salle: a free stockade in each colony that has reached the required population
        AccumulateImmigrationAndEmigrate(player);
        PayBuildingUpkeep(player); // deduct Σ building upkeep from gold — no-op in classic (enableUpkeep default off)
        ProcessTradeRoutes(player); // auto-haul any carriers on a trade route (no-op + no RNG when none — stream-0-safe)

        if (!player.IsHuman)
        {
            RunForeignPowerEconomy(player); // FP-5: pursue a father, sell surplus, recruit (own stream/market)
            RunForeignPowerTurn(player);     // FP-4: move / explore / found
        }
    }

    /// <summary>
    /// Deducts each colony's per-turn gold upkeep — Σ over its buildings of <see cref="BuildingType.Upkeep"/> — from
    /// the player's treasury (FreeCol <c>Colony.getUpkeep</c> summed by <c>ServerPlayer.csPayUpkeep</c>). Gated on the
    /// ruleset's <see cref="Ruleset.UpkeepEnabled"/> game option (classic <c>model.option.enableUpkeep</c> defaults
    /// off), so the classic economy charges nothing and stays byte-identical — the deduction runs only when a ruleset
    /// turns upkeep on. RNG-free. If the player can't cover the bill, gold is floored at 0 (it never goes negative).
    /// </summary>
    /// <remarks>
    /// TODO (86d3c9ux4): FreeCol applies a BANKRUPTCY disaster — a production-penalty modifier — to a player who can't
    /// pay, and lifts it once they can again. That penalty is a separate backlog task; here we only floor gold at 0.
    /// </remarks>
    private void PayBuildingUpkeep(Player player)
    {
        if (!Ruleset.UpkeepEnabled)
        {
            return; // classic default: no upkeep, default game byte-identical
        }
        int upkeep = ColoniesOf(player)
            .Sum(colony => colony.Buildings.Sum(buildingId => Ruleset.Building(buildingId).Upkeep));
        if (upkeep > 0)
        {
            player.Gold = Math.Max(0, player.Gold - upkeep); // can't pay → floored at 0 (bankruptcy penalty is 86d3c9ux4)
        }
    }

    /// <summary>Goods left in an AI colony's warehouse when selling surplus (0 = sell all sellable output each turn).</summary>
    private const int AiTradeReserve = 0;

    /// <summary>How many of its own colonists a foreign power lets wait in Europe before it stops recruiting (no AI shipping yet).</summary>
    private const int AiMaxEuropeRecruits = 2;

    /// <summary>
    /// The minimal foreign-power economic AI (FP-5): pursue a Founding Father, sell each colony's tradeable
    /// surplus (never food) to the power's OWN market, and recruit while affordable up to a Europe cap. Every
    /// choice draws from the power's own RNG stream and trades against its own market (ADR-019), so the human's
    /// stream 0 and market are untouched. Colonies/goods are iterated in stable id order for determinism.
    /// </summary>
    private void RunForeignPowerEconomy(Player power)
    {
        // Bank toward a father so accrued liberty is eventually spent (deterministic pick from the own stream).
        if (power.CurrentFather is null && power.OfferedFathers.Count > 0)
        {
            power.CurrentFather = power.OfferedFathers[RandomFor(power).Next(power.OfferedFathers.Count)];
        }

        // Plan each colony's workers before selling — so worked tiles + staffed buildings, not just the unattended
        // centre, feed the sell loop. Tiles first (food-first, so the colony never starves), then the remaining idle
        // colonists into the best production buildings (refineries/carpenter/town hall). RNG-free; the tile plan is
        // diff-applied to preserve on-tile experience, the building fill only ever takes colonists left idle after it.
        foreach (Colony colony in ColoniesOf(power).OrderBy(c => c.Id))
        {
            PlanColonyTileWork(power, colony);
            PlanColonyBuildingWork(power, colony);
        }

        // When the power wants another pioneer (86d3c9vta), each colony keeps a pioneer's worth of tools back rather
        // than selling them all off — FreeCol's AI reserves equipment for the roles it plans to fill (pioneersNeeded).
        // So a colonist standing in a colony can later be armed as a pioneer there (equipping needs the 20 tools
        // co-located — see ColonyCanEquipPioneer) instead of the tools always being cashed out. RNG-free read. (The
        // reserve is per colony, applied in the per-colony loop below — a deliberate over-reserve that simply sells
        // fewer tools; it never starves anything since food is always kept and tools aren't eaten.)
        int toolsReserve = PowerWantsAnotherPioneer(power) ? PioneerToolCost : AiTradeReserve;

        // Sell the surplus of each tradeable good (the colony centre and worked tiles yield cash crops/ore
        // unattended) to the power's own market. Food is kept so the colony never starves itself for gold.
        foreach (Colony colony in ColoniesOf(power).OrderBy(c => c.Id))
        {
            foreach (string goodsId in colony.Stores.Keys.OrderBy(g => g, StringComparer.Ordinal).ToList())
            {
                if (goodsId == Colony.FoodId || !power.Market.IsTradeable(goodsId))
                {
                    continue;
                }
                int reserve = goodsId == ToolsGoodsId ? toolsReserve : AiTradeReserve;
                int surplus = colony.StoreOf(goodsId) - reserve;
                if (surplus > 0)
                {
                    SellColonyGoods(power, colony, goodsId, surplus);
                }
            }
        }

        // Plan each colony's construction (build the highest-value building when nothing is queued).
        foreach (Colony colony in ColoniesOf(power).OrderBy(c => c.Id))
        {
            RunForeignColonyBuildPlan(colony);
        }

        // When flush, invest the surplus in Europe (86d3c9vmr, FreeCol trainAIUnitInEurope + the artillery/ship buy):
        // train a specialist its colonies want and buy a ship/artillery. Run BEFORE the recruit loop so a deliberate
        // wanted expert is preferred over a random recruit and is not crowded out of the Europe person cap. Bounded +
        // deterministic — no RNG draw, so the human's stream 0 stays byte-identical; all spend is the power's own gold.
        RunForeignPowerEuropeSpend(power);

        // Recruit while gold allows, capped so colonists do not pile up in Europe (ships are idle until FP-6).
        while (OwnPersonsInEurope(power) < AiMaxEuropeRecruits
               && power.RecruitDock.Count > 0
               && CheckRecruit(power, 0).Allowed)
        {
            Recruit(power, 0);
        }
    }

    /// <summary>The number of <paramref name="player"/>'s own colonists currently waiting on the Europe dock (not aboard a ship).</summary>
    private int OwnPersonsInEurope(Player player) => _units.Count(u =>
        u.Location == UnitLocation.InEurope && u.Type.IsPerson && !u.IsAboard && IsOwnedBy(u, player));

    /// <summary>Gold a foreign power keeps in reserve before it splurges on Europe units — so a poor power keeps recruiting/building rather than draining its treasury (a documented AI budget floor, not a FreeCol constant).</summary>
    private const int AiEuropeSpendFloor = 1500;

    /// <summary>
    /// The flush-treasury Europe spend (<c>86d3c9vmr</c>, FreeCol <c>EuropeanAIPlayer.trainAIUnitInEurope</c> + the
    /// artillery/ship purchases): when a power's treasury is above <see cref="AiEuropeSpendFloor"/> it invests the
    /// surplus in Europe each turn, at most one unit of each kind so the spend stays bounded:
    /// <list type="number">
    /// <item><b>Train a specialist</b> — while under the Europe person cap (<see cref="AiMaxEuropeRecruits"/>, so trained
    /// experts don't pile up un-shipped), train the cheapest affordable specialist whose <see cref="UnitType.ExpertProduction"/>
    /// matches a good one of the power's colonies is making (its best-worker fill will then place the expert in its building).</item>
    /// <item><b>Buy a ship</b> — if the power owns no naval carrier anywhere, buy the cheapest affordable ship (FreeCol's
    /// transport need — a power must eventually ship its Europe colonists to the New World).</item>
    /// <item><b>Buy artillery</b> — else, buy artillery (defence) when affordable.</item>
    /// </list>
    /// Every choice is by gold/ordinal id — <b>no RNG draw</b>, so human stream 0 is byte-identical (ADR-009); all spend
    /// is the power's own gold via the owner-scoped <see cref="TrainUnit"/>/<see cref="BuyUnit(Player, string)"/> seams.
    /// </summary>
    private void RunForeignPowerEuropeSpend(Player power)
    {
        if (power.Gold < AiEuropeSpendFloor)
        {
            return; // keep the reserve — recruiting/building comes first for a poor power
        }

        // 1. Train one specialist its colonies actually want (cheapest affordable expert for a good a colony produces).
        if (OwnPersonsInEurope(power) < AiMaxEuropeRecruits && WantedColonyGoods(power) is { Count: > 0 } wanted)
        {
            UnitType? specialist = UnitTypesTrainedInEurope()
                .Where(t => t.ExpertProduction is { } g && wanted.Contains(g) && CheckTrain(power, t.Id).Allowed)
                .OrderBy(t => EuropeUnitPrice(power, t)).ThenBy(t => t.Id, StringComparer.Ordinal)
                .FirstOrDefault();
            if (specialist is not null)
            {
                TrainUnit(power, specialist.Id);
            }
        }

        // 2. Buy a transport ship if the power owns no carrier; else 3. buy artillery for defence.
        if (!OwnsNavalCarrier(power))
        {
            UnitType? ship = UnitTypesPurchasedInEurope()
                .Where(t => t.IsNaval && t.IsCarrier && CheckBuyUnit(power, t.Id).Allowed)
                .OrderBy(t => EuropeUnitPrice(power, t)).ThenBy(t => t.Id, StringComparer.Ordinal)
                .FirstOrDefault();
            if (ship is not null)
            {
                BuyUnit(power, ship.Id);
                return; // one big-ticket purchase per turn keeps the spend bounded
            }
        }
        if (CheckBuyUnit(power, ArtilleryUnitTypeId).Allowed)
        {
            BuyUnit(power, ArtilleryUnitTypeId);
        }
    }

    /// <summary>
    /// The <b>exact</b> goods <paramref name="power"/>'s colonies could use a producer for — the basis for choosing which
    /// Europe specialist (matched by its exact <see cref="UnitType.ExpertProduction"/>) to train. It is the goods the
    /// colonies actually <em>produce</em> this turn — each worked tile's good and each centre-tile unattended output, by
    /// their <b>exact</b> good id (so <em>grain</em> and <em>fish</em> stay distinct even though both store as food: a
    /// plains colony with no water wants an expert farmer, not an expert fisherman) — <b>plus</b> the one-step refined
    /// goods makeable from those raws (each good whose <see cref="GoodsType.MadeFrom"/> is a raw produced here), so a
    /// colony farming <em>cotton</em> also wants a <em>cloth</em> expert. Transient warehouse stock is deliberately
    /// excluded: it is keyed by storage id (conflating grain/fish) and the sell loop drains it before this runs.
    /// </summary>
    private HashSet<string> WantedColonyGoods(Player power)
    {
        var produced = new HashSet<string>();
        foreach (Colony colony in ColoniesOf(power))
        {
            foreach (string good in colony.TileWorkers.Values)
            {
                produced.Add(good); // exact tile good (grain / fish / cotton / ore …)
            }
            foreach (ProductionEntry entry in Map.TerrainAt(colony.Position).Productions.Where(p => p.Unattended))
            {
                foreach (GoodsOutput o in entry.Outputs)
                {
                    produced.Add(o.GoodsId); // exact centre output
                }
            }
        }
        // Fold in the refined goods makeable from those raws (one step), so an expert at a refined output counts when its
        // input is being produced: rum-from-sugar, cloth-from-cotton, cigars-from-tobacco, coats-from-furs, tools-from-ore…
        var wanted = new HashSet<string>(produced);
        foreach (GoodsType g in Ruleset.GoodsTypes.Where(g => g.MadeFrom is { } src && produced.Contains(src)))
        {
            wanted.Add(g.Id);
        }
        return wanted;
    }

    /// <summary>True when <paramref name="power"/> owns at least one naval carrier (a ship with hold space) anywhere — the "already has a transport" test for the Europe ship purchase.</summary>
    private bool OwnsNavalCarrier(Player power) =>
        _units.Any(u => IsOwnedBy(u, power) && u.Type.IsNaval && u.Type.IsCarrier);

    /// <summary>
    /// Answers a <paramref name="trade"/> offered to <paramref name="power"/> (the AI-consumption seam, FreeCol
    /// <c>EuropeanAIPlayer.acceptDiplomaticTrade</c> → <c>csAcceptTrade</c>): scores it through the pure
    /// <see cref="EvaluateTrade(int, DiplomaticTrade)"/> and, when the power accepts (no clause unacceptable and the
    /// net value ≥ 0), <see cref="SettleTrade"/>s it; otherwise nothing happens. Both the AI's self-proposed peace
    /// (<see cref="RunForeignPowerDiplomacy"/>) and a future negotiation UI route through this one tested path so a power
    /// answers an offer exactly as it weighs its own. Deterministic — <see cref="EvaluateTrade(int, DiplomaticTrade)"/> is
    /// pure and <see cref="SettleTrade"/> is a deterministic transfer, so it draws <b>no</b> RNG (ADR-009).
    /// </summary>
    /// <returns><c>true</c> if the power accepted and the treaty was settled; <c>false</c> if it declined.</returns>
    internal bool RespondToTrade(Player power, DiplomaticTrade trade)
    {
        if (!EvaluateTrade(power.PlayerId, trade).Accept)
        {
            return false;
        }
        SettleTrade(trade);
        return true;
    }

    /// <summary>The hard ceiling on counter rounds in an AI-to-AI negotiation (FreeCol's patience roll usually ends it sooner; this is the absolute bound so a haggle can never loop forever).</summary>
    private const int MaxNegotiationRounds = 8;

    /// <summary>
    /// Runs a bounded AI-to-AI <b>negotiation</b> over an offered <paramref name="trade"/> (FreeCol's
    /// <c>DiplomaticTrade</c> back-and-forth driven by <c>EuropeanAIPlayer.acceptDiplomaticTrade</c>): the
    /// <paramref name="recipient"/> either accepts as-is (<see cref="RespondToTrade"/> settles it), or
    /// <see cref="CounterOffer(int, DiplomaticTrade, int)"/>s a pruned/cheaper version that the original
    /// <paramref name="proposer"/> then weighs — accepting (and settling) when it is now net-positive to it, or
    /// countering back, until one side accepts, a side gives up (the seeded patience roll on its <b>own</b> stream
    /// returns <c>null</c>), or the round cap <see cref="MaxNegotiationRounds"/> is hit. Each clause valuation is the
    /// pure <see cref="EvaluateTradeItem"/>; the only randomness is each AI's give-up roll, drawn from <b>its own</b>
    /// RNG stream (never the human's stream 0), so a human game is byte-identical (ADR-009). Both parties must be
    /// non-human colonial powers — a negotiation involving the human is the future negotiation UI's job, so this method
    /// only ever exercises the AI counter loop AI-to-AI and leaves stream 0 untouched.
    /// </summary>
    /// <returns><c>true</c> if some treaty was agreed and settled; <c>false</c> if the negotiation collapsed.</returns>
    internal bool NegotiateTrade(Player proposer, Player recipient, DiplomaticTrade trade)
    {
        if (proposer.IsHuman || recipient.IsHuman)
        {
            return false; // AI-to-AI only; a human negotiation routes through the (future) UI, never auto-counters
        }

        DiplomaticTrade current = trade;
        Player answerer = recipient; // the side currently weighing the offer
        Player offerer = proposer;   // the side that made `current`
        for (int round = 0; round < MaxNegotiationRounds; round++)
        {
            if (RespondToTrade(answerer, current)) // accepts as-is → settled
            {
                return true;
            }

            DiplomaticTrade? counter = CounterOffer(answerer.PlayerId, current, round); // prune/cheapen, or give up
            if (counter is null)
            {
                return false; // the answerer walked away (nothing salvageable, too many bad clauses, or patience spent)
            }

            current = counter;
            (answerer, offerer) = (offerer, answerer); // the counter is now offered back to the other side
        }
        return false; // ran out of patience by the round cap without an agreement
    }

    /// <summary>
    /// The foreign power's per-turn diplomacy (FreeCol <c>EuropeanAIPlayer</c>): two parts.
    /// <list type="number">
    /// <item><b>Sue for peace with the human</b> (`86d3c9uar`) — only when <paramref name="alreadyAtWar"/> with the human
    /// (a freshly-declared war is pressed, not undone). It weighs a single-clause <b>peace</b> treaty with the human
    /// (a <see cref="StanceTradeItem"/> to <see cref="Stance.Peace"/>) and, when its own
    /// <see cref="EvaluateTrade(int, DiplomaticTrade)"/> accepts — i.e. it is militarily weak enough
    /// (<see cref="EvaluateStance"/>: a low strength ratio scores peace positive) — settles it via
    /// <see cref="RespondToTrade"/>, ending the war. Bounded (one peace offer/turn, no haggling). A strong power's
    /// evaluation rejects the peace, so it fights on. RNG-free.</item>
    /// <item><b>Haggle foreign-foreign wars to a treaty</b> (`86d3drn4h`) — for each <em>other</em> foreign power this one
    /// is at <see cref="Stance.War"/> with, the <b>lower-id</b> party drives a bounded multi-round
    /// <see cref="NegotiateTrade"/> over a peace offer (so each war pair is negotiated once per round, not twice). The
    /// answerer accepts, prunes/cheapens and counters, or gives up — until a truce settles or the talks collapse. Each
    /// give-up roll draws <b>that power's own</b> RNG stream (never the human's stream 0), and both parties are non-human,
    /// so the human's seeded game stays byte-identical (ADR-009).</item>
    /// </list>
    /// Deterministic and (for the human path) RNG-free; no save change. A human-driven negotiation will later route an
    /// offered treaty through <see cref="RespondToTrade"/>/<see cref="NegotiateTrade"/> the same way.
    /// </summary>
    /// <param name="power">The foreign power taking its diplomacy turn.</param>
    /// <param name="alreadyAtWar">Whether <paramref name="power"/> was already at war with the human coming into this turn (the sue-for-peace gate; a war declared from territorial tension <em>this</em> turn is excluded so it isn't instantly undone).</param>
    private void RunForeignPowerDiplomacy(Player power, bool alreadyAtWar)
    {
        // 1) Sue for peace with the HUMAN when losing a war that predates this turn.
        if (alreadyAtWar && StanceBetween(power.PlayerId, _human.PlayerId) == Stance.War)
        {
            var peace = new DiplomaticTrade(power.PlayerId, _human.PlayerId)
                .Add(new StanceTradeItem(power.PlayerId, _human.PlayerId, Stance.Peace));
            RespondToTrade(power, peace); // settles iff the power's own evaluation accepts (a weak power sues for peace)
        }

        // 2) Haggle each FOREIGN-FOREIGN war to a peace treaty via the multi-round NegotiateTrade (86d3drn4h). Only the
        // lower-id party drives a given war pair, so it is negotiated exactly once per round (the higher-id power's own
        // turn finds the pair already at peace, or still at war if the talks broke down, and skips it). NegotiateTrade is
        // AI-to-AI only and draws each power's own stream — never the human's stream 0. Iterate the other powers in stable
        // id order for determinism; snapshot to a list as a settled treaty mutates stance mid-iteration.
        foreach (Player other in _players
                     .Where(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial
                                 && power.PlayerId < p.PlayerId // lower-id party drives each war pair exactly once
                                 && StanceBetween(power.PlayerId, p.PlayerId) == Stance.War)
                     .OrderBy(p => p.PlayerId)
                     .ToList())
        {
            var peace = new DiplomaticTrade(power.PlayerId, other.PlayerId)
                .Add(new StanceTradeItem(power.PlayerId, other.PlayerId, Stance.Peace));
            NegotiateTrade(power, other, peace); // settles a truce when both want it; otherwise the talks collapse (no-op)
        }
    }

    /// <summary>
    /// The minimal foreign-power AI (FP-4 / 1c-2 / 1c-3f): per unit in stable by-id order. When the power is at
    /// <see cref="Stance.War"/> with the human (war only starts when the human attacks it), an <b>armed</b> unit
    /// goes on the offensive — an armed <b>land</b> unit beside an <b>undefended</b> human colony captures it
    /// (1c-3f, the decisive move; a garrisoned colony's defender is fought via the unit-hunt first), otherwise it
    /// hunts the nearest human unit, attacking when adjacent (1c-2); with no field unit to chase, a land unit
    /// instead <b>marches on the nearest human colony</b> (86d3bx03d besiege fallback) so it closes on a colony to
    /// capture rather than wandering off. Otherwise a colonist founds a colony where it stands while the power has
    /// fewer than <see cref="MaxAiColonies"/> colonies, else steps one tile toward the nearest unexplored tile;
    /// ships and non-founders idle. Every choice draws from the player's own RNG stream
    /// (ADR-009) — never the human's stream 0 — so the human's game stays byte-stable. At war an armed
    /// <b>warship</b> hunts the human's nearest ship too (1c-3a′); transports/unarmed ships idle.
    /// </summary>
    private void RunForeignPowerTurn(Player power)
    {
        // Whether the power was ALREADY at war with the human coming into this turn (before the territorial-tension
        // accrual below may freshly declare one). Only an already-running war is eligible to be sued for peace — a power
        // that breaks the peace this turn (it has just been crowded, so it is aggrieved) presses its grievance rather
        // than instantly offering peace the same turn (86d3c9uar: don't undo a just-declared war).
        bool alreadyAtWar = StanceBetween(power.PlayerId, _human.PlayerId) == Stance.War;

        // Accrue territorial tension and re-derive the stance from it (86d3c9udb, FreeCol determineStances): a power
        // that the human is crowding flips Peace/CeaseFire → War on its own before it acts, so the offensive below
        // actually engages. The accrual is RNG-free, so the human's stream 0 stays byte-identical (ADR-009).
        AccrueTerritorialTension(power);
        if (power.Stances.GetValueOrDefault(_human.PlayerId) is Stance.Peace or Stance.CeaseFire
            && StanceFromTension(power.Stances[_human.PlayerId], power.Tensions.GetValueOrDefault(_human.PlayerId)) == Stance.War)
        {
            // Declare war exactly as an attack does (FreeCol: war is mutual): set the stance both ways and spike the
            // pair's tension symmetrically to the WAR modifier, so the end-of-turn UpdateColonialStances keeps it at War
            // (it re-derives from the lower-id player's tension — which the directional accrual alone would leave at 0).
            SetStance(power.PlayerId, _human.PlayerId, Stance.War);
            ChangeTension(power.PlayerId, _human.PlayerId, TensionWar);
        }

        // Per-turn diplomacy (86d3c9uar sue-for-peace + 86d3drn4h foreign-foreign haggle): a power already at war with the
        // human weighs a peace treaty through the SAME pure EvaluateTrade the rules expose and settles it when its own
        // evaluation accepts (it is militarily weak), so the war ends instead of grinding on — gated on `alreadyAtWar` so a
        // war just declared from territorial tension this turn is pressed, not instantly undone. The same call ALSO drives a
        // bounded NegotiateTrade haggle over any FOREIGN-FOREIGN war this power is in (the lower-id party drives each pair
        // once). Run unconditionally now (the human-peace path self-gates on `alreadyAtWar`); the human-peace settle is
        // RNG-free and the foreign-foreign haggle draws only the AI powers' own streams — so human stream 0 stays
        // byte-identical (ADR-009). Drawn before atWarWithHuman is read, so a power that makes peace stands down the same turn.
        RunForeignPowerDiplomacy(power, alreadyAtWar);

        bool atWarWithHuman = StanceBetween(power.PlayerId, _human.PlayerId) == Stance.War;

        // Snapshot the owned units (founding/combat removes a unit from _units mid-loop).
        foreach (Unit unit in _units.Where(u => IsOwnedBy(u, power)).OrderBy(u => u.Id).ToList())
        {
            if (!unit.IsOnMap)
            {
                continue; // units still in Europe wait (a warship can now act below; an unarmed ship falls through to idle)
            }

            // A pioneer mid-build is committed: its work is advanced by ProcessImprovements at the start of this turn
            // (and completed there when done) — leave it be so it isn't marched off its half-finished job (86d3c9vta).
            if (unit.IsImproving)
            {
                continue;
            }

            // AI logistics (86d3c9vq9, FreeCol CashInTreasureTrainMission): a power's treasure train — won by sacking a
            // native settlement or from a Lost City Rumour — heads to the nearest owned colony and banks its gold there,
            // instead of sitting idle forever. The cash-in is RNG-free; the step draws the power's OWN stream (never
            // stream 0). Guarded on owning a loaded treasure train, so a power without one is unaffected.
            if (unit.Type.CarryTreasure && unit.TreasureAmount > 0)
            {
                if (CheckCashInTreasureTrain(unit).Allowed)
                {
                    CashInTreasureTrain(unit); // standing at an owned colony → bank the net gold to the power
                }
                else if (NearestColonyOf(power, unit.Position, Map.Width + Map.Height) is { } bank
                    && StepToward(power, unit, bank.Position) is { } toBank)
                {
                    MoveUnit(unit, toBank); // escort it toward the nearest owned colony
                }
                continue;
            }

            // At war, an armed unit goes on the offensive instead of expanding. A land unit beside an undefended
            // human colony captures it (1c-3f — the decisive move, taking priority over chasing a field unit);
            // otherwise it hunts the human's nearest unit (1c-2 / 1c-3a′). Combat draws from the power's own RNG
            // stream via the internal Attack/AttackColony overloads (siblings of RaidHumanUnit), never stream 0.
            if (atWarWithHuman && unit.MovementLeft > 0 && OffenceBase(unit) > 0)
            {
                if (!unit.Type.IsNaval && AdjacentCapturableHumanColony(unit) is { } colonyTile)
                {
                    CapturePlayerColony(power, unit, colonyTile);
                    continue;
                }
                // Scored seek-and-destroy (FP-6a): pick the best human unit-tile OR colony within an escalating
                // Chebyshev range (FreeCol's 8/12/16 ladder) by the `UnitSeekAndDestroyMission` heuristic — value
                // minus distance, weak/valuable/treasure targets favoured, fortifications avoided — instead of just
                // hunting the nearest unit. Attack when adjacent + legal, else step toward it.
                if (PickAttackTarget(unit) is { } target)
                {
                    bool adjacent = unit.Position.IsAdjacentTo(target.Position);
                    if (target.IsColony)
                    {
                        if (adjacent && CheckAttackColony(unit, target.Position).Allowed)
                        {
                            CapturePlayerColony(power, unit, target.Position);
                        }
                        else if (StepToward(power, unit, target.Position) is { } toColony)
                        {
                            MoveUnit(unit, toColony);
                        }
                    }
                    else if (adjacent && CheckAttack(unit, target.Position).Allowed)
                    {
                        AttackHumanUnit(power, unit, target.Position);
                    }
                    else if (StepToward(power, unit, target.Position) is { } chase)
                    {
                        MoveUnit(unit, chase); // a hemmed-in attacker simply waits
                    }
                    continue;
                }
                // Nothing scored within the seek range (every human target is >16 tiles off): close on the nearest
                // human unit at ANY distance so a war unit — a warship, or a non-founder land unit like artillery —
                // pursues rather than idling (the uncapped chase the pre-FP-6a AI used; the scored pick above is the
                // preferred behaviour, this is the out-of-range fallback). Same-domain + human-only via NearestHumanUnit.
                if (NearestHumanUnit(unit) is { } distantPrey && StepToward(power, unit, distantPrey.Position) is { } pursue)
                {
                    MoveUnit(unit, pursue);
                    continue;
                }
                // With no human field unit anywhere, a land unit marches on the nearest human colony (the war objective)
                // so it besieges instead of wandering off to explore (86d3bx03d) — closing on an undefended colony to
                // capture above, or on a garrison to fight as a field unit. Naval units can't besiege a colony.
                if (!unit.Type.IsNaval && NearestHumanColony(unit) is { } targetColony
                    && StepToward(power, unit, targetColony.Position) is { } colonyStep)
                {
                    MoveUnit(unit, colonyStep);
                    continue;
                }
            }

            // Defend-settlement garrisoning (FP-5, FreeCol DefendSettlementMission): an **armed land unit** that isn't
            // going to found a new colony marches to the nearest **undefended own colony** and stands guard on its tile —
            // so the power's colonies aren't left open to capture/pillage (a garrisoned colony is fought as a field unit
            // first). Founding a new colony keeps priority (expansion over defence); a unit already standing in an own
            // colony stays put (no thrash). The step draws from the power's own stream (StepToward), never stream 0.
            if (!unit.Type.IsNaval && OffenceBase(unit) > 0)
            {
                if (ColonyAt(unit.Position) is { } here && here.OwnerId == power.PlayerId)
                {
                    continue; // already standing guard in an own colony
                }
                bool willFound = unit.Type.CanFoundColony
                    && ColoniesOf(power).Count() < MaxAiColonies && CheckFoundColony(unit).Allowed;
                if (!willFound && NearestUndefendedOwnColony(power, unit) is { } garrisonTile
                    && StepToward(power, unit, garrisonTile) is { } toGarrison)
                {
                    MoveUnit(unit, toGarrison);
                    continue;
                }
            }

            // AI pioneering — execute (86d3c9vta, FreeCol PioneeringMission/TileImprovementPlan): a tooled pioneer
            // works the power's improvement plans rather than founding/exploring. It builds the best plan on its own
            // tile, else marches to the nearest planned tile in the power's colony footprints. Placed before founding
            // (a pioneer improves, not founds) and the chief/explore fallbacks. BuildImprovement/the plan ranking are
            // RNG-free; the march draws the power's OWN stream (StepToward) — never the human's stream 0 (ADR-009).
            if (IsPioneer(unit) && unit.MovementLeft > 0)
            {
                if (BuildablePlanOnTile(power, unit) is { } here)
                {
                    BuildImprovement(unit, here);
                    continue;
                }
                if (NearestImprovementPlanTile(power, unit) is { } planTile
                    && StepToward(power, unit, planTile) is { } toPlan)
                {
                    MoveUnit(unit, toPlan);
                    continue;
                }
                // No plan left to pursue — an idle pioneer falls through to the chief/explore fallbacks below
                // (NOT founding — a tooled pioneer must never be consumed to found a colony, which would waste its
                // tools; the founding branch is gated on !IsPioneer for exactly this).
            }

            if (!unit.Type.CanFoundColony)
            {
                continue; // non-founders (e.g. an idle soldier at peace) wait
            }
            // A tooled pioneer never founds (it would destroy its 20 tools); it improves, or — out of plans — explores.
            if (!IsPioneer(unit) && ColoniesOf(power).Count() < MaxAiColonies && CheckFoundColony(unit).Allowed)
            {
                FoundColony(unit);
                continue;
            }
            // AI pioneering — equip (86d3c9vta, FreeCol pioneersNeeded/getRoleWithAbility): an idle plain colonist
            // standing in an own colony that stocks tools is armed as a pioneer when the power wants another one
            // (capped, only while improvement plans exist). It pioneers on its following turns via the execute branch
            // above. EquipRole consumes the colony's tools — RNG-free, and the colony is the power's own.
            if (unit.HasDefaultRole && ColonyAt(unit.Position) is { } equipColony && equipColony.OwnerId == power.PlayerId
                && PowerWantsAnotherPioneer(power) && CheckEquipRole(unit, equipColony, PioneerRoleId).Allowed)
            {
                EquipRole(unit, equipColony, PioneerRoleId);
                continue;
            }
            // Establish a mission (86d3c9vta missionary half, FreeCol's missionary `MissionaryMission`): a missionary-role
            // colonist beside a native settlement the power does not already hold a mission in founds one — converting the
            // tribe over time and easing its alarm (the whole mission mechanic is RNG-free, so the human's stream 0 is
            // byte-identical). Takes priority over the chief-audience below: a missionary's purpose is the mission, not a
            // one-off gift. An Angry/Hateful tribe kills the missionary inside EstablishMission — a faithful risk the AI
            // accepts, exactly as a human missionary does.
            if (AdjacentUnmissionedSettlement(power, unit) is { } missionTarget)
            {
                EstablishMission(power, unit, missionTarget);
                continue;
            }
            // Speak with a chief (86d3c9vta slice, FreeCol's scout/explore visiting): a colonist beside a native
            // settlement the power hasn't yet visited takes the chief's audience for a gift/tales, on the power's OWN
            // stream. Per-player first contact (HasBeenVisitedBy) means an AI visit never consumes the human's.
            if (AdjacentUnvisitedSettlement(power, unit) is { } chiefSettlement)
            {
                Visit(power, unit, chiefSettlement, RandomFor(power));
                continue;
            }
            // Seek out a known Lost City Rumour (86d3c9vta, FreeCol's scout/explore missions) before generic
            // exploring: head for the nearest rumour tile the power has discovered. Reaching it auto-resolves the
            // rumour (`TryExploreRumour` via `MoveUnit`) on the power's OWN stream — treasure/units/expertise land for
            // the power, never touching the human's stream 0.
            if (NearestKnownRumour(power, unit) is { } rumour && StepToward(power, unit, rumour) is { } toRumour)
            {
                MoveUnit(unit, toRumour);
                continue;
            }
            // Head for the nearest known native settlement the power hasn't spoken with yet (86d3c9vta scout facet,
            // FreeCol's scout `ScoutingMission` heading for an un-visited settlement): a scout makes for a concrete
            // discovery — a chief's gift/tales — rather than wandering blindly, before falling back to generic explore.
            // Reaching it triggers the adjacent-chief `Visit` branch above next turn. Only settlements in the power's
            // own fog (`NearestUnvisitedSettlement`), so it isn't omniscient; the step draws the power's OWN stream.
            if (NearestUnvisitedSettlement(power, unit) is { } toVisit && StepToward(power, unit, toVisit) is { } toChief)
            {
                MoveUnit(unit, toChief);
                continue;
            }
            // AI pioneering — tool up (86d3c9vta): rather than wander off exploring, an idle colonist marches to the
            // nearest own colony that can equip a pioneer when the power wants one (it equips on arrival via the branch
            // above). Last before generic explore, so it only redirects a colonist that would otherwise roam. Own stream.
            if (unit.HasDefaultRole && PowerWantsAnotherPioneer(power)
                && NearestEquippableColony(power, unit) is { } tooledColony
                && StepToward(power, unit, tooledColony) is { } toTooled)
            {
                MoveUnit(unit, toTooled);
                continue;
            }
            if (StepTowardNearestUnexplored(power, unit) is { } step)
            {
                MoveUnit(unit, step);
            }
        }
    }

    /// <summary>The nearest Lost City Rumour tile <paramref name="power"/> has already discovered (in its fog), by Chebyshev from <paramref name="unit"/> (ties by position), or null when it knows of none. Sparse (<see cref="GameMap.Rumours"/>) so it stays cheap each turn.</summary>
    private Position? NearestKnownRumour(Player power, Unit unit) =>
        Map.Rumours
            .Where(r => power.Explored.Contains(r))
            .OrderBy(r => Chebyshev(r, unit.Position)).ThenBy(r => r.Y).ThenBy(r => r.X)
            .Select(r => (Position?)r)
            .FirstOrDefault();

    /// <summary>True when one of <paramref name="power"/>'s armed land units stands on <paramref name="colony"/>'s tile (it has a garrison defender).</summary>
    private bool ColonyHasArmedDefender(Player power, Colony colony) =>
        _units.Any(u => IsOwnedBy(u, power) && u.IsOnMap && !u.Type.IsNaval && OffenceBase(u) > 0 && u.Position == colony.Position);

    /// <summary>The tile of <paramref name="power"/>'s nearest colony lacking an armed land defender (Chebyshev from <paramref name="unit"/>, ties by position), or null when every own colony is garrisoned.</summary>
    private Position? NearestUndefendedOwnColony(Player power, Unit unit) =>
        ColoniesOf(power)
            .Where(c => !ColonyHasArmedDefender(power, c))
            .OrderBy(c => Chebyshev(c.Position, unit.Position)).ThenBy(c => c.Position.Y).ThenBy(c => c.Position.X)
            .Select(c => (Position?)c.Position)
            .FirstOrDefault();

    // --- AI pioneering helpers (86d3c9vta, FreeCol PioneeringMission / TileImprovementPlan / pioneersNeeded) ---

    /// <summary>The role a pioneer carries (the improve-terrain role the AI equips and the human equips by hand).</summary>
    private const string PioneerRoleId = "model.role.pioneer";

    /// <summary>The goods a pioneer's equipment is made of (FreeCol <c>model.role.pioneer</c> required-goods).</summary>
    private const string ToolsGoodsId = "model.goods.tools";

    /// <summary>One pioneer count's worth of tools (the classic <c>model.role.pioneer</c> required-goods value) — the AI keeps this many in store when it wants a pioneer instead of selling them all off.</summary>
    private const int PioneerToolCost = 20;

    /// <summary>Most pioneers an AI power keeps at once (FreeCol <c>pioneersNeeded</c>, bounded so it never drains its colonist pool).</summary>
    private const int MaxAiPioneers = 2;

    /// <summary>
    /// The improvements the AI plans, in descending priority (FreeCol <c>TileImprovementPlan</c> favours
    /// production-boosting work): plow a field, clear a forest, then road. Roads rank lowest (a minor gain).
    /// </summary>
    private static readonly (string Id, int Weight)[] AiImprovementPriority =
    [
        (TileImprovementType.PlowId, 3),
        (TileImprovementType.ClearForestId, 2),
        (TileImprovementType.RoadId, 1),
    ];

    /// <summary>The plan weight of an improvement id (see <see cref="AiImprovementPriority"/>).</summary>
    private static int AiPlanWeight(string improvementId) =>
        AiImprovementPriority.First(p => p.Id == improvementId).Weight;

    /// <summary>True when a unit is a tooled pioneer — a non-native unit in a role that grants terrain-improvement with equipment left.</summary>
    private bool IsPioneer(Unit unit) =>
        !unit.IsNative && unit.RoleCount > 0 && Ruleset.Role(unit.RoleId).CanImproveTerrain;

    /// <summary>The number of pioneers <paramref name="power"/> owns, counting one mid-build (so a busy pioneer still counts against the cap).</summary>
    private int OwnedPioneerCount(Player power) =>
        _units.Count(u => IsOwnedBy(u, power) && (IsPioneer(u) || u.IsImproving));

    /// <summary>The tiles a power's colonies work (each colony tile + its eight neighbours, in-bounds land), de-duplicated — the area the AI plans improvements over.</summary>
    private IEnumerable<Position> ColonyFootprintTiles(Player power) =>
        ColoniesOf(power)
            .SelectMany(c => c.Position.Neighbours().Append(c.Position))
            .Where(p => Map.InBounds(p) && !Map.TerrainAt(p).IsWater)
            .Distinct();

    /// <summary>
    /// Whether <paramref name="improvement"/> is worth planning on <paramref name="tile"/> for <paramref name="power"/>
    /// (unit-independent): it applies to the terrain, isn't already there, no own pioneer is already building it there,
    /// and — for a plow — the tile actually farms a crop the plow boosts (so the AI doesn't plow barren ground).
    /// </summary>
    private bool IsWorthwhilePlan(Player power, Position tile, TileImprovementType improvement)
    {
        if (Map.HasImprovement(tile, improvement.Id) || !improvement.AppliesTo(Map.TerrainAt(tile)))
        {
            return false;
        }
        if (_units.Any(u => IsOwnedBy(u, power) && u.IsImproving
                && u.WorkImprovementId == improvement.Id && u.Position == tile))
        {
            return false; // another of the power's pioneers is already on this exact job
        }
        // A plow only helps a tile that grows a farmed crop (grain is the universal one); skip plowing barren ground.
        return !improvement.IsPlow || TileYieldPotential(tile, GrainId) > 0;
    }

    /// <summary>The highest-priority improvement id worth planning on <paramref name="tile"/> for <paramref name="power"/>, or null when none is worthwhile there.</summary>
    private string? BestPlanFor(Player power, Position tile) =>
        AiImprovementPriority
            .Where(p => IsWorthwhilePlan(power, tile, Ruleset.Improvement(p.Id)))
            .Select(p => (string?)p.Id)
            .FirstOrDefault();

    /// <summary>
    /// The best improvement <paramref name="pioneer"/> can build standing on its current tile — only when that tile is
    /// one its colonies actually work (a footprint tile; the AI improves its own land, not random wilderness) and the
    /// build is gated through <see cref="CheckBuildImprovement"/> — or null.
    /// </summary>
    private string? BuildablePlanOnTile(Player power, Unit pioneer) =>
        ColonyFootprintTiles(power).Contains(pioneer.Position)
        && BestPlanFor(power, pioneer.Position) is { } id && CheckBuildImprovement(pioneer, id).Allowed
            ? id
            : null;

    /// <summary>Whether the power has at least one worthwhile improvement plan anywhere in its colony footprints.</summary>
    private bool HasAnyImprovementPlan(Player power) =>
        ColonyFootprintTiles(power).Any(t => BestPlanFor(power, t) is not null);

    /// <summary>
    /// The nearest footprint tile with a worthwhile plan, ranked by plan priority then Chebyshev distance from the
    /// pioneer then position (deterministic), or null — the pioneer's next job to march to.
    /// </summary>
    private Position? NearestImprovementPlanTile(Player power, Unit pioneer) =>
        ColonyFootprintTiles(power)
            .Select(t => (Tile: t, Plan: BestPlanFor(power, t)))
            .Where(x => x.Plan is not null)
            .OrderByDescending(x => AiPlanWeight(x.Plan!))
            .ThenBy(x => Chebyshev(x.Tile, pioneer.Position)).ThenBy(x => x.Tile.Y).ThenBy(x => x.Tile.X)
            .Select(x => (Position?)x.Tile)
            .FirstOrDefault();

    /// <summary>Whether <paramref name="colony"/> stocks the tools to equip a pioneer (the pioneer role's required goods).</summary>
    private bool ColonyCanEquipPioneer(Colony colony) =>
        Ruleset.Role(PioneerRoleId).RequiredGoods.All(g => colony.StoreOf(Ruleset.StorageIdOf(g.GoodsId)) >= g.Amount);

    /// <summary>Whether <paramref name="power"/> should equip another pioneer: below its cap (one per colony, capped at <see cref="MaxAiPioneers"/>) and with improvement work pending.</summary>
    private bool PowerWantsAnotherPioneer(Player power) =>
        OwnedPioneerCount(power) < Math.Min(ColoniesOf(power).Count(), MaxAiPioneers)
        && HasAnyImprovementPlan(power);

    /// <summary>The tile of the power's nearest colony that can equip a pioneer (Chebyshev from <paramref name="unit"/>, ties by position), or null.</summary>
    private Position? NearestEquippableColony(Player power, Unit unit) =>
        ColoniesOf(power)
            .Where(ColonyCanEquipPioneer)
            .OrderBy(c => Chebyshev(c.Position, unit.Position)).ThenBy(c => c.Position.Y).ThenBy(c => c.Position.X)
            .Select(c => (Position?)c.Position)
            .FirstOrDefault();

    /// <summary>
    /// Resolves a foreign power's attack on the human unit at <paramref name="target"/> through the power's OWN
    /// RNG stream (never stream 0), recording a <see cref="CombatNotice"/> for the presentation. The foreign
    /// sibling of <see cref="RaidHumanUnit"/>: the defender is human-owned (filtered by <see cref="NearestHumanUnit"/>).
    /// </summary>
    private void AttackHumanUnit(Player power, Unit attacker, Position target)
    {
        Unit defender = DefenderAt(attacker, target)!;       // human-owned (filtered upstream)
        string defenderTypeId = defender.Type.Id;            // capture before the attack — a beaten loser is removed
        CombatResult result = Attack(attacker, target, RandomFor(power)); // INTERNAL overload → the power's stream
        // A privateer flies no flag (FreeCol isOwnerHidden): the victim sees an anonymous raider, not the nation.
        string attackerNation = attacker.Type.Piracy ? UnknownEnemyNationId : power.NationId!;
        _combatNotices.Add(new CombatNotice(attackerNation, defenderTypeId, result, target));
    }

    /// <summary>
    /// The tile of an undefended human colony this land unit may capture right now — adjacent and ungarrisoned,
    /// as gated by <see cref="CheckAttackColony"/> (ties broken by position), or null if none. The
    /// <see cref="IsHumanOwned(Colony)"/> filter is the same sole contract as <see cref="NearestHumanUnit"/>:
    /// <see cref="CheckAttackColony"/> alone would also admit another rival's colony, so the human filter keeps
    /// the AI assaulting only the human. A garrisoned human colony is excluded here (its garrison is fought via
    /// the unit-hunt first) so it only becomes capturable once undefended.
    /// </summary>
    private Position? AdjacentCapturableHumanColony(Unit attacker) =>
        _colonies
            .Where(c => IsHumanOwned(c) && CheckAttackColony(attacker, c.Position).Allowed)
            .OrderBy(c => c.Position.Y).ThenBy(c => c.Position.X)
            .Select(c => (Position?)c.Position)
            .FirstOrDefault();

    /// <summary>
    /// Resolves a foreign power's assault on the undefended human colony at <paramref name="target"/> through the
    /// power's OWN RNG stream (never stream 0), recording a <see cref="ColonyLossNotice"/> on a win so the
    /// presentation can tell the player. The colony-capture sibling of <see cref="AttackHumanUnit"/>; it reuses
    /// the shared <see cref="AttackColony(Unit, Position, Randomness.IGameRandom)"/> resolution — a win hands the
    /// colony (people/buildings/stores) to the power, a loss disarms/demotes the repelled attacker (no notice).
    /// The colony name is read before the handover, while it is still the human's.
    /// </summary>
    private void CapturePlayerColony(Player power, Unit attacker, Position target)
    {
        string colonyName = ColonyAt(target)!.Name; // read before AttackColony hands the colony over
        CombatResult result = AttackColony(attacker, target, RandomFor(power)); // INTERNAL overload → the power's stream
        if (result is CombatResult.GreatWin or CombatResult.Win)
        {
            _colonyLossNotices.Add(new ColonyLossNotice(power.NationId!, colonyName, target));
        }
    }

    /// <summary>
    /// The tile <paramref name="unit"/> should step to, heading toward the nearest tile its owner has not
    /// explored: the legal adjacent move that most reduces the Chebyshev distance to that target, ties broken
    /// by the player's RNG then by position. Null when the map is fully explored or the unit cannot move.
    /// </summary>
    private Position? StepTowardNearestUnexplored(Player power, Unit unit)
    {
        Position? target = null;
        int best = int.MaxValue;
        foreach (Position p in Map.AllPositions())
        {
            if (power.Explored.Contains(p))
            {
                continue;
            }
            int distance = Chebyshev(p, unit.Position);
            if (distance < best || (distance == best && target is { } t && (p.Y < t.Y || (p.Y == t.Y && p.X < t.X))))
            {
                best = distance;
                target = p;
            }
        }
        return target is { } goal ? StepToward(power, unit, goal) : null; // null = everything explored
    }

    /// <summary>
    /// The legal adjacent move that most reduces the Chebyshev distance to <paramref name="goal"/>, ties broken
    /// by position (Y then X) and finally by the player's own RNG stream. Null when the unit cannot move. The
    /// only random draw is the final tiebreak, taken from <see cref="RandomFor"/> — never the human's stream 0.
    /// </summary>
    private Position? StepToward(Player player, Unit unit, Position goal)
    {
        var steps = unit.Position.Neighbours()
            .Where(n => CheckMove(unit, n).Allowed)
            .ToList();
        if (steps.Count == 0)
        {
            return null;
        }
        int closest = steps.Min(n => Chebyshev(n, goal));
        var tied = steps.Where(n => Chebyshev(n, goal) == closest)
            .OrderBy(n => n.Y).ThenBy(n => n.X)
            .ToList();
        return tied.Count == 1 ? tied[0] : tied[RandomFor(player).Next(tied.Count)];
    }

    /// <summary>
    /// A native brave leaves its camp to hunt the human once its home settlement's alarm reaches this band
    /// (FreeCol <c>NativeAIPlayer.secureIndianSettlement</c>: a settlement sends braves to seek-and-destroy an
    /// enemy only when its tension toward that enemy is above CONTENT — i.e. Displeased or worse). Below it the
    /// brave merely wanders. Per-settlement alarm stands in for FreeCol's nation-level tension (see natives.md).
    /// </summary>
    private const AlarmLevel RaidAlarmThreshold = AlarmLevel.Displeased;

    /// <summary>Demand-amount multiplier: <c>dx = nativeDemands + 1</c> (FreeCol <c>IndianDemandMission.capAmount</c>;
    /// 3 at medium). The raw <c>model.option.nativeDemands</c> lives on
    /// <see cref="Specification.DifficultyOptions.NativeDemands"/>; the <c>+ 1</c> transform stays here.</summary>
    private int NativeDemandsDx => Ruleset.Difficulty.NativeDemands + 1;

    /// <summary>Minimum goods a demand asks for (FreeCol <c>GOODS_DEMAND_MIN</c>).</summary>
    private const int NativeDemandMin = 30;

    /// <summary>Maximum goods a demand asks for — one cargo load (FreeCol <c>GoodsContainer.CARGO_SIZE</c>).</summary>
    private const int NativeDemandMax = 100;

    /// <summary>Alarm relief across the demanding nation's settlements when a demand is paid (FreeCol
    /// <c>-(5 - nativeDemands) * 50</c> = 150 at medium); the difficulty value is
    /// <see cref="Specification.DifficultyOptions.NativeDemands"/>.</summary>
    private int NativeDemandAcceptAlarmRelief => (5 - Ruleset.Difficulty.NativeDemands) * 50;

    /// <summary>
    /// The minimal native AI (slice 1b + colony pillage): each of the nation's units, in stable by-id order, takes
    /// ONE action. When its home settlement is alarmed enough (<see cref="RaidAlarmThreshold"/>) the brave first
    /// **pillages** an adjacent undefended pillageable human colony (<see cref="AdjacentPillageableHumanColony"/> →
    /// <see cref="PillageColony"/>, the high-value target); otherwise it hunts the nearest human unit — attacking
    /// when adjacent, else stepping toward it; otherwise it wanders one tile. Every choice (the wander pick, the
    /// path tiebreak, the combat resolution, the pillage loot pick) draws from the nation's OWN RNG stream via
    /// <see cref="RandomFor"/>, never the human's stream 0, so the human's seeded game stays byte-stable
    /// (ADR-009). A flat priority switch, not FreeCol's mission planner.
    /// </summary>
    private void RunNativeTurn(Player player)
    {
        // Spread the nation's arms to where they're needed (FreeCol NativeAIPlayer.secureSettlements arms-spreading):
        // a calm, well-stocked camp ships a surplus half-unit of muskets/horses to a threatened (alarmed) camp that has
        // none — so a tribe's weapons reach the front line instead of sitting idle in a peaceful village. RNG-free, and
        // run BEFORE equipBraves so the receiving camp can arm its braves from the freshly delivered stock this turn.
        RedistributeArmsToThreatenedSettlements(player);

        // Secure the threatened camps first (FreeCol NativeAIPlayer.equipBraves, run for each settlement at the start
        // of its turn): equip/promote braves from each alarmed settlement's stock, strongest-needed brave first. This
        // is free — it never consumes a brave's action, so an armed brave still raids/wanders below as usual.
        EquipBravesAtThreatenedSettlements(player);

        // Snapshot: a raid can remove the prey (or, on a loss, the brave itself) from _units mid-loop.
        foreach (Unit brave in _units.Where(u => u.OwnerNationId == player.NationId).OrderBy(u => u.Id).ToList())
        {
            if (!brave.IsOnMap || brave.MovementLeft <= 0)
            {
                continue;
            }

            NativeSettlement? home = HomeSettlement(player, brave);
            bool hostile = home is not null && home.AlarmLevel >= RaidAlarmThreshold;
            if (hostile && AdjacentPillageableHumanColony(brave) is { } colonyTile)
            {
                PillageColony(brave, colonyTile, RandomFor(player)); // storm an undefended human colony (own stream)
            }
            else if (hostile && _pendingDemand is null && AdjacentDemandableHumanColony(player, brave) is { } demandColony)
            {
                // Can't simply storm it (defended, or only gold/food to take) → shake it down for tribute instead.
                CreateNativeDemand(player, brave, demandColony);
            }
            else if (hostile && PickRaidTarget(brave) is { } preyTile)
            {
                // Displeasure attack weighting (86d3c9vzp): go for the best-scored human target in range (soft/valuable
                // over dug-in), falling back to the nearest at any distance — instead of always the nearest body.
                if (brave.Position.IsAdjacentTo(preyTile) && CheckAttack(brave, preyTile).Allowed)
                {
                    RaidHumanUnit(player, brave, preyTile);
                }
                else if (StepToward(player, brave, preyTile) is { } step)
                {
                    MoveUnit(brave, step); // hemmed-in hostile braves simply wait (no fallback wander)
                }
            }
            else if (!hostile && TryBringGift(player, brave))
            {
                // a friendly tribe left a gift at an adjacent human colony — the brave's turn is spent
            }
            else if (Wander(player, brave) is { } wanderStep)
            {
                MoveUnit(brave, wanderStep);
            }
        }
    }

    private const int NativeGiftChanceDenominator = 8; // ~1-in-8 chance per eligible (Happy + colony-adjacent) brave turn
    private const int NativeGiftAmount = 25;           // a modest parcel of the shared good
    // A raw trade good the tribes grow — NOT food: gifting food would grow the human colony, adding tile workers and
    // thus extra stream-0 experience rolls, so a native-RNG gift would perturb the human's stream 0 (ADR-009). A raw
    // warehouse good has no such feedback into the human's draw sequence. (We model no native goods store; the parcel
    // is abstracted, like pillage's goods-to-nowhere.)
    private const string NativeGiftGoodsId = "model.goods.tobacco";

    /// <summary>
    /// A friendly tribe's brave brings a gift to an adjacent human colony (FreeCol <c>IndianBringGiftMission</c>):
    /// when the brave's home settlement is <see cref="AlarmLevel.Happy"/> toward the human and it stands beside one of
    /// the human's colonies, a per-turn chance (the nation's OWN RNG stream) leaves a parcel of goods in that colony's
    /// warehouse, recording a <see cref="ColonyGiftNotice"/>. Returns true when a gift was delivered (the brave's turn
    /// is then spent). Pure goodwill — no alarm change, no brave consumed. The chance is drawn only when the brave is
    /// already Happy <em>and</em> colony-adjacent, so an ineligible brave never perturbs the native stream (ADR-009).
    /// </summary>
    private bool TryBringGift(Player nation, Unit brave)
    {
        if (HomeSettlement(nation, brave) is not { AlarmLevel: AlarmLevel.Happy })
        {
            return false;
        }
        Colony? colony = _colonies
            .Where(c => IsHumanOwned(c) && brave.Position.IsAdjacentTo(c.Position))
            .OrderBy(c => c.Position.Y).ThenBy(c => c.Position.X)
            .FirstOrDefault();
        if (colony is null || RandomFor(nation).Next(NativeGiftChanceDenominator) != 0)
        {
            return false;
        }
        colony.AddGoods(NativeGiftGoodsId, NativeGiftAmount);
        _colonyGiftNotices.Add(new ColonyGiftNotice(nation.NationId!, colony.Name, NativeGiftGoodsId, NativeGiftAmount, colony.Position));
        return true;
    }

    /// <summary>The brave role granting muskets (FreeCol <c>model.role.armedBrave</c>).</summary>
    private const string ArmedBraveRoleId = "model.role.armedBrave";

    /// <summary>The brave role granting horses (FreeCol <c>model.role.mountedBrave</c>).</summary>
    private const string MountedBraveRoleId = "model.role.mountedBrave";

    /// <summary>The brave role granting both muskets and horses — the strongest native role (FreeCol <c>model.role.nativeDragoon</c>, offence +3 / defence +2 / +9 move).</summary>
    private const string NativeDragoonRoleId = "model.role.nativeDragoon";

    /// <summary>The military goods a nation spreads between its camps, in stable order — muskets first, then horses.</summary>
    private static readonly string[] MilitaryStockGoods = [MusketsId, HorsesId];

    /// <summary>
    /// Spreads <paramref name="nation"/>'s muskets/horses from a stocked, <b>calm</b> camp to a <b>threatened, bare</b>
    /// one (FreeCol <c>NativeAIPlayer.secureSettlements</c> arms-spreading via <c>tradeGoodsWithSettlement</c>): a tribe
    /// shouldn't leave its weapons idle in a peaceful village while a frontier camp under pressure has none to arm its
    /// braves with. For each military good (muskets then horses, deterministic), a <b>donor</b> is a settlement that is
    /// <em>not</em> threatened (alarm below <see cref="RaidAlarmThreshold"/>) and holds a <b>surplus</b> — more than
    /// <see cref="BraveEquipGoods"/> after keeping one half-unit for itself (i.e. ≥ <c>2 × BraveEquipGoods</c>); a
    /// <b>recipient</b> is a threatened settlement holding less than one half-unit of that good (it can't equip a brave
    /// from it). One half-unit (<see cref="BraveEquipGoods"/>) is shipped from the richest donor to the neediest
    /// recipient, repeating until no eligible donor/recipient pair remains. Settlements are ranked deterministically
    /// (stock then position), so the transfer is <b>RNG-free</b> — never the human's stream 0 — and a default game (every
    /// settlement holds zero stock → no donor, no transfer) stays byte-stable (ADR-009). The stock is the same transient
    /// non-serialized <see cref="NativeSettlement"/> field <see cref="TryEquipBrave"/> arms from, so this adds no save
    /// state. Run before <see cref="EquipBravesAtThreatenedSettlements"/> so a recipient can arm braves from the freshly
    /// delivered stock the same turn.
    /// </summary>
    internal void RedistributeArmsToThreatenedSettlements(Player nation)
    {
        List<NativeSettlement> nationSettlements = _nativeSettlements
            .Where(s => s.NationTypeId == nation.NationId)
            .ToList();
        if (nationSettlements.Count < 2)
        {
            return; // a one-camp nation has nobody to redistribute with
        }

        foreach (string good in MilitaryStockGoods)
        {
            // Repeatedly hand one half-unit from the richest calm donor (surplus ≥ 2 half-units) to the neediest
            // threatened recipient (< one half-unit), until no pair is left. Bounded by the finite total stock.
            while (true)
            {
                NativeSettlement? donor = nationSettlements
                    .Where(s => s.AlarmLevel < RaidAlarmThreshold && s.StockOf(good) >= 2 * BraveEquipGoods)
                    .OrderByDescending(s => s.StockOf(good)).ThenBy(s => s.Position.Y).ThenBy(s => s.Position.X)
                    .FirstOrDefault();
                NativeSettlement? recipient = nationSettlements
                    .Where(s => s.AlarmLevel >= RaidAlarmThreshold && s.StockOf(good) < BraveEquipGoods)
                    .OrderBy(s => s.StockOf(good)).ThenBy(s => s.Position.Y).ThenBy(s => s.Position.X)
                    .FirstOrDefault();
                if (donor is null || recipient is null)
                {
                    break;
                }
                donor.AddStock(good, -BraveEquipGoods);
                recipient.AddStock(good, BraveEquipGoods);
            }
        }
    }

    /// <summary>
    /// Equips/promotes the braves of every <em>threatened</em> settlement of <paramref name="nation"/> from that
    /// settlement's own military stock (FreeCol <c>NativeAIPlayer.secureSettlements</c> → <c>equipBraves</c>, run for
    /// each settlement at the start of its turn). Each alarmed settlement (alarm ≥ <see cref="RaidAlarmThreshold"/>,
    /// the secure trigger) secures its braves in <b>military-strength order</b> — the strongest-needed brave first
    /// (FreeCol's <c>getMilitaryStrengthComparator</c>) — so when stock is scarce the best warriors are armed first.
    /// Settlements and braves are iterated deterministically (by position / by id) and the equip itself is RNG-free, so
    /// securing draws nothing at all — never the human's stream 0. Securing never consumes a brave's action (it still
    /// raids/wanders this turn). A no-op when the nation holds no stock or no settlement is alarmed, so a default game
    /// stays byte-stable (ADR-009).
    /// </summary>
    private void EquipBravesAtThreatenedSettlements(Player nation)
    {
        foreach (NativeSettlement settlement in _nativeSettlements
            .Where(s => s.NationTypeId == nation.NationId && s.AlarmLevel >= RaidAlarmThreshold)
            .OrderBy(s => s.Position.Y).ThenBy(s => s.Position.X))
        {
            // The settlement's braves, strongest first (offence+defence, ties by id). FreeCol arms the strongest
            // promotable warriors first, so a partially-equipped strong brave reaches native dragoon before a weaker
            // one is armed at all when stock runs short.
            var braves = _units
                .Where(u => u.OwnerNationId == nation.NationId && u.IsOnMap && HomeSettlement(nation, u) == settlement)
                .OrderByDescending(u => OffenceBase(u) + DefenceBase(u)).ThenBy(u => u.Id)
                .ToList();
            foreach (Unit brave in braves)
            {
                TryEquipBrave(brave, settlement);
            }
        }
    }

    /// <summary>
    /// Improves one brave's military role by one step from its threatened home settlement's own goods stock (FreeCol
    /// <c>NativeAIPlayer.equipBraves</c> → <c>Settlement.canImproveUnitMilitaryRole</c>), charging only the
    /// <b>extra</b> equipment over its current role. From the unarmed default role the brave can become
    /// <see cref="ArmedBraveRoleId">armed</see> or <see cref="MountedBraveRoleId">mounted</see>; a partially-equipped
    /// armed or mounted brave is <b>promoted to full native dragoon</b> (<see cref="NativeDragoonRoleId"/>) by adding
    /// the missing half (the muskets/horses it lacks). Returns true when an upgrade happened. The brave always takes
    /// the <b>strongest role the camp can afford</b> in one step (FreeCol favours the full military role): an unarmed
    /// brave with both halves in stock becomes a dragoon outright, not a coin-flip between armed and mounted.
    /// <para>
    /// The equip is entirely <b>RNG-free</b> (the strongest-affordable choice is deterministic) — never the human's
    /// stream 0, so a default game (settlements hold no stock → this never fires) stays byte-stable (ADR-009).
    /// Equipping does not consume the brave's action.
    /// </para>
    /// </summary>
    private bool TryEquipBrave(Unit brave, NativeSettlement home)
    {
        if (brave.HasDefaultRole)
        {
            // Unarmed → take the strongest affordable role: dragoon if both halves are in stock, else the one half it
            // can afford, else nothing. (FreeCol promotes to the best military role the camp can provide.)
            bool canArm = home.StockOf(MusketsId) >= BraveEquipGoods;
            bool canMount = home.StockOf(HorsesId) >= BraveEquipGoods;
            if (canArm && canMount)
            {
                EquipFromSettlement(brave, home, NativeDragoonRoleId);
                return true;
            }
            if (!canArm && !canMount)
            {
                return false; // no stock the brave could equip from → settlement untouched
            }
            EquipFromSettlement(brave, home, canArm ? ArmedBraveRoleId : MountedBraveRoleId);
            return true;
        }

        // Already armed or mounted → promote to native dragoon if the missing half is in stock (FreeCol "promote
        // partially equipped units to full dragoon"). The delta is just the equipment it lacks.
        if (brave.RoleId is ArmedBraveRoleId or MountedBraveRoleId && CanAffordUpgrade(home, brave, NativeDragoonRoleId))
        {
            EquipFromSettlement(brave, home, NativeDragoonRoleId);
            return true;
        }
        return false; // already a dragoon, or the missing half isn't in stock → nothing to do
    }

    /// <summary>Goods units one brave role count requires (FreeCol armed/mounted brave = 25; pinned in the spec).</summary>
    private const int BraveEquipGoods = 25;

    /// <summary>Muskets goods id (the armed-brave equipment).</summary>
    private const string MusketsId = "model.goods.muskets";

    /// <summary>Horses goods id (the mounted-brave equipment).</summary>
    private const string HorsesId = "model.goods.horses";

    /// <summary>Whether <paramref name="home"/>'s stock can cover the <em>extra</em> equipment to move <paramref name="brave"/> from its current role to <paramref name="targetRoleId"/> (FreeCol <c>Settlement.canProvideGoods(getGoodsDifference(r, 1))</c>): every positively-consumed good in the delta must be in stock.</summary>
    private bool CanAffordUpgrade(NativeSettlement home, Unit brave, string targetRoleId) =>
        RoleGoodsDelta(brave, Ruleset.Role(targetRoleId)).All(d => d.Amount <= 0 || home.StockOf(d.GoodsId) >= d.Amount);

    /// <summary>
    /// Moves <paramref name="brave"/> into <paramref name="roleId"/>, deducting only the <b>extra</b> equipment over its
    /// current role from <paramref name="home"/>'s stock (FreeCol <c>getGoodsDifference</c> — so an armed→dragoon
    /// promotion charges only the horses it adds, not a fresh set of muskets), reusing the shared
    /// <see cref="RoleGoodsDelta"/>/<see cref="ChangeRole"/> mechanism (the same path combat capture uses to arm a
    /// brave). The upgrade persists via the brave's existing serialized role field (no save change).
    /// </summary>
    private void EquipFromSettlement(Unit brave, NativeSettlement home, string roleId)
    {
        foreach ((string goodsId, int amount) in RoleGoodsDelta(brave, Ruleset.Role(roleId)))
        {
            home.AddStock(goodsId, -amount); // positive = consumed; a negative delta (a downgrade) would refund
        }
        ChangeRole(brave, roleId, 1);
    }

    /// <summary>
    /// The tile of an undefended pillageable human colony this brave may raid right now — adjacent, ungarrisoned,
    /// with lootable goods, as gated by <see cref="CheckPillageColony"/> (ties broken by position), or null if
    /// none. Human-owned is the same sole target contract as <see cref="NearestHumanUnit"/> (natives raid the
    /// human only). The sibling of <see cref="AdjacentCapturableHumanColony"/> for the native side.
    /// </summary>
    private Position? AdjacentPillageableHumanColony(Unit brave) =>
        _colonies
            .Where(c => IsHumanOwned(c) && CheckPillageColony(brave, c.Position).Allowed)
            .OrderBy(c => c.Position.Y).ThenBy(c => c.Position.X)
            .Select(c => (Position?)c.Position)
            .FirstOrDefault();

    /// <summary>
    /// An adjacent human colony this brave can shake down for tribute right now — human-owned, next to the brave, and
    /// with something worth demanding (gold/goods/food) as judged by <see cref="SelectDemand"/> at the nation's alarm
    /// level — or null if none (ties broken by position). The non-violent sibling of
    /// <see cref="AdjacentPillageableHumanColony"/>; unlike pillage it does <b>not</b> require the colony to be
    /// undefended, so a garrisoned town can still be demanded of. Pure (no RNG).
    /// </summary>
    private Colony? AdjacentDemandableHumanColony(Player nation, Unit brave)
    {
        AlarmLevel level = HomeSettlement(nation, brave)?.AlarmLevel ?? AlarmLevel.Hateful;
        return _colonies
            .Where(c => IsHumanOwned(c) && brave.Position.IsAdjacentTo(c.Position) && SelectDemand(c, level) is not null)
            .OrderBy(c => c.Position.Y).ThenBy(c => c.Position.X)
            .FirstOrDefault();
    }

    /// <summary>
    /// Caps a demanded amount to FreeCol's range (<c>IndianDemandMission.capAmount</c>):
    /// <c>clamp(count * dx / 6, GOODS_DEMAND_MIN, CARGO_SIZE)</c> — at medium (dx = 3) this is
    /// <c>clamp(count / 2, 30, 100)</c>.
    /// </summary>
    private int CapDemand(int count) => Math.Clamp(count * NativeDemandsDx / 6, NativeDemandMin, NativeDemandMax);

    /// <summary>
    /// Picks what a brave demands of <paramref name="colony"/> at the demanding nation's tension
    /// <paramref name="level"/> (FreeCol <c>IndianDemandMission.selectGoods</c>), as a (goodsId, amount) pair —
    /// goodsId null means a gold demand — or null if the colony has nothing to give ("empty-handed"). The ladder:
    /// <list type="number">
    /// <item>Content or calmer with enough food banked → that food at the cutoff.</item>
    /// <item>Displeased or angrier → the priciest non-food, non-military storable stack (by market value).</item>
    /// <item>else the first present good by category, in order: military → building material → trade → refined.</item>
    /// <item>else the priciest storable stack.</item>
    /// <item>else gold: <c>gold/20</c>, or all the gold if that rounds to 0; a broke human yields nothing (null).</item>
    /// </list>
    /// Candidates are restricted to <b>storable</b> goods (our model never demands the non-warehoused
    /// hammers/bells), matching the pillage loot filter. Branch 1 is unreachable under the current Displeased+
    /// demand gate (a single per-settlement alarm channel stands in for FreeCol's split nation/settlement tension);
    /// it is implemented and tested for fidelity and would activate if that split is ever modelled. Pure (no RNG).
    /// <c>internal</c> only so the selection ladder can be unit-tested directly at every tension level (ADR-006).
    /// </summary>
    internal (string? GoodsId, int Amount)? SelectDemand(Colony colony, AlarmLevel level)
    {
        // (id, count) of the colony's storable goods, count > 0, deterministically ordered (the pillage loot set).
        var storable = PillageableGoods(colony).ToList();
        int StackValue(KeyValuePair<string, int> g) => g.Value * Market.BidPrice(g.Key);

        // 1) Calm-and-fed → food at the cutoff.
        int foodCutoff = CapDemand(colony.Food);
        if (level <= AlarmLevel.Content && colony.Food >= foodCutoff)
        {
            return (Colony.FoodId, foodCutoff);
        }

        // 2) Angry → the priciest non-food, non-military storable stack.
        if (level <= AlarmLevel.Displeased)
        {
            var angry = storable
                .Where(g => !Ruleset.Goods(g.Key).IsFood && !Ruleset.Goods(g.Key).IsMilitary)
                .OrderByDescending(StackValue).ThenBy(g => g.Key, StringComparer.Ordinal)
                .Cast<KeyValuePair<string, int>?>()
                .FirstOrDefault();
            if (angry is { } a)
            {
                return (a.Key, CapDemand(a.Value));
            }
        }

        // 3) First present good by category, FreeCol's order: military → building material → trade → refined.
        // FreeCol iterates goods in SPECIFICATION order within each rung (selectGoods uses getGoodsTypeList, first
        // present), so we do too — it matters for the refined rung when several refined goods are present.
        if (FirstInSpecOrder(colony, g => g.IsMilitary) is { } military)
        {
            return (military.Id, CapDemand(military.Count));
        }
        if (FirstInSpecOrder(colony, g => Ruleset.BuildingMaterials.Contains(g.Id)) is { } building)
        {
            return (building.Id, CapDemand(building.Count));
        }
        if (FirstInSpecOrder(colony, g => g.IsTradeGoods) is { } trade)
        {
            return (trade.Id, CapDemand(trade.Count));
        }
        if (FirstInSpecOrder(colony, g => g.MadeFrom is not null) is { } refined)
        {
            return (refined.Id, CapDemand(refined.Count));
        }

        // 4) Else the priciest storable stack of anything.
        var priciest = storable
            .OrderByDescending(StackValue).ThenBy(g => g.Key, StringComparer.Ordinal)
            .Cast<KeyValuePair<string, int>?>()
            .FirstOrDefault();
        if (priciest is { } p)
        {
            return (p.Key, CapDemand(p.Value));
        }

        // 5) No goods → gold (gold/20, else all the gold); a broke human yields nothing.
        int gold = HumanPlayer.Gold;
        if (gold < 1)
        {
            return null;
        }
        int twentieth = gold / 20;
        return (null, twentieth == 0 ? gold : twentieth);
    }

    /// <summary>
    /// The first storable goods type the colony holds (count &gt; 0) that matches <paramref name="match"/>, iterating
    /// in <b>specification order</b> — FreeCol's category-rung pick (<c>selectGoods</c> uses <c>getGoodsTypeList()</c>
    /// order, first present). Returns the (id, count) pair or null.
    /// </summary>
    private (string Id, int Count)? FirstInSpecOrder(Colony colony, Func<GoodsType, bool> match)
    {
        foreach (GoodsType goods in Ruleset.GoodsTypes)
        {
            int count = colony.StoreOf(goods.Id);
            if (count > 0 && goods.IsStorable && match(goods))
            {
                return (goods.Id, count);
            }
        }
        return null;
    }

    /// <summary>
    /// Creates a pending tribute demand from <paramref name="brave"/> against the human <paramref name="colony"/>
    /// (FreeCol <c>IndianDemandMission</c>): selects the goods/gold + amount via <see cref="SelectDemand"/> at the
    /// nation's home-settlement alarm level, stores it as the single pending demand, and ends the brave's turn. A
    /// no-op if a demand is already pending (one human-facing demand per ring — a blocking modal answers one) or the
    /// colony turns out to have nothing to give. RNG-free (the selection is deterministic), so it never touches the
    /// human's stream 0 (ADR-009). <c>internal</c> only so a specific demand can be staged deterministically in
    /// tests (ADR-006).
    /// </summary>
    internal void CreateNativeDemand(Player nation, Unit brave, Colony colony)
    {
        if (_pendingDemand is not null)
        {
            return;
        }
        AlarmLevel level = HomeSettlement(nation, brave)?.AlarmLevel ?? AlarmLevel.Hateful;
        if (SelectDemand(colony, level) is not { } demand)
        {
            return; // empty-handed — nothing to take
        }
        brave.MovementLeft = 0; // making the demand ends the brave's turn (as a raid would)
        _pendingDemand = new NativeDemand(
            nation.NationId!, colony.Id, colony.Name, demand.GoodsId, demand.Amount, colony.Position);
    }

    /// <summary>
    /// The human <b>pays</b> the pending tribute demand: transfers the demanded gold/goods out of the colony
    /// (consumed — we don't model a native treasury or goods-haul, as with pillage), capped at what the colony
    /// actually holds, then relieves the demanding nation's alarm across all its settlements
    /// (<see cref="NativeDemandAcceptAlarmRelief"/> = 150). A no-op returning false if no demand is pending, or the
    /// colony was captured/destroyed (or otherwise no longer human-owned) between the demand and the answer. RNG-free;
    /// clears the pending demand. (FreeCol <c>ServerPlayer.csCompleteNativeDemand</c>, accept branch.)
    /// </summary>
    /// <returns>True if tribute was paid; false if there was nothing to resolve.</returns>
    public bool AcceptPendingDemand()
    {
        if (_pendingDemand is not { } demand)
        {
            return false;
        }
        _pendingDemand = null;
        Colony? colony = _colonies.FirstOrDefault(c => c.Id == demand.ColonyId && IsHumanOwned(c));
        if (colony is null)
        {
            return false; // captured/destroyed before the human answered — nothing to pay
        }
        if (demand.GoodsId is null)
        {
            HumanPlayer.Gold -= Math.Min(demand.Amount, HumanPlayer.Gold); // never below 0
        }
        else
        {
            colony.AddGoods(demand.GoodsId, -Math.Min(demand.Amount, colony.StoreOf(demand.GoodsId)));
        }
        // Appeased across the nation. FreeCol lands the full −150 on the brave's home settlement and propagates a
        // halved −75 to the others (via player-level tension); we flatten to −150 on every settlement — the same
        // single-per-settlement-alarm simplification ApplyNativeCombatTension already makes (we model no nation tension).
        foreach (NativeSettlement settlement in _nativeSettlements.Where(s => s.NationTypeId == demand.DemandingNationId))
        {
            ChangeNativeAlarm(settlement, -NativeDemandAcceptAlarmRelief);
        }
        return true;
    }

    /// <summary>
    /// The human <b>refuses</b> the pending tribute demand (or ends the turn without answering): clears it with no
    /// transfer and no tension change. FreeCol changes tension only on accept — the refusal's consequence is the
    /// brave's normal next-turn raid/pillage via the native AI (it is hostile and beside the colony), not a direct
    /// alarm bump. A no-op if none is pending. RNG-free.
    /// </summary>
    public void RefusePendingDemand() => _pendingDemand = null;

    /// <summary>One legal random neighbour for an idle brave to wander to (drawn from the nation's own stream), or null if hemmed in.</summary>
    private Position? Wander(Player player, Unit brave)
    {
        var steps = brave.Position.Neighbours()
            .Where(n => CheckMove(brave, n).Allowed)
            .OrderBy(n => n.Y).ThenBy(n => n.X)
            .ToList();
        return steps.Count == 0 ? null : steps[RandomFor(player).Next(steps.Count)];
    }

    /// <summary>A scored seek-and-destroy target for the foreign-power war AI (FP-6a): a human unit's tile or a human colony, with its score.</summary>
    internal readonly record struct ScoredTarget(Position Position, bool IsColony, int Score);

    /// <summary>FreeCol <c>UnitSeekAndDestroyMission</c> base target value (1020) and the per-distance penalty (100·turns; we use Chebyshev tiles for turns).</summary>
    private const int SeekBaseValue = 1020;
    private const int SeekDistancePenalty = 100;

    /// <summary>The offensive seek-and-destroy range ladder (FreeCol's 8/12/16): the first gate yielding any eligible target wins.</summary>
    private static readonly int[] SeekRangeLadder = [8, 12, 16];

    /// <summary>
    /// The best scored attack target for an armed foreign-power unit at war with the human (FP-6a, FreeCol
    /// <c>UnitSeekAndDestroyMission</c> + <c>getSeekAndDestroyMission</c>'s 8/12/16 ladder): searches human unit-tiles
    /// (same domain) and — for a land unit — human colonies within an escalating Chebyshev range, scoring each by
    /// <see cref="ScoreUnitTarget"/> / <see cref="ScoreColonyTarget"/> (value minus distance; weak/valuable/treasure
    /// targets favoured; fortifications avoided). The first range gate that yields any target wins; ties resolve by a
    /// stable candidate order (units by id, colonies by position) with no RNG, so the human's stream 0 is untouched.
    /// Null when nothing is reachable within the widest gate (the caller then besieges the nearest colony, as before).
    /// </summary>
    internal ScoredTarget? PickAttackTarget(Unit unit)
    {
        foreach (int range in SeekRangeLadder)
        {
            if (BestTargetWithin(unit, range) is { } best)
            {
                return best;
            }
        }
        return null;
    }

    /// <summary>The strict-max scored target within a Chebyshev <paramref name="range"/>, or null if none is eligible (see <see cref="PickAttackTarget"/>).</summary>
    private ScoredTarget? BestTargetWithin(Unit unit, int range)
    {
        ScoredTarget? best = null;
        // Human unit-tile candidates (same domain), one per occupied tile (scored vs its strongest defender), in stable
        // id order so the first top-scorer wins on a tie.
        foreach (Position tile in _units
                     .Where(u => u.IsOnMap && IsHumanOwned(u) && u.Type.IsNaval == unit.Type.IsNaval
                                 && Chebyshev(u.Position, unit.Position) <= range)
                     .OrderBy(u => u.Id)
                     .Select(u => u.Position)
                     .Distinct())
        {
            int score = ScoreUnitTarget(unit, tile);
            if (score != int.MinValue && (best is null || score > best.Value.Score))
            {
                best = new ScoredTarget(tile, IsColony: false, score);
            }
        }
        // Human colony candidates — a land unit only (naval can't besiege/capture), in stable position order. A
        // GARRISONED colony is excluded (its garrison is a unit-tile candidate, fought first — like the adjacent path):
        // scoring it as a capture target would let it outscore its own garrison, then `CheckAttackColony` rejects the
        // capture and the unit wanders off without ever engaging (the seek-and-destroy garrison-first rule).
        if (!unit.Type.IsNaval)
        {
            foreach (Colony colony in _colonies
                         .Where(c => IsHumanOwned(c) && Chebyshev(c.Position, unit.Position) <= range
                                     && !_units.Any(u => u.IsOnMap && u.Position == c.Position))
                         .OrderBy(c => c.Position.Y).ThenBy(c => c.Position.X))
            {
                int score = ScoreColonyTarget(unit, colony);
                if (best is null || score > best.Value.Score)
                {
                    best = new ScoredTarget(colony.Position, IsColony: true, score);
                }
            }
        }
        return best;
    }

    /// <summary>
    /// Scores a human unit-tile as a seek-and-destroy target (FreeCol <c>scoreUnitPath</c>): <c>1020 − 100·d</c> plus
    /// the <b>truncated</b> relative-power term <c>(int)(100·(off − def))</c> against the tile's strongest defender,
    /// plus +1000 per treasure train on the tile and +500 for a naval defender caught on land. <see cref="int.MinValue"/>
    /// (skip) when the attacker has no offence or the tile has no fightable defender.
    /// </summary>
    private int ScoreUnitTarget(Unit attacker, Position tile)
    {
        double off = OffenceBase(attacker);
        if (off <= 0 || DefenderAt(attacker, tile) is not { } defender)
        {
            return int.MinValue;
        }
        int score = SeekBaseValue - SeekDistancePenalty * Chebyshev(attacker.Position, tile);
        score += (int)(100 * (off - DefencePowerOf(attacker, defender, tile))); // truncate, per FreeCol
        score += 1000 * _units.Count(u => u.IsOnMap && u.Position == tile && IsHumanOwned(u) && u.TreasureAmount > 0);
        if (defender.Type.IsNaval && !Map.TerrainAt(tile).IsWater)
        {
            score += 500; // a ship caught on a land tile is an easy kill (FreeCol)
        }
        return score;
    }

    /// <summary>
    /// Scores a human colony as a seek-and-destroy target (FreeCol <c>scoreSettlementPath</c> + <c>calculateSettlementValue</c>):
    /// <c>1020 − 100·d</c> plus the <b>rounded</b> <c>round(50·off)</c> attacker-strength term, plus the colony's
    /// population (loot), minus <c>200 × stockade level</c> (stockade 1 / fort 2 / fortress 3 → 200/400/600 — avoid
    /// fortifications, matching FreeCol exactly; note this is the settlement-VALUE penalty, distinct from the combat
    /// defence-bonus %). The settlement term rounds where the unit term truncates — a real FreeCol asymmetry.
    /// </summary>
    private int ScoreColonyTarget(Unit attacker, Colony colony)
    {
        int score = SeekBaseValue - SeekDistancePenalty * Chebyshev(attacker.Position, colony.Position);
        score += (int)Math.Round(50 * OffenceBase(attacker), MidpointRounding.AwayFromZero); // round, per FreeCol
        score += colony.Population;
        score -= 200 * StockadeLevel(colony); // FreeCol Colony.calculateSettlementValue: −200·stockade level
        return score;
    }

    /// <summary>A colony's fortification tier (FreeCol stockade <c>level</c>): stockade 1, fort 2, fortress 3, none 0.</summary>
    private static int StockadeLevel(Colony colony) =>
        colony.HasBuilding("model.building.fortress") ? 3
        : colony.HasBuilding("model.building.fort") ? 2
        : colony.HasBuilding("model.building.stockade") ? 1
        : 0;

    /// <summary>
    /// The nearest on-map human-owned unit the <paramref name="hunter"/> can fight (Chebyshev, ties broken by
    /// position), or null if none. Two filters: (1) human-owned — the <b>sole contract</b> keeping the AI
    /// attacking the human only (the engine's <see cref="CheckAttack"/>/<see cref="DefenderAt"/> gate on
    /// owner-inequality would also admit other rivals); (2) <b>same domain</b> — a ship hunts ships, a land unit
    /// hunts land units (naval and land combat don't mix), so a warship never chases an unreachable land unit and
    /// a brave never chases a ship. (Still used by the native raid AI; the foreign-power war AI now uses the scored
    /// <see cref="PickAttackTarget"/>.)
    /// </summary>
    private Unit? NearestHumanUnit(Unit hunter) =>
        _units.Where(u => u.IsOnMap && IsHumanOwned(u) && u.Type.IsNaval == hunter.Type.IsNaval)
            .OrderBy(u => Chebyshev(u.Position, hunter.Position))
            .ThenBy(u => u.Position.Y).ThenBy(u => u.Position.X)
            .FirstOrDefault();

    /// <summary>A native settlement on or adjacent to <paramref name="unit"/> whose chief <paramref name="power"/> may speak with now (not yet visited by this power, per <see cref="CheckVisit"/>), by stable position order — the AI scout-chief target. Null when none.</summary>
    private NativeSettlement? AdjacentUnvisitedSettlement(Player power, Unit unit) =>
        _nativeSettlements
            .Where(s => (s.Position == unit.Position || s.Position.IsAdjacentTo(unit.Position)) && CheckVisit(unit, s).Allowed)
            .OrderBy(s => s.Position.Y).ThenBy(s => s.Position.X)
            .FirstOrDefault();

    /// <summary>
    /// The native settlement <paramref name="unit"/> (a missionary-role colonist) may found a mission in this turn — on
    /// or adjacent to it (<see cref="CheckEstablishMission"/> passes, so the unit carries the missionary ability and has
    /// movement) and the power does <b>not already hold a mission there</b> (so it never re-founds its own mission;
    /// another power's mission is replaceable, matching <see cref="EstablishMission(Player, Unit, NativeSettlement)"/>).
    /// Ties break by position. The AI missionary target for <see cref="RunForeignPowerTurn"/> (`86d3c9vta` missionary half).
    /// </summary>
    private NativeSettlement? AdjacentUnmissionedSettlement(Player power, Unit unit) =>
        _nativeSettlements
            .Where(s => s.MissionOwnerId != power.PlayerId && CheckEstablishMission(unit, s).Allowed)
            .OrderBy(s => s.Position.Y).ThenBy(s => s.Position.X)
            .FirstOrDefault();

    /// <summary>
    /// The tile of the nearest native settlement <paramref name="power"/> has <b>discovered</b> (its position is in the
    /// power's fog) but whose chief it has <b>not yet spoken with</b> (<see cref="NativeSettlement.HasBeenVisitedBy"/> the
    /// power) — by Chebyshev from <paramref name="unit"/>, ties by position — or null when there is none. The distant
    /// equivalent of <see cref="AdjacentUnvisitedSettlement"/> that the AI scout heads toward (`86d3c9vta` scout facet),
    /// fog-gated so the AI never beelines for a settlement it hasn't seen.
    /// </summary>
    private Position? NearestUnvisitedSettlement(Player power, Unit unit) =>
        _nativeSettlements
            .Where(s => power.Explored.Contains(s.Position) && !s.HasBeenVisitedBy(power.PlayerId))
            .OrderBy(s => Chebyshev(s.Position, unit.Position)).ThenBy(s => s.Position.Y).ThenBy(s => s.Position.X)
            .Select(s => (Position?)s.Position)
            .FirstOrDefault();

    /// <summary>
    /// The human unit-tile an alarmed <paramref name="brave"/> raids — FreeCol's <c>UnitSeekAndDestroyMission</c>
    /// applied to natives (the brave's "displeasure attack weighting"): the best-scored human <b>land</b> unit-tile
    /// within the 8/12/16 Chebyshev range ladder by the same <see cref="ScoreUnitTarget"/> value−distance heuristic the
    /// foreign-power war AI uses — so an angry brave goes for the <b>soft, valuable</b> target (an unarmed colonist, a
    /// treasure escort) over a dug-in soldier, not just the nearest body. Colonies are handled separately (pillage /
    /// demand). Falls back to the nearest human unit at any distance (the prior behaviour) when nothing is in range, so a
    /// far-off war never lets a hostile brave idle. Pure (no RNG); only ever called on the native's own turn.
    /// </summary>
    private Position? PickRaidTarget(Unit brave)
    {
        foreach (int range in SeekRangeLadder)
        {
            Position? best = null;
            int bestScore = int.MinValue;
            foreach (Position tile in _units
                         .Where(u => u.IsOnMap && IsHumanOwned(u) && !u.Type.IsNaval
                                     && Chebyshev(u.Position, brave.Position) <= range)
                         .OrderBy(u => u.Id).Select(u => u.Position).Distinct())
            {
                int score = ScoreUnitTarget(brave, tile);
                if (score != int.MinValue && score > bestScore)
                {
                    bestScore = score;
                    best = tile;
                }
            }
            if (best is { } found)
            {
                return found;
            }
        }
        return NearestHumanUnit(brave)?.Position; // out of seek range → close on the nearest (prior behaviour)
    }

    /// <summary>
    /// The nearest human colony to <paramref name="unit"/> (Chebyshev, ties by position), or null if the human has
    /// none — the besiege target for a war-power land unit with no field unit to chase (86d3bx03d). Human-owned is
    /// the same sole contract as <see cref="NearestHumanUnit"/>; pure (no RNG). Targets any human colony (an
    /// undefended one is then captured on arrival; a garrisoned one's garrison becomes the nearest field unit).
    /// </summary>
    private Colony? NearestHumanColony(Unit unit) =>
        _colonies.Where(IsHumanOwned)
            .OrderBy(c => Chebyshev(c.Position, unit.Position))
            .ThenBy(c => c.Position.Y).ThenBy(c => c.Position.X)
            .FirstOrDefault();

    /// <summary>The nearest colony of <paramref name="owner"/> within <paramref name="maxDistance"/> (Chebyshev) of <paramref name="origin"/>, or null (used to muster a mission convert; the owner may be any colonial player).</summary>
    private Colony? NearestColonyOf(Player owner, Position origin, int maxDistance) =>
        ColoniesOf(owner)
            .Where(c => Chebyshev(c.Position, origin) <= maxDistance)
            .OrderBy(c => Chebyshev(c.Position, origin))
            .ThenBy(c => c.Position.Y).ThenBy(c => c.Position.X)
            .FirstOrDefault();

    /// <summary>The brave's home settlement — the nearest surviving settlement of its own nation (Chebyshev, ties by position), or null if the nation has lost them all.</summary>
    private NativeSettlement? HomeSettlement(Player player, Unit brave) =>
        _nativeSettlements.Where(s => s.NationTypeId == player.NationId)
            .OrderBy(s => Chebyshev(s.Position, brave.Position))
            .ThenBy(s => s.Position.Y).ThenBy(s => s.Position.X)
            .FirstOrDefault();

    /// <summary>
    /// Resolves a brave's raid on the human unit at <paramref name="target"/> through the nation's OWN RNG
    /// stream (never stream 0), recording a <see cref="CombatNotice"/> so the presentation can tell the player.
    /// The defender is human-owned (the caller filtered to <see cref="IsHumanOwned(Unit)"/>), so the native-alarm path
    /// in <see cref="Attack(Unit, Position, Randomness.IGameRandom)"/> is skipped — a raid never raises the
    /// raider's own nation's alarm.
    /// </summary>
    private void RaidHumanUnit(Player player, Unit brave, Position target)
    {
        // The pathing picked the nearest human as prey; Attack (via DefenderAt) resolves against the strongest
        // defender on that tile — all human, since CheckMove forbids a brave from co-locating with an enemy.
        Unit defender = DefenderAt(brave, target)!;          // human-owned (filtered upstream)
        string defenderTypeId = defender.Type.Id;            // capture before the attack — a beaten loser is removed
        CombatResult result = Attack(brave, target, RandomFor(player)); // INTERNAL overload → the nation's stream
        _combatNotices.Add(new CombatNotice(player.NationId!, defenderTypeId, result, target));
    }

    /// <summary>
    /// Grants any free buildings the player's elected fathers confer (FreeCol <c>model.event.freeBuilding</c> /
    /// <c>csFreeBuilding</c>): <b>La Salle</b> gives every colony at or above the building's required population a
    /// free <c>model.building.stockade</c> (it doesn't already have). Run each turn after election and colony
    /// growth, so it fires both when the father is elected (existing big colonies) and when a colony later reaches
    /// the population (FreeCol's per-turn pass). Idempotent (skips a colony that already has the building),
    /// RNG-free, and iterated in stable colony-id order — so it never perturbs the human's stream 0 (ADR-009).
    /// The matching <c>buildingPriceBonus −100%</c> (a free <em>manual</em> rebuild) is not modelled — the event
    /// already grants the stockade outright. A granted stockade persists in the colony's building list (no
    /// save-format change; older saves without La Salle simply grant nothing).
    /// </summary>
    private void ApplyFreeBuildings(Player player)
    {
        foreach (string fatherId in player.Congress)
        {
            foreach (string buildingId in Ruleset.Father(fatherId).FreeBuildings)
            {
                BuildingType building = Ruleset.Building(buildingId);
                foreach (Colony colony in ColoniesOf(player).OrderBy(c => c.Id))
                {
                    if (colony.Population >= building.RequiredPopulation && !colony.HasBuilding(buildingId))
                    {
                        colony.AddBuilding(buildingId);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Converts each colony's freshly-produced bells into player liberty, elects
    /// the chosen father once enough is banked, and refreshes the offered set.
    /// </summary>
    private void AccumulateLibertyAndElectFathers(Player player)
    {
        foreach (Colony colony in ColoniesOf(player))
        {
            int bells = colony.StoreOf(BellsId);
            if (bells > 0)
            {
                colony.AddGoods(BellsId, -bells); // bells become liberty, not tradeable stock
                // The colony's printing press / newspaper boosts its bell output first (+50% / +100%), plus any
                // active Boston-Tea-Party surge (+50% decaying), then the founding-father bonus (Jefferson/Paine)
                // applies, then each colonist past the first two consumes 1 bell of upkeep — so a colony that grows
                // faster than its bell output loses liberty (its Sons of Liberty can fall). FreeCol feeds the same
                // net figure to both pools.
                int boostPercent = BellProductionBonus(colony) + colony.TeaPartyBellBonusPercent;
                int boosted = bells + (bells * boostPercent / 100);
                int net = ApplyGoodsModifiers(player, BellsId, boosted) - Math.Max(0, colony.Population - Ruleset.Difficulty.UnitsThatUseNoBells);
                player.Liberty += net;   // banked toward the next founding father
                colony.AddLiberty(net);  // the colony's own Sons-of-Liberty liberty (AddLiberty floors at 0)
            }
            colony.TickTeaPartyBonus(); // decay the tea-party surge each turn, even on a no-bell turn
        }
        player.Liberty = Math.Max(0, player.Liberty); // a net-negative bell turn can't push the founding-father pool below 0

        if (player.CurrentFather is not null && player.Liberty >= TotalFoundingFatherCost(player))
        {
            string elected = player.CurrentFather; // capture before it is cleared
            player.Liberty -= TotalFoundingFatherCost(player);
            player.CongressList.Add(elected);
            if (player.IsHuman)
            {
                RecordHistory(HistoryEventKind.FatherElected, $"{FatherDisplayName(elected)} joined the Continental Congress.");
            }
            player.CurrentFather = null;
            player.OfferedFathersList.Clear();
            RefreshDockForRecruitability(player); // a newly-elected father may ban dock recruits (Brewster)
            RefreshSonsOfLibertyModifiers();       // a newly-elected father may grant a standing SoL bonus (Bolívar +20)
            foreach (string freeUnit in Ruleset.Father(elected).FreeUnits)
            {
                SpawnInEurope(freeUnit, null, player.PlayerId); // a one-time free unit on election — John Paul Jones → a frigate in Europe
            }
            if (Ruleset.Father(elected).LiftsBoycotts)
            {
                player.Market.LiftAllBoycotts(); // Jacob Fugger (model.event.boycottsLifted) — all the player's boycotts end
            }
            if (Ruleset.Father(elected).RevealsAllColonies)
            {
                foreach (Colony c in _colonies)
                {
                    RevealAround(player, c.Position, 1); // Francisco de Coronado (model.event.seeAllColonies) — every colony + its ring revealed
                }
            }
            if (Ruleset.Father(elected).Abilities.Any(a => a.Id == UpgradeConvertAbility && a.Value))
            {
                // Bartolomé de las Casas (model.ability.upgradeConvert): every native convert the player holds becomes a free colonist.
                foreach (Unit convert in _units.Where(u => u.OwnerId == player.PlayerId && u.Type.Id == IndianConvertUnitTypeId).ToList())
                {
                    UpgradeUnitType(convert, Colony.FreeColonistTypeId);
                }
            }
            if (player.IsHuman && elected == PocahontasId)
            {
                ResetAllNativeAlarm(); // FreeCol model.event.resetNativeAlarm — all native anger toward you forgotten
            }
        }

        if (player.CurrentFather is null && player.OfferedFathers.Count == 0)
        {
            GenerateOffers(player);
        }
    }

    /// <summary>
    /// Offers one eligible father per category, picked by seeded weight for the
    /// current age (already-elected fathers and zero-weight ones are excluded).
    /// </summary>
    private void GenerateOffers(Player player)
    {
        player.OfferedFathersList.Clear();
        int age = CurrentAge;
        var elected = player.Congress.ToHashSet();

        foreach (FatherType type in Enum.GetValues<FatherType>())
        {
            var candidates = Ruleset.FoundingFathers
                .Where(f => f.Type == type && !elected.Contains(f.Id) && f.WeightForAge(age) > 0)
                .ToList();
            if (candidates.Count == 0)
            {
                continue;
            }
            int totalWeight = candidates.Sum(f => f.WeightForAge(age));
            int roll = RandomFor(player).Next(totalWeight);
            foreach (FoundingFather f in candidates)
            {
                roll -= f.WeightForAge(age);
                if (roll < 0)
                {
                    player.OfferedFathersList.Add(f.Id);
                    break;
                }
            }
        }
    }

    /// <summary>The player's standing Sons-of-Liberty %-modifier from Congress (Simón Bolívar's +20, FreeCol <c>model.modifier.SoL</c> additive); 0 without such a father.</summary>
    private int SolModifierFor(Player player) =>
        player.Congress
            .SelectMany(f => Ruleset.Father(f).Modifiers)
            .Where(m => m.TargetId == SonsOfLibertyModifierId)
            .Sum(m => (int)m.Value);

    /// <summary>
    /// Refreshes every colony's <see cref="Colony.SolModifierBonus"/> from its owner's Congress. Bolívar's +20 is a
    /// standing modifier on the SoL percentage (FreeCol), not a one-time liberty bake, so it is re-derived whenever
    /// Congress could have changed — on election and on load — and stays correct as colonies grow or starve.
    /// </summary>
    private void RefreshSonsOfLibertyModifiers()
    {
        foreach (Colony colony in _colonies)
        {
            colony.SolModifierBonus = PlayerById(colony.OwnerId) is { } owner ? SolModifierFor(owner) : 0;
        }
    }

    /// <summary>The ability by which Thomas Paine adds the tax rate as a bell bonus.</summary>
    private const string AddTaxToBellsAbility = "model.ability.addTaxToBells";

    /// <summary>The ability gating which unit types may be recruited (William Brewster denies some).</summary>
    private const string CanRecruitUnitAbility = "model.ability.canRecruitUnit";

    /// <summary>Pocahontas's id — on election she zeroes all native alarm (the <c>resetNativeAlarm</c> event).</summary>
    private const string PocahontasId = "model.foundingFather.pocahontas";

    /// <summary>The Sons-of-Liberty percentage modifier (additive). Among fathers only Simón Bolívar carries it (+20, applied to every one of his player's colonies' SoL%).</summary>
    private const string SonsOfLibertyModifierId = "model.modifier.SoL";

    /// <summary>The percentage modifier by which Pocahontas damps native-alarm increases (FreeCol <c>NATIVE_ALARM_MODIFIER</c>, −50%).</summary>
    private const string NativeAlarmModifierId = "model.modifier.nativeAlarmModifier";

    /// <summary>The movement-point modifier (additive). Among fathers only Magellan carries it (+3, naval-scoped).</summary>
    private const string MovementBonusId = "model.modifier.movementBonus";

    /// <summary>The high-seas sail-turn modifier (additive). Magellan's −1 shortens the crossing.</summary>
    private const string SailHighSeasId = "model.modifier.sailHighSeas";

    /// <summary>
    /// Scales a positive native-alarm <em>gain</em> by the human's <see cref="NativeAlarmModifierId"/> modifiers — both
    /// the elected-father one (Pocahontas −50%) and the player's <b>nation-type advantage</b> (the French
    /// <c>model.nationType.cooperation</c> −50%), stacked. Gains only — goodwill and decay (negative
    /// deltas) pass through unchanged. Applied to the per-turn <b>ambient</b> proximity alarm
    /// (<see cref="ApplyAmbientNativeAlarm"/>), matching FreeCol (<c>ServerPlayer.csNewTurn</c>); combat tension is
    /// raw (<see cref="ApplyNativeCombatTension"/>).
    /// </summary>
    private int ScaleNativeAlarmGain(int delta)
    {
        if (delta <= 0)
        {
            return delta;
        }
        // Two damping sources, stacked as FreeCol stacks them: the founding-father modifier (Pocahontas −50%) and the
        // player's nation-type advantage (the French — model.nationType.cooperation — −50%); the human's nation is null
        // by default, so a default game folds only what's in Congress.
        double scaled = delta;
        foreach (FatherModifier modifier in _human.Congress.Select(Ruleset.Father)
                     .SelectMany(f => f.Modifiers)
                     .Where(m => m.TargetId == NativeAlarmModifierId)
                     .Concat(NationTypeModifiers(_human, NativeAlarmModifierId)))
        {
            scaled = modifier.ApplyTo(scaled);
        }
        return (int)scaled;
    }

    /// <summary>Zeroes every native settlement's alarm toward the human (FreeCol <c>resetNativeAlarm</c> → <c>Tension.TENSION_MIN</c>, the Happy band).</summary>
    private void ResetAllNativeAlarm()
    {
        foreach (NativeSettlement settlement in _nativeSettlements)
        {
            settlement.Alarm = 0;
        }
    }

    /// <summary>
    /// True when any Founding Father elected to the human player's Congress grants <paramref name="abilityId"/>
    /// (presentation/test helper; the rules fold abilities per acting player via <see cref="HasAbilityFor"/>, FP-5).
    /// </summary>
    public bool HasAbility(string abilityId) => HasAbilityFor(_human, abilityId);

    /// <summary>Applies the human player's elected Founding Fathers' production modifiers (presentation/test helper).</summary>
    public int ApplyGoodsModifiers(string goodsId, int baseAmount) => ApplyGoodsModifiers(_human, goodsId, baseAmount);

    /// <summary>
    /// Applies <paramref name="player"/>'s elected Founding Fathers' production modifiers for a goods type to a
    /// base amount (FreeCol <c>FeatureContainer.applyModifiers</c>: ascending index, then fold; truncated to
    /// int). Thomas Paine's <c>addTaxToBells</c> adds that player's tax rate as a bell percentage. Each player
    /// folds its OWN Congress (FP-5), so a foreign power's economy is independent of the human's fathers; with
    /// no relevant fathers elected the base is returned unchanged.
    /// </summary>
    internal int ApplyGoodsModifiers(Player player, string goodsId, int baseAmount)
    {
        var modifiers = player.Congress.Select(Ruleset.Father)
            .SelectMany(f => f.Modifiers)
            .Where(m => m.TargetId == goodsId)
            .ToList();
        if (goodsId == BellsId && HasAbilityFor(player, AddTaxToBellsAbility))
        {
            // Paine: the spec template modifier (index 40) takes the current tax rate as its value.
            modifiers.Add(new FatherModifier(BellsId, ModifierType.Percentage, player.TaxRate, 40));
        }
        if (modifiers.Count == 0)
        {
            return baseAmount;
        }
        double result = baseAmount;
        foreach (FatherModifier modifier in modifiers.OrderBy(m => m.Index))
        {
            result = modifier.ApplyTo(result);
        }
        return (int)result; // truncate, matching FreeCol's (int) cast on production
    }

    /// <summary>Whether a unit type may currently be recruited by <paramref name="player"/> (probability &gt; 0 and not denied by a father).</summary>
    private bool IsRecruitable(Player player, UnitType type) =>
        type.RecruitProbability > 0 && !IsRecruitBlocked(player, type.Id);

    /// <summary>True when a father elected by <paramref name="player"/> denies recruiting this unit type (William Brewster).</summary>
    private bool IsRecruitBlocked(Player player, string unitTypeId) =>
        player.Congress.Select(Ruleset.Father)
            .SelectMany(f => f.Abilities)
            .Any(a => a.Id == CanRecruitUnitAbility && !a.Value && a.ScopeTypes.Contains(unitTypeId));

    /// <summary>
    /// Replaces any dock recruit a newly-elected father now forbids (e.g. Brewster's scum ban) — the
    /// <c>model.event.newRecruits</c> event (FreeCol <c>ServerEurope.replaceRecruits</c>: drop the recruits the
    /// player can no longer recruit, then refill). A fresh draw never re-rolls a banned type (it goes through
    /// <see cref="IsRecruitable"/>), so the dock ends with only legal recruits.
    /// </summary>
    private void RefreshDockForRecruitability(Player player)
    {
        for (int i = 0; i < player.RecruitDock.Count; i++)
        {
            if (IsRecruitBlocked(player, player.RecruitDock[i]))
            {
                player.RecruitDockList[i] = DrawRecruitType(player);
            }
        }
    }

    // ─────────────────────────── Founding-father diplomacy (Franklin, Jan de Witt) ───────────────────────────

    /// <summary>Benjamin Franklin's ability (FreeCol <c>model.ability.alwaysOfferedPeace</c>): a European power at war with this player always accepts/offers peace — the AI never scores a peace/cease-fire/alliance clause from a Franklin holder as a cost or a refusal.</summary>
    private const string AlwaysOfferedPeaceAbility = "model.ability.alwaysOfferedPeace";

    /// <summary>Jan de Witt's ability (FreeCol <c>model.ability.tradeWithForeignColonies</c>): this player may trade with rival nations' colonies.</summary>
    private const string TradeWithForeignColoniesAbility = "model.ability.tradeWithForeignColonies";

    /// <summary>Jan de Witt's ability (FreeCol <c>model.ability.customHouseTradesWithForeignCountries</c>): this player's custom houses may sell to foreign markets even when a good is boycotted, provided he is at peace with a European power.</summary>
    private const string CustomHouseForeignTradeAbility = "model.ability.customHouseTradesWithForeignCountries";

    /// <summary>Jan de Witt's ability (FreeCol <c>model.ability.betterForeignAffairsReport</c>): this player's foreign-affairs report reveals every rival nation's diplomatic stance.</summary>
    private const string BetterForeignAffairsReportAbility = "model.ability.betterForeignAffairsReport";

    /// <summary>
    /// Whether the European power offering a peace clause to the evaluator holds Benjamin Franklin's
    /// <c>alwaysOfferedPeace</c> (FreeCol <c>EuropeanAIPlayer.acceptDiplomaticTrade</c>'s <c>franklin</c> branch):
    /// <paramref name="otherId"/> is the trade's other party, and when it holds the ability a peace/cease-fire/alliance
    /// stance clause is forced neutral (scored 0) for the evaluator — so a Franklin power's peace is always accepted.
    /// Pure; draws no RNG.
    /// </summary>
    internal bool OtherPartyAlwaysOffersPeace(int otherId) =>
        PlayerById(otherId) is { } other && HasAbilityFor(other, AlwaysOfferedPeaceAbility);

    /// <summary>
    /// Whether the colonial player <paramref name="playerId"/> may trade with foreign (rival) colonies — Jan de Witt's
    /// <c>tradeWithForeignColonies</c> ability (FreeCol <c>Player.hasAbility(TRADE_WITH_FOREIGN_COLONIES)</c>). The
    /// GameLogic oracle the trade-with-rival-settlement path (and a future trade UI) gates on; false for every player
    /// until de Witt sits in their Congress, so the default game is unaffected.
    /// </summary>
    public bool CanTradeWithForeignColonies(int playerId) =>
        PlayerById(playerId) is { PlayerType: PlayerType.Colonial } p && HasAbilityFor(p, TradeWithForeignColoniesAbility);

    /// <summary>
    /// Whether <paramref name="playerId"/>'s custom houses may sell to foreign markets — Jan de Witt's
    /// <c>customHouseTradesWithForeignCountries</c> ability, and (faithful to FreeCol <c>Player.canTrade</c>) only while
    /// he is at <see cref="Stance.Peace"/> or <see cref="Stance.Alliance"/> with at least one other European power. The
    /// oracle that will let a custom house auto-sell a boycotted good once the custom-house boycott gate lands; false
    /// for every player until de Witt is elected, so the default game's custom-house behaviour is unchanged.
    /// </summary>
    public bool CustomHouseTradesWithForeignCountries(int playerId) =>
        PlayerById(playerId) is { PlayerType: PlayerType.Colonial } p
        && HasAbilityFor(p, CustomHouseForeignTradeAbility)
        && _players.Any(o => o.PlayerId != p.PlayerId && o.PlayerType == PlayerType.Colonial
            && p.Stances.GetValueOrDefault(o.PlayerId) is Stance.Peace or Stance.Alliance);

    /// <summary>
    /// Whether <paramref name="playerId"/> sees the full foreign-affairs report — Jan de Witt's
    /// <c>betterForeignAffairsReport</c> ability (FreeCol <c>Player.hasAbility(BETTER_FOREIGN_AFFAIRS_REPORT)</c>).
    /// The report UI is a separate P7 task; this exposes the GameLogic flag and <see cref="ForeignNationStances"/>
    /// supplies the data. False until de Witt is elected.
    /// </summary>
    public bool HasBetterForeignAffairsReport(int playerId) =>
        PlayerById(playerId) is { PlayerType: PlayerType.Colonial } p && HasAbilityFor(p, BetterForeignAffairsReportAbility);

    /// <summary>
    /// The diplomatic stance <paramref name="playerId"/> holds toward every <em>other</em> colonial power — the data
    /// behind Jan de Witt's foreign-affairs report (the rival nations' stances his <c>betterForeignAffairsReport</c>
    /// reveals). Returns each rival's player id with the stance from <paramref name="playerId"/>'s point of view, in
    /// stable player-id order. Read-only (ADR-006); empty for a non-colonial player. The values are always available —
    /// de Witt unlocks the report <em>presentation</em> (see <see cref="HasBetterForeignAffairsReport"/>); this oracle
    /// is the raw stance data the report (or any caller) reads.
    /// </summary>
    public IReadOnlyList<(int PlayerId, Stance Stance)> ForeignNationStances(int playerId)
    {
        if (PlayerById(playerId) is not { PlayerType: PlayerType.Colonial } self)
        {
            return [];
        }
        return _players
            .Where(o => o.PlayerId != self.PlayerId && o.PlayerType == PlayerType.Colonial)
            .OrderBy(o => o.PlayerId)
            .Select(o => (o.PlayerId, self.Stances.GetValueOrDefault(o.PlayerId)))
            .ToList();
    }

    /// <summary>
    /// Accrues immigration and emigrates while the target is met (FreeCol
    /// <c>Player.getTotalImmigrationProduction</c> + the auto-emigrate loop in
    /// <c>ServerPlayer.csNewTurn</c>): colony crosses drain into the pool (like
    /// bells → liberty), the Europe contribution (−4 per person there, +2 player
    /// bonus, never dropping the turn's total below zero) is added, then each time
    /// the pool meets the target an emigrant arrives in Europe from a random dock slot.
    /// </summary>
    private const string ReligiousUnrestBonusId = "model.modifier.religiousUnrestBonus";

    /// <summary>
    /// The religious-unrest factor on a player's immigration target (FreeCol <c>Modifier.RELIGIOUS_UNREST_BONUS</c>):
    /// the English (the <c>immigration</c> nation type) carry <b>−33%</b>, so their emigrants need a third fewer
    /// immigration points. Returns 1.0 for a player with no nation (the human's default) or whose nation type lacks
    /// the modifier — the first <em>nation-type</em> advantage modifier we apply.
    /// </summary>
    /// <summary>
    /// A player's <b>nation-type advantage</b> modifiers with a given target id (FreeCol's <c>&lt;european-nation-type&gt;</c>
    /// <c>&lt;modifier&gt;</c>s) — the reusable seam for national advantages. Empty for a player with no nation (the human's
    /// default), so a default game folds none.
    /// </summary>
    private IEnumerable<FatherModifier> NationTypeModifiers(Player player, string targetId) =>
        player.NationId is { } nationId && Ruleset.EuropeanNations.FirstOrDefault(n => n.Id == nationId) is { } nation
            ? nation.NationType.Modifiers.Where(m => m.TargetId == targetId)
            : [];

    private double ReligiousUnrestFactor(Player player)
    {
        double factor = 1.0;
        foreach (FatherModifier modifier in NationTypeModifiers(player, ReligiousUnrestBonusId))
        {
            factor = modifier.ApplyTo(factor);
        }
        return factor;
    }

    /// <summary>The Dutch trade advantage (FreeCol <c>model.modifier.tradeBonus</c>, −50%): their market absorbs less of their trade volume, so prices move slower against them.</summary>
    private const string TradeBonusId = "model.modifier.tradeBonus";

    /// <summary>
    /// How much of a sale's volume <paramref name="player"/>'s market absorbs (FreeCol <c>Modifier.TRADE_BONUS</c>): 1.0
    /// for an ordinary player, <b>0.5 for the Dutch</b> (<c>model.nationType.trade</c>, −50%) so their sell price falls
    /// half as fast. Folded from the player's <b>nation type</b> via the shared <see cref="NationTypeModifiers"/> seam
    /// (no founding father carries this modifier); the human defaults to no nation (1.0 → unchanged), so a default game
    /// is byte-identical. (Our buy path doesn't move the market, so the advantage only surfaces on sells — a faithful
    /// subset of FreeCol's two-sided <c>addGoodsToMarket</c>.)
    /// </summary>
    private double MarketVolumeFactor(Player player)
    {
        double factor = 1.0;
        foreach (FatherModifier modifier in NationTypeModifiers(player, TradeBonusId))
        {
            factor = modifier.ApplyTo(factor);
        }
        return factor;
    }

    /// <summary>
    /// A player's <b>effective</b> immigration target — the stored target reduced by its <see cref="ReligiousUnrestFactor"/>.
    /// FreeCol stores this already-reduced; we store the raw target (so the save and the flat <c>crossesIncrement</c> growth
    /// are unchanged) and reduce on use, which is equivalent: the effective target both starts and grows ×factor.
    /// </summary>
    private int EffectiveImmigrationRequired(Player player) =>
        (int)Math.Round(player.ImmigrationRequired * ReligiousUnrestFactor(player), MidpointRounding.AwayFromZero);

    private void AccumulateImmigrationAndEmigrate(Player player)
    {
        // Colony crosses become immigration and leave the warehouse (not tradeable stock).
        int crossesThisTurn = 0;
        foreach (Colony colony in ColoniesOf(player))
        {
            int crosses = colony.StoreOf(CrossesId);
            if (crosses > 0)
            {
                colony.AddGoods(CrossesId, -crosses);
                crossesThisTurn += ApplyGoodsModifiers(player, CrossesId, crosses); // founding-father bonus (Penn)
            }
        }

        // Europe contribution: penalty per person standing on the dock (not aboard a
        // ship), plus the flat player bonus, clamped so this turn's immigration
        // production cannot be negative.
        int europe = (OwnPersonsInEurope(player) * EuropeUnitImmigrationPenalty) + PlayerImmigrationBonus;
        if (europe + crossesThisTurn < 0)
        {
            europe = -crossesThisTurn;
        }
        player.Immigration += crossesThisTurn + europe;

        // Emigrate while immigration is full. Guarded on a stocked dock: test rulesets with no recruitable units
        // have none. A human player who has earned William Brewster (model.ability.selectRecruit) instead PAUSES on
        // the first due emigrant — a pending choice the UI resolves via ChooseEmigrant (FreeCol's selectRecruit);
        // every other case (the AI, and a human without Brewster) keeps the historical random-slot auto-emigrate, so
        // the existing RNG stream + goldens are byte-identical.
        while (player.RecruitDock.Count > 0 && player.Immigration >= EffectiveImmigrationRequired(player))
        {
            if (player.IsHuman && HasAbilityFor(player, SelectRecruitAbility))
            {
                _pendingEmigration = new PendingEmigrationChoice(player.PlayerId, player.RecruitDock.ToList());
                return; // the rest waits until the player has chosen (ChooseEmigrant resumes any backlog)
            }
            Emigrate(player, RandomFor(player).Next(player.RecruitDock.Count));
            ReduceImmigration(player);
            player.ImmigrationRequired += Ruleset.Difficulty.CrossesIncrement;
        }

        // FreeCol survival auto-recruit (Europe.MigrationType.SURVIVAL): a cross-starved colonial player with a dry
        // pool and no New-World presence gets one free emigrant so it is never permanently extinguished. The gate is
        // false for the default cross-producing game, so this draws no RNG and stays byte-identical (see
        // Game.Emigration.cs). One-line hook only; the logic lives in the immigration partial.
        MaybeSurvivalRecruit(player);
    }

    /// <summary>William Brewster's gift (FreeCol <c>model.ability.selectRecruit</c>): the player picks which dock recruit emigrates.</summary>
    private const string SelectRecruitAbility = "model.ability.selectRecruit";

    /// <summary>
    /// Consumes immigration on emigration (FreeCol <c>Player.reduceImmigration</c> with
    /// classic <c>saveProductionOverflow=true</c>): subtract the <b>effective</b> target, keeping any surplus.
    /// </summary>
    private void ReduceImmigration(Player player)
    {
        int required = EffectiveImmigrationRequired(player);
        player.Immigration = required > player.Immigration ? 0 : player.Immigration - required;
    }

    /// <summary>
    /// Fills the dock to <see cref="RecruitSlots"/> with fresh weighted draws. A no-op
    /// when the ruleset defines no recruitable units (minimal test rulesets), so those
    /// games simply have no Europe dock.
    /// </summary>
    private void InitRecruitDock(Player player)
    {
        if (!Ruleset.UnitTypes.Any(t => IsRecruitable(player, t)))
        {
            return;
        }
        while (player.RecruitDock.Count < RecruitSlots)
        {
            player.RecruitDockList.Add(DrawRecruitType(player));
        }
    }

    /// <summary>
    /// A weighted-random recruitable unit type id for <paramref name="player"/> (FreeCol
    /// <c>ServerEurope.generateRecruitablesList</c>): each type's <see cref="UnitType.RecruitProbability"/> is its weight.
    /// </summary>
    private string DrawRecruitType(Player player) => DrawRecruitType(player, RandomFor(player));

    /// <summary>As <see cref="DrawRecruitType(Player)"/> but drawing from an explicit RNG (the Fountain of Youth
    /// threads the exploring unit's owner stream so its burst stays on one stream, same as the type roll).</summary>
    private string DrawRecruitType(Player player, IGameRandom random)
    {
        var pool = Ruleset.UnitTypes.Where(t => IsRecruitable(player, t)).ToList();
        int total = pool.Sum(u => u.RecruitProbability);
        int roll = random.Next(total);
        foreach (UnitType type in pool)
        {
            roll -= type.RecruitProbability;
            if (roll < 0)
            {
                return type.Id;
            }
        }
        return pool[^1].Id; // unreachable: roll < total guarantees an earlier return
    }

    /// <summary>
    /// Takes the recruit in <paramref name="slot"/> off <paramref name="player"/>'s dock, lands it in Europe,
    /// and refills the dock with a fresh draw (the new recruit joins at the bottom slot).
    /// </summary>
    private Unit Emigrate(Player player, int slot)
    {
        string typeId = player.RecruitDock[slot];
        player.RecruitDockList.RemoveAt(slot);
        player.RecruitDockList.Add(DrawRecruitType(player));
        return CreateEuropeRecruit(player, typeId);
    }

    /// <summary>Creates a recruited unit docked in <paramref name="player"/>'s Europe (it has never been on the map).</summary>
    private Unit CreateEuropeRecruit(Player player, string unitTypeId)
    {
        var unit = new Unit(_nextUnitId++, Ruleset.Unit(unitTypeId), new Position(0, 0))
        {
            Location = UnitLocation.InEurope,
            OwnerId = player.PlayerId, // the recruit belongs to its player (the human is 0; a foreign power its own id)
        };
        _units.Add(unit);
        return unit;
    }

    /// <summary>Whether the human player can buy the recruit in <paramref name="slot"/> right now.</summary>
    public MoveCheck CheckRecruit(int slot) => CheckRecruit(_human, slot);

    /// <summary>Whether <paramref name="player"/> can buy the recruit in <paramref name="slot"/> right now.</summary>
    internal MoveCheck CheckRecruit(Player player, int slot)
    {
        if (slot < 0 || slot >= player.RecruitDock.Count)
        {
            return MoveCheck.No("No recruit in that dock slot.");
        }
        int price = player.RecruitPrice;
        if (player.Gold < price)
        {
            return MoveCheck.No($"Not enough gold to recruit (need {price}).");
        }
        return MoveCheck.Yes(price);
    }

    /// <summary>
    /// Buys the recruit in <paramref name="slot"/>: pays <see cref="RecruitPrice"/>, raises the
    /// base price, and — like a free emigrant — consumes immigration and raises the next target
    /// (FreeCol <c>ServerPlayer.csEmigrate</c>, the RECRUIT case falling through to NORMAL).
    /// The recruit lands in Europe and the dock refills.
    /// </summary>
    /// <returns>The recruited unit, docked in Europe.</returns>
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckRecruit(int)"/>.</exception>
    public Unit Recruit(int slot) => Recruit(_human, slot);

    /// <summary>Buys the recruit in <paramref name="slot"/> for <paramref name="player"/> (see <see cref="Recruit(int)"/>).</summary>
    internal Unit Recruit(Player player, int slot)
    {
        MoveCheck check = CheckRecruit(player, slot);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }
        player.Gold -= check.Cost;                          // price read before the base rises
        player.BaseRecruitPrice += Ruleset.Difficulty.RecruitPriceIncrease;    // increaseRecruitmentDifficulty
        player.RecruitLowerCap += Ruleset.Difficulty.RecruitLowerCapIncrease;
        Unit recruit = Emigrate(player, slot);              // extract precedes the immigration cut (as in FreeCol)
        ReduceImmigration(player);
        player.ImmigrationRequired += Ruleset.Difficulty.CrossesIncrement;
        return recruit;
    }

    /// <summary>
    /// The yield of one goods type when a colonist works a tile, with the <em>human</em> player's
    /// Founding-Father goods modifiers (presentation/test helper). The colony-turn rules use the
    /// <see cref="TileYield(Player, Position, string)"/> overload so a foreign power's tiles fold its own fathers.
    /// </summary>
    public int TileYield(Position tile, string goodsId) => TileYield(_human, tile, goodsId);

    /// <summary>
    /// A colony's per-turn <b>net production by stored good</b> under its present assignments: each tile worker's
    /// yield (folding the human's Founding-Father goods modifiers) + the colony-centre tile's unattended output,
    /// less the food the colonists eat (<see cref="Colony.Population"/> × <see cref="Colony.FoodPerColonist"/>).
    /// Keyed by the storage good id (so e.g. all grains roll into food). A pure read (no RNG, no mutation) shared
    /// by the colony screen's production bar and the empire colony report so both show one tested figure (ADR-006).
    /// Building-worker conversions (e.g. a weaver's cloth) are not included — this is the tile/centre production
    /// the colony screen displays, matching its long-standing production bar.
    /// </summary>
    public IReadOnlyDictionary<string, int> ColonyNetProduction(Colony colony)
    {
        var net = new Dictionary<string, int>();
        void Add(string good, int amount)
        {
            string stored = Ruleset.StorageIdOf(good);
            net[stored] = net.GetValueOrDefault(stored) + amount;
        }
        foreach ((Position tile, string good) in colony.TileWorkers)
        {
            Add(good, TileYield(tile, good));
        }
        foreach (ProductionEntry p in Map.TerrainAt(colony.Position).Productions.Where(p => p.Unattended))
        {
            foreach (GoodsOutput o in p.Outputs)
            {
                Add(o.GoodsId, o.Amount);
            }
        }
        Add(Colony.FoodId, -colony.Population * Colony.FoodPerColonist);
        return net;
    }

    /// <summary>
    /// Whether a unit counts as <b>military</b> for the unit report (FreeCol <c>ReportMilitaryPanel</c> /
    /// <c>Unit.isOffensiveUnit</c>, the faithful subset): a non-naval unit whose <em>type</em> is inherently
    /// offensive (e.g. artillery, <see cref="Specification.UnitType.Offence"/> &gt; 0) or whose equipped
    /// <em>role</em> is offensive (soldier/dragoon, <see cref="Specification.RoleType.IsOffensive"/>). Pure read.
    /// Faithful-subset note: FreeCol additionally counts an <em>unarmed</em> veteran via the expertSoldier ability,
    /// which our <see cref="Specification.UnitType"/> does not model yet, so such a unit lists under labour until then.
    /// </summary>
    public bool IsMilitaryUnit(Unit unit) =>
        !unit.Type.IsNaval && (unit.Type.Offence > 0 || Ruleset.Role(unit.RoleId).IsOffensive);

    /// <summary>
    /// The yield of one goods type when a colonist of <paramref name="player"/> works a tile: the terrain's
    /// best attended output, then any bonus-resource boost on the tile, then that player's Founding-Father
    /// goods modifiers (e.g. Henry Hudson's +100% furs). 0 when the terrain can't produce the goods at all
    /// (a resource never enables a new good).
    /// </summary>
    internal int TileYield(Player player, Position tile, string goodsId) =>
        ApplyGoodsModifiers(player, goodsId, TileYieldPotential(tile, goodsId)); // father modifiers stack on the potential

    /// <summary>
    /// The yield when a colonist of <paramref name="workerTypeId"/> works the tile (86d3b6nrz): the resource-boosted
    /// <see cref="TileYieldPotential"/> (index 10), then the worker type's own production modifier (index 30 — an
    /// expert's bonus on its good, e.g. expert farmer +2 grain / fur trapper ×2 furs), then the player's
    /// founding-father modifiers (index 40). Faithful to FreeCol's modifier ordering; a free colonist (or any type
    /// with no modifier for this good) yields the plain figure.
    /// </summary>
    internal int TileYield(Player player, string workerTypeId, Position tile, string goodsId) =>
        ApplyGoodsModifiers(player, goodsId,
            ApplyWorkerProductionModifiers(workerTypeId, goodsId,
                ApplyScopedResourceModifiers(workerTypeId, tile, goodsId, TileYieldPotential(tile, goodsId))));

    /// <summary>
    /// Applies a tile's bonus-resource modifiers <b>scoped to the working unit type</b> (FreeCol resource
    /// <c>&lt;modifier&gt;</c> with a <c>&lt;scope type="…"/&gt;</c>) — the ones <see cref="TileYieldPotential"/>
    /// deliberately skips because they need a worker identity. E.g. a <c>game</c> resource gives every farmer +2 grain
    /// (unscoped, already in the potential) <em>and</em> an expert farmer a further +2 (scoped, here). Index-ordered,
    /// on the resource-boosted potential (index 10) — before the unit's own production modifiers (index 30). A free
    /// colonist, or a type the scope doesn't match, gets nothing extra.
    /// </summary>
    private int ApplyScopedResourceModifiers(string workerTypeId, Position tile, string goodsId, int value)
    {
        if (Map.ResourceAt(tile) is not { } resourceId)
        {
            return value;
        }
        double yield = value;
        foreach (ResourceModifier modifier in Ruleset.Resource(resourceId).Modifiers
                     .Where(m => m.GoodsId == goodsId && !m.IsUnscoped && m.ScopeUnitTypes.Contains(workerTypeId))
                     .OrderBy(m => m.Index))
        {
            yield = modifier.ApplyTo(yield);
        }
        return (int)yield;
    }

    /// <summary>
    /// Folds a worker type's index-30 <see cref="UnitType.ProductionModifiers"/> for <paramref name="goodsId"/> into a
    /// running production value (ascending index, floored at 0). A free colonist — or a specialist working a good it
    /// isn't expert at — leaves the value unchanged. Indentured/petty penalties bite only on the manufactured goods
    /// they list (no raw-tile modifier, so tile yields are unchanged for them; the penalty lands in building production).
    /// <para>
    /// <paramref name="competenceFactor"/> scales an <em>additive</em> modifier's value (the expert's flat bonus) by the
    /// building's competence — e.g. a master carpenter's +3 hammers becomes +6 in a lumber mill (factor 2) — faithfully
    /// to FreeCol <c>BuildingType.getCompetenceModifiers</c>, which multiplies only additive modifiers, NOT
    /// multiplicative ones (a master distiller's ×2 rum is untouched, so the doubling is never amplified). The default
    /// 1.0 is the no-op used by tile work and base buildings.
    /// </para>
    /// </summary>
    private int ApplyWorkerProductionModifiers(string workerTypeId, string goodsId, int value, double competenceFactor = 1.0)
    {
        double yield = value;
        foreach (UnitProductionModifier modifier in Ruleset.Unit(workerTypeId).ProductionModifiersOrEmpty
                     .Where(m => m.GoodsId == goodsId)
                     .OrderBy(m => m.Index))
        {
            // Competence scales only the additive (flat expert bonus) modifiers; multiplicative/percentage ones pass
            // through unchanged — FreeCol scales m.getValue()*competence solely for ModifierType.ADDITIVE.
            yield = competenceFactor != 1.0 && modifier.Type == ModifierType.Additive
                ? ModifierMath.Apply(ModifierType.Additive, yield, modifier.Value * competenceFactor)
                : modifier.ApplyTo(yield);
        }
        return Math.Max(0, (int)yield);
    }

    /// <summary>The good a water tile's coastal fish bonus applies to (FreeCol <c>model.improvement.fishBonusLand</c>).</summary>
    private const string FishId = "model.goods.fish";

    /// <summary>The universal farmed crop — the AI uses a positive grain potential as the test for "a plowable field" (86d3c9vta).</summary>
    private const string GrainId = "model.goods.grain";

    /// <summary>
    /// Flat fish added to a coastal water tile's potential (FreeCol classic <c>fishBonusLand</c> modifier, index-50
    /// additive value 2 — the Col1 "+2 fish on the coast" rule that makes coastal colonies viable).
    /// </summary>
    private const int CoastalFishBonus = 2;

    /// <summary>
    /// Flat fish added to a non-high-seas water tile that adjoins a river mouth (FreeCol classic <c>fishBonusRiver</c>
    /// modifier, index-50 additive value 1 — the "river brings fish to the sea" rule). Stacks on the coastal +2.
    /// </summary>
    private const int RiverMouthFishBonus = 1;

    /// <summary>
    /// FreeCol's Col1 coastal-fish threshold: a water tile needs <b>more than two</b> adjacent land tiles to earn the
    /// bonus (the map generator applies <c>fishBonusLand</c> only when <c>adjacentLand &gt; 2</c>; fewer land neighbours
    /// stay at the open-ocean 2 fish). See FreeCol <c>TerrainGenerator.perhapsAddBonus</c>.
    /// </summary>
    private const int CoastalLandNeighboursRequired = 2;

    /// <summary>
    /// The tile's <b>potential</b> yield of one goods type — the terrain's best attended output, any on-tile
    /// bonus-resource boost, and (for fish on coastal water) the coastal fish bonus, but <em>without</em> any player's
    /// Founding-Father goods modifiers (FreeCol <c>Tile.getPotentialProduction</c> with a null owner). This is the
    /// player-independent figure native land is valued from (see <see cref="LandPrice(Player, Position)"/>);
    /// <see cref="TileYield(Player, Position, string)"/> folds the acting player's fathers on top of it for actual
    /// colony production. 0 when the terrain can't make it.
    /// </summary>
    internal int TileYieldPotential(Position tile, string goodsId)
    {
        int baseYield = Map.TerrainAt(tile).Productions
            .Where(p => !p.Unattended)
            .SelectMany(p => p.Outputs)
            .Where(o => o.GoodsId == goodsId)
            .Select(o => o.Amount)
            .DefaultIfEmpty(0)
            .Max();
        if (baseYield <= 0)
        {
            return 0;
        }

        double yield = baseYield;
        // Bonus resource on the tile: apply its unscoped modifiers (expert-scoped ones
        // need per-colonist identity we don't track yet, so they are skipped).
        if (Map.ResourceAt(tile) is { } resourceId)
        {
            foreach (ResourceModifier modifier in Ruleset.Resource(resourceId).Modifiers
                         .Where(m => m.GoodsId == goodsId && m.IsUnscoped)
                         .OrderBy(m => m.Index))
            {
                yield = modifier.ApplyTo(yield);
            }
        }

        // Tile improvements: add each one's flat goods bonus (FreeCol improvement <modifier> children, e.g. a river's
        // +1 grain / +2 furs/lumber, a plowed field's +1 farmed goods, a road's +furs/lumber/ore/silver). All are
        // additive at index 50 — after the index-10 resource modifiers above — so a multiplicative resource bonus is
        // applied first and the improvements' flat deltas added on top. A tile may carry several (river + road/plow).
        yield += ImprovementProduction.YieldDelta(Map.ImprovementsAt(tile), goodsId);

        // Coastal fish bonus (FreeCol fishBonusLand): +2 fish on a coastal water tile — one with more than two
        // adjacent land tiles. High-seas tiles are excluded (the improvement's match-negated scope).
        if (goodsId == FishId && IsCoastalWater(tile))
        {
            yield += CoastalFishBonus;
        }

        // River-mouth fish bonus (FreeCol fishBonusRiver): +1 fish on a non-high-seas water tile adjacent to a land
        // tile where a river meets the sea (a river-mouth tile — a land tile carrying a river that itself touches
        // water). Stacks on top of the coastal +2 (FreeCol applies both improvements). The river layer is read at
        // potential time; no per-tile river style is needed (the river simply being on a coast-adjacent land tile
        // makes that land tile a mouth), so this stays a pure read.
        if (goodsId == FishId && IsRiverMouthWater(tile))
        {
            yield += RiverMouthFishBonus;
        }

        return (int)yield;
    }

    /// <summary>
    /// Whether a water tile qualifies for the coastal fish bonus: water (excluding the high seas) with more than two
    /// adjacent land tiles. Pure read of terrain + adjacency (no RNG, no stored state) — faithful to FreeCol's
    /// <c>TerrainGenerator.perhapsAddBonus</c> (<c>adjacentLand &gt; 2</c>, high seas excluded).
    /// </summary>
    private bool IsCoastalWater(Position tile)
    {
        TerrainType terrain = Map.TerrainAt(tile);
        if (!terrain.IsWater || terrain.Id == HighSeasId)
        {
            return false;
        }
        int adjacentLand = tile.Neighbours().Count(n => Map.InBounds(n) && !Map.TerrainAt(n).IsWater);
        return adjacentLand > CoastalLandNeighboursRequired;
    }

    /// <summary>
    /// Whether a water tile adjoins a river mouth and so earns the <c>fishBonusRiver</c> +1: the tile must be water
    /// (excluding the high seas) and have at least one adjacent <b>river-mouth land tile</b> — a land tile carrying a
    /// river that itself sits beside water (where the river meets the sea). Pure read of terrain + the river
    /// improvement layer + adjacency (no RNG, no stored state). Faithful to FreeCol, which stamps
    /// <c>fishBonusRiver</c> on the sea tiles a river flows into.
    /// </summary>
    private bool IsRiverMouthWater(Position tile)
    {
        TerrainType terrain = Map.TerrainAt(tile);
        if (!terrain.IsWater || terrain.Id == HighSeasId)
        {
            return false;
        }
        return tile.Neighbours().Any(n => Map.InBounds(n) && IsRiverMouthLand(n));
    }

    /// <summary>
    /// Whether a land tile is a river mouth: it carries a river and has at least one adjacent water tile, so the river
    /// reaches the sea there. (A river tile entirely inland is not a mouth.)
    /// </summary>
    private bool IsRiverMouthLand(Position land) =>
        Map.HasRiver(land)
        && land.Neighbours().Any(w => Map.InBounds(w) && Map.TerrainAt(w).IsWater);

    /// <summary>
    /// Whether a colonist of <paramref name="colony"/> may be put to work on
    /// <paramref name="tile"/> producing <paramref name="goodsId"/>.
    /// </summary>
    public MoveCheck CheckAssignWork(Colony colony, Position tile, string goodsId) =>
        CheckAssignWork(_human, colony, tile, goodsId);

    /// <summary>
    /// <inheritdoc cref="CheckAssignWork(Colony, Position, string)"/> evaluated for <paramref name="player"/> (the
    /// colony's owner), so the tile yield folds <em>that</em> player's founding fathers — the foreign-power AI must
    /// rank tiles by its own yields, not the human's (the public overload delegates here with the human).
    /// </summary>
    internal MoveCheck CheckAssignWork(Player player, Colony colony, Position tile, string goodsId)
    {
        if (!Map.InBounds(tile))
        {
            return MoveCheck.No("Tile is off the map.");
        }
        if (!tile.IsAdjacentTo(colony.Position))
        {
            return MoveCheck.No("Colonists work the eight tiles around the colony.");
        }
        if (colony.TileWorkers.ContainsKey(tile))
        {
            return MoveCheck.No("That tile is already worked.");
        }
        if (colony.IdleColonists <= 0)
        {
            return MoveCheck.No("No idle colonists.");
        }
        // Sea tiles (ocean/lake) can only be fished once the colony has built the Docks (or an upgrade that inherits
        // its model.ability.produceInWater). FreeCol ColonyTile.getNoWorkReason: !hasAbility(PRODUCE_IN_WATER) &&
        // !tile.isLand() ⇒ MISSING_ABILITY. The colony centre is always land, so this only ever gates the 8 ring tiles.
        if (Map.TerrainAt(tile).IsWater && !ColonyCanWorkWater(colony))
        {
            return MoveCheck.No("Build Docks before colonists can work the sea.");
        }
        int yield = TileYield(player, tile, goodsId);
        if (yield <= 0)
        {
            return MoveCheck.No($"That tile cannot produce {goodsId[(goodsId.LastIndexOf('.') + 1)..]}.");
        }
        return MoveCheck.Yes(yield);
    }

    /// <summary>
    /// Whether <paramref name="colony"/> may put colonists on its <b>water</b> tiles — i.e. it has built a building
    /// granting <c>model.ability.produceInWater</c> (the docks, or a drydock/shipyard that inherits it). FreeCol
    /// <c>Colony.hasAbility(Ability.PRODUCE_IN_WATER)</c> (building-sourced; the classic ruleset grants it only via docks).
    /// </summary>
    private bool ColonyCanWorkWater(Colony colony) =>
        colony.Buildings.Any(b => Ruleset.Building(b).ProducesInWater);

    /// <summary>
    /// Whether <paramref name="colony"/> may put a colonist on <paramref name="tile"/> on <b>terrain</b> grounds: a land
    /// tile always; a <b>water</b> tile only once the colony has Docks (<see cref="ColonyCanWorkWater"/>). A rules query
    /// (ADR-006) the colony screen uses to decide whether to offer tile work at all — the full per-good gate (idle
    /// colonist, not already worked, positive yield, and this same water rule) is <see cref="CheckAssignWork(Colony, Position, string)"/>.
    /// </summary>
    public bool ColonyCanWorkTile(Colony colony, Position tile) =>
        Map.InBounds(tile) && (!Map.TerrainAt(tile).IsWater || ColonyCanWorkWater(colony));

    /// <summary>Puts an idle colonist to work on a tile producing one goods type.</summary>
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckAssignWork(Colony, Position, string)"/>.</exception>
    public void AssignWork(Colony colony, Position tile, string goodsId) =>
        AssignWork(_human, colony, tile, goodsId);

    /// <summary>Puts an idle colonist to work on behalf of <paramref name="player"/> (the colony owner), gating on that player's yields.</summary>
    internal void AssignWork(Player player, Colony colony, Position tile, string goodsId)
    {
        MoveCheck check = CheckAssignWork(player, colony, tile, goodsId);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }
        colony.SetWorker(tile, goodsId, PickIdleWorkerFor(colony, goodsId));
    }

    /// <summary>Returns a tile's worker to the idle pool.</summary>
    public void UnassignWork(Colony colony, Position tile) => colony.RemoveWorker(tile);

    /// <summary>
    /// The goods a colonist could produce by working <paramref name="tile"/> — its terrain's <b>attended</b>
    /// production outputs that currently yield more than 0 (so a colony's surrounding forest offers lumber/furs/grain,
    /// hills offer ore, plains offer grain/cotton, …), each paired with its yield. Sorted by yield (descending) then
    /// goods id, for a stable, useful order in the colony screen's tile-work picker. Empty for water / off-map /
    /// barren tiles. A rules query (ADR-006) — the presentation renders these options and calls <see cref="AssignWork(Colony, Position, string)"/>.
    /// </summary>
    public IReadOnlyList<(string GoodsId, int Yield)> TileWorkOptions(Position tile) => TileWorkOptions(_human, tile);

    /// <summary><inheritdoc cref="TileWorkOptions(Position)"/> with yields folding <paramref name="player"/>'s fathers (the AI ranks tiles by its own yields).</summary>
    internal IReadOnlyList<(string GoodsId, int Yield)> TileWorkOptions(Player player, Position tile)
    {
        if (!Map.InBounds(tile))
        {
            return [];
        }
        return Map.TerrainAt(tile).Productions
            .Where(p => !p.Unattended)
            .SelectMany(p => p.Outputs)
            .Select(o => o.GoodsId)
            .Distinct()
            .Select(id => (GoodsId: id, Yield: TileYield(player, tile, id)))
            .Where(t => t.Yield > 0)
            .OrderByDescending(t => t.Yield)
            .ThenBy(t => t.GoodsId, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Auto-assigns idle colonists to the best unworked food tiles (highest grain
    /// yield, deterministic tie-break). Runs on founding and growth; also available
    /// to the player ("send idle colonists to the fields").
    /// </summary>
    public void AutoAssignIdleToFood(Colony colony) =>
        AutoAssignIdleToFood(PlayerById(colony.OwnerId) ?? _human, colony);

    /// <summary><inheritdoc cref="AutoAssignIdleToFood(Colony)"/> ranking grain by <paramref name="player"/>'s yields (so a foreign power uses its own fathers, not the human's — a correctness fix for AI colonies).</summary>
    internal void AutoAssignIdleToFood(Player player, Colony colony)
    {
        const string grain = "model.goods.grain";
        while (colony.IdleColonists > 0)
        {
            var best = colony.Position.Neighbours()
                .Where(n => Map.InBounds(n) && !colony.TileWorkers.ContainsKey(n))
                .Select(n => (tile: n, yield: TileYield(player, n, grain)))
                .Where(t => t.yield > 0)
                .OrderByDescending(t => t.yield)
                .ThenBy(t => t.tile.Y)
                .ThenBy(t => t.tile.X)
                .Cast<(Position tile, int yield)?>()
                .FirstOrDefault();
            if (best is null)
            {
                return; // nowhere productive left — colonist stays idle
            }
            colony.SetWorker(best.Value.tile, grain, PickIdleWorkerFor(colony, grain));
        }
    }

    /// <summary>
    /// The idle colonist to send to a tile producing <paramref name="goodsId"/> (86d3b6nrz): the matching expert
    /// (its <see cref="UnitType.ExpertProduction"/> == the good) if one is idle, else a free colonist, else any idle
    /// specialist (so none is silently lost). Slice 2 is type-blind; this pick gains effect when tile yield folds the
    /// worker type (slice 3) — an expert auto-lands on its own good's tile.
    /// </summary>
    private string PickIdleWorkerFor(Colony colony, string goodsId)
    {
        if (colony.IdleWorkerTypes.FirstOrDefault(t => Ruleset.Unit(t).ExpertProduction == goodsId) is { } expert)
        {
            return expert;
        }
        return colony.IdleColonists - colony.IdleWorkerTypes.Count > 0 || colony.IdleWorkerTypes.Count == 0
            ? Colony.FreeColonistTypeId
            : colony.IdleWorkerTypes[0];
    }

    /// <summary>The idle colonist to send to a building: a free colonist if one is idle (keep specialists for their own tiles), else any idle specialist.</summary>
    private static string PickIdleBuildingWorker(Colony colony) =>
        colony.IdleColonists - colony.IdleWorkerTypes.Count > 0 || colony.IdleWorkerTypes.Count == 0
            ? Colony.FreeColonistTypeId
            : colony.IdleWorkerTypes[0];

    /// <summary>
    /// One building's turn: unattended output plus per-worker conversion of
    /// warehouse inputs to outputs (scaled down when inputs run short).
    /// </summary>
    private void RunBuildingProduction(Colony colony, BuildingType building, int foodProducedThisTurn)
    {
        int workers = colony.BuildingWorkers.GetValueOrDefault(building.Id);
        IReadOnlyList<string> occupants = colony.BuildingOccupants(building.Id);
        foreach (ProductionEntry entry in building.Productions)
        {
            // Auto-production breeding (horses): a herd-size growth formula — gated at the breeding number, capped at
            // the warehouse, eating only surplus food — not the generic per-worker conversion (FreeCol autoProduction).
            if (building.BreedingDivisor > 0 && entry.Outputs.Count == 1
                && Ruleset.Goods(entry.Outputs[0].GoodsId).BreedingNumber is int breedingNumber)
            {
                RunBreeding(colony, building, entry, breedingNumber, foodProducedThisTurn);
                continue;
            }

            if (!entry.Unattended && workers == 0)
            {
                continue; // an attended entry with nobody assigned produces nothing
            }

            // Per-good output total (86d3b6nrz slice 5), faithful to FreeCol BuildingProductionCalculator: each worker's
            // own output is its base plus the Sons-of-Liberty bonus (additive, index 20), then its unit type's index-30
            // expert modifier (its ADDITIVE part scaled by the building's competence factor — lumber mill 2, factory
            // tier 2/3 — so an expert earns a bigger flat bonus in an upgraded manufactory; multiplicative experts are
            // unscaled), then floored at 0 — and the building's total is the sum over its occupants (the non-free overlay
            // padded with free colonists to the worker count). The SoL bonus is `floor(ProductionBonus × rebel-factor)`
            // per worker (lumber mill / cathedral ×2, factory tier ×1.5); folding it BEFORE the index-30 step means a
            // multiplicative expert (master distiller ×2 rum) multiplies the bonus too, and the per-worker floor means a
            // bad government can't turn a productive colonist negative. An unattended entry (town-hall bell, church
            // crosses) is a flat single unit — no worker, no bonus. An all-free, bonus-free building sums to base ×
            // workers, identical to the old scalar path (free colonists carry no production modifier, so competence is a
            // no-op for them and competence=1 buildings are byte-identical).
            int rebelBonus = entry.Unattended ? 0 : (int)Math.Floor(colony.ProductionBonus * building.RebelFactor);
            Dictionary<string, int> outputTotals = new(entry.Outputs.Count);
            foreach (GoodsOutput output in entry.Outputs)
            {
                outputTotals[output.GoodsId] = entry.Unattended
                    ? output.Amount
                    : occupants.Sum(t => ApplyWorkerProductionModifiers(
                        t, output.GoodsId, output.Amount + rebelBonus, building.CompetenceFactor));
            }

            // Input consumption / scarcity follow FreeCol's minimumRatio: each input is wanted in proportion to the
            // worker-modified output (ratio = the primary output's modified total over its base), then the whole
            // conversion is scaled down by the scarcest input. Two FreeCol details stop a 1:1 conversion losing a unit
            // to representational error: the required input is FLOORED to an integer before the short-supply test, and
            // the finals get a tiny EPSILON before flooring. The bonus is inside the output total, so inputs scale with
            // the bonus-inclusive output (FreeCol charges input for the rebel-bonus production) and there is no separate
            // flat output add. Unattended / base-zero entries fall back to a ratio of the worker count — the old integer
            // multiplier for the all-free case (so an all-free, bonus-free building is byte-identical).
            const double Epsilon = 0.0001; // FreeCol BuildingProductionCalculator flooring nudge
            int primaryBase = !entry.Unattended && entry.Outputs.Count > 0 ? entry.Outputs[0].Amount : 0;
            double ratio = primaryBase > 0
                ? (double)outputTotals[entry.Outputs[0].GoodsId] / primaryBase
                : (entry.Unattended ? 1.0 : workers);
            double scarcity = 1.0;
            foreach (GoodsOutput input in entry.Inputs)
            {
                long required = (long)Math.Floor(input.Amount * ratio);
                int available = colony.StoreOf(Ruleset.StorageIdOf(input.GoodsId));
                if (required > 0 && available < required)
                {
                    scarcity = Math.Min(scarcity, (double)available / required);
                }
            }

            foreach (GoodsOutput input in entry.Inputs)
            {
                colony.AddGoods(
                    Ruleset.StorageIdOf(input.GoodsId),
                    -(int)Math.Floor(input.Amount * ratio * scarcity + Epsilon));
            }
            foreach (GoodsOutput output in entry.Outputs)
            {
                // Each good's own worker-modified total, scaled by the (≤1) input-scarcity factor — the SoL bonus is
                // already folded in per worker, so a starved building scales the bonus down with the rest (FreeCol).
                colony.AddGoods(
                    Ruleset.StorageIdOf(output.GoodsId),
                    (int)Math.Floor(outputTotals[output.GoodsId] * scarcity + Epsilon));
            }
        }
    }

    /// <summary>
    /// Auto-production horse breeding (FreeCol <c>BuildingProductionCalculator</c> autoProduction): a pasture/stables
    /// grows the herd by <c>((herd−1)/divisor + 1) × factor</c> each turn — faster the larger the herd, faster again
    /// with a stables (divisor halved, 50 → 25) — gated at the goods' breeding number (no foals below it), stopping at
    /// the warehouse cap, and eating only <b>this turn's surplus food</b> — half of what the colony <em>produced</em>
    /// this turn (<c>consumeOnlySurplusProduction</c> 0.5), never its stored carryover — so the foals are limited by
    /// the food the colony is actually making and the herd can't be grown off a stockpile. Deterministic (no RNG).
    /// </summary>
    private void RunBreeding(Colony colony, BuildingType building, ProductionEntry entry, int breedingNumber, int foodProducedThisTurn)
    {
        string horsesId = Ruleset.StorageIdOf(entry.Outputs[0].GoodsId);
        int herd = colony.StoreOf(horsesId);
        int capacity = WarehouseCapacity(colony);
        if (herd < breedingNumber || herd >= capacity || building.BreedingDivisor <= 0)
        {
            return; // no foals below the breeding number, at/over the warehouse cap, or without a divisor
        }

        // Herd-growth formula (integer division — the classic curve), then never overflow the warehouse.
        int bred = Math.Min(
            ((herd - 1) / building.BreedingDivisor + 1) * building.BreedingFactor,
            capacity - herd);

        // Horses are made-from food at the entry's input ratio (classic 1 food : 1 horse); breeding may eat only HALF
        // of this turn's food production (consumeOnlySurplusProduction 0.5), leaving the rest (plus any carryover) for
        // the colonists — so it can't starve the colony and a herd can't grow off a stockpile alone.
        int foodPerFoal = entry.Inputs.Count == 1 ? Math.Max(1, entry.Inputs[0].Amount) : 0;
        if (foodPerFoal > 0)
        {
            bred = Math.Min(bred, foodProducedThisTurn / 2 / foodPerFoal);
        }
        if (bred <= 0)
        {
            return;
        }
        if (foodPerFoal > 0)
        {
            colony.AddGoods(Ruleset.StorageIdOf(entry.Inputs[0].GoodsId), -bred * foodPerFoal);
        }
        colony.AddGoods(horsesId, bred);
    }

    /// <summary>
    /// Drops assignments until they fit the population: building workers are
    /// pulled before field workers (deterministic order — last building, then
    /// last tile in row-major order).
    /// </summary>
    private static void TrimAssignments(Colony colony)
    {
        while (colony.IdleColonists < 0)
        {
            if (colony.BuildingWorkers.Count > 0)
            {
                string building = colony.BuildingWorkers.Keys.Last();
                colony.SetBuildingWorkers(building, colony.BuildingWorkers[building] - 1);
            }
            else if (colony.TileWorkers.Count > 0)
            {
                Position tile = colony.TileWorkers.Keys
                    .OrderBy(p => p.Y).ThenBy(p => p.X)
                    .Last();
                colony.RemoveWorker(tile);
            }
            else
            {
                break;
            }
        }
        colony.ReconcileWorkerTypes(); // keep the worker-type overlay ≤ the trimmed counts (86d3b6nrz)
    }

    /// <summary>One colony's production-eat-grow step (its <paramref name="owner"/>'s fathers fold into tile yields).</summary>
    /// <summary>
    /// A colony's <b>net food</b> for the current turn under its present assignments (FreeCol
    /// <c>getAdjustedNetProductionOf(food)</c>, the figure the survival check uses): the colony-centre tile's
    /// unattended food + each food-tile worker's yield (folding <paramref name="owner"/>'s fathers and the colony's
    /// Sons-of-Liberty production bonus, floored at 0), minus what the colonists eat (<see cref="Colony.Population"/> ×
    /// <see cref="Colony.FoodPerColonist"/>). Mirrors the production + consumption in <see cref="RunColonyTurn"/>;
    /// horse breeding is excluded (it eats only this turn's <em>surplus</em>, never starving colonists). A pure read —
    /// no RNG, no mutation — used by the foreign-power colony worker planner to balance cash crops against starvation.
    /// </summary>
    internal int ColonyNetFood(Player owner, Colony colony)
    {
        int produced = CentreFoodProduction(colony);
        foreach ((Position tile, string goodsId) in colony.TileWorkers)
        {
            if (Ruleset.StorageIdOf(goodsId) == Colony.FoodId)
            {
                produced += Math.Max(0, TileYield(owner, colony.WorkerTypeAt(tile), tile, goodsId) + colony.ProductionBonus);
            }
        }
        return produced - colony.Population * Colony.FoodPerColonist;
    }

    /// <summary>The colony-centre tile's unattended food production (always worked, no colonist needed).</summary>
    private int CentreFoodProduction(Colony colony) =>
        Map.TerrainAt(colony.Position).Productions.Where(p => p.Unattended)
            .SelectMany(p => p.Outputs)
            .Where(o => Ruleset.StorageIdOf(o.GoodsId) == Colony.FoodId)
            .Sum(o => o.Amount);

    /// <summary>
    /// Plans a foreign-power colony's <b>tile</b> workers (FreeCol <c>ColonyPlan.updateRawMaterials</c> +
    /// <c>assignWorkers</c>, tile subset): rank the tradeable, tile-farmed, non-food raws by Σ neighbour yield ×
    /// market sale price (averaged with the refined good's price when one exists, FreeCol's weighting), take the top
    /// two, then greedily fill — produce those cash raws while net food stays positive, falling back to grain
    /// otherwise (so the colony never plans itself into starvation), landing the matching expert on each good via
    /// <see cref="PickIdleWorkerFor"/>. The plan is <b>diff-applied</b>: a tile already worked with the planned good
    /// is left untouched, so a colonist keeps accruing on-the-job experience (<c>86d3c9pgj</c>) on a stable tile
    /// instead of being churned each turn. Building workers are left as-is (a later increment). <b>RNG-free</b> (pure
    /// ordinal/yield ranking) → the human's stream 0 is untouched (ADR-009). The Sons-of-Liberty
    /// <see cref="Colony.ProductionBonus"/> is invariant to assignment in our model (it keys off population + liberty,
    /// not the worked-unit count), so FreeCol's production-bonus guard does not apply. All per-tile yields are
    /// computed once into a small matrix (the modifier fold is the cost), so re-planning every turn stays cheap.
    /// <b>Deviation:</b> FreeCol's ×1.2 national-advantage multiplier in the ranking is not yet applied (a minor
    /// tie-break refinement).
    /// </summary>
    internal void PlanColonyTileWork(Player owner, Colony colony)
    {
        int tileSlots = colony.Population - colony.BuildingWorkers.Values.Sum();
        if (tileSlots <= 0)
        {
            return;
        }

        List<Position> neighbours = colony.Position.Neighbours().Where(Map.InBounds).ToList();
        List<GoodsType> cashRaws = Ruleset.GoodsTypes.Where(g => g.IsFarmed && !g.IsFood && g.IsTradeable).ToList();
        List<string> foodGoods = Ruleset.GoodsTypes.Where(g => g.IsFarmed && g.IsFood).Select(g => g.Id).ToList(); // grain (land) + fish (ocean)

        // A sea tile is only workable once the colony has Docks (the same produceInWater gate CheckAssignWork enforces).
        // The planner must respect it: a fish plan on an ungated water tile would be rejected by AssignWork below and
        // throw mid-turn, so such tiles are excluded from candidacy here (matching FreeCol's plan, which never staffs sea
        // tiles a dockless colony cannot work).
        bool worksWater = ColonyCanWorkWater(colony);
        bool CanWorkTile(Position n) => worksWater || !Map.TerrainAt(n).IsWater;

        // Precompute each neighbour's player-folded yield for every candidate good (cash raws + food goods) ONCE —
        // the modifier fold (TileYield) is the cost, and both the ranking and the greedy read these same numbers.
        var yield = new Dictionary<(Position Tile, string Good), int>();
        foreach (Position n in neighbours)
        {
            foreach (string good in cashRaws.Select(r => r.Id).Concat(foodGoods))
            {
                yield[(n, good)] = TileYield(owner, n, good);
            }
        }

        // Rank the cash raws (top-2 by Σ yield × market sale price), ties by ordinal id. FreeCol averages a raw's
        // price with its refined good's only when the colony actually produces that refined good — our tile planner
        // makes no refined goods yet, so we weight by the raw's own sale price (the refined-average arrives with the
        // building-worker increment).
        List<string> produce = cashRaws
            .Select(raw => (id: raw.Id, value: neighbours.Sum(n => yield[(n, raw.Id)]) * owner.Market.BidPrice(raw.Id)))
            .Where(t => t.value > 0)
            .OrderByDescending(t => t.value)
            .ThenBy(t => t.id, StringComparer.Ordinal)
            .Take(2)
            .Select(t => t.id)
            .ToList();

        int centreFood = CentreFoodProduction(colony);
        var target = new Dictionary<Position, string>();
        int produceIdx = 0;

        // Effective output of a tile that CAN produce the good (base yield > 0), with the SoL bonus on top, floored.
        // The base-yield gate matters: a positive ProductionBonus must never make a barren tile look workable (which
        // would then be rejected by AssignWork's "cannot produce" check) — the bonus only lifts real production.
        int Output(Position t, string good) => yield[(t, good)] <= 0 ? 0 : Math.Max(0, yield[(t, good)] + colony.ProductionBonus);
        int NetFood() => centreFood
            + target.Where(kv => Ruleset.StorageIdOf(kv.Value) == Colony.FoodId).Sum(kv => Output(kv.Key, kv.Value))
            - colony.Population * Colony.FoodPerColonist;
        Position? BestTileFor(string good) => neighbours
            .Where(n => !target.ContainsKey(n) && CanWorkTile(n) && yield[(n, good)] > 0)
            .OrderByDescending(n => Output(n, good)).ThenBy(n => n.Y).ThenBy(n => n.X)
            .Select(n => (Position?)n)
            .FirstOrDefault();
        // The best (tile, food good) across every farmed food good — grain on land, fish on ocean — so a coastal
        // colony feeds itself from fish, not only grain (FreeCol's food plans include both). Sea tiles only count once
        // the colony has Docks (CanWorkTile), so a dockless coastal colony plans grain only.
        (Position Tile, string Good)? BestFood() => neighbours
            .Where(n => !target.ContainsKey(n) && CanWorkTile(n))
            .SelectMany(n => foodGoods.Where(g => yield[(n, g)] > 0).Select(g => (Tile: n, Good: g)))
            .OrderByDescending(x => Output(x.Tile, x.Good)).ThenBy(x => x.Tile.Y).ThenBy(x => x.Tile.X).ThenBy(x => x.Good, StringComparer.Ordinal)
            .Select(x => ((Position Tile, string Good)?)x)
            .FirstOrDefault();

        while (target.Count < tileSlots)
        {
            Position? pick = null;
            string? pickGood = null;
            if (NetFood() > 0 && produce.Count > 0) // fed enough → grow the cash economy
            {
                for (int i = 0; i < produce.Count; i++)
                {
                    string good = produce[(produceIdx + i) % produce.Count];
                    if (BestTileFor(good) is { } tile)
                    {
                        pick = tile;
                        pickGood = good;
                        produceIdx = (produceIdx + i + 1) % produce.Count; // round-robin the produced good to the back
                        break;
                    }
                }
            }
            if (pick is null && BestFood() is { } food) // not fed (or no cash tile) → grow food (grain or fish)
            {
                pick = food.Tile;
                pickGood = food.Good;
            }
            if (pick is null)
            {
                break; // nothing left to work
            }
            target[pick.Value] = pickGood!;
        }

        // Diff-apply: free tiles whose plan changed (preserving experience on stable tiles), then fill the rest.
        foreach ((Position tile, string good) in colony.TileWorkers.ToList())
        {
            if (!target.TryGetValue(tile, out string? planned) || planned != good)
            {
                UnassignWork(colony, tile);
            }
        }
        foreach ((Position tile, string good) in target.OrderBy(kv => kv.Key.Y).ThenBy(kv => kv.Key.X))
        {
            if (!colony.TileWorkers.ContainsKey(tile))
            {
                AssignWork(owner, colony, tile, good);
            }
        }
    }

    /// <summary>A per-worker value for a non-tradeable but useful building output (FreeCol <c>ColonyPlan</c> production
    /// weights): construction materials (hammers/tools) are valued <b>high while a build is queued</b> (16, so the
    /// carpenter's house out-ranks a refinery and construction actually progresses, FreeCol's HAMMERS priority) and
    /// modestly otherwise (8); bells then crosses a touch below — so a colony with an active build staffs its carpenter,
    /// while an idle-queue colony favours its refineries.</summary>
    private int NonTradeableOutputValue(Colony colony, string goodsId) => goodsId switch
    {
        _ when Ruleset.BuildingMaterials.Contains(goodsId) => colony.CurrentBuild is not null ? 16 : 8, // hammers/tools → construction
        BellsId => 6,                                                                                   // liberty
        CrossesId => 4,                                                                                 // immigration
        _ => 0,
    };

    /// <summary>
    /// Plans a foreign-power colony's <b>building</b> workers (FreeCol <c>ColonyPlan.assignWorkers</c>, building subset):
    /// after the tiles are staffed (<see cref="PlanColonyTileWork"/> runs first and protects food), the colony's
    /// <em>remaining idle</em> colonists are sent to the highest-value production buildings — turning the raws the tiles
    /// (and centre) make into refined goods, hammers for construction, and bells/crosses. A building is valued by the
    /// market sale value its <b>next</b> worker would add per turn: for each attended production entry, the per-worker
    /// modified output of a free colonist × the good's market sale price (a tradeable refined good) or a fixed
    /// <see cref="NonTradeableOutputValue"/> (hammers/bells/crosses), summed; an entry is only counted when the colony
    /// can actually feed it — every positively-consumed input is either already in store <b>or</b> itself produced by a
    /// tile/centre/another building this turn (so a distiller is staffed once sugar is being farmed, not before). The
    /// highest-value building with a free workplace takes one colonist; repeat until no idle colonist or no positive
    /// building remains. Because it only ever consumes colonists already idle <b>after</b> the food-first tile plan, it
    /// can never pull a food worker — the colony's survival margin is untouched (a soak invariant). Ties break by
    /// ordinal building id; <b>RNG-free</b> (pure value/ordinal ranking) → the human's stream 0 is untouched (ADR-009).
    /// <b>Deviation:</b> not FreeCol's full marginal <c>getBestWorker</c>/expert-swap or its input-exhaustion scaling —
    /// a free-colonist greedy fill of the best-valued buildings (the experts already land on their own tiles via the tile
    /// planner; <see cref="PickIdleBuildingWorker"/> keeps specialists for their tiles).
    /// </summary>
    internal void PlanColonyBuildingWork(Player owner, Colony colony)
    {
        if (colony.IdleColonists <= 0)
        {
            return;
        }

        // The goods the colony is making locally this turn (centre unattended + worked tiles + already-stocked goods):
        // an attended building entry can only be staffed when each of its inputs is available, so a refinery isn't filled
        // before its raw is being farmed. Stored goods count too (a stockpile feeds the building until the tiles catch up).
        var available = new HashSet<string>();
        foreach (string g in colony.Stores.Where(kv => kv.Value > 0).Select(kv => kv.Key))
        {
            available.Add(g);
        }
        foreach (ProductionEntry entry in Map.TerrainAt(colony.Position).Productions.Where(p => p.Unattended))
        {
            foreach (GoodsOutput o in entry.Outputs)
            {
                available.Add(Ruleset.StorageIdOf(o.GoodsId));
            }
        }
        foreach (string good in colony.TileWorkers.Values)
        {
            available.Add(Ruleset.StorageIdOf(good));
        }

        // The per-turn market value the worker <paramref name="workerType"/> would add by joining building b (0 if it has
        // no free workplace, no attended entry, or no entry the colony can currently feed). A building's own refined
        // output also becomes an available input for a downstream building considered later (rare in classic, but keeps
        // the ranking coherent). The worker type is threaded so an expert's index-30 modifiers raise its own building's
        // output (FreeCol getBestWorker — the worker that adds the most production wins the slot).
        int BuildingWorkerValue(BuildingType b, string workerType)
        {
            if (colony.BuildingWorkers.GetValueOrDefault(b.Id) >= b.Workplaces)
            {
                return 0; // fully staffed
            }
            int value = 0;
            foreach (ProductionEntry entry in b.Productions.Where(e => !e.Unattended && e.Outputs.Count > 0))
            {
                // Skip auto-production (horse breeding) — it needs no worker and is handled by RunBuildingProduction.
                if (b.BreedingDivisor > 0)
                {
                    continue;
                }
                bool fed = entry.Inputs.All(i => available.Contains(Ruleset.StorageIdOf(i.GoodsId)));
                if (!fed)
                {
                    continue;
                }
                foreach (GoodsOutput o in entry.Outputs)
                {
                    int perWorker = Math.Max(0, ApplyWorkerProductionModifiers(workerType, o.GoodsId, o.Amount));
                    if (perWorker <= 0)
                    {
                        continue;
                    }
                    GoodsType g = Ruleset.Goods(o.GoodsId);
                    value += perWorker * (g.IsTradeable ? owner.Market.BidPrice(o.GoodsId) : NonTradeableOutputValue(colony, o.GoodsId));
                }
            }
            return value;
        }

        // The idle worker that adds the most value in building b — FreeCol getBestWorker's "the unit that most improves
        // production wins the slot", favouring the matching expert. Candidates are a free colonist (one is implicitly idle
        // whenever IdleColonists exceeds the specialist overlay) plus each distinct idle specialist type, in ordinal order
        // so the choice is deterministic; the matching expert naturally tops the value via its index-30 modifier, and a
        // free colonist is preferred on a tie (a specialist is kept for a building it actually boosts). Returns the chosen
        // worker type and its building value, or null when no idle worker yields a positive value here.
        (string Type, int Value)? BestIdleWorkerFor(BuildingType b)
        {
            var candidates = new List<string>();
            if (colony.IdleColonists - colony.IdleWorkerTypes.Count > 0 || colony.IdleWorkerTypes.Count == 0)
            {
                candidates.Add(Colony.FreeColonistTypeId); // a free colonist is idle (or only free colonists are idle)
            }
            candidates.AddRange(colony.IdleWorkerTypes.Distinct().OrderBy(t => t, StringComparer.Ordinal));

            string? bestType = null;
            int bestValue = 0;
            foreach (string type in candidates)
            {
                int value = BuildingWorkerValue(b, type);
                if (value > bestValue)
                {
                    bestType = type;
                    bestValue = value;
                }
            }
            return bestType is null ? null : (bestType, bestValue);
        }

        // Greedily fill: each pass, staff the single highest-valued (building, best-worker) pair with a free workplace,
        // then re-evaluate (a building can take more than one colonist, up to its workplaces; its output may unlock a
        // downstream building). The chosen worker is the marginal best for that building (its matching expert if idle).
        while (colony.IdleColonists > 0)
        {
            BuildingType? best = null;
            string? bestWorker = null;
            int bestValue = 0;
            foreach (string buildingId in colony.Buildings)
            {
                BuildingType b = Ruleset.Building(buildingId);
                if (BestIdleWorkerFor(b) is not { } pick)
                {
                    continue;
                }
                if (pick.Value > bestValue || (pick.Value > 0 && pick.Value == bestValue && best is not null && string.CompareOrdinal(b.Id, best.Id) < 0))
                {
                    best = b;
                    bestWorker = pick.Type;
                    bestValue = pick.Value;
                }
            }
            if (best is null)
            {
                break; // no fundable building with a free slot → leave the rest idle (the tile planner already worked food)
            }
            AssignBuildingWork(colony, best.Id, bestWorker!);
            available.Add(Ruleset.StorageIdOf(best.Productions
                .Where(e => !e.Unattended && e.Outputs.Count > 0).SelectMany(e => e.Outputs).First().GoodsId));
        }
    }

    /// <summary>The build level of a building type — its depth in the <c>UpgradesFrom</c> chain (a base building is 1, FreeCol <c>BuildingType.getLevel</c>).</summary>
    private int BuildingLevel(BuildingType b) => 1 + (b.UpgradesFrom is { } prev ? BuildingLevel(Ruleset.Building(prev)) : 0);

    /// <summary>Whether the colony's adjacent tiles can farm <paramref name="goodsId"/> at all (the input-makeable-locally test).</summary>
    private bool ColonyCanFarm(Colony colony, string goodsId) =>
        colony.Position.Neighbours().Any(n => Map.InBounds(n) && TileYieldPotential(n, goodsId) > 0);

    /// <summary>
    /// The build-priority weight of a building (FreeCol <c>ColonyPlan</c> class weights): the max over the classes it
    /// belongs to — production/building 0.9, storage 0.85, liberty 0.75, export 0.6, military 0.4, fortify 0.3,
    /// refined-production 0.25, teach 0.2, breeding/repair 0.1 — each × its support (1.0, except liberty 0.01 at full
    /// SoL). 0 if it fits no class. <b>Deviation:</b> FreeCol's per-AI-advantage ×1.1/1.2 nudges are not applied.
    /// </summary>
    internal double BuildingBuildWeight(Colony colony, BuildingType b)
    {
        double w = 0;
        void Match(double weight, double support) => w = Math.Max(w, weight * support);
        if (b.DefenceBonus > 0 || b.BombardsShips) { Match(0.3, 1.0); }                                  // FORTIFY
        if (b.GrantsExport) { Match(0.6, 1.0); }                                                          // EXPORT
        if (b.BellBonus > 0) { Match(0.75, colony.SonsOfLiberty >= 100 ? 0.01 : 1.0); }                   // LIBERTY (printing press / newspaper)
        if (b.WarehouseStorage > 0) { Match(0.85, 1.0); }                                                 // STORAGE
        if (b.Teaches) { Match(0.2, 1.0); }                                                               // TEACH
        if (b.RepairsNavalUnits) { Match(0.1, 1.0); }                                                     // REPAIR
        if (b.BreedingDivisor > 0) { Match(0.1, 1.0); }                                                   // BREEDING
        foreach (string outGood in b.Productions.SelectMany(p => p.Outputs).Select(o => o.GoodsId).Distinct())
        {
            GoodsType g = Ruleset.Goods(outGood);
            if (g.IsMilitary) { Match(0.4, 1.0); }                                                        // MILITARY (armory → muskets)
            else if (Ruleset.BuildingMaterials.Contains(outGood) && !g.IsStorable) { Match(0.9, 1.0); }   // BUILDING (carpenter → hammers)
            else if (outGood == BellsId) { Match(0.75, colony.SonsOfLiberty >= 100 ? 0.01 : 1.0); }       // LIBERTY (a bell-producing building)
            else if (outGood == CrossesId) { Match(0.05, 1.0); }                                          // IMMIGRATION (church / chapel → crosses)
            else if (g.MadeFrom is { } input && ColonyCanFarm(colony, input)) { Match(0.25, 1.0); }       // PRODUCTION (refinery for a locally-farmed raw)
        }
        return w;
    }

    /// <summary>The artillery / wagon-train unit ids a foreign-power colony will build (FreeCol <c>ColonyPlan</c> defence + transport priorities).</summary>
    private const string WagonTrainUnitTypeId = "model.unit.wagonTrain";

    /// <summary>
    /// Plans a foreign-power colony's construction (FreeCol <c>ColonyPlan.updateBuildableTypes</c> / <c>StandardAIPlayer</c>,
    /// faithful subset). When nothing is queued, two deterministic UNIT triggers are checked first, then the building plan:
    /// <list type="number">
    /// <item><b>Artillery when under-defended</b> — if no <see cref="IsMilitaryUnit">military</see> land unit owned by the
    /// colony's owner stands on the colony tile, and <c>model.unit.artillery</c> is in <see cref="BuildableUnits"/> (needs an
    /// armory + materials), queue artillery (FreeCol prioritises defence for an undefended colony).</item>
    /// <item><b>Wagon train for inland transport</b> — else, if the colony is landlocked (no adjacent water, so no ship can
    /// serve it) and the owner owns no <c>model.unit.wagonTrain</c> yet, and the wagon is buildable, queue a wagon train.</item>
    /// <item><b>Otherwise the building plan</b> — build the highest-value building: value = <see cref="BuildingBuildWeight"/> ÷
    /// difficulty, where difficulty = <c>max(1, sqrt(Σ shortfall of required goods × (input farmable here ? 1 : 5)))</c>;
    /// buildings above the colony's size-profile level are skipped, except defence/export (always considered).</item>
    /// </list>
    /// Reuses <see cref="SetBuild"/>/<see cref="RunConstruction"/>; an in-progress build is left alone. RNG-free
    /// (ADR-009 — runs on a foreign power's own turn and draws nothing; ties break by ordinal id, never by RNG).
    /// <b>Deviation:</b> not FreeCol's full value-ranking of units against buildings or its transport-route planning — just
    /// these two unit triggers plus the building fallback (faithful-subset, see [docs]/systems/players.md).
    /// </summary>
    internal void RunForeignColonyBuildPlan(Colony colony)
    {
        if (colony.CurrentBuild is not null)
        {
            return; // don't churn an in-progress build
        }

        // 1. Buildable UNITS take precedence over buildings, in a fixed order (BuildableUnits handles the armory/hammers/
        //    tools/limit/coastal gates, so we only judge the strategic trigger here).
        List<UnitType> buildableUnits = BuildableUnits(colony).ToList();

        // 1a. Artillery for an under-defended colony: no military land unit of the owner on the colony tile.
        if (!ColonyHasMilitaryDefender(colony)
            && buildableUnits.Any(u => u.Id == ArtilleryUnitTypeId))
        {
            SetBuild(colony, ArtilleryUnitTypeId);
            return;
        }

        // 1b. Wagon train for an inland (landlocked) colony that owns no wagon train yet — its only overland carrier.
        if (!IsColonyCoastal(colony)
            && !OwnerOwnsWagonTrain(colony)
            && buildableUnits.Any(u => u.Id == WagonTrainUnitTypeId))
        {
            SetBuild(colony, WagonTrainUnitTypeId);
            return;
        }

        // 2. No unit trigger fired → fall back to the best building.
        int maxLevel = colony.Population <= 2 ? 1 : colony.Population <= 4 ? 2 : colony.Population <= 8 ? 3 : 4;

        BuildingType? best = null;
        double bestValue = 0;
        foreach (BuildingType b in Buildables(colony))
        {
            bool levelExempt = b.DefenceBonus > 0 || b.BombardsShips || b.GrantsExport;
            if (!levelExempt && BuildingLevel(b) > maxLevel)
            {
                continue;
            }
            double weight = BuildingBuildWeight(colony, b);
            if (weight <= 0)
            {
                continue;
            }
            int difficulty = b.BuildCost
                .Where(c => colony.StoreOf(Ruleset.StorageIdOf(c.GoodsId)) < c.Amount)
                .Sum(c => (c.Amount - colony.StoreOf(Ruleset.StorageIdOf(c.GoodsId)))
                          * (ColonyCanFarm(colony, Ruleset.Goods(c.GoodsId).MadeFrom ?? c.GoodsId) ? 1 : 5));
            double value = weight / Math.Max(1.0, Math.Sqrt(difficulty));
            if (value > bestValue || (value == bestValue && best is not null && string.CompareOrdinal(b.Id, best.Id) < 0))
            {
                best = b;
                bestValue = value;
            }
        }
        if (best is not null)
        {
            SetBuild(colony, best.Id);
        }
    }

    /// <summary>True when a <see cref="IsMilitaryUnit">military</see> land unit owned by the colony's owner stands on the colony tile (it has a defender) — the under-defended test for <see cref="RunForeignColonyBuildPlan"/>'s artillery trigger.</summary>
    private bool ColonyHasMilitaryDefender(Colony colony) =>
        _units.Any(u => u.OwnerId == colony.OwnerId && u.OwnerNationId is null
                        && u.IsOnMap && u.Position == colony.Position && IsMilitaryUnit(u));

    /// <summary>True when the colony's owner already owns at least one wagon train anywhere — the "skip a second wagon" test for <see cref="RunForeignColonyBuildPlan"/>'s transport trigger.</summary>
    private bool OwnerOwnsWagonTrain(Colony colony) =>
        _units.Any(u => u.OwnerId == colony.OwnerId && u.OwnerNationId is null && u.Type.Id == WagonTrainUnitTypeId);

    private void RunColonyTurn(Player owner, Colony colony)
    {
        // 1a. The colony square works itself (unattended yield). Goods enter
        //     the warehouse under their stored-as id: grain/fish → food.
        // Track this turn's gross FOOD production (centre + worked tiles) — horse breeding may eat only a share of
        // it (FreeCol consumeOnlySurplusProduction), never the colony's stored carryover.
        int foodThisTurn = 0;
        TerrainType terrain = Map.TerrainAt(colony.Position);
        foreach (ProductionEntry entry in terrain.Productions.Where(p => p.Unattended))
        {
            foreach (GoodsOutput output in entry.Outputs)
            {
                string storageId = Ruleset.StorageIdOf(output.GoodsId);
                colony.AddGoods(storageId, output.Amount);
                if (storageId == Colony.FoodId)
                {
                    foodThisTurn += output.Amount;
                }
            }
        }

        // 1b. Worked tiles produce their assigned goods, each worker getting the colony's Sons-of-Liberty
        //     production bonus (+2/+1/0/−1/−2 per worker, floored at 0 so a bad-government penalty can't go negative).
        //     Iterated row-major so the per-tile experience roll (86d3c9pgj) draws from the owner's seeded RNG in a
        //     fixed order (ADR-009) — production itself is order-insensitive (AddGoods accumulates).
        IGameRandom rng = RandomFor(owner);
        foreach ((Position tile, string goodsId) in colony.TileWorkers.OrderBy(w => w.Key.Y).ThenBy(w => w.Key.X))
        {
            string storageId = Ruleset.StorageIdOf(goodsId);
            // The working colonist's type folds its expert bonus into the yield (free colonist → no change).
            int produced = Math.Max(0, TileYield(owner, colony.WorkerTypeAt(tile), tile, goodsId) + colony.ProductionBonus);
            colony.AddGoods(storageId, produced);
            if (storageId == Colony.FoodId)
            {
                foodThisTurn += produced;
            }
            // On-the-job experience: a free colonist accrues this turn's output and may upgrade to the good's expert.
            AccrueAndRollExperience(colony, tile, goodsId, produced, rng);
        }

        // 1c. Buildings produce: unattended entries always run (town hall bell);
        //     worker entries convert inputs to outputs per colonist, limited by
        //     what the warehouse holds. Horse breeding (auto-production) may eat only this turn's surplus food.
        foreach (string buildingId in colony.Buildings)
        {
            RunBuildingProduction(colony, Ruleset.Building(buildingId), foodThisTurn);
        }

        // 1d. Construction completes when materials are saved up.
        RunConstruction(colony);

        // 1e. Warehouse overflow: a storable good produced past the colony's capacity is wasted this turn
        //     (FreeCol csNewTurnWarnings — getWarehouseCapacity). Non-storable goods (bells/crosses/hammers,
        //     which accrue toward liberty/immigration/construction) and food (consumed/grown, never warehoused
        //     to a cap here) are exempt. Run after construction so a build isn't starved of materials it consumes.
        SpillWarehouseOverflow(colony);

        // 2. Colonists eat; an unfed colonist starves (population floors at 1). Note: the classic colony-centre
        //    tile always yields ≥ 2 food (desert/arctic 2, plains 3…), exactly a lone colonist's appetite, so a
        //    size-1 colony never starves in normal play — FreeCol's "last colonist starves → colony disposed"
        //    rule only fires once food production can drop below that (disasters), deferred with that system.
        int shortfall = colony.ConsumeFood(colony.Population * Colony.FoodPerColonist);
        if (shortfall > 0 && colony.Population > 1)
        {
            colony.Population--;
            TrimAssignments(colony);
        }

        // 3. Growth: a food surplus of 200 raises a new colonist, who reports
        //    to the best free food tile.
        if (colony.Food >= Colony.FoodForGrowth)
        {
            colony.ConsumeFood(Colony.FoodForGrowth);
            colony.Population++;
            AutoAssignIdleToFood(colony);
        }

        // 3b. Schools teach: an expert in a schoolhouse/college/university raises the colony's least-skilled colonist
        //     one rung toward its expertise. After growth so the Sons-of-Liberty bonus + population are settled
        //     (FreeCol defers csCheckTeach to after the bonus recompute). No RNG (automatic student selection).
        RunSchoolTeaching(colony);

        // 4. Custom house: a colony with the export ability auto-sells each eligible good's surplus over its retain
        //    level to the owner's European market. Runs LAST — after the colony has eaten and grown (FreeCol
        //    ServerColony.csNewTurn does the customs sale after the food/birth step) — so flagging food for export
        //    can't rob this turn's growth of the food it would otherwise consume. No-op without a custom house, and —
        //    in the default PerGood mode with no toggles — sells nothing, so the L5 soak stays byte-stable.
        AutoSellExports(owner, colony);
    }

    /// <summary>Where a school student currently sits, so it can be upgraded in place (86d3c9p7f).</summary>
    private enum StudentLocation { Tile, Building, Idle }

    /// <summary>The least-skilled colonist a school's teacher can raise this turn, with its location for an in-place upgrade.</summary>
    private readonly record struct Student(int Skill, StudentLocation Where, string Type, Position Tile, string BuildingId);

    /// <summary>
    /// One colony's schooling step (86d3c9p7f, FreeCol <c>ServerBuilding.csTeach</c>): each school building's eligible
    /// expert teacher (its <see cref="UnitType.Skill"/> fits the building's <see cref="BuildingType.MaximumSkill"/>)
    /// raises the colony's least-skilled teachable colonist one rung toward the teacher's expertise — petty criminal →
    /// indentured servant → free colonist → the teacher's skill-taught — after the needed turns accrue. Needed turns are
    /// the spec base (4/6/8) reduced by the Sons-of-Liberty <see cref="Colony.ProductionBonus"/>, floored at 1. The
    /// accrued count resets when a student graduates or no eligible student is present. <b>Deterministic — no RNG</b>
    /// (classic automatic student selection): a colony with no expert in a school is a pure no-op, so the L5 soak (all
    /// free-colonist colonies) stays byte-stable. A single counter per building teaches one student at a time even in a
    /// multi-workplace college/university — a documented first-cut (see [education-schools]).
    /// </summary>
    internal void RunSchoolTeaching(Colony colony)
    {
        foreach (string buildingId in colony.Buildings)
        {
            BuildingType building = Ruleset.Building(buildingId);
            if (!building.Teaches)
            {
                continue;
            }
            // The teacher: an occupant whose skill is within the school's window (deterministic pick — Ordinal-first —
            // for the single-counter model; FreeCol getNoAddReason MINIMUM_SKILL/MAXIMUM_SKILL — the floor is ≥ 1 for
            // every classic school, so only an expert teaches).
            string? teacher = colony.BuildingOccupants(buildingId)
                .Where(t => Ruleset.Unit(t).Skill >= building.MinimumSkill && Ruleset.Unit(t).Skill <= building.MaximumSkill)
                .OrderBy(t => t, StringComparer.Ordinal)
                .FirstOrDefault();
            if (teacher is null || FindLeastSkilledStudent(colony, teacher) is not { } student)
            {
                colony.ResetSchoolTraining(buildingId); // no teacher, or no eligible student — progress lapses (FreeCol)
                continue;
            }

            colony.AddSchoolTrainingTurn(buildingId);
            int needed = Math.Max(1, Ruleset.NeededTurnsOfTraining(teacher, student.Type) - colony.ProductionBonus);
            if (colony.SchoolTrainingTurnsAt(buildingId) >= needed
                && Ruleset.GetTeachingType(teacher, student.Type) is { } target)
            {
                UpgradeStudent(colony, student, target.Id);
                colony.ResetSchoolTraining(buildingId);
            }
        }
    }

    /// <summary>
    /// The colony's least-skilled colonist a teacher of <paramref name="teacherType"/> can teach (FreeCol
    /// <c>Colony.findStudent</c>, least-skill-first), searched across worked tiles, buildings and the idle pool.
    /// <b>Trade tie-break (`86d3c9p7f` follow-up):</b> among equally-least-skilled students, one already producing the
    /// teacher's expert good (<see cref="UnitType.ExpertProduction"/>) wins — so an expert ore miner teaches the
    /// colonist already mining ore first (FreeCol <c>findStudent</c>'s <c>getWorkType() == expertise</c>). Remaining
    /// ties fall back to a stable enumeration order (tiles row-major, then buildings, then idle). Deterministic, no RNG.
    /// Null when the teacher can raise no one (e.g. every colonist is already an expert).
    /// </summary>
    private Student? FindLeastSkilledStudent(Colony colony, string teacherType)
    {
        string? expertGood = Ruleset.Unit(teacherType).ExpertProduction; // the good the teacher is expert in (null = non-goods expert)
        Student? best = null;
        bool bestWorksExpertGood = false;
        void Consider(string type, StudentLocation where, Position tile, string buildingId, bool worksExpertGood)
        {
            if (Ruleset.GetTeachingType(teacherType, type) is null)
            {
                return; // not teachable by this teacher (already at/above the taught skill, or no education rung)
            }
            int skill = Ruleset.Unit(type).Skill;
            // Lower skill always wins; on a skill tie, a student already working the teacher's good wins over one that isn't.
            if (best is null
                || skill < best.Value.Skill
                || (skill == best.Value.Skill && worksExpertGood && !bestWorksExpertGood))
            {
                best = new Student(skill, where, type, tile, buildingId);
                bestWorksExpertGood = worksExpertGood;
            }
        }
        foreach (Position tile in colony.TileWorkers.Keys.OrderBy(p => p.Y).ThenBy(p => p.X))
        {
            // TileWorkers maps a tile to the good worked there, so a tile student works the teacher's good directly.
            bool worksExpertGood = expertGood is not null && colony.TileWorkers[tile] == expertGood;
            Consider(colony.WorkerTypeAt(tile), StudentLocation.Tile, tile, "", worksExpertGood);
        }
        foreach (string b in colony.Buildings)
        {
            BuildingType buildingType = Ruleset.Building(b);
            if (buildingType.Teaches)
            {
                continue; // a colonist inside a school is staff, never a student (FreeCol's minimum-skill keeps students out of schools)
            }
            // A building student "works the teacher's good" when the building produces it (attended output).
            bool buildingMakesExpertGood = expertGood is not null && buildingType.Productions
                .Where(p => !p.Unattended)
                .SelectMany(p => p.Outputs)
                .Any(o => o.GoodsId == expertGood);
            foreach (string occupant in colony.BuildingOccupants(b))
            {
                Consider(occupant, StudentLocation.Building, default, b, buildingMakesExpertGood);
            }
        }
        foreach (string idle in colony.IdleWorkerTypes)
        {
            Consider(idle, StudentLocation.Idle, default, "", worksExpertGood: false); // idle colonists produce nothing
        }
        if (colony.IdleColonists - colony.IdleWorkerTypes.Count > 0)
        {
            Consider(Colony.FreeColonistTypeId, StudentLocation.Idle, default, "", worksExpertGood: false);
        }
        return best;
    }

    /// <summary>Promotes a school student in place at its location (tile / building / idle) to <paramref name="target"/> (86d3c9p7f).</summary>
    private static void UpgradeStudent(Colony colony, Student student, string target)
    {
        switch (student.Where)
        {
            case StudentLocation.Tile:
                colony.UpgradeTileWorker(student.Tile, target);
                break;
            case StudentLocation.Building:
                colony.UpgradeBuildingWorker(student.BuildingId, student.Type, target);
                break;
            default:
                colony.UpgradeIdleWorker(student.Type, target);
                break;
        }
    }

    /// <summary>
    /// On-the-job experience (86d3c9pgj, FreeCol <c>model.unitChange.experience</c>): a colonist working a tile accrues
    /// this turn's production as experience (capped at its type's <see cref="UnitType.MaximumExperience"/>) and then
    /// rolls a per-turn chance to upgrade in place to the tile good's matching expert. The chance is
    /// <c>experience / (100·maxExp/probability)</c>, peaking at the spec probability (classic 4%) once experience caps
    /// — matching FreeCol's <c>ServerUnit</c> roll. Fully data-driven: a worker type with no <c>maximum-experience</c>
    /// or no experience <c>&lt;unit-type-change&gt;</c> to the good's expert (every classic type except the free
    /// colonist) is ineligible and draws <b>no</b> RNG, so it never perturbs the deterministic stream.
    /// </summary>
    internal void AccrueAndRollExperience(Colony colony, Position tile, string goodsId, int produced, IGameRandom rng)
    {
        string workerType = colony.WorkerTypeAt(tile);
        if (Ruleset.ExpertForProducing(goodsId) is not { } expertType || expertType == workerType)
        {
            return; // the good has no expert, or this worker already is it
        }
        int maxExperience = Ruleset.Unit(workerType).MaximumExperience;
        int probability = Ruleset.ExperienceUpgradeProbability(workerType, expertType);
        if (maxExperience <= 0 || probability <= 0)
        {
            return; // this worker type cannot experience-upgrade to that expert (no RNG drawn)
        }

        colony.AddTileWorkerExperience(tile, produced, maxExperience);
        int maxValue = 100 * maxExperience / probability; // classic: 100·200/4 = 5000 → peak chance 200/5000 = 4%/turn
        if (maxValue > 0 && rng.Next(maxValue) < Math.Min(colony.TileWorkerExperienceAt(tile), maxExperience))
        {
            colony.UpgradeTileWorker(tile, expertType);
        }
    }

    /// <summary>
    /// A colony's warehouse capacity per storable good — the sum of its buildings' <c>warehouseStorage</c>
    /// (depot 100, warehouse 200, expansion 300; FreeCol <c>Settlement.getWarehouseCapacity</c>). Every colony
    /// has a depot (a free base building), so this is ≥ 100.
    /// </summary>
    internal int WarehouseCapacity(Colony colony) =>
        colony.Buildings.Sum(b => Ruleset.Building(b).WarehouseStorage);

    /// <summary>
    /// Discards each storable good held above the colony's warehouse capacity (FreeCol's warehouse waste).
    /// Food (consumed/grown) and non-storable goods (bells/crosses/hammers) are exempt. A guard skips a colony
    /// with no capacity data (0) so a malformed/legacy colony never silently loses everything.
    /// </summary>
    private void SpillWarehouseOverflow(Colony colony)
    {
        int capacity = WarehouseCapacity(colony);
        if (capacity <= 0)
        {
            return;
        }
        foreach (string goodsId in colony.Stores.Keys.ToList())
        {
            GoodsType goods = Ruleset.Goods(goodsId);
            int held = colony.StoreOf(goodsId);
            if (goods.IsStorable && !goods.IsFood && held > capacity)
            {
                colony.AddGoods(goodsId, capacity - held); // drop the overflow to the cap
            }
        }
    }

    /// <summary>
    /// The per-turn custom-house auto-sell (FreeCol <c>ServerColony.csNewTurn</c>'s customs sale): if the colony has
    /// the export ability (a custom house), each eligible storable, tradeable good's surplus above its retain level
    /// is sold to <paramref name="owner"/>'s European market — the same after-tax, price-moving path as a manual sale
    /// (<see cref="SellColonyGoods(Player, Colony, string, int)"/>). Eligibility follows <see cref="AutoExportMode"/>:
    /// in <see cref="GameSession.AutoExportMode.PerGood"/> only goods flagged <c>Exported</c> sell (food included if
    /// flagged — FreeCol-faithful); in <see cref="GameSession.AutoExportMode.ExportAllOverLevel"/> every sellable good
    /// does <b>except food</b> (auto-dumping food would halt growth). Goods are iterated in stable id order for
    /// determinism (ADR-009); a colony with no custom house — and the default PerGood mode with no toggles — sells
    /// nothing, so the soak stays byte-stable. (No boycott check yet — FreeCol's <c>canTrade(CUSTOM_HOUSE)</c> gate is
    /// deferred with the boycott system.) Each sale from a <b>human-owned</b> colony records a transient
    /// <see cref="CustomHouseSaleNotice"/> (good + amount + after-tax gold) the HUD surfaces after End Turn.
    /// </summary>
    private void AutoSellExports(Player owner, Colony colony)
    {
        if (!ColonyHasExportAbility(colony))
        {
            return;
        }
        bool exportAll = AutoExportMode == AutoExportMode.ExportAllOverLevel;
        foreach (string goodsId in colony.Stores.Keys.OrderBy(g => g, StringComparer.Ordinal).ToList())
        {
            GoodsType goods = Ruleset.Goods(goodsId);
            if (!goods.IsStorable || !owner.Market.IsTradeable(goodsId))
            {
                continue; // hammers/bells/crosses have no market — never auto-sold
            }
            Colony.ExportSetting setting = colony.ExportOf(goodsId);
            bool eligible = exportAll ? !goods.IsFood : setting.Exported; // export-all protects food; per-good honours the flag
            if (!eligible)
            {
                continue;
            }
            int surplus = colony.StoreOf(goodsId) - setting.ExportLevel;
            if (surplus > 0)
            {
                int gold = SellColonyGoods(owner, colony, goodsId, surplus);
                if (IsHumanOwned(colony))
                {
                    // Transient player-facing notice (ADR-006): the HUD surfaces what each custom house sold after
                    // End Turn. Only the human's colonies are recorded — foreign powers still sell, silently.
                    _customHouseSaleNotices.Add(new CustomHouseSaleNotice(colony.Id, colony.Name, goodsId, surplus, gold));
                }
            }
        }
    }

    /// <summary>Each turn a settlement's alarm cools toward 0 (FreeCol tension decay, <c>ServerPlayer</c>: −value/100 − 4).</summary>
    private static void DecayNativeAlarm(NativeSettlement settlement) =>
        settlement.Alarm = Math.Max(0, settlement.Alarm - (settlement.Alarm / 100 + 4));

    /// <summary>Extra tiles beyond a settlement's own radius within which the human's presence stirs alarm (FreeCol <c>ALARM_RADIUS</c>).</summary>
    private const int NativeAlarmRadius = 2;

    /// <summary>Alarm a human-controlled/used tile contributes to a nearby settlement each turn (FreeCol <c>ALARM_TILE_IN_USE</c>).</summary>
    private const int AlarmTileInUse = 2;

    /// <summary>Chebyshev (king-move) distance between two tiles — the grid's surrounding-tiles metric.</summary>
    private static int ChebyshevDistance(Position a, Position b) =>
        Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    /// <summary>
    /// The per-turn <b>ambient</b> native alarm (FreeCol <c>ServerPlayer.csNewTurn</c>): each settlement resents the
    /// human's nearby footprint. Within <c>settlement radius + <see cref="NativeAlarmRadius"/></c> tiles, every human
    /// <b>colony</b> adds <see cref="AlarmTileInUse"/> + its population and every human <b>offensive land unit</b> adds
    /// its type offence; the total is damped by <b>Pocahontas</b>'s <c>nativeAlarmModifier</c> (−50%) — this is that
    /// modifier's faithful home — then applied. Deterministic (no RNG; stable settlement/colony/unit iteration);
    /// runs in <see cref="EndTurn"/> just before the alarm decay. (Tile-ownership/control pressure and missionary
    /// calming are not modelled; alarm is tracked toward the human only, so foreign powers exert none.)
    /// </summary>
    private void ApplyAmbientNativeAlarm()
    {
        foreach (NativeSettlement settlement in _nativeSettlements)
        {
            int radius = Ruleset.Settlement(settlement.SettlementTypeId).ClaimableRadius + NativeAlarmRadius;
            int pressure = 0;
            foreach (Colony colony in _colonies.Where(IsHumanOwned))
            {
                // FreeCol scores each tile once (if/else-if): a colony tile holding a unit counts as that unit's
                // military pressure (the unit loop below), not the colony's — so skip a garrisoned colony here.
                if (ChebyshevDistance(colony.Position, settlement.Position) <= radius
                    && !_units.Any(u => u.IsOnMap && u.Position == colony.Position))
                {
                    pressure += AlarmTileInUse + colony.Population;
                }
            }
            foreach (Unit unit in _units.Where(u => u.IsOnMap && IsHumanOwned(u) && !u.Type.IsNaval))
            {
                if (ChebyshevDistance(unit.Position, settlement.Position) <= radius)
                {
                    pressure += (int)unit.Type.Offence; // unarmed colonists (offence 0) add nothing, as in FreeCol
                }
            }
            if (pressure > 0)
            {
                ChangeNativeAlarm(settlement, ScaleNativeAlarmGain(pressure)); // Pocahontas −50% damps the ambient gain
            }
        }
    }

    /// <summary>
    /// Records first contact between colonial players (FP-6a): when one player's explored fog now covers a tile
    /// holding another colonial player's unit or colony, both move from <see cref="Stance.Uncontacted"/> to
    /// <see cref="Stance.Peace"/> (FreeCol <c>makeContact</c>: symmetric peace, zero tension). Already-met pairs
    /// (Peace or War) are left alone. Deterministic — reads the existing fog, draws no RNG, ordered by id.
    /// </summary>
    private void DetectColonialContacts()
    {
        var colonial = _players.Where(p => p.PlayerType == PlayerType.Colonial).OrderBy(p => p.PlayerId).ToList();
        for (int i = 0; i < colonial.Count; i++)
        {
            for (int j = i + 1; j < colonial.Count; j++)
            {
                Player a = colonial[i], b = colonial[j];
                if (a.Stances.GetValueOrDefault(b.PlayerId) != Stance.Uncontacted
                    || b.Stances.GetValueOrDefault(a.PlayerId) != Stance.Uncontacted)
                {
                    continue; // already met (either direction — robust to a future directional SetStance)
                }
                if (Sees(a, b) || Sees(b, a))
                {
                    SetStance(a.PlayerId, b.PlayerId, Stance.Peace); // symmetric; tension stays 0
                }
            }
        }
    }

    /// <summary>Whether <paramref name="viewer"/>'s explored fog covers any tile holding a unit or colony owned by <paramref name="other"/>.</summary>
    private bool Sees(Player viewer, Player other) =>
        _units.Any(u => IsOwnedBy(u, other) && u.IsOnMap && viewer.Explored.Contains(u.Position))
        || _colonies.Any(c => c.OwnerId == other.PlayerId && viewer.Explored.Contains(c.Position));

    /// <summary>
    /// Each turn, colonial-pair tension cools toward 0 using the same formula as native alarm
    /// (<c>−value/100 − 4</c>) — a deliberate symmetry (FreeCol has no European tension decay; the slice scope
    /// asks for it). Decay never changes <see cref="Stance"/> directly — <see cref="UpdateColonialStances"/>
    /// derives stance from the cooled tension afterwards. No RNG.
    /// </summary>
    private void DecayColonialTension()
    {
        foreach (Player p in _players.Where(p => p.PlayerType == PlayerType.Colonial).OrderBy(p => p.PlayerId))
        {
            foreach (int otherId in p.TensionMap.Keys.OrderBy(k => k).ToList())
            {
                int t = p.TensionMap[otherId];
                p.TensionMap[otherId] = Math.Max(0, t - (t / 100 + 4));
            }
        }
    }

    /// <summary>
    /// Re-derives each colonial pair's <see cref="Stance"/> from its (just-decayed) tension via
    /// <see cref="StanceFromTension"/> (FP-6b): a war cools to cease-fire then peace as tension falls. Runs after
    /// <see cref="DecayColonialTension"/>, over recorded pairs in id order, symmetric, deterministic, no RNG.
    /// Only pairs that have met (have a stance entry) are considered, so uncontacted pairs stay uncontacted.
    /// </summary>
    private void UpdateColonialStances()
    {
        var colonial = _players.Where(p => p.PlayerType == PlayerType.Colonial).OrderBy(p => p.PlayerId).ToList();
        for (int i = 0; i < colonial.Count; i++)
        {
            for (int j = i + 1; j < colonial.Count; j++)
            {
                Player a = colonial[i], b = colonial[j];
                Stance current = a.Stances.GetValueOrDefault(b.PlayerId);
                if (current == Stance.Uncontacted)
                {
                    continue; // not yet met — only first contact promotes Uncontacted
                }
                Stance next = StanceFromTension(current, a.Tensions.GetValueOrDefault(b.PlayerId));
                if (next != current)
                {
                    SetStance(a.PlayerId, b.PlayerId, next); // symmetric
                }
            }
        }
    }

    /// <summary>Reveals (permanently explores) all tiles within the unit's line of sight for the human player.</summary>
    private void Reveal(Unit unit) => Reveal(_human, unit);

    /// <summary>Reveals all tiles within the unit's line of sight for <paramref name="player"/>.</summary>
    private void Reveal(Player player, Unit unit) => RevealAround(player, unit.Position, LineOfSightOf(unit));

    /// <summary>The line-of-sight modifier (FreeCol <c>model.modifier.lineOfSightBonus</c>): the scout role grants it, and Hernando de Soto grants +1 to all the player's <b>land</b> units (scope <c>navalUnit=false</c>).</summary>
    private const string LineOfSightBonusId = "model.modifier.lineOfSightBonus";

    /// <summary>
    /// A unit's effective sight radius: its type's <see cref="UnitType.LineOfSight"/> plus its role's
    /// <see cref="RoleType.LineOfSightBonus"/> (a scout sees +1 tile further), plus <b>Hernando de Soto</b>'s
    /// <c>model.modifier.lineOfSightBonus</c> +1 — a founding-father modifier scoped to the owner's <b>non-naval</b>
    /// units (FreeCol's <c>navalUnit=false</c> scope, honoured here by the <see cref="UnitType.IsNaval"/> gate). The
    /// father bonus is folded only for a colonial owner that holds de Soto; with no such father the sight is unchanged,
    /// so a default game's fog reveal is byte-identical (ADR-009 — the fold is RNG-free).
    /// </summary>
    private int LineOfSightOf(Unit unit)
    {
        int sight = unit.Type.LineOfSight + (int)Ruleset.Role(unit.RoleId).LineOfSightBonus;
        if (!unit.Type.IsNaval && unit.OwnerNationId is null && PlayerById(unit.OwnerId) is { } owner)
        {
            sight = ApplyGoodsModifiers(owner, LineOfSightBonusId, sight); // de Soto +1 (additive; no father → unchanged)
        }
        return sight;
    }

    /// <summary>
    /// Reveals a unit's surroundings into its <em>owning colonial player's</em> fog — the human's for a human
    /// unit, a foreign power's for its own unit (FP-4). Native-owned units lift no fog, mirroring the old behaviour.
    /// </summary>
    private void RevealForOwner(Unit unit)
    {
        if (unit.OwnerNationId is null && PlayerById(unit.OwnerId) is { } owner)
        {
            Reveal(owner, unit);
        }
    }

    /// <summary>Permanently explores every in-bounds tile within <paramref name="radius"/> of a centre for the human player.</summary>
    private void RevealAround(Position centre, int radius) => RevealAround(_human, centre, radius);

    /// <summary>Permanently explores every in-bounds tile within <paramref name="radius"/> of a centre for <paramref name="player"/>.</summary>
    private void RevealAround(Player player, Position centre, int radius)
    {
        foreach (Position p in TilesInRange(centre, radius))
        {
            player.ExploredSet.Add(p);
        }
    }

    /// <summary>The in-bounds tiles within a square (Chebyshev) <paramref name="radius"/> of a centre.</summary>
    private IEnumerable<Position> TilesInRange(Position centre, int radius)
    {
        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                var p = new Position(centre.X + dx, centre.Y + dy);
                if (Map.InBounds(p))
                {
                    yield return p;
                }
            }
        }
    }
}

/// <summary>Result of a move legality check.</summary>
/// <param name="Allowed">Whether the move may be made.</param>
/// <param name="Cost">Movement points the move would cost (when allowed).</param>
/// <param name="Reason">Why the move is rejected (when not allowed).</param>
public readonly record struct MoveCheck(bool Allowed, int Cost, string? Reason)
{
    /// <summary>An allowed move with the given cost.</summary>
    public static MoveCheck Yes(int cost) => new(true, cost, null);

    /// <summary>A rejected move with the reason shown to the player.</summary>
    public static MoveCheck No(string reason) => new(false, 0, reason);
}

/// <summary>Thrown when an illegal move is attempted directly (UI should use CheckMove first).</summary>
public sealed class InvalidMoveException : Exception
{
    /// <summary>Creates the exception with the player-facing reason.</summary>
    public InvalidMoveException(string message) : base(message) { }
}
