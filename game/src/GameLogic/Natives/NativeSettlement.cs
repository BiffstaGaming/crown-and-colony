using System.Collections.Generic;
using System.Linq;
using CrownAndColony.GameLogic.World;

namespace CrownAndColony.GameLogic.Natives;

/// <summary>
/// How alarmed a native settlement is toward the player (FreeCol <c>Tension.Level</c>):
/// its hostility band, derived from the numeric alarm. Higher bands gate interaction —
/// an angry settlement won't teach its skill, a hateful one is dangerous to approach.
/// </summary>
public enum AlarmLevel
{
    /// <summary>At peace (alarm 0–100).</summary>
    Happy,

    /// <summary>Tolerant (101–600).</summary>
    Content,

    /// <summary>Wary (601–700).</summary>
    Displeased,

    /// <summary>Hostile (701–800).</summary>
    Angry,

    /// <summary>Ready to attack (801+).</summary>
    Hateful,
}

/// <summary>
/// A native settlement on the map (FreeCol <c>IndianSettlement</c>): a camp, village
/// or city belonging to one of the indigenous nations. Each sits on a single tile,
/// has a size (its resident population), and — until a visitor learns it — a skill it
/// can teach. Interaction (visiting, trade, tension, combat) arrives in later slices;
/// this slice models their placement and existence.
/// </summary>
public sealed class NativeSettlement
{
    /// <summary>Creates a native settlement. Used by the generator and on load.</summary>
    public NativeSettlement(
        int id, string nationTypeId, string settlementTypeId, bool isCapital,
        Position position, int size, string? learnableSkill)
    {
        Id = id;
        NationTypeId = nationTypeId;
        SettlementTypeId = settlementTypeId;
        IsCapital = isCapital;
        Position = position;
        Size = size;
        LearnableSkill = learnableSkill;
    }

    /// <summary>Stable per-game identifier.</summary>
    public int Id { get; }

    /// <summary>The owning native nation's type id (e.g. <c>model.nationType.apache</c>).</summary>
    public string NationTypeId { get; }

    /// <summary>The settlement template id (e.g. <c>model.settlement.camp</c> or its capital variant).</summary>
    public string SettlementTypeId { get; }

    /// <summary>Whether this is the nation's capital (bigger, better defended).</summary>
    public bool IsCapital { get; }

    /// <summary>The tile the settlement occupies.</summary>
    public Position Position { get; }

    /// <summary>Resident population (FreeCol settlement "units"); set at creation, grows in later slices.</summary>
    public int Size { get; internal set; }

    /// <summary>
    /// The expert unit type this settlement can teach a visiting colonist
    /// (e.g. <c>model.unit.expertFarmer</c>), or null if it teaches nothing.
    /// </summary>
    public string? LearnableSkill { get; }

    /// <summary>Maximum alarm value (FreeCol <c>Tension</c> caps the Hateful band at 1000). FreeCol-source-of-truth pin;
    /// the runtime clamps and bands against the data-overridable <c>NativeTensionOptions.MaxAlarm</c> (defaults here).</summary>
    public const int MaxAlarm = 1000;

    // FreeCol Tension.Level upper limits (Tension.java). These are the FreeCol-source-of-truth constants
    // (drift-guarded by NativeConstantsTests); the runtime bands alarm via the data-overridable
    // Specification.NativeTensionOptions (HappyMax/ContentMax/DispleasedMax/AngryMax, defaulting to exactly these
    // values so the classic game is byte-identical) consumed by AlarmLevelFor. See docs/systems/natives.md (86d3drpgg).
    internal const int AlarmHappyMax = 100;
    internal const int AlarmContentMax = 600;
    internal const int AlarmDispleasedMax = 700;
    internal const int AlarmAngryMax = 800;

