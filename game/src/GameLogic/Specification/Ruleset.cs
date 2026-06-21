using System.Reflection;
using System.Xml.Linq;
using CrownAndColony.GameLogic.World.Improvements;

namespace CrownAndColony.GameLogic.Specification;

/// <summary>
/// The parsed game ruleset. Loaded from a FreeCol-format <c>specification.xml</c>
/// (ADR-004); the classic ruleset ships embedded in this assembly so every host
/// (game, tests, CI) reads the identical bytes.
/// </summary>
public sealed class Ruleset
{
    private readonly Dictionary<string, TerrainType> _terrainById;
    private readonly Dictionary<string, UnitType> _unitById;
    private readonly Dictionary<string, GoodsType> _goodsById;
    private readonly Dictionary<string, BuildingType> _buildingById;
    private readonly Dictionary<string, FoundingFather> _fatherById;
    private readonly Dictionary<string, ResourceType> _resourceById;
    private readonly Dictionary<string, TileImprovementType> _improvementById;
    private readonly Dictionary<string, NativeNationType> _nativeNationById;
    private readonly Dictionary<string, SettlementType> _settlementById;
    private readonly Dictionary<string, RoleType> _roleById;
    private readonly Dictionary<string, Disaster> _disasterById;
    private readonly Dictionary<string, Dictionary<string, UnitChange>> _unitChangeByType;
    private readonly Dictionary<string, Dictionary<string, int>> _experienceUpgradeByFrom; // from-type → (expert-to-type → probability)
    private readonly Dictionary<string, Dictionary<string, int>> _educationByFrom; // from-type → (to-type → base turns of training)
    private readonly Dictionary<string, string> _expertForProducing; // goods id → the unit type that is its expert
    private readonly Dictionary<string, EuropeanNation> _europeanNationById;
    private readonly Dictionary<string, SpecEvent> _eventById; // spec <event> elements, keyed by id

    private Ruleset(
        Dictionary<string, TerrainType> terrainById,
        Dictionary<string, UnitType> unitById,
        Dictionary<string, GoodsType> goodsById,
        Dictionary<string, BuildingType> buildingById,
        Dictionary<string, FoundingFather> fatherById,
        Dictionary<string, ResourceType> resourceById,
        Dictionary<string, TileImprovementType> improvementById,
        Dictionary<string, NativeNationType> nativeNationById,
        Dictionary<string, SettlementType> settlementById,
        Dictionary<string, RoleType> roleById,
        Dictionary<string, Disaster> disasterById,
        Dictionary<string, Dictionary<string, UnitChange>> unitChangeByType,
        Dictionary<string, Dictionary<string, int>> experienceUpgradeByFrom,
        Dictionary<string, Dictionary<string, int>> educationByFrom,
        Dictionary<string, EuropeanNation> europeanNationById,
        Dictionary<string, SpecEvent> eventById,
        Calendar calendar,
        IReadOnlyList<int> fatherAgeYears,
        DifficultyOptions difficulty,
        GameOptions gameOptions,
        string difficultyLevelId,
        bool upkeepEnabled,
        int naturalDisasterPercentage,
        int lastColonialYear,
        int interventionBells,
        int interventionTurns,
        InterventionForceComposition interventionForce,
        bool victoryDefeatRef,
        bool victoryDefeatEuropeans,
        bool victoryDefeatHumans)
    {
        Calendar = calendar;
        FatherAgeYears = fatherAgeYears;
        Difficulty = difficulty;
        GameOptions = gameOptions;
        DifficultyLevelId = difficultyLevelId;
        UpkeepEnabled = upkeepEnabled;
        NaturalDisasterPercentage = naturalDisasterPercentage;
        LastColonialYear = lastColonialYear;
        InterventionBells = interventionBells;
        InterventionTurns = interventionTurns;
        InterventionForce = interventionForce;
        VictoryDefeatRef = victoryDefeatRef;
        VictoryDefeatEuropeans = victoryDefeatEuropeans;
        VictoryDefeatHumans = victoryDefeatHumans;
        _terrainById = terrainById;
        _unitById = unitById;
        _goodsById = goodsById;
        _buildingById = buildingById;
        _fatherById = fatherById;
        _resourceById = resourceById;
        _improvementById = improvementById;
        _nativeNationById = nativeNationById;
        _settlementById = settlementById;
        _roleById = roleById;
        _disasterById = disasterById;
        _unitChangeByType = unitChangeByType;
        _experienceUpgradeByFrom = experienceUpgradeByFrom;
        _educationByFrom = educationByFrom;
        _europeanNationById = europeanNationById;
        _eventById = eventById;
        // Reverse the expert→good mapping into good→expert (FreeCol Specification.getExpertForProducing): the unit type
        // whose expert-production is a given good (grain → expert farmer, fish → expert fisherman, …). First definition
        // wins on the (classic-impossible) duplicate.
        _expertForProducing = [];
        foreach (UnitType unit in _unitById.Values)
        {
            if (unit.ExpertProduction is { } good)
            {
                _expertForProducing.TryAdd(good, unit.Id);
            }
        }
        TerrainTypes = _terrainById.Values.ToList();
        UnitTypes = _unitById.Values.ToList();
        GoodsTypes = _goodsById.Values.ToList();
        BuildingTypes = _buildingById.Values.ToList();
        Disasters = _disasterById.Values.ToList();
        NaturalDisasters = _disasterById.Values.Where(d => d.Natural).ToList();
        // Building-material goods = every goods id any BUILDABLE type requires to construct, FreeCol-faithfully
        // (GoodsType.isBuildingMaterial, derived over buildings + units + roles). Classic content: buildings need
        // hammers + tools; the artillery/wagon/ships need hammers (+tools); the freeColonist's `required-goods
        // food=200` (its in-colony growth cost) makes model.goods.food a building material; and the armed/mounted
        // roles' required-goods make muskets/horses ones. This drives the native tribute-demand "building material"
        // rung — so under Angry/Hateful a colony's food is demanded via this rung, as FreeCol does (86d3c18n8).
        // SAFE for the foreign-power colony planner: food/muskets/horses are all tradeable+storable, so the two
        // planner uses of this set (NonTradeableOutputValue gated on !IsTradeable; BuildingBuildWeight gated on
        // !IsStorable) never see them — only the demand rung's behaviour changes.
        BuildingMaterials = _buildingById.Values.SelectMany(b => b.BuildCost).Select(g => g.GoodsId)
            .Concat(_unitById.Values.SelectMany(u => u.BuildCostOrEmpty).Select(g => g.GoodsId))
            .Concat(_roleById.Values.SelectMany(r => r.RequiredGoods).Select(g => g.GoodsId))
            .ToHashSet();
        FoundingFathers = _fatherById.Values.ToList();
        ResourceTypes = _resourceById.Values.ToList();
        ImprovementTypes = _improvementById.Values.ToList();
        NativeNationTypes = _nativeNationById.Values.ToList();
        SettlementTypes = _settlementById.Values.ToList();
        Roles = _roleById.Values.ToList();
        EuropeanNations = _europeanNationById.Values.ToList();
        Events = _eventById.Values.ToList();
    }

    /// <summary>
    /// The turn→year/season calendar parsed from the spec <c>gameOptions.years</c> group (classic 1492/1600/2).
    /// <see cref="GameSession.Game.CurrentYear"/>/<see cref="GameSession.Game.CurrentSeason"/> read it for the current turn.
    /// </summary>
    public Calendar Calendar { get; }

    /// <summary>
    /// The in-game year thresholds at which the founding-father age weighting changes (classic <c>1600, 1700</c>,
    /// from the spec <c>model.option.ages</c>), ascending. There are <c>NUMBER_OF_AGES − 1 = 2</c> of them, giving
    /// three ages. <see cref="AgeForYear"/> turns a year into the 1-based age.
    /// </summary>
    public IReadOnlyList<int> FatherAgeYears { get; }

    /// <summary>
    /// The 1-based founding-father age (1, 2 or 3) for an in-game <paramref name="year"/>, per <see cref="FatherAgeYears"/>:
    /// a year before the first threshold is age 1, before the second age 2, otherwise age 3 (classic: &lt;1600 → 1,
    /// 1600–1699 → 2, ≥1700 → 3). This is the same boundary as FreeCol <c>Specification.getAge</c> (which is 0-based);
    /// ours is 1-based to feed <see cref="FoundingFather.WeightForAge"/>.
    /// </summary>
    public int AgeForYear(int year) => 1 + FatherAgeYears.Count(threshold => year >= threshold);

    /// <summary>
    /// The selected difficulty level's tuning numbers (default <c>model.difficulty.medium</c>), parsed from the spec.
    /// Balance constants read these instead of hardcoding values.
    /// </summary>
    public DifficultyOptions Difficulty { get; }

    /// <summary>
    /// The base <c>gameOptions</c>-group tuning numbers (not per-difficulty-level) — currently the immigration trio
    /// (<c>initialImmigration</c> / <c>europeanUnitImmigrationPenalty</c> / <c>playerImmigrationBonus</c>), parsed once
    /// from the spec. Balance constants read these instead of hardcoding values. See [immigration].
    /// </summary>
    public GameOptions GameOptions { get; }

    /// <summary>
    /// The spec id of the difficulty level this ruleset was loaded with (e.g. <c>model.difficulty.medium</c>) — the
    /// level whose options <see cref="Difficulty"/> holds. Persisted in the save (omitted when the default medium) so
    /// a game reloads under the same balance (86d3c9y08).
    /// </summary>
    public string DifficultyLevelId { get; }

    /// <summary>
    /// Whether buildings charge their owner per-turn gold upkeep (the spec <c>model.option.enableUpkeep</c> boolean
    /// game option; classic default <b>false</b>). When off, <see cref="GameSession.Game"/> deducts no building upkeep
    /// and the default classic economy is unchanged; when on, each colony's Σ <see cref="BuildingType.Upkeep"/> is taken
    /// from the player's gold each turn (FreeCol gates <c>ServerPlayer.csPayUpkeep</c> on this same option).
    /// </summary>
    public bool UpkeepEnabled { get; }

    /// <summary>
    /// The percentage chance (0..100) that a natural disaster strikes one of a colonial player's colonies each turn
    /// (the spec <c>model.option.naturalDisasters</c> percentage game option; classic default <b>0</b>). At 0 the
    /// per-player disaster roll never fires, so the default classic game is byte-identical and rolls nothing
    /// (FreeCol gates <c>ServerPlayer.csNaturalDisasters</c> on <c>disaster &gt; 0</c>). Above 0, each colonial
    /// player rolls once per turn on a reserved RNG stream (never the human's economy stream 0).
    /// </summary>
    public int NaturalDisasterPercentage { get; }

    /// <summary>All disasters in the ruleset (natural + the special bankruptcy/raid/conquest disasters), in spec order.</summary>
    public IReadOnlyList<Disaster> Disasters { get; }

    /// <summary>The natural disasters eligible for the per-colony natural-disaster roll, in spec order (FreeCol <c>Disaster.isNatural</c>).</summary>
    public IReadOnlyList<Disaster> NaturalDisasters { get; }

    /// <summary>Looks up a disaster by id (e.g. <see cref="Disaster.BankruptcyId"/>); null when the ruleset defines no such disaster.</summary>
    public Disaster? FindDisaster(string id) => _disasterById.GetValueOrDefault(id);

    /// <summary>
    /// The last in-game year a colonial power may declare independence (the spec
    /// <c>model.option.lastColonialYear</c> integer game option in the <c>gameOptions.years</c> group; classic
    /// <b>1800</b>). Once <see cref="GameSession.Game.CurrentYear"/> passes this year it is too late to declare
    /// (FreeCol <c>model.limit.independence.year</c>, the <c>year ≤ lastColonialYear</c> limit). A spec without the
    /// option falls back to 1800, so the default classic game is unchanged.
    /// </summary>
    public int LastColonialYear { get; }

    /// <summary>
    /// The spec <c>&lt;event&gt;</c> elements (FreeCol <c>Specification.getEvents</c>): special game occurrences —
    /// declaring independence, the Spanish succession — each gated by its own <see cref="SpecEvent.Limits"/>. The
    /// limit-evaluation engine (<c>Game.CheckSpecEvent</c>) reads these to decide when an event may fire. Empty when
    /// the spec defines no <c>&lt;events&gt;</c> section (a variant could add or remove events by data alone).
    /// </summary>
    public IReadOnlyList<SpecEvent> Events { get; }

    /// <summary>The spec event with the given id (FreeCol <c>Specification.getEvent</c>), e.g.
    /// <c>model.event.spanishSuccession</c>; <c>null</c> if the ruleset defines no such event.</summary>
    /// <param name="id">The event id.</param>
    public SpecEvent? Event(string id) => _eventById.GetValueOrDefault(id);

    /// <summary>
    /// The liberty (bells) a rebel must accrue during the War of Independence before a friendly foreign power lands
    /// an <see cref="InterventionForce"/> to aid it (the spec <c>model.option.interventionBells</c> integer option in
    /// the difficulty <c>monarch</c> group; classic medium <b>5000</b>). A rebel banks the same net liberty figure it
    /// produces each turn toward this threshold (FreeCol <c>Player.modifyLiberty</c> for a rebel); once it is reached
    /// the force arrives and the counter resets. A spec without the option falls back to 5000.
    /// </summary>
    public int InterventionBells { get; }

    /// <summary>
    /// How often (in turns) FreeCol grows the standing intervention force before it lands (the spec
    /// <c>model.option.interventionTurns</c> integer option in the difficulty <c>monarch</c> group; classic medium
    /// <b>52</b>). We parse it for fidelity and a follow-up repeat/growth, but the base game lands the fixed
    /// <see cref="InterventionForce"/> once at the threshold (see [independence]). A spec without it falls back to 52.
    /// </summary>
    public int InterventionTurns { get; }

    /// <summary>
    /// The composition of the foreign Intervention Force a friendly power sends to a rebel that holds out long enough
    /// (the spec <c>model.option.interventionForce</c> <c>unitListOption</c> in the difficulty <c>monarch</c> group;
    /// classic medium 2 colonial-regular soldiers + 2 colonial-regular dragoons + 2 artillery + 2 men-o-war). A spec
    /// without the option falls back to that classic-medium composition.
    /// </summary>
    public InterventionForceComposition InterventionForce { get; }

    /// <summary>
    /// Victory condition: the first player to defeat/expel the Royal Expeditionary Force wins (the spec
    /// <c>model.option.victoryDefeatREF</c> boolean game option in the <c>gameOptions.victoryConditions</c> group;
    /// classic default <b>true</b>). This is the headline War-of-Independence win (FreeCol <c>checkForWinner</c>'s REF
    /// branch). A spec without the option falls back to true, so the default classic game is unchanged.
    /// </summary>
    public bool VictoryDefeatRef { get; }

