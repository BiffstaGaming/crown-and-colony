using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.World;

namespace CrownAndColony.GameLogic.Colonies;

/// <summary>
/// A colony on the map: identity, location, population, and goods stores.
/// Tile work assignments, buildings, and production chains are later
/// Phase 3 slices.
/// </summary>
public sealed class Colony
{
    /// <summary>
    /// Food a colonist eats per turn — the classic value (2), now the source-of-truth alias of the ruleset constant
    /// <see cref="Specification.ColonyConstants.FoodPerColonist"/> (86d3drpgg). The live colony turn reads the ruleset's
    /// value via <see cref="GameSession.Game"/>; this static alias is for presentation/tests that want the classic figure
    /// without a ruleset.
    /// </summary>
    public static readonly int FoodPerColonist = Specification.ColonyConstants.ClassicDefaults.FoodPerColonist;

    /// <summary>
    /// Stored food a new colonist needs to be born — the classic value (200), now the source-of-truth alias of the
    /// ruleset constant <see cref="Specification.ColonyConstants.FoodForGrowth"/> (86d3drpgg). The live colony turn reads
    /// the ruleset's value via <see cref="GameSession.Game"/>; this static alias is for presentation/tests.
    /// </summary>
    public static readonly int FoodForGrowth = Specification.ColonyConstants.ClassicDefaults.FoodForGrowth;

    /// <summary>
    /// The warehouse food id — grain/fish/meat all store as this
    /// (spec <c>stored-as</c>; normalization happens in <see cref="GameSession.Game"/>).
    /// </summary>
    public const string FoodId = "model.goods.food";

    /// <summary>
    /// Liberty points one rebel colonist represents (FreeCol <c>Colony.LIBERTY_PER_REBEL</c> = 200) — the classic value,
    /// now the source-of-truth alias of the ruleset constant <see cref="Specification.ColonyConstants.LibertyPerRebel"/>
    /// (86d3drpgg). A colony's own SoL maths read its per-colony <see cref="RebelLibertyDivisor"/> (set from the ruleset);
    /// this static alias is the classic figure for presentation/tests.
    /// </summary>
    public static readonly int LibertyPerRebel = Specification.ColonyConstants.ClassicDefaults.LibertyPerRebel;

    /// <summary>
    /// The per-colony liberty-per-rebel divisor behind <see cref="SonsOfLiberty"/> and the 100%-SoL liberty cap. Set from
    /// <c>Ruleset.ColonyConstants.LibertyPerRebel</c> when the colony is founded or loaded; defaults to the classic 200
    /// so a colony built without a ruleset (tests) behaves as before. A plain value (no <see cref="Ruleset"/> dependency)
    /// — the <see cref="Government"/> precedent (86d3drpgg). <c>internal set</c>: only the game/save layer assigns it.
    /// </summary>
    internal int RebelLibertyDivisor { get; set; } = Specification.ColonyConstants.ClassicDefaults.LibertyPerRebel;

    /// <summary>
    /// The government thresholds for <see cref="ProductionBonus"/>, from the difficulty level. Set from
    /// <c>Ruleset.Difficulty.Government</c> when the colony is founded or loaded; defaults to the classic medium
    /// tier so a colony built without a ruleset (tests) behaves as before. A plain value (no <see cref="Ruleset"/>
    /// dependency) — see [difficulty]. <c>internal set</c>: only the game/save layer assigns it.
    /// </summary>
    internal GovernmentLimits Government { get; set; } = GovernmentLimits.ClassicMedium;

    private readonly Dictionary<string, int> _stores = [];
    private readonly Dictionary<Position, string> _tileWorkers = [];
    private readonly List<string> _buildings = [];
    private readonly Dictionary<string, int> _buildingWorkers = [];
    private readonly Dictionary<string, ExportSetting> _exports = [];

    // Per-colonist unit-type overlay (86d3b6nrz): the count model above (Population/_tileWorkers/_buildingWorkers)
    // stays the authority for HOW MANY workers and WHERE; these sparse maps annotate which of them are NON-FREE
    // specialists (an absent entry = a free colonist). Each non-free colonist is in exactly one of: a tile, a
    // building, or the idle pool. Kept ≤ the counts by ReconcileWorkerTypes; omitted-when-all-free in the save (v30).
    private readonly Dictionary<Position, string> _tileWorkerTypes = [];        // tile → its non-free worker's type
    private readonly Dictionary<string, List<string>> _buildingWorkerTypes = []; // building → its non-free occupants' types
    private readonly List<string> _idleWorkerTypes = [];                          // non-free colonists with no assignment
    private readonly Dictionary<Position, int> _tileWorkerExperience = [];        // tile → its free-colonist worker's accrued on-the-job experience (86d3c9pgj; absent ⇒ 0, cleared when the occupant leaves/changes)
    private readonly Dictionary<string, List<int>> _schoolTrainingTurns = [];     // school building id → per-teacher-slot turns accrued toward each teacher's current student (86d3fpyc0; absent/short ⇒ 0; one entry per parallel teacher — schoolhouse 1, college 2, university 3)

