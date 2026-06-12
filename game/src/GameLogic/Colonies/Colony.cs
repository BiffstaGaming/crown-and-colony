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

    /// <summary>Goods ids that count as food (grain and fish both store as food).</summary>
    public static readonly IReadOnlySet<string> FoodGoods =
        new HashSet<string> { "model.goods.grain", "model.goods.fish" };

    private readonly Dictionary<string, int> _stores = [];

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

    /// <summary>Total stored food across all food goods.</summary>
    public int Food => FoodGoods.Sum(StoreOf);

    /// <summary>Adds goods to the store (negative removes; floor at 0).</summary>
    internal void AddGoods(string goodsId, int amount) =>
        _stores[goodsId] = Math.Max(0, StoreOf(goodsId) + amount);

    /// <summary>
    /// Removes food, draining grain before fish. Returns the shortfall
    /// (0 when the store covered it all).
    /// </summary>
    internal int ConsumeFood(int amount)
    {
        foreach (string goods in FoodGoods)
        {
            int take = Math.Min(amount, StoreOf(goods));
            AddGoods(goods, -take);
            amount -= take;
        }
        return amount;
    }
}