    /// <summary>
    /// Victory condition: a player who defeats every other European power wins (the spec
    /// <c>model.option.victoryDefeatEuropeans</c> boolean game option; classic default <b>true</b>). Satisfied when
    /// exactly one non-REF European power is still alive (FreeCol <c>checkForWinner</c>'s Europeans branch). A spec
    /// without the option falls back to true.
    /// </summary>
    public bool VictoryDefeatEuropeans { get; }

    /// <summary>
    /// Victory condition: a player who defeats every other <em>human</em> player wins (the spec
    /// <c>model.option.victoryDefeatHumans</c> boolean game option; classic default <b>false</b>). Satisfied when
    /// exactly one non-AI European power is still alive (FreeCol <c>checkForWinner</c>'s humans branch — for our
    /// single-human game it is off by default). A spec without the option falls back to false.
    /// </summary>
    public bool VictoryDefeatHumans { get; }

    /// <summary>All terrain types, in specification order.</summary>
    public IReadOnlyList<TerrainType> TerrainTypes { get; }

    /// <summary>All concrete (non-abstract) unit types, in specification order.</summary>
    public IReadOnlyList<UnitType> UnitTypes { get; }

    /// <summary>Looks up a terrain type by ruleset id (e.g. <c>model.tile.plains</c>).</summary>
    /// <exception cref="KeyNotFoundException">Unknown id.</exception>
    public TerrainType Terrain(string id) =>
        _terrainById.TryGetValue(id, out var t)
            ? t
            : throw new KeyNotFoundException($"Unknown terrain type '{id}'.");

    /// <summary>Looks up a unit type by ruleset id (e.g. <c>model.unit.freeColonist</c>).</summary>
    /// <exception cref="KeyNotFoundException">Unknown id.</exception>
    public UnitType Unit(string id) =>
        _unitById.TryGetValue(id, out var u)
            ? u
            : throw new KeyNotFoundException($"Unknown unit type '{id}'.");

    /// <summary>All goods types, in specification order.</summary>
    public IReadOnlyList<GoodsType> GoodsTypes { get; }

    /// <summary>Looks up a goods type by ruleset id (e.g. <c>model.goods.sugar</c>).</summary>
    /// <exception cref="KeyNotFoundException">Unknown id.</exception>
    public GoodsType Goods(string id) =>
        _goodsById.TryGetValue(id, out var g)
            ? g
            : throw new KeyNotFoundException($"Unknown goods type '{id}'.");

    /// <summary>
    /// The warehouse id a goods type stores as (grain → food); unknown ids pass
    /// through unchanged so test rulesets without goods stay usable.
    /// </summary>
    public string StorageIdOf(string goodsId) =>
        _goodsById.TryGetValue(goodsId, out var g) ? g.StoredAs : goodsId;

    /// <summary>All building types, in specification order.</summary>
    public IReadOnlyList<BuildingType> BuildingTypes { get; }

    /// <summary>
    /// The set of goods ids that any <b>buildable</b> type (building, unit, or role) requires to construct — the
    /// "building material" category (FreeCol <c>GoodsType.isBuildingMaterial</c>, derived over all buildables). In
    /// the classic ruleset: <c>hammers</c> + <c>tools</c> (buildings/units), <c>food</c> (the free colonist's 200-food
    /// growth cost), and <c>muskets</c> + <c>horses</c> (the armed/mounted roles). Used by native tribute-demand goods
    /// selection (so a colony's food is demanded via the building-material rung under Angry/Hateful).
    /// </summary>
    public IReadOnlySet<string> BuildingMaterials { get; }

    /// <summary>Looks up a building type by ruleset id (e.g. <c>model.building.townHall</c>).</summary>
    /// <exception cref="KeyNotFoundException">Unknown id.</exception>
    public BuildingType Building(string id) =>
        _buildingById.TryGetValue(id, out var b)
            ? b
            : throw new KeyNotFoundException($"Unknown building type '{id}'.");

    /// <summary>Looks up a building type by id without throwing; returns null when no such building exists (used to tell a queued building id from a unit id).</summary>
    public BuildingType? FindBuilding(string id) => _buildingById.GetValueOrDefault(id);

    /// <summary>
    /// Looks up a <em>colony-constructable</em> unit type by id; null when the id is not one. A build-queue unit is a
    /// <b>non-person</b> type (artillery / wagon / ships) that needs a building material to construct — the free
    /// colonist is excluded by the <b>non-person</b> gate (its <c>required-goods food=200</c> is the born-in-colony
    /// growth threshold, a separate mechanism — FreeCol keeps colonists in a distinct population queue), not by the
    /// material check (food is a building material; see <see cref="BuildingMaterials"/>).
    /// </summary>
    public UnitType? FindBuildableUnit(string id) =>
        _unitById.TryGetValue(id, out var u) && IsColonyBuildableUnit(u) ? u : null;

    /// <summary>Unit types that can be constructed in a colony (artillery, wagon train, ships), in specification order.</summary>
    public IEnumerable<UnitType> BuildableUnitTypes => UnitTypes.Where(IsColonyBuildableUnit);

    /// <summary>A unit the colony build queue can hold: a non-person type (the gate that excludes the free colonist) that costs a building material (artillery/wagon/ships need hammers ± tools).</summary>
    private bool IsColonyBuildableUnit(UnitType unit) =>
        !unit.IsPerson && unit.BuildCostOrEmpty.Any(c => BuildingMaterials.Contains(c.GoodsId));

    /// <summary>All Founding Fathers, in specification order.</summary>
    public IReadOnlyList<FoundingFather> FoundingFathers { get; }

    /// <summary>All bonus-resource types, in specification order.</summary>
    public IReadOnlyList<ResourceType> ResourceTypes { get; }

    /// <summary>Looks up a bonus-resource type by ruleset id (e.g. <c>model.resource.minerals</c>).</summary>
    /// <exception cref="KeyNotFoundException">Unknown id.</exception>
    public ResourceType Resource(string id) =>
        _resourceById.TryGetValue(id, out var r)
            ? r
            : throw new KeyNotFoundException($"Unknown resource type '{id}'.");

    /// <summary>All tile-improvement types (river, road, plow, …), in specification order. Today only the river type is modelled/used.</summary>
    public IReadOnlyList<TileImprovementType> ImprovementTypes { get; }

    /// <summary>Looks up a tile-improvement type by ruleset id (e.g. <c>model.improvement.river</c>).</summary>
    /// <exception cref="KeyNotFoundException">Unknown id.</exception>
    public TileImprovementType Improvement(string id) =>
        _improvementById.TryGetValue(id, out var imp)
            ? imp
            : throw new KeyNotFoundException($"Unknown tile-improvement type '{id}'.");

    /// <summary>The river tile-improvement type (<c>model.improvement.river</c>), the one improvement the map generator places.</summary>
    /// <exception cref="KeyNotFoundException">The ruleset declares no river type.</exception>
    public TileImprovementType RiverType => Improvement(TileImprovementType.RiverId);

    /// <summary>All native nation types (Apache, Sioux, …), in specification order.</summary>
    public IReadOnlyList<NativeNationType> NativeNationTypes { get; }

    /// <summary>Looks up a native nation type by ruleset id (e.g. <c>model.nationType.apache</c>).</summary>
    /// <exception cref="KeyNotFoundException">Unknown id.</exception>
    public NativeNationType NativeNation(string id) =>
        _nativeNationById.TryGetValue(id, out var n)
            ? n
            : throw new KeyNotFoundException($"Unknown native nation type '{id}'.");

    /// <summary>All native settlement types (camp/village/city + capital variants), in specification order.</summary>
    public IReadOnlyList<SettlementType> SettlementTypes { get; }

    /// <summary>Looks up a native settlement type by ruleset id (e.g. <c>model.settlement.camp</c>).</summary>
    /// <exception cref="KeyNotFoundException">Unknown id.</exception>
    public SettlementType Settlement(string id) =>
        _settlementById.TryGetValue(id, out var s)
            ? s
            : throw new KeyNotFoundException($"Unknown settlement type '{id}'.");

    /// <summary>All military/equipment roles (default, soldier, dragoon, scout, brave roles, …), in specification order.</summary>
    public IReadOnlyList<RoleType> Roles { get; }

    /// <summary>Looks up a role by ruleset id (e.g. <c>model.role.soldier</c>).</summary>
    /// <exception cref="KeyNotFoundException">Unknown id.</exception>
    public RoleType Role(string id) =>
        _roleById.TryGetValue(id, out var r)
            ? r
            : throw new KeyNotFoundException($"Unknown role '{id}'.");

    /// <summary>
    /// All European nations (the colonial powers and their Royal Expeditionary Forces), in specification
    /// order. The classic playable powers are the non-REF selectable ones (Dutch, French, English, Spanish).
    /// </summary>
    public IReadOnlyList<EuropeanNation> EuropeanNations { get; }

    /// <summary>Looks up a European nation by ruleset id (e.g. <c>model.nation.dutch</c>).</summary>
    /// <exception cref="KeyNotFoundException">Unknown id.</exception>
    public EuropeanNation EuropeanNation(string id) =>
        _europeanNationById.TryGetValue(id, out var n)
            ? n
            : throw new KeyNotFoundException($"Unknown European nation '{id}'.");

    /// <summary>
    /// The unit-type change of a given change-type for a unit type, or null if that type
    /// does not change (FreeCol <c>UnitType.getUnitChange</c>). E.g.
    /// <c>GetUnitChange(UnitChangeTypeIds.Promotion, "model.unit.freeColonist")</c> → veteran soldier.
    /// </summary>
    public UnitChange? GetUnitChange(string changeTypeId, string fromUnitId) =>
        _unitChangeByType.TryGetValue(changeTypeId, out var byFrom)
        && byFrom.TryGetValue(fromUnitId, out var change)
            ? change
            : null;

    /// <summary>
    /// The probability (0–100, the spec <c>probability</c>) that a working colonist of <paramref name="fromUnitId"/>
    /// experience-upgrades to <paramref name="toExpertId"/> — 0 when no such on-the-job upgrade exists. Classic: every
    /// free-colonist → raw-goods-expert row is 4. Used by the colony turn's experience roll (FreeCol
    /// <c>model.unitChange.experience</c>). Distinct from <see cref="GetUnitChange"/>, which keeps only one row per
    /// source type and so cannot answer per-target experience probabilities.
    /// </summary>
    public int ExperienceUpgradeProbability(string fromUnitId, string toExpertId) =>
        _experienceUpgradeByFrom.TryGetValue(fromUnitId, out var toMap)
        && toMap.TryGetValue(toExpertId, out int probability)
            ? probability
            : 0;

    /// <summary>
    /// The unit type that is the expert producer of <paramref name="goodsId"/> (FreeCol
    /// <c>Specification.getExpertForProducing</c>): grain → expert farmer, fish → expert fisherman,
    /// cotton → master cotton planter, … or null when no unit type is its expert.
    /// </summary>
    public string? ExpertForProducing(string goodsId) => _expertForProducing.GetValueOrDefault(goodsId);

    /// <summary>
    /// The unit type a student of <paramref name="studentTypeId"/> becomes after ONE schooling cycle under a teacher of
    /// <paramref name="teacherTypeId"/> (FreeCol <c>UnitType.getTeachingType</c>), or null if the teacher cannot raise
    /// that student. The teacher imparts its <see cref="UnitType.SkillTaughtOrSelf"/>: a student already at/above that
    /// skill is ineligible; one already eligible for the taught type directly learns it (free colonist → expert ore
    /// miner); a lower student climbs ONE rung first (petty criminal → indentured servant → free colonist), recursing
    /// so each cycle advances a single step toward the teacher's expertise.
    /// </summary>
    public UnitType? GetTeachingType(string teacherTypeId, string studentTypeId)
    {
        string taught = Unit(teacherTypeId).SkillTaughtOrSelf;
        int taughtLevel = Unit(taught).Skill;
        if (Unit(studentTypeId).Skill >= taughtLevel || !_educationByFrom.TryGetValue(studentTypeId, out var rungs))
        {
            return null;
        }
        if (rungs.ContainsKey(taught))
        {
            return Unit(taught); // the student can learn the teacher's expertise directly this cycle
        }
        // Otherwise climb one rung whose own ladder can eventually reach the taught type (criminal → servant → free).
        foreach (string to in rungs.Keys.Where(t => Unit(t).Skill < taughtLevel).OrderBy(t => t, StringComparer.Ordinal))
        {
            if (GetTeachingType(teacherTypeId, to) is not null)
            {
                return Unit(to);
            }
        }
        return null;
    }

    /// <summary>
    /// The base number of turns a teacher of <paramref name="teacherTypeId"/> needs to raise a student of
    /// <paramref name="studentTypeId"/> one rung (FreeCol <c>Specification.getNeededTurnsOfTraining</c>; classic 4/6/8),
    /// or 0 if the teacher cannot teach that student. The caller applies the Sons-of-Liberty reduction (floored at 1).
    /// </summary>
    public int NeededTurnsOfTraining(string teacherTypeId, string studentTypeId) =>
        GetTeachingType(teacherTypeId, studentTypeId) is { } learn
        && _educationByFrom.TryGetValue(studentTypeId, out var rungs)
        && rungs.TryGetValue(learn.Id, out int turns)
            ? turns
            : 0;

    /// <summary>
    /// The role a victor in <paramref name="winnerRoleId"/> upgrades to by capturing the equipment of a
    /// defeated unit in <paramref name="loserRoleId"/>, or null if none is available to that owner
    /// (FreeCol <c>Unit.canCaptureEquipment</c>): the military role whose <c>role-change</c> matches, gated
    /// to the winner's side (native roles for natives, non-REF roles for non-REF units).
    /// </summary>
    public RoleType? CaptureRole(string winnerRoleId, string loserRoleId, bool winnerIsNative) =>
        Roles.FirstOrDefault(r =>
            r.RequiresNative == winnerIsNative
            && !r.RequiresRef
            && r.RoleChanges.Any(rc => rc.From == winnerRoleId && rc.Capture == loserRoleId));

    /// <summary>Looks up a Founding Father by ruleset id (e.g. <c>model.foundingFather.adamSmith</c>).</summary>
    /// <exception cref="KeyNotFoundException">Unknown id.</exception>
    public FoundingFather Father(string id) =>
        _fatherById.TryGetValue(id, out var f)
            ? f
            : throw new KeyNotFoundException($"Unknown founding father '{id}'.");

    /// <summary>
    /// Loads the classic (1994-faithful, Colonial-America) ruleset embedded in this
    /// assembly. Convenience for the default variant; equivalent to
    /// <c>GameVariants.ClassicAmerica.LoadRuleset()</c>.
    /// </summary>
    public static Ruleset LoadClassic(string difficultyLevelId = DifficultyLevels.DefaultId) =>
        LoadEmbedded(GameVariants.ClassicSpecResource, difficultyLevelId);