    /// <summary>The unit-type id of a plain colonist — the implicit default for any worker without an overlay entry.</summary>
    public const string FreeColonistTypeId = "model.unit.freeColonist";

    /// <summary>
    /// The custom-house export/import setting for one good (FreeCol <c>ExportData</c>): whether it auto-exports, the
    /// amount to retain when exporting, and the ceiling an automatic delivery will not stock past.
    /// </summary>
    /// <param name="Exported">Whether the custom house auto-sells this good's surplus.</param>
    /// <param name="ExportLevel">The amount to keep in the warehouse before exporting the rest.</param>
    /// <param name="ImportLevel">
    /// The amount to import to: an automatic delivery (a trade-route drop-off) will not push this good above this level
    /// (FreeCol <c>ExportData.importLevel</c>). The sentinel <see cref="ImportLevelUnset"/> (−1) means "not set" — the
    /// effective cap then falls back to the colony's warehouse capacity (FreeCol <c>getEffectiveImportLevel</c>), i.e. no
    /// extra limit beyond the warehouse itself, so an untouched good auto-imports exactly as before this field existed.
    /// </param>
    public readonly record struct ExportSetting(bool Exported, int ExportLevel, int ImportLevel = ImportLevelUnset);

    /// <summary>
    /// The "import level not set" sentinel for <see cref="ExportSetting.ImportLevel"/> (FreeCol <c>ExportData</c>'s
    /// <c>importLevel = -1</c>). When a good's import level is this, its effective cap is the colony's warehouse capacity
    /// (<see cref="GameSession.Game.ColonyWarehouseCapacity"/>) — i.e. automatic delivery is bounded only by the
    /// warehouse, exactly as it was before per-good import levels existed.
    /// </summary>
    public const int ImportLevelUnset = -1;

    /// <summary>
    /// The default retain level for a good not yet configured (FreeCol <c>ExportData.EXPORT_LEVEL_DEFAULT</c> = 50) — the
    /// classic value, now the source-of-truth alias of the ruleset constant
    /// <see cref="Specification.ColonyConstants.DefaultExportLevel"/> (86d3drpgg). A colony reads its per-colony
    /// <see cref="ExportRetainDefault"/> (set from the ruleset) for the live export maths; this static alias is the
    /// classic figure for presentation/tests.
    /// </summary>
    public static readonly int DefaultExportLevel = Specification.ColonyConstants.ClassicDefaults.DefaultExportLevel;

    /// <summary>
    /// The per-colony custom-house default retain level. Set from <c>Ruleset.ColonyConstants.DefaultExportLevel</c> when
    /// the colony is founded or loaded; defaults to the classic 50 so a colony built without a ruleset (tests) behaves as
    /// before. A plain value (no <see cref="Ruleset"/> dependency) — the <see cref="Government"/> precedent (86d3drpgg).
    /// </summary>
    internal int ExportRetainDefault { get; set; } = Specification.ColonyConstants.ClassicDefaults.DefaultExportLevel;

    /// <summary>Creates a colony owned by a colonial player (the human is 0; ADR-019).</summary>
    public Colony(int id, string name, Position position, int population, int ownerId = 0)
    {
        Id = id;
        Name = name;
        Position = position;
        Population = population;
        OwnerId = ownerId;
    }

    /// <summary>Stable per-game identifier.</summary>
    public int Id { get; }

    /// <summary>The owning colonial player's id (FP-2; the human is 0). Foreign powers own colonies from FP-4+; capture (1c-3e) reassigns it.</summary>
    public int OwnerId { get; internal set; }

    /// <summary>Display name (e.g. "Jamestown"). Settable via <see cref="GameSession.Game.RenameColony"/>.</summary>
    public string Name { get; internal set; }

    /// <summary>Map tile the colony occupies.</summary>
    public Position Position { get; }

    /// <summary>Number of colonists living here (founding unit becomes the first).</summary>
    public int Population { get; internal set; }

    /// <summary>Goods in the colony's warehouse, by ruleset goods id.</summary>
    public IReadOnlyDictionary<string, int> Stores => _stores;

    /// <summary>Stored amount of one goods type (0 when none).</summary>
    public int StoreOf(string goodsId) => _stores.GetValueOrDefault(goodsId);

    /// <summary>Stored food.</summary>
    public int Food => StoreOf(FoodId);

    /// <summary>
    /// Colonists working surrounding tiles: tile → the goods they produce there.
    /// Colonists not in this map are idle (building jobs are a later slice).
    /// </summary>
    public IReadOnlyDictionary<Position, string> TileWorkers => _tileWorkers;

    /// <summary>Colonists without a tile or building assignment.</summary>
    public int IdleColonists => Population - _tileWorkers.Count - _buildingWorkers.Values.Sum();

    /// <summary>Building type ids present in the colony, in construction order.</summary>
    public IReadOnlyList<string> Buildings => _buildings;