    // FreeCol tension deltas for hostile acts (Tension.java TENSION_ADD_*). These are the FreeCol-source-of-truth
    // constants (drift-guarded by NativeConstantsTests); the runtime reads them via the data-overridable
    // Specification.NativeTensionOptions (Ruleset.Difficulty.NativeTension), which defaults to exactly these values
    // so the classic game is byte-identical. See docs/systems/natives.md (the 86d3drpgg routing).
    /// <summary>Tension added for a minor slight (FreeCol <c>TENSION_ADD_MINOR</c>).</summary>
    internal const int TensionAddMinor = 100;

    /// <summary>Tension added for an ordinary hostile act such as being attacked (FreeCol <c>TENSION_ADD_NORMAL</c>).</summary>
    internal const int TensionAddNormal = 200;

    /// <summary>Tension added for a major hostile act (FreeCol <c>TENSION_ADD_MAJOR</c>).</summary>
    internal const int TensionAddMajor = 300;

    /// <summary>Tension added when one of the nation's units is destroyed (FreeCol <c>TENSION_ADD_UNIT_DESTROYED</c>).</summary>
    internal const int TensionAddUnitDestroyed = 400;

    /// <summary>Tension added when a settlement is attacked (FreeCol <c>TENSION_ADD_SETTLEMENT_ATTACKED</c>; used by the 5c settlement-combat slice).</summary>
    internal const int TensionAddSettlementAttacked = 500;

    /// <summary>Tension added when the capital is attacked (FreeCol <c>TENSION_ADD_CAPITAL_ATTACKED</c>).</summary>
    internal const int TensionAddCapitalAttacked = 600;

    /// <summary>Alarm added to the robbed nation when land is <em>taken</em> by force rather than bought (FreeCol <c>Tension.TENSION_ADD_LAND_TAKEN</c>).</summary>
    internal const int TensionAddLandTaken = 200;

    /// <summary>
    /// The alarm a nation's settlements are set to when it surrenders — its capital sacked
    /// (FreeCol <c>Tension.SURRENDERED</c> = <c>(Content.limit + Happy.limit) / 2</c> = <c>(600 + 100) / 2</c>).
    /// </summary>
    internal const int SurrenderedAlarm = (AlarmContentMax + AlarmHappyMax) / 2;

    /// <summary>Player id of the human (the original single-player first-contact flag, and the migrated single alarm scalar, ride this id).</summary>
    private const int HumanVisitorId = 0;

    /// <summary>
    /// Alarm toward each colonial player, keyed by <see cref="GameSession.Player.PlayerId"/> (FreeCol
    /// <c>IndianSettlement</c>'s <c>Map&lt;Player, Tension&gt;</c> — a settlement can be friendly to one power and
    /// hostile to another). A missing channel reads as 0 (peaceful), matching FreeCol's "uncontacted → null alarm,
    /// treated as the minimum". The human is channel <see cref="HumanVisitorId"/> (0); before this slice a settlement
    /// held a single scalar that was effectively the human's alarm, so old saves migrate that value into channel 0
    /// (see <see cref="Persistence.SavedNativeSettlement.Alarm"/>). Mutated only via <see cref="SetAlarm"/> /
    /// <c>Game.ChangeNativeAlarm</c>, which key on the acting player's perspective (ADR-009).
    /// </summary>
    private readonly Dictionary<int, int> _alarm = [];

    /// <summary>
    /// Alarm this settlement holds toward the player with <paramref name="playerId"/> (0–<see cref="MaxAlarm"/>);
    /// 0 (peaceful) for a player it holds no channel for. FreeCol <c>IndianSettlement.getAlarm(Player)</c> (a null
    /// channel there reads as the minimum here).
    /// </summary>
    public int AlarmFor(int playerId) => _alarm.GetValueOrDefault(playerId);

