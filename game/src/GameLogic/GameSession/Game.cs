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

    /// <summary>
    /// Inland-lake terrain: enclosed water with no route to the open sea (FreeCol <c>makeLakes</c> retypes
    /// sea-unreachable water to this). A colony beside <em>only</em> a lake is not a port — it is excluded from the
    /// coastal naval-building gate (FreeCol <c>Tile.isCoastland</c> requires high-seas connectivity; lakes do not count).
    /// </summary>
    private const string LakeId = "model.tile.lake";

    /// <summary>The warehouse goods id for religious crosses (immigration points).</summary>
    private const string CrossesId = "model.goods.crosses";

    /// <summary>
    /// Immigration points needed for the first emigrant — the classic <c>model.option.initialImmigration</c> default
    /// (15). This is the <em>classic-default fallback</em> only (a fresh <see cref="Player.ImmigrationRequired"/> and the
    /// pre-v12 save fallback); the live value is the parsed <see cref="Specification.Ruleset.GameOptions"/> bundle
    /// (<see cref="GameOptions.InitialImmigration"/>). Aliased to <see cref="GameOptions.ClassicDefaults"/> so the
    /// bundle is the single source of truth (86d3d335r).
    /// </summary>
    public static readonly int InitialImmigration = GameOptions.ClassicDefaults.InitialImmigration;

    /// <summary>
    /// Immigration lost per person idling in Europe each turn — the classic
    /// <c>model.option.europeanUnitImmigrationPenalty</c> default (−4). Classic-default alias; the live value is read
    /// from <see cref="Specification.Ruleset.GameOptions"/> (86d3d335r).
    /// </summary>
    public static readonly int EuropeUnitImmigrationPenalty = GameOptions.ClassicDefaults.EuropeanUnitImmigrationPenalty;

    /// <summary>
    /// Flat immigration a colonial player gains each turn — the classic <c>model.option.playerImmigrationBonus</c>
    /// default (+2). Classic-default alias; the live value is read from <see cref="Specification.Ruleset.GameOptions"/>
    /// (86d3d335r).
    /// </summary>
    public static readonly int PlayerImmigrationBonus = GameOptions.ClassicDefaults.PlayerImmigrationBonus;

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

    /// <summary>RNG stream reserved for the per-turn natural-disaster roll (86d3c9uu8) — a high id like
    /// <see cref="LcrStreamId"/>/<see cref="ResourceQuantityStreamId"/>, so the disaster draw never correlates with or
    /// shifts the human's economy stream 0 (ADR-006/ADR-009). The roll fires only when the ruleset's
    /// <see cref="Specification.Ruleset.NaturalDisasterPercentage"/> is above 0 (classic default 0 → never), so the
    /// classic default game draws nothing on this stream and stays byte-identical.</summary>
    private const ulong DisasterStreamId = 103;

    /// <summary>RNG stream reserved for native-trade <b>haggling</b> (86d3e4bh2) — a high id like
    /// <see cref="LcrStreamId"/>, so the offer/counter patience roll never correlates with or shifts the human's economy
    /// stream 0 (ADR-009). Haggling is a human-only interactive action (the AI never calls it) and is not part of
    /// turn-replay, so this stream's running state is <b>not serialized</b>: it is seeded lazily from the game's base
    /// state (<see cref="_nativeHaggleRandom"/>). The soak/twin-determinism runs never haggle, so the default game draws
    /// nothing on it and round-trips byte-identically.</summary>
    private const ulong NativeTradeStreamId = 104;

    private Pcg32Random? _nativeHaggleRandomField; // lazily seeded from the base state; see _nativeHaggleRandom

    /// <summary>The lazily-seeded native-haggle RNG (<see cref="NativeTradeStreamId"/>), derived from the game's base
    /// state so it is deterministic per game without being serialized — haggling is interactive and not replayed.</summary>
    private Pcg32Random _nativeHaggleRandom =>
        _nativeHaggleRandomField ??= new Pcg32Random(_random.SaveState().State, NativeTradeStreamId);

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
    private readonly List<AttritionNotice> _attritionNotices = []; // transient: the most recent turn's units lost to attrition in the open (not saved)
    private readonly List<DisasterNotice> _disasterNotices = []; // transient: the most recent turn's natural disasters striking human colonies (not saved; empty in classic — naturalDisasters default 0)
    private readonly List<ColonyStarvedNotice> _colonyStarvedNotices = []; // transient: human colonies destroyed by starvation this turn (not saved; empty in classic — centre tile feeds the last colonist)
    private readonly List<ColonyFamineNotice> _colonyFamineNotices = []; // transient: human colonies that lost a colonist (but survived) to famine this turn (not saved; empty in classic)
    private readonly List<WarehouseOverflowNotice> _warehouseOverflowNotices = []; // transient: human-colony storable goods wasted over warehouse capacity this turn (not saved)
    private readonly List<MonarchDecreeNotice> _monarchDecreeNotices = []; // transient: immediate (no-choice) monarch actions taken this turn (not saved; empty before the grace period)
    private readonly List<RefLandingNotice> _refLandingNotices = []; // transient: the one-off "the REF has landed" warning fired the first time the King's army comes ashore (not saved; empty until then)
    private readonly List<FirstContactNotice> _firstContactNotices = []; // transient: the human's first contacts with rival colonial powers this turn (not saved)
    private readonly List<StanceChangeNotice> _stanceChangeNotices = []; // transient: turn-driven (tension-derived) stance shifts involving the human this turn (not saved)
    private readonly List<PriceChangeNotice> _priceChangeNotices = []; // transient: the human's Europe-market goods whose price moved this turn (not saved; rebuilt each EndTurn by comparing the live ask prices to the per-turn baseline)
    private readonly Dictionary<string, int> _priceSnapshot = []; // transient: each human-market good's buy (ask) price as the human last saw it (the baseline _priceChangeNotices compares against; seeded at New/Restore, re-baselined each EndTurn — not saved)
    private readonly List<TemporaryModifier> _temporaryModifiers = []; // transient: duration-bounded modifiers currently in force; empty in classic (nothing registers one), so never saved and the default game is byte-identical (86d3drpgz)
    private NativeDemand? _pendingDemand; // transient: a native tribute demand awaiting the human's accept/refuse (not saved)
    private PendingMoundsDecision? _pendingMounds; // transient: a strange-mounds rumour awaiting the human's investigate/decline (not saved)
    private FountainResult _lastFountainResult; // transient: how the most recent FoY burst was handled — picks the player-facing message in ExploreRumour
    private int _citiesOfCibolaRemaining = CibolaCityCount; // persisted (v63): the finite "Seven Cities of Gold" left to discover; once 0 a Cibola roll degrades to ordinary ruins (FreeCol NameCache.getNextCityOfCibola)
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

    /// <summary>
    /// How this game treats national advantages (FreeCol <c>model.option.nationalAdvantages</c>, New-Game dial
    /// 86d3fq0za). <see cref="Specification.NationalAdvantages.None"/> suppresses every nation-type advantage — the
    /// advantage modifiers folded by <see cref="NationTypeModifiers"/> and the nation-specific starting-unit upgrades —
    /// so a chosen nation plays with the neutral default roster and no bonuses. Defaults to
    /// <see cref="Specification.NationalAdvantages.Selectable"/> (advantages on), so a default game is byte-identical
    /// (ADR-009). Session-only — not persisted (a reloaded game re-derives the default, matching the other New-Game
    /// configuration seams).
    /// </summary>
    public NationalAdvantages NationalAdvantages { get; private init; } = NationalAdvantages.Selectable;

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
        Stance previous = PlayerById(a)!.StanceMap.GetValueOrDefault(b);
        // A transition *into* war that involves the human is a notable history event (recorded once, on the change).
        bool wasWar = previous == Stance.War;
        if (stance == Stance.War && !wasWar && (a == _human.PlayerId || b == _human.PlayerId))
        {
            int rival = a == _human.PlayerId ? b : a;
            RecordHistory(HistoryEventKind.WarDeclared, $"War broke out with the {NationDisplayName(rival)}.");
        }
        // Stamp / clear the peace-turn for FreeCol's decaying peace-hold (EuropeanAIPlayer.peaceHolds' peaceTurn):
        // a transition *into* Peace/Alliance records the turn the treaty took force; a declaration of War clears it.
        // Only on a genuine stance CHANGE — re-asserting an existing Peace must not reset the clock (FreeCol scans the
        // history once per MAKE_PEACE/FORM_ALLIANCE event, not per turn). Symmetric, so either party's view agrees.
        if (stance != previous)
        {
            StampPeaceTurn(a, b, stance, symmetric);
        }
        PlayerById(a)!.StanceMap[b] = stance;
        if (symmetric)
        {
            PlayerById(b)!.StanceMap[a] = stance;
        }
    }

    /// <summary>
    /// Records (or clears) the turn a colonial pair's peace took force, for the decaying peace-hold in
    /// <see cref="PeaceTreatyHolds"/> (FreeCol <c>EuropeanAIPlayer.peaceHolds</c>' <c>peaceTurn</c>). A transition into
    /// <see cref="Stance.Peace"/>/<see cref="Stance.Alliance"/> stamps the current <see cref="Turn"/>; a transition into
    /// <see cref="Stance.War"/> removes the stamp (FreeCol's <c>DECLARE_WAR → peaceTurn = -1</c>). A
    /// <see cref="Stance.CeaseFire"/> (a truce, not a treaty) leaves the existing stamp untouched. Called only on a
    /// genuine stance change, symmetrically by default so either party's <see cref="Player.PeaceTurns"/> agrees.
    /// </summary>
    private void StampPeaceTurn(int a, int b, Stance stance, bool symmetric)
    {
        switch (stance)
        {
            case Stance.Peace or Stance.Alliance:
                PlayerById(a)!.PeaceTurnMap[b] = Turn;
                if (symmetric)
                {
                    PlayerById(b)!.PeaceTurnMap[a] = Turn;
                }
                break;
            case Stance.War:
                PlayerById(a)!.PeaceTurnMap.Remove(b);
                if (symmetric)
                {
                    PlayerById(b)!.PeaceTurnMap.Remove(a);
                }
                break;
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

    /// <summary>
    /// True when the colony is a connected port — adjacent to <b>open-sea</b> water (FreeCol
    /// <c>Settlement.isConnectedPort</c> / <c>Tile.isCoastland</c>: <c>isLand() &amp;&amp; getHighSeasCount() &gt; 0</c>).
    /// An adjacent ocean or high-seas tile is sea-connected; an inland lake (<see cref="LakeId"/>) is enclosed water
    /// with no route to Europe, so a colony beside <em>only</em> a lake is land-locked from the sea and is <b>not</b>
    /// coastal. Drives the docks/drydock/shipyard build gate (<c>hasPort</c>) — naval buildings need a real port,
    /// excluding lake-side colonies exactly as FreeCol does.
    /// </summary>
    private bool IsColonyCoastal(Colony colony) =>
        colony.Position.Neighbours().Any(n =>
            Map.InBounds(n) && Map.TerrainAt(n).IsWater && Map.TerrainAt(n).Id != LakeId);

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

    /// <summary>
    /// Whether <paramref name="colony"/> has a custom house — i.e. a building that grants the auto-export ability
    /// (today only <c>model.building.customHouse</c>). The colony screen gates its per-good export controls on this:
    /// the export section only renders when the colony can actually auto-sell (the engine's <see cref="AutoSellExports"/>
    /// likewise no-ops without it). Public read-only oracle for the presentation (ADR-006); mirrors the private
    /// <see cref="ColonyHasExportAbility"/> the colony turn uses.
    /// </summary>
    public bool ColonyHasCustomHouse(Colony colony) => ColonyHasExportAbility(colony);

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
    // consume this read; nothing here mutates state, so the default game stays byte-identical (ADR-009) and the score
    // itself is NOT persisted — it is recomputed from current state each time it is read, exactly as FreeCol recomputes
    // it (FreeCol caches the result in a serialized field for the network protocol; we have no such need, so we keep it
    // a pure read). The history-event summand reads the persisted history log (saved from v58), so the discovery and
    // destruction scores it folds in survive a save/load — but the score value is still derived on read, never stored.

    /// <summary>Score bonus for each founding father (FreeCol <c>ServerPlayer.SCORE_FOUNDING_FATHER</c> = 5; Col1).</summary>
    private const int ScoreFoundingFather = 5;

    /// <summary>Gold-to-score rate: 1 point per 1000 gold (FreeCol <c>ServerPlayer.SCORE_GOLD</c> = 0.001; Col1).</summary>
    private const double ScoreGold = 0.001;

    /// <summary>
    /// Score penalty for razing a native settlement (FreeCol <c>ServerPlayer.SCORE_SETTLEMENT_DESTROYED</c> = −5; Col1).
    /// Recorded as a <see cref="HistoryEventKind.SettlementDestroyed"/> event and folded into the human's score.
    /// (FreeCol's <c>destroySettlementScore</c> <i>game option</i> defaults to −2 in the classic ruleset; we use the
    /// <c>SCORE_SETTLEMENT_DESTROYED</c> code constant −5 the task pins, keeping the penalty a fixed, faithful value.)
    /// </summary>
    internal const int ScoreSettlementDestroyed = -5;

    /// <summary>Score penalty for wiping out a native nation — razing its last settlement (FreeCol <c>ServerPlayer.SCORE_NATION_DESTROYED</c> = −50; FreeCol extension).</summary>
    internal const int ScoreNationDestroyed = -50;

    /// <summary>Percentage score bonus for being the first power to win independence (FreeCol <c>SCORE_INDEPENDENCE_BONUS_FIRST</c> = 100; Col1).</summary>
    private const int ScoreIndependenceBonusFirst = 100;

    /// <summary>Percentage score bonus for the second power to win independence (FreeCol <c>SCORE_INDEPENDENCE_BONUS_SECOND</c> = 50; Col1).</summary>
    private const int ScoreIndependenceBonusSecond = 50;

    /// <summary>Percentage score bonus for the third power to win independence (FreeCol <c>SCORE_INDEPENDENCE_BONUS_THIRD</c> = 25; Col1).</summary>
    private const int ScoreIndependenceBonusThird = 25;

    /// <summary>
    /// The score value of one unit, read straight off its parsed type (FreeCol <c>UnitType.getScoreValue</c>): the
    /// <c>score-value</c> attribute the spec sets per <c>unit-type</c> (criminal 1 .. man-o-war 8), inherited down the
    /// <c>extends</c> chain at parse time. 0 for a type the spec gives no <c>score-value</c> (braves/native units), so
    /// they never count toward a colonial score. Data-driven — a variant ruleset's score table needs no code change.
    /// </summary>
    private static int UnitScoreValue(Unit unit) => unit.Type.ScoreValue;

    /// <summary>
    /// The score value of a unit <b>type</b> id (FreeCol <c>UnitType.getScoreValue</c>), read off the parsed type via
    /// <see cref="Ruleset.UnitScoreValue"/>; 0 for an unknown id. Used for colony-worker colonists, which are colony
    /// population (type-ids) rather than entries in the unit list.
    /// </summary>
    private int UnitScoreValue(string unitTypeId) => Ruleset.UnitScoreValue(unitTypeId);

    /// <summary>
    /// The Σ score value of every colonist working inside <paramref name="player"/>'s colonies (FreeCol's
    /// <c>sum(getUnits(), Unit::getScoreValue)</c> counts colony workers, which in our model are colony <i>population</i>
    /// rather than entries in the unit list). Each colony's colonists are its non-free overlay types — tile workers,
    /// building occupants, and idle colonists — plus free colonists (score 3) padding out the rest of its
    /// <see cref="Colony.Population"/>. A free-colonist-only colony therefore contributes <c>3 × Population</c>.
    /// </summary>
    private int ColonyWorkerScore(Player player) =>
        ColoniesOf(player).Sum(c =>
        {
            int nonFree = c.TileWorkerTypes.Values.Sum(UnitScoreValue)
                + c.BuildingWorkerTypes.Values.SelectMany(occupants => occupants).Sum(UnitScoreValue)
                + c.IdleWorkerTypes.Sum(UnitScoreValue);
            int nonFreeCount = c.TileWorkerTypes.Count
                + c.BuildingWorkerTypes.Values.Sum(occupants => occupants.Count)
                + c.IdleWorkerTypes.Count;
            int freeColonists = c.Population - nonFreeCount; // the remainder are free colonists
            return nonFree + freeColonists * UnitScoreValue(Colony.FreeColonistTypeId);
        });

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
    /// colony workers are colony <em>population</em>, not unit-list entries, so they are scored explicitly via
    /// <see cref="ColonyWorkerScore"/> and added to the map/Europe units — the unit summand now matches FreeCol.
    /// (2) FreeCol folds in per-event history scores via <c>HistoryEvent.getScore()</c>; our <see cref="HistoryEvent"/>
    /// carries a numeric <c>Score</c> and these events score it, folded into the <b>human's</b> total here (the history
    /// log is the human's): <b>region-discovery</b> (positive, <c>86d3c9w2f</c>) and the <b>settlement/nation-destruction
    /// penalties</b> (−5 / −50). Lost-city finds are recorded score-less (their value rides the treasure → gold summand),
    /// exactly as FreeCol. The history log — and the scores it carries — is <b>persisted</b> from save v58, so a
    /// discovery's points and an atrocity's penalty survive a save/load round-trip.
    /// (3) FreeCol derives the independence ordinal from an INDEPENDENCE history event's stored place (0/1/2); with a single
    /// human player, an <see cref="PlayerType.Independent"/> nation is the first to win, so it takes the 100% first-place bonus.</para>
    /// </summary>
    /// <param name="player">The player to score.</param>
    /// <returns>The player's score (may be negative once destruction penalties land; today it is ≥ 0).</returns>
    public int PlayerScore(Player player) => ScoreBreakdown(player).Total;

    /// <summary>
    /// The itemised components of <see cref="PlayerScore"/> for <paramref name="player"/> — the same pure, RNG-free read
    /// (FreeCol <c>ServerPlayer.updateScore</c>), exposed line-by-line so the victory / end-of-game screen can show the
    /// player <i>why</i> their score is what it is without re-deriving the formula. <see cref="PlayerScore"/> is exactly
    /// <c>ScoreBreakdown(player).Total</c>, keeping this the single source of truth. Like <see cref="PlayerScore"/> it
    /// mutates nothing, draws no randomness and is never persisted (no save-version bump) — see the section header.
    /// </summary>
    /// <param name="player">The player to break the score down for.</param>
    /// <returns>The summands (unit values, colony liberty, founding-father points, gold points, history-event points)
    /// and the independence percentage bonus, from which <see cref="ScoreComponents.Total"/> follows.</returns>
    public ScoreComponents ScoreBreakdown(Player player)
    {
        // FreeCol's sum(getUnits(), Unit::getScoreValue) counts every owned unit, including colonists working inside
        // colonies. Our colony workers are colony population (not unit-list entries), so we add their score explicitly
        // (ColonyWorkerScore) alongside the map/Europe units — closing the old "colony workers don't score" deviation.
        int unitValues = _units.Where(u => IsOwnedBy(u, player)).Sum(UnitScoreValue) + ColonyWorkerScore(player);
        int colonyLiberty = ColoniesOf(player).Sum(c => c.Liberty);
        int fatherPoints = ScoreFoundingFather * player.Congress.Count;
        int goldPoints = (int)Math.Floor(ScoreGold * player.Gold);

        // History-event scores (FreeCol folds in HistoryEvent.getScore — region discovery today). The history log is
        // the human's only, so this contributes solely to the human's score; a foreign power scores its own units etc.
        // but no history events. This was a documented scoring TODO until region discovery landed (86d3c9w2f).
        int historyPoints = player.IsHuman ? HistoryEventScore : 0;

        int bonusPercent = IndependenceScoreBonusPercent(player);
        return new ScoreComponents(unitValues, colonyLiberty, fatherPoints, goldPoints, historyPoints, bonusPercent);
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

    /// <summary>
    /// Builds a leaderboard <see cref="HighScore"/> entry for <paramref name="player"/> from the current game state —
    /// a faithful port of FreeCol's <c>new HighScore(player)</c> constructor (it reads the same fields off the player
    /// and game): the final <see cref="PlayerScore"/> and the honorific it earns, the nation/nation-type ids, the
    /// difficulty, the player's end-of-game unit and colony counts, the retirement turn (the current turn) and — if the
    /// player has won independence — the turn it was declared. <paramref name="won"/> records victory vs. defeat (our
    /// flag in place of FreeCol's player-type read). A <b>pure, RNG-free read</b> (ADR-009): it draws no randomness and
    /// mutates nothing, so building a score leaves the game byte-identical; persistence is the caller's concern
    /// (<see cref="Persistence.HighScoreStore"/>), kept entirely separate from the game save.
    /// </summary>
    /// <param name="player">The player to record — normally the human (the leaderboard tracks the human's games).</param>
    /// <param name="won"><c>true</c> for a victory, <c>false</c> for a defeat.</param>
    /// <param name="gameId">An optional per-game id for de-duplication (a later higher score from the same game replaces the earlier); empty if unknown.</param>
    /// <returns>The completed high-score entry, dated <see cref="DateTime.UtcNow"/>.</returns>
    public HighScore RecordHighScore(Player player, bool won, string gameId = "")
    {
        int score = PlayerScore(player);
        return new HighScore(
            PlayerName: PlayerDisplayName(player),
            NationId: player.NationId ?? "",
            NationTypeId: NationTypeIdOf(player),
            Score: score,
            Level: ScoreLevels.ForScore(score),
            Difficulty: DifficultyLevelId,
            UnitCount: _units.Count(u => IsOwnedBy(u, player)),
            ColonyCount: ColoniesOf(player).Count(),
            RetirementTurn: Turn,
            IndependenceTurn: player.PlayerType == PlayerType.Independent
                ? player.DeclaredIndependenceTurn ?? Turn
                : -1,
            Won: won,
            DateUtc: DateTime.UtcNow,
            GameId: gameId);
    }

    /// <summary>
    /// The display name for a player on the leaderboard and everywhere the player's nation is labelled (FreeCol
    /// <c>Player.getNationLabel</c> / <c>getName</c>). Once the player has declared independence — it is a
    /// <see cref="PlayerType.Rebel"/> or <see cref="PlayerType.Independent"/> — this is the free nation's chosen
    /// <see cref="Player.IndependentNationName"/> (e.g. "United States"), exactly as FreeCol's <c>getNationLabel</c>
    /// switches to <c>independentNationName</c> for those types. A rebel that named itself blank, and every player that
    /// has not declared, falls through to the colonial nation's display name — the capitalised tail of the nation id
    /// (e.g. <c>model.nation.dutch</c> → "Dutch") — or "Anonymous" for a nation-less default game, mirroring FreeCol's
    /// "anonymous" fallback.
    /// </summary>
    private static string PlayerDisplayName(Player player)
    {
        // A declared nation labels itself by the name it chose on declaring (FreeCol getNationLabel → independentNationName).
        if (player.PlayerType is PlayerType.Rebel or PlayerType.Independent
            && player.IndependentNationName is { Length: > 0 } chosen)
        {
            return chosen;
        }
        if (player.NationId is { } id && id.LastIndexOf('.') is var dot && dot + 1 < id.Length)
        {
            string tail = id[(dot + 1)..];
            return char.ToUpperInvariant(tail[0]) + tail[1..];
        }
        return "Anonymous";
    }

    /// <summary>The player's nation-type id (national advantage), resolved through the ruleset; null if the game had no nation pick.</summary>
    private string? NationTypeIdOf(Player player) =>
        player.NationId is { } nationId
            ? Ruleset.EuropeanNations.FirstOrDefault(n => n.Id == nationId)?.NationType?.Id
            : null;

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

    /// <summary>
    /// Units a player lost to <b>attrition</b> — wasting away after too many turns standing in the open wilderness —
    /// during the most recent <see cref="EndTurn"/> (86d3drmzp). Transient per-turn UI scratch (refreshed each
    /// <c>EndTurn</c>'s attrition step, never saved); the presentation reads it after the turn resolves to tell the
    /// player "your X wasted away in the wilderness near Y" (FreeCol's <c>model.unit.attrition</c> UNIT_LOST message).
    /// In the classic ruleset only the Indian Convert is ever subject to attrition, so this is normally empty.
    /// </summary>
    public IReadOnlyList<AttritionNotice> AttritionNotices => _attritionNotices;

    /// <summary>
    /// Natural disasters that struck the human's colonies during the most recent <see cref="EndTurn"/> (86d3c9uu8).
    /// Transient per-turn UI scratch (refreshed each <c>EndTurn</c>, never saved); the presentation reads it after the
    /// turn resolves to tell the player a colony was hit. Disasters roll only when the ruleset's
    /// <see cref="Ruleset.NaturalDisasterPercentage"/> is above 0 (classic default 0), so this is empty in the classic
    /// default game (FreeCol <c>ServerPlayer.csNaturalDisasters</c>).
    /// </summary>
    public IReadOnlyList<DisasterNotice> DisasterNotices => _disasterNotices;

    /// <summary>
    /// Human colonies <b>destroyed by starvation</b> during the most recent <see cref="EndTurn"/> (FreeCol
    /// <c>ServerColony.csNewTurn</c>'s <c>model.colony.colonyStarved</c> → <c>csDisposeSettlement</c>). Transient
    /// per-turn UI scratch (cleared each <c>EndTurn</c>, never saved); the presentation reads it after the turn
    /// resolves to tell the player a colony starved out of existence. A colony only starves once its food production
    /// plus stored carryover can no longer feed its <b>last</b> colonist; the classic colony-centre tile always
    /// yields ≥ 2 food (a lone colonist's appetite), so this is empty in the classic default game (and the L5 soak
    /// keeps every colony).
    /// </summary>
    public IReadOnlyList<ColonyStarvedNotice> ColonyStarvedNotices => _colonyStarvedNotices;

    /// <summary>
    /// Human colonies that <b>lost a colonist to famine</b> (but survived) during the most recent <see cref="EndTurn"/>
    /// — the survivable sibling of <see cref="ColonyStarvedNotices"/> (FreeCol <c>ServerColony.csNewTurn</c>'s
    /// <c>model.colony.colonyStarving</c> per-turn famine victim). Transient per-turn UI scratch (cleared each
    /// <c>EndTurn</c>, never saved); the presentation reads it after the turn resolves to warn the player a colony is
    /// starving. A colony loses a colonist only when its food production plus stored carryover can no longer feed its
    /// whole population <b>and</b> more than one colonist remains; in the classic default game production never falls
    /// that low, so this is normally empty.
    /// </summary>
    public IReadOnlyList<ColonyFamineNotice> ColonyFamineNotices => _colonyFamineNotices;

    /// <summary>
    /// Storable goods a human colony produced <b>past its warehouse capacity</b> during the most recent
    /// <see cref="EndTurn"/>, so the surplus was wasted (FreeCol <c>ServerColony.csNewTurn</c>'s warehouse-overflow
    /// warning). Transient per-turn UI scratch (cleared each <c>EndTurn</c>, never saved); the presentation reads it
    /// after the turn resolves to warn the player which goods are spilling. One entry per (colony, good) that
    /// overflowed; empty when nothing spilled.
    /// </summary>
    public IReadOnlyList<WarehouseOverflowNotice> WarehouseOverflowNotices => _warehouseOverflowNotices;

    /// <summary>
    /// Immediate <b>King's-decree</b> actions the home-nation Monarch took on the human's behalf during the most recent
    /// <see cref="EndTurn"/> — lower/waive tax, declare war/peace, grant free support, or grow the Royal Expeditionary
    /// Force (FreeCol's auto-applied monarch actions, distinct from the tax-rise / mercenary <em>demands</em> that
    /// surface through <see cref="PendingMonarchDemand"/>). Transient per-turn UI scratch (cleared each <c>EndTurn</c>,
    /// never saved); the presentation reads it after the turn resolves to tell the player what the King decreed. Empty
    /// before the monarch grace period.
    /// </summary>
    public IReadOnlyList<MonarchDecreeNotice> MonarchDecreeNotices => _monarchDecreeNotices;

    /// <summary>
    /// The one-off <b>"the Royal Expeditionary Force has landed"</b> warning, produced the first time REF units came
    /// ashore during the most recent <see cref="EndTurn"/> after the human declared independence (FreeCol's REF-landing
    /// turn message). At most one entry, and never re-produced on the later staggered reinforcement waves. Transient
    /// per-turn UI scratch (cleared each <c>EndTurn</c>, never saved); the presentation reads it after the turn resolves.
    /// </summary>
    public IReadOnlyList<RefLandingNotice> RefLandingNotices => _refLandingNotices;

    /// <summary>
    /// The human's <b>first contacts</b> with rival colonial powers during the most recent <see cref="EndTurn"/> — each
    /// turn the human's explored fog first covered that power's unit or colony, flipping the pair
    /// <see cref="Stance.Uncontacted"/> → <see cref="Stance.Peace"/> (FreeCol <c>makeContact</c>). Transient per-turn UI
    /// scratch (cleared each <c>EndTurn</c>, never saved); the presentation reads it after the turn resolves to announce
    /// the new acquaintance. Empty when the human met no one this turn. Only human-involving contacts are recorded
    /// (foreign-foreign meetings stay silent). RNG-free / deterministic (ADR-009).
    /// </summary>
    public IReadOnlyList<FirstContactNotice> FirstContactNotices => _firstContactNotices;

    /// <summary>
    /// Turn-driven (tension-derived) <b>stance shifts</b> involving the human during the most recent <see cref="EndTurn"/>
    /// — when <see cref="UpdateColonialStances"/> re-derived a met pair's <see cref="Stance"/> from its cooled tension and
    /// found it changed (war → cease-fire → peace as tension falls, or a peace a rival breaks). Transient per-turn UI
    /// scratch (cleared each <c>EndTurn</c>, never saved); the presentation reads it after the turn resolves to tell the
    /// human the relationship moved. Empty when no automatic shift involving the human happened. Player-initiated stance
    /// changes (treaties, declared wars) are NOT recorded here — those have their own diplomacy-screen feedback. RNG-free
    /// / deterministic (ADR-009).
    /// </summary>
    public IReadOnlyList<StanceChangeNotice> StanceChangeNotices => _stanceChangeNotices;

    /// <summary>
    /// The human's Europe-market goods whose <b>price changed</b> over the most recent <see cref="EndTurn"/> — one entry
    /// per good whose buy (ask) price differs from the baseline (the price the player last saw, recorded at the end of the
    /// previous turn; FreeCol <c>ServerPlayer.csFlushMarket</c>'s <c>model.market.priceIncrease</c>/<c>priceDecrease</c>
    /// message). The market only moves when something trades (a Europe sell/buy, a custom-house auto-sale, a trade-route
    /// delivery), and that movement returns nothing the human UI reads after End Turn, so the game keeps a per-turn ask
    /// baseline (seeded at <see cref="New"/>/<see cref="Restore"/>) and re-derives this list when the turn resolves, then
    /// re-baselines. The watched window spans the whole turn — the human's own pre-End-Turn trades and the turn's
    /// custom-house/trade-route activity. Transient per-turn UI scratch (rebuilt each <c>EndTurn</c>, never saved); the
    /// presentation reads it after the turn to tell the player which goods rose or fell. Emitted in the market's stable
    /// goods order, so the sequence is deterministic. RNG-free (ADR-009) — it only compares two recorded prices, drawing
    /// no randomness. Empty when no human-market price moved this turn (the common case).
    /// </summary>
    public IReadOnlyList<PriceChangeNotice> PriceChangeNotices => _priceChangeNotices;

    /// <summary>
    /// The duration-bounded modifiers currently registered (FreeCol's temporary <c>Modifier</c>s — those carrying a
    /// <c>firstTurn</c>/<c>lastTurn</c>). A modifier stays here from the turn it is registered until the
    /// <see cref="EndTurn"/> that finds it <see cref="TemporaryModifier.IsOutOfDate"/>, when the per-turn strip
    /// (<see cref="RemoveExpiredTemporaryModifiers"/>) removes it. <b>Transient</b> — never serialized; in the classic
    /// ruleset nothing ever registers one, so this is always empty and the default game is byte-identical (ADR-009).
    /// </summary>
    public IReadOnlyList<TemporaryModifier> TemporaryModifiers => _temporaryModifiers;

    /// <summary>
    /// Registers a duration-bounded modifier so it folds into matching values while active and is stripped once it
    /// expires (FreeCol <c>ChangeSet.addModifier</c> of a <c>makeTimedModifier</c> result). This is the only way a
    /// temporary modifier enters play; no classic content calls it, which is why the default game registers none and
    /// stays byte-identical. Use <see cref="TemporaryModifier.MakeTimed"/> to build one bounded to a duration.
    /// </summary>
    /// <param name="modifier">The duration-bounded modifier to add.</param>
    internal void RegisterTemporaryModifier(TemporaryModifier modifier) => _temporaryModifiers.Add(modifier);

    /// <summary>
    /// The temporary modifiers targeting <paramref name="targetId"/> that are active on the current <see cref="Turn"/>
    /// (FreeCol <c>FeatureContainer.getModifiers</c> filtered by <c>appliesTo(turn)</c>): a registered modifier
    /// contributes only inside its <c>[firstTurn, lastTurn]</c> window. Empty whenever the registry is empty (always,
    /// in the classic default game), so a caller that folds these is a no-op there. <paramref name="colonyId"/> scopes
    /// the query to one colony's production: a colony-scoped modifier (a disaster penalty) is returned only when its
    /// colony matches; an unscoped modifier (a variant/event bonus) is always returned; a <c>null</c> id (a non-colony
    /// fold — movement, sail time) returns only unscoped modifiers, so a colony-scoped penalty never touches them.
    /// </summary>
    /// <param name="targetId">The modifier target to match (e.g. a goods id).</param>
    /// <param name="colonyId">The colony whose production is being folded, or <c>null</c> for a non-colony fold (returns only unscoped modifiers).</param>
    /// <returns>The active temporary modifiers for that target and colony scope, in registration order.</returns>
    public IEnumerable<TemporaryModifier> ActiveTemporaryModifiers(string targetId, int? colonyId = null) =>
        _temporaryModifiers.Where(m => m.TargetId == targetId && m.AppliesTo(Turn) && m.AppliesToColony(colonyId));

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

    /// <summary>Alarm added to the robbed nation when land is <em>taken</em> rather than bought (FreeCol <c>Tension.TENSION_ADD_LAND_TAKEN</c>). The FreeCol-source pin; the runtime reads the data-overridable <see cref="Specification.NativeTensionOptions.LandTaken"/> (which defaults to this).</summary>
    internal const int LandTakenAlarm = NativeSettlement.TensionAddLandTaken;

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
            ChangeNativeAlarm(settlement, player.PlayerId, Ruleset.Difficulty.NativeTension.LandTaken); // FreeCol TENSION_ADD_LAND_TAKEN toward the robber, nation-wide (ownership is tracked per nation)
        }
    }

    // ----- Forced buy-or-steal-or-abandon claim trigger (86d3e4bj7) ------------------------------------------
    // Founding a colony on, or working, a native-OWNED tile is no longer free: it forces a claim FIRST (FreeCol
    // ServerPlayer.csClaimLand, invoked from InGameController.claimLand before BuildColonyMission builds). The human's
    // pay-vs-steal choice is a UI dialog (GameController.FoundColonyWithClaim / AssignWorkWithClaim); the AI resolves
    // it deterministically (AiResolveLandClaim — RNG-free, ADR-009). Abandon is simply not calling the action. The
    // claim is resolved synchronously through the existing ClaimLandByPaying/Stealing paths — NO pending-claim state is
    // stored, so the save format is unchanged.

    /// <summary>Whether founding/working a tile must first resolve a forced native-land claim, and at what cost.</summary>
    /// <param name="Required">True when the tile is native-owned (and not a settlement tile), so a
    /// <see cref="LandClaimChoice"/> must be made before the tile can be founded on or worked.</param>
    /// <param name="BuyPrice">The gold the buy option costs (the <see cref="LandPrice(Player, Position)"/>; 0 under Peter
    /// Minuit, or when no claim is required).</param>
    /// <param name="OwningNation">The native nation type id that owns the tile (e.g. <c>model.nationType.apache</c>), or null when no claim is required.</param>
    public readonly record struct ForcedLandClaim(bool Required, int BuyPrice, string? OwningNation);

    /// <summary>Whether founding/working <paramref name="tile"/> forces the <b>human</b> into a buy-or-steal-or-abandon
    /// claim, and its buy price (see <see cref="RequiredLandClaim(Player, Position)"/>). The presentation reads this to
    /// decide whether to raise the claim dialog before founding a colony on, or working, the tile.</summary>
    public ForcedLandClaim RequiredLandClaim(Position tile) => RequiredLandClaim(_human, tile);

    /// <summary>
    /// Whether <paramref name="player"/> founding a colony on, or working, <paramref name="tile"/> must first claim it
    /// from the natives (FreeCol: a native-owned, non-settlement tile is claimed via <c>csClaimLand</c> before use).
    /// A tile that is unclaimed, already the player's, or a native-settlement tile (not for sale) forces nothing.
    /// </summary>
    internal ForcedLandClaim RequiredLandClaim(Player player, Position tile) =>
        Map.IsNativeOwned(tile) && NativeSettlementAt(tile) is null
            ? new ForcedLandClaim(true, LandPrice(player, tile), Map.NativeOwnerOf(tile))
            : new ForcedLandClaim(false, 0, null);

    /// <summary>
    /// The deterministic claim an AI player makes for a forced native-land tile (FreeCol <c>BuildColonyMission</c>:
    /// <c>price == 0 ? 0 : checkGold(price) ? price : STEAL_LAND</c>). The AI <see cref="LandClaimChoice.Buy"/>s when it
    /// can afford the price (a free tile under Peter Minuit is always "bought" — a zero-cost, peaceful claim) and
    /// <see cref="LandClaimChoice.Steal"/>s only when it cannot. RNG-free (ADR-009): FreeCol's optional 1-in-4 gold-cheat
    /// is omitted — we never cheat gold, and a random draw would break twin-determinism — so the rule is the pure
    /// pay-if-affordable-else-steal. The human never calls this; their choice comes from the UI dialog.
    /// </summary>
    internal LandClaimChoice AiResolveLandClaim(Player player, Position tile) =>
        player.Gold >= LandPrice(player, tile) ? LandClaimChoice.Buy : LandClaimChoice.Steal;

    /// <summary>
    /// Performs <paramref name="player"/>'s forced claim of the native-owned <paramref name="tile"/> per
    /// <paramref name="choice"/>: <see cref="LandClaimChoice.Buy"/> pays the price (<see cref="ClaimLandByPaying(Player, Position)"/>),
    /// <see cref="LandClaimChoice.Steal"/> takes it and raises the owning nation's per-player alarm
    /// (<see cref="ClaimLandByStealing(Player, Position)"/>). Reuses the voluntary-purchase paths so the two stay in lock-step.
    /// </summary>
    /// <exception cref="InvalidMoveException"><paramref name="choice"/> is <see cref="LandClaimChoice.Abandon"/> (the action
    /// should simply not be called), or the underlying claim is illegal (e.g. not enough gold to buy).</exception>
    private void ResolveForcedLandClaim(Player player, Position tile, LandClaimChoice choice)
    {
        switch (choice)
        {
            case LandClaimChoice.Buy:
                ClaimLandByPaying(player, tile);
                break;
            case LandClaimChoice.Steal:
                ClaimLandByStealing(player, tile);
                break;
            default:
                throw new InvalidMoveException("The natives own this land — choose to buy or steal it, or abandon the attempt.");
        }
    }

    // ===== Treasure-train cash-in (86d3c9rzu; Col1 fee model 86d3fb5mj) ======================================
    // Escort a treasure train to a colony (or Europe) to bank its gold. We follow the ORIGINAL Colonization (1994)
    // economics here, a deliberate Col1-ward divergence from FreeCol's flat 60% transport fee (like the tax-cadence
    // fix):
    //   • Carry it home yourself on a galleon to Europe → keep the FULL amount (fee-free AND tax-free).
    //   • Let the King ship it from one of your colonies → he takes a cut equal to your CURRENT tax rate
    //     (25% tax → King keeps 25%, you net 75%). There is no separate 60% cut and no extra tax on top.
    // The King's at-colony cut is framed in the UI as his OFFER to carry the treasure across.
    // (FreeCol's model is Unit.canCashInTreasureTrain / getTransportFee + the cash-in handler — flat-fee then tax.)

    /// <summary>The father modifier id scaling the King's at-colony transport cut — Hernán Cortés's −100% ships treasure for free.</summary>
    private const string TreasureTransportFeeModifierId = "model.modifier.treasureTransportFee";

    /// <summary>
    /// The King's cut (gold) to ship <paramref name="train"/>'s treasure home from a colony, in the Col1 model: the
    /// owner's <b>current tax rate</b> applied to the carried amount (25% tax → 25% cut), folded with Hernán Cortés's
    /// <c>treasureTransportFee</c> modifier (−100% → the cut is waived, so Cortés keeps the full amount even via the
    /// King). Integer-truncated. This is the King's-transport fee only — a train carried home yourself (see
    /// <see cref="TreasureIsInEurope"/>) pays nothing, so callers gate on the location before charging it.
    /// </summary>
    private int TransportFee(Player owner, Unit train) =>
        ApplyGoodsModifiers(owner, TreasureTransportFeeModifierId, owner.TaxRate * train.TreasureAmount / 100);

    /// <summary>
    /// Whether <paramref name="train"/> is in Europe — either docked there itself <em>or</em> loaded as cargo on a ship
    /// that is docked there (FreeCol <c>Unit.isInEurope</c> follows the carrier). A train that reached Europe — under its
    /// own (test) location or carried home on a galleon — pays no King's transport cut and no tax (you carried it yourself).
    /// </summary>
    private bool TreasureIsInEurope(Unit train) =>
        train.Location == UnitLocation.InEurope
        || (train.IsAboard && UnitById(train.CarrierId!.Value) is { Location: UnitLocation.InEurope });

    /// <summary>
    /// The gold <paramref name="owner"/> nets cashing in <paramref name="train"/>, in the Col1 fee model: the FULL
    /// carried amount if the train reached Europe (<see cref="TreasureIsInEurope"/> — you carried it yourself, so no
    /// King's cut and no tax), otherwise (at a colony, the King transports it) the carried amount less the King's
    /// <see cref="TransportFee"/> (his cut = your tax rate %, waived under Cortés). Integer-truncated, like the rest of
    /// the economy.
    /// </summary>
    private int CashInValue(Player owner, Unit train) =>
        TreasureIsInEurope(train)
            ? train.TreasureAmount                         // carried home yourself → keep it all (no fee, no tax)
            : train.TreasureAmount - TransportFee(owner, train); // King transports → his cut = tax% (Cortés waives it)

    /// <summary>The gold the human would net by cashing in <paramref name="train"/> where it stands (0 if it can't here).</summary>
    public int CashInValue(Unit train) =>
        CheckCashInTreasureTrain(train).Allowed && PlayerById(train.OwnerId) is { } owner ? CashInValue(owner, train) : 0;

    /// <summary>
    /// Whether cashing in <paramref name="train"/> where it stands is fee-free — i.e. it reached Europe (docked itself or
    /// aboard a galleon docked there) so you carried it home yourself and keep the full amount (no King's cut, no tax),
    /// versus banking it at a colony where the King offers to ship it for a cut. A read-only oracle (ADR-006) the UI uses
    /// to phrase the cash-in confirmation; mirrors the Europe branch in <see cref="CashInValue(Player, Unit)"/>.
    /// </summary>
    public bool TreasureCashInIsFeeFree(Unit train) => TreasureIsInEurope(train);

    /// <summary>
    /// The King's cut (gold) the human would forgo by letting the King ship <paramref name="train"/> home from a colony —
    /// his transport offer, equal to the carried amount × the current tax rate %, waived to 0 under Hernán Cortés. 0 when
    /// the train is fee-free (already in Europe — <see cref="TreasureCashInIsFeeFree"/>) or the cash-in is not allowed
    /// here. A read-only oracle (ADR-006) the UI uses to phrase the King's-offer confirmation; the cut plus the net
    /// (<see cref="CashInValue(Unit)"/>) equal the carried amount.
    /// </summary>
    public int TreasureKingsCut(Unit train) =>
        !TreasureCashInIsFeeFree(train) && CheckCashInTreasureTrain(train).Allowed && PlayerById(train.OwnerId) is { } owner
            ? TransportFee(owner, train)
            : 0;

    /// <summary>
    /// Whether <paramref name="train"/> may be cashed in where it stands: it must be a treasure-carrying unit with
    /// gold aboard, standing at a colony its owner holds <b>that is a port connected to Europe</b> (FreeCol
    /// <c>canCashInTreasureTrain</c> requires <c>colony.isConnectedPort()</c> — a coastal colony with a sea route home;
    /// an inland colony cannot summon the King's ship, so the treasure must be moved to a coastal colony or carried
    /// home by galleon), docked in Europe, <b>or loaded as cargo on a ship docked in Europe</b> (the classic "carry the
    /// treasure home on a galleon, fee-free" play — FreeCol accepts <c>loc instanceof Unit &amp;&amp;
    /// ((Unit)loc).isInEurope()</c>). The connected-port test reuses <see cref="IsColonyCoastal"/> (our
    /// <c>isConnectedPort</c> analogue, lake-side colonies excluded). The check's cost carries the net gold.
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
        // FreeCol requires a connected port (coastal colony) at which to cash in — an inland colony has no sea route for
        // the King's ship, so it does not qualify. IsColonyCoastal is our Settlement.isConnectedPort analogue.
        bool atOwnColony = train.IsOnMap && ColonyAt(train.Position) is { } colony && colony.OwnerId == train.OwnerId && IsColonyCoastal(colony);
        bool inEurope = TreasureIsInEurope(train); // docked itself, or aboard a galleon docked in Europe (fee-free)
        if (!atOwnColony && !inEurope)
        {
            return MoveCheck.No("Bring the treasure train to one of your coastal colonies (a port connected to Europe), or carry it home aboard a ship, to cash it in.");
        }
        return PlayerById(train.OwnerId) is { } owner ? MoveCheck.Yes(CashInValue(owner, train)) : MoveCheck.No("The treasure train has no owner.");
    }

    /// <summary>
    /// Cashes in <paramref name="train"/>: banks the net gold (<see cref="CashInValue(Unit)"/>) to its owner and the
    /// train leaves the game (FreeCol disposes it on cash-in). In the Col1 fee model, a train carried home yourself to
    /// Europe banks the full amount (no King's cut, no tax); at a colony the King offers to ship it for a cut equal to
    /// the current tax rate (waived under Hernán Cortés), and you bank the remainder.
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
    internal const int GiftMinimum = 10;
    internal const int GiftMaximum = 80;

    /// <summary>The scout role id — a scout-role unit gets the full chief audience (FreeCol <c>scoutSpeakToChief</c>); other colonists get the basic visit.</summary>
    private const string ScoutRoleId = "model.role.scout";

    /// <summary>The expert-scout unit type (FreeCol <c>model.ability.expertScout</c>) a chief may train a scout into — the scout role's expert unit.</summary>
    private const string SeasonedScoutUnitTypeId = "model.unit.seasonedScout";

    /// <summary>Player id of the human — channel 0 of a settlement's per-player alarm map (the migrated single scalar).</summary>
    private const int HumanAlarmChannel = 0;

    /// <summary>
    /// Changes a native settlement's alarm toward the <b>human</b> (channel <see cref="HumanAlarmChannel"/>), clamped to
    /// [0, <c>Ruleset.Difficulty.NativeTension.MaxAlarm</c>]. The human-centric overload of
    /// <see cref="ChangeNativeAlarm(NativeSettlement, int, int)"/>, kept for the per-turn ambient/decay path, the raid AI,
    /// and tests — all of which are human-centric today (FreeCol <c>csModifyAlarm</c>).
    /// </summary>
    public void ChangeNativeAlarm(NativeSettlement settlement, int delta) =>
        ChangeNativeAlarm(settlement, HumanAlarmChannel, delta);

    /// <summary>
    /// Changes <paramref name="settlement"/>'s alarm toward the player with <paramref name="playerId"/> by
    /// <paramref name="delta"/>, clamped to [0, <c>Ruleset.Difficulty.NativeTension.MaxAlarm</c>] (the data-overridable
    /// alarm ceiling, classic 1000), then refreshes its <see cref="NativeSettlement.MostHated"/>. The single mutation
    /// point hostile (or appeasing) acts call, keyed on the <b>acting player's</b> perspective — so a settlement can be
    /// friendly to one power and hostile to another (FreeCol <c>IndianSettlement.setAlarm(Player, Tension)</c> /
    /// <c>ServerIndianSettlement.changeAlarm</c>). ADR-009: never perturbs a player's channel but the acting one's, so
    /// an AI power's provocation leaves the human's channel 0 untouched.
    /// </summary>
    /// <param name="settlement">The settlement whose alarm changes.</param>
    /// <param name="playerId">The colonial player whose channel changes (the human is <see cref="HumanAlarmChannel"/>).</param>
    /// <param name="delta">The signed amount to add (clamped into range afterwards).</param>
    public void ChangeNativeAlarm(NativeSettlement settlement, int playerId, int delta)
    {
        settlement.SetAlarm(playerId, Math.Clamp(settlement.AlarmFor(playerId) + delta, 0, Ruleset.Difficulty.NativeTension.MaxAlarm));
        UpdateMostHated(settlement);
        PropagateToTribe(settlement, playerId, delta); // a share of a per-settlement act stirs the whole tribe (86d3fpzkq; Game.Natives.cs)
    }

    /// <summary>
    /// Recomputes <paramref name="settlement"/>'s <see cref="NativeSettlement.MostHated"/> (FreeCol
    /// <c>ServerIndianSettlement.updateMostHated</c>): the live <b>colonial</b> player whose alarm band is not Happy and
    /// is the highest; <c>null</c> if every channel is Happy/absent. Ties break to the lowest player id (deterministic).
    /// Cheap and RNG-free; called after every alarm change.
    /// </summary>
    private void UpdateMostHated(NativeSettlement settlement)
    {
        NativeTensionOptions tension = Ruleset.Difficulty.NativeTension;
        int? hated = null;
        int hatedAlarm = 0;
        foreach (Player p in _players.Where(p => p.PlayerType == PlayerType.Colonial))
        {
            int alarm = settlement.AlarmFor(p.PlayerId);
            if (settlement.AlarmLevelFor(p.PlayerId, tension) == AlarmLevel.Happy)
            {
                continue; // FreeCol skips Happy channels — not "hated"
            }
            if (hated is null || alarm > hatedAlarm)
            {
                hated = p.PlayerId;
                hatedAlarm = alarm;
            }
        }
        settlement.MostHated = hated;
    }

    /// <summary>
    /// The hostility band of <paramref name="settlement"/> toward the <b>human</b> against the <b>data-overridable</b>
    /// band limits (<c>Ruleset.Difficulty.NativeTension</c>). The human-centric overload of
    /// <see cref="AlarmLevelOf(NativeSettlement, int)"/> — the raid AI and the human's interaction gates read it.
    /// Equivalent to the classic <see cref="NativeSettlement.AlarmLevel"/> property for the default ruleset.
    /// </summary>
    private AlarmLevel AlarmLevelOf(NativeSettlement settlement) =>
        AlarmLevelOf(settlement, HumanAlarmChannel);

    /// <summary>
    /// The hostility band of <paramref name="settlement"/> toward the player with <paramref name="playerId"/> against the
    /// <b>data-overridable</b> band limits (<c>Ruleset.Difficulty.NativeTension</c>) — the per-player rules-engine form,
    /// so a variant's retuned Happy/Content/Displeased/Angry thresholds drive every gameplay gate and a settlement can
    /// gate differently for different powers.
    /// </summary>
    private AlarmLevel AlarmLevelOf(NativeSettlement settlement, int playerId) =>
        settlement.AlarmLevelFor(playerId, Ruleset.Difficulty.NativeTension);

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
        // A SCOUT may revisit a chief it has already spoken with — FreeCol draws the scouting roll then returns "nothing"
        // (no gift/skill/reveal); see ScoutSpeakToChief's already-scouted branch. A plain colonist's one-time visit stays
        // hard-rejected here (the simplified VisitAsColonist path has no "nothing" outcome).
        if (settlement.HasBeenVisitedBy(unit.OwnerId) && unit.RoleId != ScoutRoleId)
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

        bool alreadyScouted = settlement.HasBeenVisitedBy(player.PlayerId); // captured before the mark — drives the scout "nothing" revisit
        settlement.MarkVisitedBy(player.PlayerId); // per-player first contact (FreeCol's per-player hasVisited)
        return unit.RoleId == ScoutRoleId
            ? ScoutSpeakToChief(player, unit, settlement, random, alreadyScouted)
            : VisitAsColonist(player, unit, settlement, random);
    }

    /// <summary>The role ability a unit must carry to found a mission (FreeCol <c>model.role.missionary</c> grants it).</summary>
    private const string EstablishMissionAbility = "model.ability.establishMission";

    /// <summary>Alarm a settlement sheds when a mission is established (FreeCol <c>ServerIndianSettlement.ALARM_NEW_MISSIONARY</c> = −100 goodwill).</summary>
    internal const int AlarmNewMissionary = 100;

    /// <summary>The native unit a mission converts (FreeCol <c>model.unit.indianConvert</c>).</summary>
    public const string IndianConvertUnitTypeId = "model.unit.indianConvert";

    /// <summary>Bartolomé de las Casas's ability: on election every native convert the player holds upgrades to a free colonist (FreeCol <c>model.ability.upgradeConvert</c>).</summary>
    private const string UpgradeConvertAbility = "model.ability.upgradeConvert";

    /// <summary>Flat convert progress a mission accrues per turn (FreeCol <c>model.modifier.conversionSkill</c> +6, on the colonist base type).</summary>
    internal const int ConversionSkillBonus = 6;

    /// <summary>The expert (jesuit) missionary's extra skill term (FreeCol jesuit <c>skill</c> 3; an ordinary colonist is 0).</summary>
    internal const int JesuitConversionSkill = 3;

    /// <summary>Father Jean de Brébeuf's ability: every one of the player's missionaries converts as an expert jesuit (FreeCol <c>model.ability.expertMissionary</c>).</summary>
    private const string ExpertMissionaryAbility = "model.ability.expertMissionary";

    /// <summary>Percent of the settlement's alarm added to convert progress each turn (FreeCol <c>model.modifier.conversionAlarmRate</c> +2%).</summary>
    internal const int ConversionAlarmRatePercent = 2;

    /// <summary>Furthest a colony may be from a converting settlement to receive the convert (FreeCol <c>ServerIndianSettlement.MAX_CONVERT_DISTANCE</c> = 10, Chebyshev).</summary>
    internal const int MaxConvertDistance = 10;

    /// <summary>Base chance (percent) that winning an assault on a settlement you hold a mission in captures a brave as a convert — the difficulty option <c>model.option.nativeConvertProbability</c> (classic-medium = 30; FreeCol <c>Unit.getConvertProbability</c> = 0.01×opt). Routed through <see cref="Specification.DifficultyOptions.NativeConvertProbability"/> (ADR-018, <c>86d3bb1x3</c>) — read from the embedded spec, default game unchanged.</summary>
    internal int NativeConvertProbabilityPercent => Ruleset.Difficulty.NativeConvertProbability;

    /// <summary>The convert-capture modifier (FreeCol <c>model.modifier.nativeConvertBonus</c>): Juan de Sepúlveda's +20% and the Spanish <c>conquest</c> nation type's +200% raise the capture-convert chance.</summary>
    private const string NativeConvertBonusId = "model.modifier.nativeConvertBonus";

    /// <summary>Chance (percent) that winning an assault on a settlement you hold a mission in instead burns the attacker's missions across that nation — the difficulty option <c>model.option.burnProbability</c> (classic-medium = 6, no modifier scales it; FreeCol <c>Unit.getBurnProbability</c> = 0.01×opt). Routed through <see cref="Specification.DifficultyOptions.BurnProbability"/> (ADR-018, <c>86d3bb1x3</c>) — read from the embedded spec, default game unchanged.</summary>
    internal int NativeBurnProbabilityPercent => Ruleset.Difficulty.BurnProbability;

    /// <summary>The role ability a missionary carries to denounce a rival's mission (FreeCol <c>model.role.missionary</c> grants <c>model.ability.denounceHeresy</c> alongside establish).</summary>
    private const string DenounceHeresyAbility = "model.ability.denounceHeresy";

    /// <summary>The expert-missionary skill the denounce roll favours/penalises (FreeCol <c>Ability.EXPERT_MISSIONARY</c>): ±0.2 to the roll for an expert resident / challenger.</summary>
    private const double DenounceExpertSwing = 0.2;

    /// <summary>The denounce success cutoff (FreeCol <c>InGameController.denounceMission</c>: <c>denounce &lt; 0.5</c> ousts the rival).</summary>
    private const double DenounceSuccessCutoff = 0.5;

    /// <summary>
    /// Whether <paramref name="unit"/> may attempt to establish a mission at <paramref name="settlement"/> (FreeCol
    /// <c>InGameController.establishMission</c>): an on-map unit in the missionary role, with movement left, on or
    /// adjacent to the settlement. The settlement's <b>alarm does not gate the command</b> — establishing at an
    /// Angry/Hateful tribe is a legal action that simply gets the missionary killed (mirrors how a hateful tribe
    /// legally kills a visiting scout); <see cref="EstablishMission(Player, Unit, NativeSettlement)"/> decides
    /// install-vs-destroy. A settlement that already holds a <b>rival</b> mission routes through the
    /// <see cref="DenounceMission(Player, Unit, NativeSettlement, IGameRandom)">denounce</see> path (a roll), not an
    /// unconditional replace; re-establishing over your <b>own</b> mission simply re-installs it.
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
    /// (FreeCol <c>InGameController.establishMission</c>). If the settlement already holds a <b>rival</b> player's
    /// mission, this routes through the <see cref="DenounceMission(Unit, NativeSettlement)">denounce</see> path (an
    /// immigration-weighted roll) and may draw randomness. With no rival mission present it is the plain establish:
    /// if the tribe is <b>Angry or Hateful</b> the missionary is <b>killed</b> (consumed, no mission); otherwise the
    /// mission is installed (the settlement records the owner + whether the missionary was a jesuit), the settlement's
    /// <b>alarm eases by 100</b> as goodwill (FreeCol <c>ALARM_NEW_MISSIONARY</c>), the surrounding tiles are revealed
    /// at the missionary's line of sight, and the missionary is consumed into the settlement. The plain-establish path
    /// draws <b>no</b> randomness (ADR-009); only the rival-denounce branch rolls.
    /// </summary>
    /// <returns><c>true</c> if a mission of the player's was installed; <c>false</c> if the missionary was killed or a denounce failed.</returns>
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckEstablishMission"/>.</exception>
    public bool EstablishMission(Unit unit, NativeSettlement settlement) => EstablishMission(_human, unit, settlement);

    /// <summary>Establishes a mission on behalf of <paramref name="player"/> (the unit's owner); routes to denounce if a rival mission is present (drawing the player's stream there).</summary>
    internal bool EstablishMission(Player player, Unit unit, NativeSettlement settlement)
    {
        MoveCheck check = CheckEstablishMission(unit, settlement);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }

        // A rival's mission must be denounced (a roll), not silently overwritten (FreeCol routes establish vs denounce
        // in MissionaryMessage). Our own mission, or none, is a plain (RNG-free) re-establish/install.
        if (settlement.HasMission && settlement.MissionOwnerId != player.PlayerId)
        {
            return DenounceMission(player, unit, settlement, RandomFor(player));
        }
        return InstallMission(player, unit, settlement);
    }

    /// <summary>
    /// The RNG-free install half of <see cref="EstablishMission(Player, Unit, NativeSettlement)"/>: kill the missionary
    /// at an Angry/Hateful tribe, else record the mission (owner + jesuit-ness), ease alarm by
    /// <see cref="AlarmNewMissionary"/>, reveal at line-of-sight, and consume the unit into the settlement. Shared by
    /// the plain-establish path and a <em>successful</em> denounce (FreeCol's <c>denounceMission</c> success tail calls
    /// straight back into <c>establishMission</c>, so a denounce over an Angry/Hateful tribe still kills the challenger).
    /// </summary>
    private bool InstallMission(Player player, Unit unit, NativeSettlement settlement)
    {
        if (AlarmLevelOf(settlement, player.PlayerId) >= AlarmLevel.Angry)
        {
            _units.Remove(unit); // an Angry/Hateful tribe kills the missionary (FreeCol csRemove)
            return false;
        }

        settlement.MissionOwnerId = player.PlayerId;
        settlement.MissionIsExpert = unit.Type.Id == Ruleset.Role(unit.RoleId).ExpertUnit; // jesuit (the role's expert unit) vs ordinary colonist
        settlement.ConvertProgress = 0; // a fresh mission (or one taken from a rival) starts its convert accrual from zero
        ChangeNativeAlarm(settlement, player.PlayerId, -AlarmNewMissionary); // a new mission eases tension toward this player (FreeCol ALARM_NEW_MISSIONARY −100, clamped at 0)
        RevealAround(player, settlement.Position, LineOfSightOf(unit)); // missionary line-of-sight reveal
        _units.Remove(unit); // the missionary is installed as the settlement's resident, not left on the map
        return true;
    }

    /// <summary>Whether <paramref name="unit"/> may denounce the rival mission at <paramref name="settlement"/> (the establish gate + a present rival mission + the <c>denounceHeresy</c> ability).</summary>
    public MoveCheck CheckDenounceMission(Unit unit, NativeSettlement settlement)
    {
        MoveCheck establish = CheckEstablishMission(unit, settlement);
        if (!establish.Allowed)
        {
            return establish; // same on-map / role / moves / adjacency gate
        }
        if (!Ruleset.Role(unit.RoleId).GrantedAbilities.GetValueOrDefault(DenounceHeresyAbility))
        {
            return MoveCheck.No($"A {unit.Type.ShortName} cannot denounce heresy.");
        }
        if (!settlement.HasMission)
        {
            return MoveCheck.No("There is no rival mission to denounce here.");
        }
        if (settlement.MissionOwnerId == unit.OwnerId)
        {
            return MoveCheck.No("You cannot denounce your own mission.");
        }
        return MoveCheck.Yes(0);
    }

    /// <summary>
    /// Denounces (and, on success, ousts) the rival mission the human's missionary <paramref name="unit"/> finds at
    /// <paramref name="settlement"/> (FreeCol <c>InGameController.denounceMission</c>). The denounce roll is
    /// <c>r × rivalImmigration ÷ (yourImmigration + 1)</c> where <c>r</c> is a uniform [0,1) draw, made <b>harder</b>
    /// by +0.2 if the resident rival is an expert (jesuit) missionary and <b>easier</b> by −0.2 if your challenger is
    /// one. A result below <see cref="DenounceSuccessCutoff">0.5</see> succeeds — the rival mission is cleared and your
    /// mission installed in its place (via the shared <see cref="InstallMission"/>, so an Angry/Hateful tribe kills
    /// your missionary even on a winning roll); otherwise your missionary is consumed for nothing. Draws the player's
    /// own RNG stream (the human's stream 0).
    /// </summary>
    /// <returns><c>true</c> if your mission was installed; <c>false</c> if the denounce failed or the tribe killed your missionary.</returns>
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckDenounceMission"/>.</exception>
    public bool DenounceMission(Unit unit, NativeSettlement settlement) => DenounceMission(_human, unit, settlement, _random);

    /// <summary>The denounce resolution drawing from an explicit RNG (the human's stream 0 by default; tests inject a fixed RNG, as for <see cref="Attack(Unit, Position, IGameRandom)"/>).</summary>
    internal bool DenounceMission(Unit unit, NativeSettlement settlement, IGameRandom random) => DenounceMission(_human, unit, settlement, random);

    /// <summary>Denounces a rival mission for <paramref name="player"/>, drawing from the supplied RNG (the per-owner stream).</summary>
    internal bool DenounceMission(Player player, Unit unit, NativeSettlement settlement, IGameRandom random)
    {
        MoveCheck check = CheckDenounceMission(unit, settlement);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }

        Player rival = PlayerById(settlement.MissionOwnerId!.Value)!;
        double denounce = random.NextDouble() * rival.Immigration / (player.Immigration + 1);
        if (settlement.MissionIsExpert)
        {
            denounce += DenounceExpertSwing; // an expert (jesuit) resident is harder to oust
        }
        if (unit.Type.Id == Ruleset.Role(unit.RoleId).ExpertUnit)
        {
            denounce -= DenounceExpertSwing; // your own expert (jesuit) challenger denounces more readily
        }

        if (denounce < DenounceSuccessCutoff)
        {
            return InstallMission(player, unit, settlement); // success: clear the rival, install ours (or be killed if Angry/Hateful)
        }
        _units.Remove(unit); // failed denounce: the challenger is consumed, the rival mission stands
        return false;
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
            // The conversion rate scales by the settlement's alarm toward the MISSION OWNER (a friendlier tribe converts
            // faster for that power). For a human-only game this is channel 0 — byte-identical to the old single scalar.
            int alarm = Math.Min(settlement.AlarmFor(owner.PlayerId), Ruleset.Difficulty.NativeTension.MaxAlarm);
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
        RevealAround(player, settlement.Position, Ruleset.Difficulty.NativeTension.TalesRevealRadius); // tales of nearby lands
        int gift = 0;
        if (AlarmLevelOf(settlement, player.PlayerId) != AlarmLevel.Hateful)
        {
            NativeTensionOptions tension = Ruleset.Difficulty.NativeTension;
            gift = random.Next(tension.GiftMinimum, tension.GiftMaximum + 1); // the visitor's own stream (the human is 0)
            player.Gold += gift;
        }
        unit.MovementLeft = 0; // speaking ends the unit's turn
        return gift;
    }

    /// <summary>
    /// A scout's audience with the chief (FreeCol <c>InGameController.scoutSpeakToChief</c>): a <b>hateful</b> tribe
    /// slays the scout; otherwise the "scouting" roll is drawn and then — if <paramref name="alreadyScouted"/> — the
    /// scout gets <b>nothing</b> (no gift/skill/reveal, the turn simply ends, FreeCol's <c>hasAnyScouted()</c> short
    /// circuit); on a first visit one roll decides — the scout may be <b>trained</b> into a seasoned scout (always if
    /// the chief teaches scouting, else a 1-in-10 chance), else <b>tales</b> (a wider reveal — taken 1-in-3 of the time
    /// or when the type gives no beads) or <b>beads</b> (gold from the type's <c>&lt;gifts&gt;</c> range, +10% for an
    /// already-expert scout). Draws from <paramref name="random"/>. (We don't deduct the gold from a native treasury —
    /// natives hold none, as with treasure plunder.)
    /// </summary>
    /// <param name="player">The acting player (the unit's owner); gold and reveals land on it, draws on its stream.</param>
    /// <param name="unit">The scout-role unit speaking with the chief.</param>
    /// <param name="settlement">The settlement whose chief is visited.</param>
    /// <param name="random">The acting player's RNG stream (the human is 0).</param>
    /// <param name="alreadyScouted">
    /// Whether this player had already spoken with this chief before this visit (captured before the visit-mark). A
    /// revisit draws exactly one "scouting" roll (matching FreeCol's draw-then-test order) and then returns nothing.
    /// <b>Divergence:</b> FreeCol's <c>hasAnyScouted()</c> is true once <em>any</em> player has scouted the settlement;
    /// ours is <em>per-player</em> (<see cref="NativeSettlement.HasBeenVisitedBy"/>), so a chief a rival scouted first is
    /// still a fresh audience for the human (we track visits per colonial power, not a single global flag).
    /// </param>
    private int ScoutSpeakToChief(Player player, Unit unit, NativeSettlement settlement, IGameRandom random, bool alreadyScouted)
    {
        // Hateful natives kill the scout outright.
        if (AlarmLevelOf(settlement, player.PlayerId) == AlarmLevel.Hateful)
        {
            _units.Remove(unit);
            return 0;
        }

        unit.MovementLeft = 0; // the audience ends the scout's turn
        SettlementType type = Ruleset.Settlement(settlement.SettlementTypeId);
        int rnd = random.Next(10); // FreeCol "scouting" roll — drawn BEFORE the already-scouted test, so a revisit consumes exactly one rnd

        // A chief this player already spoke with gives "nothing" on a revisit (FreeCol: the roll is drawn, then
        // hasAnyScouted short-circuits before any gift/skill/reveal). The turn has already ended (MovementLeft = 0).
        if (alreadyScouted)
        {
            return 0;
        }

        // Trained into a seasoned scout — always if this chief teaches scouting, otherwise a 1-in-10 chance.
        bool teachesScouting = settlement.LearnableSkill == SeasonedScoutUnitTypeId;
        if (unit.Type.Id != SeasonedScoutUnitTypeId && (teachesScouting || rnd == 0))
        {
            UpgradeUnitType(unit, SeasonedScoutUnitTypeId);
            RevealAround(player, settlement.Position, Ruleset.Difficulty.NativeTension.TalesRevealRadius);
            return 0;
        }

        // Otherwise beads (gold) or tales (a wider reveal). Tales when there are no beads or 1-in-3 of the time.
        int gold = GiftsAmount(type, random);
        if (gold <= 0 || rnd <= 3)
        {
            RevealAround(player, settlement.Position, Ruleset.Difficulty.NativeTension.TalesRevealRadius); // "tales of nearby lands"
            return 0;
        }
        if (unit.Type.Id == SeasonedScoutUnitTypeId)
        {
            gold = gold * 11 / 10; // an expert scout haggles 10% more (FreeCol)
        }
        player.Gold += gold;
        RevealAround(player, settlement.Position, Ruleset.Difficulty.NativeTension.TalesRevealRadius);
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
        // Eligibility is data-driven from the spec's model.unitChange.natives change-type (FreeCol
        // learnFromIndianSettlement's getUnitChange(NATIVES, skill) gate): a unit may learn only a skill its type has a
        // natives from→to row for. Classic: the free colonist and indentured servant learn any taught profession; a petty
        // criminal, expert or non-person has no row and is turned away.
        if (!Ruleset.CanLearnSkillFromNatives(unit.Type.Id, settlement.LearnableSkill!))
        {
            return MoveCheck.No($"A {unit.Type.ShortName} cannot learn a new skill here.");
        }
        if (AlarmLevelOf(settlement, unit.OwnerId) >= AlarmLevel.Angry)
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

    /// <summary>The most goods of one type a single trade may move, FreeCol <c>GoodsContainer.CARGO_SIZE</c> (one full hold).</summary>
    internal const int NativeTradeCargoSize = 100;

    /// <summary>Below this per-unit price the natives will not buy a good at all, FreeCol <c>IndianSettlement.TRADE_MINIMUM_PRICE</c>.</summary>
    private const int NativeTradeMinimumPrice = 3;

    /// <summary>
    /// A settlement's goods <b>storage capacity</b> per type (FreeCol <c>SettlementType.getWarehouseCapacity</c> =
    /// <c>CARGO_SIZE × claimableRadius</c>): camp 100, capital/village 200, city capital 300. Drives the stock-fill price
    /// decay — a fuller store pays less when buying and sells cheaper.
    /// </summary>
    private int NativeGoodsCapacity(NativeSettlement settlement) =>
        NativeTradeCargoSize * Ruleset.Settlement(settlement.SettlementTypeId).ClaimableRadius;

    /// <summary>
    /// The wanted-goods premium multiplier (percent) for <paramref name="goodsId"/> at <paramref name="settlement"/>:
    /// 150 / 125 / 110 for its 1st / 2nd / 3rd wanted good, else 100 (FreeCol <c>getPriceToBuy</c>'s <c>wantedBonus</c>).
    /// </summary>
    private static int NativeWantedMultiplier(NativeSettlement settlement, string goodsId) =>
        settlement.WantedSlot(goodsId) switch { 0 => 150, 1 => 125, 2 => 110, _ => 100 };

    /// <summary>
    /// The per-unit base price a settlement applies before the wanted-goods premium — what it would pay to take one unit
    /// into a store that is <paramref name="current"/> full of <paramref name="goodsId"/> (FreeCol
    /// <c>getNormalGoodsPriceToBuy</c>'s unit price): <c>(12 + trade-bonus) × (capacity − current) / capacity</c>. Falls
    /// to 0 once the store is full, so the more a settlement already holds the less it pays — and the more it wants to be
    /// paid when selling that surplus on. (FreeCol additionally halves farmed/raw-building goods and fakes raw-material
    /// production; we keep the bare stock-fill decay, which preserves the established empty-store sell price while making
    /// it stock-driven — a documented simplification, see docs/systems/natives.md.)
    /// </summary>
    private int NativeNormalUnitPrice(NativeSettlement settlement, string goodsId, int current)
    {
        int capacity = NativeGoodsCapacity(settlement);
        int full = NativeGoodsBasePrice + Ruleset.Settlement(settlement.SettlementTypeId).TradeBonus;
        return Math.Max(0, full * Math.Max(0, capacity - current) / capacity);
    }

    /// <summary>
    /// What a settlement pays for <paramref name="amount"/> of <paramref name="goodsId"/> a <b>ship</b> sells it
    /// (FreeCol <c>getPriceToSell</c> ≈ <c>amount + 11·getPriceToBuy/10</c>): a per-unit base of
    /// <c>12 + the settlement's trade bonus</c>, <b>reduced as its store of that good fills toward capacity</b>
    /// (<see cref="NativeNormalUnitPrice"/>, FreeCol's stock-fill decay), times a wanted-goods premium (150 / 125 / 110%
    /// for its 1st / 2nd / 3rd wanted good), then the classic <b>ship-trade penalty</b> (a settlement pays a ship-borne
    /// trader less — <see cref="DifficultyOptions.ShipTradePenalty"/>, −30% at medium; FreeCol
    /// <c>model.option.shipTradePenalty</c> applied with <c>sense=true</c> for the player's sale). This naval-trader
    /// overload always applies the penalty; an overland wagon train pays the un-penalised price via
    /// <see cref="NativeSalePrice(NativeSettlement, string, int, bool)"/>.
    /// </summary>
    public int NativeSalePrice(NativeSettlement settlement, string goodsId, int amount) =>
        NativeSalePrice(settlement, goodsId, amount, naval: true);

    /// <summary>
    /// What a settlement pays for <paramref name="amount"/> of <paramref name="goodsId"/> a trader sells it, with the
    /// classic <b>ship-trade penalty</b> applied only when <paramref name="naval"/> is true (FreeCol
    /// <c>model.option.shipTradePenalty</c> is a sea-trader penalty). A naval carrier is paid
    /// <see cref="DifficultyOptions.ShipTradePenalty"/>% less; an <b>overland wagon train</b> (a land carrier) is paid
    /// the full un-penalised price — the better deal an inland trader earns. The pre-penalty formula is FreeCol's
    /// <c>getPriceToSell</c> ≈ <c>amount + 11·getPriceToBuy/10</c> (stock-fill base × wanted premium). RNG-free.
    /// </summary>
    public int NativeSalePrice(NativeSettlement settlement, string goodsId, int amount, bool naval)
    {
        int basePerUnit = NativeNormalUnitPrice(settlement, goodsId, settlement.GeneralStockOf(goodsId));
        int perUnit = basePerUnit * NativeWantedMultiplier(settlement, goodsId) / 100;
        int price = amount + (11 * perUnit * amount) / 10;
        // The ship-trade penalty is a percentage modifier on the whole sale price (FreeCol applyModifiers), applied to
        // a SEA trader only — a wagon train trading overland gets the full, un-penalised price.
        return naval ? price * (100 + Ruleset.Difficulty.ShipTradePenalty) / 100 : price;
    }

    /// <summary>
    /// Whether the carrier <paramref name="ship"/> (a ship <em>or</em> an overland wagon train) may sell
    /// <paramref name="amount"/> of a good to <paramref name="settlement"/> now. The quoted price applies the
    /// ship-trade penalty only to a naval carrier — a wagon train trading overland with an inland settlement is paid
    /// the better un-penalised price (<see cref="NativeSalePrice(NativeSettlement, string, int, bool)"/>).
    /// </summary>
    public MoveCheck CheckSellToNatives(Unit ship, NativeSettlement settlement, string goodsId, int amount)
    {
        if (!ship.Type.IsCarrier || !ship.IsOnMap)
        {
            return MoveCheck.No("Only a carrier on the map can trade with a settlement.");
        }
        if (ship.Position != settlement.Position && !ship.Position.IsAdjacentTo(settlement.Position))
        {
            return MoveCheck.No("The carrier must be next to the settlement to trade.");
        }
        if (AlarmLevelOf(settlement, ship.OwnerId) >= AlarmLevel.Angry)
        {
            return MoveCheck.No("The settlement is too hostile to trade.");
        }
        if (amount <= 0)
        {
            return MoveCheck.No("Nothing to sell.");
        }
        if (ship.CargoOf(goodsId) < amount)
        {
            return MoveCheck.No($"The carrier is not carrying {amount} {goodsId}.");
        }
        return MoveCheck.Yes(NativeSalePrice(settlement, goodsId, amount, ship.Type.IsNaval));
    }

    /// <summary>
    /// Sells goods from a carrier's hold to an adjacent native settlement for gold (no European tax), at the native
    /// price. The carrier may be a ship (beside a coastal settlement) <b>or</b> an overland wagon train (beside an
    /// inland settlement); a wagon train is paid the un-penalised price, a ship the ship-trade-penalised one (see
    /// <see cref="NativeSalePrice(NativeSettlement, string, int, bool)"/>). Trading builds goodwill (lowers the
    /// settlement's alarm) and ends the carrier's turn.
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
        settlement.AddGoods(goodsId, amount); // the goods join the settlement's store (FreeCol moveGoods unit→settlement)
        player.Gold += price; // natives pay in gold; no European market tax
        ChangeNativeAlarm(settlement, player.PlayerId, -Math.Max(1, price / 50)); // goodwill toward this trader (FreeCol ALARM_BONUS_SELL ≈ 20% → price/50; min 1 per trade)
        RecomputeWantedGoods(settlement); // the fuller store re-prices its cravings (FreeCol csSell → updateWantedGoods)
        ship.MovementLeft = 0; // opening a trade session ends the carrier's turn
        return price;
    }

    // ===== Buying FROM a native settlement (Phase 5; FreeCol getSellGoods / getPriceToSell / csBuy) =====

    /// <summary>
    /// What you pay a settlement to <b>buy</b> <paramref name="amount"/> of <paramref name="goodsId"/> from its store
    /// (FreeCol <c>IndianSettlement.getPriceToSell</c>, the settlement's asking price): <c>amount + max(0,
    /// 11·getPriceToBuy/10)</c> — i.e. the settlement's own buy valuation (<see cref="NativeSalePrice(NativeSettlement, string, int)"/>'s un-penalised
    /// per-unit base × the wanted premium) marked up ~10%, with a one-unit floor per unit. Unlike selling <em>to</em>
    /// the natives there is no ship-trade penalty (FreeCol applies that only to the player's sale, <c>sense=true</c>).
    /// The price rises as their store empties (the stock-fill term), so each purchase makes the next dearer.
    /// </summary>
    public int NativeBuyPrice(NativeSettlement settlement, string goodsId, int amount)
    {
        int basePerUnit = NativeNormalUnitPrice(settlement, goodsId, settlement.GeneralStockOf(goodsId));
        int perUnit = basePerUnit * NativeWantedMultiplier(settlement, goodsId) / 100;
        int priceToBuy = perUnit * amount; // the settlement's own valuation of the lot (its getPriceToBuy)
        return amount + Math.Max(0, 11 * priceToBuy / 10); // marked up ~10%, FreeCol getPriceToSell
    }

    /// <summary>
    /// The goods a settlement is willing to <b>sell</b> a visiting trader, most-valuable first (FreeCol
    /// <c>IndianSettlement.getSellGoods</c>): the goods in its <see cref="NativeSettlement.GeneralStock"/> it holds at
    /// least <see cref="NativeTradeMinimumSize"/> of (a settlement won't part with a token amount), capped at one full
    /// hold (<see cref="NativeTradeCargoSize"/>) each, ranked by the price it would charge. Each entry is the goods id
    /// and the amount on offer.
    /// </summary>
    public IReadOnlyList<(string GoodsId, int Amount)> GoodsToSell(NativeSettlement settlement) =>
        settlement.GeneralStock
            .Select(kv => (kv.Key, Amount: Math.Min(kv.Value, NativeTradeCargoSize)))
            .Where(g => g.Amount >= NativeTradeMinimumSize)
            .OrderByDescending(g => NativeBuyPrice(settlement, g.Key, g.Amount))
            .ThenByDescending(g => g.Amount)
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => (g.Key, g.Amount))
            .ToList();

    /// <summary>A settlement won't sell fewer than this many units of a good (FreeCol <c>IndianSettlement.TRADE_MINIMUM_SIZE</c>).</summary>
    private const int NativeTradeMinimumSize = 20;

    /// <summary>
    /// Whether the carrier <paramref name="ship"/> (a ship or an overland wagon train) may buy <paramref name="amount"/>
    /// of a good from <paramref name="settlement"/> now. The buy price carries no ship-trade penalty for either carrier
    /// (FreeCol applies that only to the player's sale).
    /// </summary>
    public MoveCheck CheckBuyFromNatives(Unit ship, NativeSettlement settlement, string goodsId, int amount)
    {
        if (!ship.Type.IsCarrier || !ship.IsOnMap)
        {
            return MoveCheck.No("Only a carrier on the map can trade with a settlement.");
        }
        if (ship.Position != settlement.Position && !ship.Position.IsAdjacentTo(settlement.Position))
        {
            return MoveCheck.No("The carrier must be next to the settlement to trade.");
        }
        if (AlarmLevelOf(settlement, ship.OwnerId) >= AlarmLevel.Angry)
        {
            return MoveCheck.No("The settlement is too hostile to trade.");
        }
        if (amount <= 0)
        {
            return MoveCheck.No("Nothing to buy.");
        }
        if (settlement.GeneralStockOf(goodsId) < amount)
        {
            return MoveCheck.No($"The settlement does not have {amount} {goodsId} to sell.");
        }
        int price = NativeBuyPrice(settlement, goodsId, amount);
        if (PlayerById(ship.OwnerId)!.Gold < price)
        {
            return MoveCheck.No("You cannot afford that.");
        }
        return MoveCheck.Yes(price);
    }

    /// <summary>
    /// Buys goods from an adjacent native settlement's store into a carrier's hold for gold (no European tax), at the
    /// native asking price. The carrier may be a ship (beside a coastal settlement) or an overland wagon train (beside
    /// an inland settlement). Draining the store re-prices the settlement's wanted goods and ends the carrier's turn.
    /// </summary>
    /// <returns>The gold paid.</returns>
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckBuyFromNatives"/>.</exception>
    public int BuyFromNatives(Unit ship, NativeSettlement settlement, string goodsId, int amount) =>
        BuyFromNatives(_human, ship, settlement, goodsId, amount);

    /// <summary>Buys goods from a native settlement on behalf of <paramref name="player"/> (the ship's owner).</summary>
    internal int BuyFromNatives(Player player, Unit ship, NativeSettlement settlement, string goodsId, int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);
        MoveCheck check = CheckBuyFromNatives(ship, settlement, goodsId, amount);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }
        int price = check.Cost;
        settlement.AddGoods(goodsId, -amount); // drained from the settlement's store (FreeCol moveGoods settlement→unit)
        ship.AddCargo(goodsId, amount);
        player.Gold -= price; // no European market tax
        ChangeNativeAlarm(settlement, player.PlayerId, -Math.Max(1, price / 200)); // a little goodwill (FreeCol ALARM_BONUS_BUY ≈ 5% → price/200; min 1)
        RecomputeWantedGoods(settlement); // the emptier store re-prices its cravings (FreeCol csBuy → updateWantedGoods)
        ship.MovementLeft = 0; // opening a trade session ends the carrier's turn
        return price;
    }

    // ===== Stock-driven wanted goods (FreeCol IndianSettlement.updateWantedGoods) =====

    /// <summary>
    /// Recomputes a settlement's top-3 <see cref="NativeSettlement.WantedGoods"/> from what it currently stocks (FreeCol
    /// <c>IndianSettlement.updateWantedGoods</c>): of the storable, non-military, tradeable goods, the three it would pay
    /// the most per full hold for — most-wanted first, dropping any whose value is below the trade-minimum floor
    /// (<c>capacity-worth × <see cref="NativeTradeMinimumPrice"/></c>). The valuation is FreeCol's
    /// <c>getNormalGoodsPriceToBuy</c>: the stock-fill base (<see cref="NativeNormalUnitPrice"/>, falling as a store
    /// fills) <b>halved for farmed goods</b> (natives value raw farm produce less) — so manufactured wares they cannot
    /// make and hold none of (rum, cloth, coats, tools, trade goods) out-rank the raw goods they gather, and among the
    /// raw goods the one they are shortest of wins. Ties break by goods id so the result is deterministic and RNG-free —
    /// never the human's stream 0 (ADR-009). Called at generation and after every buy/sell, so a settlement's cravings
    /// track its stores.
    /// </summary>
    internal void RecomputeWantedGoods(NativeSettlement settlement)
    {
        int floor = NativeTradeCargoSize * NativeTradeMinimumPrice;
        settlement.WantedGoods = Ruleset.GoodsTypes
            .Where(g => g.Market is not null && g.IsStorable && !g.IsMilitary)
            // Value a full hold at the current stock level (FreeCol ranks on the bare normal price, no slot premium),
            // farmed goods halved as FreeCol getNormalGoodsPriceToBuy does, so cravings reflect scarcity + worth.
            .Select(g => (g.Id, Value: WantedRankingValue(settlement, g)))
            .Where(g => g.Value > floor)
            .OrderByDescending(g => g.Value)
            .ThenBy(g => g.Id, StringComparer.Ordinal)
            .Take(3)
            .Select(g => g.Id)
            .ToList();
    }

    /// <summary>
    /// The wanted-goods ranking value of one good for a settlement (FreeCol <c>getNormalGoodsPriceToBuy(CARGO_SIZE)</c>):
    /// the stock-fill base per unit, halved for farmed goods, times a full hold. Used only to rank cravings — the sell
    /// price keeps its established un-halved base.
    /// </summary>
    private int WantedRankingValue(NativeSettlement settlement, GoodsType good)
    {
        int perUnit = NativeNormalUnitPrice(settlement, good.Id, settlement.GeneralStockOf(good.Id));
        if (good.IsFarmed)
        {
            perUnit /= 2; // farmed goods are always less interesting (FreeCol getNormalGoodsPriceToBuy)
        }
        return perUnit * NativeTradeCargoSize;
    }

    // ===== Haggling (FreeCol NativeAIPlayer.handleTrade: offer/counter up to 3 rounds) =====

    /// <summary>The number of haggle rounds beyond which a settlement walks away, FreeCol <c>NativeAIPlayer.HAGGLE_NUMBER</c>.</summary>
    private const int NativeHaggleNumber = 3;

    /// <summary>
    /// The outcome of one haggle round: whether the natives <see cref="Accepted"/> the offered price, the
    /// <see cref="CounterPrice"/> they will accept (their current asking/bidding price — unchanged on acceptance), and
    /// whether the session is <see cref="Done"/> (they have run out of patience and will not haggle further this visit).
    /// </summary>
    /// <param name="Accepted">True if the player's offer is good enough and the trade may proceed at <see cref="CounterPrice"/>.</param>
    /// <param name="CounterPrice">The price the natives are holding out for (their counter-offer when not accepted; the agreed price when accepted).</param>
    /// <param name="Done">True if the natives have lost patience (too many rounds) and the haggle is over without a deal.</param>
    public readonly record struct NativeHaggleResult(bool Accepted, int CounterPrice, bool Done);

    /// <summary>The next price up when the natives haggle a buyer upward, FreeCol <c>NativeTrade.haggleUp</c> (×11/10).</summary>
    private static int HaggleUp(int price) => price * 11 / 10;

    /// <summary>The next price down when the natives haggle a seller downward, FreeCol <c>NativeTrade.haggleDown</c> (×9/10).</summary>
    private static int HaggleDown(int price) => price * 9 / 10;

    /// <summary>
    /// Offers to <b>sell</b> <paramref name="amount"/> of <paramref name="goodsId"/> to a settlement at the player's
    /// asking <paramref name="offerPrice"/> after <paramref name="round"/> prior haggle rounds (0 = the opening offer) —
    /// FreeCol <c>NativeAIPlayer.handleTrade</c>'s SELL branch. The settlement's fair price is
    /// <see cref="NativeSalePrice(NativeSettlement, string, int)"/> walked <b>down</b> ×9/10 per round so far (each round the natives offer a little
    /// less). If the player asks no more than that, the trade is accepted at the player's price; otherwise the natives
    /// either counter (their lower price) or — with rising probability the longer it drags on (FreeCol's
    /// <c>randomInt(HAGGLE_NUMBER + haggle) ≥ HAGGLE_NUMBER</c>) — lose patience and walk away
    /// (<see cref="NativeHaggleResult.Done"/>). The haggle roll is drawn on the <b>native</b> stream, never the human's
    /// economy stream 0 (ADR-009). This surfaces the offer/counter loop without committing the sale — call
    /// <see cref="SellToNatives(Unit, NativeSettlement, string, int)"/> once a price is agreed.
    /// <para>
    /// <paramref name="naval"/> selects the fair-price basis to match the carrier: <c>true</c> (the default) applies the
    /// classic ship-trade penalty (a sea trader is paid less); <c>false</c> — pass this for an overland <b>wagon train</b> —
    /// uses the un-penalised land price, so a wagon's <em>displayed</em> counter-offer matches the un-penalised price the
    /// committed sale already uses (the sale via <see cref="CheckSellToNatives"/> passes <c>ship.Type.IsNaval</c>). Only
    /// the displayed fair-price basis changes — no RNG draw depends on it (ADR-009).
    /// </para>
    /// </summary>
    public NativeHaggleResult TryHaggleSell(NativeSettlement settlement, string goodsId, int amount, int offerPrice, int round, bool naval = true)
    {
        int fair = NativeSalePrice(settlement, goodsId, amount, naval);
        for (int h = 0; h < round; h++)
        {
            fair = HaggleDown(fair);
        }
        if (offerPrice <= fair)
        {
            return new NativeHaggleResult(Accepted: true, CounterPrice: offerPrice, Done: false);
        }
        return ResolveHaggle(fair, round);
    }

    /// <summary>
    /// Offers to <b>buy</b> <paramref name="amount"/> of <paramref name="goodsId"/> from a settlement at the player's
    /// <paramref name="offerPrice"/> after <paramref name="round"/> prior haggle rounds (0 = the opening offer) — FreeCol
    /// <c>NativeAIPlayer.handleTrade</c>'s BUY branch. The settlement's asking price is <see cref="NativeBuyPrice"/>
    /// walked <b>up</b> ×11/10 per round so far (each round they hold out for a little more). If the player offers at
    /// least that, the trade is accepted at the player's price; otherwise the natives counter (their higher price) or
    /// lose patience and walk away. The haggle roll is drawn on the <b>native</b> stream, never the human's stream 0
    /// (ADR-009).
    /// </summary>
    public NativeHaggleResult TryHaggleBuy(NativeSettlement settlement, string goodsId, int amount, int offerPrice, int round)
    {
        int asking = NativeBuyPrice(settlement, goodsId, amount);
        for (int h = 0; h < round; h++)
        {
            asking = HaggleUp(asking);
        }
        if (offerPrice >= asking)
        {
            return new NativeHaggleResult(Accepted: true, CounterPrice: offerPrice, Done: false);
        }
        return ResolveHaggle(asking, round);
    }

    /// <summary>
    /// The shared "they didn't accept" branch of a haggle round (FreeCol <c>handleTrade</c>'s post-reject tail): roll
    /// <c>randomInt(HAGGLE_NUMBER + (round+1))</c> on the native stream; if it lands at or above
    /// <see cref="NativeHaggleNumber"/> the natives lose patience (<see cref="NativeHaggleResult.Done"/>), otherwise they
    /// counter at <paramref name="counterPrice"/> and the player may haggle once more. The longer the haggle, the likelier
    /// they walk.
    /// </summary>
    private NativeHaggleResult ResolveHaggle(int counterPrice, int round)
    {
        int haggle = round + 1;
        int roll = _nativeHaggleRandom.Next(NativeHaggleNumber + haggle);
        bool done = roll >= NativeHaggleNumber;
        return new NativeHaggleResult(Accepted: false, CounterPrice: counterPrice, Done: done);
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
    private const string CoastalOnlyAbility = "model.ability.coastalOnly"; // a building buildable only in a coastal colony (custom house under customsOnCoast)

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
    /// option (classic-medium 30), raised by the attacker's <see cref="NativeConvertBonusId"/> modifiers — <b>Juan de Sepúlveda</b>
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
    /// ability (Paul Revere → soldier) and whose colony stocks the equipment. The granted role's defence
    /// bonus applies for the fight; the equipment itself stays banked in the colony's store and is only
    /// <em>spent</em> when the auto-armed defender is beaten (FreeCol's auto-equipment "is stored in the
    /// settlement" and is removed by <c>csLoseAutoEquip</c> on a defence loss) — see
    /// <see cref="ConsumeAutoEquipment"/>, called from the attack resolution.
    /// </summary>
    internal string EffectiveCombatRole(Unit unit, bool defending) =>
        AutomaticDefenceRole(unit, defending) ?? unit.RoleId;

    /// <summary>
    /// The auto-equipment role <paramref name="unit"/> would defend in (FreeCol <c>Unit.getAutomaticRole</c>), or
    /// null when none applies: only an unarmed defender, inside a friendly colony, whose owner has the
    /// automatic-equipment ability (Paul Revere) and whose colony currently stocks the role's required goods.
    /// The single source of truth shared by <see cref="EffectiveCombatRole"/> (which folds it into the role used
    /// for the defence power) and the post-combat <see cref="ConsumeAutoEquipment"/> (which spends the goods if
    /// the defender lost). Returns null — never the unit's own role — so a caller can test "was this auto-armed?".
    /// </summary>
    private string? AutomaticDefenceRole(Unit unit, bool defending)
    {
        if (!defending || !unit.HasDefaultRole || ColonyAt(unit.Position) is not { } colony)
        {
            return null; // auto-equip only arms an unarmed defender inside a friendly colony
        }
        foreach (string roleId in AutoEquipRoleScopes(unit))
        {
            RoleType role = Ruleset.Role(roleId);
            if (role.RequiredGoods.All(g => colony.StoreOf(Ruleset.StorageIdOf(g.GoodsId)) >= g.Amount))
            {
                return roleId;
            }
        }
        return null;
    }

    /// <summary>
    /// Spends a beaten auto-armed defender's equipment from its colony (FreeCol <c>csLoseAutoEquip</c>): the
    /// muskets Paul Revere lent the last colonist are drawn from the colony's store when — and only when — that
    /// auto-armed colonist <b>loses</b> the defence. <paramref name="autoRole"/> is the role captured <em>before</em>
    /// the fight resolved (the stock may have been the deciding factor); a no-op when null (the defender was not
    /// auto-armed) or the colony is gone. RNG-free; consumes exactly the role's required goods, no refund (the
    /// equipment is lost with the failed defence, never recovered or captured into a store).
    /// </summary>
    private void ConsumeAutoEquipment(Unit defender, string? autoRole)
    {
        if (autoRole is null || ColonyAt(defender.Position) is not { } colony)
        {
            return;
        }
        foreach (RoleRequiredGoods g in Ruleset.Role(autoRole).RequiredGoods)
        {
            colony.AddGoods(Ruleset.StorageIdOf(g.GoodsId), -g.Amount);
        }
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
        Colony? colony = naval ? null : ColonyAt(target);
        bool inColony = colony is not null;
        // Popular support (FreeCol getDefensiveModifiers settlement branch): when the REF fights a rebel colony's
        // GARRISON on the colony tile, the town's Sons-of-Liberty still scales its defence — a rebel defends at SoL%,
        // the REF attacks against 100−SoL%. Only in a War-of-Independence battle, and only when the defender stands in
        // the colony, so every other fight is unchanged (ADR-009).
        double popularSupport = inColony && IsWarOfIndependenceColonyBattle(attacker, defender.OwnerId)
            ? CombatModel.PopularSupportPercent(colony!.SonsOfLiberty, IsRefUnit(attacker))
            : 0;
        var context = new DefenceContext(
            TerrainDefenceBonus: (naval || inColony) ? 0 : Map.TerrainAt(target).DefenceBonus,
            Fortified: !naval && defender.IsFortified,
            SettlementDefenceBonus: naval ? 0 : ColonyDefenceBonusAt(target),
            ArtilleryInOpen: !naval && defender.Type.Bombard && !inColony && !defender.IsFortified,
            ArtilleryAgainstRaid: !naval && inColony && defender.Type.Bombard && attacker.IsNative,
            GoodsCarried: naval ? GoodsSlotsUsed(defender) : 0,
            PopularSupportBonus: popularSupport);
        return CombatModel.DefencePower(DefenceBase(defender), context, Ruleset.CombatModifiers);
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
    /// Applies a combat tension change (FreeCol <c>defenderTension</c>) to every settlement of a native nation — its
    /// alarm <b>toward the colonial combatant</b> with <paramref name="playerId"/> (positive after a European win,
    /// negative after a repelled attack). FreeCol propagates the full delta to all the nation's settlements
    /// (<c>csModifyTension</c>), keyed on the offending player — so two powers warring with the same tribe build
    /// independent alarm.
    /// </summary>
    /// <param name="nationTypeId">The defending native nation whose settlements take the tension.</param>
    /// <param name="playerId">The colonial player the tension is directed at (the attacker's owner).</param>
    /// <param name="defenderTension">The signed tension delta (a no-op when 0).</param>
    private void ApplyNativeCombatTension(string nationTypeId, int playerId, int defenderTension)
    {
        if (defenderTension == 0)
        {
            return;
        }
        // Combat tension is applied RAW (FreeCol): the nativeAlarmModifier (Pocahontas −50%) damps only the
        // per-turn ambient proximity alarm — see ApplyAmbientNativeAlarm.
        foreach (NativeSettlement s in _nativeSettlements.Where(s => s.NationTypeId == nationTypeId))
        {
            ChangeNativeAlarm(s, playerId, defenderTension);
        }
    }

    /// <summary>
    /// The native combat tension a European victory or defeat inflicts on the defending nation
    /// (FreeCol <c>defenderTension</c>): a win adds the slain defender's slaughter tension (+ a minor
    /// insult); a loss subtracts a minor insult, and a further <c>NORMAL</c> if the attacker was slain.
    /// </summary>
    private int DefenderCombatTension(bool attackerWon, int slaughterTension, bool attackerSlain)
    {
        NativeTensionOptions t = Ruleset.Difficulty.NativeTension;
        return attackerWon
            ? slaughterTension + t.AddMinor
            : -(t.AddMinor + (attackerSlain ? t.AddNormal : 0));
    }

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
    /// 50 muskets; into the dragoon role, 50 muskets and 50 horses. A multi-count role (the pioneer,
    /// <c>maximum-count="5"</c>) is equipped at the <b>highest count the colony's stock affords</b> — a
    /// pioneer takes up to 5 tool-units (100 tools) when the colony holds them, falling back through
    /// 4/3/2/1 when it does not. Mirrors FreeCol's <c>QuickActionMenu</c> equip loop
    /// (<c>for (count = maximum-count; count &gt; 0; count--)</c> → first affordable count) feeding
    /// <c>Settlement.equipForRole(unit, role, count)</c>.
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
        int count = MaxAffordableRoleCount(unit, colony, target);
        foreach ((string goodsId, int amount) in RoleGoodsDelta(unit, target, count))
        {
            colony.AddGoods(Ruleset.StorageIdOf(goodsId), -amount); // consume positive deltas, refund negative
        }
        ChangeRole(unit, targetRoleId, count);
    }

    /// <summary>
    /// The highest equipment count (1..<see cref="RoleType.MaximumCount"/>) of <paramref name="target"/> that
    /// <paramref name="colony"/>'s store can fully arm <paramref name="unit"/> at — FreeCol steps down from the role's
    /// maximum until the warehouse can pay the required-goods difference (<c>QuickActionMenu</c> equip loop). The default
    /// (unarmed) role is always count 0; a single-count role resolves to 1 (validated affordable by
    /// <see cref="CheckEquipRole"/>). For the pioneer this banks up to 5 tool-units when the colony holds 100 tools.
    /// </summary>
    private int MaxAffordableRoleCount(Unit unit, Colony colony, RoleType target)
    {
        if (target.Id == RoleType.DefaultRoleId)
        {
            return 0;
        }
        for (int count = Math.Max(1, target.MaximumCount); count > 1; count--)
        {
            if (RoleGoodsDelta(unit, target, count)
                .All(d => d.Amount <= 0 || colony.StoreOf(Ruleset.StorageIdOf(d.GoodsId)) >= d.Amount))
            {
                return count;
            }
        }
        return 1; // CheckEquipRole already guaranteed count 1 is affordable
    }

    /// <summary>
    /// The per-good change in equipment to move from a unit's current role to <paramref name="target"/> at
    /// <paramref name="targetCount"/> equipment multiples: positive = consumed from the store, negative = refunded.
    /// The target side scales by <paramref name="targetCount"/> (FreeCol <c>Role.getRequiredGoods(count)</c>); the
    /// current side refunds the unit's present <see cref="Unit.RoleCount"/> multiples it carries.
    /// </summary>
    private IEnumerable<(string GoodsId, int Amount)> RoleGoodsDelta(Unit unit, RoleType target, int targetCount = 1)
    {
        var delta = new Dictionary<string, int>();
        foreach (RoleRequiredGoods g in target.RequiredGoods)
        {
            delta[g.GoodsId] = delta.GetValueOrDefault(g.GoodsId) + (g.Amount * Math.Max(1, targetCount));
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
    /// Returns an equipped unit's role goods to <paramref name="colony"/>'s store as it joins/founds the colony as a
    /// worker: a soldier's muskets, a dragoon's muskets+horses, a scout's horses, a pioneer's tools go into the
    /// warehouse rather than being destroyed (FreeCol <c>joinColony</c> → <c>colony.equipForRole(unit, defaultRole, 0)</c>,
    /// which unequips and banks the equipment). A no-op for a unit already in the default (unequipped) role. Mirrors the
    /// "Disarm" refund in <see cref="EquipRole"/>: the delta to the default role is the (negative) equipment refund.
    /// </summary>
    private void ReturnRoleEquipmentToColony(Unit unit, Colony colony)
    {
        if (unit.HasDefaultRole || unit.RoleCount <= 0)
        {
            return;
        }
        foreach ((string goodsId, int amount) in RoleGoodsDelta(unit, Ruleset.Role(RoleType.DefaultRoleId)))
        {
            colony.AddGoods(Ruleset.StorageIdOf(goodsId), -amount); // amount is the negative refund → adds the equipment back
        }
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
        // Ambush (FreeCol Unit.canAmbush): an open-field strike from/at concealing terrain on an unfortified defender,
        // fired when the ATTACKER has the ambush bonus (a native) OR the DEFENDER has the ambush penalty (a REF unit) —
        // this is the REF mirror (P6). Either way the attacker gains the defender's terrain bonus as offence.
        bool ambush = !naval && !inColony && !attackerInColony && !defender.IsFortified
            && (attacker.IsNative || IsRefUnit(defender))
            && (Map.TerrainAt(attacker.Position).AmbushTerrain || Map.TerrainAt(target).AmbushTerrain);
        var ctx = new AttackContext(
            Movement: MovementPenaltyFor(attacker),
            ArtilleryInOpen: !naval && attacker.Type.Bombard && !attackerInColony && !attacker.IsFortified && !inColony,
            AmbushBonus: ambush ? Map.TerrainAt(target).DefenceBonus : 0,
            GoodsCarried: naval ? GoodsSlotsUsed(attacker) : 0,
            // The REF's bombard bonus also applies battering a garrison standing on a settlement tile (FreeCol
            // getOffensiveModifiers: defender's tile hasSettlement → BOMBARD_BONUS), not just the colony-capture path.
            Bombard: inColony && IsRefUnit(attacker));
        double attack = CombatModel.AttackPower(OffenceBase(attacker) * OffenceAgainstNativeFactor(attacker, defender), ctx, Ruleset.CombatModifiers);
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
        else if (attacker.Type.Piracy && IsHumanOwned(defender))
        {
            // A privateer attacking the human's shipping deniably (no war, no tension above) nevertheless sets the
            // human's attackedByPrivateers flag — FreeCol csCombat sets it on the defender player on any piracy
            // attack. This is what unlocks the King's one-shot SUPPORT_SEA decree (Game.Monarch MonarchActionIsValid);
            // it is the only place the flag is raised, so a frigate sortie against a rival isn't blamed on privateers.
            AttackedByPrivateers = true;
        }

        // Naval combat (ship vs ship): the defender may evade; a land unit can't stand on water, so a naval
        // defender means ship-vs-ship resolution below. The two final combat powers (terrain/fortify/settlement/
        // ambush/artillery/cargo all folded in) come from the shared, side-effect-free CombatPowers — the same
        // figures the pre-combat odds preview shows (see CombatOddsAgainst).
        bool naval = defender.Type.IsNaval;
        (double attackPower, double defencePower) = CombatPowers(attacker, defender, target);
        // Whether the defender fights in Paul-Revere auto-equipment (muskets banked in the colony, not on the unit) —
        // captured before resolution because the muskets are only SPENT if this defender then loses (FreeCol's
        // csLoseAutoEquip removes the goods from the settlement on a defence loss; a winning auto-armed colonist
        // keeps the colony's muskets). Null for any normally-equipped or non-colony defender.
        string? defenderAutoRole = AutomaticDefenceRole(defender, defending: true);

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

        if (attackerWon)
        {
            ConsumeAutoEquipment(defender, defenderAutoRole); // the beaten auto-armed colonist's muskets are spent from the colony store
        }
        ResolveLoserOutcome(winner, loser, great);
        ApplyWinnerPromotion(winner, great, random);

        // Native alarm shifts across the defender's whole nation by FreeCol's defenderTension: a European
        // win raises it (the slain brave in the open + a minor insult); a repelled attack lowers it.
        // (FreeCol also short-circuits this for a piracy attacker, but a privateer can't reach a native today —
        // the naval-vs-land gate blocks it and natives have no ships — so no `!attacker.Type.Piracy` guard yet.)
        if (defenderNation is not null)
        {
            int slaughter = _units.Any(u => u.Id == defenderId) ? 0 : Ruleset.Difficulty.NativeTension.AddUnitDestroyed;
            bool attackerSlain = !_units.Any(u => u.Id == attackerId);
            ApplyNativeCombatTension(defenderNation, attacker.OwnerId, DefenderCombatTension(attackerWon, slaughter, attackerSlain));
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
        double attackPower = CombatModel.AttackPower(OffenceBase(attacker) * OffenceAgainstNativeFactor(attacker, defender), attackContext, Ruleset.CombatModifiers);
        double defencePower = CombatModel.DefencePower(DefenceBase(defender), defenceContext, Ruleset.CombatModifiers);

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

            // The atrocity's score penalties (FreeCol csDestroySettlement): −5 for razing the camp and a further −50 if
            // it was the nation's LAST settlement (DESTROY_NATION). Recorded as history events on the human attacker —
            // the history log is the human's, so only the human's razings carry a penalty (an AI razing scores nothing
            // for the human). Score-bearing, so they fold into the human's PlayerScore via HistoryEventScore.
            if (attacker.OwnerId == _human.PlayerId)
            {
                string nationName = SettlementNationDisplayName(nation);
                RecordHistory(HistoryEventKind.SettlementDestroyed, $"Razed a {nationName} settlement.", ScoreSettlementDestroyed);
                if (!_nativeSettlements.Any(s => s.NationTypeId == nation)) // that was the nation's last settlement
                {
                    RecordHistory(HistoryEventKind.NationDestroyed, $"Destroyed the {nationName} nation.", ScoreNationDestroyed);
                }
            }

            NativeTensionOptions t = Ruleset.Difficulty.NativeTension;
            if (capital)
            {
                // Burning a native capital makes the nation surrender to the attacker — its surviving settlements drop
                // their alarm TOWARD THE ATTACKER to peace (FreeCol Tension.SURRENDERED). Other powers' channels are
                // untouched — a tribe the human conquers stays hostile to whoever else it was angry at.
                foreach (NativeSettlement s in _nativeSettlements.Where(s => s.NationTypeId == nation))
                {
                    s.SetAlarm(attacker.OwnerId, t.Surrendered);
                    UpdateMostHated(s);
                }
            }
            else
            {
                // In-settlement defender slaughtered (+500) + the settlement destroyed (+300 MAJOR) + a
                // minor insult (+100) = +900, propagated to the nation's surviving settlements (toward the attacker).
                ApplyNativeCombatTension(nation, attacker.OwnerId, t.AddSettlementAttacked + t.AddMajor + t.AddMinor);
            }
        }
        else
        {
            // The attacker loses to the garrison: disarm/demote/destroy it via the shared precedence.
            // A repelled assault lowers the nation's alarm toward the attacker (the natives prevailed) — across all its settlements.
            // (The attacker is a land unit here, so the naval damage/sink branch never applies.)
            ResolveLoserOutcome(defender, attacker, great);
            bool attackerSlain = !_units.Any(u => u.Id == attackerId);
            ApplyNativeCombatTension(nation, attacker.OwnerId, DefenderCombatTension(attackerWon: false, slaughterTension: 0, attackerSlain));
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

        // REF siege fidelity (FreeCol getOffensiveModifiers settlement branch): the Royal Expeditionary Force batters a
        // settlement with its bombard bonus (+50%), and in a War of Independence the colony's popular support scales its
        // defence — a rebel-held town defends at its Sons-of-Liberty %, the REF assaults against 100−SoL%. Both are 0 in
        // an ordinary (non-REF, non-WoI) capture, so that path is byte-identical (ADR-009).
        bool attackerIsRef = IsRefUnit(attacker);
        double popularSupport = IsWarOfIndependenceColonyBattle(attacker, formerOwner)
            ? CombatModel.PopularSupportPercent(colony.SonsOfLiberty, attackerIsRef)
            : 0;
        var attackContext = new AttackContext(Movement: MovementPenaltyFor(attacker), Bombard: attackerIsRef);
        double attackPower = CombatModel.AttackPower(OffenceBase(attacker), attackContext, Ruleset.CombatModifiers);
        double defencePower = CombatModel.DefencePower(
            DefenceBase(defender),
            new DefenceContext(SettlementDefenceBonus: ColonyDefenceBonus(colony), PopularSupportBonus: popularSupport),
            Ruleset.CombatModifiers);
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
    /// Whether <paramref name="attacker"/> — a land unit <b>aboard a ship</b> — may launch an <b>amphibious assault</b>
    /// on the strongest enemy standing on the adjacent land tile <paramref name="target"/>, attacking straight off the
    /// ship without disembarking first. Gated by FreeCol's <c>Unit.allowMoveFrom</c>: the move from water is only legal
    /// when the <see cref="GameOptions.AmphibiousMoves"/> game option is on and the attacker is <b>not</b> the Royal
    /// Expeditionary Force (the REF must land before it fights). The defender must be a land unit on a land tile next to
    /// the carrier (a ship can't reach a sea defender as an amphibious target — that's ordinary naval combat). The
    /// −75% amphibious-attack penalty is applied in <see cref="AttackAmphibious(Unit, Position)"/>.
    /// </summary>
    public MoveCheck CheckAttackAmphibious(Unit attacker, Position target)
    {
        if (!attacker.IsAboard)
        {
            return MoveCheck.No("Only a unit aboard a ship can make an amphibious assault.");
        }
        if (attacker.Type.IsNaval)
        {
            return MoveCheck.No("A ship cannot be carried, and so cannot assault from a carrier.");
        }
        if (!Ruleset.GameOptions.AmphibiousMoves || IsRefUnit(attacker))
        {
            // FreeCol allowMoveFrom: a move off water needs AMPHIBIOUS_MOVES and a non-REF owner — otherwise land first.
            return MoveCheck.No("Put the unit ashore before it can attack.");
        }
        if (UnitById(attacker.CarrierId!.Value) is not { } carrier || !carrier.IsOnMap)
        {
            return MoveCheck.No("The ship must be on the map to assault from it.");
        }
        if (!Map.InBounds(target))
        {
            return MoveCheck.No("Target is off the map.");
        }
        if (!carrier.Position.IsAdjacentTo(target))
        {
            return MoveCheck.No("Assault a tile next to the ship.");
        }
        if (Map.TerrainAt(target).IsWater)
        {
            return MoveCheck.No("An amphibious assault strikes a land tile.");
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
        if (defender.Type.IsNaval)
        {
            // A naval defender can't stand on the land tile we just required; this guard is belt-and-braces (a ship
            // sharing a coastal land tile is impossible) — an amphibious target is always a land unit/settlement.
            return MoveCheck.No("An amphibious assault strikes a land defender.");
        }
        return MoveCheck.Yes(0);
    }

    /// <summary>
    /// Resolves an amphibious assault: a land unit aboard a ship attacks the strongest enemy on the adjacent land tile
    /// <paramref name="target"/> through the pure <see cref="CombatModel"/> with the <b>−75% amphibious-attack
    /// penalty</b> set (FreeCol <c>model.modifier.amphibiousAttack</c>, applied because the attacker fights from a water
    /// tile onto land — <c>combatIsAmphibious</c>). The attacker stays aboard the ship the whole time (win or lose; it
    /// does not disembark). A unit beaten in an amphibious assault is <b>slain, not captured</b> (FreeCol gates the
    /// capture branch on <c>!combatIsAmphibious</c>) — <see cref="ResolveLoserOutcome"/> is called with
    /// <c>amphibious: true</c>. Like the on-map <see cref="Attack(Unit, Position)"/> it declares war / spikes tension on
    /// a rival colonial defender, raises native alarm, and ends the attacker's turn. Uses the game's main saved RNG
    /// (resume-deterministic).
    /// </summary>
    /// <returns>The graded combat result.</returns>
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckAttackAmphibious"/>.</exception>
    public CombatResult AttackAmphibious(Unit attacker, Position target) => AttackAmphibious(attacker, target, _random);

    /// <summary>The amphibious-assault resolution drawing from an explicit RNG (tests inject a fixed RNG to force a band).</summary>
    internal CombatResult AttackAmphibious(Unit attacker, Position target, IGameRandom random)
    {
        MoveCheck check = CheckAttackAmphibious(attacker, target);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }

        Unit defender = DefenderAt(attacker, target)!;
        int attackerId = attacker.Id; // ids survive a promotion/demotion swap; the object reference may not
        int defenderId = defender.Id;
        string? defenderNation = defender.OwnerNationId;

        // Assaulting a rival colonial power's unit declares war and spikes tension, both ways (as on-map Attack);
        // a no-op for native defenders (the alarm system below) and for piracy (a privateer can't be carried, so
        // the attacker is never a pirate here — but a defender privateer keeps the deniable-raid exception).
        if (defenderNation is null && !attacker.Type.Piracy && !defender.Type.Piracy)
        {
            SetStance(attacker.OwnerId, defender.OwnerId, Stance.War);
            ChangeTension(attacker.OwnerId, defender.OwnerId, TensionWar);
        }

        // Offence with the −75% amphibious penalty (Amphibious: true). The defender's terrain/fortify/settlement/etc.
        // fold through DefencePowerOf, exactly as on-map combat — only the attacker's context differs.
        double attack = CombatModel.AttackPower(
            OffenceBase(attacker) * OffenceAgainstNativeFactor(attacker, defender),
            new AttackContext(Amphibious: true, Movement: MovementPenaltyFor(attacker)),
            Ruleset.CombatModifiers);
        double defence = DefencePowerOf(attacker, defender, target);

        attacker.MovementLeft = 0; // the assault ends the attacker's turn (before any promotion/demotion swap)

        CombatResult result = CombatModel.Resolve(CombatModel.WinProbability(attack, defence), random);
        bool attackerWon = result is CombatResult.GreatWin or CombatResult.Win;
        bool great = result is CombatResult.GreatWin or CombatResult.GreatLoss;
        Unit winner = attackerWon ? attacker : defender;
        Unit loser = attackerWon ? defender : attacker;

        // The slain-not-captured rule: pass amphibious: true so a capturable defender that loses is slain, not taken.
        ResolveLoserOutcome(winner, loser, great, amphibious: true);
        ApplyWinnerPromotion(winner, great, random);

        if (defenderNation is not null)
        {
            int slaughter = _units.Any(u => u.Id == defenderId) ? 0 : Ruleset.Difficulty.NativeTension.AddUnitDestroyed;
            bool attackerSlain = !_units.Any(u => u.Id == attackerId);
            ApplyNativeCombatTension(defenderNation, attacker.OwnerId, DefenderCombatTension(attackerWon, slaughter, attackerSlain));
        }

        return result;
    }

    /// <summary>The role ability a unit must carry to spy on a rival colony's interior (FreeCol <c>model.ability.spyOnColony</c>, granted by <c>model.role.scout</c>).</summary>
    private const string SpyOnColonyAbility = "model.ability.spyOnColony";

    /// <summary>
    /// Whether <paramref name="unit"/> may spy on the rival colony on <paramref name="target"/> (FreeCol
    /// <c>SpySettlementMessage.serverHandler</c> + <c>Unit.MoveType.ENTER_FOREIGN_COLONY_WITH_SCOUT</c>): an on-map unit
    /// carrying the <c>spyOnColony</c> ability (the scout role), with movement left, adjacent to a colony owned by
    /// <b>another</b> player. Spying does not require war or any stance — a scout simply walks up to the gate and looks
    /// (FreeCol gates only on the ability + the foreign-colony move type, not on diplomacy). A garrison on the colony
    /// tile does not block the look (unlike an assault), matching FreeCol — the scout enters as a visitor, not a soldier.
    /// </summary>
    public MoveCheck CheckSpyOnColony(Unit unit, Position target)
    {
        if (!unit.IsOnMap)
        {
            return MoveCheck.No("The unit is at sea or in Europe.");
        }
        if (!Ruleset.Role(unit.RoleId).GrantedAbilities.GetValueOrDefault(SpyOnColonyAbility))
        {
            return MoveCheck.No($"A {unit.Type.ShortName} cannot spy on a colony — only a scout can.");
        }
        if (unit.MovementLeft <= 0)
        {
            return MoveCheck.No("No movement left this turn.");
        }
        if (!unit.Position.IsAdjacentTo(target))
        {
            return MoveCheck.No("Move next to the colony to spy on it.");
        }
        if (ColonyAt(target) is not { } colony || colony.OwnerId == unit.OwnerId)
        {
            return MoveCheck.No("There is no rival colony to spy on there.");
        }
        return MoveCheck.Yes(0);
    }

    /// <summary>
    /// Spies on the rival colony on <paramref name="target"/> with the scout <paramref name="unit"/> (FreeCol
    /// <c>InGameController.spySettlement</c>): the scout walks up to the gate and <b>always succeeds</b> — the player
    /// gets a one-shot glimpse of the colony's full interior (buildings, the worked-tile/building layout, the warehouse
    /// stockpile and the Sons-of-Liberty standing), returned as a <see cref="ColonyInteriorSnapshot"/> oracle the
    /// presentation renders. The scout's turn ends (FreeCol <c>setMovesLeft(0)</c>) and the colony's tile is revealed.
    /// The interior is a <b>snapshot</b>: the player gains no ongoing visibility (FreeCol reveals it for the one look
    /// only). Spying draws <b>no RNG</b> at all (FreeCol's spy never fails), so the human's economy stream 0 is never
    /// shifted — a default game stays byte-identical (ADR-006/009).
    /// </summary>
    /// <returns>The spy outcome: the colony interior glimpsed.</returns>
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckSpyOnColony"/>.</exception>
    public SpyResult SpyOnColony(Unit unit, Position target) => SpyOnColony(_human, unit, target);

    /// <summary>Spies on behalf of <paramref name="player"/> (the unit's owner). Always reveals the interior — no RNG drawn (FreeCol-exact).</summary>
    internal SpyResult SpyOnColony(Player player, Unit unit, Position target)
    {
        MoveCheck check = CheckSpyOnColony(unit, target);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }

        Colony colony = ColonyAt(target)!;
        unit.MovementLeft = 0;                                   // the spy ends the scout's turn (FreeCol setMovesLeft(0))
        RevealAround(player, colony.Position, ColonySightRadius); // the scout learns the colony's tile/surroundings

        // FreeCol's spySettlement always succeeds — no failure roll, so the spy draws no randomness at all.
        return SpyResult.Revealed(ColonyInteriorSnapshot.Of(colony));
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

    /// <summary>
    /// A colony's burnable buildings, in construction order — the buildings a native raid can torch (FreeCol
    /// <c>Colony.getBurnableBuildings</c> = <c>Building.canBeDamaged</c>). A building is burnable unless it is an
    /// <em>automatic</em> build (FreeCol <c>BuildingType.isAutomaticBuild</c>: needs no goods to build AND has no
    /// predecessor) — so the free base houses (town hall, carpenter's house, the base manufactory houses, the
    /// chapel) are spared, while anything that had to be built or upgraded (incl. the stockade-line defences) can
    /// burn. Stable order so a seeded pick is deterministic (ADR-009).
    /// </summary>
    private IReadOnlyList<string> BurnableBuildings(Colony colony) =>
        colony.Buildings.Where(IsBurnable).ToList();

    /// <summary>Whether a building type can be damaged by a raid (FreeCol <c>Building.canBeDamaged</c> / <c>!BuildingType.isAutomaticBuild</c>): it either cost goods to build or upgrades from an earlier building.</summary>
    private bool IsBurnable(string buildingId)
    {
        BuildingType type = Ruleset.Building(buildingId);
        return type.BuildCost.Count > 0 || type.UpgradesFrom is not null;
    }

    /// <summary>
    /// The naval units moored in a colony's port — the ships a native raid can sink (FreeCol
    /// <c>colony.getTile().getNavalUnits()</c>). A ship can't occupy the colony's own land tile in our model, so
    /// "in port" is a ship on a water tile adjacent to the colony (the same adaptation as <see cref="ResolveCaughtShips"/>),
    /// in stable unit-id order for a deterministic seeded pick.
    /// </summary>
    private IReadOnlyList<Unit> DockedShips(Colony colony)
    {
        HashSet<Position> port = colony.Position.Neighbours()
            .Where(n => Map.InBounds(n) && Map.TerrainAt(n).IsWater)
            .ToHashSet();
        return _units
            .Where(u => u.OwnerId == colony.OwnerId && u.OwnerNationId is null && u.Type.IsNaval && u.IsOnMap && port.Contains(u.Position))
            .OrderBy(u => u.Id)
            .ToList();
    }

    /// <summary>
    /// Burns a building in a raid (FreeCol <c>ServerPlayer.csDamageBuilding</c>): a building that upgrades from an
    /// earlier one is <b>downgraded</b> to its predecessor (staffing preserved — <see cref="Colony.ReplaceBuilding"/>);
    /// a base building (no predecessor, but costing goods) is <b>razed</b> outright (<see cref="Colony.RemoveBuilding"/>,
    /// ejecting its workers to idle). RNG-free; the caller has already picked which building.
    /// </summary>
    private void BurnBuilding(Colony colony, string buildingId)
    {
        if (Ruleset.Building(buildingId).UpgradesFrom is { } predecessor)
        {
            colony.ReplaceBuilding(buildingId, predecessor); // downgrade one tier, keeping the workers
        }
        else
        {
            colony.RemoveBuilding(buildingId); // a base building with no predecessor is destroyed
        }
    }

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
        // canBePillaged: the colony must have SOMETHING a raid can take — a burnable building, a ship in port,
        // a lootable goods stack, or gold to plunder (FreeCol Colony.canBePillaged). A bare new colony with only
        // its free base buildings, no goods, no ships and a broke owner is nothing to raid.
        bool somethingToTake = BurnableBuildings(colony).Count > 0
            || DockedShips(colony).Count > 0
            || PillageableGoods(colony).Any()
            || HumanPlayer.Gold > 0;
        if (!somethingToTake)
        {
            return MoveCheck.No("The colony has nothing worth pillaging.");
        }
        return MoveCheck.Yes(0);
    }

    /// <summary>
    /// A native brave pillages the undefended human colony on <paramref name="target"/> (FreeCol
    /// <c>csPillageColony</c> / the <c>PILLAGE_COLONY</c> combat effect — a native win over a colony's unarmed
    /// last-resort defender). The defender is a transient unarmed colonist (defence 1, the abstracted population).
    /// On a brave **win** the raid inflicts <em>one</em> randomly-chosen destructive outcome, picked uniformly over
    /// FreeCol's full option set in FreeCol's order — a building to <b>burn</b>, a ship in port to <b>sink</b>, a
    /// goods stack to <b>loot</b> (<c>min(amount/2, 50)</c>), then a <b>gold</b> option when the owner can be
    /// plundered — recording a <see cref="ColonyRaidNotice"/>; the colony keeps its people and ownership (natives
    /// never capture a colony). On a **loss** the brave is slain (dispose-on-combat-loss). The whole path draws
    /// from <paramref name="random"/> (the nation's own stream when driven by the AI) — the combat band, then the
    /// single pillage-option pick (plus the gold range if that option is chosen) — never the human's stream 0 (ADR-009).
    /// </summary>
    /// <remarks>
    /// Faithful subset: a looted goods stack is destroyed (the brave does not carry it off — no native
    /// goods-hauling/settlement-restock model; FreeCol's brave does <c>attacker.add(goods)</c>). A burned building
    /// downgrades to its predecessor, or is razed if it is a base building (FreeCol <c>csDamageBuilding</c>); a sunk
    /// ship sinks if it has nowhere to repair, else is damaged and limps off (FreeCol <c>csSinkShipAttack</c> /
    /// <c>csDamageShipAttack</c>). We still pillage on **any** native win, where FreeCol gates pillage on a non-great
    /// win and lets a **great** win kill a colonist or destroy the colony — so our great win is *gentler* than
    /// FreeCol's (a tribe never destroys a colony here); the colonist-kill/destroy path and the attacker's tension
    /// easing are deferred (no population-on-combat decrement; no nation-level native-tension store).
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
        double attackPower = CombatModel.AttackPower(OffenceBase(brave), attackContext, Ruleset.CombatModifiers);
        double defencePower = CombatModel.DefencePower(DefenceBase(defender), new DefenceContext(SettlementDefenceBonus: ColonyDefenceBonus(colony)), Ruleset.CombatModifiers);
        brave.MovementLeft = 0; // raiding ends the brave's turn

        CombatResult result = CombatModel.Resolve(CombatModel.WinProbability(attackPower, defencePower), random);
        if (result is CombatResult.GreatWin or CombatResult.Win)
        {
            ApplyPillageOutcome(brave, colony, target, random);
        }
        else
        {
            ResolveLoserOutcome(defender, brave, result is CombatResult.GreatLoss); // the brave is dispose-on-combat-loss → slain
        }
    }

    /// <summary>
    /// Picks and applies the single destructive outcome of a won native pillage (FreeCol <c>csPillageColony</c>'s
    /// "Pillage choice"): one option, uniformly, over the burnable buildings, then the ships in port, then the
    /// lootable goods stacks, then a single extra gold option when the owner can be plundered — exactly FreeCol's
    /// index order, so the chosen pillage matches what FreeCol would do under the same draw. One draw from
    /// <paramref name="random"/> selects the option; the gold branch draws the plunder range from the same stream.
    /// </summary>
    private void ApplyPillageOutcome(Unit brave, Colony colony, Position target, IGameRandom random)
    {
        IReadOnlyList<string> buildings = BurnableBuildings(colony);
        IReadOnlyList<Unit> ships = DockedShips(colony);
        var loot = PillageableGoods(colony).ToList();
        bool canPlunderGold = HumanPlayer.Gold > 0; // the colony is human-owned (CheckPillageColony gated)

        int pick = random.Next(buildings.Count + ships.Count + loot.Count + (canPlunderGold ? 1 : 0));
        string nation = brave.OwnerNationId!;
        if (pick < buildings.Count)
        {
            string buildingId = buildings[pick];
            BurnBuilding(colony, buildingId);
            _colonyRaidNotices.Add(new ColonyRaidNotice(nation, colony.Name, null, 0, target, PillageKind.Building, buildingId));
            return;
        }
        pick -= buildings.Count;
        if (pick < ships.Count)
        {
            SinkOrDamagePillagedShip(ships[pick]);
            _colonyRaidNotices.Add(new ColonyRaidNotice(nation, colony.Name, null, 0, target, PillageKind.Ship));
            return;
        }
        pick -= ships.Count;
        if (pick < loot.Count)
        {
            (string goodsId, int amount) = loot[pick];
            int take = Math.Min(amount / 2, PillageGoodsCap);
            if (take > 0) // a 1-unit stack yields 0 (amount/2 == 0): the raid won but carried nothing off — no notice
            {
                colony.AddGoods(goodsId, -take);
                _colonyRaidNotices.Add(new ColonyRaidNotice(nation, colony.Name, goodsId, take, target));
            }
            return;
        }

        // Steal gold: FreeCol max(1, colony.getPlunder/5), capped at the owner's purse (no negative balance).
        // ColonyPlunderAmount draws from the nation's stream (the same `random`) — never the human's stream 0.
        int plunder = Math.Min(Math.Max(1, ColonyPlunderAmount(colony, HumanPlayer, random) / 5), HumanPlayer.Gold);
        HumanPlayer.Gold -= plunder;
        _colonyRaidNotices.Add(new ColonyRaidNotice(nation, colony.Name, null, plunder, target, PillageKind.Gold));
    }

    /// <summary>
    /// Sinks a ship caught in a pillaged port, or — if it has somewhere to repair — damages it instead (FreeCol
    /// <c>csSinkShipAttack</c> vs <c>csDamageShipAttack</c>, chosen on <c>getRepairLocation()</c>). Reuses the shared
    /// naval damage/sink path (a damaged ship limps to its drydock berth or Europe; a ship with nowhere to repair
    /// goes down). RNG-free.
    /// </summary>
    private void SinkOrDamagePillagedShip(Unit ship)
    {
        if (RepairBerthFor(ship) is not null || CanRepairAtEurope(ship))
        {
            DamageShip(ship); // limps to its repair location
        }
        else
        {
            SinkShip(ship);   // nowhere to repair → goes down
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
    /// <param name="winner">The unit that won the round (captures equipment / takes the captive).</param>
    /// <param name="loser">The unit that lost the round (disarmed / captured / demoted / slain).</param>
    /// <param name="greatLoss">True on a decisive loss (a great loss): a defeated ship sinks rather than limps.</param>
    /// <param name="amphibious">
    /// True when the combat was an amphibious assault (the attacker struck straight off a ship). FreeCol gates the
    /// capture-unit branch on <c>!combatIsAmphibious</c> (<c>SimpleCombatModel.resolveAttack</c>): a unit beaten in an
    /// amphibious assault is <b>slain, not captured</b> — there is no neutral land to march a captive back across. When
    /// set, the capturable-unit branch (#3) is skipped, so the loser falls through to demotion (#4) or slaughter (#5).
    /// Defaults to <c>false</c> for the on-the-map attack paths, which are never amphibious.
    /// </param>
    private void ResolveLoserOutcome(Unit winner, Unit loser, bool greatLoss, bool amphibious = false)
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

        // 3. A capturable unit changes side (and may downgrade its type on capture) — UNLESS the combat was an
        // amphibious assault. FreeCol gates this branch on !combatIsAmphibious (SimpleCombatModel.resolveAttack): a unit
        // beaten in an assault fired straight off a ship is SLAIN, not captured — there's no friendly shore to march a
        // captive across, so it falls through to demotion (#4) or slaughter (#5). The on-the-map attack paths pass
        // amphibious=false, so they capture as before (86d3e4bmp).
        if (!amphibious && loser.Type.CanBeCaptured && CanCaptureUnits(winner))
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
        double defence = CombatModel.DefencePower(DefenceBase(ship), new DefenceContext(GoodsCarried: GoodsSlotsUsed(ship)), Ruleset.CombatModifiers);
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
            Nationality = unit.Nationality, // a type swap is the same individual: it keeps its origin + custom name
            Ethnicity = unit.Ethnicity,
            Name = unit.Name,
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

    /// <summary>
    /// How far a colony sees: the Chebyshev radius it reveals when founded and keeps in sight. Read data-driven from the
    /// ruleset (the colony settlement's <c>visible-radius</c> — FreeCol <c>Settlement.getLineOfSight</c>); classic
    /// <b>2</b>, a 5×5 ring. See <see cref="Specification.ColonyConstants.ColonySightRadius"/>.
    /// </summary>
    public int ColonySightRadius => Ruleset.ColonyConstants.ColonySightRadius;

    /// <summary>
    /// The Chebyshev radius Coronado's see-all-colonies reveal uses around every colony: the colony's own sight radius
    /// (<see cref="ColonySightRadius"/>) widened by Coronado's <c>model.modifier.exposedTilesRadius</c> father modifier
    /// (FreeCol <c>father.apply(colony.getLineOfSight(), …, EXPOSED_TILES_RADIUS)</c>). Classic Coronado adds +3, so
    /// <c>2 + 3 = 5</c> — an 11×11 block. A father without the modifier (or a ruleset that drops it) reveals at the bare
    /// colony sight radius. <paramref name="father"/> is the elected Coronado.
    /// </summary>
    private int CoronadoRevealRadius(FoundingFather father)
    {
        double radius = ColonySightRadius;
        foreach (FatherModifier modifier in father.Modifiers
                     .Where(m => m.TargetId == ExposedTilesRadiusModifierId)
                     .OrderBy(m => m.Index))
        {
            radius = modifier.ApplyTo(radius);
        }
        return (int)radius;
    }

    /// <summary>
    /// Tiles the player can see <em>right now</em> — within the line of sight of an
    /// on-map unit or a colony. Always a subset of <see cref="Explored"/>; recomputed
    /// from current positions (not stored, never stale). Explored-but-not-visible tiles
    /// are "remembered" (drawn dimmed); foreign units there are hidden.
    /// <para>
    /// When the <c>model.option.fogOfWar</c> game option is <b>off</b>
    /// (<see cref="Specification.GameOptions.FogOfWar"/> = <c>false</c>) there is no remembered-but-hidden state: every
    /// tile the player has ever explored counts as visible (FreeCol <c>Player.getVisibleTileSet</c>'s no-fog branch =
    /// all explored tiles). With fog <b>on</b> (the classic default) visibility is just the union of current lines of
    /// sight, so a tile the player has walked away from is re-hidden — exactly as before, so the default game is
    /// byte-identical.
    /// </para>
    /// </summary>
    public IReadOnlySet<Position> CurrentlyVisible
    {
        get
        {
            // Fog off: every explored tile is visible (FreeCol's no-fog branch). A defensive copy so callers can't
            // mutate the explored set through this read-only view.
            if (!Ruleset.GameOptions.FogOfWar)
            {
                return new HashSet<Position>(_human.Explored);
            }

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

    /// <summary>
    /// Whether a tile is currently in sight (not merely explored). With fog of war <b>off</b> every explored tile is in
    /// sight (FreeCol's no-fog branch); with fog <b>on</b> (the classic default) only tiles within a current line of
    /// sight are — so the default game is byte-identical.
    /// </summary>
    public bool IsVisible(Position p) =>
        !Ruleset.GameOptions.FogOfWar
            ? _human.Explored.Contains(p)
            : _units.Any(u => u.IsOnMap && IsHumanOwned(u) && InSight(u.Position, p, LineOfSightOf(u)))
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
    /// <param name="landStyle">
    /// The shape the generated land takes (FreeCol's <c>model.option.landGeneratorType</c>): one
    /// <see cref="LandStyle.Continent"/> (default — the historical, byte-identical map), a few large
    /// <see cref="LandStyle.Archipelago"/> islands, or many small <see cref="LandStyle.Islands"/>. Applies only to the
    /// random map path (ignored on a fixed <paramref name="mapSource"/>, whose land shape is loaded). The default keeps
    /// an unpicked new game byte-identical (ADR-009).
    /// </param>
    /// <param name="importOverride">
    /// A test-only seam (default null): a pre-built <see cref="MapImportResult"/> (terrain + declared native
    /// settlements, fixed start tiles, region layer) used in place of importing <paramref name="mapSource"/> from disk.
    /// Lets a test drive a scenario map that declares <c>[settlements]</c>/<c>[starts]</c>/<c>[regions]</c> sections
    /// through the real install path without shipping it as a <see cref="MapSource"/>. Null in normal play, so the
    /// production America/Random paths are unchanged.
    /// </param>
    /// <param name="greatRivers">
    /// Whether to generate navigable <b>great-river</b> terrain — the spine of long rivers retyped to
    /// <c>model.tile.greatRiver</c> water (FreeCol's <c>mapGeneratorOptions.enableGreatRivers</c>, which ships
    /// <b>off</b>). <b>Default false</b>, so an unpicked new game is byte-identical to before (ADR-009); when on, the
    /// retyping is a pure RNG-free post-process, so even an enabled game keeps the same stream-0 draw sequence (only the
    /// terrain output gains great-river tiles). Applies only to the random map path.
    /// </param>
    /// <param name="foreignPowerCount">
    /// The number of rival European powers to land (the New-Game rival-count dial, FreeCol's <c>NationOptions</c> roster
    /// size; 86d3fq1df). <b>Null = the classic default of 3</b> (byte-identical, ADR-009); a chosen value is clamped to
    /// <c>0..selectableRivals</c> (the selectable non-REF nations other than the human's own), so it can never ask for
    /// negative powers or more nations than the ruleset offers.
    /// </param>
    /// <param name="mapOptions">
    /// The tunable map-generation counts and climate bands — mountain/river/forest/bonus density and a temperature /
    /// humidity bias (FreeCol's <c>model.option.mountainNumber</c>/<c>riverNumber</c>/<c>forestNumber</c>/<c>bonusNumber</c>
    /// + <c>temperature</c>/<c>humidity</c>; 86d3fq18b/86d3fq13u). <b>Null = <see cref="MapGenerationOptions.Classic"/></b>
    /// (the historical values), so an unpicked new game is byte-identical (ADR-009).
    /// </param>
    /// <param name="rumourNumber">
    /// Land tiles per Lost City Rumour (the New-Game rumour-count dial, FreeCol's <c>model.option.rumourNumber</c>;
    /// 86d3fq1b8; <b>higher = fewer</b>). Defaults to <see cref="LostCityRumourGenerator.DefaultRumourNumber"/> (classic
    /// 35), so an unpicked new game is byte-identical (ADR-009).
    /// </param>
    /// <param name="nationalAdvantages">
    /// Whether national advantages are in play (the New-Game dial, FreeCol's <c>model.option.nationalAdvantages</c>;
    /// 86d3fq0za). <see cref="NationalAdvantages.None"/> suppresses every nation-type advantage (the modifier bonuses and
    /// the nation-specific starting-unit upgrades); <see cref="NationalAdvantages.Selectable"/> (the default) /
    /// <see cref="NationalAdvantages.Fixed"/> keep them, so an unpicked new game is byte-identical (ADR-009).
    /// </param>
    public static Game New(
        Ruleset ruleset, ulong seed, int mapWidth = 36, int mapHeight = 24,
        int startingGold = 0, int startingTax = 0,
        double landMassFraction = MapGenerator.DefaultLandMassFraction,
        string difficultyLevelId = DifficultyLevels.DefaultId,
        MapSource mapSource = MapSource.Random,
        string? humanNationId = null,
        LandStyle landStyle = LandStyle.Continent,
        MapImportResult? importOverride = null,
        bool greatRivers = false,
        int? foreignPowerCount = null,
        MapGenerationOptions? mapOptions = null,
        int rumourNumber = LostCityRumourGenerator.DefaultRumourNumber,
        NationalAdvantages nationalAdvantages = NationalAdvantages.Selectable)
    {
        // The number of rival European powers (FreeCol's NationOptions roster size; 86d3fq1df). Null = the classic
        // default (ForeignPowerCount = 3); clamped to a sane range so a New-Game dial can't ask for negative powers or
        // more than the ruleset has selectable non-REF nations (minus the human's own slot). The map-gen options
        // (86d3fq18b/86d3fq13u) default to the classic values, so an omitting caller is byte-identical (ADR-009).
        mapOptions ??= MapGenerationOptions.Classic;
        // A picked nation must be a real, selectable, non-REF European power; anything else (null, an unknown id, a
        // native/REF id) falls back to the nation-less classic human — so the default new game stays byte-identical.
        string? humanNation = humanNationId is { } nid
            && ruleset.EuropeanNations.Any(n => n.Id == nid && n.Selectable && !n.IsRef)
            ? humanNationId
            : null;
        var random = new Pcg32Random(seed);

        // The map: either a fixed scenario map imported from disk (FreeCol's fixed America terrain, decorated with our
        // rivers/resources/regions) or, by default, a procedurally grown New World. A fixed map sets its own dimensions,
        // so the world-size args apply only to the random path. Both draw from the same stream-0 RNG, so the default
        // (Random) game is unchanged. The import also carries any native settlements the definition declared (the
        // shipped america.txt declares none → an empty list → procedural native placement, byte-identical).
        MapImportResult? imported = importOverride ?? FixedMap.TryImport(mapSource, ruleset);
        IReadOnlyList<NativeSettlement> importedSettlements = imported?.Settlements ?? [];
        GameMap map = imported is null
            ? MapGenerator.Generate(ruleset, mapWidth, mapHeight, random, landMassFraction, landStyle, greatRivers, mapOptions)
            : MapGenerator.DecorateFixedMap(imported.Map, ruleset, random, mapOptions);

        // A scenario map that declared a [regions] layer keeps it: the decorate pass re-derives regions from terrain
        // (RegionGenerator.Assign), so we re-install the imported region table + per-tile ids over that result. This is
        // RNG-free and runs only when the import carried regions — the default/America maps declare none, so they keep
        // the generator-derived regions, byte-identical (ADR-006/009). FreeCol's FreeColMapLoader likewise imports each
        // saved tile's Region rather than re-deriving it.
        if (imported?.Map is { Regions.Count: > 0 } importedMap)
        {
            int[] importedRegionIds = [.. importedMap.AllPositions().Select(importedMap.RegionIdAt)];
            map.SetRegions(importedRegionIds, importedMap.Regions);
        }

        // The single human player (stream 0; foreign powers and natives become players in FP-3). Its nation is the
        // validated pick (null for the classic nation-less default), which seeds the human's national advantage +
        // colony names through the existing nation-id-driven seams (NationTypeModifiers / ColonyNamesFor).
        var human = new Player(playerId: 0, nationId: humanNation, isHuman: true, PlayerType.Colonial, new Market(ruleset))
        {
            Gold = startingGold,
            TaxRate = startingTax,
        };
        var game = new Game(ruleset, map, random, turn: 1, human)
        {
            DifficultyLevelId = difficultyLevelId,
            // The national-advantages mode (86d3fq0za) decides whether nation advantages apply at all (None = off).
            // Session-only — not persisted (a reloaded game re-derives Selectable, like the other New-Game seams).
            NationalAdvantages = nationalAdvantages,
        };

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
        // A scenario map may FIX the human's landing tile (an imported [starts] `human X Y`, FreeCol Player.entryTile).
        // When it does, the human starts exactly there — the scenario author owns placement; otherwise the coastal
        // heuristic chooses. Either way no RNG is drawn, so stream 0 stays byte-stable (the default/America maps declare
        // no [starts], so they keep the heuristic result, byte-identical).
        int equator = map.Height / 2; // map.Height == mapHeight for Random; a fixed map sets its own height
        var settleable = map.AllPositions().Where(Settleable).OrderBy(p => Math.Abs(p.Y - equator)).ToList();
        Position start = imported?.HumanStart
            ?? settleable.Where(p => HasWaterNeighbour(p) && HasLandNeighbour(p)).Cast<Position?>().FirstOrDefault()
            ?? settleable.Where(HasLandNeighbour).Cast<Position?>().FirstOrDefault()
            ?? settleable.First();
        game.SpawnHumanStartingUnits(ruleset, start);

        // The REF's entry tile: a scenario map may fix it (an imported [starts] `ref X Y`, FreeCol ourREF.setEntryTile);
        // otherwise it is the nearest water tile to the human's start (FreeCol picks a non-land tile within distance 10
        // of the start). Chosen deterministically — no RNG draw, so the human's stream 0 stays byte-stable; on
        // independence the King's fleet arrives here. Persisted. (The default/America maps declare none → heuristic.)
        game.SetRefEntryTile(imported?.RefEntry ?? game.NearestWaterTile(start));

        // Native settlements, on their own RNG stream so placement does not shift the
        // economy/father/immigration draws. They keep clear of the player's landing.
        //
        // Two sources. (1) An imported scenario map that declared a [settlements] section installs those exact
        // settlements (position, nation, type, capital flag, size, learnable skill) instead of generator-placed ones,
        // and skips the procedural generator entirely — the scenario author placed the natives. (2) Otherwise (the
        // default Random world, and the terrain-only america.txt, which declares no settlements) the procedural
        // generator places them exactly as before, so the default and America games stay byte-identical (ADR-006/009).
        var nativeRandom = new Pcg32Random(seed, NativeStreamId);
        if (importedSettlements.Count > 0)
        {
            // Finish the imported settlements as the generator would: seed each a general goods store (drawn on the
            // native stream, never stream 0). The importer already assigned stable ids from 1, so install them as-is.
            NativeSettlementGenerator.SeedGeneralStockTo(importedSettlements, ruleset, nativeRandom);
            foreach (NativeSettlement settlement in importedSettlements)
            {
                game._nativeSettlements.Add(settlement);
                game._nextSettlementId = Math.Max(game._nextSettlementId, settlement.Id + 1);
            }
        }
        else
        {
            var excluded = new HashSet<Position>(start.Neighbours().Append(start));
            foreach (NativeSettlement settlement in
                     NativeSettlementGenerator.Place(ruleset, map, nativeRandom, excluded))
            {
                game._nativeSettlements.Add(settlement);
                game._nextSettlementId = Math.Max(game._nextSettlementId, settlement.Id + 1);
            }
        }

        // Each settlement's wanted goods are derived from the seeded store it now holds (FreeCol updateWantedGoods).
        // RNG-free, so the native placement stream is not advanced and stream 0 is untouched (ADR-009).
        foreach (NativeSettlement settlement in game._nativeSettlements)
        {
            game.RecomputeWantedGoods(settlement);
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

        game.SpawnRivalsAndNatives(ruleset, start, humanNation, foreignPowerCount); // foreign powers (landed) + native nations as players (FP-3b/FP-4); the human's own nation is excluded from the rival roster. foreignPowerCount (null = classic 3) is the New-Game rival-count dial (86d3fq1df)

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
        foreach (Position p in LostCityRumourGenerator.Place(map, lcrExcluded, lcrRandom, rumourNumber))
        {
            map.AddRumour(p);
        }

        game.GenerateOffers(human); // Congress choices available from the first turn
        game.InitRecruitDock(human); // three recruits waiting on the Europe dock from turn 1
        game.SeedPriceBaseline(); // baseline the price-change watch at the ruleset seed prices, so a turn-1 Europe trade is caught (86d3fpz0p)

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

    /// <summary>Colonies a foreign power's AI founds before its remaining colonists explore instead (FP-4 minimal AI;
    /// difficulty-scoped via <see cref="DifficultyOptions.Ai"/>, classic value 3 — see <see cref="AiTuning"/>).
    /// Lifted from 1 to 3 once the colony economy (`86d3c9vmr`: food-first tile plan + building-worker fill) was proven
    /// to keep multiple AI colonies fed and solvent under the soak (no starvation / negative treasury at 25 seeds ×
    /// 200 turns). Bounded in practice by each power's handful of founder colonists. The instance-level founding checks
    /// read <c>Ruleset.Difficulty.Ai.MaxColonies</c>; this <c>internal static</c> alias exposes the (level-invariant)
    /// classic value so AI tests can drive a power to its cap without hard-coding the number.</summary>
    internal static int MaxAiColonies => DifficultyOptions.ClassicMedium.Ai.MaxColonies;

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
    private void SpawnRivalsAndNatives(
        Ruleset ruleset, Position humanStart, string? humanNationId = null, int? foreignPowerCount = null)
    {
        foreach (string nationType in _nativeSettlements.Select(s => s.NationTypeId).Distinct().OrderBy(n => n))
        {
            _players.Add(new Player(_players.Count, nationType, isHuman: false, PlayerType.Native, new Market(ruleset)));
        }

        // The rival roster is the selectable non-REF European nations other than the human's own, taken up to the
        // requested count (New-Game dial 86d3fq1df). Null = the classic ForeignPowerCount (3); a chosen count is clamped
        // to 0..availableRivals, so the dial can never ask for negative powers or more nations than the ruleset offers
        // (the picker offers the same upper bound). At the classic 3 this is byte-identical to the historical roster.
        var availableRivals = ruleset.EuropeanNations
            .Where(n => n.Selectable && !n.IsRef && n.Id != humanNationId)
            .ToList();
        int wanted = Math.Clamp(foreignPowerCount ?? ForeignPowerCount, 0, availableRivals.Count);

        var taken = new HashSet<Position>(); // tiles claimed by foreign landings (keeps powers off each other's units)
        var anchors = new List<Position>();  // each placed power's landing anchor (keeps powers spread along the coast)
        foreach (EuropeanNation nation in availableRivals.Take(wanted))
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
    /// <para>This array is the roster used only for the <b>nation-less classic human</b> (the New-Game default pick): it
    /// is held verbatim so that default game stays byte-identical (ADR-009 soak), independent of how the spec's
    /// <c>model.nationType.default</c> happens to enumerate its slots. A human who <i>picks</i> a nation instead reads
    /// that nation's own roster from <see cref="EuropeanNationType.StartingUnitsFor"/> (see
    /// <see cref="SpawnHumanStartingUnits"/>).</para>
    /// </summary>
    private static readonly (string TypeId, string? RoleId)[] HumanStartingRoster =
    [
        ("model.unit.freeColonist", "model.role.pioneer"),
        ("model.unit.freeColonist", "model.role.soldier"),
        ("model.unit.caravel", null),
    ];

    /// <summary>
    /// Places the human's starting units around <paramref name="start"/> — the land units (pioneer, soldier) on
    /// <paramref name="start"/> and its free land neighbours, the ship on a free adjacent water tile.
    /// <para>The <b>roster reads from the human's chosen nation</b> (FreeCol <c>EuropeanNationType.getStartingUnits</c>):
    /// the nation type's <see cref="EuropeanNationType.StartingUnitsFor"/> with the difficulty's
    /// <see cref="DifficultyOptions.ExpertStartingUnits"/> flag — so the Dutch land a merchantman, the French a hardy
    /// pioneer, the Spanish a mounted veteran soldier, and the two easiest levels upgrade each slot's expert variant.
    /// The <b>nation-less classic human</b> (default New-Game pick, <see cref="Player.NationId"/> null) keeps the verbatim
    /// <see cref="HumanStartingRoster"/>, so the default/soak game is byte-identical (ADR-009).</para>
    /// Land units are placed before the ship so a coastal start always has a water tile free for the ship. Deterministic
    /// — draws no RNG, so the human's stream 0 stays byte-stable (ADR-009); each unit lifts the human's fog
    /// (<see cref="RevealForOwner"/>). A unit type a ruleset variant omits, or a ship with no adjacent water (a
    /// landlocked start), is simply skipped.
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

        foreach ((string typeId, string? roleId) in HumanStartingRosterFor(ruleset))
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
            InitNationalityAndEthnicity(unit); // person units take the human's nation (null for the classic human)
            _units.Add(unit);
            taken.Add(pos);
            RevealForOwner(unit); // the human's own units lift its fog around the landing
        }
    }

    /// <summary>
    /// Resolves the human's starting roster as (unit type id, role id) pairs, land units first so the ship lands last.
    /// A human who picked a nation uses that nation type's <see cref="EuropeanNationType.StartingUnitsFor"/> (applying
    /// the difficulty's <see cref="DifficultyOptions.ExpertStartingUnits"/>); the nation-less classic human keeps the
    /// verbatim <see cref="HumanStartingRoster"/> so the default game is byte-identical (ADR-009). Each slot resolves
    /// the unit type once to order ships after land units (FreeCol places no ship before its colonists need a berth).
    /// </summary>
    private IEnumerable<(string TypeId, string? RoleId)> HumanStartingRosterFor(Ruleset ruleset)
    {
        if (NationalAdvantages == NationalAdvantages.None
            || HumanPlayer.NationId is not { } nationId
            || ruleset.EuropeanNations.FirstOrDefault(n => n.Id == nationId) is not { } nation)
        {
            // Nation-less classic human, OR national advantages turned off (86d3fq0za) → the neutral default roster (no
            // nation-specific starting-unit upgrade). The nation-less default is byte-identical (ADR-009).
            return HumanStartingRoster;
        }

        // Naval-last ordering (stable: land slots keep their spec order, then the ship), so the ship still finds a
        // free water tile on a coastal start regardless of how the spec enumerates the nation type's slots.
        return nation.NationType.StartingUnitsFor(ruleset.Difficulty.ExpertStartingUnits)
            .Select(u => (u.UnitTypeId, u.RoleId, IsNaval: ruleset.UnitTypes.Any(t => t.Id == u.UnitTypeId) && ruleset.Unit(u.UnitTypeId).IsNaval))
            .OrderBy(u => u.IsNaval)
            .Select(u => (u.UnitTypeId, u.RoleId));
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

        // Candidate coastal land anchors, ordered: (1) the nation's historical seaboard first — FreeCol's default CLASSIC
        // starting-positions mode lands each power on its preferred coast (Nation.startsOnEastCoast; east = the high-X
        // Atlantic half toward Europe, west = the low-X Pacific half — only Russia starts west); then (2) farthest from
        // the human; then (3) a stable Y/X tie-break. FreeCol also spreads powers by a minimum distance between their
        // starts; we honour it when a candidate satisfies the spacing for every already-placed power, and relax (first
        // to the off-coast candidates, finally to any tile) when the map is too crowded. Pure deterministic comparator —
        // no RNG draw, so the human's stream 0 stays byte-identical (ADR-009); placement just shifts which coast a rival
        // prefers.
        bool OnPreferredCoast(Position p) => (p.X >= Map.Width / 2) == nation.StartsOnEastCoast;
        var candidates = Map.AllPositions()
            .Where(p => FreeLand(p) && Chebyshev(p, humanStart) >= ForeignLandingMinDistance && p.Neighbours().Any(FreeWater))
            .OrderByDescending(OnPreferredCoast)
            .ThenByDescending(p => Chebyshev(p, humanStart)).ThenBy(p => p.Y).ThenBy(p => p.X)
            .ToList();
        Position? anchor =
            candidates.Cast<Position?>().FirstOrDefault(
                p => placedAnchors.All(a => Chebyshev(p!.Value, a) >= MinDistanceBetweenPowers))
            ?? candidates.Cast<Position?>().FirstOrDefault(); // relax the spacing on a crowded map

        // Same expert-aware resolution as the human (FreeCol: all players read the spec-level expertStartingUnits) —
        // on the default medium level the flag is off, so this is exactly RegularStartingUnits (byte-identical default).
        // With national advantages OFF (86d3fq0za) the expert upgrade is suppressed too (the regular roster only), so a
        // rival lands the plain colonist/soldier/ship for its nation rather than an expert-upgraded slot.
        bool expertUnits = NationalAdvantages != NationalAdvantages.None && ruleset.Difficulty.ExpertStartingUnits;
        foreach (EuropeanStartingUnit start in nation.NationType.StartingUnitsFor(expertUnits))
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
            InitNationalityAndEthnicity(unit); // a foreign power's person units take its own nation
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
            string? workImprovementId, int workTurnsLeft, int attrition,
            string? nationality, string? ethnicity, string? name)> units,
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
                  string? workImprovementId, int workTurnsLeft, int attrition,
                  string? nationality, string? ethnicity, string? name) in units)
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
                Attrition = attrition,
                Name = name,
            };
            // Identity (v52): a persisted value is a DIVERGED origin (a captured colonist) and is honoured verbatim; a
            // null means "equals the owner" (the omit-when-default case — every fresh unit, incl. a brave, and pre-v52
            // saves) and is re-derived from the now-set owner, mirroring the spawn-time stamp byte-for-byte.
            unit.Nationality = nationality ?? game.OwnerNationOf(unit);
            unit.Ethnicity = ethnicity ?? game.OwnerNationOf(unit);
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
        game.SeedPriceBaseline(); // baseline the price-change watch at the restored (already-moved) prices, so a reload does not re-announce old changes (86d3fpz0p)
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
        if (saved.TradeAccounts is { Count: > 0 })
        {
            market.LoadCounters(saved.TradeAccounts); // per-good trade accounting for the Trade report (v56)
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
            LastTaxRaiseTurn = saved.LastTaxRaiseTurn,
            DeclaredIndependenceTurn = saved.DeclaredIndependenceTurn,
            InterventionBells = saved.InterventionBells,
            IndependentNationName = saved.IndependentNationName, // v68; the free nation's chosen name on declaring
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
        if (saved.PeaceTurns is not null) // v53; the turn each peace took force (FreeCol peaceHolds' peaceTurn)
        {
            foreach ((int otherId, int peaceTurn) in saved.PeaceTurns)
            {
                player.PeaceTurnMap[otherId] = peaceTurn;
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
    /// The next unit id the allocator will hand out, captured for saving. This counter monotonically increases as units
    /// are created and is <b>not</b> rewound when a unit is destroyed, so it can run ahead of <c>max(existing id) + 1</c>.
    /// Persisting it (rather than only re-deriving it from surviving units on load) keeps a save/load round-trip
    /// byte-identical when the highest-id unit ever created has since been removed — see save-load.
    /// </summary>
    internal int NextUnitId => _nextUnitId;

    /// <summary>
    /// Restores the persisted next-unit-id counter (v54). Clamped with <see cref="Math.Max(int,int)"/> so it can only
    /// move the counter <b>forward</b> of the value already re-derived from the restored units — never below a surviving
    /// unit's id, which would risk an id collision on the next spawn.
    /// </summary>
    /// <param name="nextUnitId">The saved counter value.</param>
    internal void RestoreNextUnitId(int nextUnitId) => _nextUnitId = Math.Max(_nextUnitId, nextUnitId);

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
        InitNationalityAndEthnicity(unit);
        _units.Add(unit);
        RevealForOwner(unit); // a unit lifts its own owner's fog (the human's, or a foreign power's; natives none)
        return unit;
    }

    /// <summary>
    /// Stamps a freshly-spawned unit's <see cref="Unit.Nationality"/> and <see cref="Unit.Ethnicity"/> from its
    /// owner's nation (FreeCol <c>Unit.initialize</c>: <c>setNationality(owner.getNationId())</c> +
    /// <c>setEthnicity(owner.getNationId())</c>). Only a <b>person</b> gets them — a ship or wagon never does
    /// (FreeCol gates on <c>isPerson()</c>). The owner's nation is a native brave's <see cref="Unit.OwnerNationId"/>
    /// (the native nation type id) or, for a colonial unit, its colonial player's <see cref="Player.NationId"/>
    /// (null for the classic nation-less human, leaving both null = omitted in the save). Called once at spawn; a
    /// later capture deliberately does <b>not</b> re-stamp them, so a captured colonist keeps its origin.
    /// </summary>
    private void InitNationalityAndEthnicity(Unit unit)
    {
        if (!unit.Type.IsPerson)
        {
            return; // ships/wagons carry no nationality or ethnicity (FreeCol isPerson() gate)
        }
        string? nationId = OwnerNationOf(unit);
        unit.Nationality = nationId;
        unit.Ethnicity = nationId;
    }

    /// <summary>
    /// The nation id a unit's <em>current owner</em> would stamp as its origin (FreeCol <c>owner.getNationId()</c>):
    /// a native brave's <see cref="Unit.OwnerNationId"/>, else its colonial player's <see cref="Player.NationId"/>
    /// (null for the nation-less human / a non-person). The save uses this as the omit-when-default baseline so a
    /// freshly-raised unit — whose origin equals its owner — writes <b>no</b> nationality/ethnicity token (and a
    /// pre-v52 save re-derives the same value on load); only a unit whose origin has <em>diverged</em> from its owner
    /// (a captured colonist, a naturalised convert) persists an explicit value.
    /// </summary>
    internal string? OwnerNationOf(Unit unit) =>
        unit.Type.IsPerson ? unit.OwnerNationId ?? PlayerById(unit.OwnerId)?.NationId : null;

    /// <summary>
    /// A unit's movement points for a fresh turn: its unit-type base plus its role's movement bonus
    /// (FreeCol <c>Unit.getInitialMovesLeft</c> folding <c>model.modifier.movementBonus</c>) — e.g. a
    /// dragoon/scout/cavalry/mounted brave gets +9 (one extra "move" is 3 points). For a <b>naval</b> unit it
    /// also folds the owner's <c>movementBonus</c> — <b>Ferdinand Magellan</b> (+3, Congress) and the <b>Portuguese</b>
    /// <c>naval</c> nation-type advantage (+3, spec-scoped <c>model.ability.navalUnit</c>): both ride the same
    /// <see cref="ApplyGoodsModifiers(Player, string, int, int?)"/> fold (which now also folds nation-type modifiers),
    /// and the surrounding <c>IsNaval</c> gate <b>is</b> the naval scope — a land unit never enters this branch, so the
    /// Portuguese +3 (like Magellan's) applies to ships only. Stacked when a Portuguese player also elects Magellan (+6).
    /// The role lookup is null-safe so minimal rulesets without role data simply get the base.
    /// </summary>
    private int InitialMovement(Unit unit)
    {
        int moves = unit.Type.Movement + (int)(Ruleset.Roles.FirstOrDefault(r => r.Id == unit.RoleId)?.MovementBonus ?? 0);
        if (unit.Type.IsNaval && PlayerById(unit.OwnerId) is { } owner)
        {
            // Magellan (+3, Congress) AND the Portuguese naval nation-type advantage (+3) both target movementBonus;
            // ApplyGoodsModifiers folds the father modifier and the nation-type one together. The IsNaval gate is the
            // naval scope for both — so the nation advantage never leaks onto land units.
            moves = ApplyGoodsModifiers(owner, MovementBonusId, moves);
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
        // qualifies; none exist yet.) Otherwise the unit must wait. The +2 slack
        // is the "make 1/3 and 2/3 count as 3/3" round-up threshold, now routed
        // through the ruleset (MovementConstants.PartialMoveThreshold = classic 2).
        if (cost > movesLeft)
        {
            int threshold = Ruleset.MovementConstants.PartialMoveThreshold;
            bool allowed = movesLeft + threshold >= InitialMovement(unit) || cost <= movesLeft + threshold;
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

        // FreeCol pathfinding treats an unexplored tile as impassable to a planned route (BaseCostDecider: a
        // not-yet-explored target is ILLEGAL_MOVE), so the only way to enter the unknown is a manual single step —
        // and that step ends the unit's turn. We capture, before the move resolves the fog, whether the target was
        // already explored FOR THIS UNIT'S OWNER; a land unit stepping onto a tile that was still black for it then
        // spends all remaining movement (the classic "the wilderness uses up your turn" rule). Ships are exempt
        // (sea exploration does not stop a ship) and a target already explored costs only the normal terrain move.
        bool steppedIntoUnknown = !unit.Type.IsNaval && !WasExploredForOwner(unit, target);

        unit.Position = target;
        unit.MovementLeft -= check.Cost;
        unit.Orders = UnitOrders.Active; // moving wakes a fortified/sentry unit (FreeCol clears the state on a move)
        unit.Destination = null;         // a manual move cancels any standing goto (FreeCol setDestination(null))
        RevealForOwner(unit); // the mover lifts its own owner's fog (mirrors SpawnUnit)
        if (steppedIntoUnknown && unit.MovementLeft > 0)
        {
            unit.MovementLeft = 0; // entering unexplored ground ends the turn (FreeCol unexplored-tile move spends all moves)
        }
        if (unit.Type.IsCarrier)
        {
            SyncPassengers(unit); // any colonists aboard move with the ship
        }
        ActivateSentries(unit); // an enemy stepping adjacent wakes any sentried unit guarding the spot (FreeCol csActivateSentries)
        NoteFirstLandfall(unit); // a human land unit stepping ashore for the first time triggers the one-shot name-the-new-world prompt (FreeCol csMove firstLanding)
        TryExploreRumour(unit, target); // a land unit stepping onto a Lost City Rumour investigates it (may consume/transform the unit)
    }

    /// <summary>
    /// Wakes any sentried <em>enemy</em> unit standing next to <paramref name="mover"/>'s new tile (FreeCol
    /// <c>ServerUnit.csActivateSentries</c>): a sentry guards the ground, so the instant a hostile unit moves
    /// adjacent it returns to <see cref="UnitOrders.Active"/> — no longer skipped when cycling, so the player is
    /// prompted to react rather than the sentry sleeping through the threat. Only enemies of the sentry trigger it
    /// (FreeCol activates non-owned units on a contacting move; an enemy is the case that matters for guarding, and
    /// it keeps a player's own or allied units from waking each other's sentries). RNG-free, side-effect-light
    /// (only an order flip), so it never perturbs any seeded stream (ADR-009).
    /// </summary>
    private void ActivateSentries(Unit mover)
    {
        foreach (Position n in mover.Position.Neighbours())
        {
            foreach (Unit sentry in _units.Where(u => u.IsOnMap && u.Position == n
                && u.Orders == UnitOrders.Sentry && AreEnemies(mover, u)))
            {
                sentry.Orders = UnitOrders.Active;
            }
        }
    }

    /// <summary>
    /// Whether <paramref name="tile"/> had already been explored, for <paramref name="unit"/>'s owning colonial player,
    /// at the moment of the call (FreeCol <c>Player.hasExplored</c>). A native-owned unit lifts no fog and is treated as
    /// "already explored" everywhere (natives never trigger the unexplored-ends-turn rule). Read this <em>before</em>
    /// <see cref="RevealForOwner"/> resolves the move, or the destination will already have been lit.
    /// </summary>
    private bool WasExploredForOwner(Unit unit, Position tile) =>
        unit.OwnerNationId is not null
        || PlayerById(unit.OwnerId) is not { } owner
        || owner.Explored.Contains(tile);

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
    /// the owning colony — tripled by that colony's lumber mill — and rolls the chance the cleared ground exposes a
    /// hidden bonus resource), consumes the role's expended tools (reverting the pioneer to a plain colonist when its
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
                // The owning colony's lumber mill (model.modifier.tileTypeChangeProduction ×3 scoped to lumber)
                // multiplies the one-off delivery before it lands (FreeCol Settlement.apply(amount, …,
                // TILE_TYPE_CHANGE_PRODUCTION, deliver.getType())); a colony without one applies the ×1 identity.
                int delivered = (int)(change.ProductionAmount * LumberTileTypeChangeFactor(colony, goodsId));
                colony.AddGoods(Ruleset.StorageIdOf(goodsId), delivered);
            }

            // The cleared ground may reveal a hidden bonus resource (FreeCol csImproveTile expose-resource roll):
            // a chance per the improvement's expose-resource-percent, only when the (now-cleared) tile carries no
            // resource, picking weighted-random from the new terrain type's resource table and rolling its quantity.
            TryExposeResource(unit, pos, improvement);
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
    /// The multiplier a colony applies to a one-off tile-type-change goods delivery — its highest
    /// <see cref="BuildingType.LumberTileTypeChangeFactor"/> over the goods's matching buildings (a lumber mill ×3 for
    /// lumber; ×1 otherwise). FreeCol applies the settlement's <c>TILE_TYPE_CHANGE_PRODUCTION</c> modifier scoped to the
    /// delivered goods; only the lumber-scoped modifier (lumber mill) exists in the classic spec, so this returns 3 for
    /// a lumber-mill colony delivering lumber and 1 everywhere else.
    /// </summary>
    private double LumberTileTypeChangeFactor(Colony colony, string goodsId) =>
        goodsId != "model.goods.lumber"
            ? 1.0
            : colony.Buildings
                .Select(b => Ruleset.Building(b).LumberTileTypeChangeFactor)
                .DefaultIfEmpty(1.0)
                .Max();

    /// <summary>
    /// Rolls FreeCol's clear-forest resource-exposure (FreeCol <c>ServerUnit.csImproveTile</c>): with probability
    /// <see cref="TileImprovementType.ExposeResourcePercent"/>% — only when the improvement carries one (clear-forest =
    /// 5%) and the freshly-cleared tile holds no resource — a hidden bonus resource appears, picked weighted-random from
    /// the cleared terrain type's resource table and (for a finite type) given a rolled starting quantity. All draws go
    /// through the mover's owning player's seeded stream (ADR-009) in FreeCol's exact order: the percent gate, then the
    /// weighted type pick, then the quantity. No RNG is drawn unless the percent gate is reached, so a no-expose
    /// improvement (any road/plow, or a forest type with no exposable resources) stays byte-stable.
    /// </summary>
    private void TryExposeResource(Unit unit, Position pos, TileImprovementType improvement)
    {
        if (improvement.ExposeResourcePercent <= 0 || Map.ResourceAt(pos) is not null)
        {
            return;
        }
        IReadOnlyList<ResourceChance> table = Map.TerrainAt(pos).Resources;
        if (table.Count == 0 || PlayerById(unit.OwnerId) is not { } owner)
        {
            return; // native-owned pioneers (no colonial player) and resource-less terrain expose nothing
        }
        IGameRandom random = RandomFor(owner);
        if (random.Next(100) >= improvement.ExposeResourcePercent)
        {
            return; // the roll failed — no resource exposed
        }
        string resourceId = PickWeightedResource(table, random);
        Map.SetResource(pos, resourceId);
        Map.SetResourceQuantity(pos, NullIfLimitless(Ruleset.Resource(resourceId).RollQuantity(random)));
    }

    /// <summary>The weighted resource pick (FreeCol <c>RandomChoice.getWeightedRandom</c>) shared with map generation.</summary>
    private static string PickWeightedResource(IReadOnlyList<ResourceChance> table, IGameRandom random)
    {
        int roll = random.Next(table.Sum(r => r.Probability));
        foreach (ResourceChance entry in table)
        {
            roll -= entry.Probability;
            if (roll < 0)
            {
                return entry.ResourceId;
            }
        }
        return table[^1].ResourceId;
    }

    /// <summary>A rolled quantity of 0 means a limitless resource (no finite range) — store no quantity for it.</summary>
    private static int? NullIfLimitless(int quantity) => quantity > 0 ? quantity : null;

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
        // footprints never touch. A native-owned tile is still legal to found on — founding remains Allowed — but
        // FoundColony forces a buy-or-steal-or-abandon claim first (RequiredLandClaim / FoundColony's LandClaimChoice
        // overload); CheckFoundColony stays a pure legality gate (see natives.md).
        if (unit.Position.Neighbours().Any(n => Map.InBounds(n) && ColonyAt(n) is not null))
        {
            return MoveCheck.No("A colony cannot be founded next to another colony.");
        }
        return MoveCheck.Yes(0);
    }

    /// <summary>
    /// Founds a colony where the unit stands. The founding unit settles down and becomes the colony's first colonist
    /// (it leaves the map). If the centre tile is <b>native-owned</b>, this overload resolves the forced claim
    /// automatically for an <b>AI</b> founder (<see cref="AiResolveLandClaim"/> — pay if affordable, else steal) but
    /// throws <see cref="LandClaimRequiredException"/> for the <b>human</b>, who must surface the pay/steal/abandon
    /// choice (call the <see cref="FoundColony(Unit, LandClaimChoice)"/> overload).
    /// </summary>
    /// <exception cref="InvalidMoveException">Founding is not allowed; see <see cref="CheckFoundColony"/>.</exception>
    /// <exception cref="LandClaimRequiredException">The human is founding on native-owned land without a claim choice.</exception>
    public Colony FoundColony(Unit unit) => FoundColony(unit, claim: null);

    /// <summary>
    /// Founds a colony on a <b>native-owned</b> centre tile, resolving the forced claim with the human's
    /// <paramref name="claim"/> (FreeCol <c>csClaimLand</c> before the build): <see cref="LandClaimChoice.Buy"/> pays
    /// the land price, <see cref="LandClaimChoice.Steal"/> takes it and angers the owning nation (per-player alarm).
    /// On a tile that is not native-owned, <paramref name="claim"/> is ignored. Use <see cref="RequiredLandClaim(Position)"/>
    /// to learn the price before offering the choice.
    /// </summary>
    /// <exception cref="InvalidMoveException">Founding is not allowed (see <see cref="CheckFoundColony"/>), or the buy
    /// is unaffordable / the choice is <see cref="LandClaimChoice.Abandon"/>.</exception>
    public Colony FoundColony(Unit unit, LandClaimChoice claim) => FoundColony(unit, (LandClaimChoice?)claim);

    /// <summary>Shared founding core; <paramref name="claim"/> null means "no explicit choice" — auto-resolved for an AI founder, rejected for the human (forces the UI choice).</summary>
    private Colony FoundColony(Unit unit, LandClaimChoice? claim)
    {
        MoveCheck check = CheckFoundColony(unit);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }

        // The centre tile is claimed from the natives BEFORE the colony exists (FreeCol claims the build tile in
        // InGameController.claimLand, then builds): a native-owned site forces buy-or-steal-or-abandon. The human must
        // pass an explicit choice (else throw, so the UI raises its dialog); an AI founder resolves deterministically.
        Player founder = PlayerById(unit.OwnerId) ?? _human;
        ForcedLandClaim forced = RequiredLandClaim(founder, unit.Position);
        if (forced.Required)
        {
            if (claim is null)
            {
                if (founder.PlayerId == _human.PlayerId)
                {
                    throw new LandClaimRequiredException(forced.BuyPrice, forced.OwningNation!);
                }
                claim = AiResolveLandClaim(founder, unit.Position);
            }
            ResolveForcedLandClaim(founder, unit.Position, claim.Value);
        }

        IReadOnlyList<string> names = ColonyNamesFor(unit.OwnerId);
        string name = names[(_nextColonyId - 1) % names.Count];
        var colony = new Colony(_nextColonyId++, name, unit.Position, population: 1, ownerId: unit.OwnerId)
        {
            Government = Ruleset.Difficulty.Government, // production-bonus thresholds from the difficulty level
            RebelLibertyDivisor = Ruleset.ColonyConstants.LibertyPerRebel, // liberty-per-rebel from the ruleset (86d3drpgg)
            ExportRetainDefault = Ruleset.ColonyConstants.DefaultExportLevel, // custom-house default retain from the ruleset (86d3drpgg)
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
        ReturnRoleEquipmentToColony(unit, colony); // a soldier/dragoon/scout/pioneer founds unequipped — its muskets/horses/tools stock the new colony, not lost
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
        ReturnRoleEquipmentToColony(unit, colony); // an armed/equipped joiner drops its muskets/horses/tools into the colony store, not lost
        _units.Remove(unit);
        // An arriving expert claims its specialty slot FIRST (bumping a free colonist off that good) before the
        // generic auto-assign — otherwise the still-idle expert would be seated on a food tile and never reach its
        // specialty. The displaced free colonist (and any other idle) is then sent to the fields.
        TrySeatExpertBySwap(colony, unit.Type.Id);
        AutoAssignIdleToFood(colony);
    }

    /// <summary>
    /// Full expert-swap (86d3drn5j, FreeCol <c>Unit.trySwapExpert</c>/<c>swapWork</c>): when a just-joined colonist of
    /// <paramref name="joinedType"/> is still <b>idle</b> and is an <b>expert</b> at some good, it displaces a
    /// <b>free</b> colonist already working that good so the expert takes the specialist slot it is best at — exactly
    /// FreeCol's rule that an expert evicts a non-expert from its specialty. The bumped free colonist is left idle for
    /// the caller's <see cref="AutoAssignIdleToFood(Colony)"/> to re-seat on the next-best food tile. A no-op when the
    /// joiner is a free colonist (or a non-goods expert), isn't idle, or no free colonist is working its good (nothing
    /// to swap — the expert stays idle and is seated normally). RNG-free, so the human's stream 0 stays byte-stable
    /// (ADR-009). Tiles are scanned in the same row-major order the rest of the colony code uses, so the swap is
    /// deterministic.
    /// </summary>
    private void TrySeatExpertBySwap(Colony colony, string joinedType)
    {
        if (Ruleset.Unit(joinedType).ExpertProduction is not { } specialty)
        {
            return; // a free colonist (or a non-goods expert) never bumps anyone
        }
        if (!colony.IdleWorkerTypes.Contains(joinedType))
        {
            return; // the expert is not idle (already seated) — nothing to swap
        }
        // The first free colonist (no overlay entry) working the expert's specialty good, in row-major order.
        Position? target = colony.TileWorkers
            .Where(kv => kv.Value == specialty && colony.WorkerTypeAt(kv.Key) == Colony.FreeColonistTypeId)
            .Select(kv => kv.Key)
            .OrderBy(t => t.Y).ThenBy(t => t.X)
            .Cast<Position?>()
            .FirstOrDefault();
        if (target is { } tile)
        {
            colony.SwapInExpertForTile(tile, joinedType); // expert takes the tile; the free colonist is freed (idle)
        }
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
    /// Gives an individual unit a <b>custom name</b>, or clears it back to the generic type name (FreeCol
    /// <c>Unit.setName</c> via the <c>Nameable</c> interface — christening a famous ship, say). A blank or null
    /// <paramref name="name"/> <b>clears</b> the custom name (FreeCol treats an empty rename as a reset to the default);
    /// otherwise the trimmed text is stored. RNG-free, so the human's stream stays byte-stable (ADR-009). Persisted
    /// omit-when-null (save v52), so an unnamed unit serialises byte-identically.
    /// </summary>
    /// <param name="unit">The unit to (re)name.</param>
    /// <param name="name">The new custom name, or null/blank to clear it.</param>
    public void NameUnit(Unit unit, string? name) =>
        unit.Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim();

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

    /// <summary>
    /// Sells a colony's goods to <paramref name="player"/>'s European market (the colony's owner today). When
    /// <paramref name="ignoreBoycott"/> is true the boycott gate is bypassed — the custom-house smuggling path
    /// (FreeCol <c>Player.canTrade(type, Market.Access.CUSTOM_HOUSE)</c> under <c>customIgnoreBoycott</c>): a boycotted
    /// good still sells, with tax withheld and the price still moving exactly as a normal sale (classic applies no
    /// extra smuggling penalty). The default (false) keeps the manual-sale gate that refuses a boycotted good.
    /// </summary>
    internal int SellColonyGoods(Player player, Colony colony, string goodsId, int amount, bool ignoreBoycott = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);
        if (!player.Market.IsTradeable(goodsId))
        {
            throw new InvalidMoveException($"{goodsId} cannot be sold in Europe.");
        }
        if (!ignoreBoycott && !player.Market.CanTrade(goodsId))
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
        PropagateTradeToRivalMarkets(player, goodsId, amount); // FreeCol ServerPlayer.sellInEurope:1327 — incl. custom house (ServerColony:851)
        return sale.GoldAfterTax;
    }

    /// <summary>
    /// Throws away <paramref name="amount"/> of a stored good from <paramref name="colony"/>'s warehouse — the FreeCol
    /// warehouse <b>dump</b> / discard (<c>Colony.removeGoods</c> with no market, gold or tax effect): the goods are
    /// simply destroyed, freeing warehouse space. Unlike <see cref="SellColonyGoods(Colony, string, int)"/> there is no
    /// payment and no price move — it is for ditching a good you cannot or will not sell (a boycotted good, or one
    /// overflowing the warehouse and wasting each turn's production). The colony must actually hold that much of the
    /// good. <b>RNG-free</b> and save-neutral (it only lowers an already-saved stored amount).
    /// </summary>
    /// <param name="colony">The colony whose warehouse loses the goods.</param>
    /// <param name="goodsId">The good to discard.</param>
    /// <param name="amount">How much to throw away (must be positive and ≤ the stored amount).</param>
    /// <exception cref="System.ArgumentOutOfRangeException">The amount is zero or negative.</exception>
    /// <exception cref="InvalidMoveException">The colony does not hold that much of the good.</exception>
    public void DumpColonyGoods(Colony colony, string goodsId, int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);
        if (colony.StoreOf(goodsId) < amount)
        {
            throw new InvalidMoveException($"The colony does not have {amount} {goodsId} to dump.");
        }
        colony.AddGoods(goodsId, -amount); // destroyed: no gold, no market move, no tax (AddGoods floors at 0)
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

    /// <summary>
    /// Sets a colony's custom-house <b>import level</b> for a good (FreeCol <c>ExportData.setImportLevel</c>): the ceiling
    /// an automatic delivery (a trade-route drop-off) will not stock the good past. The good must be storable and
    /// tradeable — the same gate as <see cref="SetColonyExport"/>. A non-negative <paramref name="importLevel"/> caps
    /// delivery at that amount; a negative value (or <see cref="Colony.ImportLevelUnset"/>) clears the cap back to "not set",
    /// so the effective ceiling reverts to the colony's warehouse capacity (FreeCol <c>getEffectiveImportLevel</c>) and the
    /// good auto-imports exactly as it did before an import level was set. (Setting is allowed regardless of whether the
    /// custom house is built; the cap only acts on the auto-delivery path.)
    /// </summary>
    /// <exception cref="InvalidMoveException">The good cannot be traded through the custom house (non-tradeable / non-storable).</exception>
    public void SetColonyImport(Colony colony, string goodsId, int importLevel)
    {
        GoodsType goods = Ruleset.Goods(goodsId);
        if (!goods.IsTradeable || !goods.IsStorable)
        {
            throw new InvalidMoveException($"{goodsId} cannot be traded through the custom house.");
        }
        colony.SetImport(goodsId, importLevel);
    }

    /// <summary>
    /// A good's <b>effective import level</b> at <paramref name="colony"/> (FreeCol
    /// <c>ExportData.getEffectiveImportLevel(capacity)</c>): the good's set import level, or — when unset
    /// (<see cref="Colony.ImportLevelUnset"/>) — the colony's warehouse capacity. This is the ceiling an automatic
    /// delivery will not stock the good past. Public oracle for the presentation (ADR-006) so the colony screen can show /
    /// default the import-level control to the warehouse capacity, matching the cap the trade-route delivery applies.
    /// </summary>
    public int EffectiveImportLevel(Colony colony, string goodsId)
    {
        int set = colony.ExportOf(goodsId).ImportLevel;
        return set >= 0 ? set : WarehouseCapacity(colony);
    }

    /// <summary>Base turns a naval unit spends crossing the high seas each way from a high-seas <em>edge</em> tile (FreeCol TURNS_TO_SAIL); a tile deeper in the high-seas band sails longer — see <see cref="SailTurnsFor"/>.</summary>
    public const int SailTurns = 3;

    /// <summary>
    /// How many extra turns a deep high-seas embarkation tile adds to the crossing, beyond the base — capped so even a
    /// far-from-edge launch never sails absurdly long (a faithful-subset bound on FreeCol's distance-driven crossing).
    /// </summary>
    private const int MaxSailEdgeBonus = 3;

    /// <summary>
    /// The crossing length for a ship leaving <paramref name="embark"/>: the <see cref="SailTurns"/> base <b>plus how far
    /// the open-ocean route to Europe lies from the embarkation point</b> (FreeCol varies the crossing by the sailing
    /// unit's distance to the high-seas edge — its <c>Tile.highSeasCount</c>, the hop count from the
    /// directly-high-seas-connected edge water), then shortened by the owner's Congress <c>sailHighSeas</c> modifier
    /// (Ferdinand Magellan −1) and floored at 1. A ship setting sail from a high-seas tile right at the map edge (the
    /// open-ocean exit) sails the base 3 turns; one launched from a high-seas tile tucked deeper in the band (a port far
    /// from the open ocean) sails longer. The extra is the <see cref="HighSeasEdgeDistance"/> of the gateway high-seas
    /// tile, capped at <see cref="MaxSailEdgeBonus"/>. Deterministic (pure geometry, no RNG).
    /// </summary>
    /// <param name="owner">The ship's owner (its Congress folds the Magellan modifier); null skips the fold.</param>
    /// <param name="embark">The tile the ship sets sail from / re-enters at (its on-map <see cref="Unit.Position"/>).</param>
    private int SailTurnsFor(Player? owner, Position embark)
    {
        int edgeBonus = Math.Min(MaxSailEdgeBonus, HighSeasEdgeDistance(embark));
        int baseTurns = SailTurns + edgeBonus;
        return owner is null ? baseTurns : Math.Max(1, ApplyGoodsModifiers(owner, SailHighSeasId, baseTurns));
    }

    /// <summary>
    /// How far the open-ocean route to Europe lies from <paramref name="embark"/> — the crossing-length driver. The
    /// high-seas band hugs the east/west map edges (FreeCol <c>Map.resetHighSeas</c>), and the open-ocean exit is the
    /// outermost high-seas column, so this is the <b>nearest high-seas tile's</b> column gap to the nearer vertical edge
    /// (<c>min(x, width − 1 − x)</c>) — FreeCol's <c>Tile.highSeasCount</c> analogue. A ship launched from (or re-entering
    /// at) a tile whose nearest high seas sits at the very edge gets 0 (the base crossing); a high-seas tile buried deep
    /// in the band measures its own distance and sails longer. If the map has no high seas at all (test fixtures), the
    /// distance is 0. Pure geometry (no RNG); used only to vary the Atlantic crossing length.
    /// </summary>
    /// <param name="embark">The embarkation / re-entry tile.</param>
    private int HighSeasEdgeDistance(Position embark)
    {
        int EdgeGap(Position p) => Math.Min(p.X, Map.Width - 1 - p.X);

        // A ship leaving a high-seas tile measures that tile directly (the SailToEurope gate guarantees this case).
        if (Map.TerrainAt(embark).Id == HighSeasId)
        {
            return EdgeGap(embark);
        }
        // Otherwise (a coastal re-entry tile on the SailToNewWorld leg) measure the nearest high-seas tile — the gateway
        // the ship reaches the open ocean through. The band hugs the edges, so a coastal port's gateway is at the edge.
        Position? nearest = Map.AllPositions()
            .Where(p => Map.TerrainAt(p).Id == HighSeasId)
            .Cast<Position?>()
            .OrderBy(p => Chebyshev(embark, p!.Value))
            .FirstOrDefault();
        return nearest is { } hs ? EdgeGap(hs) : 0; // no high seas on the map (fixtures) → no extra
    }

    /// <summary>The human player's units currently docked in Europe (resolved by owner — FP-2).</summary>
    public IEnumerable<Unit> UnitsInEurope => _units.Where(u => u.Location == UnitLocation.InEurope && IsHumanOwned(u));

    /// <summary>
    /// The human player's ships currently crossing the high seas <b>towards Europe</b> (in transit, not yet docked) —
    /// the "expected soon" arrivals. Read-only oracle (ADR-006) for the Europe screen's in-transit lane; each ship's
    /// remaining crossing length is <see cref="Unit.SailTurnsRemaining"/>. Resolved by owner (FP-2).
    /// </summary>
    public IReadOnlyList<Unit> ShipsSailingToEurope =>
        _units.Where(u => u.Location == UnitLocation.SailingToEurope && IsHumanOwned(u)).ToList();

    /// <summary>
    /// The human player's ships currently crossing the high seas <b>towards the New World</b> (in transit, not yet
    /// arrived) — the "bound for the New World" departures. Read-only oracle (ADR-006) for the Europe screen's
    /// in-transit lane; each ship's remaining crossing length is <see cref="Unit.SailTurnsRemaining"/>. Resolved by
    /// owner (FP-2).
    /// </summary>
    public IReadOnlyList<Unit> ShipsSailingToNewWorld =>
        _units.Where(u => u.Location == UnitLocation.SailingToNewWorld && IsHumanOwned(u)).ToList();

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
        // The crossing length varies with how far this high-seas tile sits from the open-ocean edge (Magellan still shortens it).
        unit.SailTurnsRemaining = SailTurnsFor(PlayerById(unit.OwnerId), unit.Position);
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
        // The return crossing is symmetric: its length varies with the departure tile (the ship re-enters there) and
        // Magellan still shortens it. unit.Position is the high-seas tile the ship left from (set on its outbound leg).
        unit.SailTurnsRemaining = SailTurnsFor(PlayerById(unit.OwnerId), unit.Position);
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
    /// <c>Player.newTradeRoute</c>). Every stop must name a colony <paramref name="player"/> owns <b>or be Europe</b>
    /// (<see cref="TradeRouteStop.IsEurope"/>). The route is given the next per-player id and added to
    /// <see cref="Player.TradeRoutes"/>.
    /// </summary>
    /// <exception cref="InvalidMoveException">A stop names a colony the player does not own (a Europe stop is always allowed).</exception>
    public TradeRoute CreateTradeRoute(Player player, string name, IReadOnlyList<TradeRouteStop> stops)
    {
        foreach (TradeRouteStop stop in stops)
        {
            if (stop.IsEurope)
            {
                continue; // a Europe stop is always a valid location for any player (FreeCol Player.getEurope)
            }
            if (_colonies.FirstOrDefault(c => c.Id == stop.ColonyId) is not { } colony || colony.OwnerId != player.PlayerId)
            {
                throw new InvalidMoveException("A trade-route stop must be one of your own colonies (or Europe).");
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
    /// carrier heads for its current stop; on arrival it <b>delivers</b> everything it holds that the stop doesn't list
    /// to load (<see cref="UnloadToColony"/>), <b>loads</b> the stop's goods up to its hold (<see cref="LoadFromColony"/>),
    /// and advances to the next stop (wrapping). A <b>Europe</b> stop (<see cref="TradeRouteStop.IsEurope"/>) is served at
    /// the European market instead: a docked carrier <b>sells</b> what the stop doesn't load and <b>buys</b> what it does
    /// (<see cref="ServeEuropeStop"/>); a carrier still in the New World sails across (reaching the high seas) or steps
    /// toward the nearest high-seas tile; a carrier mid-crossing simply waits for it to arrive. A carrier whose route was
    /// deleted is dropped; a stop whose colony is gone — or a Europe stop a non-sea carrier can never reach — is skipped.
    /// The step uses <see cref="StepToward"/> on the owner's stream — a route-less player iterates nothing, so it never
    /// perturbs the human's stream 0 or churns goldens (ADR-009).
    /// </summary>
    private void ProcessTradeRoutes(Player player)
    {
        foreach (Unit unit in _units
            .Where(u => u.OwnerId == player.PlayerId && u.IsOnTradeRoute && (u.IsOnMap || u.Location is UnitLocation.InEurope))
            .OrderBy(u => u.Id).ToList())
        {
            if (player.TradeRoutes.FirstOrDefault(r => r.Id == unit.TradeRouteId) is not { Stops.Count: > 0 } route)
            {
                ClearTradeRoute(unit); // the route was deleted (or is empty) → drop the assignment
                continue;
            }
            int stopIndex = unit.TradeRouteStopIndex % route.Stops.Count;
            TradeRouteStop stop = route.Stops[stopIndex];
            if (stop.IsEurope)
            {
                ProcessEuropeStop(player, unit, route, stopIndex, stop);
                continue;
            }
            if (unit.Location is UnitLocation.InEurope)
            {
                if (unit.Type.IsNaval && !unit.IsUnderRepair)
                {
                    SailToNewWorld(unit); // a colony stop is served on the map → leave Europe and cross back
                }
                continue; // under repair (or a stuck non-sailer) → wait to re-enter the map before serving the colony
            }
            if (unit.Location is not UnitLocation.OnMap)
            {
                continue; // sailing the high seas (just left a Europe stop) → wait for the crossing to finish
            }
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

    /// <summary>
    /// Advances a carrier toward, or serves it at, a <b>Europe</b> trade-route stop. If the carrier is docked in Europe it
    /// is served (<see cref="ServeEuropeStop"/>) and the route advances; if it's on the map it sails across (standing on
    /// the high seas) or steps toward the nearest high-seas tile to embark; if it's mid-crossing it waits. A non-naval
    /// carrier (a wagon train) can never reach Europe, so the stop is skipped — the self-healing analogue of a vanished
    /// colony. Sailing/selling/buying draw no RNG; the only RNG is <see cref="StepToward"/>'s tie-break on the owner's
    /// stream (ADR-009).
    /// </summary>
    private void ProcessEuropeStop(Player player, Unit unit, TradeRoute route, int stopIndex, TradeRouteStop stop)
    {
        if (!unit.Type.IsNaval)
        {
            unit.TradeRouteStopIndex = (stopIndex + 1) % route.Stops.Count; // a wagon train cannot sail to Europe → skip the stop
            return;
        }
        if (unit.Location is UnitLocation.InEurope)
        {
            ServeEuropeStop(player, unit, stop);
            unit.TradeRouteStopIndex = (stopIndex + 1) % route.Stops.Count;
            return;
        }
        if (unit.Location is not UnitLocation.OnMap)
        {
            return; // sailing the high seas (to Europe or back) → wait for the crossing to finish
        }
        if (CheckSailToEurope(unit).Allowed)
        {
            SailToEurope(unit); // standing on the high seas → cross now
        }
        else if (NearestHighSeasTile(player, unit.Position) is { } highSeas && StepToward(player, unit, highSeas) is { } step)
        {
            MoveUnit(unit, step); // make for the map edge to embark for Europe
        }
    }

    /// <summary>
    /// Serves a docked carrier at a <b>Europe</b> stop (FreeCol's trade-route Europe leg): <b>sells</b> to the European
    /// market everything the carrier holds that <paramref name="stop"/> does not list to load (the delivery half), then
    /// <b>buys</b> the stop's listed goods up to the carrier's free hold and the owner's gold (the load half). Boycotted or
    /// untradeable goods are simply left aboard (they stay until a stop can take them). Mirrors <see cref="ServeTradeRouteStop"/>
    /// but against the market (<see cref="SellShipCargo(Player, Unit, string, int)"/>/<see cref="BuyEuropeGoods(Player, Unit, string, int)"/>) instead of a colony warehouse.
    /// </summary>
    private void ServeEuropeStop(Player player, Unit carrier, TradeRouteStop stop)
    {
        foreach ((string goodsId, int amount) in carrier.Cargo.Where(c => !stop.LoadGoodsIds.Contains(c.Key)).ToList())
        {
            if (player.Market.IsTradeable(goodsId) && player.Market.CanTrade(goodsId)) // skip boycotted/untradeable → keep it aboard
            {
                SellShipCargo(player, carrier, goodsId, amount); // sell what this stop doesn't want
            }
        }
        foreach (string goodsId in stop.LoadGoodsIds.Distinct())
        {
            if (!player.Market.IsTradeable(goodsId))
            {
                continue;
            }
            int partial = SlotsFor(carrier.CargoOf(goodsId)) * CargoSlotSize - carrier.CargoOf(goodsId); // slack in the current stack
            // FreeCol getCompactCargo caps the buy target at CargoSlotSize × (times listed) − what's already aboard, not
            // the whole free hold (Ref TradeRouteStop.java:164; InGameController.java:2431).
            int room = Math.Min(partial + CargoSlotsFree(carrier) * CargoSlotSize, CompactCargoRoom(carrier, stop, goodsId));
            // buy as much as fits AND the treasury affords (chunked price rises as we drain the market) — binary-narrow the max
            int buy = MaxAffordableBuy(player, carrier, goodsId, room);
            if (buy > 0)
            {
                BuyEuropeGoods(player, carrier, goodsId, buy);
            }
        }
    }

    /// <summary>The largest amount of <paramref name="goodsId"/> (≤ <paramref name="cap"/>) the docked <paramref name="carrier"/> can buy for <paramref name="player"/> right now — fits the hold and the treasury at the chunked market ask. 0 if none is affordable.</summary>
    private int MaxAffordableBuy(Player player, Unit carrier, string goodsId, int cap)
    {
        int lo = 0, hi = cap;
        while (lo < hi)
        {
            int mid = lo + (hi - lo + 1) / 2;
            if (CheckBuyEuropeGoods(player, carrier, goodsId, mid).Allowed)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }
        return lo;
    }

    /// <summary>
    /// Serves one trade-route stop: deliver everything the carrier holds that <paramref name="stop"/> doesn't load — but
    /// no further than each good's <see cref="EffectiveImportLevel"/> (FreeCol <c>getImportAmount</c>, which caps an
    /// automatic delivery at the colony's effective import level) — then load the stop's goods up to the carrier's free
    /// hold. Any surplus a good's import cap forbids is left aboard the carrier (FreeCol leaves un-deliverable cargo on the
    /// carrier, to ride to a stop that can take it).
    /// </summary>
    private void ServeTradeRouteStop(Unit carrier, Colony colony, TradeRouteStop stop)
    {
        foreach ((string goodsId, int amount) in carrier.Cargo.Where(c => !stop.LoadGoodsIds.Contains(c.Key)).ToList())
        {
            // FreeCol getImportAmount: deliver at most (effective import level − what's already here), so an automatic
            // delivery never stocks a good past its import level. Unset levels default to the warehouse capacity, so a
            // good with no import level set delivers its whole load exactly as before (bounded only by the warehouse).
            int room = EffectiveImportLevel(colony, goodsId) - colony.StoreOf(goodsId);
            int deliver = Math.Min(amount, room);
            if (deliver > 0)
            {
                UnloadToColony(carrier, colony, goodsId, deliver); // deliver what this stop doesn't want, up to the import cap
            }
        }
        foreach (string goodsId in stop.LoadGoodsIds.Distinct())
        {
            int available = colony.StoreOf(goodsId);
            int partial = SlotsFor(carrier.CargoOf(goodsId)) * CargoSlotSize - carrier.CargoOf(goodsId); // slack in the current stack
            int holdRoom = partial + CargoSlotsFree(carrier) * CargoSlotSize;
            // FreeCol getCompactCargo: the auto-load TARGET for a good is CargoSlotSize × (times it is listed at this
            // stop), minus what's already aboard — not the whole free hold. We honour that per-type cap so a good listed
            // once tops up to 100, listed twice to 200, etc. (Ref TradeRouteStop.java:164; InGameController.java:2293.)
            int load = Math.Min(Math.Min(available, holdRoom), CompactCargoRoom(carrier, stop, goodsId));
            if (load > 0)
            {
                LoadFromColony(carrier, colony, goodsId, load);
            }
        }
    }

    /// <summary>
    /// The remaining amount of <paramref name="goodsId"/> a trade-route auto-load should top the <paramref name="carrier"/>
    /// up to at <paramref name="stop"/>, mirroring FreeCol <c>TradeRouteStop.getCompactCargo</c> (each listing of a goods
    /// type contributes <see cref="CargoSlotSize"/> to that type's load target, duplicates accumulating), less what the
    /// carrier already holds of it. So a good listed once auto-loads up to 100 units, listed twice up to 200, and so on —
    /// never the whole free hold. Never negative. (Ref <c>TradeRouteStop.java:164</c>; <c>InGameController.java:2293/2431</c>.)
    /// </summary>
    private int CompactCargoRoom(Unit carrier, TradeRouteStop stop, string goodsId)
    {
        int listings = stop.LoadGoodsIds.Count(g => g == goodsId);
        return Math.Max(0, listings * CargoSlotSize - carrier.CargoOf(goodsId));
    }

    // ----- Trade-route validation (86d3drn0j) -----------------------------------------------------------------
    // A pure read mirroring FreeCol's TradeRoute.verify(): it returns advisory WARNINGS for a route (FreeCol warns,
    // it never blocks — a route with warnings still runs, it just may not behave as the player intends). Each warning
    // maps to a FreeCol model.tradeRoute.* case. Whereas verify() returns the FIRST problem only, we surface ALL of
    // them (richer UI) — a strict superset of FreeCol's information. No state changes, no RNG, no save fields.

    /// <summary>
    /// Whether the ENHANCED_TRADE_ROUTES game option is in effect (FreeCol <c>GameOptions.ENHANCED_TRADE_ROUTES</c>,
    /// <c>model.option.enhancedTradeRoutes</c>, classic <c>defaultValue="false"</c>). Read live from the parsed ruleset
    /// (<see cref="Specification.GameOptions.EnhancedTradeRoutes"/>), overridable at New Game via
    /// <see cref="Ruleset.WithEnhancedTradeRoutes"/>. When on, FreeCol relaxes the "always-present goods" check (cargoes
    /// are sorted to maximise transfer); we honour that relaxation here (the always-present advisory is suppressed).
    /// Only the validation relaxation is modelled — the cargo re-sort / import-amount behaviours are a separate item.
    /// Off in the classic ruleset, so the default game's route validation is unchanged (ADR-009).
    /// </summary>
    private bool EnhancedTradeRoutes => Ruleset.GameOptions.EnhancedTradeRoutes;

    /// <summary>
    /// Validates a single trade route and returns its advisory <see cref="TradeRouteWarning"/>s (FreeCol
    /// <c>TradeRoute.verify()</c>). The route is <b>not</b> changed and warnings never block — they are surfaced for the
    /// player. Checks, faithful to FreeCol:
    /// <list type="bullet">
    /// <item>fewer than two stops (<see cref="TradeRouteWarningKind.NotEnoughStops"/>);</item>
    /// <item>a stop naming a colony the owner does not own / that no longer exists (<see cref="TradeRouteWarningKind.InvalidStop"/>);</item>
    /// <item>no stop loads any goods, so nothing would be hauled (<see cref="TradeRouteWarningKind.AllEmpty"/>);</item>
    /// <item>a good loaded at <em>every</em> stop, so it is never delivered anywhere (<see cref="TradeRouteWarningKind.GoodsAlwaysPresent"/>) —
    /// suppressed when <see cref="EnhancedTradeRoutes"/> is on.</item>
    /// </list>
    /// A valid route returns an empty list. The owning player is resolved from <see cref="Players"/>; a route held by no
    /// player is treated as ownerless (every stop is then reported invalid).
    /// </summary>
    /// <param name="route">The route to validate.</param>
    /// <returns>The warnings, in check order (empty when the route is valid).</returns>
    public IReadOnlyList<TradeRouteWarning> ValidateTradeRoute(TradeRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);
        Player? owner = _players.FirstOrDefault(p => p.TradeRoutes.Any(r => ReferenceEquals(r, route)))
            ?? _players.FirstOrDefault(p => p.TradeRoutes.Any(r => r.Id == route.Id));
        return ValidateTradeRoute(owner, route);
    }

    /// <summary>
    /// Validates every trade route <paramref name="player"/> owns, concatenating their warnings (FreeCol surfaces these
    /// per-route on the route panel / via <c>checkIntegrity</c>). Returns an empty list when the player has no routes or
    /// none has a problem. Pure read.
    /// </summary>
    /// <param name="player">The route owner whose routes to validate.</param>
    /// <returns>All warnings across the player's routes, grouped by route (empty when all are valid).</returns>
    public IReadOnlyList<TradeRouteWarning> ValidateTradeRoutesOf(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        var warnings = new List<TradeRouteWarning>();
        foreach (TradeRoute route in player.TradeRoutes)
        {
            warnings.AddRange(ValidateTradeRoute(player, route));
        }
        return warnings;
    }

    /// <summary>The core validator: produces <paramref name="route"/>'s warnings as if owned by <paramref name="owner"/> (FreeCol <c>TradeRoute.verify()</c>; a null owner means every stop is invalid).</summary>
    private IReadOnlyList<TradeRouteWarning> ValidateTradeRoute(Player? owner, TradeRoute route)
    {
        var warnings = new List<TradeRouteWarning>();

        // FreeCol: a route needs at least two stops to move anything; verify() returns this immediately, so the
        // cargo checks below (which presuppose a ring of stops to deliver around) never run for a sub-2-stop route.
        // We short-circuit the same way — a 1-stop route reports only NotEnoughStops, not a spurious "always present".
        if (route.Stops.Count < 2)
        {
            warnings.Add(new TradeRouteWarning(route.Id, TradeRouteWarningKind.NotEnoughStops, null, null,
                "A trade route needs at least two stops to move goods."));
            return warnings;
        }

        // Walk the stops: flag any stop that is not one of the owner's colonies, track whether ANY stop loads goods,
        // and accumulate the set of goods present at EVERY stop (intersection) — those are never unloaded anywhere.
        bool anyCargo = false;
        HashSet<string>? alwaysPresent = null;
        for (int i = 0; i < route.Stops.Count; i++)
        {
            TradeRouteStop stop = route.Stops[i];
            // A Europe stop is always a valid location (the player always has a Europe); a colony stop must be one the
            // owner currently holds. (FreeCol TradeRouteStop.isValid: a Europe Location is valid, a Colony must be owned.)
            bool stopValid = owner is not null
                && (stop.IsEurope
                    || (_colonies.FirstOrDefault(c => c.Id == stop.ColonyId) is { } colony && colony.OwnerId == owner.PlayerId));
            if (!stopValid)
            {
                warnings.Add(new TradeRouteWarning(route.Id, TradeRouteWarningKind.InvalidStop, i, null,
                    $"Stop {i + 1} is not one of your colonies (or Europe)."));
            }

            if (stop.LoadGoodsIds.Count > 0)
            {
                anyCargo = true;
            }

            // Intersect across stops: start from the first stop's load list, then retain only goods every later stop
            // also loads (FreeCol seeds `always` from stop 0 and `retainAll`s each stop's cargo).
            if (alwaysPresent is null)
            {
                alwaysPresent = new HashSet<string>(stop.LoadGoodsIds);
            }
            else
            {
                alwaysPresent.IntersectWith(stop.LoadGoodsIds);
            }
        }

        // FreeCol: if no stop loads anything, the route hauls nothing.
        if (!anyCargo)
        {
            warnings.Add(new TradeRouteWarning(route.Id, TradeRouteWarningKind.AllEmpty, null, null,
                "No stop loads any goods, so this route would haul nothing."));
        }
        // FreeCol: a good loaded at every stop is never delivered anywhere — unless ENHANCED_TRADE_ROUTES relaxes it.
        // FreeCol names a single such good (`first(always)`); we report each so the player can fix them all.
        else if (!EnhancedTradeRoutes && alwaysPresent is { Count: > 0 })
        {
            foreach (string goodsId in alwaysPresent.OrderBy(g => g, StringComparer.Ordinal))
            {
                warnings.Add(new TradeRouteWarning(route.Id, TradeRouteWarningKind.GoodsAlwaysPresent, null, goodsId,
                    $"{ShortGoodsName(goodsId)} is loaded at every stop, so it is never delivered anywhere."));
            }
        }

        return warnings;
    }

    /// <summary>The short, human display name for a goods id (e.g. <c>model.goods.sugar</c> → <c>sugar</c>); the raw id if unknown.</summary>
    private string ShortGoodsName(string goodsId)
    {
        int dot = goodsId.LastIndexOf('.');
        return dot >= 0 && dot + 1 < goodsId.Length ? goodsId[(dot + 1)..] : goodsId;
    }

    /// <summary>
    /// Whether the player can sell <paramref name="amount"/> of a good from the docked <paramref name="ship"/>'s hold in
    /// Europe. The quoted cost is the <b>after-tax</b> proceeds (<see cref="Market.SaleValue"/>): the price slides down as
    /// the sale floods the market and the King withholds the player's tax per chunk, so the cost is what actually reaches
    /// the treasury — the SELL mirror of <see cref="CheckBuyEuropeGoods(Unit, string, int)"/> (and the cost-carries-the-value pattern of
    /// <see cref="CheckCashInTreasureTrain"/>). Used by the Europe screen to label and gate the Sell button (ADR-006).
    /// </summary>
    public MoveCheck CheckSellShipCargo(Unit ship, string goodsId, int amount) =>
        CheckSellShipCargo(_human, ship, goodsId, amount);

    /// <summary>Whether <paramref name="player"/> can sell <paramref name="amount"/> of a good from the docked <paramref name="ship"/>.</summary>
    internal MoveCheck CheckSellShipCargo(Player player, Unit ship, string goodsId, int amount)
    {
        if (ship.Location != UnitLocation.InEurope)
        {
            return MoveCheck.No("Goods are sold once the ship reaches Europe.");
        }
        if (!player.Market.IsTradeable(goodsId))
        {
            return MoveCheck.No($"{goodsId} cannot be sold in Europe.");
        }
        if (!player.Market.CanTrade(goodsId))
        {
            return MoveCheck.No($"{goodsId} is under boycott — pay the back taxes to lift it.");
        }
        if (ship.CargoOf(goodsId) < amount)
        {
            return MoveCheck.No($"The ship is not carrying {amount} {goodsId}.");
        }
        // The cost carries the after-tax proceeds (CheckCashInTreasureTrain pattern) so the UI can label the Sell button
        // with what actually banks, never moving the market to find out (Market.SaleValue is non-mutating).
        return MoveCheck.Yes(player.Market.SaleValue(goodsId, amount, player.TaxRate, MarketVolumeFactor(player)));
    }

    /// <summary>Sells goods from a docked ship's hold to the European market, crediting the treasury after tax.</summary>
    /// <returns>The gold credited after tax.</returns>
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckSellShipCargo(Unit, string, int)"/>.</exception>
    public int SellShipCargo(Unit ship, string goodsId, int amount) =>
        SellShipCargo(_human, ship, goodsId, amount);

    /// <summary>Sells a docked ship's cargo to <paramref name="player"/>'s European market (the ship's owner today).</summary>
    internal int SellShipCargo(Player player, Unit ship, string goodsId, int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);
        MoveCheck check = CheckSellShipCargo(player, ship, goodsId, amount);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }
        ship.AddCargo(goodsId, -amount);
        SaleResult sale = player.Market.Sell(goodsId, amount, player.TaxRate, MarketVolumeFactor(player));
        player.Gold += sale.GoldAfterTax;
        PropagateTradeToRivalMarkets(player, goodsId, amount); // FreeCol ServerPlayer.sellInEurope:1327
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
        PropagateTradeToRivalMarkets(player, goodsId, -amount); // FreeCol ServerPlayer.buyInEurope:1261 (sign negated)
        return cost;
    }

    /// <summary>
    /// The map's default high-seas entry tile — the first high-seas tile in row-major order (top-left).
    /// Used only as the last-resort fallback by <see cref="EuropeEntryTileFor"/> (a player with no colony
    /// and no on-map unit to anchor near). Falls back to (0,0) on maps with no high seas (test fixtures).
    /// </summary>
    private Position EuropeEntryTile() =>
        Map.AllPositions().FirstOrDefault(p => Map.TerrainAt(p).Id == HighSeasId, new Position(0, 0));

    /// <summary>
    /// The high-seas tile a ship bought, built-for, or delivered to <paramref name="player"/> in Europe should
    /// enter the New World at — the one nearest the player's territory, so a freshly-bought ship arrives beside
    /// the player's colonies rather than at the map's top-left default (FreeCol <c>Player.getEntryTile</c> /
    /// <c>Unit.getFullEntryLocation</c>: a unit with no recorded entry location uses its owner's entry tile near
    /// its start/colonies). The anchor is the player's first colony (lowest id) if it has one, otherwise its
    /// first on-map unit (e.g. the starting caravel/colonists), and the high-seas tile nearest that anchor is
    /// chosen by Chebyshev distance with a stable row/column tie-break. With no colony and no on-map unit to
    /// anchor near (or on a map with no high seas) it falls back to <see cref="EuropeEntryTile"/>. Deterministic
    /// and RNG-free (ADR-009); the fog is not consulted, so the entry tile is valid even where the player has
    /// not yet explored the sea by its colony.
    /// </summary>
    private Position EuropeEntryTileFor(Player player)
    {
        Position? anchor =
            ColoniesOf(player).OrderBy(c => c.Id).Select(c => (Position?)c.Position).FirstOrDefault()
            ?? _units.Where(u => u.IsOnMap && IsOwnedBy(u, player))
                .OrderBy(u => u.Id).Select(u => (Position?)u.Position).FirstOrDefault();
        if (anchor is not { } origin)
        {
            return EuropeEntryTile(); // no colony and no on-map unit to anchor near → the map default
        }
        return Map.AllPositions()
            .Where(p => Map.TerrainAt(p).Id == HighSeasId)
            .OrderBy(p => Chebyshev(p, origin)).ThenBy(p => p.Y).ThenBy(p => p.X)
            .FirstOrDefault(EuropeEntryTile()); // no high seas at all (test fixtures) → the map default
    }

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

    /// <summary>
    /// The human's current Europe price for a unit type id — what the player would pay to train (specialist) or buy
    /// (ship/artillery) it right now. This is the escalated override for an escalating type (artillery, after a prior
    /// purchase) or the ruleset base price otherwise. A price oracle for the Europe screen so it can label each
    /// trainable/purchasable type without itself knowing the escalation rule (ADR-006).
    /// </summary>
    public int EuropeUnitPrice(string unitTypeId) => EuropeUnitPrice(_human, Ruleset.Unit(unitTypeId));

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
    /// Buys a unit in Europe for gold; it appears docked there. A ship is given the high-seas entry tile
    /// nearest the player's territory (<see cref="EuropeEntryTileFor"/>) so that, when it sails to the New
    /// World, it arrives beside the player's colonies rather than at the map's top-left default; a land unit
    /// waits on the dock to board one.
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
        var unit = new Unit(_nextUnitId++, type, type.IsNaval ? EuropeEntryTileFor(player) : new Position(0, 0))
        {
            Location = UnitLocation.InEurope,
            OwnerId = player.PlayerId, // the bought unit belongs to its buyer (the human is 0; a foreign power its own id)
        };
        _units.Add(unit);
        return unit;
    }

    /// <summary>Whether the human can train the specialist <paramref name="unitTypeId"/> in Europe right now (flat price, gated on <see cref="UnitType.IsTrainedInEurope"/>).</summary>
    public MoveCheck CheckTrain(string unitTypeId) => CheckTrain(_human, unitTypeId);

    /// <summary>
    /// Trains a specialist (a priced, skill &gt; 0 unit — expert farmer, master carpenter…) in the human's Europe for a
    /// <b>flat</b> price; it docks there as a person, ready to board a ship. Unlike <see cref="BuyUnit(string)"/>'s
    /// artillery there is no per-purchase price escalation. The Europe screen routes specialists here and ships/artillery
    /// to <see cref="BuyUnit(string)"/> (86d3f6…), so the on-screen action matches the engine's train/purchase split.
    /// </summary>
    /// <returns>The trained specialist, docked in Europe.</returns>
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckTrain(string)"/>.</exception>
    public Unit TrainUnit(string unitTypeId) => TrainUnit(_human, unitTypeId);

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
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckTrain(Player, string)"/>.</exception>
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

    /// <summary>
    /// Hold slots a goods stack of <paramref name="amount"/> units occupies — each goods type packs into 100s, rounded
    /// up (FreeCol <c>GoodsContainer.CARGO_SIZE</c>). The public oracle behind the Europe screen's hold-slot view so the
    /// presentation reads the engine's per-stack slot rule rather than reimplementing the <c>(amount + 99) / 100</c>
    /// literal (ADR-006).
    /// </summary>
    public int GoodsSlotsFor(int amount) => SlotsFor(amount);

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
        NoteFirstLandfall(unit); // the classic Col1 first landing: a human colonist stepping ashore off the caravel triggers the one-shot name prompt
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
    /// The finite number of named cities in the "Seven Cities of Gold" — FreeCol ships seven Cibola city names
    /// (<c>nameCache.lostCityRumour.cityName.0..6</c>, consumed one per CIBOLA outcome by
    /// <c>NameCache.getNextCityOfCibola</c>). Once all seven are found, a further CIBOLA roll degrades to an ordinary
    /// RUINS find (the classic "you've found them all" fall-through).
    /// </summary>
    public const int CibolaCityCount = 7;

    /// <summary>
    /// How many of the Seven Cities of Gold are still undiscovered this game (starts at <see cref="CibolaCityCount"/>).
    /// While positive, a CIBOLA rumour spawns the big treasure train and decrements this; at zero a CIBOLA roll
    /// degrades to an ordinary ruins find. Persisted in the save (v63, omit-when-default — a game where none are found
    /// stays byte-identical and serialises nothing).
    /// </summary>
    internal int CitiesOfCibolaRemaining => _citiesOfCibolaRemaining;

    /// <summary>Restores the undiscovered-cities counter from a save (v63). No-op-equivalent for a default game (the field already starts full).</summary>
    internal void SetCitiesOfCibolaRemaining(int remaining) => _citiesOfCibolaRemaining = remaining;

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
                // The finite "Seven Cities of Gold": FreeCol draws the next named city (NameCache.getNextCityOfCibola)
                // and, if one remains, spawns the big treasure train (rand(0, dx·600) + dx·300 → medium dx=8 → 2400–7199)
                // and consumes that name; once all seven are found the city name is null and the outcome FALLS THROUGH to
                // an ordinary RUINS find (ServerUnit.csExploreLostCityRumour CIBOLA → "Fall through, found all the cities").
                if (_citiesOfCibolaRemaining > 0)
                {
                    int cibolaTreasure = random.Next(RumourDifficultyDx * 600) + (RumourDifficultyDx * 300);
                    _citiesOfCibolaRemaining--; // one of the Seven is now found — the count is per-game and persisted
                    SpawnTreasureTrain(target, unit.OwnerId, cibolaTreasure);
                    // Record the find for the History report (FreeCol CITY_OF_GOLD) when the human discovers it — score 0,
                    // its value rides the treasure (→ gold summand once cashed in). The history log is the human's only.
                    if (unit.OwnerId == _human.PlayerId)
                    {
                        RecordHistory(HistoryEventKind.CityOfGold, $"Found one of the Seven Cities of Gold — {cibolaTreasure} gold in treasure.");
                    }
                    break;
                }
                // All seven are found: degrade to a RUINS find, drawing the ruins amount exactly as the RUINS case does
                // (FreeCol's fall-through to the shared `case RUINS:` body). A small find pays gold; a larger one is a train.
                goto case LostCityRumourType.Ruins;
            case LostCityRumourType.BurialGround:
                ApplyBurialGround(target, unit.OwnerId); // the owning nation turns hateful toward the desecrator
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
    /// A desecrated burial ground (FreeCol <c>csNativeBurialGround</c>): the natives who own the tile turn hateful
    /// <b>toward the desecrating player</b> (<paramref name="playerId"/>). FreeCol sets the nation's tension to HATEFUL
    /// and forces war; we have no native-vs-colonial stance model, so we raise every settlement of the owning nation to
    /// maximum alarm toward that power — the nation-wide hostility analogue used by the other land-grievance acts (cf.
    /// <c>ClaimLandByStealing</c>). No gold or unit change.
    /// </summary>
    /// <param name="target">The desecrated tile (its native owner's settlements turn hateful).</param>
    /// <param name="playerId">The player who desecrated the ground.</param>
    private void ApplyBurialGround(Position target, int playerId)
    {
        if (Map.NativeOwnerOf(target) is not { } nation)
        {
            return; // gated upstream to native-owned tiles; guard is belt-and-braces
        }
        foreach (NativeSettlement settlement in _nativeSettlements.Where(s => s.NationTypeId == nation))
        {
            ChangeNativeAlarm(settlement, playerId, Ruleset.Difficulty.NativeTension.MaxAlarm); // clamps to max (hateful)
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
        // COASTAL gate (FreeCol Colony.getNoBuildReason COASTAL): a building carrying the coastalOnly ability may only
        // be raised in a sea-connected colony. Classic data declares the custom house's coastalOnly=false, so this
        // never fires by default; the customsOnCoast game option flips the effective value on (mirroring FreeCol
        // Specification.clean), and a variant may set the ability true directly in data — either way the gate is
        // data-driven. Buildings only (a unit never declares coastalOnly).
        if (!target.IsUnit && BuildingRequiresCoast(target.Id) && !IsColonyCoastal(colony))
        {
            return $"A {target.ShortName} can only be built in a coastal colony.";
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
    /// Whether the building with id <paramref name="buildingId"/> may only be built in a coastal colony — its
    /// <b>effective</b> <c>model.ability.coastalOnly</c> value (FreeCol <c>BuildingType.hasAbility(COASTAL_ONLY)</c>,
    /// which <c>Specification.clean</c> flips on when <c>model.option.customsOnCoast</c> is set). True when the
    /// building either declares the ability true directly in the ruleset (a variant may do so) OR declares it (at any
    /// value, as the classic custom house declares <c>coastalOnly=false</c>) while the <see cref="GameOptions.CustomsOnCoast"/>
    /// game option is on. Classic data declares the custom house's <c>coastalOnly=false</c> and the option defaults off,
    /// so this returns <c>false</c> for every classic building and the default game is unchanged (ADR-009).
    /// </summary>
    private bool BuildingRequiresCoast(string buildingId)
    {
        if (Ruleset.FindBuilding(buildingId) is not { } building)
        {
            return false;
        }
        if (building.HasAbility(CoastalOnlyAbility))
        {
            return true; // variant data set the ability true — coastal-only regardless of the option
        }
        // The classic custom house declares coastalOnly=false; the customsOnCoast option flips the effective value on.
        return Ruleset.GameOptions.CustomsOnCoast
            && building.Abilities is { } abilities && abilities.ContainsKey(CoastalOnlyAbility);
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
        _disasterNotices.Clear(); // and this turn's natural disasters striking human colonies (empty in classic — naturalDisasters default 0)
        _colonyStarvedNotices.Clear(); // and any human colonies starved out of existence this turn (empty in classic — the centre tile feeds the last colonist)
        _colonyFamineNotices.Clear(); // and any human colonies that lost a colonist (but survived) to famine this turn
        _warehouseOverflowNotices.Clear(); // and any human-colony goods wasted over warehouse capacity this turn
        _monarchDecreeNotices.Clear(); // and any immediate (no-choice) King's decrees this turn (empty before the monarch grace period)
        _refLandingNotices.Clear(); // and the one-off "the REF has landed" warning (LandRefUnits re-fills it on the first landing only)
        _firstContactNotices.Clear(); // and the human's first contacts with rival colonial powers this turn (FP-6a; DetectColonialContacts re-fills it)
        _stanceChangeNotices.Clear(); // and any turn-driven (tension-derived) stance shifts involving the human this turn (FP-6b; UpdateColonialStances re-fills it)
        _priceChangeNotices.Clear(); // and any Europe-market price moves from last turn (re-derived below by comparing the live prices to the baseline snapshot)
        _rumourNotices.Clear(); // and any rumour outcomes the human explored this turn (normally drained by the UI mid-turn; cleared here belt-and-braces)
        ClearPendingHumanProposals(); // and this round's AI alliance/cease-fire offers to the human (86d3drn4f; drained by the negotiation UI, cleared here belt-and-braces so the seam holds only the current round)
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
        ProcessAttrition();          // units left standing in the open wilderness waste away (FreeCol csNewTurn attrition) — RNG-free, dormant in classic except the Indian Convert
        foreach (Unit unit in _units)
        {
            if (unit.Orders == UnitOrders.Fortifying)
            {
                unit.Orders = UnitOrders.Fortified; // a turn spent digging in completes (FreeCol ages FORTIFYING → FORTIFIED)
            }
            // A ship still under repair stays pinned at 0 moves (FreeCol forced repair); everyone else resets.
            unit.MovementLeft = unit.IsUnderRepair ? 0 : InitialMovement(unit); // base + role bonus (dragoon/scout +9)
        }
        DetectMarketPriceChanges(); // compare each human-market good's ask price to the baseline (last turn's prices) and emit a notice for each that moved (FreeCol csFlushMarket); then re-baseline for next turn
        RecordYearlyDemographics(CurrentYear); // snapshot the human's population/gold/score for the year now ending (once per year; see Game.Demographics)
        Turn++;
        RemoveExpiredTemporaryModifiers(); // strip any duration-bounded modifier now out of date for the new turn (FreeCol's per-new-turn temporary-modifier removal) — a no-op in classic (registry empty)
    }

    /// <summary>
    /// Strips every registered temporary modifier that has expired for the turn just entered (FreeCol's per-new-turn
    /// removal of temporary modifiers — those <c>Modifier.isTemporary()</c> ones whose <c>lastTurn</c> has passed,
    /// e.g. <c>Player.removeOldTemporaryModifiers</c>). Runs once per <see cref="EndTurn"/>, immediately after the turn
    /// counter advances, so a modifier with <c>lastTurn = T</c> is active through turn T and removed on entering T+1.
    /// In the classic ruleset the registry is always empty (nothing registers a temporary modifier), so this is a
    /// no-op and the default game stays byte-identical (ADR-009).
    /// </summary>
    private void RemoveExpiredTemporaryModifiers() =>
        _temporaryModifiers.RemoveAll(m => m.IsOutOfDate(Turn));

    /// <summary>
    /// The classic Indian Convert's maximum attrition (classic spec <c>model.unit.indianConvert
    /// maximum-attrition="8"</c>): a convert that ends 9 consecutive turns standing in the open wilderness wastes
    /// away. It is the <b>only</b> classic unit type with a finite cap — every other type's maximum attrition is
    /// infinite (FreeCol <c>UnitType.INFINITY</c>), so no other classic unit ever accrues attrition.
    /// </summary>
    /// <remarks>
    /// This constant is a deliberate, documented stopgap: the faithful source of the cap is the unit type's
    /// <c>maximum-attrition</c> attribute (FreeCol <c>UnitType.getMaximumAttrition</c>), which our
    /// <see cref="UnitType"/> does not yet parse. The mechanic below is data-shaped — it routes through
    /// <see cref="MaxAttritionOf"/>, a single lookup point — so promoting it to a real parsed <c>UnitType</c>
    /// field (the proper variant-friendly fix, follow-up 86d3drmzp) is a one-line change there. See the doc.
    /// </remarks>
    private const int IndianConvertMaximumAttrition = 8;

    /// <summary>
    /// The maximum attrition a unit of this type may accumulate before it wastes away (FreeCol
    /// <c>UnitType.getMaximumAttrition</c>), or <see cref="int.MaxValue"/> (FreeCol <c>INFINITY</c>) when the type
    /// is not subject to attrition at all — in which case a unit of that type never accrues. In the classic ruleset
    /// only the Indian Convert has a finite cap (<see cref="IndianConvertMaximumAttrition"/>).
    /// </summary>
    private int MaxAttritionOf(UnitType type) =>
        type.Id == IndianConvertUnitTypeId ? IndianConvertMaximumAttrition : int.MaxValue;

    /// <summary>
    /// The shared world's per-turn <b>attrition</b> step (FreeCol <c>ServerUnit.csNewTurn</c>): a unit ending the
    /// turn on a settlement-less map tile, and whose type has a finite <see cref="MaxAttritionOf"/>, gains +1
    /// attrition; once its attrition <em>exceeds</em> that maximum the unit wastes away and is removed, its owner
    /// notified (<see cref="AttritionNotice"/>). A unit anywhere else — in a colony or native settlement, sailing,
    /// in Europe, or aboard a ship — has its attrition reset to 0 (FreeCol's <c>else setAttrition(0)</c>). RNG-free
    /// and self-contained; runs once per <see cref="EndTurn"/> after the per-player turns, in the world-advance
    /// phase. In the classic ruleset this is effectively dormant: only the Indian Convert is subject to attrition,
    /// so a typical game never destroys a unit here and the default game stays byte-identical (the field omits
    /// when 0).
    /// </summary>
    private void ProcessAttrition()
    {
        _attritionNotices.Clear(); // refresh each turn (transient UI scratch; never saved)
        // Snapshot: removing a wasted unit mutates _units mid-iteration.
        foreach (Unit unit in _units.ToList())
        {
            // "In the open" = on a map tile (not sailing/Europe), not aboard a ship, and no settlement on that tile.
            bool inTheOpen = unit.IsOnMap
                && ColonyAt(unit.Position) is null
                && NativeSettlementAt(unit.Position) is null;
            if (!inTheOpen || MaxAttritionOf(unit.Type) == int.MaxValue)
            {
                unit.Attrition = 0; // sheltered (or not subject to attrition) → the count resets / stays 0
                continue;
            }

            unit.Attrition++;
            if (unit.Attrition > MaxAttritionOf(unit.Type))
            {
                _attritionNotices.Add(new AttritionNotice(unit.OwnerId, unit.Type.Id, unit.Position));
                _units.Remove(unit); // the unit wastes away in the wilderness (FreeCol csRemove + model.unit.attrition message)
            }
        }
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

        RunYearlyMarketAdjust(player); // FreeCol ServerPlayer.csStartTurn:1813 → csYearlyGoodsAdjust — every European turn start

        ProcessImprovements(player); // advance any pioneers building tile improvements; land completed ones (no-op + RNG-free when none)

        ProcessGotos(player); // walk any units on a standing goto toward their destination (no-op when none — RNG-free)

        // Materialise: a colony that starves out its last colonist is disposed mid-turn (RunColonyTurn → DisposeColony
        // removes it from _colonies), so we cannot enumerate the lazy ColoniesOf view while it mutates.
        foreach (Colony colony in ColoniesOf(player).ToList())
        {
            RunColonyTurn(player, colony);
        }
        AccumulateLibertyAndElectFathers(player);
        ApplyFreeBuildings(player); // La Salle: a free stockade in each colony that has reached the required population
        AccumulateImmigrationAndEmigrate(player);
        PayBuildingUpkeep(player); // deduct Σ building upkeep from gold; flag bankruptcy if unpayable — no-op in classic (enableUpkeep default off)
        RollNaturalDisasters(player); // per-turn colony disaster roll on a reserved stream — no-op in classic (naturalDisasters default 0)
        ProcessTradeRoutes(player); // auto-haul any carriers on a trade route (no-op + no RNG when none — stream-0-safe)

        if (!player.IsHuman)
        {
            MaybeDeclareIndependence(player); // 86d3e49jp: a dominant AI colonial power that out-strengthens the amassed REF rebels (RNG-free gate; no-op in the default game) — flips to Rebel, spawns the REF; it then runs the path below and DEFENDS this same turn
            RunForeignPowerEconomy(player); // FP-5: pursue a father, sell surplus, recruit (own stream/market)
            RunForeignPowerTurn(player);     // FP-4: move / explore / found (a rebel at war with the REF — not the human — falls through to the FP-5 garrison/arming defence)
        }
    }

    /// <summary>
    /// Deducts each colony's per-turn gold upkeep — Σ over its buildings of <see cref="BuildingType.Upkeep"/> — from
    /// the player's treasury (FreeCol <c>Colony.getUpkeep</c> summed by <c>ServerPlayer.csPayUpkeep</c>), and tracks
    /// <b>bankruptcy</b> (86d3c9ux4). Gated on the ruleset's <see cref="Ruleset.UpkeepEnabled"/> game option (classic
    /// <c>model.option.enableUpkeep</c> defaults off), so the classic economy charges nothing, never goes bankrupt,
    /// and stays byte-identical — everything here runs only when a ruleset turns upkeep on. RNG-free.
    /// <para>
    /// Faithful to <c>csPayUpkeep</c>: if the player can afford the whole bill it pays in full and any prior
    /// bankruptcy is <b>lifted</b> (<see cref="Player.Bankrupt"/> cleared); if it cannot, its gold is drained to 0 and
    /// it goes <b>bankrupt</b> — which penalises every colony's building production (the FreeCol
    /// <c>model.disaster.bankruptcy</c> effect: −50% to building-produced goods, applied in
    /// <see cref="RunBuildingProduction"/>) until it can pay again. Because production runs before upkeep within a
    /// turn, a bankruptcy declared this turn bites next turn's production — matching FreeCol, where the bankruptcy
    /// modifier persists across turns until cleared.
    /// </para>
    /// <para>
    /// The bankruptcy flag is transient (recomputed here each turn, never persisted), so this adds no save field and
    /// the save version is unchanged.
    /// </para>
    /// </summary>
    private void PayBuildingUpkeep(Player player)
    {
        if (!Ruleset.UpkeepEnabled)
        {
            return; // classic default: no upkeep, no bankruptcy, default game byte-identical
        }
        int upkeep = ColoniesOf(player)
            .Sum(colony => colony.Buildings.Sum(buildingId => Ruleset.Building(buildingId).Upkeep));
        if (player.Gold >= upkeep)
        {
            player.Gold -= upkeep;       // afford the whole bill (a 0 bill is trivially affordable)
            player.Bankrupt = false;     // solvent → lift any standing bankruptcy (FreeCol setBankrupt(false))
        }
        else
        {
            player.Gold = 0;             // can't pay → treasury drained (FreeCol modifyGold(-getGold()))
            player.Bankrupt = true;      // production-penalty disaster strikes until payable again (FreeCol setBankrupt(true))
        }
    }

    /// <summary>
    /// Rolls the per-turn natural-disaster check for one colonial player (FreeCol <c>ServerPlayer.csNaturalDisasters</c>,
    /// 86d3c9uu8). With probability <see cref="Ruleset.NaturalDisasterPercentage"/>% a disaster strikes one of the
    /// player's colonies; the colony is picked at random, a natural disaster is chosen, and its effects are applied
    /// (loss of money / loss of goods; production-penalty effects are noted but not applied — see below).
    /// <para>
    /// <b>Default-off &amp; byte-identical.</b> The whole method is skipped when the percentage is 0 — the classic
    /// default (<c>model.option.naturalDisasters</c> defaults 0) — so the classic game rolls nothing. When it does
    /// roll, every draw is taken from a dedicated <see cref="DisasterStreamId"/> generator seeded off the player's
    /// own RNG state (the human's from the saved stream-0 <em>state</em>, read without advancing it; an AI power's
    /// from its own stream): the human's economy stream 0 is never advanced, so the human's seeded game stays
    /// byte-stable (ADR-006/ADR-009). The seed mixes in the turn and player id so successive turns and different
    /// players roll independently.
    /// </para>
    /// <para>
    /// <b>Faithful subset (documented in docs/systems/colonies.md).</b> Two simplifications keep this within the
    /// no-save-bump rule and our current model: (1) We do not yet map disasters to specific terrain/tiles, so the
    /// colony's disaster pool is all <see cref="Ruleset.NaturalDisasters"/> (uniform), rather than only the disasters
    /// its worked tiles allow (FreeCol <c>Colony.getDisasterChoices</c>). (2) The <c>lossOfTileProduction</c>/
    /// <c>lossOfBuildingProduction</c> effects are <em>timed</em> modifiers (−50% for 3 turns); applying them would
    /// need persisted per-colony timed-modifier state (a save bump), so we record that the effect fired but do not
    /// apply the multi-turn penalty. The immediate effects — loss of money, loss of goods — are applied in full. The
    /// loss-of-unit / loss-of-building / damaged-ship effects are likewise not yet modelled (no parse, no apply).
    /// </para>
    /// </summary>
    private void RollNaturalDisasters(Player player)
    {
        int probability = Ruleset.NaturalDisasterPercentage;
        if (probability <= 0)
        {
            return; // classic default: no disasters, default game byte-identical (no draw on any stream)
        }
        List<Colony> colonies = ColoniesOf(player).ToList();
        IReadOnlyList<Disaster> pool = Ruleset.NaturalDisasters;
        if (colonies.Count == 0 || pool.Count == 0)
        {
            return;
        }

        // A dedicated disaster generator seeded off this player's own RNG state — never advancing the human's stream 0
        // (we read its saved state word read-only). The turn and player id mix in so rolls don't repeat across turns.
        ulong baseState = RandomFor(player).SaveState().State;
        var rng = new Pcg32Random(baseState ^ ((ulong)Turn << 1) ^ ((ulong)player.PlayerId << 32), DisasterStreamId);

        if (rng.Next(100) >= probability)
        {
            return; // no disaster this turn
        }

        // Pick a starting colony, then walk colonies until one takes an effect (FreeCol wraps around the list).
        int start = rng.Next(colonies.Count);
        for (int i = 0; i < colonies.Count; i++)
        {
            Colony colony = colonies[(start + i) % colonies.Count];
            Disaster disaster = pool[rng.Next(pool.Count)];
            if (ApplyDisaster(player, colony, disaster, rng))
            {
                return; // one colony struck per turn (FreeCol returns after the first colony that takes an effect)
            }
        }
    }

    /// <summary>
    /// Applies a disaster's effects to one colony per its <see cref="DisasterEffects"/> policy (FreeCol
    /// <c>csApplyDisaster</c>), recording a <see cref="DisasterNotice"/> when the owner is the human. Returns true if
    /// any effect fired (so the caller stops walking colonies). See <see cref="RollNaturalDisasters"/> for which
    /// effects are applied vs. noted.
    /// </summary>
    private bool ApplyDisaster(Player player, Colony colony, Disaster disaster, IGameRandom rng)
    {
        List<DisasterEffect> firing = SelectDisasterEffects(disaster, rng);
        if (firing.Count == 0)
        {
            return false;
        }

        int goldLost = 0;
        string? goodsLostId = null;
        int goodsLost = 0;
        bool productionPenalty = false;
        foreach (DisasterEffect effect in firing)
        {
            switch (effect.Kind)
            {
                case DisasterEffectKind.LossOfMoney:
                    // FreeCol: plunder max(1, colony plunder value / 5), capped at the owner's purse.
                    int plunder = Math.Min(Math.Max(1, ColonyPlunderAmount(colony, player, rng) / 5), player.Gold);
                    if (plunder > 0)
                    {
                        player.Gold -= plunder;
                        goldLost += plunder;
                    }
                    break;
                case DisasterEffectKind.LossOfGoods:
                    // FreeCol: halve a random stored stack, capped at 50 lost.
                    var loot = PillageableGoods(colony).ToList();
                    if (loot.Count > 0)
                    {
                        KeyValuePair<string, int> stack = loot[rng.Next(loot.Count)];
                        int lost = Math.Min(stack.Value / 2, PillageGoodsCap);
                        if (lost > 0)
                        {
                            colony.AddGoods(stack.Key, -lost);
                            goodsLostId = stack.Key;
                            goodsLost += lost;
                        }
                    }
                    break;
                case DisasterEffectKind.ProductionPenalty:
                    // FreeCol csApplyDisaster: for each modifier the effect carries, attach a TIMED modifier (a −50%
                    // percentage on that good for `duration` turns) to the STRUCK COLONY at DISASTER_PRODUCTION_INDEX
                    // (ServerPlayer.java:1690-1695; specification.xml:833-889). We register a colony-scoped
                    // TemporaryModifier per modifier so the penalty damps only this colony's production (folded into
                    // tile yield / building output while in window, stripped when it expires). Only fires when the
                    // naturalDisasters option is > 0 (classic 0 → this branch is never reached), so classic is
                    // byte-identical (ADR-009); the registry is transient, so no save bump.
                    foreach (DisasterModifier mod in effect.Modifiers)
                    {
                        if (mod.Duration <= 0)
                        {
                            continue; // a permanent penalty is a different (bankruptcy) mechanism, handled via Player.Bankrupt
                        }
                        var payload = new FatherModifier(mod.GoodsId, mod.Type, mod.Value, DisasterProductionIndex);
                        // FreeCol Modifier.makeTimedModifier sets lastTurn = start + duration (inclusive), i.e. a window
                        // of duration+1 turns; MakeTimed's window is [start, start+duration-1], so pass mod.Duration + 1
                        // to match FreeCol exactly ([Turn, Turn+duration]). The strike turn's production has already run
                        // (disasters resolve after RunColonyTurn), so a spec duration of 3 penalises the next 3 cycles.
                        RegisterTemporaryModifier(TemporaryModifier.MakeTimed(payload, mod.Duration + 1, Turn, colony.Id));
                        productionPenalty = true;
                    }
                    break;
            }
        }

        if (goldLost == 0 && goodsLost == 0 && !productionPenalty)
        {
            return false; // every effect was a no-op on this colony — try the next (FreeCol returns empty messages)
        }
        if (player.IsHuman)
        {
            _disasterNotices.Add(new DisasterNotice(
                disaster.Id, colony.Name, colony.Position, goldLost, goodsLostId, goodsLost, productionPenalty));
        }
        return true;
    }

    /// <summary>
    /// Selects which of a disaster's effects fire (FreeCol <c>csApplyDisaster</c> ONE/SEVERAL/ALL): for
    /// <see cref="DisasterEffects.One"/> a single weighted-random effect; for <see cref="DisasterEffects.Several"/>
    /// each effect rolled independently against its probability; for <see cref="DisasterEffects.All"/> all of them.
    /// </summary>
    private static List<DisasterEffect> SelectDisasterEffects(Disaster disaster, IGameRandom rng)
    {
        IReadOnlyList<DisasterEffect> effects = disaster.Effects;
        if (effects.Count == 0)
        {
            return [];
        }
        switch (disaster.NumberOfEffects)
        {
            case DisasterEffects.All:
                return effects.ToList();
            case DisasterEffects.Several:
                return effects.Where(e => rng.Next(100) < e.Probability).ToList();
            default: // One: weighted-random over the effect probabilities (FreeCol RandomChoice.getWeightedRandom).
                int totalWeight = effects.Sum(e => Math.Max(0, e.Probability));
                if (totalWeight <= 0)
                {
                    return [effects[rng.Next(effects.Count)]]; // all-zero weights → uniform pick (defensive)
                }
                int pick = rng.Next(totalWeight);
                foreach (DisasterEffect e in effects)
                {
                    pick -= Math.Max(0, e.Probability);
                    if (pick < 0)
                    {
                        return [e];
                    }
                }
                return [effects[^1]];
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
        // Bank toward a father so accrued liberty is eventually spent. The pick is value-weighted, not random
        // (86d3e49ej): FreeCol's EuropeanAIPlayer.selectFoundingFather chooses the offered father most worth having
        // this age — no RNG draw is taken here (the offer SET is still seeded in GenerateOffers; only this SELECT
        // is deterministic now). See SelectFoundingFatherFor.
        if (power.CurrentFather is null && power.OfferedFathers.Count > 0)
        {
            power.CurrentFather = SelectFoundingFatherFor(power);
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
                int reserve = goodsId == ToolsGoodsId ? toolsReserve : MilitaryReserveFor(power, colony, goodsId);
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

    /// <summary>Gold a foreign power keeps in reserve before it splurges on Europe units — so a poor power keeps
    /// recruiting/building rather than draining its treasury (a documented AI budget floor, not a FreeCol constant;
    /// difficulty-scoped via <see cref="DifficultyOptions.Ai"/>, classic value 1500 — see <see cref="AiTuning"/>).</summary>
    private int AiEuropeSpendFloor => Ruleset.Difficulty.Ai.EuropeSpendFloor;

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
    /// is the power's own gold via the owner-scoped <see cref="TrainUnit(Player, string)"/>/<see cref="BuyUnit(Player, string)"/> seams.
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

        // 3) Proactive proposals beyond suing for peace (86d3drn4f): offer ALLIANCE to a strong, friendly power that
        // shares an enemy, and CEASE_FIRE on a stalemated war. AI-to-AI offers settle through NegotiateTrade on the
        // proposer's own RNG stream; an offer to the human is queued in PendingHumanProposals for the negotiation UI
        // (never auto-applied) — so the human's stream 0 stays byte-identical (ADR-009).
        ProposeProactiveTreaties(power);
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
            && StanceFromTension(power.Stances[_human.PlayerId], power.Tensions.GetValueOrDefault(_human.PlayerId)) == Stance.War
            // Benjamin Franklin's peaceTreaty modifier (FreeCol EuropeanAIPlayer.peaceHolds): a power that WOULD break
            // the peace from grievance first rolls against the peace-hold probability — a Franklin human's treaty holds
            // (war averted) with probability = his modifier fraction (+50% → 0.5). Drawn on the POWER'S OWN stream
            // (never the human's stream 0), and skipped entirely (no draw) for a non-Franklin human — so the default
            // game declares war exactly as before and stays byte-identical (ADR-009).
            && !PeaceTreatyHolds(power, _human))
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

        // AI logistics — transport missions (86d3c9vq9, FreeCol TransportMission/WishRealizationMission): drive the
        // power's carrier ships to ferry its Europe colonists/experts to the colony that most wants a worker (the
        // worker-wish realisation). Run BEFORE the per-unit loop so a carrier on a transport job holds its
        // position there (it spent its movement here) instead of being marched off to explore, and so a freshly-landed
        // colonist is integrated this same turn. Skipped for an armed carrier at war with the human (it hunts below).
        // Every draw is the power's OWN stream (StepToward); the board/sail/join seams are RNG-free — human stream 0 is
        // byte-identical (ADR-009).
        RunForeignPowerTransport(power, atWarWithHuman);

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
            // native settlement or from a Lost City Rumour — heads to the nearest owned colony that is a CONNECTED PORT
            // (coastal, so the King's ship can reach it — the cash-in rule, 86d3fpy96) and banks its gold there, instead
            // of sitting idle forever. Routing to a coastal colony (not just the nearest, which could be land-locked and
            // never cashable) keeps the treasure from getting stuck. The cash-in is RNG-free; the step draws the power's
            // OWN stream (never stream 0). Guarded on owning a loaded treasure train, so a power without one is unaffected.
            if (unit.Type.CarryTreasure && unit.TreasureAmount > 0)
            {
                if (CheckCashInTreasureTrain(unit).Allowed)
                {
                    CashInTreasureTrain(unit); // standing at an owned connected-port colony → bank the net gold to the power
                }
                else if (NearestColonyOf(power, unit.Position, Map.Width + Map.Height, IsColonyCoastal) is { } bank
                    && StepToward(power, unit, bank.Position) is { } toBank)
                {
                    MoveUnit(unit, toBank); // escort it toward the nearest owned coastal (connected-port) colony
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
            // A guard that has reached/holds its post **digs in** (FreeCol DefendSettlementMission ends in Unit.fortify):
            // calling Fortify (RNG-free) when CheckFortify allows it ages FORTIFYING → FORTIFIED next turn for the +50%
            // dig-in bonus, instead of standing un-entrenched. (86d3e49cm)
            if (!unit.Type.IsNaval && OffenceBase(unit) > 0)
            {
                if (ColonyAt(unit.Position) is { } here && here.OwnerId == power.PlayerId)
                {
                    GarrisonFortify(unit); // already standing guard in an own colony → dig in (no-op if already fortified)
                    continue;
                }
                bool willFound = unit.Type.CanFoundColony
                    && ColoniesOf(power).Count() < Ruleset.Difficulty.Ai.MaxColonies && CheckFoundColony(unit).Allowed;
                if (!willFound && NearestUndefendedOwnColony(power, unit) is { } garrisonTile
                    && StepToward(power, unit, garrisonTile) is { } toGarrison)
                {
                    MoveUnit(unit, toGarrison);
                    // If that step landed the unit on the undefended own colony it was marching to, dig in this turn
                    // rather than waiting a full turn un-entrenched (the move already spent its remaining movement).
                    if (ColonyAt(unit.Position) is { } reached && reached.OwnerId == power.PlayerId)
                    {
                        GarrisonFortify(unit);
                    }
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
            // AI defence — arm a colonist (86d3e49cm, FreeCol EuropeanAIPlayer.giveNormalMissions arming + buyDragoon's
            // ARMED+MOUNTED preference): an idle plain-role colonist (HasDefaultRole, OffenceBase 0) standing in an
            // UNDER-DEFENDED own colony whose stock can cover it is armed to the strongest affordable military role,
            // preferring model.role.dragoon (50 muskets + 50 horses) over model.role.soldier (50 muskets). Gated on the
            // colony lacking a military land defender (ColonyHasMilitaryDefender) so it arms toward defence rather than
            // stripping every worker — and placed BEFORE the found-colony branch so a defenceless colony entrenches a
            // guard rather than marching the colonist off to found another undefended colony (expansion yields to
            // defence when the colony is badly defended, mirroring FreeCol's early badlyDefended pass). EquipRole
            // consumes the colony's OWN muskets/horses and is RNG-free; the role pick is by ordinal preference — no RNG
            // draw, so human stream 0 stays byte-identical (ADR-009). The armed colonist digs in via the FP-5 garrison
            // branch on its following turn. Faithful SUBSET: this is a single under-defended check, not FreeCol's full
            // badlyDefended + ColonyPlan military scheduler (no role/strength target, no multi-unit garrison plan).
            if (unit.HasDefaultRole && OffenceBase(unit) <= 0
                && ColonyAt(unit.Position) is { } armColony && armColony.OwnerId == power.PlayerId
                && !ColonyHasMilitaryDefender(armColony)
                && BestAffordableMilitaryRole(unit, armColony) is { } armRole)
            {
                EquipRole(unit, armColony, armRole);
                continue;
            }
            // A tooled pioneer never founds (it would destroy its 20 tools); it improves, or — out of plans — explores.
            if (!IsPioneer(unit) && ColoniesOf(power).Count() < Ruleset.Difficulty.Ai.MaxColonies && CheckFoundColony(unit).Allowed)
            {
                // National-advantage site ranking (86d3drn5d, FreeCol getColonyValue × getAIAdvantage tilt): for an
                // EXPANSION colony (the power already owns at least one), rather than always settling the exact tile it
                // stands on, the founder compares its current site against each legal adjacent site it could step to
                // (ScoreColonySite) and, if a neighbour scores STRICTLY higher for this power's advantage, hill-climbs one
                // step toward it to found from the better tile — so a trade power drifts toward a coastal cash site and a
                // production power toward fertile land. Because terrain is fixed and only a STRICTLY-better neighbour
                // lures it, the walk climbs to a local maximum in a few steps and then founds (no neighbour beats a local
                // max → no thrash, guaranteed termination). The power's FIRST colony is NEVER deferred (the gate below):
                // it settles its landing immediately, so a power always gets a foothold on turn one. Scoring + the step
                // draw the power's OWN stream (StepToward); the human plans no sites here → stream 0 byte-identical (ADR-009).
                if (ColoniesOf(power).Any()
                    && BetterAdjacentColonySite(power, unit) is { } betterSite
                    && StepToward(power, unit, betterSite) is { } toSite)
                {
                    MoveUnit(unit, toSite);
                    continue;
                }
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

    // ===== AI logistics — transport missions + worker/goods wishes (86d3c9vq9) ================================
    // A faithful subset of FreeCol's TransportMission / WishRealizationMission / Wish: the foreign AI ferries its
    // colonists by sea so a recruited/trained/bought colonist no longer rots on the Europe dock — it sails over and is
    // landed into the colony that most wants a worker. Deferred vs FreeCol: no multi-stop cargo planner (Cargo's
    // 4-waypoint LOAD/PICKUP/UNLOAD/DROPOFF lifecycle, DESTINATION_UPPER_BOUND=4 route optimisation, the 3-strike DUMP)
    // — each carrier serves one leg at a time; goods are NOT hauled (the economy already auto-sells colony surplus —
    // shipping it would double-sell, see CollectBySea); a "wish" is the lightweight need-score below (WorkerWishValue),
    // not a persisted posted/assigned Wish object. Enough to make the worker-transport + treasure-cash-in behaviours
    // visible without a save-format change.

    /// <summary>The most colonists a foreign carrier loads from its Europe dock in one trip — the carrier's hold (one slot per colonist), so it never over-boards. A documented logistics bound, not a FreeCol constant.</summary>
    private int AiTransportLoad(Unit carrier) => CargoSlotsFree(carrier);

    /// <summary>
    /// Drives <paramref name="power"/>'s carrier ships for the turn — the AI transport mission (86d3c9vq9, FreeCol
    /// <c>TransportMission.doTransport</c> + <c>WishRealizationMission</c>), a one-leg-at-a-time subset:
    /// <list type="number">
    /// <item><b>In Europe with passengers</b> → sail to the New World (deliver the load).</item>
    /// <item><b>In Europe, empty</b> → board own dock colonists (experts first — the colonies' worker wish), capped at
    /// the hold, then sail; with nothing to carry it waits on the dock.</item>
    /// <item><b>On the map carrying passengers</b> → make for the colony that most wants a worker
    /// (<see cref="BestWorkerWishColony"/>) and, on arrival beside it, land each passenger straight into the colony
    /// (<see cref="JoinColony"/> — the wish realisation). With no own colony it holds.</item>
    /// <item><b>On the map, empty</b> → if colonists wait in Europe, make for the high seas and sail to collect them;
    /// with nothing to fetch it is left for the per-unit explore fallback. (Goods are not hauled — the AI economy
    /// already auto-sells a colony's surplus directly; see <see cref="CollectBySea"/>.)</item>
    /// </list>
    /// Each carrier spends its movement here (so the per-unit loop's explore fallback can't also march it); a carrier
    /// it deliberately holds in place keeps its movement (it has no transport job, so exploring is fine). Skipped for an
    /// <b>armed</b> carrier while <paramref name="atWarWithHuman"/> — that ship hunts in the per-unit loop instead
    /// (FreeCol: a transport with no cargo may turn pirate). All movement draws the power's OWN stream via
    /// <see cref="StepToward"/>; boarding, sailing, disembarking and joining are RNG-free — so the human's stream 0
    /// stays byte-identical (ADR-009).
    /// </summary>
    /// <param name="power">The foreign power taking its transport turn.</param>
    /// <param name="atWarWithHuman">Whether the power is at war with the human (an armed carrier then hunts, not ferries).</param>
    private void RunForeignPowerTransport(Player power, bool atWarWithHuman)
    {
        foreach (Unit carrier in _units
            .Where(u => IsOwnedBy(u, power) && u.Type.IsNaval && u.Type.IsCarrier && !u.IsUnderRepair)
            .OrderBy(u => u.Id).ToList())
        {
            if (atWarWithHuman && OffenceBase(carrier) > 0)
            {
                continue; // an armed warship hunts the human in the per-unit loop; don't divert it to ferrying
            }

            if (carrier.Location == UnitLocation.InEurope)
            {
                TransportFromEurope(power, carrier);
                continue;
            }
            if (carrier.Location is UnitLocation.SailingToEurope or UnitLocation.SailingToNewWorld)
            {
                continue; // in transit — AdvanceSailing lands it; nothing to decide this turn
            }
            if (!carrier.IsOnMap)
            {
                continue; // any other off-map state (defensive — carriers are InEurope / sailing / on the map)
            }

            if (Passengers(carrier).Any())
            {
                DeliverPassengers(power, carrier);
            }
            else
            {
                CollectBySea(power, carrier);
            }
        }
    }

    /// <summary>
    /// A carrier docked in Europe (FreeCol <c>TransportMission</c> collect-then-deliver in Europe): if it already
    /// carries passengers it sails for the New World; otherwise it boards own dock colonists — <b>experts first</b>
    /// (the colonies' standing worker wish, FreeCol <c>WorkerWish</c> prefers the wanted expert) up to its free hold,
    /// then sails if it loaded anyone. With no one to carry it waits on the dock. RNG-free.
    /// </summary>
    private void TransportFromEurope(Player power, Unit carrier)
    {
        if (Passengers(carrier).Any())
        {
            SailToNewWorld(carrier); // a loaded ship heads over (it may have boarded a partial load on an earlier turn)
            return;
        }
        // Only worth a crossing if the power has somewhere to put the colonists (an own colony to deliver to).
        if (!ColoniesOf(power).Any())
        {
            return;
        }
        // Board own dock colonists, experts first (an expert satisfies a stronger worker wish), then by id for stability.
        List<Unit> dock = _units
            .Where(u => u.Location == UnitLocation.InEurope && u.Type.IsPerson && !u.IsAboard && IsOwnedBy(u, power))
            .OrderByDescending(u => u.Type.ExpertProduction is not null).ThenBy(u => u.Id)
            .ToList();
        int boarded = 0;
        foreach (Unit colonist in dock)
        {
            if (boarded >= AiTransportLoad(carrier) || !CheckBoard(colonist, carrier).Allowed)
            {
                break;
            }
            Board(colonist, carrier);
            boarded++;
        }
        if (boarded > 0)
        {
            SailToNewWorld(carrier); // sail with the load (it re-enters at its departure high-seas tile)
        }
    }

    /// <summary>
    /// A loaded carrier on the map (FreeCol <c>TransportMission</c> delivery + <c>WishRealizationMission</c>): it makes
    /// for the colony that most wants a worker (<see cref="BestWorkerWishColony"/>) and, when it is beside that colony,
    /// puts each passenger ashore onto the colony's tile (<see cref="Disembark"/>) and joins it into the colony
    /// (<see cref="JoinColony"/> — the colonist enters the population and is put to work, which is the wish
    /// realisation). When no own colony exists it holds (keeps its passengers aboard). The step draws the power's OWN
    /// stream; disembark and join are RNG-free.
    /// </summary>
    private void DeliverPassengers(Player power, Unit carrier)
    {
        if (BestWorkerWishColony(power) is not { } colony)
        {
            return; // nowhere to deliver yet (no own colony) — hold the load
        }
        if (carrier.Position.IsAdjacentTo(colony.Position) || carrier.Position == colony.Position)
        {
            // Put each passenger ashore onto the colony's land tile, then join it into the colony. Snapshot first —
            // Disembark + JoinColony mutate the unit and the global list mid-iteration.
            foreach (Unit passenger in Passengers(carrier).OrderBy(u => u.Id).ToList())
            {
                if (!passenger.Type.IsPerson || !CheckDisembark(passenger, colony.Position).Allowed)
                {
                    continue; // only a person joins a colony; a non-person (none today) stays aboard
                }
                Disembark(passenger, colony.Position); // ashore on the colony tile (ends its move)
                if (CheckJoinColony(passenger, colony).Allowed)
                {
                    JoinColony(passenger, colony); // realise the worker wish — the colonist is put to work
                }
            }
            return;
        }
        if (StepToward(power, carrier, colony.Position) is { } step)
        {
            MoveUnit(carrier, step); // close on the wishing colony (passengers ride along via SyncPassengers)
        }
    }

    /// <summary>
    /// An empty carrier on the map (FreeCol <c>TransportMission</c> collection): if the power has colonists waiting in
    /// its Europe, make for the high seas and sail to fetch them (reaching a high-seas tile sails it across; otherwise
    /// step toward the nearest known one). With no one to fetch the carrier is left untouched, so the per-unit explore
    /// fallback may send it scouting. Movement draws the power's OWN stream; sailing is RNG-free.
    /// <para>
    /// Deferred (documented vs FreeCol): an empty carrier does <b>not</b> haul a colony's surplus to Europe — our AI
    /// economy already sells a colony's surplus directly to its market each turn (<see cref="RunForeignPowerEconomy"/>,
    /// the abstracted "auto-sell" model), so there is no leftover stack for a ship to carry. FreeCol ships the goods;
    /// shipping them here would double-sell. So this slice covers the worker leg (the real gap — un-shipped Europe
    /// colonists) and leaves goods transport to the direct-sell economy.
    /// </para>
    /// </summary>
    private void CollectBySea(Player power, Unit carrier)
    {
        if (OwnPersonsInEurope(power) == 0)
        {
            return; // no colonists waiting in Europe — leave the empty ship for the explore fallback
        }
        if (CheckSailToEurope(carrier).Allowed)
        {
            SailToEurope(carrier); // standing on the high seas → cross now to fetch the waiting colonists
        }
        else if (NearestHighSeasTile(power, carrier.Position) is { } highSeas
            && StepToward(power, carrier, highSeas) is { } toSea)
        {
            MoveUnit(carrier, toSea); // make for the map edge to embark for Europe
        }
    }

    /// <summary>
    /// The own colony that most wants another worker (the AI's standing <b>worker wish</b>, FreeCol <c>WorkerWish</c>):
    /// the highest <see cref="WorkerWishValue"/> among the power's colonies, ties broken by id. Null when the power
    /// owns no colony. A coastal colony is preferred (a ship can deliver to it) but an inland colony still counts — a
    /// passenger lands beside it and joins. Used as the delivery target for a loaded carrier.
    /// </summary>
    private Colony? BestWorkerWishColony(Player power) =>
        ColoniesOf(power)
            .OrderByDescending(c => WorkerWishValue(c)).ThenBy(c => c.Id)
            .FirstOrDefault();

    /// <summary>
    /// How much <paramref name="colony"/> wants another worker (FreeCol <c>WorkerWish.value</c>, simplified): the count
    /// of unstaffed work positions it could fill — its idle colonists plus the empty building work-slots its current
    /// population can't fill yet. A bigger number means a stronger pull for a delivered colonist. RNG-free, derived
    /// from current colony state (no posted/persisted wish object — the deferred subset).
    /// </summary>
    private int WorkerWishValue(Colony colony) =>
        colony.IdleColonists + FreeBuildingWorkSlots(colony);

    /// <summary>The number of empty production-building work-slots in <paramref name="colony"/> (each building's <see cref="BuildingType.Workplaces"/> capacity minus its seated worker count, summed over its work buildings) — the room it has for more workers.</summary>
    private int FreeBuildingWorkSlots(Colony colony) =>
        colony.Buildings
            .Select(Ruleset.Building)
            .Where(b => b.Workplaces > 0)
            .Sum(b => Math.Max(0, b.Workplaces - colony.BuildingWorkers.GetValueOrDefault(b.Id)));

    /// <summary>The nearest high-seas tile to <paramref name="origin"/> the <paramref name="power"/> has explored (ties by position), or null when it knows of none — the embarkation point for a crossing to Europe.</summary>
    private Position? NearestHighSeasTile(Player power, Position origin) =>
        Map.AllPositions()
            .Where(p => Map.TerrainAt(p).Id == HighSeasId && power.Explored.Contains(p))
            .OrderBy(p => Chebyshev(p, origin)).ThenBy(p => p.Y).ThenBy(p => p.X)
            .Select(p => (Position?)p)
            .FirstOrDefault();

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

    /// <summary>The infantry military role a foreign power arms an idle colonist into for defence (50 muskets) — FreeCol <c>model.role.soldier</c>.</summary>
    private const string SoldierRoleId = "model.role.soldier";

    /// <summary>The mounted military role a foreign power prefers when its colony also stocks horses (50 muskets + 50 horses) — FreeCol <c>model.role.dragoon</c>, the ARMED+MOUNTED role <c>buyDragoon</c> favours.</summary>
    private const string DragoonRoleId = "model.role.dragoon";

    /// <summary>
    /// The strongest military role <paramref name="unit"/> can be armed into from <paramref name="colony"/>'s own stock
    /// right now (86d3e49cm, FreeCol <c>EuropeanAIPlayer.buyDragoon</c> preferring an ARMED+MOUNTED role over a plain
    /// armed one): <see cref="DragoonRoleId"/> (muskets + horses) when the colony can equip it, else <see cref="SoldierRoleId"/>
    /// (muskets) when it can, else <c>null</c> when neither is affordable. Affordability is the existing
    /// <see cref="CheckEquipRole"/> guard, which checks the colony store covers the role's required goods — so the choice
    /// is purely by ordinal preference and colony stock, drawing <b>no</b> RNG (ADR-009).
    /// </summary>
    private string? BestAffordableMilitaryRole(Unit unit, Colony colony) =>
        CheckEquipRole(unit, colony, DragoonRoleId).Allowed ? DragoonRoleId
        : CheckEquipRole(unit, colony, SoldierRoleId).Allowed ? SoldierRoleId
        : null;

    /// <summary>
    /// How much of <paramref name="goodsId"/> <paramref name="colony"/> keeps back from the surplus sell for the AI
    /// defence-arming step (86d3e49cm) — the equipment-reserve counterpart to the pioneer tools reserve, so the
    /// economy does not cash out the very muskets/horses the arm step would spend a turn later (FreeCol's AI reserves
    /// equipment for the military roles it plans to fill). Non-zero only when the colony is <b>under-defended</b>
    /// (no military land defender) and holds an idle plain-role colonist that could be armed: it then reserves the
    /// <em>strongest affordable</em> military role's required amount of this good — 50 muskets for the soldier, plus
    /// 50 horses when the colony also stocks enough horses to mount a dragoon (the role the arm step prefers). Returns
    /// 0 (sell everything) otherwise. RNG-free read.
    /// </summary>
    private int MilitaryReserveFor(Player power, Colony colony, string goodsId)
    {
        if ((goodsId != MusketsId && goodsId != HorsesId) || ColonyHasMilitaryDefender(colony))
        {
            return AiTradeReserve; // not military equipment, or already defended → sell the surplus as usual
        }
        Unit? armable = _units.FirstOrDefault(u => IsOwnedBy(u, power) && u.IsOnMap && u.HasDefaultRole
            && OffenceBase(u) <= 0 && u.Position == colony.Position);
        if (armable is null)
        {
            return AiTradeReserve; // no idle colonist here to arm → nothing to reserve for
        }
        // Reserve the strongest role the colony could currently equip (dragoon when both goods are stocked, else
        // soldier) — the same preference the arm step applies — so its required goods survive the sell.
        string? role = BestAffordableMilitaryRole(armable, colony);
        if (role is null)
        {
            return AiTradeReserve;
        }
        return Ruleset.Role(role).RequiredGoods
            .Where(g => g.GoodsId == goodsId)
            .Sum(g => g.Amount);
    }

    /// <summary>
    /// Orders <paramref name="unit"/> to fortify (dig in for the +50% defence bonus) when <see cref="CheckFortify"/>
    /// allows it (86d3e49cm, the FreeCol <c>DefendSettlementMission</c> garrison ending in <c>Unit.fortify</c>): a no-op
    /// when the unit is already fortifying/fortified, so a guard standing post does not re-issue the order each turn.
    /// <see cref="Fortify"/> is RNG-free, so this never touches the human's stream 0 (ADR-009).
    /// </summary>
    private void GarrisonFortify(Unit unit)
    {
        if (CheckFortify(unit).Allowed)
        {
            Fortify(unit);
        }
    }

    // --- AI colony-site planning — national-advantage ranking (86d3drn5d, FreeCol EuropeanAIPlayer.getColonyValue
    //     weighted by the nation's advantage; ColonyPlan's getAIAdvantage ×1.2 production tilt) ----------------------

    /// <summary>The national-advantage site multiplier a favoured tile/site gets (FreeCol <c>ColonyPlan</c> production tilt: <c>×1.2</c> on the advantage's preferred goods/tiles).</summary>
    private const double ColonySiteAdvantageFactor = 1.2;

    /// <summary>A tile counts as "good production" of a good when its best single-good potential reaches this (FreeCol <c>Player.getColonyValue</c> <c>GOOD_PRODUCTION = 4</c>) — a production-advantage power gives such a neighbour the extra ×1.2 site tilt.</summary>
    private const int GoodProductionYield = 4;

    /// <summary>The advantage short name of <paramref name="power"/> (FreeCol <c>AIPlayer.getAIAdvantage</c>: the part after <c>model.nationType.</c> — <c>trade</c>/<c>conquest</c>/<c>cooperation</c>/<c>immigration</c>), or the empty string for a power with no nation (the no-advantage default). Read straight off the resolved nation type.</summary>
    private string AdvantageOf(Player power) =>
        power.NationId is { } nationId && Ruleset.EuropeanNations.FirstOrDefault(n => n.Id == nationId) is { } nation
            ? nation.NationType.ShortName
            : string.Empty;

    /// <summary>The European sale value of one unit of <paramref name="goods"/> for colony-site scoring: its own market price, or — for a good with no market of its own (grain/fish, which store as food) — the price of the good it stores as (food = 1). 0 for a good that trades nowhere and stores as nothing priced.</summary>
    private int GoodsUnitValue(GoodsType goods) =>
        goods.Market?.InitialPrice ?? Ruleset.Goods(goods.StoredAs).Market?.InitialPrice ?? 0;

    /// <summary>
    /// How a foreign power values founding a colony on <paramref name="tile"/> — a faithful subset of FreeCol's
    /// <c>Player.getColonyValue</c> tilted by the power's national advantage (FreeCol <c>ColonyPlan</c>'s
    /// <c>getAIAdvantage</c> ×1.2 production tilt). It sums, over the centre tile and its eight land neighbours, the
    /// potential output of every tradeable good the tile could yield. <b>What that output is worth depends on the
    /// power's advantage</b> — which is exactly how a trade power and a production power rank the same land differently:
    /// <list type="bullet">
    /// <item><b>No nation / neutral advantage</b> (<c>cooperation</c>/<c>immigration</c>, or no nation at all) — each
    /// good is valued at its <b>European sale price</b> (yield × price): a cash-crop neighbourhood out-scores barren
    /// ground. This is the un-tilted base; the human, who plans no sites here, is never affected.</item>
    /// <item><b><c>trade</c></b> (Dutch) — the same price-weighted base, but tradeable value is taken at
    /// <see cref="ColonySiteAdvantageFactor"/> (×1.2) and a <b>coastal</b> site (any water neighbour — a port a ship can
    /// reach) earns another ×1.2. A trade power therefore prizes coastal, high-value cash sites above inland ones.</item>
    /// <item><b><c>conquest</c></b> (Spanish — the production archetype) — value is the <b>raw yield amount</b> itself,
    /// <em>ignoring</em> market price, with a tile's contribution taken at ×1.2 when its best single-good yield is among
    /// the higher ones (the <c>GoodProductionYield</c> band). A production power therefore prizes high-output land —
    /// ore/grain volume — even when that output sells cheaply, so it ranks a thin-but-pricey site below a fertile one.</item>
    /// </list>
    /// Pure read of terrain + market seed (no RNG, no mutation), so it never touches the human's stream 0 (ADR-009).
    /// Deferred vs FreeCol (documented): no high-seas-distance / settlement-spacing / food-floor categories — this is the
    /// advantage-ranking subset, layered on the existing legality (<see cref="CheckFoundColony"/>) and spacing checks.
    /// </summary>
    /// <param name="power">The foreign power evaluating the site (its advantage drives the weighting).</param>
    /// <param name="tile">The candidate centre tile a colony would be founded on.</param>
    /// <returns>The site's value to this power (a non-negative score; higher is better). Not comparable across advantages — each advantage scores on its own scale; only same-advantage comparisons rank sites.</returns>
    internal double ScoreColonySite(Player power, Position tile)
    {
        string advantage = AdvantageOf(power);
        bool tradeAdvantage = advantage == "trade";
        bool productionAdvantage = advantage == "conquest";

        double score = 0.0;
        foreach (Position t in tile.Neighbours().Append(tile))
        {
            if (!Map.InBounds(t) || Map.TerrainAt(t).IsWater)
            {
                continue; // value is drawn from the workable land tiles of the colony's footprint
            }

            int bestSingleYield = 0;
            double tileValue = 0.0;
            foreach (GoodsType goods in Ruleset.GoodsTypes)
            {
                int yield = TileYieldPotential(t, goods.Id);
                if (yield <= 0)
                {
                    continue;
                }
                bestSingleYield = Math.Max(bestSingleYield, yield);
                // A production power weighs RAW YIELD (it wants output volume); every other advantage weighs the good's
                // European sale PRICE (it wants cash). This single switch is what makes ore land win for conquest and
                // cash-crop land win for trade — the core of the advantage ranking. Food-class goods (grain/fish) have
                // no market of their own, so their cash value comes from the good they store as (food, price 1) via
                // GoodsUnitValue — a thin but real cash contribution that still counts the colony's breadbasket tiles.
                tileValue += productionAdvantage ? yield : yield * GoodsUnitValue(goods);
            }

            if (tradeAdvantage)
            {
                // Trade advantage: count this tile's tradeable value at ×1.2 (a trade power prizes a cash-crop neighbourhood).
                tileValue *= ColonySiteAdvantageFactor;
            }
            else if (productionAdvantage && bestSingleYield >= GoodProductionYield)
            {
                // Production advantage: a high-output neighbour earns the ×1.2 tilt on ITS OWN value (per tile, so the
                // result is order-independent) — a production power prizes fertile/high-ore land.
                tileValue *= ColonySiteAdvantageFactor;
            }

            score += tileValue;
        }

        // Trade advantage also prizes a coastal site (a port a ship can reach) — ×1.2 when any neighbour is water.
        if (tradeAdvantage && tile.Neighbours().Any(n => Map.InBounds(n) && Map.TerrainAt(n).IsWater))
        {
            score *= ColonySiteAdvantageFactor;
        }

        return score;
    }

    /// <summary>
    /// Whether a foreign power could legally found a colony on <paramref name="tile"/> if a founder stood there
    /// (FreeCol <c>Player.canClaimToFoundSettlementReason</c>): in-bounds settleable land, empty of a colony, and not
    /// adjacent to an existing colony (the universal spacing rule, mirroring <see cref="CheckFoundColony"/> but keyed on a
    /// position rather than a unit). Used to enumerate the candidate sites the founder could step to and rank by
    /// <see cref="ScoreColonySite"/>. Pure read — no RNG, no mutation.
    /// </summary>
    private bool IsLegalColonySite(Position tile) =>
        Map.InBounds(tile)
        && Map.TerrainAt(tile).CanSettle
        && ColonyAt(tile) is null
        && !tile.Neighbours().Any(n => Map.InBounds(n) && ColonyAt(n) is not null);

    /// <summary>
    /// The legal adjacent colony site <paramref name="unit"/> could step to that scores <b>strictly higher</b> for
    /// <paramref name="power"/>'s national advantage than the unit's current tile (<see cref="ScoreColonySite"/>), or
    /// null when its own tile is the best nearby (the common case — found in place). Candidates are the unit's eight
    /// neighbours that are a legal site (<see cref="IsLegalColonySite"/>) AND a legal move (so it can actually reach
    /// them); ties break deterministically by score then position, so the choice is replay-stable. A strict improvement
    /// is required, so an equally-good neighbour never lures the founder off its tile (no thrash). Pure ranking — no
    /// mutation; the only stream draw happens later in <see cref="StepToward"/> (the power's own stream, never stream 0).
    /// </summary>
    private Position? BetterAdjacentColonySite(Player power, Unit unit)
    {
        double here = ScoreColonySite(power, unit.Position);
        Position? best = null;
        double bestScore = here;
        foreach (Position n in unit.Position.Neighbours()
                     .Where(n => IsLegalColonySite(n) && CheckMove(unit, n).Allowed)
                     .OrderBy(n => n.Y).ThenBy(n => n.X))
        {
            double score = ScoreColonySite(power, n);
            if (score > bestScore) // strict: an equal neighbour never lures the founder off its current tile
            {
                bestScore = score;
                best = n;
            }
        }
        return best;
    }

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
    internal const int NativeDemandMin = 30;

    /// <summary>Maximum goods a demand asks for — one cargo load (FreeCol <c>GoodsContainer.CARGO_SIZE</c>).</summary>
    internal const int NativeDemandMax = 100;

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
        // Each settlement produces one turn of goods into its store (FreeCol ServerIndianSettlement.csNewTurn) and the
        // tribe-wide tension cools a turn (FreeCol the native player's Tension decay) — both on the nation's own stream /
        // RNG-free, so the human's stream 0 is untouched (86d3fpzvy/86d3fpzkq; logic in Game.Natives.cs).
        ProduceNativeNationGoods(player);
        if (player.NationId is { } nationId)
        {
            DecayTribeTensionForNation(nationId);
        }

        // Re-derive the nation-level WAR/CEASE_FIRE/PEACE stance toward each colonial power from the (now-decayed)
        // tribe tension (FreeCol NativeAIPlayer.determineStances → Stance.getStanceFromTension) — the uprising signal
        // when alarm peaks (86d3fpzqf). TRANSIENT + RNG-free: recomputed each turn, never written to the saved Stances
        // dict, so it adds no save field and a default game (natives never reach Hateful) stays byte-identical. Braves
        // already attack at the per-settlement Displeased threshold below — this is additive nation-level signalling.
        DetermineNativeStances(player);

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
            bool hostile = home is not null && AlarmLevelOf(home) >= RaidAlarmThreshold;
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
            else if (home is not null && PickRivalRaidTarget(player, brave, home) is { } rivalTile)
            {
                // Not raiding the human (calm toward the human, or no human target) but the home camp is alarmed at a
                // rival European power (incl. a rival AI) — FreeCol braves raid ANY sufficiently-disliked European, not
                // just one foe. Seek-and-destroy that rival's nearest field unit on the nation's own stream (86d3fpzu3).
                if (brave.Position.IsAdjacentTo(rivalTile) && CheckAttack(brave, rivalTile).Allowed)
                {
                    RaidForeignUnit(player, brave, rivalTile);
                }
                else if (StepToward(player, brave, rivalTile) is { } rivalStep)
                {
                    MoveUnit(brave, rivalStep); // close on the rival; a hemmed-in brave waits
                }
            }
            else if (!hostile && TryBringGiftFromStore(player, brave))
            {
                // a friendly tribe left a store-backed gift at an adjacent human colony — the brave's turn is spent (86d3fpzx1)
            }
            else if (Wander(player, brave) is { } wanderStep)
            {
                MoveUnit(brave, wanderStep);
            }
        }
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
                    .Where(s => AlarmLevelOf(s) < RaidAlarmThreshold && s.StockOf(good) >= 2 * BraveEquipGoods)
                    .OrderByDescending(s => s.StockOf(good)).ThenBy(s => s.Position.Y).ThenBy(s => s.Position.X)
                    .FirstOrDefault();
                NativeSettlement? recipient = nationSettlements
                    .Where(s => AlarmLevelOf(s) >= RaidAlarmThreshold && s.StockOf(good) < BraveEquipGoods)
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
    internal void EquipBravesAtThreatenedSettlements(Player nation)
    {
        foreach (NativeSettlement settlement in _nativeSettlements
            .Where(s => s.NationTypeId == nation.NationId && AlarmLevelOf(s) >= RaidAlarmThreshold)
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
        AlarmLevel level = HomeSettlement(nation, brave) is { } home ? AlarmLevelOf(home) : AlarmLevel.Hateful;
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
        AlarmLevel level = HomeSettlement(nation, brave) is { } home ? AlarmLevelOf(home) : AlarmLevel.Hateful;
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

    // ───────────────────────── tribute demands by offensive units (86d3drn3b) ─────────────────────────

    /// <summary>Turns that must pass before a native settlement can be shaken down for tribute again (FreeCol <c>InGameController.demandTribute</c> <c>TURNS_PER_TRIBUTE</c>).</summary>
    public const int TributeCooldownTurns = 5;

    /// <summary>The per-demand gold cap (FreeCol caps <c>demandTribute</c> gold at 100 regardless of the rolled range).</summary>
    private const int TributeGoldCap = 100;

    /// <summary>
    /// The outcome of a <see cref="DemandTribute(Unit, Position)"/> attempt (FreeCol <c>scoutSettlement.tributeAgree/tributeDisagree</c>):
    /// <see cref="Paid"/> says whether the settlement yielded, and <see cref="Gold"/> is the tribute extracted (0 on a
    /// refusal). A blocking, single-shot result the presentation reports as "they paid N gold" or "they refused".
    /// </summary>
    /// <param name="Paid">True when the settlement yielded tribute (gold &gt; 0); false when it refused (too angry, on cooldown, or it has no gift range).</param>
    /// <param name="Gold">The gold extracted (0 on a refusal).</param>
    public readonly record struct TributeResult(bool Paid, int Gold);

    /// <summary>
    /// Whether <paramref name="unit"/> may demand tribute from the native settlement on <paramref name="target"/> now
    /// (FreeCol <c>DemandTributeMessage</c> validation): the unit must be on the map, a <b>colonial</b> (non-native) unit
    /// with offensive strength (our faithful subset of FreeCol's "armed or has the <c>demandTribute</c> ability" — we
    /// gate on offence, the same test <see cref="CheckAttackSettlement"/> uses, since the unarmed-scout demand ability
    /// is not yet modelled), with movement left, standing on or next to a native settlement. A read oracle (ADR-006) —
    /// no RNG, no mutation; the presentation enables the "Demand tribute" action from it.
    /// </summary>
    public MoveCheck CheckDemandTribute(Unit unit, Position target)
    {
        if (!unit.IsOnMap)
        {
            return MoveCheck.No("The unit is at sea or in Europe.");
        }
        if (unit.IsNative)
        {
            return MoveCheck.No("Native units do not demand tribute of settlements."); // braves use the native-demand path
        }
        if (!Map.InBounds(target))
        {
            return MoveCheck.No("Target is off the map.");
        }
        if (!unit.Position.IsAdjacentTo(target) && unit.Position != target)
        {
            return MoveCheck.No("Move next to the settlement to demand tribute.");
        }
        if (unit.MovementLeft <= 0)
        {
            return MoveCheck.No("No movement left this turn.");
        }
        if (OffenceBase(unit) <= 0)
        {
            return MoveCheck.No($"A {unit.Type.ShortName} has no offensive strength — arm it first.");
        }
        if (NativeSettlementAt(target) is null)
        {
            return MoveCheck.No("There is no native settlement to demand tribute of there.");
        }
        return MoveCheck.Yes(0);
    }

    /// <summary>
    /// The tribute a settlement would yield to a demand right now (FreeCol <c>InGameController.demandTribute</c>'s
    /// accept/refuse rule), as a pure function of its <see cref="NativeSettlement.AlarmLevel"/>, its <c>gifts</c> range,
    /// the cooldown, and one RNG draw from <paramref name="random"/>:
    /// <list type="bullet">
    /// <item><b>Happy/Content</b> → <c>giftsRoll / 10</c>, capped at <see cref="TributeGoldCap"/> (100).</item>
    /// <item><b>Displeased</b> → <c>giftsRoll / 20</c>, capped at 100.</item>
    /// <item><b>Angry/Hateful</b> → 0 (refuses outright — no RNG drawn).</item>
    /// </list>
    /// Returns 0 (a refusal) when on cooldown (<c>lastTribute + 5 ≥ year</c>) or the settlement type has no gift range —
    /// matching FreeCol's "no tribute" branches. <c>internal</c> so the band rule can be unit-tested directly (ADR-006).
    /// </summary>
    internal int EvaluateTributeDemand(NativeSettlement settlement, int playerId, IGameRandom random)
    {
        // FreeCol gates on the actual game year (lastTribute + 5 < year); a settlement never demanded of has a
        // lastTribute far in the past and always passes. We track the turn number (1-based), so a fresh settlement
        // (LastTribute 0 — never demanded) is always demandable, and a stamped one waits out the 5-turn cooldown.
        if (settlement.LastTribute > 0 && settlement.LastTribute + TributeCooldownTurns >= Turn)
        {
            return 0; // recently demanded of — nothing this time (FreeCol cooldown), no RNG drawn
        }
        // The settlement weighs the demand by its alarm TOWARD THE DEMANDER (a tribe friendly to this power pays more).
        int divisor = AlarmLevelOf(settlement, playerId) switch
        {
            AlarmLevel.Happy or AlarmLevel.Content => 10,
            AlarmLevel.Displeased => 20,
            _ => 0, // Angry / Hateful → refuse
        };
        if (divisor == 0)
        {
            return 0; // too alarmed — refuses (no RNG drawn, matching FreeCol's switch default)
        }
        SettlementType type = Ruleset.Settlement(settlement.SettlementTypeId);
        return Math.Min(GiftsAmount(type, random) / divisor, TributeGoldCap);
    }

    /// <summary>
    /// Demands tribute from the native settlement on <paramref name="target"/> under threat (86d3drn3b, FreeCol
    /// <c>InGameController.demandTribute</c> — the <see cref="Diplomacy.TradeContext.Tribute"/> negotiation an offensive
    /// unit can make <em>instead of</em> attacking). The settlement evaluates the demand by its alarm band
    /// (<see cref="EvaluateTributeDemand"/>): a calm one pays gold, an angry one refuses. <b>Either way</b> the demand is
    /// an insult — the settlement's alarm rises by <see cref="Specification.NativeTensionOptions.AddNormal"/> (200) and its tribute
    /// cooldown is stamped — and the unit's turn ends. Paid gold is minted to the demander (we model no native treasury,
    /// as the scout-beads/plunder paths do). The amount draws from the <b>demander's own RNG stream</b>
    /// (<see cref="RandomFor"/>), so a foreign power demanding never perturbs the human's stream 0 (ADR-009).
    /// </summary>
    /// <returns>Whether tribute was paid and how much (0 on a refusal).</returns>
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckDemandTribute"/>.</exception>
    public TributeResult DemandTribute(Unit unit, Position target) =>
        DemandTribute(unit, target, RandomFor(PlayerById(unit.OwnerId) ?? _human));

    /// <summary>The tribute-demand resolution drawing from an explicit RNG (tests inject a fixed RNG to force the rolled amount, as for <see cref="Attack(Unit, Position, IGameRandom)"/>).</summary>
    internal TributeResult DemandTribute(Unit unit, Position target, IGameRandom random)
    {
        MoveCheck check = CheckDemandTribute(unit, target);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }

        NativeSettlement settlement = NativeSettlementAt(target)!;
        int gold = EvaluateTributeDemand(settlement, unit.OwnerId, random);
        if (gold > 0)
        {
            (PlayerById(unit.OwnerId) ?? _human).Gold += gold; // minted to the demander (no native treasury modelled)
        }
        // Demanding is always an insult to the demander, whether or not they paid (FreeCol stamps alarm + lastTribute either way).
        ChangeNativeAlarm(settlement, unit.OwnerId, Ruleset.Difficulty.NativeTension.AddNormal);
        settlement.LastTribute = Turn;
        unit.MovementLeft = 0; // the demand ends the unit's turn
        return new TributeResult(gold > 0, gold);
    }

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

    /// <summary>The offensive seek-and-destroy range ladder (FreeCol's 8/12/16): the first gate yielding any eligible
    /// target wins. Difficulty-scoped via <see cref="DifficultyOptions.Ai"/> (classic 8/12/16 — see <see cref="AiTuning"/>).</summary>
    private IReadOnlyList<int> SeekRangeLadder => Ruleset.Difficulty.Ai.SeekRangeLadder;

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

    /// <summary>
    /// The nearest colony of <paramref name="owner"/> within <paramref name="maxDistance"/> (Chebyshev) of
    /// <paramref name="origin"/>, optionally restricted to colonies matching <paramref name="predicate"/>, or null. Used
    /// to muster a mission convert (any colony) and to route a treasure train to a <b>connected port</b> (coastal colony
    /// only — see the AI treasure logistics). The owner may be any colonial player.
    /// </summary>
    private Colony? NearestColonyOf(Player owner, Position origin, int maxDistance, Func<Colony, bool>? predicate = null) =>
        ColoniesOf(owner)
            .Where(c => Chebyshev(c.Position, origin) <= maxDistance && (predicate is null || predicate(c)))
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
    /// The tile of a <b>rival European</b> field unit an alarmed <paramref name="brave"/> raids (FreeCol
    /// <c>NativeAIPlayer.secureIndianSettlement</c> — a settlement dispatches braves at <em>any</em> European whose
    /// tension at that camp is above CONTENT, i.e. Displeased+, not just the human): among the colonial powers <b>other
    /// than the human</b> whose alarm at <paramref name="home"/> is ≥ <see cref="RaidAlarmThreshold"/>, the best-scored
    /// land unit-tile by the same <see cref="ScoreUnitTarget"/> value−distance heuristic the human raid uses, searched
    /// over the 8/12/16 Chebyshev range ladder, then any-distance nearest as a fallback. The human is excluded here —
    /// the human is handled by the earlier <see cref="PickRaidTarget"/> branch on its own (channel-0) alarm, so this is
    /// strictly the <em>rival-power</em> extension (and a no-op in a solo game, where no rival colonial power exists, so
    /// the human's stream 0 stays byte-stable). Pure (no RNG); only ever called on the native's own turn.
    /// </summary>
    private Position? PickRivalRaidTarget(Player nation, Unit brave, NativeSettlement home)
    {
        // The colonial powers (NOT the human) this camp is alarmed enough at to raid. Empty in a solo game.
        var rivals = _players
            .Where(p => p.PlayerType == PlayerType.Colonial && p.PlayerId != HumanAlarmChannel
                        && AlarmLevelOf(home, p.PlayerId) >= RaidAlarmThreshold)
            .Select(p => p.PlayerId)
            .ToHashSet();
        if (rivals.Count == 0)
        {
            return null; // no disliked rival power → nothing to raid (solo game falls out here, RNG untouched)
        }

        foreach (int range in SeekRangeLadder)
        {
            Position? best = null;
            int bestScore = int.MinValue;
            foreach (Position tile in _units
                         .Where(u => u.IsOnMap && !u.IsNative && rivals.Contains(u.OwnerId) && !u.Type.IsNaval
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
        // Out of seek range → close on the rivals' nearest land unit at any distance (mirrors PickRaidTarget's fallback).
        return _units
            .Where(u => u.IsOnMap && !u.IsNative && rivals.Contains(u.OwnerId) && u.Type.IsNaval == brave.Type.IsNaval)
            .OrderBy(u => Chebyshev(u.Position, brave.Position))
            .ThenBy(u => u.Position.Y).ThenBy(u => u.Position.X)
            .Select(u => (Position?)u.Position)
            .FirstOrDefault();
    }

    /// <summary>
    /// Resolves a brave's raid on the <b>rival European</b> unit at <paramref name="target"/> through the nation's OWN
    /// RNG stream (never stream 0), like <see cref="RaidHumanUnit"/> but against a non-human colonial power. No
    /// <see cref="CombatNotice"/> is recorded — those are the <em>human</em>'s victim log (combat the human suffered),
    /// and this fight is between a native nation and a rival AI, which the human is not party to (the foreign-power AI's
    /// <see cref="AttackHumanUnit"/> likewise records a notice only for human victims). The defender is non-native (a
    /// colonial rival, filtered upstream), so the native-alarm path in
    /// <see cref="Attack(Unit, Position, Randomness.IGameRandom)"/> is skipped — a raid never raises the raider's own
    /// nation's alarm. Drawing only on the nation's stream keeps the human's stream 0 byte-stable (ADR-009).
    /// </summary>
    private void RaidForeignUnit(Player nation, Unit brave, Position target) =>
        Attack(brave, target, RandomFor(nation)); // INTERNAL overload → the nation's stream; no human-facing notice

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
    /// Converts each colony's freshly-produced bells into player liberty, then — unless the player has declared
    /// independence (a <see cref="PlayerType.Rebel"/>/<see cref="PlayerType.Independent"/> player, under the classic
    /// <see cref="GameOptions.ContinueFoundingFatherRecruitment"/> = false) — elects the chosen father once enough is
    /// banked and refreshes the offered set. Faithful to classic Colonization, the Continental Congress closes at the
    /// Declaration: a rebel's bells still update Sons of Liberty (the bake stays ungated) and already-elected fathers
    /// keep aiding the war, but no NEW father is recruited. With the option on, recruitment continues post-declaration.
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

        // Classic Colonization closes the Continental Congress at the Declaration of Independence: a Rebel/Independent
        // player elects no NEW fathers and is offered none (the bells→Liberty bake above stays ungated so Sons of
        // Liberty keep updating, and already-elected fathers keep their effects via player.Congress). FreeCol
        // ServerPlayer.canRecruitFoundingFather: COLONIAL recruits freely; REBEL/INDEPENDENT only under
        // model.option.continueFoundingFatherRecruitment (classic default false). Skipping the block for a HUMAN rebel
        // also removes that turn's RandomFor(player) offers draw — but only on the Rebel/Independent path; the COLONIAL
        // path's draw sequence (the only one the default soak exercises) is byte-identical (ADR-009).
        if (player.PlayerType is not (PlayerType.Rebel or PlayerType.Independent)
            || Ruleset.GameOptions.ContinueFoundingFatherRecruitment)
        {
            ElectAndRefreshFounders(player);
        }
    }

    /// <summary>
    /// Elects the chosen father once enough liberty is banked and refreshes the offered set — the recruitment half of
    /// <see cref="AccumulateLibertyAndElectFathers"/>, gated out for a rebel under the classic ruleset (see that method
    /// and <see cref="GameOptions.ContinueFoundingFatherRecruitment"/>).
    /// </summary>
    private void ElectAndRefreshFounders(Player player)
    {
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
                // Francisco de Coronado (model.event.seeAllColonies): every colony + a wide ring revealed. FreeCol reveals
                // at father.apply(colony.getLineOfSight(), …, EXPOSED_TILES_RADIUS) — the colony's visible-radius widened by
                // Coronado's own exposedTilesRadius modifier (classic additive +3). Classic 2 + 3 = 5 → an 11×11 block. See [fog-of-war].
                int revealRadius = CoronadoRevealRadius(Ruleset.Father(elected));
                foreach (Colony c in _colonies)
                {
                    RevealAround(player, c.Position, revealRadius);
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

    /// <summary>The ability granting a colony the custom house (FreeCol <c>Ability.BUILD_CUSTOM_HOUSE</c>); among fathers only Peter Stuyvesant carries it.</summary>
    private const string BuildCustomHouseAbility = "model.ability.buildCustomHouse";

    /// <summary>
    /// Picks which of a foreign power's currently-offered fathers it banks toward — a value-weighted, RNG-free choice
    /// faithful to FreeCol's <c>EuropeanAIPlayer.selectFoundingFather</c> (<c>86d3e49ej</c>). Two rules, in order:
    /// <list type="number">
    /// <item><b>Custom-house override</b>: if any offered father grants <see cref="BuildCustomHouseAbility"/> (Peter
    /// Stuyvesant), pick it outright — FreeCol always grabs the custom house, since building it early eases the AI's
    /// shipping/transport burden.</item>
    /// <item><b>Highest age weight</b>: otherwise the offered father with the greatest
    /// <see cref="FoundingFather.WeightForAge"/> for the <see cref="CurrentAge"/> — the same weight that biased the offer
    /// draw, reused here to value the candidates. Ties (equal weight, or two custom-house grantors) break deterministically
    /// by ordinal father-id order, so the choice is fully reproducible without consuming the power's RNG stream.</item>
    /// </list>
    /// Assumes <paramref name="power"/> has at least one offered father (the caller guards the empty case).
    /// </summary>
    /// <param name="power">The foreign power choosing a father from its own <see cref="Player.OfferedFathers"/>.</param>
    /// <returns>The id of the offered father to pursue.</returns>
    private string SelectFoundingFatherFor(Player power)
    {
        int age = CurrentAge;
        string? customHouse = power.OfferedFathers
            .Where(id => Ruleset.Father(id).Abilities.Any(a => a.Id == BuildCustomHouseAbility && a.Value))
            .OrderBy(id => id, StringComparer.Ordinal)
            .FirstOrDefault();
        if (customHouse is not null)
        {
            return customHouse; // FreeCol: a custom-house grantor is chosen outright, ahead of any weight.
        }

        return power.OfferedFathers
            .OrderByDescending(id => Ruleset.Father(id).WeightForAge(age))
            .ThenBy(id => id, StringComparer.Ordinal) // deterministic tie-break — no RNG (ADR-009)
            .First();
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

    /// <summary>
    /// The additive radius Coronado adds on top of a colony's line of sight when his see-all-colonies reveal fires
    /// (FreeCol <c>Modifier.EXPOSED_TILES_RADIUS</c>, classic +3): <c>ColonySightRadius (2) + 3 = 5</c>, an 11×11 block.
    /// Among fathers only Francisco de Coronado carries it; default (no modifier) leaves the reveal at the colony's own
    /// sight radius.
    /// </summary>
    private const string ExposedTilesRadiusModifierId = "model.modifier.exposedTilesRadius";

    /// <summary>The movement-point modifier (additive). Among fathers only Magellan carries it (+3, naval-scoped).</summary>
    private const string MovementBonusId = "model.modifier.movementBonus";

    /// <summary>The high-seas sail-turn modifier (additive). Magellan's −1 shortens the crossing.</summary>
    private const string SailHighSeasId = "model.modifier.sailHighSeas";

    /// <summary>
    /// Scales a native-alarm <em>net</em> ambient delta by the human's <see cref="NativeAlarmModifierId"/> modifiers —
    /// both the elected-father one (Pocahontas −50%) and the player's <b>nation-type advantage</b> (the French
    /// <c>model.nationType.cooperation</c> −50%), stacked. Applied to the per-turn <b>ambient</b> proximity alarm
    /// (<see cref="ApplyAmbientNativeAlarm"/>), which folds the negative mission relief into the same per-settlement net
    /// <b>before</b> scaling, so the modifier damps the net once — matching FreeCol (<c>ServerPlayer.csNewTurn</c>, which
    /// applies <c>NATIVE_ALARM_MODIFIER</c> to the accumulated <c>extra</c> including the mission influence). Both signs
    /// are scaled: a −50% modifier halves an ambient gain <em>and</em> the mission relief inside the same net, so relief
    /// is no longer applied 2× too fast. Combat tension is a separate, raw path (<see cref="ApplyNativeCombatTension"/>)
    /// and never routes through here. Goodwill from trade and the whole-tribe decay are also separate and unscaled.
    /// </summary>
    private int ScaleNativeAlarmGain(int delta)
    {
        // FreeCol applies NATIVE_ALARM_MODIFIER to the whole accumulated net (gain minus mission relief), not to gains
        // only — so we scale ANY delta. Combat tension does not come through here (audited: the sole caller is the
        // ambient pass in ApplyAmbientNativeAlarm; ApplyNativeCombatTension applies its delta raw via ChangeNativeAlarm).
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

    /// <summary>Zeroes every native settlement's alarm toward the <b>human</b> only (FreeCol <c>resetNativeAlarm</c> → <c>Tension.TENSION_MIN</c>, the Happy band); other powers' channels are untouched. Refreshes most-hated.</summary>
    private void ResetAllNativeAlarm()
    {
        foreach (NativeSettlement settlement in _nativeSettlements)
        {
            settlement.Alarm = 0; // channel 0 (human)
            UpdateMostHated(settlement);
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
    internal int ApplyGoodsModifiers(Player player, string goodsId, int baseAmount, int? colonyId = null)
    {
        var modifiers = player.Congress.Select(Ruleset.Father)
            .SelectMany(f => f.Modifiers)
            .Where(m => m.TargetId == goodsId)
            .ToList();
        // A player's nation-type GOODS advantage (FreeCol european-nation-type <modifier id="model.goods.*">): the
        // Swedish lumber +2, Danish grain +2 and Russian furs +2 — the UNSCOPED (tile-yield) advantages. The only
        // goods-yield callers of this method are the TileYield overloads, so folding here targets tile output. The
        // PERSON-scoped building advantages (Swedish hammers, Russian coats — scoped model.ability.person in the spec)
        // must NOT fold here: building output does not run through this method, and those are folded per working
        // colonist in ComputeBuildingProduction instead — so they are excluded here to keep the scopes honest even if
        // a future caller passes a person-scoped good. The classic four nations carry no goods advantage → no change.
        if (!PersonScopedNationGoods.Contains(goodsId))
        {
            modifiers.AddRange(NationTypeModifiers(player, goodsId));
        }
        if (goodsId == BellsId && HasAbilityFor(player, AddTaxToBellsAbility))
        {
            // Paine: the spec template modifier (index 40) takes the current tax rate as its value.
            modifiers.Add(new FatherModifier(BellsId, ModifierType.Percentage, player.TaxRate, 40));
        }
        // Fold any duration-bounded modifier currently active for this goods (FreeCol's temporary Modifiers join the
        // permanent ones in applyModifiers). The registry is empty in the classic default game, so this adds nothing
        // there and the result is byte-identical; a registered event/variant bonus folds here while it is in window.
        // A colony id scopes the fold to that colony's production, so a per-colony disaster penalty (registered with a
        // ColonyId) only damps the struck colony — a non-colony fold (null id) sees only unscoped modifiers.
        modifiers.AddRange(ActiveTemporaryModifiers(goodsId, colonyId).Select(m => m.Payload));
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

    /// <summary>Benjamin Franklin's modifier (FreeCol <c>model.modifier.peaceTreaty</c>, +50%): scales the probability that a recent peace treaty with the holder still holds — a rival is less willing to re-declare war on a Franklin power (FreeCol <c>EuropeanAIPlayer.peaceHolds</c>: <c>prob = p.apply(prob, turn, PEACE_TREATY)</c>).</summary>
    private const string PeaceTreatyModifierId = "model.modifier.peaceTreaty";

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
    /// Benjamin Franklin's <c>peaceTreaty</c> multiplier on the peace-hold probability — FreeCol's
    /// <c>p.apply(prob, turn, Modifier.PEACE_TREATY)</c>, where <paramref name="otherParty"/> is the treaty's
    /// <em>other</em> party (<c>p</c>). Each of its elected fathers' <c>peaceTreaty</c> percentage modifiers is a
    /// <em>percentage-additive</em> bonus, so the factors compound the same way FreeCol's <c>Modifier.apply</c> does:
    /// <c>∏ (1 + value/100)</c>. Franklin's lone +50% → <b>1.5</b>; a non-Franklin party → <b>1.0</b> (the base
    /// passes through unscaled). Pure; draws no RNG.
    /// </summary>
    internal double PeaceTreatyModifierFactor(Player otherParty)
    {
        double factor = 1.0;
        foreach (FatherModifier modifier in otherParty.Congress.Select(Ruleset.Father)
            .SelectMany(f => f.Modifiers)
            .Where(m => m.TargetId == PeaceTreatyModifierId && m.Type == ModifierType.Percentage))
        {
            factor *= 1.0 + modifier.Value / 100.0; // FreeCol Modifier.apply for a percentage-additive bonus
        }
        return factor;
    }

    /// <summary>
    /// Whether <paramref name="otherParty"/> holds any <c>peaceTreaty</c> modifier (i.e. Benjamin Franklin). The
    /// peace-hold gate is deliberately <b>inert without it</b>: the default game has no Franklin, so the decaying
    /// peace-hold never engages and a no-Franklin game stays byte-identical (ADR-009) — exactly the Wave-12 contract.
    /// </summary>
    private bool HasPeaceTreatyModifier(Player otherParty) =>
        otherParty.Congress.Select(Ruleset.Father)
            .SelectMany(f => f.Modifiers)
            .Any(m => m.TargetId == PeaceTreatyModifierId && m.Type == ModifierType.Percentage);

    /// <summary>
    /// The probability (in <c>[0, 1]</c>) that a peace between <paramref name="power"/> and <paramref name="otherParty"/>
    /// <b>holds</b> this turn — i.e. that <paramref name="power"/>, having accrued enough grievance to re-declare war,
    /// instead lets the treaty stand. The faithful mirror of FreeCol <c>EuropeanAIPlayer.peaceHolds</c>:
    /// <c>prob = (PEACE_PROBABILITY/100)^n</c>, then <c>prob = p.apply(prob, turn, Modifier.PEACE_TREATY)</c>, where
    /// <c>n</c> is the turns since the peace took force (<see cref="Player.PeaceTurns"/>) and <c>p</c> is
    /// <paramref name="otherParty"/>. So the base <b>decays</b> the longer the peace has held (classic base <b>0.90</b>
    /// per turn) and Benjamin Franklin's <c>peaceTreaty +50%</c> on <paramref name="otherParty"/> <em>scales</em> it up
    /// (<see cref="PeaceTreatyModifierFactor"/>), clamped to <c>[0, 1]</c>.
    /// <para><b>The Wave-12 gate is preserved:</b> a <paramref name="otherParty"/> <b>without</b> Franklin's
    /// <c>peaceTreaty</c> modifier yields <b>0</b> — no reprieve, the war proceeds exactly as before, so the default
    /// game is unchanged (this deliberately diverges from raw FreeCol, where the 0.90 base applies even without Franklin,
    /// to keep the default byte-identical). When the modifier <em>is</em> present, the full decaying base applies.
    /// A pair with no recorded peace turn (never met, or whose last transition was war) likewise yields 0.</para>
    /// Pure; draws no RNG (the roll is in <see cref="PeaceTreatyHolds"/>).
    /// </summary>
    internal double PeaceTreatyHoldProbability(Player power, Player otherParty)
    {
        if (!HasPeaceTreatyModifier(otherParty))
        {
            return 0.0; // no Franklin → the gate is inert; the default game is byte-identical
        }
        if (!power.PeaceTurns.TryGetValue(otherParty.PlayerId, out int peaceTurn))
        {
            return 0.0; // no peace on record (never met, or war was the last transition) — nothing to hold
        }
        int n = Math.Max(0, Turn - peaceTurn);                       // turns since the treaty took force
        double prob = Math.Pow(Ruleset.GameOptions.PeaceProbabilityMultiplier, n); // FreeCol (PEACE_PROBABILITY/100)^n
        prob *= PeaceTreatyModifierFactor(otherParty);               // FreeCol p.apply(prob, turn, PEACE_TREATY)
        return Math.Clamp(prob, 0.0, 1.0);
    }

    /// <summary>
    /// Whether a peace between <paramref name="power"/> and <paramref name="otherParty"/> <b>holds</b> this turn rather
    /// than collapsing into a fresh war — FreeCol <c>EuropeanAIPlayer.peaceHolds</c>: when <paramref name="power"/> has
    /// accrued enough grievance that its stance would flip Peace/CeaseFire → War, it first rolls against the decaying
    /// peace-hold probability and, on success, lets the treaty stand. Returns <c>true</c> (war averted) with probability
    /// <see cref="PeaceTreatyHoldProbability"/> — the decaying <c>peaceProb^n</c> base scaled by Benjamin Franklin's
    /// <c>peaceTreaty</c> modifier on <paramref name="otherParty"/>, so a <paramref name="otherParty"/> <b>without</b>
    /// Franklin yields probability 0 and this <b>always returns false</b> (the war proceeds exactly as before — the
    /// default game is unchanged), and the longer the peace has held the more likely it eventually breaks.
    /// </summary>
    /// <remarks>
    /// <b>Determinism (ADR-009):</b> the roll is drawn from <paramref name="power"/>'s <b>own</b> RNG stream
    /// (<see cref="RandomFor"/>) — never the human's stream 0 — so a Franklin human's seeded game stays byte-identical
    /// (the reprieve perturbs only the rolling power's stream, exactly as FreeCol rolls on the AI's own random). With no
    /// Franklin party (or no recorded peace) the probability is 0, so no roll is drawn at all and even the rolling
    /// power's stream is untouched (FreeCol's <c>prob &gt; 0.0f</c> short-circuit). Mirrors FreeCol's
    /// <c>randomInt(100) &lt; (int)(100·prob)</c>.
    /// </remarks>
    internal bool PeaceTreatyHolds(Player power, Player otherParty)
    {
        double prob = PeaceTreatyHoldProbability(power, otherParty);
        if (prob <= 0.0)
        {
            return false; // no Franklin / no recorded peace → no reprieve; the war proceeds and the power's stream is untouched
        }
        return RandomFor(power).Next(100) < (int)(100.0 * prob);
    }

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
    /// he is at <see cref="Stance.Peace"/> or <see cref="Stance.Alliance"/> with at least one other <b>European power</b>.
    /// The peer set matches FreeCol's <c>getLiveEuropeanPlayers(this)</c> — a <em>live</em> player whose type is
    /// <see cref="IsEuropeanPower"/> (Colonial, Rebel, Independent, or the Royal Expeditionary Force), so a post-declaration
    /// rebel/independent nation, and the REF itself while it is a live player, all count as trade peers; the native nations
    /// (and a wiped-out power) never do. The oracle that will let a custom house auto-sell a boycotted good once the
    /// custom-house boycott gate lands; false for every player until de Witt is elected, so the default game's
    /// custom-house behaviour is unchanged.
    /// </summary>
    public bool CustomHouseTradesWithForeignCountries(int playerId) =>
        PlayerById(playerId) is { PlayerType: PlayerType.Colonial } p
        && HasAbilityFor(p, CustomHouseForeignTradeAbility)
        && _players.Any(o => o.PlayerId != p.PlayerId && IsLiveEuropeanPower(o)
            && p.Stances.GetValueOrDefault(o.PlayerId) is Stance.Peace or Stance.Alliance);

    /// <summary>
    /// Whether <paramref name="player"/> is a <b>European power</b> (FreeCol <c>Player.isEuropean</c>): a Colonial,
    /// Rebel, Independent, or Royal-Expeditionary-Force player — i.e. any non-native, non-retired nation that competes on
    /// the European stage. Used to scope diplomacy/trade peer sets to the powers FreeCol counts as European.
    /// </summary>
    private static bool IsEuropeanPower(Player player) =>
        player.PlayerType is PlayerType.Colonial or PlayerType.Rebel or PlayerType.Independent
            or PlayerType.RoyalExpeditionaryForce;

    /// <summary>
    /// Whether <paramref name="player"/> is a <see cref="IsEuropeanPower"/> that is still <b>live</b> — it holds at least
    /// one colony or one non-native unit (the same liveness the victory reads use, FreeCol's not-<c>isDead</c>). The REF
    /// counts while it still has un-landed/landed forces. Mirrors FreeCol <c>getLiveEuropeanPlayers</c> (which, unlike
    /// <see cref="LiveEuropeanPowers"/>, includes the REF).
    /// </summary>
    private bool IsLiveEuropeanPower(Player player) =>
        IsEuropeanPower(player)
        && (ColoniesOf(player).Any() || _units.Any(u => u.OwnerId == player.PlayerId && !u.IsNative));

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
    /// <summary>The building-hammers goods id (Swedish <c>building</c> nation-type advantage, +2, <c>person</c>-scoped).</summary>
    private const string HammersGoodsId = "model.goods.hammers";

    /// <summary>The refined-coats goods id (Russian <c>furTrapping</c> nation-type advantage, +2, <c>person</c>-scoped).</summary>
    private const string CoatsGoodsId = "model.goods.coats";

    /// <summary>
    /// The goods whose nation-type advantage modifier is <c>person</c>-scoped (FreeCol <c>&lt;scope ability-id="model.ability.person"/&gt;</c>)
    /// — the Swedish <b>hammers</b> +2 and Russian <b>coats</b> +2. These apply to a colonist's BUILDING production (folded per
    /// working occupant in <see cref="ComputeBuildingProduction"/>), never to raw tile yield, so they are excluded from the
    /// tile-yield fold in <see cref="ApplyGoodsModifiers(Player, string, int, int?)"/>. Our modifier parser drops the spec's
    /// <c>ability-id</c> scope (it only captures unit-type <c>&lt;scope type="…"/&gt;</c>), so we honour the person-scope by
    /// PLACEMENT — this set is the single source of truth for which goods route to the building path instead of the tile path.
    /// </summary>
    private static readonly HashSet<string> PersonScopedNationGoods = new(StringComparer.Ordinal) { HammersGoodsId, CoatsGoodsId };

    private IEnumerable<FatherModifier> NationTypeModifiers(Player player, string targetId) =>
        // With national advantages turned OFF (the New-Game dial, 86d3fq0za) no nation-type advantage applies, so the
        // seam folds nothing for every player — a chosen nation plays with the neutral default. On (the classic default)
        // a player's nation type contributes its matching advantage modifiers.
        NationalAdvantages != NationalAdvantages.None
        && player.NationId is { } nationId && Ruleset.EuropeanNations.FirstOrDefault(n => n.Id == nationId) is { } nation
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
        // production cannot be negative. Both numbers come from the parsed base
        // gameOptions bundle (the classic −4 / +2; 86d3d335r).
        int europe = (OwnPersonsInEurope(player) * Ruleset.GameOptions.EuropeanUnitImmigrationPenalty)
            + Ruleset.GameOptions.PlayerImmigrationBonus;
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

    /// <summary>
    /// Creates a recruited unit docked in <paramref name="player"/>'s Europe (it has never been on the map). Every
    /// emigration/recruitment path (auto-emigrate, the Brewster choice, the paid recruit, Fountain of Youth, the
    /// survival auto-recruit) funnels through here — the single spawn chokepoint, mirroring FreeCol's lone
    /// <c>new ServerUnit(…, role)</c> in <c>ServerPlayer.csEmigrate</c> — so it is also where a recruit is equipped in
    /// its unit type's default role (FreeCol <c>equipEuropeanRecruits</c>; see <see cref="EquipEuropeanRecruit"/>).
    /// </summary>
    private Unit CreateEuropeRecruit(Player player, string unitTypeId)
    {
        var unit = new Unit(_nextUnitId++, Ruleset.Unit(unitTypeId), new Position(0, 0))
        {
            Location = UnitLocation.InEurope,
            OwnerId = player.PlayerId, // the recruit belongs to its player (the human is 0; a foreign power its own id)
        };
        EquipEuropeanRecruit(unit); // equipEuropeanRecruits: experts (veteran soldier, …) arrive already in their role
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
    /// The <b>net</b> yield the colony screen's per-tile diamond badge shows for a worked tile: the goods produced by
    /// the tile's <em>actual</em> worker type (folding the worker's index-30 production modifier — an expert lumberjack's
    /// ×2 lumber, an expert farmer's +2 grain) plus the Sons-of-Liberty <see cref="Colony.ProductionBonus"/>, floored at
    /// 0. This is exactly the figure <see cref="ColonyProductionSummary"/> / <see cref="RunColonyTurn"/> bank for that
    /// tile (ADR-006: one tested figure), so the diamond badge matches the production overview. Uses the colony's owning
    /// player's Founding-Father modifiers (falling back to the human for an unresolvable owner), as the colony turn does.
    /// </summary>
    public int TileWorkerNetYield(Colony colony, Position tile, string goodsId)
    {
        Player owner = PlayerById(colony.OwnerId) ?? _human;
        return Math.Max(0, TileYield(owner, colony.WorkerTypeAt(tile), tile, goodsId, colony.Id) + colony.ProductionBonus);
    }

    /// <summary>
    /// A colony's per-turn <b>net production by stored good</b> under its present assignments: each tile worker's
    /// yield (folding the human's Founding-Father goods modifiers) + the colony-centre tile's unattended output,
    /// less the food the colonists eat (<see cref="Colony.Population"/> × <see cref="Specification.ColonyConstants.FoodPerColonist"/>).
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
        Add(Colony.FoodId, -colony.Population * Ruleset.ColonyConstants.FoodPerColonist);
        return net;
    }

    /// <summary>
    /// A colony's <b>full per-turn production breakdown</b> by stored good — what it <em>produces</em>, what it
    /// <em>consumes</em>, and the <em>net</em> for every good touched this turn — the read behind the colony screen's
    /// production-overview panel and FreeCol's colony-panel production summary. Unlike <see cref="ColonyNetProduction"/>
    /// (tiles + centre − food eaten only), this also folds in <b>building production</b>: the manufactured goods a
    /// staffed building makes (a weaver's cloth, a carpenter's hammers), the bells/crosses an unattended town hall /
    /// church makes, and the raw inputs those buildings consume (the lumber a carpenter burns, the cotton a weaver
    /// spins). A <b>pure read</b> (no RNG, no mutation) that mirrors <see cref="RunColonyTurn"/>'s production order:
    /// (1) the colony-centre tile's unattended yield; (2) each worked tile's yield (worker type + Sons-of-Liberty bonus
    /// folded, floored at 0); (3) each building, in build order, against a running working copy of the warehouse — so
    /// input scarcity matches the live turn (a building short of inputs scales down identically); then (4) the food the
    /// colonists eat. Horse breeding and construction are excluded (breeding eats only this-turn's surplus and is an
    /// auto-production special case; construction is a one-off material spend, not steady-state production), matching
    /// what a production summary should show. Per good, <c>Net = Produced − Consumed</c>; both are non-negative
    /// (consumption is reported as a positive amount). ADR-006: presentation reads this and renders it.
    /// </summary>
    /// <param name="colony">The colony to summarise.</param>
    /// <returns>Stored-good id → its <see cref="ColonyGoodFlow"/> (produced / consumed / net) for the turn.</returns>
    public IReadOnlyDictionary<string, ColonyGoodFlow> ColonyProductionSummary(Colony colony)
    {
        var produced = new Dictionary<string, int>();
        var consumed = new Dictionary<string, int>();
        // A working copy of the warehouse the building-input scarcity test reads against, accumulating production as the
        // turn would (centre + tiles bank before buildings convert) so a building short of an input scales down exactly
        // as the live RunColonyTurn does. Keyed by stored id.
        var working = new Dictionary<string, int>(colony.Stores);
        void Bank(string good, int amount)
        {
            string stored = Ruleset.StorageIdOf(good);
            produced[stored] = produced.GetValueOrDefault(stored) + amount;
            working[stored] = working.GetValueOrDefault(stored) + amount;
        }
        void Consume(string good, int amount)
        {
            string stored = Ruleset.StorageIdOf(good);
            consumed[stored] = consumed.GetValueOrDefault(stored) + amount;
            working[stored] = Math.Max(0, working.GetValueOrDefault(stored) - amount);
        }

        // (1) colony-centre unattended yield.
        foreach (ProductionEntry p in Map.TerrainAt(colony.Position).Productions.Where(p => p.Unattended))
        {
            foreach (GoodsOutput o in p.Outputs)
            {
                Bank(o.GoodsId, o.Amount);
            }
        }
        // The colony's owning player folds its own Founding-Father goods modifiers into tile yields + carries its
        // bankruptcy flag (falling back to the human for a colony with no resolvable owner, as the tile-yield helper does).
        Player owner = PlayerById(colony.OwnerId) ?? _human;
        // (2) worked tiles (worker type + Sons-of-Liberty bonus, floored at 0 — the RunColonyTurn fold).
        foreach ((Position tile, string goodsId) in colony.TileWorkers)
        {
            Bank(goodsId, Math.Max(0, TileYield(owner, colony.WorkerTypeAt(tile), tile, goodsId, colony.Id) + colony.ProductionBonus));
        }
        // (3) buildings, in build order, against the running working copy (scarcity matches the live turn). Breeding
        //     (auto-production) is skipped — it eats only this-turn's surplus, an auto-production special case.
        bool ownerBankrupt = owner.Bankrupt;
        foreach (string buildingId in colony.Buildings)
        {
            BuildingType building = Ruleset.Building(buildingId);
            foreach (ProductionEntry entry in building.Productions)
            {
                if (building.BreedingDivisor > 0 && entry.Outputs.Count == 1
                    && Ruleset.Goods(entry.Outputs[0].GoodsId).BreedingNumber is not null)
                {
                    continue;
                }
                foreach ((string storageId, int delta) in ComputeBuildingProduction(colony, building, entry, g => working.GetValueOrDefault(g), ownerBankrupt))
                {
                    if (delta >= 0)
                    {
                        Bank(storageId, delta);
                    }
                    else
                    {
                        Consume(storageId, -delta);
                    }
                }
            }
        }
        // (4) the colonists eat.
        Consume(Colony.FoodId, colony.Population * Ruleset.ColonyConstants.FoodPerColonist);

        var flows = new Dictionary<string, ColonyGoodFlow>();
        foreach (string good in produced.Keys.Concat(consumed.Keys).Distinct())
        {
            int p = produced.GetValueOrDefault(good);
            int c = consumed.GetValueOrDefault(good);
            flows[good] = new ColonyGoodFlow(p, c);
        }
        return flows;
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
    internal int TileYield(Player player, string workerTypeId, Position tile, string goodsId, int? colonyId = null) =>
        ApplyGoodsModifiers(player, goodsId,
            ApplyWorkerProductionModifiers(workerTypeId, goodsId,
                ApplyScopedResourceModifiers(workerTypeId, tile, goodsId, TileYieldPotential(tile, goodsId))),
            colonyId); // colonyId scopes a per-colony disaster production penalty to the struck colony's tile output

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
    /// Expends a finite bonus resource by the amount it boosted this turn's production of <paramref name="goodsId"/> on
    /// <paramref name="tile"/> by a worker of <paramref name="workerTypeId"/>, and removes the deposit when its quantity
    /// is exhausted — leaving the tile at its bare yield (FreeCol <c>ServerColonyTile.expendResource</c> →
    /// <c>Resource.useQuantity</c>). The reduction is the resource's own bonus contribution
    /// (<see cref="ResourceBonusDelta"/>) capped at the remaining quantity; a limitless resource (no stored quantity)
    /// never depletes. A no-op for a tile with no resource or one that does not boost this good.
    /// </summary>
    private void DepleteWorkedResource(Position tile, string workerTypeId, string goodsId)
    {
        if (Map.ResourceQuantityAt(tile) is not { } quantity || Map.ResourceAt(tile) is not { } resourceId)
        {
            return; // no resource, or a limitless one (no finite quantity to deplete)
        }
        int bonus = ResourceBonusDelta(resourceId, workerTypeId, goodsId, TileYieldPotential(tile, goodsId));
        if (bonus <= 0)
        {
            return; // this resource adds nothing to this good — nothing to expend
        }
        int remaining = quantity - bonus;
        if (remaining > 0)
        {
            Map.SetResourceQuantity(tile, remaining);
        }
        else
        {
            // Exhausted: drop the deposit and its quantity so the tile reverts to its bare base yield.
            Map.RemoveResource(tile);
        }
    }

    /// <summary>
    /// The amount a bonus resource adds to one worker's production of <paramref name="goodsId"/> — the resource's
    /// unscoped + worker-scoped modifiers applied to <paramref name="boostedPotential"/> (already inclusive of the
    /// resource), minus that same potential with the resource's contribution backed out. For the classic finite
    /// resources (minerals/ore/silver — all flat additives) this is simply the additive value; the general form also
    /// handles a multiplicative deposit. Mirrors FreeCol <c>Resource.applyBonus</c>'s <c>applyModifiers(potential) −
    /// potential</c> bonus term.
    /// </summary>
    private int ResourceBonusDelta(string resourceId, string workerTypeId, string goodsId, int boostedPotential)
    {
        ResourceModifier[] modifiers = Ruleset.Resource(resourceId).Modifiers
            .Where(m => m.GoodsId == goodsId && (m.IsUnscoped || m.ScopeUnitTypes.Contains(workerTypeId)))
            .OrderBy(m => m.Index)
            .ToArray();
        if (modifiers.Length == 0)
        {
            return 0;
        }
        // Back the resource out to the bare potential, then re-apply it: the gap is the resource's contribution.
        double bare = boostedPotential;
        for (int i = modifiers.Length - 1; i >= 0; i--)
        {
            bare = modifiers[i].Type == ModifierType.Multiplicative ? bare / modifiers[i].Value : bare - modifiers[i].Value;
        }
        return boostedPotential - (int)bare;
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

    /// <summary>
    /// Puts an idle colonist to work on a tile producing one goods type. If <paramref name="tile"/> is
    /// <b>native-owned</b>, working it forces a buy-or-steal-or-abandon claim first (FreeCol claims a worked tile via
    /// <c>csClaimLand</c>): this overload throws <see cref="LandClaimRequiredException"/> so the presentation surfaces
    /// the pay/steal/abandon choice (then calls <see cref="AssignWork(Colony, Position, string, LandClaimChoice)"/>).
    /// </summary>
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckAssignWork(Colony, Position, string)"/>.</exception>
    /// <exception cref="LandClaimRequiredException">The tile is native-owned and no claim choice was given.</exception>
    public void AssignWork(Colony colony, Position tile, string goodsId) =>
        AssignWork(_human, colony, tile, goodsId, claim: null);

    /// <summary>
    /// Puts an idle colonist to work on a <b>native-owned</b> tile, resolving the forced claim with the human's
    /// <paramref name="claim"/>: <see cref="LandClaimChoice.Buy"/> pays the land price, <see cref="LandClaimChoice.Steal"/>
    /// takes it and angers the owning nation (per-player alarm). Ignored on a tile that is not native-owned. Use
    /// <see cref="RequiredLandClaim(Position)"/> to learn the price before offering the choice.
    /// </summary>
    /// <exception cref="InvalidMoveException">Not allowed (see <see cref="CheckAssignWork(Colony, Position, string)"/>), or the buy is unaffordable / the choice is <see cref="LandClaimChoice.Abandon"/>.</exception>
    public void AssignWork(Colony colony, Position tile, string goodsId, LandClaimChoice claim) =>
        AssignWork(_human, colony, tile, goodsId, claim);

    /// <summary>Puts an idle colonist to work on behalf of <paramref name="player"/> (the colony owner), gating on that
    /// player's yields. A native-owned tile is auto-claimed for an <b>AI</b> owner (<see cref="AiResolveLandClaim"/>);
    /// the human path arrives here only with an explicit choice via the public overloads.</summary>
    internal void AssignWork(Player player, Colony colony, Position tile, string goodsId) =>
        AssignWork(player, colony, tile, goodsId, claim: null);

    /// <summary>Shared work-assignment core; <paramref name="claim"/> null means "no explicit choice" — auto-resolved for an AI owner, rejected for the human (forces the UI choice).</summary>
    private void AssignWork(Player player, Colony colony, Position tile, string goodsId, LandClaimChoice? claim)
    {
        MoveCheck check = CheckAssignWork(player, colony, tile, goodsId);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }
        ClaimWorkTileIfNeeded(player, tile, claim);
        colony.SetWorker(tile, goodsId, PickIdleWorkerFor(colony, goodsId));
    }

    /// <summary>
    /// Resolves the forced native-land claim for a worked <paramref name="tile"/> (86d3e4bj7), if one is required:
    /// the human must supply an explicit <paramref name="claim"/> (else <see cref="LandClaimRequiredException"/>, so the
    /// UI raises its dialog); an AI owner auto-resolves via <see cref="AiResolveLandClaim"/> (RNG-free). A no-op when the
    /// tile is not native-owned. The single seam every worker-seating path (tile-work picker, AI planner, food
    /// auto-assign) funnels through, so a colonist is never seated on un-claimed native ground.
    /// </summary>
    private void ClaimWorkTileIfNeeded(Player player, Position tile, LandClaimChoice? claim)
    {
        ForcedLandClaim forced = RequiredLandClaim(player, tile);
        if (!forced.Required)
        {
            return;
        }
        if (claim is null)
        {
            if (player.PlayerId == _human.PlayerId)
            {
                throw new LandClaimRequiredException(forced.BuyPrice, forced.OwningNation!);
            }
            claim = AiResolveLandClaim(player, tile);
        }
        ResolveForcedLandClaim(player, tile, claim.Value);
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
        // Auto food-assignment never raises the human's claim dialog (it runs unattended on founding/growth): for the
        // HUMAN a native-owned tile is skipped (they can later choose to claim + work it via the tile picker); an AI
        // owner auto-claims it (AiResolveLandClaim) so its colonists still feed themselves on native ground (86d3e4bj7).
        bool isHuman = player.PlayerId == _human.PlayerId;
        while (colony.IdleColonists > 0)
        {
            var best = colony.Position.Neighbours()
                .Where(n => Map.InBounds(n) && !colony.TileWorkers.ContainsKey(n)
                    && !(isHuman && RequiredLandClaim(player, n).Required)) // the human's auto-assign leaves native tiles for a deliberate claim
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
            ClaimWorkTileIfNeeded(player, best.Value.tile, claim: null); // no-op off native land; AI auto-resolves, human tiles already filtered out
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
    /// warehouse inputs to outputs (scaled down when inputs run short). Computes the per-good deltas via
    /// <see cref="ComputeBuildingProduction"/> (the shared pure calculator the production-summary read also uses) and
    /// applies them to the warehouse; breeding is its own auto-production path.
    /// </summary>
    private void RunBuildingProduction(Colony colony, BuildingType building, int foodProducedThisTurn, bool ownerBankrupt = false)
    {
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

            foreach ((string storageId, int delta) in ComputeBuildingProduction(colony, building, entry, colony.StoreOf, ownerBankrupt))
            {
                colony.AddGoods(storageId, delta);
            }
        }
    }

    /// <summary>
    /// The pure per-good production deltas (keyed by <b>stored</b> good id; negative = input consumed, positive = output
    /// produced) of one building <paramref name="entry"/> — the single calculator behind both the live
    /// <see cref="RunBuildingProduction"/> turn step and the read-only <see cref="ColonyProductionSummary"/>. It reads the
    /// warehouse only through <paramref name="storeOf"/> (the live colony for the turn; a running working copy for the
    /// summary) and never mutates, so the same arithmetic drives both. Breeding (auto-production) is excluded — its own
    /// path runs separately. An unattended entry with no workers yields its flat output; an attended entry with nobody
    /// assigned yields nothing.
    /// </summary>
    private IEnumerable<(string StorageId, int Delta)> ComputeBuildingProduction(
        Colony colony, BuildingType building, ProductionEntry entry, Func<string, int> storeOf, bool ownerBankrupt)
    {
        int workers = colony.BuildingWorkers.GetValueOrDefault(building.Id);
        if (!entry.Unattended && workers == 0)
        {
            yield break; // an attended entry with nobody assigned produces nothing
        }
        IReadOnlyList<string> occupants = colony.BuildingOccupants(building.Id);

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
        Player colonyOwner = PlayerById(colony.OwnerId)!; // the owner whose nation-type PERSON advantage folds per colonist
        Dictionary<string, int> outputTotals = new(entry.Outputs.Count);
        foreach (GoodsOutput output in entry.Outputs)
        {
            // A PERSON-scoped nation-type BUILDING advantage (FreeCol <scope ability-id="model.ability.person">): the
            // Swedish hammers +2 and Russian coats +2, added PER working colonist (its faithful home — building output
            // never routes through ApplyGoodsModifiers). Empty (and byte-identical) for the four classic nations and an
            // unattended entry. Folded on the worker-modified per-occupant output, uncompeted (FreeCol applies the flat
            // nation modifier alongside the worker's own, not scaled by the building's competence factor).
            int nationPersonBonus = entry.Unattended || !PersonScopedNationGoods.Contains(output.GoodsId)
                ? 0
                : (int)NationTypeModifiers(colonyOwner, output.GoodsId).Aggregate(0.0, (v, m) => m.ApplyTo(v));
            outputTotals[output.GoodsId] = entry.Unattended
                ? output.Amount
                : occupants.Sum(t => ApplyWorkerProductionModifiers(
                    t, output.GoodsId, output.Amount + rebelBonus, building.CompetenceFactor) + nationPersonBonus);
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
        // Expert "connections" floor (FreeCol BuildingProductionCalculator, gated on the experts-have-connections game
        // option — OFF in classic, so this whole block is skipped in the default game and the per-turn production is
        // byte-identical, ADR-009 / L5 soak). When on and the building carries model.ability.expertsUseConnections
        // (the factory tier), each expert worker of the building's expert type guarantees expertConnectionProduction
        // (4) units of output even without the raw input — modelled by RAISING the available input to at least that
        // floor before the scarcity ratio, never lowering it. Computed once (option-gated) so classic pays nothing.
        int expertConnectionFloor = 0;
        if (Ruleset.GameOptions.ExpertsHaveConnections && building.ExpertsUseConnections && entry.Outputs.Count > 0
            && Ruleset.ExpertForProducing(entry.Outputs[0].GoodsId) is { } buildingExpertType)
        {
            int expertCount = occupants.Count(t => t == buildingExpertType);
            expertConnectionFloor = building.EffectiveExpertConnectionProduction * expertCount;
        }

        double scarcity = 1.0;
        foreach (GoodsOutput input in entry.Inputs)
        {
            long required = (long)Math.Floor(input.Amount * ratio);
            int available = storeOf(Ruleset.StorageIdOf(input.GoodsId));
            // The connections floor only ever raises `available` (FreeCol: available = max(available, floor)); it
            // never lowers it, so it can only relieve scarcity. Applied here for the scarcity ratio only — the actual
            // input consumed below is still charged against real stock, so no phantom goods are drawn down.
            if (expertConnectionFloor > available)
            {
                available = expertConnectionFloor;
            }
            if (required > 0 && available < required)
            {
                scarcity = Math.Min(scarcity, (double)available / required);
            }
        }

        foreach (GoodsOutput input in entry.Inputs)
        {
            int wantConsume = (int)Math.Floor(input.Amount * ratio * scarcity + Epsilon);
            // With the connections floor raising `scarcity`, the wanted consumption can exceed the real stock (the
            // experts produce off "connections", not off input they don't have). Charge only what is actually present
            // so the warehouse is never drawn below 0 — FreeCol never consumes absent input. Without the floor this
            // clamp is inert (scarcity ≤ available/required already, so wantConsume ≤ stock), keeping classic
            // byte-identical.
            if (expertConnectionFloor > 0)
            {
                wantConsume = Math.Min(wantConsume, storeOf(Ruleset.StorageIdOf(input.GoodsId)));
            }
            yield return (Ruleset.StorageIdOf(input.GoodsId), -wantConsume);
        }
        foreach (GoodsOutput output in entry.Outputs)
        {
            // Each good's own worker-modified total, scaled by the (≤1) input-scarcity factor — the SoL bonus is
            // already folded in per worker, so a starved building scales the bonus down with the rest (FreeCol).
            // A bankrupt owner then halves the building's output (FreeCol model.disaster.bankruptcy: −50% to every
            // building-produced good, applied at DISASTER_PRODUCTION_INDEX — i.e. on the final goods production,
            // after the input charge above). Off in classic: a player is never bankrupt with upkeep disabled.
            double total = outputTotals[output.GoodsId] * scarcity;
            if (ownerBankrupt)
            {
                total *= BankruptcyProductionFactor;
            }
            // A per-colony disaster building-production penalty (FreeCol lossOfBuildingProduction: a timed −50% on the
            // struck colony's building goods, at DISASTER_PRODUCTION_INDEX) folds on the final output here. Classic
            // registers none, so ApplyColonyTemporaryModifiers is a no-op and this stays byte-identical (ADR-009).
            total = ApplyColonyTemporaryModifiers(colony.Id, output.GoodsId, total);
            yield return (Ruleset.StorageIdOf(output.GoodsId), (int)Math.Floor(total + Epsilon));
        }
    }

    /// <summary>
    /// Folds the colony-scoped active temporary modifiers targeting <paramref name="goodsId"/> into a colony's
    /// building-production <paramref name="value"/> (in ascending modifier index) — the FreeCol
    /// <c>lossOfBuildingProduction</c> disaster penalty (a timed −50% on the struck colony's building goods, at
    /// <c>DISASTER_PRODUCTION_INDEX</c>). Only <b>colony-scoped</b> modifiers whose colony matches are applied (an
    /// unscoped game-wide modifier is <em>not</em> folded here — building output does not otherwise run through
    /// <see cref="ApplyGoodsModifiers(Player, string, int, int?)"/>, so folding unscoped ones would double-count them
    /// elsewhere). The classic registry is empty, so this returns the value unchanged and the default game is byte-identical.
    /// </summary>
    private double ApplyColonyTemporaryModifiers(int colonyId, string goodsId, double value)
    {
        double result = value;
        foreach (TemporaryModifier modifier in _temporaryModifiers
            .Where(m => m.TargetId == goodsId && m.ColonyId == colonyId && m.AppliesTo(Turn))
            .OrderBy(m => m.Payload.Index))
        {
            result = modifier.Payload.ApplyTo(result);
        }
        return result;
    }

    /// <summary>
    /// The factor a bankrupt player's building output is multiplied by — FreeCol <c>model.disaster.bankruptcy</c>'s
    /// <c>lossOfBuildingProduction</c> effect is a −50% percentage modifier on every building-produced good, so a
    /// bankrupt colony makes half its normal building output until the player can pay upkeep again.
    /// </summary>
    private const double BankruptcyProductionFactor = 0.5;

    /// <summary>
    /// The modifier index a natural-disaster production penalty applies at (FreeCol <c>Modifier.DISASTER_PRODUCTION_INDEX</c>
    /// = 100) — high, so it folds <b>after</b> the worker/expert (index 30) and founding-father (index 40) modifiers,
    /// on the final goods production. Used when a <see cref="DisasterEffectKind.ProductionPenalty"/> registers its
    /// timed <see cref="TemporaryModifier"/>.
    /// </summary>
    private const int DisasterProductionIndex = 100;

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
    /// <see cref="Specification.ColonyConstants.FoodPerColonist"/>). Mirrors the production + consumption in <see cref="RunColonyTurn"/>;
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
                produced += Math.Max(0, TileYield(owner, colony.WorkerTypeAt(tile), tile, goodsId, colony.Id) + colony.ProductionBonus);
            }
        }
        return produced - colony.Population * Ruleset.ColonyConstants.FoodPerColonist;
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
            - colony.Population * Ruleset.ColonyConstants.FoodPerColonist;
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
            string workerType = colony.WorkerTypeAt(tile);
            // The working colonist's type folds its expert bonus into the yield (free colonist → no change). The
            // colony id scopes any active per-colony disaster tile-production penalty to this colony (FreeCol
            // lossOfTileProduction, applied to the struck colony only).
            int produced = Math.Max(0, TileYield(owner, workerType, tile, goodsId, colony.Id) + colony.ProductionBonus);
            colony.AddGoods(storageId, produced);
            if (storageId == Colony.FoodId)
            {
                foodThisTurn += produced;
            }
            // On-the-job experience: a free colonist accrues this turn's output and may upgrade to the good's expert.
            AccrueAndRollExperience(colony, tile, goodsId, produced, rng);
            // A finite bonus resource on the worked tile is expended by the bonus it contributed this turn; once its
            // quantity is used up the deposit is removed and the tile falls back to its bare yield (FreeCol depletion).
            DepleteWorkedResource(tile, workerType, goodsId);
        }

        // 1c. Buildings produce: unattended entries always run (town hall bell);
        //     worker entries convert inputs to outputs per colonist, limited by
        //     what the warehouse holds. Horse breeding (auto-production) may eat only this turn's surplus food.
        //     A bankrupt owner (couldn't pay upkeep last turn) halves all building output — the FreeCol
        //     model.disaster.bankruptcy −50% building-production penalty (off in classic: never bankrupt).
        bool ownerBankrupt = owner.Bankrupt;
        foreach (string buildingId in colony.Buildings)
        {
            RunBuildingProduction(colony, Ruleset.Building(buildingId), foodThisTurn, ownerBankrupt);
        }

        // 1d. Construction completes when materials are saved up.
        RunConstruction(colony);

        // 1e. Warehouse overflow: a storable good produced past the colony's capacity is wasted this turn
        //     (FreeCol csNewTurnWarnings — getWarehouseCapacity). Non-storable goods (bells/crosses/hammers,
        //     which accrue toward liberty/immigration/construction) and food (consumed/grown, never warehoused
        //     to a cap here) are exempt. Run after construction so a build isn't starved of materials it consumes.
        SpillWarehouseOverflow(owner, colony);

        // 2. Colonists eat; an unfed colonist starves. With more than one colonist a single colonist is lost that
        //    turn (FreeCol's per-turn famine victim); with only the LAST colonist left and still no food, the colony
        //    is DESTROYED — disposed exactly like an abandon (FreeCol ServerColony.csNewTurn's model.colony.colonyStarved
        //    branch → ServerPlayer.csDisposeSettlement). Note: the classic colony-centre tile always yields ≥ 2 food
        //    (desert/arctic 2, plains 3…), exactly a lone colonist's appetite, so a size-1 colony never reaches a
        //    shortfall in normal play and the L5 soak keeps every colony — destruction only becomes reachable once
        //    food production can drop below that (e.g. disasters).
        int shortfall = colony.ConsumeFood(colony.Population * Ruleset.ColonyConstants.FoodPerColonist);
        if (shortfall > 0 && colony.Population > 1)
        {
            colony.Population--;
            TrimAssignments(colony);
            if (owner.PlayerId == _human.PlayerId)
            {
                // A survivable famine: one colonist starved but the colony lives on — warn the human (the per-turn
                // famine victim of FreeCol's model.colony.colonyStarving, distinct from total destruction below).
                _colonyFamineNotices.Add(new ColonyFamineNotice(colony.Name, colony.Position, colony.Population));
            }
        }
        else if (shortfall > 0) // the last colonist could not be fed → the colony starves out of existence
        {
            StarveColonyToDeath(owner, colony);
            return; // a destroyed colony does not grow, teach, or export this turn (FreeCol returns after disposal)
        }

        // 3. Growth: a food surplus of 200 raises a new colonist, who reports
        //    to the best free food tile.
        if (colony.Food >= Ruleset.ColonyConstants.FoodForGrowth)
        {
            colony.ConsumeFood(Ruleset.ColonyConstants.FoodForGrowth);
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

    /// <summary>
    /// Destroys a colony that has starved out its last colonist (FreeCol <c>ServerColony.csNewTurn</c>'s
    /// <c>model.colony.colonyStarved</c> branch → <c>ServerPlayer.csDisposeSettlement</c>). The colony is
    /// <see cref="DisposeColony">disposed</see> — removed from the game, its tile cleared and its tile-work
    /// assignments dropped — exactly like an abandon; the last colonist dies with the colony (no unit walks out,
    /// unlike <see cref="AbandonColony"/>), and any <b>garrison units standing on the colony tile survive</b> on the
    /// now-empty land. When <paramref name="owner"/> is the human, the loss is <b>notified</b> (a transient
    /// <see cref="ColonyStarvedNotice"/> the presentation surfaces after the turn) and recorded as a
    /// <see cref="HistoryEventKind.ColonyDestroyed"/> history event (FreeCol <c>COLONY_DESTROYED</c>; <b>carries no
    /// score</b> — FreeCol never sets one, the lost colony's units/liberty already leave the score). <b>Deterministic</b>
    /// — no RNG is drawn (ADR-009); the colony is gone from the save but the history event now persists (v58).
    /// </summary>
    /// <param name="owner">The colony's owner (the human or a foreign power); only the human is notified / recorded.</param>
    /// <param name="colony">The starving colony to destroy.</param>
    private void StarveColonyToDeath(Player owner, Colony colony)
    {
        if (owner.PlayerId == _human.PlayerId)
        {
            _colonyStarvedNotices.Add(new ColonyStarvedNotice(colony.Name, colony.Position));
            RecordHistory(HistoryEventKind.ColonyDestroyed, $"{colony.Name} starved and was lost.");
        }
        DisposeColony(colony); // clears the tile + drops the colony; garrison units on the tile stay (per FreeCol)
    }

    /// <summary>Where a school student currently sits, so it can be upgraded in place (86d3c9p7f).</summary>
    private enum StudentLocation { Tile, Building, Idle }

    /// <summary>
    /// A stable identity for a claimable student within one teaching turn (86d3fpyc0) — so that when several teachers
    /// in a college/university teach in parallel, no two claim the same colonist. A tile worker is keyed by its tile;
    /// a building or idle colonist (interchangeable by type) is keyed by (location, building/empty, type, occurrence
    /// index) — the Nth same-typed colonist in that group. Not a persistent unit id (we have none); valid only within
    /// a single <see cref="RunSchoolTeaching"/> sweep.
    /// </summary>
    private readonly record struct StudentRef(StudentLocation Where, Position Tile, string BuildingId, string Type, int Index);

    /// <summary>The least-skilled colonist a school's teacher can raise this turn, with its location for an in-place upgrade and a per-turn <see cref="StudentRef"/> so parallel teachers don't double-claim it (86d3c9p7f / 86d3fpyc0).</summary>
    private readonly record struct Student(int Skill, StudentLocation Where, string Type, Position Tile, string BuildingId, int Index)
    {
        /// <summary>The per-turn claim key for this student (tile by position; building/idle by type + occurrence index).</summary>
        public StudentRef Ref => new(Where, Tile, BuildingId, Type, Index);
    }

    /// <summary>
    /// The eligible teachers in a school building, deterministically ordered (86d3fpyc0): every occupant whose
    /// <see cref="UnitType.Skill"/> fits the building's <see cref="BuildingType.MinimumSkill"/>..<see cref="BuildingType.MaximumSkill"/>
    /// window, ordered by unit-type id (Ordinal). A college has 2 teacher slots and a university 3, so a parallel
    /// school can have more than one teacher; the index in this list is the teacher's <b>slot</b> for its own
    /// training counter. FreeCol's <c>getNoAddReason</c> MINIMUM_SKILL/MAXIMUM_SKILL — the floor is ≥ 1 for every
    /// classic school, so only an expert ever teaches.
    /// </summary>
    private List<string> EligibleTeachers(Colony colony, string buildingId)
    {
        BuildingType building = Ruleset.Building(buildingId);
        return colony.BuildingOccupants(buildingId)
            .Where(t => Ruleset.Unit(t).Skill >= building.MinimumSkill && Ruleset.Unit(t).Skill <= building.MaximumSkill)
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// One colony's schooling step (86d3c9p7f / 86d3fpyc0, FreeCol <c>ServerBuilding.csTeach</c>): <b>each</b> eligible
    /// expert teacher in a school building raises a student in parallel — a schoolhouse has 1 teacher slot, a college 2,
    /// a university 3, and FreeCol teaches one student <b>per teacher</b>, not one per building. Every teacher takes the
    /// colony's least-skilled teachable colonist it can raise — petty criminal → indentured servant → free colonist →
    /// the teacher's skill-taught — but no two teachers claim the same student in one turn (FreeCol <c>findStudent</c>
    /// excludes a colonist already bound to another teacher). Needed turns are the spec base (4/6/8) reduced by the
    /// Sons-of-Liberty <see cref="Colony.ProductionBonus"/>, floored at 1; a teacher's per-slot counter resets when its
    /// student graduates or it has no eligible student. <b>Deterministic — no RNG</b> (classic automatic student
    /// selection): a colony with no expert in a school is a pure no-op, so the L5 soak (all free-colonist colonies)
    /// stays byte-stable.
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
            List<string> teachers = EligibleTeachers(colony, buildingId);
            if (teachers.Count == 0)
            {
                colony.ResetAllSchoolTraining(buildingId); // no teacher → every slot lapses (FreeCol)
                continue;
            }

            // One student per teacher, in parallel (FreeCol csTeach iterates each teacher in the building). Done in two
            // passes so every teacher's student selection sees the SAME colony state this turn: pass 1 claims a distinct
            // least-skilled student per teacher (StudentRef claimed-set = FreeCol findStudent's u.getTeacher()==null) and
            // accrues a turn; pass 2 applies the graduations. Folding the graduations into pass 1 would mutate the worker
            // overlay mid-loop and shift a later teacher's claim indices.
            var claimed = new HashSet<StudentRef>();
            var graduations = new List<(Student student, string target)>();
            for (int slot = 0; slot < teachers.Count; slot++)
            {
                string teacher = teachers[slot];
                if (FindLeastSkilledStudent(colony, teacher, claimed) is not { } student)
                {
                    colony.ResetSchoolTraining(buildingId, slot); // this teacher has no eligible student — its progress lapses
                    continue;
                }
                claimed.Add(student.Ref);
                colony.AddSchoolTrainingTurn(buildingId, slot);
                int needed = Math.Max(1, Ruleset.NeededTurnsOfTraining(teacher, student.Type) - colony.ProductionBonus);
                if (colony.SchoolTrainingTurnsAt(buildingId, slot) >= needed
                    && Ruleset.GetTeachingType(teacher, student.Type) is { } target)
                {
                    graduations.Add((student, target.Id));
                    colony.ResetSchoolTraining(buildingId, slot);
                }
            }
            foreach ((Student student, string target) in graduations)
            {
                UpgradeStudent(colony, student, target); // pass 2: apply after every teacher has claimed + accrued
            }
        }
    }

    /// <summary>
    /// The colony's least-skilled colonist a teacher of <paramref name="teacherType"/> can teach (FreeCol
    /// <c>Colony.findStudent</c>, least-skill-first), searched across worked tiles, buildings and the idle pool, skipping
    /// any colonist already <paramref name="claimed"/> by another teacher in the same turn (FreeCol <c>findStudent</c>'s
    /// <c>u.getTeacher() == null</c> — so parallel college/university teachers each raise a <b>distinct</b> student).
    /// <b>Trade tie-break (`86d3c9p7f` follow-up):</b> among equally-least-skilled students, one already producing the
    /// teacher's expert good (<see cref="UnitType.ExpertProduction"/>) wins — so an expert ore miner teaches the
    /// colonist already mining ore first (FreeCol <c>findStudent</c>'s <c>getWorkType() == expertise</c>). Remaining
    /// ties fall back to a stable enumeration order (tiles row-major, then buildings, then idle). Deterministic, no RNG.
    /// Null when the teacher can raise no unclaimed one (e.g. every other colonist is already an expert or claimed).
    /// </summary>
    /// <param name="colony">The colony whose colonists are searched.</param>
    /// <param name="teacherType">The teacher's unit-type id (its skill-taught sets what students can climb toward).</param>
    /// <param name="claimed">Students already taken by earlier teachers this turn (empty for the single-teacher case).</param>
    private Student? FindLeastSkilledStudent(Colony colony, string teacherType, IReadOnlySet<StudentRef> claimed)
    {
        string? expertGood = Ruleset.Unit(teacherType).ExpertProduction; // the good the teacher is expert in (null = non-goods expert)
        Student? best = null;
        bool bestWorksExpertGood = false;
        void Consider(string type, StudentLocation where, Position tile, string buildingId, int index, bool worksExpertGood)
        {
            if (Ruleset.GetTeachingType(teacherType, type) is null)
            {
                return; // not teachable by this teacher (already at/above the taught skill, or no education rung)
            }
            var candidate = new Student(Ruleset.Unit(type).Skill, where, type, tile, buildingId, index);
            if (claimed.Contains(candidate.Ref))
            {
                return; // another teacher already took this exact colonist this turn (FreeCol getTeacher() != null)
            }
            int skill = candidate.Skill;
            // Lower skill always wins; on a skill tie, a student already working the teacher's good wins over one that isn't.
            if (best is null
                || skill < best.Value.Skill
                || (skill == best.Value.Skill && worksExpertGood && !bestWorksExpertGood))
            {
                best = candidate;
                bestWorksExpertGood = worksExpertGood;
            }
        }
        foreach (Position tile in colony.TileWorkers.Keys.OrderBy(p => p.Y).ThenBy(p => p.X))
        {
            // TileWorkers maps a tile to the good worked there, so a tile student works the teacher's good directly.
            // A tile is keyed by its position (index 0), so each tile worker is its own claimable student.
            bool worksExpertGood = expertGood is not null && colony.TileWorkers[tile] == expertGood;
            Consider(colony.WorkerTypeAt(tile), StudentLocation.Tile, tile, "", 0, worksExpertGood);
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
            // Same-typed occupants are interchangeable, so index them per-type within the building to keep each distinct.
            var seenInBuilding = new Dictionary<string, int>();
            foreach (string occupant in colony.BuildingOccupants(b))
            {
                int index = seenInBuilding.GetValueOrDefault(occupant);
                seenInBuilding[occupant] = index + 1;
                Consider(occupant, StudentLocation.Building, default, b, index, buildingMakesExpertGood);
            }
        }
        // Idle colonists (non-free overlay entries first, then the implicit free ones), indexed per-type so two idle
        // colonists of the same type are distinct claims.
        var seenIdle = new Dictionary<string, int>();
        foreach (string idle in colony.IdleWorkerTypes)
        {
            int index = seenIdle.GetValueOrDefault(idle);
            seenIdle[idle] = index + 1;
            Consider(idle, StudentLocation.Idle, default, "", index, worksExpertGood: false); // idle colonists produce nothing
        }
        int implicitFree = colony.IdleColonists - colony.IdleWorkerTypes.Count;
        for (int i = 0; i < implicitFree; i++)
        {
            int index = seenIdle.GetValueOrDefault(Colony.FreeColonistTypeId);
            seenIdle[Colony.FreeColonistTypeId] = index + 1;
            Consider(Colony.FreeColonistTypeId, StudentLocation.Idle, default, "", index, worksExpertGood: false);
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
    /// Whether <paramref name="teacherType"/> may be placed as a <b>teacher</b> in school building
    /// <paramref name="buildingId"/> (86d3fpxd6, the assign-a-teacher gate; FreeCol <c>WorkLocation.getNoAddReason</c>'s
    /// MINIMUM_SKILL / MAXIMUM_SKILL on a school): the building must teach and have a free workplace, an idle colonist of
    /// that type must be available, and its <see cref="UnitType.Skill"/> must fall inside the school's
    /// <see cref="BuildingType.MinimumSkill"/>..<see cref="BuildingType.MaximumSkill"/> window — so a non-expert (skill
    /// below the floor) or an over-skilled expert (an elder statesman in a schoolhouse) is refused up front, rather than
    /// being seated and silently never teaching. The skill-window check is what distinguishes this from the generic
    /// <see cref="CheckAssignBuildingWork"/> (which any idle colonist passes). ADR-006: the panel reads this oracle and
    /// forwards <see cref="AssignTeacher"/>; the rule lives here and is xUnit-tested.
    /// </summary>
    public MoveCheck CheckAssignTeacher(Colony colony, string buildingId, string teacherType)
    {
        if (!colony.HasBuilding(buildingId))
        {
            return MoveCheck.No("The colony does not have that building.");
        }
        BuildingType building = Ruleset.Building(buildingId);
        if (!building.Teaches)
        {
            return MoveCheck.No($"The {building.ShortName} is not a school.");
        }
        if (colony.BuildingWorkers.GetValueOrDefault(buildingId) >= building.Workplaces)
        {
            return MoveCheck.No($"The {building.ShortName} has no free teacher slot.");
        }
        if (teacherType == Colony.FreeColonistTypeId || !colony.IdleWorkerTypes.Contains(teacherType))
        {
            return MoveCheck.No("That expert is not available in the colony.");
        }
        int skill = Ruleset.Unit(teacherType).Skill;
        if (skill < building.MinimumSkill)
        {
            return MoveCheck.No($"A {Ruleset.Unit(teacherType).ShortName} is not skilled enough to teach here.");
        }
        if (skill > building.MaximumSkill)
        {
            return MoveCheck.No($"A {Ruleset.Unit(teacherType).ShortName} is too advanced to teach in the {building.ShortName}.");
        }
        return MoveCheck.Yes(0);
    }

    /// <summary>
    /// Seats a specific idle expert as a <b>teacher</b> in a school building (86d3fpxd6): the player's explicit
    /// teacher-designation command — places <paramref name="teacherType"/> into <paramref name="buildingId"/> so it
    /// teaches the colony's least-skilled colonist (FreeCol's "drag a unit into the schoolhouse"). Gated by
    /// <see cref="CheckAssignTeacher"/> — only an idle colonist whose skill fits the school's window is accepted, so a
    /// non-expert or an over-skilled expert is rejected rather than seated as dead weight. Unlike
    /// <see cref="AssignBuildingWork(Colony, string)"/> (which picks a free colonist), this places the named expert.
    /// </summary>
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckAssignTeacher"/>.</exception>
    public void AssignTeacher(Colony colony, string buildingId, string teacherType)
    {
        MoveCheck check = CheckAssignTeacher(colony, buildingId, teacherType);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }
        colony.AssignBuildingWorker(buildingId, teacherType); // draws the named expert from the idle pool into the school
    }

    /// <summary>
    /// The distinct idle expert unit-type ids that could be designated as a teacher in school <paramref name="buildingId"/>
    /// (86d3fpxd6) — each passes <see cref="CheckAssignTeacher"/> — for the colony panel's assign-teacher control. Ordered
    /// by unit-type id (Ordinal) so the panel is deterministic. Empty when the building is not a school, is fully staffed,
    /// or no idle colonist's skill fits the school's window.
    /// </summary>
    public IReadOnlyList<string> AssignableTeachers(Colony colony, string buildingId) =>
        colony.IdleWorkerTypes
            .Distinct()
            .Where(t => CheckAssignTeacher(colony, buildingId, t).Allowed)
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();

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
    /// A colony's per-good warehouse capacity (the sum of its buildings' <c>warehouseStorage</c>; depot 100,
    /// warehouse 200, expansion 300). Public read-only oracle for the presentation (ADR-006) — the colony screen's
    /// custom-house retain-level control caps its slider at this, since retaining more than the warehouse can hold is
    /// meaningless. Mirrors the internal <see cref="WarehouseCapacity"/> the colony turn uses.
    /// </summary>
    public int ColonyWarehouseCapacity(Colony colony) => WarehouseCapacity(colony);

    /// <summary>
    /// Discards each storable good held above the colony's warehouse capacity (FreeCol's warehouse waste).
    /// Food (consumed/grown) and non-storable goods (bells/crosses/hammers) are exempt. A guard skips a colony
    /// with no capacity data (0) so a malformed/legacy colony never silently loses everything.
    /// </summary>
    private void SpillWarehouseOverflow(Player owner, Colony colony)
    {
        int capacity = WarehouseCapacity(colony);
        if (capacity <= 0)
        {
            return;
        }
        bool notify = owner.PlayerId == _human.PlayerId;
        foreach (string goodsId in colony.Stores.Keys.ToList())
        {
            GoodsType goods = Ruleset.Goods(goodsId);
            int held = colony.StoreOf(goodsId);
            if (goods.IsStorable && !goods.IsFood && held > capacity)
            {
                int wasted = held - capacity;
                colony.AddGoods(goodsId, -wasted); // drop the overflow to the cap
                if (notify)
                {
                    // Warn the human their warehouse is spilling this good (FreeCol's warehouse-overflow message).
                    _warehouseOverflowNotices.Add(new WarehouseOverflowNotice(colony.Name, colony.Position, goodsId, wasted));
                }
            }
        }
    }

    /// <summary>
    /// The per-turn custom-house auto-sell (FreeCol <c>ServerColony.csNewTurn</c>'s customs sale): if the colony has
    /// the export ability (a custom house), each eligible storable, tradeable good's surplus above its retain level
    /// is sold to <paramref name="owner"/>'s European market — the same after-tax, price-moving path as a manual sale
    /// (<see cref="SellColonyGoods(Player, Colony, string, int, bool)"/>). Eligibility follows <see cref="AutoExportMode"/>:
    /// in <see cref="GameSession.AutoExportMode.PerGood"/> only goods flagged <c>Exported</c> sell (food included if
    /// flagged — FreeCol-faithful); in <see cref="GameSession.AutoExportMode.ExportAllOverLevel"/> every sellable good
    /// does <b>except food</b> (auto-dumping food would halt growth). Goods are iterated in stable id order for
    /// determinism (ADR-009); a colony with no custom house — and the default PerGood mode with no toggles — sells
    /// nothing, so the soak stays byte-stable.
    /// <para>
    /// Boycott handling follows FreeCol's <c>Player.canTrade(type, Market.Access.CUSTOM_HOUSE)</c>: a boycotted good
    /// still sells (the custom house <b>smuggles</b> it — tax and price movement as a normal sale, no extra penalty)
    /// when <b>either</b> the <c>customIgnoreBoycott</c> game option is on (classic default,
    /// <see cref="GameOptions.CustomIgnoreBoycott"/>) <b>or</b> this owner holds Jan de Witt's
    /// <c>customHouseTradesWithForeignCountries</c> ability while at peace with a foreign power
    /// (<see cref="CustomHouseTradesWithForeignCountries"/>) — de Witt lets a custom house sell boycotted goods to
    /// foreign powers it is at peace with even with the smuggling option off. When neither condition holds a boycotted
    /// good is <b>skipped</b> safely — it is never sold and, crucially, the sell path is never even entered, so End Turn
    /// never throws <see cref="InvalidMoveException"/> on a boycotted custom-house good. The player-level allow flag is
    /// computed once above the per-good loop, then passed to <see cref="SellColonyGoods(Player, Colony, string, int, bool)"/>
    /// as its <c>ignoreBoycott</c> so the boycotted good's sale is made with the gate bypassed rather than throwing.
    /// </para>
    /// Each sale from a <b>human-owned</b> colony records a transient
    /// <see cref="CustomHouseSaleNotice"/> (good + amount + after-tax gold) the HUD surfaces after End Turn.
    /// </summary>
    private void AutoSellExports(Player owner, Colony colony)
    {
        if (!ColonyHasExportAbility(colony))
        {
            return;
        }
        bool exportAll = AutoExportMode == AutoExportMode.ExportAllOverLevel;
        // A boycotted good is still sold when EITHER the customIgnoreBoycott smuggling option is on (the classic
        // default) OR this owner has Jan de Witt's customHouseTradesWithForeignCountries ability and is at peace with a
        // foreign power (FreeCol Player.canTrade(type, CUSTOM_HOUSE): the boycott is ignored under either condition).
        // Hoisted ABOVE the per-good loop — it is a player-level fact, not a per-good one.
        bool mayTradeBoycotted = Ruleset.GameOptions.CustomIgnoreBoycott
            || CustomHouseTradesWithForeignCountries(owner.PlayerId);
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
            // Boycott gate (FreeCol canTrade(CUSTOM_HOUSE)): sell a boycotted good only when allowed (smuggling option
            // OR de Witt's foreign-trade ability), otherwise skip it entirely so End Turn never throws on it. Either way
            // the sell path is only entered when allowed.
            if (!mayTradeBoycotted && !owner.Market.CanTrade(goodsId))
            {
                continue;
            }
            int surplus = colony.StoreOf(goodsId) - setting.ExportLevel;
            if (surplus > 0)
            {
                int gold = SellColonyGoods(owner, colony, goodsId, surplus, ignoreBoycott: mayTradeBoycotted);
                if (IsHumanOwned(colony))
                {
                    // Transient player-facing notice (ADR-006): the HUD surfaces what each custom house sold after
                    // End Turn. Only the human's colonies are recorded — foreign powers still sell, silently.
                    _customHouseSaleNotices.Add(new CustomHouseSaleNotice(colony.Id, colony.Name, goodsId, surplus, gold));
                }
            }
        }
    }

    /// <summary>
    /// Each turn a settlement's alarm cools toward 0, <b>independently for every player it holds a channel toward</b>
    /// (FreeCol tension decay, <c>ServerPlayer</c>: −value/100 − 4 per player). The divisor/base are the
    /// data-overridable <see cref="Specification.NativeTensionOptions.DecayDivisor"/>/<see cref="Specification.NativeTensionOptions.DecayBase"/>
    /// (classic 100/4). RNG-free; <see cref="NativeSettlement.SetAlarm"/> drops any channel that reaches 0, so a
    /// human-only game decays exactly as before (only channel 0 exists). Refreshes most-hated once after.
    /// </summary>
    private void DecayNativeAlarm(NativeSettlement settlement)
    {
        NativeTensionOptions t = Ruleset.Difficulty.NativeTension;
        // Snapshot the channels first — SetAlarm mutates the backing map (and may drop a channel) mid-iteration.
        foreach ((int playerId, int alarm) in settlement.AlarmChannels.ToList())
        {
            settlement.SetAlarm(playerId, Math.Max(0, alarm - (alarm / t.DecayDivisor + t.DecayBase)));
        }
        UpdateMostHated(settlement);
    }

    /// <summary>Extra tiles beyond a settlement's own radius within which the human's presence stirs alarm (FreeCol <c>ALARM_RADIUS</c>).</summary>
    private const int NativeAlarmRadius = 2;

    /// <summary>Alarm a human-controlled/used tile contributes to a nearby settlement each turn (FreeCol <c>ALARM_TILE_IN_USE</c>).</summary>
    private const int AlarmTileInUse = 2;

    /// <summary>
    /// The per-turn alarm relief a resident mission grants its owner (FreeCol <c>GameOptions.MISSION_INFLUENCE</c>, the
    /// classic <c>model.option.missionInfluence</c> = −10), doubled for an expert/jesuit missionary. A <b>negative</b>
    /// delta (calming) applied each turn in <see cref="ApplyAmbientNativeAlarm"/>, distinct from the one-time install
    /// bonus <see cref="AlarmNewMissionary"/> (−100). We carry it as a named constant rather than routing the spec option
    /// (the option is not parsed into our <see cref="Specification.NativeTensionOptions"/>); a variant retunes it here.
    /// </summary>
    private const int MissionInfluence = -10;

    /// <summary>Chebyshev (king-move) distance between two tiles — the grid's surrounding-tiles metric.</summary>
    private static int ChebyshevDistance(Position a, Position b) =>
        Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    /// <summary>
    /// The per-turn <b>ambient</b> native alarm (FreeCol <c>ServerPlayer.csNewTurn</c>): each settlement resents the
    /// human's nearby footprint. Within <c>settlement radius + <see cref="NativeAlarmRadius"/></c> tiles, every human
    /// <b>colony</b> adds <see cref="AlarmTileInUse"/> + its population and every human <b>offensive land unit</b> adds
    /// its type offence; the total is damped by <b>Pocahontas</b>'s <c>nativeAlarmModifier</c> (−50%) — this is that
    /// modifier's faithful home. A settlement that holds the human's <b>resident mission</b> also gets a recurring
    /// <see cref="MissionInfluence"/> (−10, doubled for an expert/jesuit) <b>calming</b> each turn (FreeCol
    /// <c>ServerPlayer.java:1896-1906</c>); FreeCol folds that relief into the <em>same</em> per-settlement accumulator as
    /// the ambient pressure and scales the <b>net</b> by <c>NATIVE_ALARM_MODIFIER</c> once, so under Pocahontas/French the
    /// relief is damped too (−5 ordinary / −10 expert), not applied at full strength — we mirror that here (one
    /// <see cref="ChangeNativeAlarm(NativeSettlement, int)"/> on <c>ScaleNativeAlarmGain(pressure + relief)</c>), clamped at 0. Deterministic
    /// (no RNG; stable settlement/colony/unit iteration); runs in
    /// <see cref="EndTurn"/> just before the alarm decay. (Alarm is tracked toward the human only, so foreign powers
    /// exert none.)
    /// </summary>
    /// <remarks>
    /// <b>Tile-control branch — model-limitation deviation (FreeCol <c>ServerPlayer.java:1887-1892</c>).</b> FreeCol also
    /// adds <see cref="AlarmTileInUse"/> for a surrounding tile <em>claimed/worked by a European colony but holding no
    /// unit or colony</em> (a European work-radius claim). Our map model (<see cref="GameMap"/>) tracks only <b>native</b>
    /// tile ownership (<c>_nativeOwners</c>); there is no European colony work-radius claim, so this branch contributes
    /// nothing without a new ownership model — out of scope. We faithfully implement the colony, military-unit and
    /// missionary branches and document the tile-control branch as the deviation here. See [players] / [natives].
    /// </remarks>
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
            // Tile-control branch (ServerPlayer.java:1887-1892) is a no-op for us — no European work-radius claim model
            // exists (GameMap tracks only native tile ownership); documented as a deviation in the remarks above.

            // Per-turn missionary calming (ServerPlayer.java:1896-1906): a resident human mission eases the settlement's
            // alarm — MissionInfluence (−10), doubled for an expert/jesuit. FreeCol folds this into the SAME per-settlement
            // accumulator as the ambient pressure, then applies NATIVE_ALARM_MODIFIER to the NET once. So we compute the
            // net here (pressure + relief) and scale the whole thing — under Pocahontas/French the relief is halved too,
            // not applied at full strength (the old two-call path damped the gain but never the relief, calming 2× fast).
            int net = pressure;
            if (settlement.HasMission && settlement.MissionOwnerId == HumanAlarmChannel)
            {
                net += settlement.MissionIsExpert ? MissionInfluence * 2 : MissionInfluence;
            }
            if (net != 0)
            {
                ChangeNativeAlarm(settlement, ScaleNativeAlarmGain(net)); // Pocahontas/French −50% damps the whole net; clamps at 0
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
                    // Surface the human's first contact (FP-6a, FreeCol makeContact / FIRST_CONTACT message). Only a pair
                    // that includes the human produces a player-facing notice — two foreign powers meeting stay silent.
                    // The rival is whichever side is not the human. RNG-free; the presentation resolves the nation name.
                    if (a.PlayerId == _human.PlayerId || b.PlayerId == _human.PlayerId)
                    {
                        Player rival = a.PlayerId == _human.PlayerId ? b : a;
                        if (rival.NationId is { Length: > 0 } rivalNation)
                        {
                            _firstContactNotices.Add(new FirstContactNotice(rivalNation));
                        }
                    }
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
                    // Surface a turn-driven stance shift that involves the human (FP-6b — war→cease-fire→peace as tension
                    // cools, or a peace the rival breaks). Player-initiated changes go through SetStance from the
                    // diplomacy screen, never here, so this records only the automatic, tension-derived drift. RNG-free;
                    // the presentation resolves the nation name and phrasing.
                    if (a.PlayerId == _human.PlayerId || b.PlayerId == _human.PlayerId)
                    {
                        Player rival = a.PlayerId == _human.PlayerId ? b : a;
                        if (rival.NationId is { Length: > 0 } rivalNation)
                        {
                            _stanceChangeNotices.Add(new StanceChangeNotice(rivalNation, current, next));
                        }
                    }
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
            CheckDiscoverRegion(player, p); // a freshly-revealed tile may be the first sight of an undiscovered region
        }
    }

    /// <summary>
    /// Region discovery (FreeCol <c>ServerUnit.csCheckDiscoverRegion</c> + <c>ServerRegion.csDiscover</c>): when a
    /// colonial (European) <paramref name="player"/> reveals <paramref name="tile"/>, if that tile contributes to a
    /// still-undiscovered, discoverable region (its own or a discoverable Pacific parent, via
    /// <see cref="GameMap.DiscoverableRegionOf"/>), that player <b>discovers</b> it: the region is stamped with the
    /// discoverer, a deterministic <see cref="NameForRegion">name</see> and the discovery turn; and — for the human —
    /// a scored <see cref="HistoryEventKind.RegionDiscovered"/> event is recorded (the region's
    /// <see cref="Region.ScoreValue"/> feeds <see cref="PlayerScore"/>). A region is discovered at most once, by the
    /// first colonial player to reach it.
    ///
    /// <para><b>Faithful subset.</b> FreeCol awards the discovery score only when the <c>EXPLORATION_POINTS</c> game
    /// option is on; the classic ruleset enables it, so we always award it. FreeCol also names regions from per-nation
    /// name lists with a generic "{Nation} {Type} {n}" fallback; our naming uses that deterministic fallback only (the
    /// per-nation lists are not yet loaded) — RNG-free, so the default game stays byte-identical (ADR-009). The polar
    /// bands, the Atlantic and the ocean leaf quadrants are not discoverable; only land/mountain regions and the
    /// Pacific are (see <see cref="Region.IsDiscoverable"/>).</para>
    /// </summary>
    /// <param name="player">The colonial player whose fog just lifted over <paramref name="tile"/>.</param>
    /// <param name="tile">The freshly-revealed tile.</param>
    private void CheckDiscoverRegion(Player player, Position tile)
    {
        // Only European (colonial/rebel/independent) players discover regions; natives never do (FreeCol isEuropean()).
        if (player.PlayerType == PlayerType.Native)
        {
            return;
        }
        if (Map.DiscoverableRegionOf(tile) is not { } region)
        {
            return; // no discoverable region here (already discovered, or a polar/atlantic/lake/leaf tile)
        }
        string name = region.Name ?? NameForRegion(player, region);
        Map.UpdateRegion(region with
        {
            DiscoveredBy = player.PlayerId,
            Name = name,
            DiscoveredInTurn = Turn,
        });
        // The discovery score + history notice surface in the human's report only (the history log is human-only,
        // like every other RecordHistory call). A foreign power still claims the region (DiscoveredBy above) so the
        // human cannot re-discover it, but earns no entry in the human's history.
        if (player.IsHuman)
        {
            RecordHistory(
                HistoryEventKind.RegionDiscovered,
                $"Discovered {name}.",
                region.ScoreValue);
        }
    }

    /// <summary>
    /// A deterministic name for a newly-discovered region (FreeCol <c>NameCache.getRegionName</c>'s generic fallback):
    /// a fixed region keeps its predefined label (the Pacific → "Pacific Ocean"); a dynamic land/mountain region is
    /// named "{Nation} {Type} {n}" where <c>n</c> is the next unused ordinal for that (player, type) pair, scanning the
    /// names already handed out. RNG-free and order-stable, so it perturbs no RNG stream (ADR-009).
    /// </summary>
    private string NameForRegion(Player player, Region region)
    {
        // A fixed region (the Pacific) carries its own predefined label — use it verbatim rather than numbering.
        if (region.Key is not null)
        {
            return PredefinedRegionLabel(region.Key);
        }

        string nationPrefix = player.NationId is { Length: > 0 } ? NationDisplayName(player.PlayerId) + " " : "";
        string typeName = region.Type.ToString(); // "Land" / "Mountain"
        string prefix = nationPrefix + typeName + " ";

        // Next unused ordinal: the count of regions of this type this player has already named, plus one. Scanning the
        // table (rather than a counter) keeps the result a pure function of current state — stable across save/load.
        int next = 1 + Map.Regions.Count(r =>
            r.DiscoveredBy == player.PlayerId && r.Type == region.Type && r.Key is null);
        return prefix + next;
    }

    /// <summary>A human-readable label for a predefined region key (e.g. <c>model.region.pacific</c> → "Pacific Ocean").</summary>
    private static string PredefinedRegionLabel(string key) => key switch
    {
        "model.region.pacific" => "Pacific Ocean",
        "model.region.atlantic" => "Atlantic Ocean",
        "model.region.arctic" => "Arctic Ocean",
        "model.region.antarctic" => "Antarctic Ocean",
        _ => key[(key.LastIndexOf('.') + 1)..], // fall back to the short key part
    };

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

/// <summary>
/// Thrown when founding a colony on, or working, a <b>native-owned</b> tile is attempted without resolving the forced
/// buy-or-steal-or-abandon claim first (86d3e4bj7; FreeCol's pre-build <c>csClaimLand</c>). The presentation should
/// consult <see cref="Game.RequiredLandClaim(Position)"/> beforehand and, when a claim is required, raise its
/// pay/steal/abandon dialog and call the <see cref="LandClaimChoice"/> overload — this exception is the guard for when
/// it did not. Carries the buy price and owning nation so the dialog can be built from the caught instance if needed.
/// </summary>
public sealed class LandClaimRequiredException : Exception
{
    /// <summary>The gold the buy option costs (the land price; 0 under Peter Minuit).</summary>
    public int BuyPrice { get; }

    /// <summary>The native nation type id that owns the tile.</summary>
    public string OwningNation { get; }

    /// <summary>Creates the exception describing the forced claim the caller must resolve.</summary>
    public LandClaimRequiredException(int buyPrice, string owningNation)
        : base($"The natives own this land — buy it for {buyPrice} gold, steal it, or abandon the attempt.")
    {
        BuyPrice = buyPrice;
        OwningNation = owningNation;
    }
}

/// <summary>
/// A record of a unit lost to <b>attrition</b> — wasting away after too many turns standing in the open
/// wilderness — during the world-advance phase of <see cref="Game.EndTurn"/> (86d3drmzp; FreeCol
/// <c>ServerUnit.csNewTurn</c> + the <c>model.unit.attrition</c> UNIT_LOST message). The unit is removed inside the
/// per-turn attrition step with no return value the UI can read, so the game collects these notices and the
/// presentation surfaces them after the turn ("your X wasted away in the wilderness").
/// </summary>
/// <remarks>
/// Transient per-turn UI scratch: refreshed at the start of every attrition step and never saved or restored (no
/// save-format impact). Fields are raw ids/positions — formatting the English message is the presentation layer's
/// job (ADR-006).
/// </remarks>
/// <param name="OwnerId">The owning colonial player's id (0 = the human) of the unit that wasted away.</param>
/// <param name="UnitTypeId">The lost unit's type id (e.g. <c>model.unit.indianConvert</c>).</param>
/// <param name="Position">The open tile the unit wasted away on.</param>
public readonly record struct AttritionNotice(int OwnerId, string UnitTypeId, Position Position);
