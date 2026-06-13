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

    private readonly Dictionary<string, int> _stores = [];
    private readonly Dictionary<Position, string> _tileWorkers = [];

    /// <summary>Creates a colony.</summary>
    public Colony(int id, string name, Position position, int population)
    {
        Id = id;
        Name = name;
        Position = position;
        Population = population;
    }

    /// <summary>Stable per-game identifier.</summary>
    public int Id { get; }

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

    /// <summary>Colonists without a tile assignment.</summary>
    public int IdleColonists => Population - _tileWorkers.Count;

    internal void SetWorker(Position tile, string goodsId) => _tileWorkers[tile] = goodsId;

    internal void RemoveWorker(Position tile) => _tileWorkers.Remove(tile);

    /// <summary>Adds goods to the store (negative removes; floor at 0).</summary>
    internal void AddGoods(string goodsId, int amount) =>
        _stores[goodsId] = Math.Max(0, StoreOf(goodsId) + amount);

    /// <summary>Removes food. Returns the shortfall (0 when the store covered it all).</summary>
    internal int ConsumeFood(int amount)
    {
        int take = Math.Min(amount, Food);
        AddGoods(FoodId, -take);
        return amount - take;
    }
}