    /// <summary>
    /// Sets this settlement's alarm toward <paramref name="playerId"/> to <paramref name="value"/> (the caller clamps to
    /// [0, <see cref="MaxAlarm"/>]). A value of 0 <b>drops the channel</b> rather than storing a zero, so a settlement
    /// that only ever held the human's (now-zero) alarm round-trips byte-identically to a settlement with no channels
    /// at all — the additive + omit-when-default save shape (<see cref="AlarmChannels"/>). Internal: the rules engine
    /// mutates alarm through <c>Game.ChangeNativeAlarm</c>.
    /// </summary>
    /// <param name="playerId">The colonial player whose channel to set (the human is 0).</param>
    /// <param name="value">The new alarm, already clamped to the legal range.</param>
    internal void SetAlarm(int playerId, int value)
    {
        if (value <= 0)
        {
            _alarm.Remove(playerId);
        }
        else
        {
            _alarm[playerId] = value;
        }
    }

    /// <summary>
    /// The non-default alarm channels as player-id → alarm pairs (only players this settlement actually holds a
    /// non-zero alarm toward, ordered by player id for a stable serialization), or an empty sequence when it holds
    /// none. The serializer (save v55) emits this only when non-empty and omits any zero channel, so a settlement
    /// that is peaceful toward everyone (the fresh / single-power-at-peace case) is byte-identical to one with no
    /// field (ADR-009). The human (channel 0) is included here when non-zero — old v54 saves' single scalar migrates
    /// into it.
    /// </summary>
    public IEnumerable<KeyValuePair<int, int>> AlarmChannels =>
        _alarm.Where(kv => kv.Value != 0).OrderBy(kv => kv.Key);

    /// <summary>
    /// Alarm toward the <b>human</b> (channel <see cref="HumanVisitorId"/>, id 0); the original single-scalar field,
    /// now backed by the per-player map (<see cref="AlarmFor"/>). Kept for the presentation panels, the per-turn ambient
    /// pressure / decay, and the native raid AI — all of which are human-centric today (natives only raid the human, and
    /// only the human's footprint stirs ambient alarm). Reading/writing it is exactly reading/writing channel 0.
    /// </summary>
    public int Alarm
    {
        get => AlarmFor(HumanVisitorId);
        internal set => SetAlarm(HumanVisitorId, value);
    }

    /// <summary>
    /// The colonial player this settlement most hates (FreeCol <c>IndianSettlement.getMostHated</c>): among the players
    /// it holds a channel toward whose band is <b>not</b> <see cref="AlarmLevel.Happy"/>, the one with the highest
    /// alarm; <c>null</c> when every channel is calm (Happy) or absent. Recomputed by <c>Game</c> whenever a channel
    /// changes (it knows the live colonial players). Drives the future foreign-power native-relations and the
    /// declaration-of-independence native re-stancing; not used by the human-only raid AI today.
    /// </summary>
    public int? MostHated { get; internal set; }

    /// <summary>
    /// The hostility band (FreeCol <c>Tension.getLevel</c>) of this settlement's alarm toward the <b>human</b>
    /// (channel 0) against the <b>classic</b> band limits. A convenience read for callers without a ruleset (the
    /// presentation panels) that delegates to <see cref="AlarmLevelFor(int, Specification.NativeTensionOptions)"/>,
    /// so it stays byte-identical to the original hardcoded thresholds. The rules engine reads the
    /// <b>data-overridable</b> band, per acting player, via <see cref="AlarmLevelFor(int, Specification.NativeTensionOptions)"/>
    /// with <c>Ruleset.Difficulty.NativeTension</c>, so a variant's retuned thresholds drive gameplay.
    /// </summary>
    public AlarmLevel AlarmLevel => AlarmLevelFor(HumanVisitorId, Specification.NativeTensionOptions.ClassicMedium);

    /// <summary>
    /// The hostility band of this settlement's alarm toward the <b>human</b> (channel 0) against the band limits in
    /// <paramref name="tension"/>. Convenience overload of
    /// <see cref="AlarmLevelFor(int, Specification.NativeTensionOptions)"/> for the human-centric callers (raid AI,
    /// presentation); the rules engine passes the acting player explicitly.
    /// </summary>
    /// <param name="tension">The native-tension options carrying the band limits (<c>Ruleset.Difficulty.NativeTension</c>).</param>
    public AlarmLevel AlarmLevelFor(Specification.NativeTensionOptions tension) =>
        AlarmLevelFor(HumanVisitorId, tension);

