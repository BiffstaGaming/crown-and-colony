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
    /// Liberty multiplier in the Founding Father cost formula. Classic "other"
    /// difficulty = 24 (spec <c>model.option.foundingFatherFactor</c>);
    /// difficulty-based values arrive with the difficulty system.
    /// </summary>
    public const int FoundingFatherFactor = 24;

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

    private readonly List<Unit> _units = [];
    private readonly List<Colony> _colonies = [];
    private readonly List<NativeSettlement> _nativeSettlements = [];
    private readonly List<Player> _players = [];
    private readonly List<CombatNotice> _combatNotices = []; // transient: the most recent turn's AI-vs-human raids (not saved)
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

    /// <summary>All colonies, in founding order.</summary>
    public IReadOnlyList<Colony> Colonies => _colonies;

    /// <summary>The colony on a tile, or null.</summary>
    public Colony? ColonyAt(Position p) => _colonies.FirstOrDefault(c => c.Position == p);

    /// <summary>All native settlements on the map.</summary>
    public IReadOnlyList<NativeSettlement> NativeSettlements => _nativeSettlements;

    /// <summary>The native settlement on a tile, or null.</summary>
    public NativeSettlement? NativeSettlementAt(Position p) =>
        _nativeSettlements.FirstOrDefault(s => s.Position == p);

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

    /// <summary>
    /// A unit's offence base for combat: the type's pre-role additive plus its role's offence, then the
    /// type's own percentage multiplier (so a veteran soldier's +50% applies to base <em>and</em> role —
    /// FreeCol's single index-ordered fold; the situational percentages come later in <see cref="CombatModel"/>).
    /// </summary>
    internal double OffenceBase(Unit unit) =>
        (unit.Type.OffenceAdditive + Ruleset.Role(unit.RoleId).Offence) * unit.Type.OffenceMultiplier;

    /// <summary>A unit's defence base for combat: the type's pre-role additive plus its (effective) role's defence, then the type's percentage multiplier.</summary>
    internal double DefenceBase(Unit unit) =>
        (unit.Type.DefenceAdditive + Ruleset.Role(EffectiveCombatRole(unit, defending: true)).Defence)
        * unit.Type.DefenceMultiplier;

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
        if (DefenderAt(attacker, target) is null)
        {
            return MoveCheck.No("There is no enemy to attack there.");
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
        if (defenderNation is null)
        {
            SetStance(attacker.OwnerId, defender.OwnerId, Stance.War);
            ChangeTension(attacker.OwnerId, defender.OwnerId, TensionWar);
        }

        var attackContext = new AttackContext(
            Movement: MovementPenaltyFor(attacker), // snapshot the movement penalty before spending it
            ArtilleryInOpen: attacker.Type.Bombard); // 5b defenders are in the open, never in a settlement
        var defenceContext = new DefenceContext(
            TerrainDefenceBonus: Map.TerrainAt(target).DefenceBonus);

        double attackPower = CombatModel.AttackPower(OffenceBase(attacker), attackContext);
        double defencePower = CombatModel.DefencePower(DefenceBase(defender), defenceContext);

        // Attacking ends the attacker's turn now — before any promotion/demotion that swaps the unit
        // object (UpgradeUnitType copies MovementLeft, so the swapped unit inherits the spent turn).
        attacker.MovementLeft = 0;

        CombatResult result = CombatModel.Resolve(CombatModel.WinProbability(attackPower, defencePower), random);
        bool attackerWon = result is CombatResult.GreatWin or CombatResult.Win;
        bool great = result is CombatResult.GreatWin or CombatResult.GreatLoss;
        Unit winner = attackerWon ? attacker : defender;
        Unit loser = attackerWon ? defender : attacker;

        ResolveLoserOutcome(winner, loser);
        ApplyWinnerPromotion(winner, great, random);

        // Native alarm shifts across the defender's whole nation by FreeCol's defenderTension: a European
        // win raises it (the slain brave in the open + a minor insult); a repelled attack lowers it.
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
            _human.Gold += ComputePlunder(type, hasPlunderAbility, random); // the attacker is the human in FP-1 (combat becomes player-aware in FP-6)
            _nativeSettlements.Remove(settlement); // destroyed

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
            ResolveLoserOutcome(defender, attacker);
            bool attackerSlain = !_units.Any(u => u.Id == attackerId);
            ApplyNativeCombatTension(nation, DefenderCombatTension(attackerWon: false, slaughterTension: 0, attackerSlain));
        }
        return result;
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
    private void ResolveLoserOutcome(Unit winner, Unit loser)
    {
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
                    visible.UnionWith(TilesInRange(unit.Position, unit.Type.LineOfSight));
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
        _units.Any(u => u.IsOnMap && IsHumanOwned(u) && InSight(u.Position, p, u.Type.LineOfSight))
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
            int? carrierId, string? ownerNationId, string? roleId, int roleCount, int ownerId)> units,
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
                  int? carrierId, string? ownerNationId, string? roleId, int roleCount, int ownerId) in units)
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
            };
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
    public Unit SpawnUnit(UnitType type, Position position, string? ownerNationId = null)
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

        var unit = new Unit(_nextUnitId++, type, position) { OwnerNationId = ownerNationId };
        _units.Add(unit);
        RevealForOwner(unit); // a unit lifts its own owner's fog (the human's, or a foreign power's; natives none)
        return unit;
    }

    /// <summary>
    /// A unit's movement points for a fresh turn: its unit-type base plus its role's movement bonus
    /// (FreeCol <c>Unit.getInitialMovesLeft</c> folding <c>model.modifier.movementBonus</c>) — e.g. a
    /// dragoon/scout/cavalry/mounted brave gets +9 (one extra "move" is 3 points). The role lookup is
    /// null-safe so minimal rulesets without role data simply get the base. (Nation/Magellan movement
    /// bonuses are separate, scoped modifiers — deferred with scope evaluation / founding-father effects.)
    /// </summary>
    private int InitialMovement(Unit unit) =>
        unit.Type.Movement + (int)(Ruleset.Roles.FirstOrDefault(r => r.Id == unit.RoleId)?.MovementBonus ?? 0);

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
        RevealForOwner(unit); // the mover lifts its own owner's fog (mirrors SpawnUnit)
        if (unit.Type.IsCarrier)
        {
            SyncPassengers(unit); // any colonists aboard move with the ship
        }
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
        // footprints never touch. Native settlements do not block founding (FreeCol treats that as a land
        // claim, not a hard bar; we don't model land price).
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

    /// <summary>Turns a naval unit spends crossing the high seas each way (FreeCol TURNS_TO_SAIL).</summary>
    public const int SailTurns = 3;

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
        unit.SailTurnsRemaining = SailTurns;
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
        unit.Location = UnitLocation.SailingToNewWorld;
        unit.SailTurnsRemaining = SailTurns;
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

    /// <summary>Whether the colony may start constructing a building type.</summary>
    public MoveCheck CheckSetBuild(Colony colony, string buildingId)
    {
        BuildingType building = Ruleset.Building(buildingId);
        if (colony.HasBuilding(buildingId))
        {
            return MoveCheck.No($"The colony already has a {building.ShortName}.");
        }
        if (building.BuildCost.Count == 0)
        {
            return MoveCheck.No($"The {building.ShortName} cannot be constructed.");
        }
        if (building.UpgradesFrom is not null && !colony.HasBuilding(building.UpgradesFrom))
        {
            return MoveCheck.No($"A {building.ShortName} upgrades an existing building the colony lacks.");
        }
        if (colony.Population < building.RequiredPopulation)
        {
            return MoveCheck.No(
                $"The {building.ShortName} needs a population of {building.RequiredPopulation}.");
        }
        return MoveCheck.Yes(0);
    }

    /// <summary>Sets what the colony is constructing (null stops construction).</summary>
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckSetBuild"/>.</exception>
    public void SetBuild(Colony colony, string? buildingId)
    {
        if (buildingId is not null)
        {
            MoveCheck check = CheckSetBuild(colony, buildingId);
            if (!check.Allowed)
            {
                throw new InvalidMoveException(check.Reason!);
            }
        }
        colony.CurrentBuild = buildingId;
    }

    /// <summary>Building types the colony could start constructing right now.</summary>
    public IEnumerable<BuildingType> Buildables(Colony colony) =>
        Ruleset.BuildingTypes.Where(b => CheckSetBuild(colony, b.Id).Allowed);

    /// <summary>
    /// Completes construction when the stores cover the cost: materials are
    /// consumed and the building appears (replacing the one it upgrades).
    /// </summary>
    private void RunConstruction(Colony colony)
    {
        if (colony.CurrentBuild is null)
        {
            return;
        }
        BuildingType building = Ruleset.Building(colony.CurrentBuild);
        if (building.BuildCost.Any(c => colony.StoreOf(Ruleset.StorageIdOf(c.GoodsId)) < c.Amount))
        {
            return; // keep saving materials
        }

        foreach (GoodsOutput cost in building.BuildCost)
        {
            colony.AddGoods(Ruleset.StorageIdOf(cost.GoodsId), -cost.Amount);
        }
        if (building.UpgradesFrom is not null)
        {
            colony.ReplaceBuilding(building.UpgradesFrom, building.Id);
        }
        else
        {
            colony.AddBuilding(building.Id);
        }
        colony.CurrentBuild = null;
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
        // completes the loop back to the player it started on.
        _combatNotices.Clear(); // this turn's AI-initiated raids on the human are collected fresh each round
        int startIndex = _currentPlayerIndex;
        do
        {
            RunPlayerTurn(_players[_currentPlayerIndex]);
            _currentPlayerIndex = NextPlayerIndex(_currentPlayerIndex);
        }
        while (_currentPlayerIndex != startIndex);

        AdvanceSailing();
        DetectColonialContacts();   // first sight of a rival colonial power → Peace (FP-6a)
        DecayColonialTension();     // colonial-pair tension cools each turn (mirrors native alarm)
        UpdateColonialStances();    // stance follows tension: war → cease-fire → peace as it cools (FP-6b)
        foreach (NativeSettlement settlement in _nativeSettlements)
        {
            DecayNativeAlarm(settlement);
        }
        foreach (Unit unit in _units)
        {
            unit.MovementLeft = InitialMovement(unit); // base + role bonus (dragoon/scout +9)
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
    /// The minimal foreign-power AI (FP-4): per unit in stable by-id order, a colonist founds a colony where
    /// it stands while the power has fewer than <see cref="MaxAiColonies"/> colonies, else steps one tile
    /// toward the nearest tile the power has not explored; ships and non-founders idle. Choices draw from the
    /// player's own RNG stream (ADR-009) — never the human's stream 0 — so the human's game stays byte-stable.
    /// </summary>
    private void RunForeignPowerTurn(Player power)
    {
        // Snapshot the owned units (founding removes the founder from _units mid-loop).
        foreach (Unit unit in _units.Where(u => IsOwnedBy(u, power)).OrderBy(u => u.Id).ToList())
        {
            if (!unit.IsOnMap || unit.Type.IsNaval || !unit.Type.CanFoundColony)
            {
                continue; // ships and non-founders idle; units still in Europe wait
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
    /// The minimal native AI (slice 1b): each of the nation's units, in stable by-id order, takes ONE action.
    /// When its home settlement is alarmed enough (<see cref="RaidAlarmThreshold"/>) the brave hunts the nearest
    /// human unit — attacking when adjacent, else stepping toward it; otherwise it wanders one tile. Every choice
    /// (the wander pick, the path tiebreak, the combat resolution) draws from the nation's OWN RNG stream via
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
            if (hostile && NearestHumanUnit(brave) is { } prey)
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
    /// The nearest on-map human-owned unit to a brave (Chebyshev, ties broken by position), or null if the human
    /// has none on the map. This filter is the <b>sole contract</b> that keeps braves attacking the human only:
    /// the engine's <see cref="CheckAttack"/>/<see cref="DefenderAt"/> gate on owner-inequality
    /// (<see cref="AreEnemies"/>), which would also admit foreign powers and rival tribes, so a brave is only ever
    /// handed a human target here — never a foreign-power or other-nation unit.
    /// </summary>
    private Unit? NearestHumanUnit(Unit brave) =>
        _units.Where(u => u.IsOnMap && IsHumanOwned(u))
            .OrderBy(u => Chebyshev(u.Position, brave.Position))
            .ThenBy(u => u.Position.Y).ThenBy(u => u.Position.X)
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
                player.Liberty += ApplyGoodsModifiers(player, BellsId, bells); // founding-father bonuses (Jefferson, Paine)
            }
        }

        if (player.CurrentFather is not null && player.Liberty >= TotalFoundingFatherCost(player))
        {
            player.Liberty -= TotalFoundingFatherCost(player);
            player.CongressList.Add(player.CurrentFather);
            player.CurrentFather = null;
            player.OfferedFathersList.Clear();
            RefreshDockForRecruitability(player); // a newly-elected father may ban dock recruits (Brewster)
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

    /// <summary>The ability by which Thomas Paine adds the tax rate as a bell bonus.</summary>
    private const string AddTaxToBellsAbility = "model.ability.addTaxToBells";

    /// <summary>The ability gating which unit types may be recruited (William Brewster denies some).</summary>
    private const string CanRecruitUnitAbility = "model.ability.canRecruitUnit";

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
    private string DrawRecruitType(Player player)
    {
        var pool = Ruleset.UnitTypes.Where(t => IsRecruitable(player, t)).ToList();
        int total = pool.Sum(u => u.RecruitProbability);
        int roll = RandomFor(player).Next(total);
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
    internal int TileYield(Player player, Position tile, string goodsId)
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

        // Founding-father goods modifiers stack on top (higher index, applied last).
        return ApplyGoodsModifiers(player, goodsId, (int)yield);
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
                colony.AddGoods(
                    Ruleset.StorageIdOf(output.GoodsId),
                    (int)Math.Floor(output.Amount * multiplier * fraction));
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

        // 1b. Worked tiles produce their assigned goods.
        foreach ((Position tile, string goodsId) in colony.TileWorkers)
        {
            colony.AddGoods(Ruleset.StorageIdOf(goodsId), TileYield(owner, tile, goodsId));
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

        // 2. Colonists eat; an unfed colonist starves (population floors at 1 —
        //    colony destruction is a later rule). Assignments shrink to match.
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

    /// <summary>Each turn a settlement's alarm cools toward 0 (FreeCol tension decay, <c>ServerPlayer</c>: −value/100 − 4).</summary>
    private static void DecayNativeAlarm(NativeSettlement settlement) =>
        settlement.Alarm = Math.Max(0, settlement.Alarm - (settlement.Alarm / 100 + 4));

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
    private void Reveal(Player player, Unit unit) => RevealAround(player, unit.Position, unit.Type.LineOfSight);

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
