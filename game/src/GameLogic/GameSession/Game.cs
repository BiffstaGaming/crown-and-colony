using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.Combat;
using CrownAndColony.GameLogic.Natives;
using CrownAndColony.GameLogic.Randomness;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Trade;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;

namespace CrownAndColony.GameLogic.GameSession;

/// <summary>
/// One running game: the map, the units, exploration state, and the turn counter.
/// All mutations of game state go through methods on this class so rules are
/// enforced in one place.
/// </summary>
public sealed class Game
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

    /// <summary>Colonists who consume no bell upkeep — the first two are free; each beyond eats 1 bell/turn (FreeCol <c>unitsThatUseNoBells</c>, 2 on all classic difficulties). A colony must staff its town hall to outpace this as it grows.</summary>
    private const int UnitsThatUseNoBells = 2;

    /// <summary>The terrain a ship sets sail to Europe from (the map's outer edge).</summary>
    private const string HighSeasId = "model.tile.highSeas";

    /// <summary>
    /// Liberty multiplier in the Founding Father cost formula. Classic <b>medium</b> difficulty = <b>40</b>
    /// (spec <c>model.option.foundingFatherFactor</c>) — the value the base game is balanced around, so fathers
    /// are a deliberate long-game investment rather than a snowball. (Difficulty-driven values, e.g. "other" = 24,
    /// arrive with the difficulty-options system.)
    /// </summary>
    public const int FoundingFatherFactor = 40;

    /// <summary>The warehouse goods id for religious crosses (immigration points).</summary>
    private const string CrossesId = "model.goods.crosses";

    /// <summary>Immigration points needed for the first emigrant (spec <c>model.option.initialImmigration</c>, classic 15).</summary>
    public const int InitialImmigration = 15;

    /// <summary>Added to the immigration target after each emigrant (spec <c>crossesIncrement</c>, classic medium 2).</summary>
    public const int CrossesIncrement = 2;

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

    /// <summary>Base recruit price rise per paid recruit (spec <c>recruitPriceIncrease</c>, classic medium 30).</summary>
    public const int RecruitPriceIncrease = 30;

    /// <summary>Recruit-price-floor rise per paid recruit (spec <c>lowerCapIncrease</c>, classic 0).</summary>
    public const int RecruitLowerCapIncrease = 0;

    /// <summary>
    /// RNG stream id for native settlement placement (ADR-009). A separate stream
    /// from the main game (stream 0) keeps placement deterministic without shifting
    /// the economy/father/immigration draws.
    /// </summary>
    private const ulong NativeStreamId = 1;

    /// <summary>RNG stream reserved for Lost City Rumour placement — a high id above every per-player stream
    /// (<c>Player.RngStreamId</c> = playerId + 1) so rumour scatter never correlates with or shifts another stream.</summary>
    private const ulong LcrStreamId = 100;

    private readonly List<Unit> _units = [];
    private readonly List<Colony> _colonies = [];
    private readonly List<NativeSettlement> _nativeSettlements = [];
    private readonly List<Player> _players = [];
    private readonly List<CombatNotice> _combatNotices = []; // transient: the most recent turn's AI-vs-human raids (not saved)
    private readonly List<ColonyLossNotice> _colonyLossNotices = []; // transient: the most recent turn's AI captures of human colonies (not saved)
    private readonly List<ColonyRaidNotice> _colonyRaidNotices = []; // transient: the most recent turn's native pillages of human colonies (not saved)
    private NativeDemand? _pendingDemand; // transient: a native tribute demand awaiting the human's accept/refuse (not saved)
    private readonly Player _human;
    private readonly Pcg32Random _random;
    private int _nextUnitId = 1;
    private int _nextColonyId = 1;
    private int _nextSettlementId = 1;
    private int _currentPlayerIndex; // whose turn it is in the ring (the human, index 0, between turns)

    private Game(Ruleset ruleset, GameMap map, Pcg32Random random, int turn, Player human)
    {
        Ruleset = ruleset;
        Map = map;
        _random = random;
        Turn = turn;
        _human = human;
        _players.Add(human);
    }

    /// <summary>The rule data this game plays by.</summary>
    public Ruleset Ruleset { get; }

    /// <summary>The game world.</summary>
    public GameMap Map { get; }

    /// <summary>Current turn number, starting at 1.</summary>
    public int Turn { get; private set; }

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
    /// Whether two units are combat/fog enemies — a different owner. The stance hook: diplomacy
    /// (<see cref="Stance"/>) is now <em>recorded</em> (FP-6a) but does not yet gate this — every distinct
    /// owner is still hostile for move/attack/fog legality; making this stance-aware (no attacking at peace)
    /// is FP-6b, which would change current behaviour and needs a playtest.
    /// </summary>
    private static bool AreEnemies(Unit a, Unit b) => !SameOwner(a, b);

    // ===== Diplomacy (FP-6a, ADR-019): colonial-player ↔ colonial-player stance + tension, RECORDED only.
    // Each player holds its own directional view (FreeCol Player.stance/tension maps). Natives stay on the
    // per-settlement alarm system; native player ids are silently ignored here. No path draws RNG.

    /// <summary>Maximum tension (FreeCol <c>Tension.Level.HATEFUL.limit + 100</c>); mirrors the native-alarm scale.</summary>
    internal const int MaxTension = 1100;

    /// <summary>Tension added to a colonial pair by an act of war — the FreeCol WAR modifier (<c>HATEFUL.limit</c>).</summary>
    internal const int TensionWar = 1000;

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

    /// <summary>Immigration points needed to produce the human player's next emigrant (rises by <see cref="CrossesIncrement"/> each time).</summary>
    public int ImmigrationRequired => _human.ImmigrationRequired;

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
    /// Game age (1–3) used to weight which fathers are offered. Simplified
    /// turn bands until the calendar exists; FreeCol keys age off the year.
    /// </summary>
    public int CurrentAge => Turn < 100 ? 1 : Turn < 200 ? 2 : 3;

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
        FoundingFatherCost(player.Congress.Count, FoundingFatherFactor);

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
    /// The native tribute demand currently awaiting the human's accept/refuse, or null if none. Transient per-turn
    /// UI state — created during a native turn (<see cref="RunNativeTurn"/>), answered via
    /// <see cref="AcceptPendingDemand"/>/<see cref="RefusePendingDemand"/>, and auto-refused at the next
    /// <see cref="EndTurn"/> if ignored; never saved.
    /// </summary>
    public NativeDemand? PendingDemand => _pendingDemand;

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

    /// <summary>Land-price factor (FreeCol <c>model.option.landPriceFactor</c>; <b>60</b> = the classic-medium tier
    /// this project standardises on, cf. the founding-father factor 40 decision). Difficulty options are not yet
    /// data-driven (the Ruleset parses no <c>&lt;difficulty&gt;</c> group), so this is hardcoded; revisit when they are.</summary>
    private const int LandPriceFactor = 60;

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
    /// <c>Player.getLandPrice</c>): <see cref="LandPriceFactor"/> × the tile's potential yield of every
    /// <em>non-food</em> good + <see cref="LandPriceBase"/>, then the player's <see cref="LandPaymentModifierId"/>
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
        int raw = (LandPriceFactor * Ruleset.GoodsTypes.Where(g => !g.IsFood).Sum(g => TileYieldPotential(tile, g.Id)))
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

    /// <summary>The King's cut to ship treasure to Europe (FreeCol <c>model.option.treasureTransportFee</c>; <b>60%</b>,
    /// classic-medium — the tier this project standardises on, like <see cref="LandPriceFactor"/>=60. Difficulty options
    /// are not yet data-driven, so this is hardcoded; revisit when they are).</summary>
    private const int TreasureTransportFeePercent = 60;

    /// <summary>The father modifier id scaling the transport fee — Hernán Cortés's −100% ships treasure for free.</summary>
    private const string TreasureTransportFeeModifierId = "model.modifier.treasureTransportFee";

    /// <summary>The King's fee to ship <paramref name="train"/>'s treasure to Europe: <see cref="TreasureTransportFeePercent"/>% of the amount, less Hernán Cortés's <c>treasureTransportFee</c> modifier (−100% → free).</summary>
    private int TransportFee(Player owner, Unit train) =>
        ApplyGoodsModifiers(owner, TreasureTransportFeeModifierId, TreasureTransportFeePercent * train.TreasureAmount / 100);

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
        if (settlement.HasBeenVisited)
        {
            return MoveCheck.No("You have already spoken with this settlement's chief.");
        }
        return MoveCheck.Yes(0);
    }

    /// <summary>
    /// Speaks with a settlement's chief (first contact): reveals the surrounding lands
    /// ("tales") and, unless the settlement is hateful, gives a small gold gift. Marks the
    /// settlement visited and ends the unit's turn. (Scout-specific outcomes — larger beads,
    /// learning by chance, danger — are a later slice.)
    /// </summary>
    /// <returns>The gold gifted (0 if the settlement gave none).</returns>
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckVisit"/>.</exception>
    public int Visit(Unit unit, NativeSettlement settlement) => Visit(_human, unit, settlement);

    /// <summary>Speaks with a settlement's chief on behalf of <paramref name="player"/> (the unit's owner).</summary>
    internal int Visit(Player player, Unit unit, NativeSettlement settlement)
    {
        MoveCheck check = CheckVisit(unit, settlement);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }

        settlement.HasBeenVisited = true;
        RevealAround(player, settlement.Position, TalesRevealRadius); // tales of nearby lands
        int gift = 0;
        if (settlement.AlarmLevel != AlarmLevel.Hateful)
        {
            gift = RandomFor(player).Next(GiftMinimum, GiftMaximum + 1); // the visitor's own stream (the human is 0)
            player.Gold += gift;
        }
        unit.MovementLeft = 0; // speaking ends the unit's turn
        return gift;
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
    /// 1st / 2nd / 3rd wanted good). Settlement goods stock — which lowers the price as they fill up — is
    /// not modelled yet, so the price assumes they still want it.
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
        return amount + (11 * perUnit * amount) / 10;
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

    /// <summary>The strongest enemy of <paramref name="attacker"/> standing on a tile, or null.</summary>
    private Unit? DefenderAt(Unit attacker, Position p) =>
        _units.Where(u => u.IsOnMap && AreEnemies(attacker, u) && u.Position == p)
            .OrderByDescending(DefenceBase)
            .FirstOrDefault();

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
    /// Whether <paramref name="attacker"/> may attack the strongest enemy on <paramref name="target"/> now.
    /// Native units may attack from slice 1b (the gate is gone), so this admits any owner-inequality enemy
    /// (<see cref="AreEnemies"/>) — restricting a brave to human targets is the native AI's job
    /// (<see cref="NearestHumanUnit"/>), not this legality check.
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

        // Naval combat (ship vs ship): no terrain bonus on water, a cargo penalty per hold slot, and the
        // defender may evade. A land unit can't stand on a water tile to defend, so a naval defender ⇒ ship-vs-ship.
        bool naval = defender.Type.IsNaval;
        // A unit defending in a colony uses the colony's defence (its fortification bonus), NOT the tile terrain —
        // FreeCol suppresses the terrain modifier inside a settlement (as our native-settlement assault already does).
        bool inColony = !naval && ColonyAt(target) is not null;
        bool attackerInColony = ColonyAt(attacker.Position) is not null;
        // Ambush (FreeCol canAmbush + getOffensiveModifiers): a native attacker striking in the open from — or at a
        // defender on — concealing forest/hills negates the defender's terrain cover by gaining it as offence. Both
        // units on a tile outside a settlement, the defender not dug in, the attacker native (the indian ambushBonus;
        // the REF ambushPenalty is P6). The bonus is the defender's own tile defence percentage.
        bool ambush = !naval && !inColony && !attackerInColony && attacker.IsNative && !defender.IsFortified
            && (Map.TerrainAt(attacker.Position).AmbushTerrain || Map.TerrainAt(target).AmbushTerrain);
        var attackContext = new AttackContext(
            Movement: MovementPenaltyFor(attacker), // snapshot the movement penalty before spending it
            // Artillery is brittle attacking IN THE OPEN (−75%): only when neither unit is in a settlement and the
            // gun isn't dug in (FreeCol getOffensiveModifiers). Battering a colony/garrison, it keeps full power.
            ArtilleryInOpen: !naval && attacker.Type.Bombard && !attackerInColony && !attacker.IsFortified && !inColony,
            AmbushBonus: ambush ? Map.TerrainAt(target).DefenceBonus : 0,
            GoodsCarried: naval ? GoodsSlotsUsed(attacker) : 0); // FreeCol cargo penalty is goods only, not passengers
        var defenceContext = new DefenceContext(
            TerrainDefenceBonus: (naval || inColony) ? 0 : Map.TerrainAt(target).DefenceBonus, // open water / a colony: no terrain bonus
            Fortified: !naval && defender.IsFortified, // a dug-in land defender resists at +50% (FreeCol FORTIFIED)
            SettlementDefenceBonus: naval ? 0 : ColonyDefenceBonusAt(target), // a unit defending in a fortified colony (stockade/fort/fortress)
            // Artillery caught defending in the open is brittle too (−75%, unless dug in); but behind a colony's
            // walls against a NATIVE raid it doubles (+100%, FreeCol ARTILLERY_AGAINST_RAID).
            ArtilleryInOpen: !naval && defender.Type.Bombard && !inColony && !defender.IsFortified,
            ArtilleryAgainstRaid: !naval && inColony && defender.Type.Bombard && attacker.IsNative,
            GoodsCarried: naval ? GoodsSlotsUsed(defender) : 0);

        double attackPower = CombatModel.AttackPower(OffenceBase(attacker), attackContext);
        double defencePower = CombatModel.DefencePower(DefenceBase(defender), defenceContext);

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
        double attackPower = CombatModel.AttackPower(OffenceBase(attacker), attackContext);
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
    /// buildings and stores transfer intact with the ownership change. Plunder gold and ships caught in a falling
    /// colony are later sub-slices.
    /// </summary>
    private void CaptureColony(Colony colony, int newOwnerId) => colony.OwnerId = newOwnerId;

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

        // 3. A capturable unit changes side (and may downgrade its type on capture).
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
    /// Starts a new game: generates a map from the seed and places one starting
    /// colonist on the first settleable land tile, revealing its surroundings.
    /// </summary>
    public static Game New(
        Ruleset ruleset, ulong seed, int mapWidth = 36, int mapHeight = 24,
        int startingGold = 0, int startingTax = 0)
    {
        var random = new Pcg32Random(seed);
        GameMap map = MapGenerator.Generate(ruleset, mapWidth, mapHeight, random);

        // The single human player (stream 0; foreign powers and natives become players in FP-3).
        var human = new Player(playerId: 0, nationId: null, isHuman: true, PlayerType.Colonial, new Market(ruleset))
        {
            Gold = startingGold,
            TaxRate = startingTax,
        };
        var game = new Game(ruleset, map, random, turn: 1, human);

        // Start on settleable land that has somewhere to walk to (not a 1-tile
        // islet), preferring temperate latitudes (nearest the equator row) over
        // a polar landfall.
        bool Settleable(Position p)
        {
            TerrainType t = map.TerrainAt(p);
            return !t.IsWater && t.CanSettle;
        }
        int equator = mapHeight / 2;
        Position start = map.AllPositions()
            .Where(Settleable)
            .OrderBy(p => Math.Abs(p.Y - equator))
            .FirstOrDefault(
                p => p.Neighbours().Any(n => map.InBounds(n) && !map.TerrainAt(n).IsWater),
                map.AllPositions().First(Settleable));
        game.SpawnUnit(ruleset.Unit(StartingUnitTypeId), start);

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

        game.SpawnRivalsAndNatives(ruleset, start); // foreign powers (landed) + native nations as players (FP-3b/FP-4)

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

    /// <summary>The number of foreign colonial powers spawned alongside the human (the classic four minus the human's slot).</summary>
    private const int ForeignPowerCount = 3;

    /// <summary>How far (Chebyshev) a foreign power lands from the human's start, so rivals stay outside the human's view.</summary>
    private const int ForeignLandingMinDistance = 6;

    /// <summary>Colonies a foreign power's AI founds before its remaining colonists explore instead (FP-4 minimal AI).</summary>
    private const int MaxAiColonies = 1;

    private static int Chebyshev(Position a, Position b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    /// <summary>
    /// Registers the native nations and the foreign colonial powers as players (ADR-019). Each distinct
    /// native nation present becomes a <see cref="PlayerType.Native"/> player (its units/settlements
    /// reference it by nation id; its braves act via <see cref="RunNativeTurn"/> from slice 1b); the foreign powers are the first
    /// <see cref="ForeignPowerCount"/> classic playable European nations, <b>landed on the map</b> far from
    /// the human (FP-4) with their starting units. Placement draws no RNG (the human's stream 0 stays
    /// byte-stable); player ids are allocated densely in a stable order (human 0, then natives, then powers).
    /// </summary>
    private void SpawnRivalsAndNatives(Ruleset ruleset, Position humanStart)
    {
        foreach (string nationType in _nativeSettlements.Select(s => s.NationTypeId).Distinct().OrderBy(n => n))
        {
            _players.Add(new Player(_players.Count, nationType, isHuman: false, PlayerType.Native, new Market(ruleset)));
        }

        var taken = new HashSet<Position>(); // tiles claimed by foreign landings (keeps powers apart)
        foreach (EuropeanNation nation in ruleset.EuropeanNations
                     .Where(n => n.Selectable && !n.IsRef).Take(ForeignPowerCount))
        {
            var power = new Player(_players.Count, nation.Id, isHuman: false, PlayerType.Colonial, new Market(ruleset));
            _players.Add(power);
            LandForeignPower(ruleset, power, nation, humanStart, taken);
        }
    }

    /// <summary>
    /// Lands a foreign power on the map far from the human (FP-4): its colonists on settleable land and its
    /// ship on adjacent water, around a deterministic anchor (the farthest free coastal tile from the human,
    /// away from other landings), revealing the power's own fog. A unit with no free on-map tile falls back
    /// to docking in Europe (robustness on small/crowded maps). Deterministic — draws no RNG.
    /// </summary>
    private void LandForeignPower(Ruleset ruleset, Player power, EuropeanNation nation, Position humanStart, HashSet<Position> taken)
    {
        bool FreeLand(Position p) => Map.InBounds(p) && Map.TerrainAt(p).CanSettle && !Map.TerrainAt(p).IsWater
            && ColonyAt(p) is null && NativeSettlementAt(p) is null
            && !_units.Any(u => u.IsOnMap && u.Position == p) && !taken.Contains(p);
        bool FreeWater(Position p) => Map.InBounds(p) && Map.TerrainAt(p).IsWater
            && !_units.Any(u => u.IsOnMap && u.Position == p) && !taken.Contains(p);
        Position? FirstFree(Position anchor, Func<Position, bool> free) =>
            free(anchor) ? anchor : anchor.Neighbours().Where(free).Cast<Position?>().FirstOrDefault();

        // A coastal land anchor as far from the human as possible (and from other powers' claimed tiles).
        Position? anchor = Map.AllPositions()
            .Where(p => FreeLand(p) && Chebyshev(p, humanStart) >= ForeignLandingMinDistance && p.Neighbours().Any(FreeWater))
            .OrderByDescending(p => Chebyshev(p, humanStart)).ThenBy(p => p.Y).ThenBy(p => p.X)
            .Cast<Position?>().FirstOrDefault();

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
            int repairTurns, UnitOrders orders, int treasureAmount)> units,
        IEnumerable<Colony>? colonies = null,
        IEnumerable<NativeSettlement>? nativeSettlements = null)
    {
        Player human = BuildPlayer(ruleset, players.Single(p => p.IsHuman), randomState);
        var game = new Game(ruleset, map, Pcg32Random.FromState(randomState), turn, human);
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
                  int repairTurns, UnitOrders orders, int treasureAmount) in units)
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
    /// Whether <paramref name="unit"/> may move to <paramref name="target"/> right now,
    /// and why not if not.
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

        // FreeCol's partial-movement rule (Unit.getMoveCost): when the terrain
        // costs more than the unit has left, the move is still allowed — for the
        // full remainder — only if the unit is near full movement (lost at most
        // 2/3 of a move) or the shortfall is small. (A settlement target also
        // qualifies; none exist yet.) Otherwise the unit must wait.
        int cost = terrain.MoveCost;
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

    /// <summary>Clears a unit's standing order back to active (it does not refund the spent movement).</summary>
    public void ClearOrders(Unit unit) => unit.Orders = UnitOrders.Active;

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
        var colony = new Colony(_nextColonyId++, name, unit.Position, population: 1, ownerId: unit.OwnerId);

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
        // The colony keeps its surroundings explored — for its owner (the human, or a foreign founder; FP-4).
        RevealAround(PlayerById(colony.OwnerId) ?? _human, colony.Position, ColonySightRadius);
        AutoAssignIdleToFood(colony);
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
        _units.Remove(unit);
        AutoAssignIdleToFood(colony);
    }

    /// <summary>Whether a colonist may be detached from <paramref name="colony"/> (it must keep at least one).</summary>
    public MoveCheck CheckLeaveColony(Colony colony) =>
        colony.Population > 1
            ? MoveCheck.Yes(0)
            : MoveCheck.No("A colony must keep at least one colonist.");

    /// <summary>
    /// Detaches a colonist from a colony onto the colony's own tile. Our colony model
    /// is a population count, so the detached unit is a generic free colonist (expert
    /// colonists are not tracked inside a colony).
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
        colony.Population--;
        TrimAssignments(colony); // the lost colonist vacates a job if every colonist was working
        var unit = new Unit(_nextUnitId++, Ruleset.Unit(StartingUnitTypeId), colony.Position)
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
    /// Abandons a colony: it is removed from the map and its last colonist walks out as a free colonist on the
    /// colony's tile (FreeCol <c>abandonSettlement</c>).
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
        var unit = new Unit(_nextUnitId++, Ruleset.Unit(StartingUnitTypeId), colony.Position)
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
        colony.SetBuildingWorkers(buildingId, colony.BuildingWorkers.GetValueOrDefault(buildingId) + 1);
    }

    /// <summary>Returns one of a building's workers to the idle pool.</summary>
    public void UnassignBuildingWork(Colony colony, string buildingId) =>
        colony.SetBuildingWorkers(buildingId, colony.BuildingWorkers.GetValueOrDefault(buildingId) - 1);

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
        if (colony.StoreOf(goodsId) < amount)
        {
            throw new InvalidMoveException($"The colony does not have {amount} {goodsId} to sell.");
        }

        colony.AddGoods(goodsId, -amount);
        SaleResult sale = player.Market.Sell(goodsId, amount, player.TaxRate);
        player.Gold += sale.GoldAfterTax;
        return sale.GoldAfterTax;
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

    /// <summary>Loads goods from a colony's warehouse into an adjacent ship's hold.</summary>
    /// <exception cref="InvalidMoveException">The ship isn't adjacent on the map, or the colony lacks the goods.</exception>
    public void LoadFromColony(Unit ship, Colony colony, string goodsId, int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);
        if (!ship.Type.IsNaval || !ship.IsOnMap)
        {
            throw new InvalidMoveException("Only a ship on the map can carry cargo.");
        }
        if (!ship.Position.IsAdjacentTo(colony.Position) && ship.Position != colony.Position)
        {
            throw new InvalidMoveException("The ship must be next to the colony to load cargo.");
        }
        if (colony.StoreOf(goodsId) < amount)
        {
            throw new InvalidMoveException($"The colony does not have {amount} {goodsId}.");
        }
        if (ExtraGoodsSlots(ship, goodsId, amount) > CargoSlotsFree(ship))
        {
            throw new InvalidMoveException("The ship has no room for that cargo.");
        }
        colony.AddGoods(goodsId, -amount);
        ship.AddCargo(goodsId, amount);
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
        if (ship.CargoOf(goodsId) < amount)
        {
            throw new InvalidMoveException($"The ship is not carrying {amount} {goodsId}.");
        }
        ship.AddCargo(goodsId, -amount);
        SaleResult sale = player.Market.Sell(goodsId, amount, player.TaxRate);
        player.Gold += sale.GoldAfterTax;
        return sale.GoldAfterTax;
    }

    /// <summary>
    /// Whether the player can buy <paramref name="amount"/> of a good in Europe for
    /// the docked <paramref name="ship"/> (no market price rise on buying, per FreeCol).
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
        int cost = player.Market.AskPrice(goodsId) * amount;
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

    /// <summary>Buys goods in Europe into a docked ship's hold, debiting the treasury at the ask price.</summary>
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
        player.Gold -= check.Cost;
        ship.AddCargo(goodsId, amount);
        return check.Cost;
    }

    /// <summary>
    /// A high-seas tile a ship bought in Europe enters the New World at (the map's
    /// entry edge). Falls back to (0,0) on maps with no high seas (test fixtures).
    /// </summary>
    private Position EuropeEntryTile() =>
        Map.AllPositions().FirstOrDefault(p => Map.TerrainAt(p).Id == HighSeasId, new Position(0, 0));

    /// <summary>Whether the player can buy a <paramref name="unitTypeId"/> in Europe right now.</summary>
    public MoveCheck CheckBuyUnit(string unitTypeId) => CheckBuyUnit(_human, unitTypeId);

    /// <summary>Whether <paramref name="player"/> can buy a <paramref name="unitTypeId"/> in Europe right now.</summary>
    internal MoveCheck CheckBuyUnit(Player player, string unitTypeId)
    {
        UnitType type = Ruleset.Unit(unitTypeId);
        if (!type.IsPurchasable)
        {
            return MoveCheck.No($"A {type.ShortName} cannot be bought in Europe.");
        }
        if (player.Gold < type.Price)
        {
            return MoveCheck.No($"Not enough gold (need {type.Price}).");
        }
        return MoveCheck.Yes(type.Price);
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
        var unit = new Unit(_nextUnitId++, type, type.IsNaval ? EuropeEntryTile() : new Position(0, 0))
        {
            Location = UnitLocation.InEurope,
            OwnerId = player.PlayerId, // the bought unit belongs to its buyer (the human is 0; a foreign power its own id)
        };
        _units.Add(unit);
        return unit;
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
    }

    /// <summary>Bad-outcome chance, classic <b>medium</b> difficulty (<c>model.option.badRumour</c>); see the difficulty note below.</summary>
    private const int RumourBadPercent = 23;

    /// <summary>Good-outcome chance, classic <b>medium</b> difficulty (<c>model.option.goodRumour</c>).</summary>
    private const int RumourGoodPercent = 48;

    /// <summary>
    /// FreeCol's <c>dx = 10 − rumourDifficulty</c> reward-scale: classic <b>medium</b> sets
    /// <c>model.option.rumourDifficulty=2</c>, so <c>dx = 8</c>. We hardcode the <b>medium</b> tier to match the
    /// rest of the project's classic baseline (the founding-father factor is likewise pinned to medium = 40);
    /// the Ruleset does not parse difficulty options yet, so a difficulty-driven value is a later refinement.
    /// </summary>
    private const int RumourDifficultyDx = 8;

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
        if (PlayerById(unit.OwnerId) is { } owner)
        {
            ExploreRumour(unit, target, RandomFor(owner));
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
        LostCityRumourType outcome = ChooseRumourType(unit, random);
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
                GenerateFountainRecruits(unit.OwnerId, random); // a burst of dx immigrants arrives on the owner's Europe dock
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
            case LostCityRumourType.Nothing:
            default:
                break; // no effect (the player-facing message is presentation)
        }
        Map.RemoveRumour(target); // consumed regardless of outcome
        return outcome;
    }

    /// <summary>Musters a treasure train on <paramref name="target"/> carrying <paramref name="amount"/> gold, owned by <paramref name="ownerId"/> (FreeCol spawns a treasure train for a rich plunder/find — see [treasure-train.md]).</summary>
    private void SpawnTreasureTrain(Position target, int ownerId, int amount) =>
        SpawnUnit(Ruleset.Unit(TreasureTrainUnitTypeId), target, ownerId).SetTreasureAmount(amount);

    /// <summary>
    /// A Fountain of Youth: lands <see cref="RumourDifficultyDx"/> fresh immigrants on the owner's Europe dock
    /// (FreeCol <c>ServerEurope.generateFountainRecruits(dx)</c>). Each is an independent weighted recruit draw
    /// (<see cref="DrawRecruitType(Player, IGameRandom)"/> → <see cref="CreateEuropeRecruit"/> off <paramref name="random"/>,
    /// the exploring unit's owner stream) — they arrive as units <em>in Europe</em>, not as the three dock
    /// <em>candidates</em>, so the player still ships them over. FreeCol lets the human pick each immigrant; we
    /// generate them directly (its AI path) until a select-recruit flow exists. A no-op for a player with no
    /// recruitable unit types (minimal rulesets).
    /// </summary>
    private void GenerateFountainRecruits(int ownerId, IGameRandom random)
    {
        if (PlayerById(ownerId) is not { } owner || !Ruleset.UnitTypes.Any(t => IsRecruitable(owner, t)))
        {
            return;
        }
        for (int i = 0; i < RumourDifficultyDx; i++)
        {
            CreateEuropeRecruit(owner, DrawRecruitType(owner, random));
        }
    }

    /// <summary>
    /// Picks a rumour type by the FreeCol <c>LostCityRumour.chooseType</c> weighted split (good / bad / neutral),
    /// restricted to the outcomes shipped so far. Good outcomes are weighted ×<see cref="RumourGoodPercent"/>:
    /// FOUNTAIN_OF_YOUTH (2, always available to a colonial explorer); then a learnable unit LEARN (30) /
    /// TRIBAL_CHIEF (30) / COLONIST (20), a non-learnable one TRIBAL_CHIEF (50) / COLONIST (30); then the treasure
    /// finds RUINS (6) and CIBOLA (4) for any explorer. The bad side (EXPEDITION_VANISHES) carries FreeCol's
    /// normalised weight of 100 (the burial ground needs native tile ownership, not modelled yet). NOTHING takes
    /// the neutral remainder ×100. MOUNDS (8), the seasoned-scout exploration bonus and Hernando de Soto's
    /// always-positive ability are later refinements.
    /// </summary>
    private LostCityRumourType ChooseRumourType(Unit unit, IGameRandom random)
    {
        bool canLearn = Ruleset.GetUnitChange(UnitChangeTypeIds.LostCity, unit.Type.Id) is not null;
        int neutral = Math.Max(0, 100 - RumourBadPercent - RumourGoodPercent);

        var choices = new List<(LostCityRumourType Type, int Weight)>();
        if (RumourGoodPercent > 0)
        {
            // Fountain of Youth (weight 2, listed first to mirror FreeCol's chooseType). FreeCol allowFoY is
            // "owner is COLONIAL"; today every explorer here is colonial (natives are excluded upstream, and the
            // only player types are colonial + native), so it is unconditional. When the REF lands (P6) — European
            // but not colonial — this must gate on a colonial owner (an IsColonialPlayer(unit.OwnerId) check).
            choices.Add((LostCityRumourType.FountainOfYouth, 2 * RumourGoodPercent));
            if (canLearn)
            {
                choices.Add((LostCityRumourType.Learn, 30 * RumourGoodPercent));
                choices.Add((LostCityRumourType.TribalChief, 30 * RumourGoodPercent));
                choices.Add((LostCityRumourType.Colonist, 20 * RumourGoodPercent));
            }
            else
            {
                choices.Add((LostCityRumourType.TribalChief, 50 * RumourGoodPercent));
                choices.Add((LostCityRumourType.Colonist, 30 * RumourGoodPercent));
            }
            // The treasure finds are available to any explorer (FreeCol adds them outside the learn split): ancient
            // RUINS (6) and a city of gold CIBOLA (4). MOUNDS (8) stays deferred to the strange-mounds slice.
            choices.Add((LostCityRumourType.Ruins, 6 * RumourGoodPercent));
            choices.Add((LostCityRumourType.Cibola, 4 * RumourGoodPercent));
        }
        if (RumourBadPercent > 0)
        {
            // FreeCol normalises the bad sub-list to a total weight of 100; with only the vanishing expedition
            // available (no native-owned tiles → no burial ground) that single entry takes the whole 100.
            choices.Add((LostCityRumourType.ExpeditionVanishes, 100));
        }
        if (neutral > 0)
        {
            choices.Add((LostCityRumourType.Nothing, 100 * neutral));
        }

        return WeightedPick(choices, random);
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
    /// Resolves a construction id to a building or a buildable <b>land</b> unit; <c>null</c> when the id is
    /// neither (unknown, a non-buildable type, or a ship — naval construction needs a shipyard's water-scoped
    /// build ability and a water berth, deferred, so ships are excluded here to keep the colony tile valid).
    /// </summary>
    private Buildable? ResolveBuildable(string id)
    {
        if (Ruleset.FindBuilding(id) is { } b)
        {
            return new Buildable(b.Id, b.ShortName, b.BuildCost, b.RequiredPopulation, b.RequiredAbilitiesOrEmpty, IsUnit: false, b.UpgradesFrom, Unit: null);
        }
        if (Ruleset.FindBuildableUnit(id) is { IsNaval: false } u)
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
    /// </summary>
    private bool ColonyCanBuildUnit(Colony colony, UnitType unit) =>
        colony.Buildings.Select(Ruleset.Building)
            .Any(b => b.BuildableUnitTypeIdsOrEmpty.Contains(unit.Id) || (unit.IsNaval && b.BuildsNavalUnits));

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

    /// <summary>Building types the colony could start constructing right now.</summary>
    public IEnumerable<BuildingType> Buildables(Colony colony) =>
        Ruleset.BuildingTypes.Where(b => CheckSetBuild(colony, b.Id).Allowed);

    /// <summary>Land unit types the colony could start constructing right now (artillery, wagon train).</summary>
    public IEnumerable<UnitType> BuildableUnits(Colony colony) =>
        Ruleset.BuildableUnitTypes.Where(u => !u.IsNaval && CheckSetBuild(colony, u.Id).Allowed);

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
                SpawnUnit(Ruleset.Unit(target.Id), colony.Position, colony.OwnerId); // a built unit musters at the colony
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
        RefusePendingDemand();      // a tribute demand the human ended the turn without answering counts as a refusal (FreeCol session timeout = reject)
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
        ApplyAmbientNativeAlarm();   // natives resent the human's nearby colonies/troops (FreeCol csNewTurn) — before the calm-down
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
        if (player.PlayerType != PlayerType.Colonial)
        {
            return; // future-proofing: any PlayerType that is neither Native nor Colonial takes no turn
        }

        foreach (Colony colony in ColoniesOf(player))
        {
            RunColonyTurn(player, colony);
        }
        AccumulateLibertyAndElectFathers(player);
        ApplyFreeBuildings(player); // La Salle: a free stockade in each colony that has reached the required population
        AccumulateImmigrationAndEmigrate(player);

        if (!player.IsHuman)
        {
            RunForeignPowerEconomy(player); // FP-5: pursue a father, sell surplus, recruit (own stream/market)
            RunForeignPowerTurn(player);     // FP-4: move / explore / found
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
                int surplus = colony.StoreOf(goodsId) - AiTradeReserve;
                if (surplus > 0)
                {
                    SellColonyGoods(power, colony, goodsId, surplus);
                }
            }
        }

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
        bool atWarWithHuman = StanceBetween(power.PlayerId, _human.PlayerId) == Stance.War;

        // Snapshot the owned units (founding/combat removes a unit from _units mid-loop).
        foreach (Unit unit in _units.Where(u => IsOwnedBy(u, power)).OrderBy(u => u.Id).ToList())
        {
            if (!unit.IsOnMap)
            {
                continue; // units still in Europe wait (a warship can now act below; an unarmed ship falls through to idle)
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
                if (NearestHumanUnit(unit) is { } prey)
                {
                    if (unit.Position.IsAdjacentTo(prey.Position) && CheckAttack(unit, prey.Position).Allowed)
                    {
                        AttackHumanUnit(power, unit, prey.Position);
                    }
                    else if (StepToward(power, unit, prey.Position) is { } chase)
                    {
                        MoveUnit(unit, chase); // a hemmed-in attacker simply waits
                    }
                    continue;
                }
                // No field unit to chase: a land unit marches on the nearest human colony (the war objective) so it
                // besieges instead of wandering off to explore (86d3bx03d) — closing on an undefended colony to
                // capture above, or on a garrison to fight as a field unit. Naval units can't besiege a colony.
                if (!unit.Type.IsNaval && NearestHumanColony(unit) is { } targetColony
                    && StepToward(power, unit, targetColony.Position) is { } colonyStep)
                {
                    MoveUnit(unit, colonyStep);
                    continue;
                }
            }

            if (!unit.Type.CanFoundColony)
            {
                continue; // non-founders (e.g. an idle soldier at peace) wait
            }
            if (ColoniesOf(power).Count() < MaxAiColonies && CheckFoundColony(unit).Allowed)
            {
                FoundColony(unit);
                continue;
            }
            if (StepTowardNearestUnexplored(power, unit) is { } step)
            {
                MoveUnit(unit, step);
            }
        }
    }

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

    /// <summary>
    /// Native tribute-demand difficulty: FreeCol's <c>model.option.nativeDemands</c> at the default "medium" level
    /// (<c>specification.xml</c> <c>model.difficulty.medium</c> → 2). Drives the demand amount (dx = difficulty + 1)
    /// and the alarm relief on accept. A fixed constant for now — we don't model difficulty levels yet (ADR-018
    /// ruleset-constant debt, task 86d3bb1x3).
    /// </summary>
    private const int NativeDemandsDifficulty = 2;

    /// <summary>Demand-amount multiplier: dx = difficulty + 1 (FreeCol <c>IndianDemandMission.capAmount</c>; 3 at medium).</summary>
    private const int NativeDemandsDx = NativeDemandsDifficulty + 1;

    /// <summary>Minimum goods a demand asks for (FreeCol <c>GOODS_DEMAND_MIN</c>).</summary>
    private const int NativeDemandMin = 30;

    /// <summary>Maximum goods a demand asks for — one cargo load (FreeCol <c>GoodsContainer.CARGO_SIZE</c>).</summary>
    private const int NativeDemandMax = 100;

    /// <summary>Alarm relief across the demanding nation's settlements when a demand is paid (FreeCol <c>-(5 - difficulty) * 50</c> = 150 at medium).</summary>
    private const int NativeDemandAcceptAlarmRelief = (5 - NativeDemandsDifficulty) * 50;

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
        // Snapshot: a raid can remove the prey (or, on a loss, the brave itself) from _units mid-loop.
        foreach (Unit brave in _units.Where(u => u.OwnerNationId == player.NationId).OrderBy(u => u.Id).ToList())
        {
            if (!brave.IsOnMap || brave.MovementLeft <= 0)
            {
                continue;
            }

            bool hostile = HomeSettlement(player, brave) is { } home && home.AlarmLevel >= RaidAlarmThreshold;
            if (hostile && AdjacentPillageableHumanColony(brave) is { } colonyTile)
            {
                PillageColony(brave, colonyTile, RandomFor(player)); // storm an undefended human colony (own stream)
            }
            else if (hostile && _pendingDemand is null && AdjacentDemandableHumanColony(player, brave) is { } demandColony)
            {
                // Can't simply storm it (defended, or only gold/food to take) → shake it down for tribute instead.
                CreateNativeDemand(player, brave, demandColony);
            }
            else if (hostile && NearestHumanUnit(brave) is { } prey)
            {
                if (brave.Position.IsAdjacentTo(prey.Position) && CheckAttack(brave, prey.Position).Allowed)
                {
                    RaidHumanUnit(player, brave, prey.Position);
                }
                else if (StepToward(player, brave, prey.Position) is { } step)
                {
                    MoveUnit(brave, step); // hemmed-in hostile braves simply wait (no fallback wander)
                }
            }
            else if (Wander(player, brave) is { } wanderStep)
            {
                MoveUnit(brave, wanderStep);
            }
        }
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
    private static int CapDemand(int count) => Math.Clamp(count * NativeDemandsDx / 6, NativeDemandMin, NativeDemandMax);

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

    /// <summary>
    /// The nearest on-map human-owned unit the <paramref name="hunter"/> can fight (Chebyshev, ties broken by
    /// position), or null if none. Two filters: (1) human-owned — the <b>sole contract</b> keeping the AI
    /// attacking the human only (the engine's <see cref="CheckAttack"/>/<see cref="DefenderAt"/> gate on
    /// owner-inequality would also admit other rivals); (2) <b>same domain</b> — a ship hunts ships, a land unit
    /// hunts land units (naval and land combat don't mix), so a warship never chases an unreachable land unit and
    /// a brave never chases a ship.
    /// </summary>
    private Unit? NearestHumanUnit(Unit hunter) =>
        _units.Where(u => u.IsOnMap && IsHumanOwned(u) && u.Type.IsNaval == hunter.Type.IsNaval)
            .OrderBy(u => Chebyshev(u.Position, hunter.Position))
            .ThenBy(u => u.Position.Y).ThenBy(u => u.Position.X)
            .FirstOrDefault();

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
                // The colony's printing press / newspaper boosts its bell output first (+50% / +100%), then the
                // founding-father bonus (Jefferson/Paine) applies, then each colonist past the first two consumes
                // 1 bell of upkeep — so a colony that grows faster than its bell output loses liberty (its Sons of
                // Liberty can fall). FreeCol feeds the same net figure to both pools.
                int boosted = bells + (bells * BellProductionBonus(colony) / 100); // printing press / newspaper
                int net = ApplyGoodsModifiers(player, BellsId, boosted) - Math.Max(0, colony.Population - UnitsThatUseNoBells);
                player.Liberty += net;   // banked toward the next founding father
                colony.AddLiberty(net);  // the colony's own Sons-of-Liberty liberty (AddLiberty floors at 0)
            }
        }
        player.Liberty = Math.Max(0, player.Liberty); // a net-negative bell turn can't push the founding-father pool below 0

        if (player.CurrentFather is not null && player.Liberty >= TotalFoundingFatherCost(player))
        {
            string elected = player.CurrentFather; // capture before it is cleared
            player.Liberty -= TotalFoundingFatherCost(player);
            player.CongressList.Add(elected);
            player.CurrentFather = null;
            player.OfferedFathersList.Clear();
            RefreshDockForRecruitability(player); // a newly-elected father may ban dock recruits (Brewster)
            RefreshSonsOfLibertyModifiers();       // a newly-elected father may grant a standing SoL bonus (Bolívar +20)
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
    /// Scales a positive native-alarm <em>gain</em> by the human's <see cref="NativeAlarmModifierId"/> modifier
    /// (Pocahontas −50%, read from the spec via the elected father). Gains only — goodwill and decay (negative
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
        FatherModifier? modifier = _human.Congress.Select(Ruleset.Father)
            .SelectMany(f => f.Modifiers)
            .FirstOrDefault(m => m.TargetId == NativeAlarmModifierId);
        return modifier is null ? delta : (int)modifier.ApplyTo(delta);
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

    /// <summary>Replaces any dock recruit a newly-elected father now forbids (e.g. Brewster's scum ban).</summary>
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

    /// <summary>
    /// Accrues immigration and emigrates while the target is met (FreeCol
    /// <c>Player.getTotalImmigrationProduction</c> + the auto-emigrate loop in
    /// <c>ServerPlayer.csNewTurn</c>): colony crosses drain into the pool (like
    /// bells → liberty), the Europe contribution (−4 per person there, +2 player
    /// bonus, never dropping the turn's total below zero) is added, then each time
    /// the pool meets the target an emigrant arrives in Europe from a random dock slot.
    /// </summary>
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

        // Auto-emigrate (no William Brewster / select-recruit yet → a random dock slot).
        // Guarded on a stocked dock: test rulesets with no recruitable units have none.
        while (player.RecruitDock.Count > 0 && player.Immigration >= player.ImmigrationRequired)
        {
            Emigrate(player, RandomFor(player).Next(player.RecruitDock.Count));
            ReduceImmigration(player);
            player.ImmigrationRequired += CrossesIncrement;
        }
    }

    /// <summary>
    /// Consumes immigration on emigration (FreeCol <c>Player.reduceImmigration</c> with
    /// classic <c>saveProductionOverflow=true</c>): subtract the target, keeping any surplus.
    /// </summary>
    private static void ReduceImmigration(Player player) =>
        player.Immigration = player.ImmigrationRequired > player.Immigration ? 0 : player.Immigration - player.ImmigrationRequired;

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
        player.BaseRecruitPrice += RecruitPriceIncrease;    // increaseRecruitmentDifficulty
        player.RecruitLowerCap += RecruitLowerCapIncrease;
        Unit recruit = Emigrate(player, slot);              // extract precedes the immigration cut (as in FreeCol)
        ReduceImmigration(player);
        player.ImmigrationRequired += CrossesIncrement;
        return recruit;
    }

    /// <summary>
    /// The yield of one goods type when a colonist works a tile, with the <em>human</em> player's
    /// Founding-Father goods modifiers (presentation/test helper). The colony-turn rules use the
    /// <see cref="TileYield(Player, Position, string)"/> overload so a foreign power's tiles fold its own fathers.
    /// </summary>
    public int TileYield(Position tile, string goodsId) => TileYield(_human, tile, goodsId);

    /// <summary>
    /// The yield of one goods type when a colonist of <paramref name="player"/> works a tile: the terrain's
    /// best attended output, then any bonus-resource boost on the tile, then that player's Founding-Father
    /// goods modifiers (e.g. Henry Hudson's +100% furs). 0 when the terrain can't produce the goods at all
    /// (a resource never enables a new good).
    /// </summary>
    internal int TileYield(Player player, Position tile, string goodsId) =>
        ApplyGoodsModifiers(player, goodsId, TileYieldPotential(tile, goodsId)); // father modifiers stack on the potential

    /// <summary>
    /// The tile's <b>potential</b> yield of one goods type — the terrain's best attended output plus any on-tile
    /// bonus-resource boost, but <em>without</em> any player's Founding-Father goods modifiers (FreeCol
    /// <c>Tile.getPotentialProduction</c> with a null owner). This is the player-independent figure native land is
    /// valued from (see <see cref="LandPrice(Player, Position)"/>); <see cref="TileYield(Player, Position, string)"/>
    /// folds the acting player's fathers on top of it for actual colony production. 0 when the terrain can't make it.
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

        return (int)yield;
    }

    /// <summary>
    /// Whether a colonist of <paramref name="colony"/> may be put to work on
    /// <paramref name="tile"/> producing <paramref name="goodsId"/>.
    /// </summary>
    public MoveCheck CheckAssignWork(Colony colony, Position tile, string goodsId)
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
        int yield = TileYield(tile, goodsId);
        if (yield <= 0)
        {
            return MoveCheck.No($"That tile cannot produce {goodsId[(goodsId.LastIndexOf('.') + 1)..]}.");
        }
        return MoveCheck.Yes(yield);
    }

    /// <summary>Puts an idle colonist to work on a tile producing one goods type.</summary>
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckAssignWork"/>.</exception>
    public void AssignWork(Colony colony, Position tile, string goodsId)
    {
        MoveCheck check = CheckAssignWork(colony, tile, goodsId);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }
        colony.SetWorker(tile, goodsId);
    }

    /// <summary>Returns a tile's worker to the idle pool.</summary>
    public void UnassignWork(Colony colony, Position tile) => colony.RemoveWorker(tile);

    /// <summary>
    /// The goods a colonist could produce by working <paramref name="tile"/> — its terrain's <b>attended</b>
    /// production outputs that currently yield more than 0 (so a colony's surrounding forest offers lumber/furs/grain,
    /// hills offer ore, plains offer grain/cotton, …), each paired with its yield. Sorted by yield (descending) then
    /// goods id, for a stable, useful order in the colony screen's tile-work picker. Empty for water / off-map /
    /// barren tiles. A rules query (ADR-006) — the presentation renders these options and calls <see cref="AssignWork"/>.
    /// </summary>
    public IReadOnlyList<(string GoodsId, int Yield)> TileWorkOptions(Position tile)
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
            .Select(id => (GoodsId: id, Yield: TileYield(tile, id)))
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
    public void AutoAssignIdleToFood(Colony colony)
    {
        const string grain = "model.goods.grain";
        while (colony.IdleColonists > 0)
        {
            var best = colony.Position.Neighbours()
                .Where(n => Map.InBounds(n) && !colony.TileWorkers.ContainsKey(n))
                .Select(n => (tile: n, yield: TileYield(n, grain)))
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
            colony.SetWorker(best.Value.tile, grain);
        }
    }

    /// <summary>
    /// One building's turn: unattended output plus per-worker conversion of
    /// warehouse inputs to outputs (scaled down when inputs run short).
    /// </summary>
    private void RunBuildingProduction(Colony colony, BuildingType building)
    {
        int workers = colony.BuildingWorkers.GetValueOrDefault(building.Id);
        foreach (ProductionEntry entry in building.Productions)
        {
            int multiplier = entry.Unattended ? 1 : workers;
            if (multiplier == 0)
            {
                continue;
            }

            // Breeding gate (FreeCol autoProduction): goods with a breeding
            // number (horses) only multiply when enough are already stabled.
            bool breedingBlocked = entry.Outputs.Any(o =>
                Ruleset.GoodsTypes.FirstOrDefault(g => g.Id == o.GoodsId)?.BreedingNumber
                    is int needed && colony.StoreOf(Ruleset.StorageIdOf(o.GoodsId)) < needed);
            if (breedingBlocked)
            {
                continue;
            }

            // Scale by the scarcest input (classic conversions are 1:1, but the
            // ratio is honoured generically).
            double fraction = 1.0;
            foreach (GoodsOutput input in entry.Inputs)
            {
                int wanted = input.Amount * multiplier;
                int available = colony.StoreOf(Ruleset.StorageIdOf(input.GoodsId));
                fraction = Math.Min(fraction, wanted == 0 ? 1.0 : Math.Min(1.0, available / (double)wanted));
            }

            foreach (GoodsOutput input in entry.Inputs)
            {
                colony.AddGoods(
                    Ruleset.StorageIdOf(input.GoodsId),
                    -(int)Math.Floor(input.Amount * multiplier * fraction));
            }
            foreach (GoodsOutput output in entry.Outputs)
            {
                int amount = (int)Math.Floor(output.Amount * multiplier * fraction);
                if (!entry.Unattended)
                {
                    // Sons-of-Liberty production bonus: +bonus per attended worker (multiplier == workers here),
                    // floored at 0 so a penalty can't make a building produce negative. The unattended town-hall
                    // bell entry is excluded (FreeCol bonuses only worker production).
                    amount = Math.Max(0, amount + workers * colony.ProductionBonus);
                }
                colony.AddGoods(Ruleset.StorageIdOf(output.GoodsId), amount);
            }
        }
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
                return;
            }
        }
    }

    /// <summary>One colony's production-eat-grow step (its <paramref name="owner"/>'s fathers fold into tile yields).</summary>
    private void RunColonyTurn(Player owner, Colony colony)
    {
        // 1a. The colony square works itself (unattended yield). Goods enter
        //     the warehouse under their stored-as id: grain/fish → food.
        TerrainType terrain = Map.TerrainAt(colony.Position);
        foreach (ProductionEntry entry in terrain.Productions.Where(p => p.Unattended))
        {
            foreach (GoodsOutput output in entry.Outputs)
            {
                colony.AddGoods(Ruleset.StorageIdOf(output.GoodsId), output.Amount);
            }
        }

        // 1b. Worked tiles produce their assigned goods, each worker getting the colony's Sons-of-Liberty
        //     production bonus (+2/+1/0/−1/−2 per worker, floored at 0 so a bad-government penalty can't go negative).
        foreach ((Position tile, string goodsId) in colony.TileWorkers)
        {
            colony.AddGoods(Ruleset.StorageIdOf(goodsId), Math.Max(0, TileYield(owner, tile, goodsId) + colony.ProductionBonus));
        }

        // 1c. Buildings produce: unattended entries always run (town hall bell);
        //     worker entries convert inputs to outputs per colonist, limited by
        //     what the warehouse holds.
        foreach (string buildingId in colony.Buildings)
        {
            RunBuildingProduction(colony, Ruleset.Building(buildingId));
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

    /// <summary>
    /// A unit's effective sight radius: its type's <see cref="UnitType.LineOfSight"/> plus its role's
    /// <see cref="RoleType.LineOfSightBonus"/> (a scout sees +1 tile further, FreeCol <c>model.modifier.lineOfSightBonus</c>).
    /// </summary>
    private int LineOfSightOf(Unit unit) =>
        unit.Type.LineOfSight + (int)Ruleset.Role(unit.RoleId).LineOfSightBonus;

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