    /// <summary>
    /// The hostility band (FreeCol <c>Tension.getLevel</c>) of this settlement's alarm toward
    /// <paramref name="playerId"/> against the band limits in <paramref name="tension"/>
    /// (<c>Ruleset.Difficulty.NativeTension</c>) — the data-overridable, per-player form the rules engine uses, so a
    /// variant can retune the Happy/Content/Displeased/Angry thresholds and a settlement can sit in different bands for
    /// different powers. The classic limits (100/600/700/800, Hateful above) reproduce the original bands exactly. Like
    /// FreeCol's <c>getLevel</c>, the band is the first whose inclusive upper limit the alarm does not exceed,
    /// defaulting to Hateful. A player with no channel reads as <see cref="AlarmLevel.Happy"/> (alarm 0).
    /// </summary>
    /// <param name="playerId">The colonial player whose perspective to band (the human is 0).</param>
    /// <param name="tension">The native-tension options carrying the band limits (<c>Ruleset.Difficulty.NativeTension</c>).</param>
    public AlarmLevel AlarmLevelFor(int playerId, Specification.NativeTensionOptions tension)
    {
        int alarm = AlarmFor(playerId);
        return alarm <= tension.HappyMax ? AlarmLevel.Happy
            : alarm <= tension.ContentMax ? AlarmLevel.Content
            : alarm <= tension.DispleasedMax ? AlarmLevel.Displeased
            : alarm <= tension.AngryMax ? AlarmLevel.Angry
            : AlarmLevel.Hateful;
    }

    /// <summary>Colonial player ids that have spoken with this settlement's chief — FreeCol's per-player first contact; each player gets the first-contact gift once.</summary>
    private readonly HashSet<int> _visitedBy = [];

    /// <summary>Whether <paramref name="playerId"/> has spoken with this settlement's chief (its one-time gift is spent).</summary>
    public bool HasBeenVisitedBy(int playerId) => _visitedBy.Contains(playerId);

    /// <summary>Records that <paramref name="playerId"/> has spoken with the chief.</summary>
    internal void MarkVisitedBy(int playerId) => _visitedBy.Add(playerId);

    /// <summary>True once the <b>human</b> (player 0) has spoken with the chief — the original first-contact flag, now backed by the per-player set (kept for the presentation panel and the legacy save field).</summary>
    public bool HasBeenVisited
    {
        get => _visitedBy.Contains(HumanVisitorId);
        internal set { if (value) { _visitedBy.Add(HumanVisitorId); } else { _visitedBy.Remove(HumanVisitorId); } }
    }

    /// <summary>The non-human player ids that have visited (the additive save field; the human rides <see cref="HasBeenVisited"/>).</summary>
    public IReadOnlyList<int> VisitedByPowers => _visitedBy.Where(id => id != HumanVisitorId).OrderBy(id => id).ToArray();

    /// <summary>True once this settlement's <see cref="LearnableSkill"/> has been taught (capitals never consume theirs).</summary>
    public bool SkillConsumed { get; internal set; }

    /// <summary>
    /// The colonial player id whose missionary resides here (FreeCol's per-settlement missionary), or <c>null</c> for
    /// no mission. At most one mission per settlement; set by <c>Game.EstablishMission</c> when a missionary is
    /// installed (alarm Displeased or calmer). See [natives].
    /// </summary>
    public int? MissionOwnerId { get; internal set; }

    /// <summary>
    /// Whether the resident missionary is an expert (a jesuit, skill 3) rather than an ordinary colonist (skill 0) —
    /// captured at establish-time so the per-turn conversion skill term is recoverable without a unit reference.
    /// Only meaningful while <see cref="HasMission"/>.
    /// </summary>
    public bool MissionIsExpert { get; internal set; }

    /// <summary>Whether a colonial player's missionary resides here.</summary>
    public bool HasMission => MissionOwnerId is not null;

