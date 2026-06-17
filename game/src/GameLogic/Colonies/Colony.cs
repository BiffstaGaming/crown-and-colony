using CrownAndColony.GameLogic.World;

namespace CrownAndColony.GameLogic.Colonies;

/// <summary>
/// A colony on the map: identity, location, population, and goods stores.
/// Tile work assignments, buildings, and production chains are later
/// Phase 3 slices.
/// </summary>
public sealed class Colony
{
    /// <summary>Food a colonist eats per turn (original's value — cross-check flagged in docs).</summary>
    public const int FoodPerColonist = 2;

    /// <summary>Stored food needed for a new colonist to be born (original's threshold).</summary>
    public const int FoodForGrowth = 200;

    /// <summary>
    /// The warehouse food id — grain/fish/meat all store as this
    /// (spec <c>stored-as</c>; normalization happens in <see cref="GameSession.Game"/>).
    /// </summary>
    public const string FoodId = "model.goods.food";

    /// <summary>Liberty points one rebel colonist represents (FreeCol <c>Colony.LIBERTY_PER_REBEL</c> = 200; a real code constant, not a difficulty option).</summary>
    public const int LibertyPerRebel = 200;

    // Government thresholds for the production bonus. These four are FreeCol *difficulty options*, and they DIFFER by
    // difficulty (veryEasy 8/12 … medium 6/10 … veryHard 4/8). We hardcode the **medium** values — the classic
    // default — because we don't model difficulty levels yet (same choice as the native-demand tuning). When a
    // difficulty system lands, these must become data-driven.
    /// <summary>SoL% at/above which government is "very good" → +2 per worker (FreeCol medium <c>veryGoodGovernmentLimit</c>).</summary>
    public const int VeryGoodGovernmentLimit = 100;
    /// <summary>SoL% at/above which government is "good" → +1 per worker (FreeCol medium <c>goodGovernmentLimit</c>).</summary>
    public const int GoodGovernmentLimit = 50;
    /// <summary>Tory count above which government is "bad" → −1 per worker (FreeCol medium <c>badGovernmentLimit</c>).</summary>
    public const int BadGovernmentLimit = 6;
    /// <summary>Tory count above which government is "very bad" → −2 per worker (FreeCol medium <c>veryBadGovernmentLimit</c>).</summary>
    public const int VeryBadGovernmentLimit = 10;

    private readonly Dictionary<string, int> _stores = [];
    private readonly Dictionary<Position, string> _tileWorkers = [];
    private readonly List<string> _buildings = [];
    private readonly Dictionary<string, int> _buildingWorkers = [];
    private readonly Dictionary<string, ExportSetting> _exports = [];

    /// <summary>The custom-house export setting for one good (FreeCol <c>ExportData</c>): whether it auto-exports and the amount to retain.</summary>
    /// <param name="Exported">Whether the custom house auto-sells this good's surplus.</param>
    /// <param name="ExportLevel">The amount to keep in the warehouse before exporting the rest.</param>
    public readonly record struct ExportSetting(bool Exported, int ExportLevel);

    /// <summary>The default retain level for a good not yet configured (FreeCol <c>ExportData.EXPORT_LEVEL_DEFAULT</c>).</summary>
    public const int DefaultExportLevel = 50;

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
        Population <= 0 ? 0 : Math.Clamp(Liberty * 100 / (LibertyPerRebel * Population) + SolModifierBonus, 0, 100);

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
        SonsOfLiberty >= VeryGoodGovernmentLimit ? 2
        : SonsOfLiberty >= GoodGovernmentLimit ? 1
        : ToryCount > VeryBadGovernmentLimit ? -2
        : ToryCount > BadGovernmentLimit ? -1
        : 0;

    internal void AddBuilding(string buildingId) => _buildings.Add(buildingId);

    /// <summary>Swaps an upgraded building for its successor, preserving staffing.</summary>
    internal void ReplaceBuilding(string oldId, string newId)
    {
        int index = _buildings.IndexOf(oldId);
        _buildings[index] = newId;
        if (_buildingWorkers.Remove(oldId, out int workers))
        {
            _buildingWorkers[newId] = workers;
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

    internal void SetWorker(Position tile, string goodsId) => _tileWorkers[tile] = goodsId;

    internal void RemoveWorker(Position tile) => _tileWorkers.Remove(tile);

    /// <summary>Adds goods to the store (negative removes; floor at 0).</summary>
    internal void AddGoods(string goodsId, int amount) =>
        _stores[goodsId] = Math.Max(0, StoreOf(goodsId) + amount);

    /// <summary>
    /// Adds bell production to liberty (negative on a future deficit turn), floored at 0. At 100% SoL the
    /// accumulation is capped to exactly <c>200·population</c> (FreeCol <c>bellAccumulationCapped</c>), so it never
    /// overshoots — the cap reads the population already settled for this turn (growth/starvation run before banking).
    /// </summary>
    internal void AddLiberty(int amount)
    {
        Liberty = Math.Max(0, Liberty + amount);
        if (SonsOfLiberty >= 100)
        {
            Liberty = LibertyPerRebel * Population;
        }
    }

    /// <summary>Removes food. Returns the shortfall (0 when the store covered it all).</summary>
    internal int ConsumeFood(int amount)
    {
        int take = Math.Min(amount, Food);
        AddGoods(FoodId, -take);
        return amount - take;
    }

    /// <summary>All non-default custom-house export settings (good id → setting), sparse — a default good is absent.</summary>
    public IReadOnlyDictionary<string, ExportSetting> Exports => _exports;

    /// <summary>The export setting for a good — its stored setting, or the default (not exported, retain <see cref="DefaultExportLevel"/>).</summary>
    public ExportSetting ExportOf(string goodsId) =>
        _exports.GetValueOrDefault(goodsId, new ExportSetting(false, DefaultExportLevel));

    /// <summary>
    /// Sets a good's custom-house export setting (the level is floored at 0). A setting equal to the default
    /// (not exported, retain <see cref="DefaultExportLevel"/>) is <em>removed</em>, so an untouched/reset good
    /// leaves no trace and the save stays byte-stable.
    /// </summary>
    internal void SetExport(string goodsId, bool exported, int exportLevel)
    {
        int level = Math.Max(0, exportLevel);
        if (!exported && level == DefaultExportLevel)
        {
            _exports.Remove(goodsId);
        }
        else
        {
            _exports[goodsId] = new ExportSetting(exported, level);
        }
    }
}