    /// <summary>Colonists working in buildings: building type id → worker count.</summary>
    public IReadOnlyDictionary<string, int> BuildingWorkers => _buildingWorkers;

    /// <summary>Whether the colony has a building.</summary>
    public bool HasBuilding(string buildingId) => _buildings.Contains(buildingId);

    /// <summary>Non-free tile workers by tile (sparse — a tile worked by a free colonist is absent). 86d3b6nrz.</summary>
    public IReadOnlyDictionary<Position, string> TileWorkerTypes => _tileWorkerTypes;

    /// <summary>Non-free building occupants by building id (sparse — an all-free building is absent), unordered (production is a commutative sum).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> BuildingWorkerTypes =>
        _buildingWorkerTypes.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value);

    /// <summary>Non-free colonists with no current assignment (free idle colonists are implicit in <see cref="IdleColonists"/>).</summary>
    public IReadOnlyList<string> IdleWorkerTypes => _idleWorkerTypes;

    /// <summary>The unit-type id of the colonist working <paramref name="tile"/> — its overlay entry, or a free colonist by default.</summary>
    public string WorkerTypeAt(Position tile) => _tileWorkerTypes.GetValueOrDefault(tile, FreeColonistTypeId);

    /// <summary>On-the-job experience a free colonist working <paramref name="tile"/> has accrued toward an expert upgrade (86d3c9pgj; 0 by default).</summary>
    public int TileWorkerExperienceAt(Position tile) => _tileWorkerExperience.GetValueOrDefault(tile);

    /// <summary>Accrued tile-worker experience by tile (sparse — a tile with no/zero experience is absent). 86d3c9pgj.</summary>
    public IReadOnlyDictionary<Position, int> TileWorkerExperience => _tileWorkerExperience;

    /// <summary>
    /// The unit-type ids of a building's workers — the non-free occupants from the overlay plus free colonists padded
    /// to the building's worker count. Order is non-free-then-free; production sums over it (commutative), so order is
    /// immaterial. An all-free building yields a list of free colonists of length = its worker count.
    /// </summary>
    public IReadOnlyList<string> BuildingOccupants(string buildingId)
    {
        int count = _buildingWorkers.GetValueOrDefault(buildingId);
        var occupants = new List<string>(count);
        if (_buildingWorkerTypes.TryGetValue(buildingId, out List<string>? nonFree))
        {
            occupants.AddRange(nonFree);
        }
        while (occupants.Count < count)
        {
            occupants.Add(FreeColonistTypeId);
        }
        return occupants;
    }

    private readonly List<string> _buildQueue = [];

    /// <summary>The colony's ordered construction queue (buildable ids; empty when idle). The front is built first.</summary>
    public IReadOnlyList<string> BuildQueue => _buildQueue;

    /// <summary>Building type currently under construction — the front of <see cref="BuildQueue"/> (null when idle).</summary>
    public string? CurrentBuild => _buildQueue.Count > 0 ? _buildQueue[0] : null;

    /// <summary>Replaces the whole construction queue (empty clears it). Validation lives in <see cref="GameSession.Game"/>.</summary>
    internal void SetBuildQueue(IEnumerable<string> ids)
    {
        _buildQueue.Clear();
        _buildQueue.AddRange(ids);
    }

    /// <summary>Appends a buildable to the end of the construction queue.</summary>
    internal void EnqueueBuild(string id) => _buildQueue.Add(id);

    /// <summary>Removes the front item (its construction finished or it became invalid).</summary>
    internal void AdvanceBuild()
    {
        if (_buildQueue.Count > 0)
        {
            _buildQueue.RemoveAt(0);
        }
    }

    /// <summary>Removes the queued buildable at <paramref name="index"/> (a no-op if out of range).</summary>
    internal void RemoveFromBuildQueue(int index)
    {
        if (index >= 0 && index < _buildQueue.Count)
        {
            _buildQueue.RemoveAt(index);
        }
    }

    /// <summary>Swaps the queued buildable at <paramref name="index"/> with the one <paramref name="delta"/> places away (a no-op if either position is out of range).</summary>
    internal void MoveBuildQueueItem(int index, int delta)
    {
        int target = index + delta;
        if (index >= 0 && index < _buildQueue.Count && target >= 0 && target < _buildQueue.Count)
        {
            (_buildQueue[index], _buildQueue[target]) = (_buildQueue[target], _buildQueue[index]);
        }
    }

    /// <summary>
    /// Accumulated liberty points from <b>net</b> bell production (gross bells − upkeep; FreeCol <c>Colony.liberty</c>).
    /// Drives <see cref="SonsOfLiberty"/>. Floored at 0; persisted (SaveGame v22). Each turn the colony banks the same
    /// founding-father-modified figure the player pool gets, less bell upkeep — so a colony that outgrows its bell
    /// output loses liberty and its Sons of Liberty can fall.
    /// </summary>
    public int Liberty { get; internal set; }

    /// <summary>
    /// The owner's standing Sons-of-Liberty percentage modifier from Congress (Simón Bolívar's <c>model.modifier.SoL</c>
    /// = +20), folded into <see cref="SonsOfLiberty"/> after the liberty→% conversion exactly as FreeCol does. Derived
    /// from the owner's Congress (not persisted) — <see cref="GameSession.Game"/> refreshes it on election, founding,
    /// and load. 0 for a player without such a father.
    /// </summary>
    public int SolModifierBonus { get; internal set; }

    /// <summary>
    /// Sons-of-Liberty membership, 0–100 (FreeCol <c>calculateSoLPercentage</c>):
    /// <c>floor(liberty·100 / (200·population))</c>, plus the owner's standing <see cref="SolModifierBonus"/>
    /// (applied to the percentage, after the conversion — FreeCol's order), clamped 0–100; 0 for an empty colony.
    /// </summary>
    public int SonsOfLiberty =>
        Population <= 0 ? 0 : Math.Clamp(Liberty * 100 / (RebelLibertyDivisor * Population) + SolModifierBonus, 0, 100);

    /// <summary>Colonists who are rebels: <c>floor(SoL% · population / 100)</c> (FreeCol <c>calculateRebelCount</c>; integer arithmetic — bit-identical to its float floor, ADR-009).</summary>
    public int RebelCount => SonsOfLiberty * Population / 100;

    /// <summary>Colonists who are royalists/tories: <c>population − rebels</c> (FreeCol <c>calculateToryCount</c>).</summary>
    public int ToryCount => Population - RebelCount;

    /// <summary>
    /// Per-producing-worker production bonus, −2..+2 (FreeCol <c>calculateProductionBonus</c>): SoL ≥ 100 → +2;
    /// SoL ≥ 50 → +1; else by tory count: &gt; 10 → −2, &gt; 6 → −1, else 0 (the penalty tiers gate on absolute tory
    /// count, so a small colony never gets one regardless of low SoL). Added to each attended worker's tile/building
    /// output (floored at 0) in <see cref="GameSession.Game"/>'s colony turn.
    /// </summary>
    public int ProductionBonus =>
        SonsOfLiberty >= Government.VeryGood ? 2
        : SonsOfLiberty >= Government.Good ? 1
        : ToryCount > Government.VeryBad ? -2
        : ToryCount > Government.Bad ? -1
        : 0;

    internal void AddBuilding(string buildingId) => _buildings.Add(buildingId);

    /// <summary>Swaps an upgraded building for its successor, preserving staffing (count + worker-type overlay).</summary>
    internal void ReplaceBuilding(string oldId, string newId)
    {
        int index = _buildings.IndexOf(oldId);
        _buildings[index] = newId;
        if (_buildingWorkers.Remove(oldId, out int workers))
        {
            _buildingWorkers[newId] = workers;
        }
        if (_buildingWorkerTypes.Remove(oldId, out List<string>? occupants))
        {
            _buildingWorkerTypes[newId] = occupants;
        }
    }

    internal void SetBuildingWorkers(string buildingId, int workers)
    {
        if (workers <= 0)
        {
            _buildingWorkers.Remove(buildingId);
        }
        else
        {
            _buildingWorkers[buildingId] = workers;
        }
    }

    /// <summary>
    /// Removes a building outright (a base building with no predecessor, destroyed by a raid — FreeCol
    /// <c>ServerColony.destroyBuilding</c>), ejecting every worker in it back to the idle pool first so no colonist
    /// is stranded. A no-op when the colony has no such building. Used by the native-pillage building-burn; an
    /// upgraded building is downgraded via <see cref="ReplaceBuilding"/> instead, never removed.
    /// </summary>
    internal void RemoveBuilding(string buildingId)
    {
        if (!_buildings.Contains(buildingId))
        {
            return;
        }
        while (_buildingWorkers.GetValueOrDefault(buildingId) > 0)
        {
            UnassignBuildingWorker(buildingId); // workers spill into the idle pool (FreeCol ejectUnits)
        }
        _buildings.Remove(buildingId);
        _buildingWorkers.Remove(buildingId);
        _buildingWorkerTypes.Remove(buildingId);
    }

    /// <summary>Assigns a free colonist to a tile (the type-blind path: tests/incidental callers). Use the typed overload from the real assignment flows.</summary>
    internal void SetWorker(Position tile, string goodsId) => SetWorker(tile, goodsId, FreeColonistTypeId);

    /// <summary>Assigns a colonist of <paramref name="type"/> (from the idle pool) to <paramref name="tile"/>, recording a non-free type in the overlay.</summary>
    internal void SetWorker(Position tile, string goodsId, string type)
    {
        _tileWorkers[tile] = goodsId;
        _tileWorkerExperience.Remove(tile); // a (re)assigned tile starts the new occupant's experience at 0 (FreeCol resets on a work change)
        if (type == FreeColonistTypeId)
        {
            _tileWorkerTypes.Remove(tile);
        }
        else
        {
            _tileWorkerTypes[tile] = type;
            RemoveOneIdle(type); // the colonist moved idle → tile
        }
    }

    /// <summary>Returns a tile's worker to the idle pool (its non-free type, if any, rejoins the idle overlay).</summary>
    internal void RemoveWorker(Position tile)
    {
        if (_tileWorkerTypes.Remove(tile, out string? type))
        {
            _idleWorkerTypes.Add(type);
        }
        _tileWorkers.Remove(tile);
        _tileWorkerExperience.Remove(tile); // experience belongs to the (tile, occupant) — it leaves with the worker
    }

    /// <summary>Adds <paramref name="amount"/> on-the-job experience to <paramref name="tile"/>'s worker, clamped to <paramref name="cap"/> (86d3c9pgj).</summary>
    internal void AddTileWorkerExperience(Position tile, int amount, int cap) =>
        _tileWorkerExperience[tile] = Math.Min(_tileWorkerExperience.GetValueOrDefault(tile) + amount, cap);

    /// <summary>Restores a tile worker's accrued experience from a save (skips 0 to keep the map sparse). 86d3c9pgj.</summary>
    internal void SetTileWorkerExperience(Position tile, int experience)
    {
        if (experience > 0)
        {
            _tileWorkerExperience[tile] = experience;
        }
    }

    /// <summary>
    /// Promotes <paramref name="tile"/>'s worker in place to <paramref name="newType"/> (an on-the-job experience
    /// upgrade 86d3c9pgj, or a schooling upgrade 86d3c9p7f): sets the overlay type — or clears it when the new type is
    /// a free colonist (the servant→free schooling rung) — and clears the accrued experience, leaving the count model
    /// (<see cref="Population"/>, the tile assignment, the idle pool) untouched. Distinct from
    /// <see cref="SetWorker(Position, string, string)"/>, which draws a colonist from the idle pool — here the same
    /// colonist stays put, just changes type.
    /// </summary>
    internal void UpgradeTileWorker(Position tile, string newType)
    {
        if (newType == FreeColonistTypeId)
        {
            _tileWorkerTypes.Remove(tile);
        }
        else
        {
            _tileWorkerTypes[tile] = newType;
        }
        _tileWorkerExperience.Remove(tile);
    }

    /// <summary>
    /// Promotes one building occupant of <paramref name="currentType"/> in <paramref name="buildingId"/> to
    /// <paramref name="newType"/> in place (a schooling upgrade, 86d3c9p7f), leaving the worker count untouched: a
    /// non-free student's overlay entry is replaced (or removed when promoted to a free colonist); a free student
    /// (no overlay entry — an implicit free-padded slot) gains one. Same idea as <see cref="UpgradeTileWorker"/> for a
    /// building occupant; occupants of one type are interchangeable, so any one of <paramref name="currentType"/> is taken.
    /// </summary>
    internal void UpgradeBuildingWorker(string buildingId, string currentType, string newType)
    {
        if (currentType != FreeColonistTypeId && _buildingWorkerTypes.TryGetValue(buildingId, out List<string>? list))
        {
            list.Remove(currentType);
            if (list.Count == 0)
            {
                _buildingWorkerTypes.Remove(buildingId);
            }
        }
        if (newType != FreeColonistTypeId)
        {
            if (!_buildingWorkerTypes.TryGetValue(buildingId, out List<string>? l))
            {
                _buildingWorkerTypes[buildingId] = l = [];
            }
            l.Add(newType);
        }
    }

    /// <summary>Promotes one idle colonist of <paramref name="currentType"/> to <paramref name="newType"/> (a schooling upgrade, 86d3c9p7f); a free idle is implicit, so promoting it adds an overlay entry and promoting to free removes one.</summary>
    internal void UpgradeIdleWorker(string currentType, string newType)
    {
        RemoveOneIdle(currentType); // no-op for a free colonist (implicit in the idle count)
        if (newType != FreeColonistTypeId)
        {
            _idleWorkerTypes.Add(newType);
        }
    }

    /// <summary>
    /// Accrued training turns toward the current student of a school building's <b>first</b> teacher slot (slot 0)
    /// — the single-teacher reading kept for callers/tests that pre-date parallel teaching (a schoolhouse has only
    /// slot 0). For a multi-teacher college/university use <see cref="SchoolTrainingTurnsAt(string, int)"/>.
    /// (86d3c9p7f / 86d3fpyc0; 0 by default).
    /// </summary>
    public int SchoolTrainingTurnsAt(string buildingId) => SchoolTrainingTurnsAt(buildingId, 0);

    /// <summary>Accrued training turns toward the current student of teacher <paramref name="slot"/> (0-based) in a school building (86d3fpyc0; 0 when absent or the slot is short).</summary>
    public int SchoolTrainingTurnsAt(string buildingId, int slot) =>
        _schoolTrainingTurns.TryGetValue(buildingId, out List<int>? slots) && slot >= 0 && slot < slots.Count ? slots[slot] : 0;

    /// <summary>Accrued school-training turns by building id — the per-teacher-slot list (sparse — a school not currently teaching is absent). 86d3fpyc0.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<int>> SchoolTrainingTurns =>
        _schoolTrainingTurns.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<int>)kv.Value);

    /// <summary>Adds one turn of accrued training to teacher <paramref name="slot"/> (0-based) of a school building, growing the per-slot list as needed (86d3fpyc0).</summary>
    internal void AddSchoolTrainingTurn(string buildingId, int slot = 0)
    {
        if (!_schoolTrainingTurns.TryGetValue(buildingId, out List<int>? slots))
        {
            _schoolTrainingTurns[buildingId] = slots = [];
        }
        while (slots.Count <= slot)
        {
            slots.Add(0);
        }
        slots[slot]++;
    }

    /// <summary>Resets teacher <paramref name="slot"/>'s accrued training (no eligible student this turn, or its student just graduated); drops the building entry once every slot is back to 0 (86d3fpyc0).</summary>
    internal void ResetSchoolTraining(string buildingId, int slot = 0)
    {
        if (!_schoolTrainingTurns.TryGetValue(buildingId, out List<int>? slots))
        {
            return;
        }
        if (slot < slots.Count)
        {
            slots[slot] = 0;
        }
        if (slots.All(t => t == 0))
        {
            _schoolTrainingTurns.Remove(buildingId); // every slot lapsed → keep the map sparse
        }
    }

    /// <summary>Resets <b>every</b> teacher slot of a school building (the school lost its teacher, or has none eligible). 86d3fpyc0.</summary>
    internal void ResetAllSchoolTraining(string buildingId) => _schoolTrainingTurns.Remove(buildingId);

    /// <summary>Restores a school building's per-teacher-slot accrued training from a save (skips an all-zero list to keep the map sparse). 86d3fpyc0.</summary>
    internal void RestoreSchoolTraining(string buildingId, IReadOnlyList<int> slots)
    {
        if (slots.Count > 0 && slots.Any(t => t > 0))
        {
            _schoolTrainingTurns[buildingId] = [.. slots];
        }
    }

    /// <summary>Restores a single-counter (slot-0) school training value from a pre-86d3fpyc0 save (v32..v59 wrote one int per school). 86d3fpyc0 down-version compat.</summary>
    internal void RestoreSchoolTraining(string buildingId, int turns)
    {
        if (turns > 0)
        {
            _schoolTrainingTurns[buildingId] = [turns];
        }
    }

    /// <summary>Adds a colonist of <paramref name="type"/> to the colony's idle pool (founding / joining / growth). Free colonists are implicit.</summary>
    internal void AddIdleColonist(string type)
    {
        if (type != FreeColonistTypeId)
        {
            _idleWorkerTypes.Add(type);
        }
    }

    /// <summary>
    /// Seats an idle <paramref name="expertType"/> into <paramref name="tile"/> (currently worked by a <b>free</b>
    /// colonist), bumping that free colonist into the idle pool — the full expert-swap of FreeCol
    /// <c>Unit.swapWork</c> (86d3drn5j): the expert takes the specialist slot it is best at and the displaced free
    /// colonist is freed to be re-seated elsewhere. The tile keeps its goods assignment; only the worker type changes.
    /// Caller guarantees the tile holds a free colonist and an <paramref name="expertType"/> is idle (checked by
    /// <see cref="GameSession.Game"/> before the call). Resets the tile's accrued experience (a new occupant starts at 0).
    /// </summary>
    internal void SwapInExpertForTile(Position tile, string expertType)
    {
        RemoveOneIdle(expertType);            // the expert leaves the idle pool…
        _tileWorkerTypes[tile] = expertType;  // …and takes the tile (it was free, so previously had no overlay entry)
        _tileWorkerExperience.Remove(tile);   // a (re)seated tile restarts the occupant's experience at 0
        // The displaced free colonist becomes idle. A free colonist is implicit in the idle count, so no overlay add.
    }

    /// <summary>Assigns a colonist of <paramref name="type"/> (from idle) into a building, bumping the count and overlay.</summary>
    internal void AssignBuildingWorker(string buildingId, string type)
    {
        SetBuildingWorkers(buildingId, _buildingWorkers.GetValueOrDefault(buildingId) + 1);
        if (type != FreeColonistTypeId)
        {
            if (!_buildingWorkerTypes.TryGetValue(buildingId, out List<string>? list))
            {
                _buildingWorkerTypes[buildingId] = list = [];
            }
            list.Add(type);
            RemoveOneIdle(type); // the colonist moved idle → building
        }
    }

    /// <summary>Returns one of a building's workers to the idle pool (a non-free occupant rejoins idle; else a free colonist).</summary>
    internal void UnassignBuildingWorker(string buildingId)
    {
        int count = _buildingWorkers.GetValueOrDefault(buildingId);
        if (count <= 0)
        {
            return;
        }
        if (_buildingWorkerTypes.TryGetValue(buildingId, out List<string>? list) && list.Count > 0)
        {
            string type = list[^1];
            list.RemoveAt(list.Count - 1);
            if (list.Count == 0)
            {
                _buildingWorkerTypes.Remove(buildingId);
            }
            _idleWorkerTypes.Add(type);
        }
        SetBuildingWorkers(buildingId, count - 1);
    }

    /// <summary>Restores a building's non-free occupant types from a save (the count is restored separately via <see cref="SetBuildingWorkers"/>).</summary>
    internal void RestoreBuildingWorkerTypes(string buildingId, IEnumerable<string> types) =>
        _buildingWorkerTypes[buildingId] = [.. types];

    /// <summary>Removes one occurrence of <paramref name="type"/> from the idle overlay (a no-op when absent).</summary>
    private void RemoveOneIdle(string type)
    {
        int i = _idleWorkerTypes.LastIndexOf(type);
        if (i >= 0)
        {
            _idleWorkerTypes.RemoveAt(i);
        }
    }

    /// <summary>
    /// Trims the worker-type overlay so it never exceeds the count model (FreeCol has no equivalent — this guards
    /// the sparse overlay against the count-reducing flows, leave/abandon/starve/trim, that don't thread an exact
    /// departing colonist). Drops excess entries deterministically: orphaned tiles, then over-full buildings, then
    /// idle beyond <see cref="IdleColonists"/>. A safety net for count-reducing flows that don't pick an exact
    /// colonist (starvation, job trimming); the deliberate leave/abandon path uses <see cref="RemoveOneColonist"/>,
    /// which picks the departing colonist and emits its real type.
    /// </summary>
    internal void ReconcileWorkerTypes()
    {
        foreach (Position tile in _tileWorkerTypes.Keys.Where(t => !_tileWorkers.ContainsKey(t)).ToList())
        {
            _tileWorkerTypes.Remove(tile);
        }
        foreach (Position tile in _tileWorkerExperience.Keys.Where(t => !_tileWorkers.ContainsKey(t)).ToList())
        {
            _tileWorkerExperience.Remove(tile); // accrued experience belongs to a present tile worker (86d3c9pgj)
        }
        foreach (string building in _buildingWorkerTypes.Keys.ToList())
        {
            List<string> list = _buildingWorkerTypes[building];
            int cap = _buildingWorkers.GetValueOrDefault(building);
            while (list.Count > cap)
            {
                list.RemoveAt(list.Count - 1);
            }
            if (list.Count == 0)
            {
                _buildingWorkerTypes.Remove(building);
            }
        }
        while (_idleWorkerTypes.Count > IdleColonists)
        {
            _idleWorkerTypes.RemoveAt(_idleWorkerTypes.Count - 1);
        }
        foreach (string building in _schoolTrainingTurns.Keys.Where(b => !_buildings.Contains(b)).ToList())
        {
            _schoolTrainingTurns.Remove(building); // accrued training belongs to a present school building (86d3c9p7f)
        }
    }

    /// <summary>
    /// Removes one colonist (for "send a colonist out" / abandon) and returns its unit type, conserving the colony's
    /// mix of specialists (86d3b6nrz slice 6). Mirrors the colony's own count-reduction order so the colonist emitted
    /// is exactly the one the roster loses: an idle colonist first (a free one before a specialist), else the last
    /// building's worker, else the last tile's worker (and within a building/tile a free colonist before a specialist).
    /// Decrements <see cref="Population"/> and updates the counts and overlay together, leaving them consistent — a
    /// free departure leaves the overlay untouched. The caller emits a unit of the returned type.
    /// </summary>
    internal string RemoveOneColonist()
    {
        if (IdleColonists > 0)
        {
            Population--;
            // A free idle colonist leaves unless every remaining idle slot is a specialist.
            if (_idleWorkerTypes.Count > IdleColonists)
            {
                string type = _idleWorkerTypes[^1];
                _idleWorkerTypes.RemoveAt(_idleWorkerTypes.Count - 1);
                return type;
            }
            return FreeColonistTypeId;
        }

        // Everyone is employed: a worker leaves, vacating the last building job, then the last tile job (the order
        // TrimAssignments uses). In each spot a free colonist leaves before a specialist (count > overlay ⇒ a free one).
        if (_buildingWorkers.Count > 0)
        {
            string building = _buildingWorkers.Keys.Last();
            int count = _buildingWorkers[building];
            Population--;
            SetBuildingWorkers(building, count - 1);
            if (_buildingWorkerTypes.TryGetValue(building, out List<string>? list) && list.Count >= count)
            {
                string type = list[^1];
                list.RemoveAt(list.Count - 1);
                if (list.Count == 0)
                {
                    _buildingWorkerTypes.Remove(building);
                }
                return type;
            }
            return FreeColonistTypeId;
        }
        if (_tileWorkers.Count > 0)
        {
            Position tile = _tileWorkers.Keys.OrderBy(p => p.Y).ThenBy(p => p.X).Last();
            Population--;
            _tileWorkers.Remove(tile);
            _tileWorkerExperience.Remove(tile);
            return _tileWorkerTypes.Remove(tile, out string? type) ? type : FreeColonistTypeId;
        }

        Population--; // no idle and no workers (unreachable for Population > 0) — a free colonist leaves
        return FreeColonistTypeId;
    }

    /// <summary>Adds goods to the store (negative removes; floor at 0).</summary>
    internal void AddGoods(string goodsId, int amount) =>
        _stores[goodsId] = Math.Max(0, StoreOf(goodsId) + amount);

    /// <summary>
    /// Adds bell production to liberty (negative on a future deficit turn), floored at 0. At 100% SoL the
    /// accumulation is capped to exactly <c>200·population</c> (FreeCol <c>bellAccumulationCapped</c>), so it never
    /// overshoots — the cap reads the population already settled for this turn (growth/starvation run before banking).
    /// </summary>
    /// <summary>
    /// Turns remaining of the Boston-Tea-Party bell bonus (FreeCol <c>colonyGoodsParty</c> modifier: +50% bell
    /// output decaying −2%/turn over 25 turns). 0 = none. Set when the colony holds a tea party; ticked down each
    /// turn. Persisted v37 (omit-when-0). See [monarchy].
    /// </summary>
    public int TeaPartyBellTurns { get; internal set; }

    /// <summary>The current tea-party bell-output bonus percentage: +2% per remaining turn (so +50% at 25 turns down to 0).</summary>
    public int TeaPartyBellBonusPercent => TeaPartyBellTurns * 2;

    /// <summary>Decays the tea-party bell bonus by one turn (a no-op when none is active).</summary>
    internal void TickTeaPartyBonus()
    {
        if (TeaPartyBellTurns > 0)
        {
            TeaPartyBellTurns--;
        }
    }

    internal void AddLiberty(int amount)
    {
        Liberty = Math.Max(0, Liberty + amount);
        if (SonsOfLiberty >= 100)
        {
            Liberty = RebelLibertyDivisor * Population;
        }
    }

    /// <summary>Removes food. Returns the shortfall (0 when the store covered it all).</summary>
    internal int ConsumeFood(int amount)
    {
        int take = Math.Min(amount, Food);
        AddGoods(FoodId, -take);
        return amount - take;
    }

    /// <summary>All non-default custom-house export/import settings (good id → setting), sparse — a default good is absent.</summary>
    public IReadOnlyDictionary<string, ExportSetting> Exports => _exports;

    /// <summary>The export/import setting for a good — its stored setting, or the default (not exported, retain <see cref="ExportRetainDefault"/>, import level unset).</summary>
    public ExportSetting ExportOf(string goodsId) =>
        _exports.GetValueOrDefault(goodsId, new ExportSetting(false, ExportRetainDefault, ImportLevelUnset));

    /// <summary>
    /// Sets a good's custom-house <b>export</b> pair (exported flag + retain level, floored at 0), <em>preserving</em> the
    /// good's current import level. A good back at the all-default state (not exported, retain <see cref="ExportRetainDefault"/>,
    /// import level <see cref="ImportLevelUnset"/>) is <em>removed</em>, so an untouched/reset good leaves no trace and the
    /// save stays byte-stable.
    /// </summary>
    internal void SetExport(string goodsId, bool exported, int exportLevel) =>
        Store(goodsId, exported, Math.Max(0, exportLevel), ExportOf(goodsId).ImportLevel);

    /// <summary>
    /// Sets a good's custom-house <b>import level</b> — the ceiling an automatic delivery will not stock the good past
    /// (FreeCol <c>ExportData.setImportLevel</c>) — <em>preserving</em> the good's current export flag and retain level. A
    /// non-negative level is honoured; any negative value is normalised to <see cref="ImportLevelUnset"/> (−1, "not set" →
    /// the effective cap is the warehouse capacity). A good back at the all-default state is removed (save stays byte-stable).
    /// </summary>
    internal void SetImport(string goodsId, int importLevel)
    {
        ExportSetting current = ExportOf(goodsId);
        Store(goodsId, current.Exported, current.ExportLevel, importLevel < 0 ? ImportLevelUnset : importLevel);
    }

    /// <summary>
    /// Writes (or, when fully default, removes) a good's export/import setting. A setting equal to the default
    /// (not exported, retain <see cref="ExportRetainDefault"/>, import level <see cref="ImportLevelUnset"/>) is removed so
    /// the sparse dictionary only ever holds non-default goods — the invariant that keeps a default game byte-stable.
    /// </summary>
    private void Store(string goodsId, bool exported, int exportLevel, int importLevel)
    {
        if (!exported && exportLevel == ExportRetainDefault && importLevel == ImportLevelUnset)
        {
            _exports.Remove(goodsId);
        }
        else
        {
            _exports[goodsId] = new ExportSetting(exported, exportLevel, importLevel);
        }
    }
}