    /// <summary>
    /// Loads a ruleset from a specification embedded in this assembly (used by
    /// <see cref="GameVariant.LoadRuleset"/> to load the selected variant's data).
    /// </summary>
    /// <param name="resourceName">Manifest resource name of the embedded <c>specification.xml</c>.</param>
    /// <param name="difficultyLevelId">The difficulty level to apply (default <c>model.difficulty.medium</c>); see <see cref="ParseDifficulty"/>.</param>
    /// <exception cref="InvalidOperationException">No embedded resource with that name exists.</exception>
    public static Ruleset LoadEmbedded(string resourceName, string difficultyLevelId = DifficultyLevels.DefaultId)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using Stream stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded ruleset '{resourceName}' missing from assembly.");
        // Per-nation colony names ship as a sibling resource (FreeCol keeps them out of the spec XML).
        // A variant supplies its own by following the naming convention; absent names → no colony-name lists.
        string colonyNamesResource = resourceName.Replace("specification.xml", "european-nation-names.properties");
        using Stream? colonyNames = assembly.GetManifestResourceStream(colonyNamesResource);
        return Load(stream, colonyNames, difficultyLevelId);
    }

    /// <summary>Parses a ruleset from FreeCol-format specification XML.</summary>
    /// <param name="xml">The specification XML stream.</param>
    /// <param name="colonyNames">Optional FreeCol-format per-nation colony-name properties (null → European nations get empty colony-name lists).</param>
    /// <param name="difficultyLevelId">The difficulty level to apply (default <c>model.difficulty.medium</c>); see <see cref="ParseDifficulty"/>.</param>
    /// <exception cref="RulesetFormatException">The XML is missing required elements or attributes.</exception>
    public static Ruleset Load(
        Stream xml, Stream? colonyNames = null, string difficultyLevelId = DifficultyLevels.DefaultId)
    {
        XDocument doc = XDocument.Load(xml);
        XElement root = doc.Root
            ?? throw new RulesetFormatException("Specification has no root element.");

        XElement tileTypes = root.Element("tile-types")
            ?? throw new RulesetFormatException("Specification has no <tile-types> section.");

        var terrain = new Dictionary<string, TerrainType>();
        foreach (XElement el in tileTypes.Elements("tile-type"))
        {
            TerrainType parsed = ParseTileType(el);
            if (!terrain.TryAdd(parsed.Id, parsed))
            {
                throw new RulesetFormatException($"Duplicate tile-type id '{parsed.Id}'.");
            }
        }

        if (terrain.Count == 0)
        {
            throw new RulesetFormatException("Specification defines no tile types.");
        }

        XElement unitTypes = root.Element("unit-types")
            ?? throw new RulesetFormatException("Specification has no <unit-types> section.");
        Dictionary<string, UnitType> units = ParseUnitTypes(unitTypes);

        var goods = new Dictionary<string, GoodsType>();
        foreach (XElement el in root.Element("goods-types")?.Elements("goods-type") ?? [])
        {
            string id = RequiredAttribute(el, "id");
            XElement? market = el.Element("market");
            goods[id] = new GoodsType(
                Id: id,
                IsFood: (bool?)el.Attribute("is-food") ?? false,
                StoredAs: (string?)el.Attribute("stored-as") ?? id,
                MadeFrom: (string?)el.Attribute("made-from"),
                IsFarmed: (bool?)el.Attribute("is-farmed") ?? false,
                BreedingNumber: (int?)el.Attribute("breeding-number"),
                IsNewWorldGoods: (bool?)el.Attribute("new-world-goods") ?? false,
                IsStorable: (bool?)el.Attribute("storable") ?? true,
                IsMilitary: (bool?)el.Attribute("is-military") ?? false,
                IsTradeGoods: (bool?)el.Attribute("trade-goods") ?? false,
                Market: market is null ? null : new GoodsMarket(
                    InitialAmount: (int?)market.Attribute("initial-amount")
                        ?? throw new RulesetFormatException($"<market> in '{id}' lacks initial-amount."),
                    InitialPrice: (int?)market.Attribute("initial-price")
                        ?? throw new RulesetFormatException($"<market> in '{id}' lacks initial-price."),
                    PriceDifference: (int?)market.Attribute("price-difference")
                        ?? throw new RulesetFormatException($"<market> in '{id}' lacks price-difference.")));
        }

        var buildings = new Dictionary<string, BuildingType>();
        var buildingElements = new Dictionary<string, XElement>();
        foreach (XElement el in root.Element("building-types")?.Elements("building-type") ?? [])
        {
            buildingElements[RequiredAttribute(el, "id")] = el;
        }
        foreach ((string id, XElement el) in buildingElements)
        {
            // Productions: the building's own if defined, else inherited up the
            // extends chain. Attributes: nearest definition wins (FreeCol default
            // workplaces = 3).
            XElement? productionSource = el;
            while (productionSource is not null && !productionSource.Elements("production").Any())
            {
                string? parent = (string?)productionSource.Attribute("extends");
                productionSource = parent is not null ? buildingElements.GetValueOrDefault(parent) : null;
            }

            buildings[id] = new BuildingType(
                Id: id,
                UpgradesFrom: (string?)el.Attribute("upgrades-from"),
                Workplaces: ResolveIntAttribute(el, "workplaces", buildingElements) ?? 3,
                RequiredPopulation: ResolveIntAttribute(el, "required-population", buildingElements) ?? 1,
                Productions: (productionSource?.Elements("production") ?? [])
                    .Select(ParseProduction)
                    .ToList(),
                BuildCost: el.Elements("required-goods")
                    .Select(g => new GoodsOutput(
                        RequiredAttribute(g, "id"),
                        (int?)g.Attribute("value")
                            ?? throw new RulesetFormatException($"required-goods in '{id}' lacks value.")))
                    .ToList(),
                // The colony defence bonus (stockade +100, fort +150, fortress +200): the building's own
                // model.modifier.defence percentage. Skip any `delete="true"` marker (fort/fortress delete the
                // inherited modifier then re-add their own), taking the valued one. 0 for non-defence buildings.
                DefenceBonus: el.Elements("modifier")
                    .Where(m => (string?)m.Attribute("id") == "model.modifier.defence"
                                && (string?)m.Attribute("delete") != "true")
                    .Select(m => (int?)m.Attribute("value") ?? 0)
                    .DefaultIfEmpty(0)
                    .Last(),
                // Warehouse capacity: the model.modifier.warehouseStorage additive, summed up the extends chain
                // (depot 100; warehouse extends depot → 200; expansion extends warehouse → 300) — FreeCol resolves
                // an extending building's modifiers cumulatively, unlike the redefined (delete+readd) defence one.
                WarehouseStorage: SumModifierUpChain(el, "model.modifier.warehouseStorage", buildingElements),
                // Bell-production bonus (printing press +50, newspaper +100): the building's own model.goods.bells
                // percentage modifier, taking the valued (non-delete) one — the newspaper deletes the inherited 50
                // and re-adds 100, the same redefine pattern as the defence modifier above.
                BellBonus: el.Elements("modifier")
                    .Where(m => (string?)m.Attribute("id") == "model.goods.bells"
                                && (string?)m.Attribute("delete") != "true")
                    .Select(m => (int?)m.Attribute("value") ?? 0)
                    .DefaultIfEmpty(0)
                    .Last(),
                // Build-gating abilities (factory tier → buildFactory, custom house → buildCustomHouse, docks →
                // hasPort), collected down the extends chain so drydock/shipyard inherit docks' hasPort (FreeCol
                // BuildableType.getRequiredAbilities; nearest definition wins for a re-stated id).
                RequiredAbilities: CollectRequiredAbilitiesUpChain(el, buildingElements),
                // Ship repair: drydock grants model.ability.repairUnits, shipyard inherits it up the extends chain.
                RepairsNavalUnits: ResolveAbility(el, "model.ability.repairUnits", buildingElements),
                // Unit construction: model.ability.build scopes (carpenter's house → wagon train, armory →
                // artillery, shipyard → any naval unit), collected down the extends chain (magazine/arsenal inherit
                // armory's artillery scope) — drives the unit build-ability gate.
                BuildableUnitTypeIds: CollectBuildUnitTypeScopes(el, buildingElements),
                BuildsNavalUnits: GrantsNavalBuildScope(el, buildingElements),
                // Ship bombardment: the fort grants model.ability.bombardShips, the fortress inherits it.
                BombardsShips: ResolveAbility(el, "model.ability.bombardShips", buildingElements),
                // Auto-export: the custom house grants model.ability.export (per-turn auto-sell).
                GrantsExport: ResolveAbility(el, "model.ability.export", buildingElements),
                // Missionary ordination: the church grants model.ability.dressMissionary, the cathedral inherits it
                // (the chapel does not) — the colony-side requirement of the model.role.missionary role.
                DressesMissionary: ResolveAbility(el, "model.ability.dressMissionary", buildingElements),
                // Water work: the docks grant model.ability.produceInWater (drydock/shipyard inherit it down the
                // extends chain) — the colony-side requirement for assigning a colonist to a sea tile to fish.
                ProducesInWater: ResolveAbility(el, "model.ability.produceInWater", buildingElements),
                // Horse breeding: pasture/country sets breedingDivisor 50 / breedingFactor 2; stables multiplies the
                // divisor by 0.5 → 25 (resolved additive-then-multiplicative up the extends chain). 0 = not a breeder.
                BreedingDivisor: ResolveScalarModifierUpChain(el, "model.modifier.breedingDivisor", buildingElements),
                BreedingFactor: ResolveScalarModifierUpChain(el, "model.modifier.breedingFactor", buildingElements),
                // Rebel factor: the Sons-of-Liberty production bonus is multiplied by this before it is added to a
                // worker's output (lumber mill / cathedral ×2, factory tier ×1.5; default 1, nearest definition wins
                // up the extends chain). FreeCol ProductionUtils.getRebelProductionModifiersForBuilding.
                RebelFactor: ResolveDoubleAttribute(el, "rebel-factor", buildingElements) ?? 1.0,
                // Competence factor: a specialist's ADDITIVE production bonus is multiplied by this in this building
                // (lumber mill 2, the manufactory tier 2/3; default 1, nearest definition wins up the extends chain).
                // FreeCol BuildingType.getCompetenceModifiers — scales only the unit's additive modifiers, never its
                // multiplicative ones, so a master carpenter's +3 hammers becomes +6 here but a master distiller's ×2
                // rum is unchanged. Applied per worker in Game.RunBuildingProduction.
                CompetenceFactor: ResolveDoubleAttribute(el, "competence-factor", buildingElements) ?? 1.0,
                // Per-turn gold upkeep (lumber mill 10, blacksmith shop 5, iron works 15, …; default 0, resolved up the
                // extends chain like FreeCol's xr.getAttribute(UPKEEP_TAG, parent.upkeep)). Summed over a colony's
                // buildings and deducted from the owner's gold each turn — but only when the spec's enableUpkeep option
                // is on (classic leaves it off, so the classic game charges no upkeep). FreeCol Colony.getUpkeep.
                Upkeep: ResolveIntAttribute(el, "upkeep", buildingElements) ?? 0,
                // Teaching: a school's skill window (schoolhouse 1..1 / college 1..2 / university 1..4; the floor + the
                // teach ability are declared on the schoolhouse and inherited down the extends chain) — only an expert
                // within the window teaches.
                MaximumSkill: ResolveIntAttribute(el, "maximum-skill", buildingElements) ?? 0,
                Teaches: ResolveAbility(el, "model.ability.teach", buildingElements),
                MinimumSkill: ResolveIntAttribute(el, "minimum-skill", buildingElements) ?? 0);
        }

        var fathers = new Dictionary<string, FoundingFather>();
        foreach (XElement el in root.Element("founding-fathers")?.Elements("founding-father") ?? [])
        {
            string id = RequiredAttribute(el, "id");
            string typeName = RequiredAttribute(el, "type");
            if (!Enum.TryParse(typeName, ignoreCase: true, out FatherType type))
            {
                throw new RulesetFormatException($"Founding father '{id}' has unknown type '{typeName}'.");
            }
            fathers[id] = new FoundingFather(
                Id: id,
                Type: type,
                Weight1: (int?)el.Attribute("weight1") ?? 0,
                Weight2: (int?)el.Attribute("weight2") ?? 0,
                Weight3: (int?)el.Attribute("weight3") ?? 0,
                Modifiers: el.Elements("modifier").Select(ParseModifier).ToList(),
                Abilities: el.Elements("ability").Select(ParseAbility).ToList(),
                // Free buildings (FreeCol model.event.freeBuilding): La Salle → a free stockade per qualifying colony.
                FreeBuildings: el.Elements("event")
                    .Where(e => (string?)e.Attribute("id") == "model.event.freeBuilding")
                    .Select(e => RequiredAttribute(e, "value"))
                    .ToList(),
                // Free units (FreeCol founding-father <unit id="…"/>): John Paul Jones → a free frigate in Europe.
                FreeUnits: el.Elements("unit")
                    .Select(e => RequiredAttribute(e, "id"))
                    .ToList(),
                // Boycotts-lifted event (FreeCol model.event.boycottsLifted): Jacob Fugger clears all the player's boycotts.
                LiftsBoycotts: el.Elements("event")
                    .Any(e => (string?)e.Attribute("id") == "model.event.boycottsLifted"),
                // See-all-colonies event (FreeCol model.event.seeAllColonies): Coronado reveals every colony on election.
                RevealsAllColonies: el.Elements("event")
                    .Any(e => (string?)e.Attribute("id") == "model.event.seeAllColonies"));
        }

        var resources = new Dictionary<string, ResourceType>();
        foreach (XElement el in root.Element("resource-types")?.Elements("resource-type") ?? [])
        {
            string id = RequiredAttribute(el, "id");
            resources[id] = new ResourceType(
                Id: id,
                Modifiers: el.Elements("modifier").Select(ParseResourceModifier).ToList(),
                // Starting-quantity range (FreeCol resource-type minimum-value/maximum-value); absent → 0 = limitless.
                MinValue: (int?)el.Attribute("minimum-value") ?? 0,
                MaxValue: (int?)el.Attribute("maximum-value") ?? 0);
        }

        // Tile-improvement types (FreeCol <tile-improvement-types>): the natural/pioneer features laid on a tile —
        // the natural river (placed by the generator) and the pioneer-built road/plow/clear-forest. We read each
        // type's applicability scopes, required role + tool cost, and any terrain transformation (clear-forest).
        var improvements = new Dictionary<string, TileImprovementType>();
        foreach (XElement el in root.Element("tile-improvement-types")?.Elements("tile-improvement-type") ?? [])
        {
            string id = RequiredAttribute(el, "id");
            improvements[id] = new TileImprovementType(
                Id: id,
                Magnitude: (int?)el.Attribute("magnitude") ?? 1,
                MovementCost: (int?)el.Attribute("movement-cost") ?? 0,
                AddWorkTurns: (int?)el.Attribute("add-work-turns") ?? 0,
                Modifiers: el.Elements("modifier").Select(ParseImprovementModifier).ToList(),
                IsNatural: (bool?)el.Attribute("natural") ?? false,
                RequiredRoleId: (string?)el.Attribute("required-role"),
                ExpendedAmount: (int?)el.Attribute("expended-amount") ?? 0,
                ExposeResourcePercent: (int?)el.Attribute("expose-resource-percent") ?? 0,
                Scopes: el.Elements("scope").Select(ParseImprovementScope).ToList(),
                TileTypeChanges: el.Elements("tile-type-change")
                    .ToDictionary(c => RequiredAttribute(c, "from"), ParseTileTypeChange));
        }

        (Dictionary<string, NativeNationType> nativeNations, Dictionary<string, SettlementType> settlements) =
            ParseNativeNationTypes(root.Element("indian-nation-types"));

        Dictionary<string, RoleType> roles = ParseRoles(root.Element("roles"));
        Dictionary<string, Disaster> disasters = ParseDisasters(root.Element("disasters"));
        Dictionary<string, Dictionary<string, UnitChange>> unitChanges =
            ParseUnitChanges(root.Element("unit-change-types"));
        Dictionary<string, Dictionary<string, int>> experienceUpgrades =
            ParseExperienceUpgrades(root.Element("unit-change-types"));
        Dictionary<string, Dictionary<string, int>> educationTurns =
            ParseEducationTurns(root.Element("unit-change-types"));

        Dictionary<string, EuropeanNationType> europeanNationTypes =
            ParseEuropeanNationTypes(root.Element("european-nation-types"));
        Dictionary<string, EuropeanNation> europeanNations = ParseEuropeanNations(
            root.Element("nations"), europeanNationTypes, ParseColonyNames(colonyNames));

        // The spec <events> section (declare-independence + Spanish-succession events, each with its <limit> gates).
        // Missing → an empty map, so a spec without events still loads (the default game's hardcoded paths fall back).
        Dictionary<string, SpecEvent> events = ParseEvents(root.Element("events"));

        Calendar calendar = ParseCalendar(root);
        IReadOnlyList<int> fatherAgeYears = ParseFatherAgeYears(root, calendar.StartingYear);
        DifficultyOptions difficulty = ParseDifficulty(root, difficultyLevelId);
        // The base gameOptions group (immigration trio: initialImmigration / europeanUnitImmigrationPenalty /
        // playerImmigrationBonus) — read once into a bundle; a spec without an option falls back to its classic value.
        GameOptions gameOptions = ParseGameOptions(root);
        // Building upkeep is a boolean game option (model.option.enableUpkeep); classic ships it defaultValue="false",
        // so the default game charges no upkeep and stays byte-identical (FreeCol gates csPayUpkeep on this option).
        bool upkeepEnabled = ParseBooleanOption(root, "model.option.enableUpkeep", fallback: false);
        // Natural-disaster chance is a percentage game option (model.option.naturalDisasters); classic ships it
        // defaultValue="0", so the default game rolls no disasters and stays byte-identical (FreeCol only calls
        // csNaturalDisasters when this option is > 0).
        int naturalDisasterPercentage = ParsePercentageOption(root, "model.option.naturalDisasters", fallback: 0);
        // The last colonial game year (model.option.lastColonialYear, in the gameOptions.years group); classic value
        // 1800. Past this year a colonial power may no longer declare independence (FreeCol model.limit.independence.year).
        int lastColonialYear = ParseIntOption(root, "model.option.lastColonialYear", fallback: 1800);
        // The foreign-intervention options (interventionBells / interventionTurns / interventionForce) live in the
        // chosen difficulty level's monarch group (classic medium 5000 / 52 / the 8-unit ally force). Parsed from the
        // selected level so a variant resizes the ally by data alone.
        (int interventionBells, int interventionTurns, InterventionForceComposition interventionForce) =
            ParseIntervention(root, difficultyLevelId);
        // The alternative victory conditions (gameOptions.victoryConditions group); classic defaults: REF + Europeans
        // on, Humans off. These gate the pure victory reads in Game.Independence.cs; a spec without them falls back to
        // the classic defaults, so the default game is unchanged (ADR-009).
        bool victoryDefeatRef = ParseBooleanOption(root, "model.option.victoryDefeatREF", fallback: true);
        bool victoryDefeatEuropeans = ParseBooleanOption(root, "model.option.victoryDefeatEuropeans", fallback: true);
        bool victoryDefeatHumans = ParseBooleanOption(root, "model.option.victoryDefeatHumans", fallback: false);

        return new Ruleset(
            terrain, units, goods, buildings, fathers, resources, improvements, nativeNations, settlements,
            roles, disasters, unitChanges, experienceUpgrades, educationTurns, europeanNations, events, calendar, fatherAgeYears,
            difficulty, gameOptions, difficultyLevelId, upkeepEnabled, naturalDisasterPercentage, lastColonialYear,
            interventionBells, interventionTurns, interventionForce,
            victoryDefeatRef, victoryDefeatEuropeans, victoryDefeatHumans);
    }

    /// <summary>
    /// Parses a difficulty level (default <c>model.difficulty.medium</c>) into <see cref="DifficultyOptions"/>.
    /// Unlike the other option parses, difficulty options are restated under <em>every</em> level group, so this
    /// FIRST selects the chosen level's subtree, THEN reads option values within it — reading over the whole document
    /// would pick up the first level (<c>veryEasy</c>). A missing level, or a missing option within it, falls back to
    /// <see cref="DifficultyOptions.ClassicMedium"/>. (FreeCol <c>Specification.applyDifficultyLevel</c>.)
    /// </summary>
    internal static DifficultyOptions ParseDifficulty(XElement root, string levelId = "model.difficulty.medium")
    {
        XElement? level = root.Descendants("optionGroup")
            .FirstOrDefault(g => (string?)g.Attribute("id") == levelId);
        if (level is null)
        {
            return DifficultyOptions.ClassicMedium;
        }

        int IntOption(string id, int fallback) =>
            level.Descendants("integerOption")
                .Where(o => (string?)o.Attribute("id") == id)
                .Select(o => ParseInt((string?)o.Attribute("value")))
                .FirstOrDefault(v => v is not null) ?? fallback;

        // Percentages are a distinct element (<percentageOption>) but carry the same `value` attribute.
        int PctOption(string id, int fallback) =>
            level.Descendants("percentageOption")
                .Where(o => (string?)o.Attribute("id") == id)
                .Select(o => ParseInt((string?)o.Attribute("value")))
                .FirstOrDefault(v => v is not null) ?? fallback;

        // A REF entry's <number> within the <unitListOption id="model.option.refSize"> group: each block (soldiers,
        // dragoons, …) is a <unitOption> carrying a <number value="…"/>. Reads it by the block's option id.
        int RefSize(string unitOptionId, int fallback) =>
            level.Descendants("unitOption")
                .Where(o => (string?)o.Attribute("id") == unitOptionId)
                .Select(o => ParseInt((string?)o.Element("number")?.Attribute("value")))
                .FirstOrDefault(v => v is not null) ?? fallback;

        // The King's war-support force: a <unitListOption id="model.option.warSupportForce"> of <unitOption> blocks,
        // each a <unitType>/<role>/<number>. Parses every block into a MonarchSupportUnit; falls back if absent/empty.
        IReadOnlyList<MonarchSupportUnit> WarSupportForce(IReadOnlyList<MonarchSupportUnit> fallback)
        {
            XElement? listOption = level.Descendants("unitListOption")
                .FirstOrDefault(o => (string?)o.Attribute("id") == "model.option.warSupportForce");
            if (listOption is null)
            {
                return fallback;
            }
            var blocks = listOption.Elements("unitOption")
                .Select(u => new MonarchSupportUnit(
                    UnitTypeId: (string?)u.Element("unitType")?.Attribute("value") ?? "",
                    RoleId: (string?)u.Element("role")?.Attribute("value"),
                    Number: ParseInt((string?)u.Element("number")?.Attribute("value")) ?? 0))
                .Where(b => b.UnitTypeId.Length > 0 && b.Number > 0)
                .ToList();
            return blocks.Count > 0 ? blocks : fallback;
        }

        DifficultyOptions m = DifficultyOptions.ClassicMedium;
        GovernmentLimits medium = GovernmentLimits.ClassicMedium;
        MonarchOptions mon = MonarchOptions.ClassicMedium;
        return new DifficultyOptions(
            FoundingFatherFactor: IntOption("model.option.foundingFatherFactor", DifficultyOptions.ClassicMedium.FoundingFatherFactor),
            UnitsThatUseNoBells: IntOption("model.option.unitsThatUseNoBells", DifficultyOptions.ClassicMedium.UnitsThatUseNoBells),
            Government: new GovernmentLimits(
                VeryGood: IntOption("model.option.veryGoodGovernmentLimit", medium.VeryGood),
                Good: IntOption("model.option.goodGovernmentLimit", medium.Good),
                Bad: IntOption("model.option.badGovernmentLimit", medium.Bad),
                VeryBad: IntOption("model.option.veryBadGovernmentLimit", medium.VeryBad)),
            LandPriceFactor: IntOption("model.option.landPriceFactor", m.LandPriceFactor),
            NativeDemands: IntOption("model.option.nativeDemands", m.NativeDemands),
            NativeConvertProbability: IntOption("model.option.nativeConvertProbability", m.NativeConvertProbability),
            BurnProbability: IntOption("model.option.burnProbability", m.BurnProbability),
            RumourDifficulty: IntOption("model.option.rumourDifficulty", m.RumourDifficulty),
            RumourBadPercent: PctOption("model.option.badRumour", m.RumourBadPercent),
            RumourGoodPercent: PctOption("model.option.goodRumour", m.RumourGoodPercent),
            CrossesIncrement: IntOption("model.option.crossesIncrement", m.CrossesIncrement),
            RecruitPriceIncrease: IntOption("model.option.recruitPriceIncrease", m.RecruitPriceIncrease),
            RecruitLowerCapIncrease: IntOption("model.option.lowerCapIncrease", m.RecruitLowerCapIncrease),
            ArtilleryPriceIncrease: IntOption("model.option.priceIncrease.artillery", m.ArtilleryPriceIncrease),
            TreasureTransportFee: IntOption("model.option.treasureTransportFee", m.TreasureTransportFee),
            ShipTradePenalty: IntOption("model.option.shipTradePenalty", m.ShipTradePenalty),
            Monarch: new MonarchOptions(
                Meddling: IntOption("model.option.monarchMeddling", mon.Meddling),
                MaximumTaxRate: IntOption("model.option.maximumTax", mon.MaximumTaxRate),
                TaxAdjustment: IntOption("model.option.taxAdjustment", mon.TaxAdjustment),
                MercenaryPricePercent: IntOption("model.option.mercenaryPrice", mon.MercenaryPricePercent),
                SupportLandMountedUnits: IntOption("model.option.monarchSupport", mon.SupportLandMountedUnits),
                // ArrearsFactor: the classic game has always used 300 here, not the spec's medium 500; kept at the
                // const-preserving default (not read from model.option.arrearsFactor) so behaviour is byte-identical.
                ArrearsFactor: mon.ArrearsFactor,
                RefBaseInfantry: RefSize("model.option.refSize.soldiers", mon.RefBaseInfantry),
                RefBaseCavalry: RefSize("model.option.refSize.dragoons", mon.RefBaseCavalry),
                RefBaseArtillery: RefSize("model.option.refSize.artillery", mon.RefBaseArtillery),
                RefBaseManOWar: RefSize("model.option.refSize.menOfWar", mon.RefBaseManOWar),
                WarSupportForce: WarSupportForce(mon.WarSupportForce),
                WarSupportGold: IntOption("model.option.warSupportGold", mon.WarSupportGold)),
            // Ai: FreeCol scales none of the rival-AI constants by difficulty (the colony cap, seek ladder and our
            // Europe spend floor are all hardcoded in EuropeanAIPlayer — see AiTuning), so there is no spec option to
            // read. Kept at the classic-medium value across every level (the ArrearsFactor pattern), data-overridable.
            Ai: m.Ai);
    }

    /// <summary>
    /// Parses the base <c>gameOptions</c> group into <see cref="Specification.GameOptions"/> — the immigration trio
    /// (<c>model.option.initialImmigration</c> / <c>europeanUnitImmigrationPenalty</c> / <c>playerImmigrationBonus</c>).
    /// Unlike <see cref="ParseDifficulty"/>, these are not restated per level, so each is a plain document-wide
    /// integer-option lookup (its <c>value</c>, else <c>defaultValue</c>); a missing option falls back to its classic
    /// value in <see cref="GameOptions.ClassicDefaults"/>, so the default game is byte-identical (ADR-009).
    /// </summary>
    internal static GameOptions ParseGameOptions(XElement root) =>
        new(
            InitialImmigration: ParseIntOption(
                root, "model.option.initialImmigration", GameOptions.ClassicDefaults.InitialImmigration),
            EuropeanUnitImmigrationPenalty: ParseIntOption(
                root, "model.option.europeanUnitImmigrationPenalty", GameOptions.ClassicDefaults.EuropeanUnitImmigrationPenalty),
            PlayerImmigrationBonus: ParseIntOption(
                root, "model.option.playerImmigrationBonus", GameOptions.ClassicDefaults.PlayerImmigrationBonus));

    /// <summary>
    /// Parses the spec <c>model.option.ages</c> text option (classic <c>"1600,1700"</c>) into the two ascending
    /// founding-father age-year thresholds. Faithful to FreeCol's <c>Specification.clean</c> "badAges" handling:
    /// the option must yield exactly <c>NUMBER_OF_AGES − 1 = 2</c> integer years, each at or after
    /// <paramref name="startingYear"/> (FreeCol rejects a year that converts to turn &lt; 1), otherwise it falls back
    /// to the classic <c>1600, 1700</c>; out-of-order years are sorted ascending.
    /// </summary>
    internal static IReadOnlyList<int> ParseFatherAgeYears(XElement root, int startingYear)
    {
        int[] fallback = [1600, 1700];
        string? raw = root.Descendants("textOption")
            .Where(o => (string?)o.Attribute("id") == "model.option.ages")
            .Select(o => (string?)o.Attribute("value") ?? (string?)o.Attribute("defaultValue"))
            .FirstOrDefault(v => v is not null);
        if (raw is null)
        {
            return fallback;
        }

        int[] years = raw.Split(',')
            .Select(s => ParseInt(s.Trim()))
            .Where(v => v is not null)
            .Select(v => v!.Value)
            .ToArray();
        if (years.Length != 2 || years.Any(y => y < startingYear)) // FreeCol's badAges: count + the turn-≥-1 (year-≥-start) clamp
        {
            return fallback;
        }

        Array.Sort(years);
        return years;
    }

    /// <summary>
    /// Parses the spec <c>gameOptions.years</c> option group into a <see cref="Specification.Calendar"/>
    /// (<c>startingYear</c>/<c>seasonYear</c>/<c>seasons</c>). Any option absent falls back to the classic default,
    /// so a spec without the group still yields a working 1492/1600/2 calendar.
    /// </summary>
    internal static Calendar ParseCalendar(XElement root)
    {
        int YearsOption(string id, int fallback) =>
            root.Descendants("integerOption")
                .Where(o => (string?)o.Attribute("id") == id)
                .Select(o => ParseInt((string?)o.Attribute("value")))
                .FirstOrDefault(v => v is not null) ?? fallback;

        return new Calendar(
            StartingYear: YearsOption("model.option.startingYear", Calendar.Classic.StartingYear),
            SeasonYear: YearsOption("model.option.seasonYear", Calendar.Classic.SeasonYear),
            Seasons: YearsOption("model.option.seasons", Calendar.Classic.Seasons));
    }

    /// <summary>
    /// Reads a top-level <c>&lt;booleanOption&gt;</c> game option by id (its <c>value</c>, else <c>defaultValue</c>),
    /// falling back to <paramref name="fallback"/> when the option is absent or has no parseable boolean. Used for
    /// <c>model.option.enableUpkeep</c> (classic default false).
    /// </summary>
    internal static bool ParseBooleanOption(XElement root, string id, bool fallback)
    {
        XElement? option = root.Descendants("booleanOption")
            .FirstOrDefault(o => (string?)o.Attribute("id") == id);
        if (option is null)
        {
            return fallback;
        }
        return (bool?)option.Attribute("value") ?? (bool?)option.Attribute("defaultValue") ?? fallback;
    }

    /// <summary>
    /// Reads a top-level <c>&lt;integerOption&gt;</c> game option by id (its <c>value</c>, else <c>defaultValue</c>),
    /// falling back to <paramref name="fallback"/> when the option is absent or has no parseable integer. Used for
    /// <c>model.option.lastColonialYear</c> (classic default 1800).
    /// </summary>
    internal static int ParseIntOption(XElement root, string id, int fallback)
    {
        XElement? option = root.Descendants("integerOption")
            .FirstOrDefault(o => (string?)o.Attribute("id") == id);
        if (option is null)
        {
            return fallback;
        }
        return ParseInt((string?)option.Attribute("value"))
            ?? ParseInt((string?)option.Attribute("defaultValue"))
            ?? fallback;
    }

    /// <summary>
    /// Reads a top-level <c>&lt;percentageOption&gt;</c> game option by id (its <c>value</c>, else <c>defaultValue</c>),
    /// falling back to <paramref name="fallback"/> when the option is absent or has no parseable integer. Used for
    /// <c>model.option.naturalDisasters</c> (classic default 0).
    /// </summary>
    internal static int ParsePercentageOption(XElement root, string id, int fallback)
    {
        XElement? option = root.Descendants("percentageOption")
            .FirstOrDefault(o => (string?)o.Attribute("id") == id);
        if (option is null)
        {
            return fallback;
        }
        return ParseInt((string?)option.Attribute("value"))
            ?? ParseInt((string?)option.Attribute("defaultValue"))
            ?? fallback;
    }

    /// <summary>
    /// Parses the <c>&lt;disasters&gt;</c> block into <see cref="Disaster"/>s (FreeCol <c>Disaster</c>). Resolves the
    /// classic <c>extends="model.disaster.common"</c> inheritance: a child with no effects of its own inherits its
    /// parent's effect list (the natural disasters all extend the abstract <c>common</c> disaster, which carries the
    /// shared effect set). An <c>abstract="true"</c> definition is parsed for inheritance but excluded from the
    /// resulting ruleset (FreeCol never instantiates abstract types). Effects are mapped to the discrete kinds we
    /// resolve (<see cref="DisasterEffectKind"/>); an effect whose id we do not model (e.g. lossOfUnit/lossOfBuilding/
    /// damagedUnit — these need full unit/building damage we have not yet wired) is skipped, so a struck colony applies
    /// only the modelled subset (faithful subset; documented in docs/systems/colonies.md). Order is spec order.
    /// </summary>
    internal static Dictionary<string, Disaster> ParseDisasters(XElement? disastersElement)
    {
        var result = new Dictionary<string, Disaster>();
        if (disastersElement is null)
        {
            return result;
        }

        // First pass: index every <disaster> element (incl. abstract parents) so an extends child can find its parent.
        Dictionary<string, XElement> byId = disastersElement.Elements("disaster")
            .Where(e => e.Attribute("id") is not null)
            .GroupBy(e => (string)e.Attribute("id")!)
            .ToDictionary(g => g.Key, g => g.Last());

        foreach (XElement el in disastersElement.Elements("disaster"))
        {
            string id = RequiredAttribute(el, "id");
            if ((bool?)el.Attribute("abstract") == true)
            {
                continue; // abstract parent: usable for inheritance, never instantiated
            }

            // Inherit attributes/effects from the extends parent (one level, matching classic's single-level chain).
            XElement? parent = (string?)el.Attribute("extends") is { } parentId && byId.TryGetValue(parentId, out var p)
                ? p
                : null;
            XElement effectSource = el.Elements("effect").Any() ? el : (parent ?? el);

            bool natural = (bool?)el.Attribute("natural")
                ?? (parent is not null ? (bool?)parent.Attribute("natural") : null)
                ?? false;
            DisasterEffects number = ParseDisasterEffects((string?)el.Attribute("effects")
                ?? (parent is not null ? (string?)parent.Attribute("effects") : null));

            var effects = effectSource.Elements("effect")
                .Select(ParseDisasterEffect)
                .Where(e => e is not null)
                .Select(e => e!)
                .ToList();

            result[id] = new Disaster(id, natural, number, effects);
        }
        return result;
    }

    private static DisasterEffects ParseDisasterEffects(string? value) => value?.ToLowerInvariant() switch
    {
        "several" => DisasterEffects.Several,
        "all" => DisasterEffects.All,
        _ => DisasterEffects.One,
    };

    /// <summary>Maps a single <c>&lt;effect&gt;</c> to a <see cref="DisasterEffect"/>, or null when its id is one we do not yet model.</summary>
    private static DisasterEffect? ParseDisasterEffect(XElement el)
    {
        string id = RequiredAttribute(el, "id");
        int probability = (int?)el.Attribute("probability") ?? 0;
        DisasterEffectKind? kind = id switch
        {
            "model.disaster.effect.lossOfMoney" => DisasterEffectKind.LossOfMoney,
            "model.disaster.effect.lossOfGoods" => DisasterEffectKind.LossOfGoods,
            "model.disaster.effect.lossOfTileProduction" => DisasterEffectKind.ProductionPenalty,
            "model.disaster.effect.lossOfBuildingProduction" => DisasterEffectKind.ProductionPenalty,
            _ => null, // lossOfUnit / lossOfBuilding / damagedUnit: not yet modelled (faithful subset)
        };
        if (kind is null)
        {
            return null;
        }
        var modifiers = el.Elements("modifier")
            .Select(m => new DisasterModifier(
                GoodsId: RequiredAttribute(m, "id"),
                Type: (string?)m.Attribute("type") switch
                {
                    "multiplicative" => ModifierType.Multiplicative,
                    "percentage" => ModifierType.Percentage,
                    _ => ModifierType.Additive,
                },
                Value: (double?)m.Attribute("value") ?? 0,
                Duration: (int?)m.Attribute("duration") ?? 0))
            .ToList();
        return new DisasterEffect(kind.Value, probability, modifiers);
    }

    /// <summary>
    /// Parses the foreign-intervention options from the chosen difficulty level's <c>monarch</c> group (FreeCol
    /// <c>GameOptions.INTERVENTION_BELLS</c>/<c>INTERVENTION_TURNS</c>/<c>INTERVENTION_FORCE</c>): the bell threshold,
    /// the growth period, and the ally force composition. Like <see cref="ParseDifficulty"/>, these options are
    /// restated under every level, so this FIRST selects the chosen level's subtree (avoiding the first <c>veryEasy</c>
    /// block), THEN reads within it. A missing level — or any missing option — falls back to the classic-medium values
    /// (5000 / 52 / the 8-unit ally force), so the default game and a spec without the options are unchanged.
    /// </summary>
    internal static (int Bells, int Turns, InterventionForceComposition Force) ParseIntervention(XElement root, string levelId = "model.difficulty.medium")
    {
        InterventionForceComposition fallbackForce = InterventionForceComposition.ClassicMedium;
        XElement? level = root.Descendants("optionGroup")
            .FirstOrDefault(g => (string?)g.Attribute("id") == levelId);
        if (level is null)
        {
            return (InterventionForceComposition.ClassicMediumBells, InterventionForceComposition.ClassicMediumTurns, fallbackForce);
        }

        int IntOption(string id, int fallback) =>
            level.Descendants("integerOption")
                .Where(o => (string?)o.Attribute("id") == id)
                .Select(o => ParseInt((string?)o.Attribute("value")))
                .FirstOrDefault(v => v is not null) ?? fallback;

        // The ally force: a <unitListOption id="model.option.interventionForce"> of <unitOption> blocks, each a
        // <unitType>/<role>/<number>. Parses every block into a unit; falls back to the classic-medium force if absent.
        XElement? forceOption = level.Descendants("unitListOption")
            .FirstOrDefault(o => (string?)o.Attribute("id") == "model.option.interventionForce");
        IReadOnlyList<InterventionForceUnit> force = forceOption is null
            ? fallbackForce.Units
            : forceOption.Elements("unitOption")
                .Select(u => new InterventionForceUnit(
                    UnitTypeId: (string?)u.Element("unitType")?.Attribute("value") ?? "",
                    RoleId: (string?)u.Element("role")?.Attribute("value"),
                    Count: ParseInt((string?)u.Element("number")?.Attribute("value")) ?? 0))
                .Where(b => b.UnitTypeId.Length > 0 && b.Count > 0)
                .ToList();
        if (force.Count == 0)
        {
            force = fallbackForce.Units;
        }

        return (
            IntOption("model.option.interventionBells", InterventionForceComposition.ClassicMediumBells),
            IntOption("model.option.interventionTurns", InterventionForceComposition.ClassicMediumTurns),
            new InterventionForceComposition(force));
    }

    /// <summary>Parses an integer attribute value, returning <c>null</c> for a missing or non-numeric string.</summary>
    private static int? ParseInt(string? value) =>
        int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int n)
            ? n
            : null;

    /// <summary>
    /// Parses the <c>&lt;european-nation-types&gt;</c> section, resolving <c>extends</c> chains: starting
    /// units are taken from the nearest level that defines a slot (keeping a slot's expert variant), while
    /// abilities and modifiers accumulate from the whole chain. A null section yields no types.
    /// </summary>
    private static Dictionary<string, EuropeanNationType> ParseEuropeanNationTypes(XElement? section)
    {
        var types = new Dictionary<string, EuropeanNationType>();
        if (section is null)
        {
            return types;
        }

        var elements = new Dictionary<string, XElement>();
        foreach (XElement el in section.Elements("european-nation-type"))
        {
            string id = RequiredAttribute(el, "id");
            if (!elements.TryAdd(id, el))
            {
                throw new RulesetFormatException($"Duplicate european-nation-type id '{id}'.");
            }
        }

        foreach ((string id, XElement el) in elements)
        {
            var chain = ExtendsChain(el, elements).ToList(); // leaf → root
            var bySlot = new Dictionary<string, List<EuropeanStartingUnit>>();
            foreach (XElement level in chain)
            {
                foreach (var slot in level.Elements("unit").GroupBy(u => RequiredAttribute(u, "id")))
                {
                    if (bySlot.ContainsKey(slot.Key))
                    {
                        continue; // a nearer level already defined this slot (override)
                    }
                    bySlot[slot.Key] = slot.Select(u => new EuropeanStartingUnit(
                        Slot: slot.Key,
                        UnitTypeId: RequiredAttribute(u, "type"),
                        RoleId: (string?)u.Attribute("role"),
                        Mounted: (bool?)u.Attribute("mounted") ?? false,
                        Expert: (bool?)u.Attribute("expert-starting-units") ?? false)).ToList();
                }
            }
            types[id] = new EuropeanNationType(
                Id: id,
                IsRef: (bool?)el.Attribute("ref") ?? false,
                StartingUnits: bySlot.Values.SelectMany(u => u).ToList(),
                Abilities: chain.SelectMany(c => c.Elements("ability")).Select(ParseAbility).ToList(),
                Modifiers: chain.SelectMany(c => c.Elements("modifier")).Select(ParseModifier).ToList());
        }
        return types;
    }

    /// <summary>
    /// Parses the European <c>&lt;nation&gt;</c> rows, resolving each to its <see cref="EuropeanNationType"/>
    /// and per-nation colony names. Nations whose <c>nation-type</c> is not a European type (the native
    /// nations) and the <c>unknownEnemy</c> pseudo-nation are skipped. A null section yields no nations.
    /// </summary>
    private static Dictionary<string, EuropeanNation> ParseEuropeanNations(
        XElement? section,
        Dictionary<string, EuropeanNationType> types,
        Dictionary<string, IReadOnlyList<string>> colonyNamesByNation)
    {
        var nations = new Dictionary<string, EuropeanNation>();
        foreach (XElement el in section?.Elements("nation") ?? [])
        {
            string id = RequiredAttribute(el, "id");
            if (id == "model.nation.unknownEnemy")
            {
                continue; // the no-owner pseudo-nation, not a real power
            }
            // Native nations carry an indian-nation-type here — they are handled separately; skip them.
            if (!types.TryGetValue(RequiredAttribute(el, "nation-type"), out EuropeanNationType? type))
            {
                continue;
            }
            nations[id] = new EuropeanNation(
                Id: id,
                DisplayName: DeriveNationDisplayName(id),
                NationType: type,
                Color: (string?)el.Attribute("color"),
                Selectable: (bool?)el.Attribute("selectable") ?? false,
                RefNationId: (string?)el.Attribute("ref"),
                ColonyNames: colonyNamesByNation.GetValueOrDefault(id, []));
        }
        return nations;
    }

    /// <summary>A player-facing nation name derived from its id: <c>model.nation.dutch</c> → <c>Dutch</c>.</summary>
    private static string DeriveNationDisplayName(string id)
    {
        string shortName = id[(id.LastIndexOf('.') + 1)..];
        return shortName.Length == 0 ? shortName : char.ToUpperInvariant(shortName[0]) + shortName[1..];
    }

    /// <summary>
    /// Parses FreeCol-format colony-name properties (<c>model.nation.&lt;id&gt;.settlementName.classic.&lt;n&gt;=Name</c>)
    /// into per-nation lists ordered by index. A null stream yields no names.
    /// </summary>
    private static Dictionary<string, IReadOnlyList<string>> ParseColonyNames(Stream? properties)
    {
        var byNation = new Dictionary<string, List<(int Index, string Name)>>();
        if (properties is not null)
        {
            const string marker = ".settlementName.classic.";
            using var reader = new StreamReader(properties);
            for (string? line = reader.ReadLine(); line is not null; line = reader.ReadLine())
            {
                int eq = line.IndexOf('=');
                if (line.StartsWith('#') || eq < 0)
                {
                    continue;
                }
                string key = line[..eq].Trim();
                int m = key.IndexOf(marker, StringComparison.Ordinal);
                if (m < 0 || !int.TryParse(key[(m + marker.Length)..], out int index))
                {
                    continue;
                }
                string nationId = key[..m];
                if (!byNation.TryGetValue(nationId, out var list))
                {
                    byNation[nationId] = list = [];
                }
                list.Add((index, line[(eq + 1)..].Trim()));
            }
        }
        return byNation.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<string>)kv.Value.OrderBy(x => x.Index).Select(x => x.Name).ToList());
    }

    /// <summary>
    /// Parses the <c>&lt;roles&gt;</c> section into role types. Offence/defence are summed from each
    /// role's index-30 additive combat modifiers; required-goods, abilities, downgrade and capture
    /// rules carry over verbatim. A null section (minimal test rulesets) yields no roles.
    /// </summary>
    private static Dictionary<string, RoleType> ParseRoles(XElement? section)
    {
        var roles = new Dictionary<string, RoleType>();
        foreach (XElement el in section?.Elements("role") ?? [])
        {
            string id = RequiredAttribute(el, "id");

            double SumModifier(string modifierId) => el.Elements("modifier")
                .Where(m => (string?)m.Attribute("id") == modifierId
                            && (string?)m.Attribute("type") is null or "additive")
                .Sum(m => (double?)m.Attribute("value") ?? 0);

            var parsed = new RoleType(
                Id: id,
                Downgrade: (string?)el.Attribute("downgrade"),
                MaximumCount: (int?)el.Attribute("maximum-count") ?? 1,
                ExpertUnit: (string?)el.Attribute("expert-unit"),
                Offence: SumModifier("model.modifier.offence"),
                Defence: SumModifier("model.modifier.defence"),
                MovementBonus: SumModifier("model.modifier.movementBonus"),
                LineOfSightBonus: SumModifier("model.modifier.lineOfSightBonus"),
                RequiredGoods: el.Elements("required-goods")
                    .Select(g => new RoleRequiredGoods(
                        RequiredAttribute(g, "id"),
                        (int?)g.Attribute("value")
                            ?? throw new RulesetFormatException($"required-goods in role '{id}' lacks value.")))
                    .ToList(),
                RequiredAbilities: el.Elements("required-ability")
                    .ToDictionary(a => RequiredAttribute(a, "id"), a => (bool?)a.Attribute("value") ?? true),
                GrantedAbilities: el.Elements("ability")
                    .ToDictionary(a => RequiredAttribute(a, "id"), a => (bool?)a.Attribute("value") ?? true),
                RoleChanges: el.Elements("role-change")
                    .Select(rc => new RoleChange(RequiredAttribute(rc, "from"), RequiredAttribute(rc, "capture")))
                    .ToList());

            if (!roles.TryAdd(id, parsed))
            {
                throw new RulesetFormatException($"Duplicate role id '{id}'.");
            }
        }
        return roles;
    }

    /// <summary>
    /// Parses the <c>&lt;unit-change-types&gt;</c> section into a [change-type id][from-unit id] → change
    /// map. A null section yields no changes (combat then never promotes/demotes/captures by type).
    /// </summary>
    private static Dictionary<string, Dictionary<string, UnitChange>> ParseUnitChanges(XElement? section)
    {
        var byType = new Dictionary<string, Dictionary<string, UnitChange>>();
        foreach (XElement typeEl in section?.Elements("unit-change-type") ?? [])
        {
            string changeTypeId = RequiredAttribute(typeEl, "id");
            var byFrom = new Dictionary<string, UnitChange>();
            foreach (XElement change in typeEl.Elements("unit-type-change"))
            {
                string from = RequiredAttribute(change, "from");
                // Nearest definition wins on the rare duplicate from-row (none in classic combat sets).
                byFrom[from] = new UnitChange(
                    From: from,
                    To: RequiredAttribute(change, "to"),
                    Probability: (int?)change.Attribute("probability") ?? 0);
            }
            byType[changeTypeId] = byFrom;
        }
        return byType;
    }

    /// <summary>
    /// Parses the <c>model.unitChange.experience</c> change-type into a [from-unit id][to-expert id] → probability map.
    /// Unlike <see cref="ParseUnitChanges"/> (which keys on <c>from</c> and so keeps only one row per source type),
    /// this keeps <b>every</b> from→to row — the classic experience set has nine rows all sharing
    /// <c>from="model.unit.freeColonist"</c>, one per raw-goods expert, which the by-from map would collapse to one.
    /// </summary>
    private static Dictionary<string, Dictionary<string, int>> ParseExperienceUpgrades(XElement? section)
    {
        var byFrom = new Dictionary<string, Dictionary<string, int>>();
        XElement? experience = section?.Elements("unit-change-type")
            .FirstOrDefault(t => (string?)t.Attribute("id") == UnitChangeTypeIds.Experience);
        foreach (XElement change in experience?.Elements("unit-type-change") ?? [])
        {
            string from = RequiredAttribute(change, "from");
            if (!byFrom.TryGetValue(from, out Dictionary<string, int>? toMap))
            {
                byFrom[from] = toMap = [];
            }
            toMap[RequiredAttribute(change, "to")] = (int?)change.Attribute("probability") ?? 0;
        }
        return byFrom;
    }

    /// <summary>
    /// Parses the <c>model.unitChange.education</c> change-type into a [from-unit id][to-unit id] → base training-turns
    /// map (the schooling ladder). Like <see cref="ParseExperienceUpgrades"/>, it keeps <b>every</b> from→to row — the
    /// criminal→servant and servant→free rungs (unique <c>from</c>) plus the many free-colonist→expert rows the by-from
    /// <see cref="ParseUnitChanges"/> map would collapse to one.
    /// </summary>
    private static Dictionary<string, Dictionary<string, int>> ParseEducationTurns(XElement? section)
    {
        var byFrom = new Dictionary<string, Dictionary<string, int>>();
        XElement? education = section?.Elements("unit-change-type")
            .FirstOrDefault(t => (string?)t.Attribute("id") == UnitChangeTypeIds.Education);
        foreach (XElement change in education?.Elements("unit-type-change") ?? [])
        {
            string from = RequiredAttribute(change, "from");
            if (!byFrom.TryGetValue(from, out Dictionary<string, int>? toMap))
            {
                byFrom[from] = toMap = [];
            }
            toMap[RequiredAttribute(change, "to")] = (int?)change.Attribute("turns") ?? 0;
        }
        return byFrom;
    }

    private static ResourceModifier ParseResourceModifier(XElement m) => new(
        GoodsId: RequiredAttribute(m, "id"),
        Type: (string?)m.Attribute("type") switch
        {
            "multiplicative" => ModifierType.Multiplicative,
            "percentage" => ModifierType.Percentage,
            _ => ModifierType.Additive,
        },
        Value: (double?)m.Attribute("value") ?? 0,
        Index: (int?)m.Attribute("index") ?? 0,
        ScopeUnitTypes: m.Elements("scope")
            .Select(s => (string?)s.Attribute("type"))
            .Where(t => t is not null)
            .Select(t => t!)
            .ToList());

    private static ImprovementModifier ParseImprovementModifier(XElement m) => new(
        GoodsId: RequiredAttribute(m, "id"),
        Type: (string?)m.Attribute("type") switch
        {
            "multiplicative" => ModifierType.Multiplicative,
            "percentage" => ModifierType.Percentage,
            _ => ModifierType.Additive,
        },
        Value: (double?)m.Attribute("value") ?? 0);

    /// <summary>
    /// Parses one improvement-type <c>&lt;scope&gt;</c>: either a terrain predicate (<c>method-name</c> +
    /// <c>method-value</c>) or a terrain-id scope (<c>type</c> + optional <c>match-negated</c>). See
    /// <see cref="ImprovementScope"/>.
    /// </summary>
    private static ImprovementScope ParseImprovementScope(XElement s) => new(
        MethodName: (string?)s.Attribute("method-name"),
        MethodValue: (bool?)s.Attribute("method-value") ?? false,
        TileTypeId: (string?)s.Attribute("type"),
        MatchNegated: (bool?)s.Attribute("match-negated") ?? false);

    /// <summary>
    /// Parses one improvement-type <c>&lt;tile-type-change&gt;</c> (clear-forest's forest → cleared base): the
    /// destination terrain and the one-off <c>&lt;production&gt;</c> goods (lumber) delivered on the change.
    /// </summary>
    private static ImprovementTypeChange ParseTileTypeChange(XElement c)
    {
        XElement? production = c.Element("production");
        return new ImprovementTypeChange(
            ToTerrainId: RequiredAttribute(c, "to"),
            ProductionGoodsId: (string?)production?.Attribute("goods-type"),
            ProductionAmount: (int?)production?.Attribute("value") ?? 0);
    }

    private static UnitProductionModifier ParseUnitProductionModifier(XElement m) => new(
        GoodsId: RequiredAttribute(m, "id"),
        Type: (string?)m.Attribute("type") switch
        {
            "multiplicative" => ModifierType.Multiplicative,
            "percentage" => ModifierType.Percentage,
            _ => ModifierType.Additive,
        },
        Value: (double?)m.Attribute("value") ?? 0,
        Index: (int?)m.Attribute("index") ?? 0);

    private static FatherModifier ParseModifier(XElement m) => new(
        TargetId: RequiredAttribute(m, "id"),
        Type: (string?)m.Attribute("type") switch
        {
            "multiplicative" => ModifierType.Multiplicative,
            "percentage" => ModifierType.Percentage,
            _ => ModifierType.Additive,
        },
        Value: (double?)m.Attribute("value") ?? 0,
        Index: (int?)m.Attribute("index") ?? 0,
        // <scope type="model.unit.privateer"/> children restrict the modifier (Francis Drake → privateers only).
        ScopeUnitTypes: m.Elements("scope")
            .Select(s => (string?)s.Attribute("type"))
            .Where(t => t is not null)
            .Select(t => t!)
            .ToList());

    private static FatherAbility ParseAbility(XElement a) => new(
        Id: RequiredAttribute(a, "id"),
        Value: (bool?)a.Attribute("value") ?? true,
        ScopeTypes: a.Elements("scope")
            .Select(s => (string?)s.Attribute("type"))
            .Where(t => t is not null)
            .Select(t => t!)
            .ToList());

    private static ProductionEntry ParseProduction(XElement p) => new(
        Unattended: (bool?)p.Attribute("unattended") ?? false,
        Outputs: p.Elements("output")
            .Select(o => new GoodsOutput(
                RequiredAttribute(o, "goods-type"),
                (int?)o.Attribute("value")
                    ?? throw new RulesetFormatException("<output> lacks a value.")))
            .ToList(),
        Inputs: p.Elements("input")
            .Select(o => new GoodsOutput(
                RequiredAttribute(o, "goods-type"),
                (int?)o.Attribute("value")
                    ?? throw new RulesetFormatException("<input> lacks a value.")))
            .ToList());

    /// <summary>
    /// Parses unit types, resolving the spec's <c>extends</c> inheritance chains
    /// (attributes: nearest definition wins; abilities: any ancestor may set them).
    /// Abstract types participate in resolution but are not exposed.
    /// </summary>
    private static Dictionary<string, UnitType> ParseUnitTypes(XElement unitTypes)
    {
        var elements = new Dictionary<string, XElement>();
        foreach (XElement el in unitTypes.Elements("unit-type"))
        {
            string id = RequiredAttribute(el, "id");
            if (!elements.TryAdd(id, el))
            {
                throw new RulesetFormatException($"Duplicate unit-type id '{id}'.");
            }
        }

        var units = new Dictionary<string, UnitType>();
        foreach ((string id, XElement el) in elements)
        {
            if ((bool?)el.Attribute("abstract") ?? false)
            {
                continue;
            }

            units[id] = new UnitType(
                Id: id,
                Movement: ResolveIntAttribute(el, "movement", elements) ?? 3,
                LineOfSight: ResolveIntAttribute(el, "line-of-sight", elements) ?? 1,
                IsNaval: ResolveAbility(el, "model.ability.navalUnit", elements),
                CanFoundColony: ResolveAbility(el, "model.ability.foundColony", elements),
                // recruit-probability is a direct attribute in the spec (not inherited
                // via extends), so it is read off the concrete type only.
                RecruitProbability: (int?)el.Attribute("recruit-probability") ?? 0,
                IsPerson: ResolveAbility(el, "model.ability.person", elements),
                // space (cargo capacity) and spaceTaken (carry cost) inherit up the
                // extends chain in FreeCol; defaults match UnitType (0 and 1).
                Space: ResolveIntAttribute(el, "space", elements) ?? 0,
                SpaceTaken: ResolveIntAttribute(el, "spaceTaken", elements) ?? 1,
                // price = Europe purchase/training cost (0 if absent; manOWar uses
                // mercenary-price, not price, so it stays non-purchasable here).
                Price: ResolveIntAttribute(el, "price", elements) ?? 0,
                // Combat power, split at the role-modifier index (30) so a unit's role additive can
                // fold in at the correct point: the pre-role additive base (attribute + the type's own
                // index-<30 additive modifiers, e.g. king's regular +4) and the post-role multiplier
                // (the type's own index-≥30 percentage modifiers, e.g. veteran soldier +50% → ×1.5).
                // Offence/Defence are the unarmed fold (additive × multiplier), kept for callers that
                // want a unit's bare power; the combat code recombines with the role (see Game.OffenceBase).
                OffenceAdditive: ResolveCombatPower(el, "offence", "model.modifier.offence", elements).Additive,
                DefenceAdditive: ResolveCombatPower(el, "defence", "model.modifier.defence", elements).Additive,
                OffenceMultiplier: ResolveCombatPower(el, "offence", "model.modifier.offence", elements).Multiplier,
                DefenceMultiplier: ResolveCombatPower(el, "defence", "model.modifier.defence", elements).Multiplier,
                Offence: ResolveCombatPower(el, "offence", "model.modifier.offence", elements).Folded,
                Defence: ResolveCombatPower(el, "defence", "model.modifier.defence", elements).Folded,
                // Combat-outcome abilities (drive the loser/winner precedence; default false).
                DisposeOnCombatLoss: ResolveAbility(el, "model.ability.disposeOnCombatLoss", elements),
                CanBeCaptured: ResolveAbility(el, "model.ability.canBeCaptured", elements),
                CaptureUnits: ResolveAbility(el, "model.ability.captureUnits", elements),
                CaptureEquipment: ResolveAbility(el, "model.ability.captureEquipment", elements),
                DisposeOnAllEquipmentLost: ResolveAbility(el, "model.ability.disposeOnAllEquipLost", elements),
                DemoteOnAllEquipmentLost: ResolveAbility(el, "model.ability.demoteOnAllEquipLost", elements),
                Bombard: ResolveAbility(el, "model.ability.bombard", elements),
                // hit-points: every concrete ship sets 6 directly (the abstract `ship` base omits it); resolved
                // through the extends chain like other ints. Default 1 for the non-naval types that never set it.
                MaxHitPoints: ResolveIntAttribute(el, "hit-points", elements) ?? 1,
                // captureGoods: a naval raider loots a beaten ship's hold (frigate, privateer, man-o-war).
                CaptureGoods: ResolveAbility(el, "model.ability.captureGoods", elements),
                // piracy: a privateer attacks rivals without declaring war, flying no flag.
                Piracy: ResolveAbility(el, "model.ability.piracy", elements),
                // carryTreasure: a treasure train holds plundered/discovered gold to cash in.
                CarryTreasure: ResolveAbility(el, "model.ability.carryTreasure", elements),
                // Colony construction: the unit's own required-goods (artillery hammers 192 + tools 40, wagon
                // train hammers 40); required-population (FreeCol default 1) and required-abilities (collected
                // down the extends chain, as for buildings — ships' navalUnit-scoped build gate rides here).
                BuildCost: el.Elements("required-goods")
                    .Select(g => new GoodsOutput(
                        RequiredAttribute(g, "id"),
                        (int?)g.Attribute("value")
                            ?? throw new RulesetFormatException($"required-goods in unit '{id}' lacks value.")))
                    .ToList(),
                RequiredPopulation: ResolveIntAttribute(el, "required-population", elements) ?? 1,
                RequiredAbilities: CollectRequiredAbilitiesUpChain(el, elements),
                // Build cap (classic: the wagon-train limit "units lt settlements" at player scope — at most one
                // wagon train per colony). Only the units/settlements operands are evaluated (see UnitBuildLimit).
                BuildLimit: el.Element("limit") is { } lim
                    ? new UnitBuildLimit(
                        (string?)lim.Attribute("operator") ?? "lt",
                        (string?)lim.Element("left-hand-side")?.Attribute("operand-type") ?? "",
                        (string?)lim.Element("right-hand-side")?.Attribute("operand-type") ?? "")
                    : null,
                // Skill level (0 = plain colonist / ship / artillery; ≥1 = expert) — splits Europe Train vs Purchase.
                Skill: ResolveIntAttribute(el, "skill", elements) ?? 0,
                // The goods this unit is the expert producer of (expert farmer → grain); null for a non-expert.
                ExpertProduction: (string?)el.Attribute("expert-production"),
                // Per-goods production modifiers (expert bonus / indentured-petty penalty): the unit's own
                // <modifier id="model.goods.*"> children (index 30). Goods-id filtered so combat modifiers stay out.
                ProductionModifiers: el.Elements("modifier")
                    .Where(m => ((string?)m.Attribute("id"))?.StartsWith("model.goods.") == true)
                    .Select(ParseUnitProductionModifier)
                    .ToList(),
                // Experience cap toward an on-the-job expert upgrade (classic: only the free colonist sets 200).
                MaximumExperience: ResolveIntAttribute(el, "maximum-experience", elements) ?? 0,
                // Expert scout (seasoned scout): never triggers the vanishing-expedition rumour outcome.
                ExpertScout: ResolveAbility(el, "model.ability.expertScout", elements),
                // Lost City Rumour exploration bonus % (seasoned scout +10): tilts rumour odds toward good.
                ExploreLostCityRumourBonus: el.Elements("modifier")
                    .Where(m => (string?)m.Attribute("id") == "model.modifier.exploreLostCityRumour")
                    .Select(m => (int?)m.Attribute("value") ?? 0)
                    .DefaultIfEmpty(0)
                    .Last(),
                // The skill this unit teaches in a school, when overridden (own attribute, NOT inherited — defaults to
                // the type itself; classic only the colonial regular sets it, to veteranSoldier).
                SkillTaught: (string?)el.Attribute("skill-taught"));
        }

        if (units.Count == 0)
        {
            throw new RulesetFormatException("Specification defines no concrete unit types.");
        }
        return units;
    }

    /// <summary>The index at which a unit's role contributes its offence/defence (FreeCol roles use index 30).</summary>
    private const int RoleCombatIndex = 30;

    /// <summary>
    /// A unit type's combat power, split at the role-modifier index so a role additive can fold at the
    /// correct point (FreeCol applies modifiers in one ascending-index pass; roles sit at index 30
    /// between the type's additive modifiers at 20 and its percentage modifiers at 40):
    /// <list type="bullet">
    /// <item><c>Additive</c> — the base <paramref name="attribute"/> with the type's own index-&lt;30
    /// modifiers folded in (king's regular +4, colonial regular +3); a role additive adds to this.</item>
    /// <item><c>Multiplier</c> — the product of the type's index-≥30 percentage/multiplicative modifiers
    /// (veteran soldier +50% → 1.5), applied to <c>Additive + roleAdditive</c>.</item>
    /// <item><c>Folded</c> — <c>Additive × Multiplier</c>, the unit's bare (unarmed) power.</item>
    /// </list>
    /// </summary>
    private static (double Additive, double Multiplier, double Folded) ResolveCombatPower(
        XElement el, string attribute, string modifierId, Dictionary<string, XElement> elements)
    {
        var modifiers = new List<(double Value, ModifierType Type, int Index)>();
        for (XElement? current = el; current is not null; current = ParentOf(current, elements))
        {
            foreach (XElement m in current.Elements("modifier")
                         .Where(m => (string?)m.Attribute("id") == modifierId))
            {
                modifiers.Add((
                    (double?)m.Attribute("value") ?? 0,
                    (string?)m.Attribute("type") switch
                    {
                        "multiplicative" => ModifierType.Multiplicative,
                        "percentage" => ModifierType.Percentage,
                        _ => ModifierType.Additive,
                    },
                    (int?)m.Attribute("index") ?? 0));
            }
        }

        double additive = ResolveIntAttribute(el, attribute, elements) ?? 0;
        double multiplier = 1.0;
        foreach ((double value, ModifierType type, int index) in modifiers.OrderBy(m => m.Index))
        {
            if (index < RoleCombatIndex)
            {
                additive = ModifierMath.Apply(type, additive, value); // pre-role: fold into the base
            }
            else
            {
                // Post-role: applies after the role additive. Percentage/multiplicative accumulate as a
                // multiplier (×(1+v/100) / ×v); classic combat data has no post-role additive.
                multiplier = ModifierMath.Apply(type, multiplier, value);
            }
        }
        return (additive, multiplier, additive * multiplier);
    }

    /// <summary>Walks the extends chain until an element defines the attribute. Defaults match FreeCol's UnitType.</summary>
    private static int? ResolveIntAttribute(
        XElement el, string name, Dictionary<string, XElement> elements)
    {
        for (XElement? current = el; current is not null; current = ParentOf(current, elements))
        {
            if ((int?)current.Attribute(name) is int value)
            {
                return value;
            }
        }
        return null;
    }

    private static double? ResolveDoubleAttribute(
        XElement el, string name, Dictionary<string, XElement> elements)
    {
        for (XElement? current = el; current is not null; current = ParentOf(current, elements))
        {
            if ((double?)current.Attribute(name) is double value)
            {
                return value;
            }
        }
        return null;
    }

    /// <summary>
    /// Sums an additive modifier's value across the whole extends chain (cumulative inheritance) — used for
    /// <c>model.modifier.warehouseStorage</c>, where an extending building <em>adds to</em> its parent's value
    /// (depot 100 → warehouse 200 → expansion 300), unlike a redefined modifier.
    /// </summary>
    private static int SumModifierUpChain(XElement el, string modifierId, Dictionary<string, XElement> elements)
    {
        int total = 0;
        for (XElement? current = el; current is not null; current = ParentOf(current, elements))
        {
            total += current.Elements("modifier")
                .Where(m => (string?)m.Attribute("id") == modifierId)
                .Sum(m => (int?)m.Attribute("value") ?? 0);
        }
        return total;
    }

    /// <summary>
    /// Resolves a scalar modifier across the whole <c>extends</c> chain by folding it onto a base of 0 with FreeCol's
    /// modifier semantics — all <c>additive</c> values summed, then all <c>multiplicative</c> values multiplied
    /// (commutative, so chain order is irrelevant). Used for <c>model.modifier.breedingDivisor</c>, where the pasture
    /// sets an additive 50 and the stables (which extends it) multiplies by 0.5 → 25. 0 when the modifier is absent.
    /// </summary>
    private static int ResolveScalarModifierUpChain(XElement el, string modifierId, Dictionary<string, XElement> elements)
    {
        double additive = 0.0;
        double multiplicative = 1.0;
        for (XElement? current = el; current is not null; current = ParentOf(current, elements))
        {
            foreach (XElement m in current.Elements("modifier").Where(m => (string?)m.Attribute("id") == modifierId))
            {
                double value = (double?)m.Attribute("value") ?? 0.0;
                if ((string?)m.Attribute("type") == "multiplicative")
                {
                    multiplicative *= value;
                }
                else
                {
                    additive += value;
                }
            }
        }
        return (int)Math.Round(additive * multiplicative);
    }

    /// <summary>
    /// Collects a building's <c>required-ability</c> entries (id → required value) down the whole <c>extends</c>
    /// chain — a child building inherits its parent's requirements (drydock/shipyard keep docks' <c>hasPort</c>),
    /// and a re-stated id on the nearer element wins (FreeCol <c>BuildableType.getRequiredAbilities</c>).
    /// </summary>
    private static IReadOnlyDictionary<string, bool> CollectRequiredAbilitiesUpChain(
        XElement el, Dictionary<string, XElement> elements)
    {
        var result = new Dictionary<string, bool>();
        for (XElement? current = el; current is not null; current = ParentOf(current, elements))
        {
            foreach (XElement ability in current.Elements("required-ability"))
            {
                string id = RequiredAttribute(ability, "id");
                if (!result.ContainsKey(id)) // leaf → root: the nearer (already-seen) definition wins
                {
                    result[id] = (bool?)ability.Attribute("value") ?? true;
                }
            }
        }
        return result;
    }

    /// <summary>
    /// Collects the unit-type ids a building enables constructing — the <c>&lt;scope type="…"/&gt;</c> of every
    /// <c>model.ability.build</c> ability (value true) down the whole <c>extends</c> chain (so magazine/arsenal
    /// inherit armory's artillery scope, lumber mill carpenter's-house's wagon-train scope). FreeCol
    /// <c>UnitType.canBeBuiltInColony</c> matches a colony's scoped build ability to the target unit.
    /// </summary>
    private static IReadOnlySet<string> CollectBuildUnitTypeScopes(XElement el, Dictionary<string, XElement> elements)
    {
        var result = new HashSet<string>();
        for (XElement? current = el; current is not null; current = ParentOf(current, elements))
        {
            foreach (XElement ability in current.Elements("ability")
                .Where(a => (string?)a.Attribute("id") == "model.ability.build" && ((bool?)a.Attribute("value") ?? true)))
            {
                foreach (XElement scope in ability.Elements("scope"))
                {
                    if ((string?)scope.Attribute("type") is { } unitTypeId)
                    {
                        result.Add(unitTypeId);
                    }
                }
            }
        }
        return result;
    }

    /// <summary>
    /// True when a building grants <c>model.ability.build</c> scoped to <c>model.ability.navalUnit</c> (the
    /// shipyard) anywhere up its <c>extends</c> chain — it can construct any ship. (Ship construction is deferred,
    /// so this is parsed for completeness only.)
    /// </summary>
    private static bool GrantsNavalBuildScope(XElement el, Dictionary<string, XElement> elements)
    {
        for (XElement? current = el; current is not null; current = ParentOf(current, elements))
        {
            if (current.Elements("ability")
                .Where(a => (string?)a.Attribute("id") == "model.ability.build" && ((bool?)a.Attribute("value") ?? true))
                .SelectMany(a => a.Elements("scope"))
                .Any(s => (string?)s.Attribute("ability-id") == "model.ability.navalUnit"))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>True when any element in the extends chain sets the ability to true (nearest wins).</summary>
    private static bool ResolveAbility(
        XElement el, string abilityId, Dictionary<string, XElement> elements)
    {
        for (XElement? current = el; current is not null; current = ParentOf(current, elements))
        {
            XElement? ability = current.Elements("ability")
                .FirstOrDefault(a => (string?)a.Attribute("id") == abilityId);
            if (ability is not null)
            {
                return (bool?)ability.Attribute("value") ?? true;
            }
        }
        return false;
    }

    private static XElement? ParentOf(XElement el, Dictionary<string, XElement> elements)
    {
        string? parentId = (string?)el.Attribute("extends");
        if (parentId is null)
        {
            return null;
        }
        return elements.TryGetValue(parentId, out XElement? parent)
            ? parent
            : throw new RulesetFormatException(
                $"unit-type '{(string?)el.Attribute("id")}' extends unknown type '{parentId}'.");
    }

    private static TerrainType ParseTileType(XElement el)
    {
        string id = RequiredAttribute(el, "id");

        var productions = el.Elements("production").Select(ParseProduction).ToList();

        XElement? gen = el.Element("gen");

        return new TerrainType(
            Id: id,
            MoveCost: (int?)el.Attribute("basic-move-cost")
                ?? throw new RulesetFormatException($"Tile-type '{id}' lacks basic-move-cost."),
            WorkTurns: (int?)el.Attribute("basic-work-turns")
                ?? throw new RulesetFormatException($"Tile-type '{id}' lacks basic-work-turns."),
            IsForest: (bool?)el.Attribute("is-forest") ?? false,
            IsWater: (bool?)el.Attribute("is-water") ?? false,
            IsElevation: (bool?)el.Attribute("is-elevation") ?? false,
            CanSettle: (bool?)el.Attribute("can-settle") ?? true,
            IsConnected: (bool?)el.Attribute("is-connected") ?? false,
            Productions: productions,
            Gen: gen is null ? null : new GenRanges(
                HumidityMin: (int?)gen.Attribute("humidity-minimum") ?? 0,
                HumidityMax: (int?)gen.Attribute("humidity-maximum") ?? 100,
                TemperatureMin: (int?)gen.Attribute("temperature-minimum") ?? -20,
                TemperatureMax: (int?)gen.Attribute("temperature-maximum") ?? 40,
                AltitudeMin: (int?)gen.Attribute("altitude-minimum") ?? 0,
                AltitudeMax: (int?)gen.Attribute("altitude-maximum") ?? 30),
            Resources: el.Elements("resource")
                .Select(r => new ResourceChance(
                    RequiredAttribute(r, "type"),
                    (int?)r.Attribute("probability") ?? 100))
                .ToList(),
            // Combat defence bonus a unit gains while on this terrain (percentage modifier).
            DefenceBonus: (double?)el.Elements("modifier")
                .FirstOrDefault(m => (string?)m.Attribute("id") == "model.modifier.defence")
                ?.Attribute("value") ?? 0,
            // Concealing terrain (forests + hills) that enables an ambush.
            AmbushTerrain: el.Elements("ability")
                .Any(a => (string?)a.Attribute("id") == "model.ability.ambushTerrain" && ((bool?)a.Attribute("value") ?? true)));
    }

    /// <summary>
    /// Parses native nation types and their settlement templates from the
    /// <c>&lt;indian-nation-types&gt;</c> section, resolving the spec's <c>extends</c>
    /// chains (camp/village/city → default): attributes and the settlement template
    /// use nearest-wins; skills and regions accumulate from the chain (root first).
    /// Abstract types (default/camp/village/city) participate but are not exposed.
    /// A null section (minimal test rulesets) yields no native nations.
    /// </summary>
    private static (Dictionary<string, NativeNationType> nations, Dictionary<string, SettlementType> settlements)
        ParseNativeNationTypes(XElement? section)
    {
        var nations = new Dictionary<string, NativeNationType>();
        var settlements = new Dictionary<string, SettlementType>();
        if (section is null)
        {
            return (nations, settlements);
        }

        // Index every element (incl. abstract default/camp/village/city) for extends resolution.
        var elements = new Dictionary<string, XElement>();
        foreach (XElement el in section.Elements("indian-nation-type"))
        {
            string id = RequiredAttribute(el, "id");
            if (!elements.TryAdd(id, el))
            {
                throw new RulesetFormatException($"Duplicate indian-nation-type id '{id}'.");
            }
        }

        // Each distinct <settlement> template (camp, camp.capital, village…, inca…, aztec…).
        foreach (XElement el in elements.Values.SelectMany(e => e.Elements("settlement")))
        {
            SettlementType parsed = ParseSettlementType(el);
            settlements[parsed.Id] = parsed; // ids are unique across the section
        }

        // Concrete nation types, in specification order.
        foreach (XElement el in section.Elements("indian-nation-type"))
        {
            if ((bool?)el.Attribute("abstract") ?? false)
            {
                continue;
            }
            string id = RequiredAttribute(el, "id");
            XElement baseSettlement = ResolveSettlementElement(el, elements, capital: false)
                ?? throw new RulesetFormatException($"indian-nation-type '{id}' has no settlement template.");
            XElement capitalSettlement = ResolveSettlementElement(el, elements, capital: true)
                ?? throw new RulesetFormatException($"indian-nation-type '{id}' has no capital settlement template.");

            // Chain ordered root → leaf so inherited skills/regions come before the nation's own.
            var rootToLeaf = ExtendsChain(el, elements).Reverse().ToList();
            nations[id] = new NativeNationType(
                Id: id,
                SettlementTypeId: RequiredAttribute(baseSettlement, "id"),
                CapitalSettlementTypeId: RequiredAttribute(capitalSettlement, "id"),
                NumberOfSettlements: ParseSettlementNumber(
                    ResolveStringAttribute(el, "number-of-settlements", elements)),
                Aggression: ParseAggression(ResolveStringAttribute(el, "aggression", elements)),
                Skills: rootToLeaf
                    .SelectMany(c => c.Elements("skill"))
                    .Select(s => new NativeSkill(
                        RequiredAttribute(s, "id"), (int?)s.Attribute("probability") ?? 0))
                    .ToList(),
                Regions: rootToLeaf
                    .SelectMany(c => c.Elements("region"))
                    .Select(r => RequiredAttribute(r, "id"))
                    .ToList());
        }

        return (nations, settlements);
    }

    private static SettlementType ParseSettlementType(XElement el)
    {
        XElement? defence = el.Elements("modifier")
            .FirstOrDefault(m => (string?)m.Attribute("id") == "model.modifier.defence");
        return new SettlementType(
            Id: RequiredAttribute(el, "id"),
            Capital: (bool?)el.Attribute("capital") ?? false,
            ClaimableRadius: (int?)el.Attribute("claimable-radius") ?? 0,
            ExtraClaimableRadius: (int?)el.Attribute("extra-claimable-radius") ?? 0,
            MinimumSize: (int?)el.Attribute("minimum-size") ?? 0,
            MaximumSize: (int?)el.Attribute("maximum-size") ?? 0,
            MinimumGrowth: (int?)el.Attribute("minimum-growth") ?? 0,
            MaximumGrowth: (int?)el.Attribute("maximum-growth") ?? 0,
            TradeBonus: (int?)el.Attribute("trade-bonus") ?? 0,
            ConvertThreshold: (int?)el.Attribute("convert-threshold") ?? 0,
            DefenceModifier: (double?)defence?.Attribute("value") ?? 0,
            Plunder: el.Elements("plunder")
                .Select(p => new SettlementPlunder(
                    Probability: (int?)p.Attribute("probability") ?? 0,
                    Minimum: (int?)p.Attribute("minimum") ?? 0,
                    Maximum: (int?)p.Attribute("maximum") ?? 0,
                    Factor: (int?)p.Attribute("factor") ?? 0,
                    RequiresPlunderAbility:
                        (bool?)p.Element("scope")?.Attribute("ability-value") ?? false))
                .ToList(),
            Gifts: el.Elements("gifts")
                .Select(g => new SettlementGifts(
                    Probability: (int?)g.Attribute("probability") ?? 0,
                    Minimum: (int?)g.Attribute("minimum") ?? 0,
                    Maximum: (int?)g.Attribute("maximum") ?? 0,
                    Factor: (int?)g.Attribute("factor") ?? 0))
                .FirstOrDefault());
    }

    /// <summary>The chain of indian-nation-type elements from <paramref name="el"/> up its extends ancestors (leaf → root).</summary>
    private static IEnumerable<XElement> ExtendsChain(XElement el, Dictionary<string, XElement> elements)
    {
        for (XElement? current = el; current is not null;)
        {
            yield return current;
            string? parentId = (string?)current.Attribute("extends");
            current = parentId is not null ? elements.GetValueOrDefault(parentId) : null;
        }
    }

    /// <summary>The nearest <c>&lt;settlement&gt;</c> of the requested capital flag up the extends chain, or null.</summary>
    private static XElement? ResolveSettlementElement(
        XElement el, Dictionary<string, XElement> elements, bool capital)
    {
        foreach (XElement current in ExtendsChain(el, elements))
        {
            XElement? settlement = current.Elements("settlement")
                .FirstOrDefault(s => ((bool?)s.Attribute("capital") ?? false) == capital);
            if (settlement is not null)
            {
                return settlement;
            }
        }
        return null;
    }

    /// <summary>The nearest definition of a string attribute up the extends chain, or null.</summary>
    private static string? ResolveStringAttribute(
        XElement el, string name, Dictionary<string, XElement> elements)
    {
        foreach (XElement current in ExtendsChain(el, elements))
        {
            if ((string?)current.Attribute(name) is string value)
            {
                return value;
            }
        }
        return null;
    }

    private static SettlementNumber ParseSettlementNumber(string? value) => value switch
    {
        "low" => SettlementNumber.Low,
        "high" => SettlementNumber.High,
        _ => SettlementNumber.Average,
    };

    private static NativeAggression ParseAggression(string? value) => value switch
    {
        "low" => NativeAggression.Low,
        "high" => NativeAggression.High,
        _ => NativeAggression.Average,
    };

    private static string RequiredAttribute(XElement el, string name) =>
        el.Attribute(name)?.Value
            ?? throw new RulesetFormatException($"<{el.Name}> lacks required attribute '{name}'.");

    /// <summary>
    /// Parses the spec <c>&lt;events&gt;</c> section (FreeCol <c>Specification</c> reading <c>Event</c> elements) into a
    /// map of <see cref="SpecEvent"/> keyed by id. Each <c>&lt;event&gt;</c> carries an optional <c>score-value</c> and
    /// any number of child <c>&lt;limit&gt;</c> gates (<see cref="ParseLimit"/>). A null section (no <c>&lt;events&gt;</c>)
    /// yields an empty map. <c>&lt;ability&gt;</c> children on an event (the independence event's
    /// <c>independenceDeclared</c>/<c>independentNation</c> abilities) are not consumed here — the limit engine only
    /// needs the limits — matching the faithful-subset scope.
    /// </summary>
    private static Dictionary<string, SpecEvent> ParseEvents(XElement? eventsRoot)
    {
        var events = new Dictionary<string, SpecEvent>();
        if (eventsRoot is null)
        {
            return events;
        }
        foreach (XElement el in eventsRoot.Elements("event"))
        {
            string id = RequiredAttribute(el, "id");
            var limits = new Dictionary<string, Limit>();
            foreach (XElement limEl in el.Elements("limit"))
            {
                Limit limit = ParseLimit(limEl);
                limits[limit.Id] = limit;
            }
            var ev = new SpecEvent(id, (int?)el.Attribute("score-value") ?? 0, limits);
            if (!events.TryAdd(id, ev))
            {
                throw new RulesetFormatException($"Duplicate event id '{id}'.");
            }
        }
        return events;
    }

    /// <summary>
    /// Parses one spec <c>&lt;limit&gt;</c> (FreeCol <c>Limit.readChild</c>) into a <see cref="Limit"/>: the
    /// <c>operator</c> attribute plus the <c>&lt;left-hand-side&gt;</c> and <c>&lt;right-hand-side&gt;</c> operands
    /// (<see cref="ParseOperand"/>). Used both for an event's gates and standalone unit-build limits.
    /// </summary>
    internal static Limit ParseLimit(XElement el)
    {
        string id = (string?)el.Attribute("id") ?? "";
        LimitOperator op = ParseOperator(RequiredAttribute(el, "operator"));
        Operand lhs = ParseOperand(el.Element("left-hand-side")
            ?? throw new RulesetFormatException($"<limit> '{id}' lacks a <left-hand-side>."));
        Operand rhs = ParseOperand(el.Element("right-hand-side")
            ?? throw new RulesetFormatException($"<limit> '{id}' lacks a <right-hand-side>."));
        return new Limit(id, lhs, op, rhs);
    }

    /// <summary>Maps a spec <c>operator</c> attribute (<c>eq</c>/<c>lt</c>/<c>gt</c>/<c>le</c>/<c>ge</c>) to a <see cref="LimitOperator"/>.</summary>
    private static LimitOperator ParseOperator(string op) => op switch
    {
        "eq" => LimitOperator.Eq,
        "lt" => LimitOperator.Lt,
        "gt" => LimitOperator.Gt,
        "le" => LimitOperator.Le,
        "ge" => LimitOperator.Ge,
        _ => throw new RulesetFormatException($"Unknown limit operator '{op}'."),
    };

    /// <summary>
    /// Parses one operand element (<c>&lt;left-hand-side&gt;</c> / <c>&lt;right-hand-side&gt;</c>, FreeCol
    /// <c>Operand.readAttributes</c>): its <c>operand-type</c>, <c>scope-level</c>, optional literal <c>value</c>,
    /// optional <c>method-name</c>/<c>method-value</c>, and optional <c>type</c> (the option id for an option operand).
    /// </summary>
    private static Operand ParseOperand(XElement el) => new(
        ParseOperandType((string?)el.Attribute("operand-type")),
        ParseScopeLevel((string?)el.Attribute("scope-level")),
        (int?)el.Attribute("value"),
        (string?)el.Attribute("method-name"),
        (string?)el.Attribute("method-value"),
        (string?)el.Attribute("type"));

    /// <summary>Maps a spec <c>operand-type</c> attribute to an <see cref="OperandType"/> (default <see cref="OperandType.None"/>).</summary>
    private static OperandType ParseOperandType(string? type) => type switch
    {
        "units" => OperandType.Units,
        "settlements" => OperandType.Settlements,
        "foundingFathers" => OperandType.FoundingFathers,
        "year" => OperandType.Year,
        "option" => OperandType.Option,
        null or "none" => OperandType.None,
        _ => OperandType.None, // an unrecognised operand type counts as a literal/none operand (evaluates to null → no constraint)
    };

    /// <summary>Maps a spec <c>scope-level</c> attribute to a <see cref="LimitScopeLevel"/> (default <see cref="LimitScopeLevel.None"/>).</summary>
    private static LimitScopeLevel ParseScopeLevel(string? level) => level switch
    {
        "settlement" => LimitScopeLevel.Settlement,
        "player" => LimitScopeLevel.Player,
        "game" => LimitScopeLevel.Game,
        _ => LimitScopeLevel.None,
    };
}

/// <summary>
/// One block of the foreign Intervention Force (FreeCol <c>AbstractUnit</c> in <c>model.option.interventionForce</c>):
/// a count of a unit type in a given military role (a null role = the unit's default role).
/// </summary>
/// <param name="UnitTypeId">The unit type id (e.g. <c>model.unit.colonialRegular</c>, <c>model.unit.manOWar</c>).</param>
/// <param name="RoleId">The military role id the units carry (e.g. <c>model.role.dragoon</c>); null = the default role.</param>
/// <param name="Count">How many units of this (type, role) the ally lands.</param>
public sealed record InterventionForceUnit(string UnitTypeId, string? RoleId, int Count);

/// <summary>
/// The composition of the foreign Intervention Force (FreeCol <c>Monarch.getInterventionForce</c>): the unit blocks a
/// friendly power sends to aid a rebel that has held out long enough during the War of Independence. Parsed from the
/// spec <c>model.option.interventionForce</c>; the classic-medium default is 2 colonial-regular soldiers + 2
/// colonial-regular dragoons + 2 artillery + 2 men-o-war. The land/naval split is decided at spawn time from each
/// unit type's <c>IsNaval</c> flag, so this record stays ruleset-agnostic.
/// </summary>
/// <param name="Units">The force's unit blocks, in spec order.</param>
public sealed record InterventionForceComposition(IReadOnlyList<InterventionForceUnit> Units)
{
    /// <summary>The classic-medium bell threshold (<c>model.option.interventionBells</c>), the parse fallback.</summary>
    public const int ClassicMediumBells = 5000;

    /// <summary>The classic-medium growth period (<c>model.option.interventionTurns</c>), the parse fallback.</summary>
    public const int ClassicMediumTurns = 52;

    /// <summary>
    /// The classic-medium intervention force (the parse fallback): 2 colonial-regular soldiers, 2 colonial-regular
    /// dragoons, 2 artillery, 2 men-o-war (FreeCol <c>model.difficulty.medium</c> <c>interventionForce</c>).
    /// </summary>
    public static readonly InterventionForceComposition ClassicMedium = new(
    [
        new InterventionForceUnit("model.unit.colonialRegular", "model.role.soldier", 2),
        new InterventionForceUnit("model.unit.colonialRegular", "model.role.dragoon", 2),
        new InterventionForceUnit("model.unit.artillery", "model.role.default", 2),
        new InterventionForceUnit("model.unit.manOWar", "model.role.default", 2),
    ]);

    /// <summary>Total units across all blocks (land + naval).</summary>
    public int TotalCount => Units.Sum(u => u.Count);
}

/// <summary>Thrown when a ruleset specification file is malformed.</summary>
public sealed class RulesetFormatException : Exception
{
    /// <summary>Creates the exception with a description of the format problem.</summary>
    public RulesetFormatException(string message) : base(message) { }
}
