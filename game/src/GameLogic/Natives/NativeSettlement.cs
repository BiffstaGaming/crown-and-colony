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

    /// <summary>Maximum alarm value (FreeCol <c>Tension</c> caps the Hateful band at 1000).</summary>
    public const int MaxAlarm = 1000;

    // FreeCol Tension.Level upper limits (Tension.java).
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

    /// <summary>
    /// Alarm toward the player (0–<see cref="MaxAlarm"/>); starts peaceful at 0, raised
    /// by hostile acts (combat/land-taking, later slices) and cools each turn.
    /// </summary>
    public int Alarm { get; internal set; }

    /// <summary>The hostility band (FreeCol <c>Tension.Level</c>) derived from <see cref="Alarm"/>.</summary>
    public AlarmLevel AlarmLevel =>
        Alarm <= AlarmHappyMax ? AlarmLevel.Happy
        : Alarm <= AlarmContentMax ? AlarmLevel.Content
        : Alarm <= AlarmDispleasedMax ? AlarmLevel.Displeased
        : Alarm <= AlarmAngryMax ? AlarmLevel.Angry
        : AlarmLevel.Hateful;

    /// <summary>Player id of the human (the original single-player first-contact flag rides this id).</summary>
    private const int HumanVisitorId = 0;

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
    /// The goods this settlement most wants to buy (FreeCol <c>wantedGoods</c>, up to 3,
    /// most-wanted first). Selling a wanted good earns a premium (150/125/110%).
    /// </summary>
    public IReadOnlyList<string> WantedGoods { get; internal set; } = [];

    /// <summary>
    /// Muskets and horses the settlement holds, by goods id (FreeCol <c>IndianSettlement</c> goods container — the
    /// stock the native AI arms its braves from, <see cref="GameSession.Game.TryEquipBrave"/>). We do not model a
    /// full native warehouse: this is a <b>minimal, transient</b> military stock that is <em>not serialized</em>
    /// (consistent with the "no native goods store" abstraction the gift/pillage paths already make) and defaults to
    /// empty, so a generated/loaded game holds none and the equip step never fires — the human's stream 0 and every
    /// save round-trip stay byte-identical (ADR-009). Only gameplay/tests that deposit muskets/horses here activate it.
    /// </summary>
    private readonly Dictionary<string, int> _militaryStock = [];

    /// <summary>Units of <paramref name="goodsId"/> the settlement holds (0 if none). See <see cref="_militaryStock"/>.</summary>
    public int StockOf(string goodsId) => _militaryStock.GetValueOrDefault(goodsId);

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
    /// The turn number of the last tribute this settlement paid (FreeCol <c>IndianSettlement.lastTribute</c>), or 0 if
    /// never demanded of. A demander can only extract tribute again once <see cref="GameSession.Game.TributeCooldownTurns"/>
    /// turns have passed (<c>lastTribute + 5 &lt; year</c>) — so a settlement can't be shaken down every turn. <b>Transient</b>
    /// (not serialized): defaults to 0, so a generated/loaded settlement is freshly demandable and every save round-trips
    /// byte-identically — consistent with the no-native-treasury abstraction the gift/pillage/native-demand paths make.
    /// </summary>
    public int LastTribute { get; internal set; }

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