    /// <summary>
    /// Accrued convert progress while a mission is installed (FreeCol <c>convertProgress</c>): each turn it gains
    /// <c>(missionary skill + 6) + 2% of alarm</c>, and at the settlement type's convert threshold (classic 100) a
    /// brave converts into an Indian Convert worker and this resets. 0 with no mission.
    /// </summary>
    public int ConvertProgress { get; internal set; }

    /// <summary>
    /// The goods this settlement most wants to buy (FreeCol <c>wantedGoods</c>, up to 3, most-wanted first). Selling a
    /// wanted good earns a premium (150/125/110%). <b>Stock-driven</b> (FreeCol <c>updateWantedGoods</c>): recomputed by
    /// <c>Game.RecomputeWantedGoods</c> from the settlement's <see cref="GeneralStock"/> — the storable, non-military
    /// goods it can pay the most for (those it is shortest of) — at generation and again after every buy/sell, so its
    /// cravings shift as its stores change. (Pre-v57 it was 3 fixed random goods assigned at placement.)
    /// </summary>
    public IReadOnlyList<string> WantedGoods { get; internal set; } = [];

    /// <summary>
    /// Muskets and horses the settlement holds, by goods id (FreeCol <c>IndianSettlement</c> goods container — the
    /// stock the native AI arms its braves from, <see cref="GameSession.Game.TryEquipBrave"/>). We do not model a
    /// full native warehouse: this is a <b>minimal military stock</b> — only the muskets/horses a settlement retains
    /// to arm its braves (FreeCol's military-goods retention, <c>Settlement.getWantedGoodsAmount</c> for military
    /// goods). The generator <b>seeds</b> it at game creation, sized by settlement type/capital
    /// (<see cref="World.NativeSettlementGenerator"/>), so a real game's threatened camps actually arm their braves.
    /// It is <b>serialized additively</b> (save v54, <see cref="Persistence.SavedNativeSettlement.MilitaryStock"/>):
    /// emitted only when non-empty, so an empty-stock settlement round-trips byte-identically (ADR-009). Spending
    /// (equipping braves) and the deterministic seed are RNG-free, never the human's stream 0.
    /// </summary>
    private readonly Dictionary<string, int> _militaryStock = [];

    /// <summary>Units of <paramref name="goodsId"/> the settlement holds (0 if none). See <see cref="_militaryStock"/>.</summary>
    public int StockOf(string goodsId) => _militaryStock.GetValueOrDefault(goodsId);

    /// <summary>
    /// The settlement's military stock as goods-id → amount pairs (only goods it actually holds, ordered by goods id
    /// for a stable serialization), or an empty sequence when it holds none. The serializer (save v54) emits this only
    /// when non-empty, so an empty-stock settlement is byte-identical to one with no field (ADR-009). See
    /// <see cref="_militaryStock"/>.
    /// </summary>
    public IEnumerable<KeyValuePair<string, int>> MilitaryStock =>
        _militaryStock.OrderBy(kv => kv.Key, System.StringComparer.Ordinal);

    /// <summary>Adds <paramref name="amount"/> of <paramref name="goodsId"/> to the settlement's military stock (clamped at 0); the native AI consumes it when equipping braves.</summary>
    public void AddStock(string goodsId, int amount)
    {
        int next = StockOf(goodsId) + amount;
        if (next <= 0)
        {
            _militaryStock.Remove(goodsId);
        }
        else
        {
            _militaryStock[goodsId] = next;
        }
    }

