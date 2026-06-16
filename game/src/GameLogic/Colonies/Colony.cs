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

    /// <summary>Display name (e.g. "Jamestown").</summary>
    public string Name { get; }

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

    /// <summary>Building type currently under construction (null when idle).</summary>
    public string? CurrentBuild { get; internal set; }

    /// <summary>
    /// Accumulated liberty points from bell production (FreeCol <c>Colony.liberty</c>). Drives
    /// <see cref="SonsOfLiberty"/>. Floored at 0; persisted (SaveGame v22). NOTE: this first cut accumulates the
    /// same (founding-father-modified) bell figure the player pool gets, with no bell upkeep yet — so SoL only
    /// rises; the FreeCol net-of-upkeep accumulation (which lets SoL fall) lands with the production-bonus slice.
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
}