    /// <summary>
    /// The settlement's <b>general goods stock</b> — the (non-military) wares it has accumulated and offers a visiting
    /// trader to <b>buy</b> (FreeCol <c>IndianSettlement</c>'s <c>GoodsContainer</c>, distinct from the
    /// <see cref="_militaryStock"/> muskets/horses it keeps to arm braves). Unlike FreeCol we do not simulate the
    /// natives' tile production turn by turn; this stock is <b>seeded at generation</b> (sized by settlement
    /// type/capital) then <b>drained as the trader buys from it</b>, which re-prices the goods — a fuller store sells
    /// cheaper, so each purchase nudges the price up (FreeCol <c>getNormalGoodsPriceToBuy</c>). It also drives the
    /// <b>stock-driven wanted goods</b> (FreeCol <c>updateWantedGoods</c>): the goods it can pay the most for — the ones
    /// it is shortest of — become the goods it most wants to buy. Serialized additively (save v57,
    /// <see cref="Persistence.SavedNativeSettlement.GeneralStock"/>): emitted only when non-empty, so a settlement with
    /// no general stock round-trips byte-identically (ADR-009). The seed is deterministic (a pure function of the
    /// settlement type, drawn on the native stream at generation) and the buy-drain is RNG-free, so neither touches the
    /// human's economy stream 0.
    /// </summary>
    private readonly Dictionary<string, int> _goodsStock = [];

    /// <summary>Units of <paramref name="goodsId"/> in the settlement's general (non-military) goods stock (0 if none). See <see cref="_goodsStock"/>.</summary>
    public int GeneralStockOf(string goodsId) => _goodsStock.GetValueOrDefault(goodsId);

    /// <summary>
    /// The settlement's general goods stock as goods-id → amount pairs (only goods it actually holds, ordered by goods
    /// id for a stable serialization), or an empty sequence when it holds none. The serializer (save v57) emits this
    /// only when non-empty, so a no-general-stock settlement is byte-identical to one with no field (ADR-009). See
    /// <see cref="_goodsStock"/>.
    /// </summary>
    public IEnumerable<KeyValuePair<string, int>> GeneralStock =>
        _goodsStock.OrderBy(kv => kv.Key, System.StringComparer.Ordinal);

    /// <summary>
    /// Adds <paramref name="amount"/> of <paramref name="goodsId"/> to the settlement's general goods stock (clamping at
    /// 0 and dropping an emptied entry, so the omit-when-default save shape holds). The generator seeds it; the human's
    /// buying from the settlement drains it.
    /// </summary>
    public void AddGoods(string goodsId, int amount)
    {
        int next = GeneralStockOf(goodsId) + amount;
        if (next <= 0)
        {
            _goodsStock.Remove(goodsId);
        }
        else
        {
            _goodsStock[goodsId] = next;
        }
    }

    /// <summary>
    /// The turn number of the last tribute this settlement paid (FreeCol <c>IndianSettlement.lastTribute</c>), or 0 if
    /// never demanded of. A demander can only extract tribute again once <see cref="GameSession.Game.TributeCooldownTurns"/>
    /// turns have passed (<c>lastTribute + 5 &lt; year</c>) — so a settlement can't be shaken down every turn. <b>Transient</b>
    /// (not serialized): defaults to 0, so a generated/loaded settlement is freshly demandable and every save round-trips
    /// byte-identically — consistent with the no-native-treasury abstraction the gift/pillage/native-demand paths make.
    /// </summary>
    public int LastTribute { get; internal set; }

    /// <summary>
    /// The turn number of the last gift this settlement sent the human (FreeCol gifts are throttled by the AI's
    /// per-turn gift probability + the one-mission-in-flight cap; we model the throttle as an explicit per-settlement
    /// cooldown). A settlement won't send another gift until <see cref="GameSession.Game.NativeGiftCooldownTurns"/>
    /// turns have passed. <b>Transient</b> (not serialized): defaults to 0, so a generated/loaded settlement is freshly
    /// giftable and every save round-trips byte-identically — mirroring the <see cref="LastTribute"/> abstraction.
    /// </summary>
    public int LastGiftTurn { get; internal set; }

    /// <summary>The wanted-good slot of a goods id (0 = most wanted), or -1 if not wanted.</summary>
    public int WantedSlot(string goodsId)
    {
        for (int i = 0; i < WantedGoods.Count; i++)
        {
            if (WantedGoods[i] == goodsId)
            {
                return i;
            }
        }
        return -1;
    }
}
