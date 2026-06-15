namespace CrownAndColony.GameLogic.Specification;

/// <summary>
/// A goods type from the ruleset (food, sugar, rum, …) — immutable rule data.
/// </summary>
/// <param name="Id">Ruleset id, e.g. <c>model.goods.sugar</c>.</param>
/// <param name="IsFood">Counts as food (grain, fish, meat, and food itself).</param>
/// <param name="StoredAs">
/// The goods id this is stored as in a colony's warehouse — grain/fish/meat all
/// store as <c>model.goods.food</c>; everything else stores as itself.
/// </param>
/// <param name="MadeFrom">Raw input this refined goods is made from (null for raw goods).</param>
/// <param name="IsFarmed">Produced by working tiles (vs manufactured in buildings).</param>
/// <param name="BreedingNumber">
/// Minimum stored amount before auto-production breeds more (horses: 2);
/// null for goods that don't breed.
/// </param>
/// <param name="IsNewWorldGoods">
/// A raw New World good (sugar, tobacco, cotton, furs); their prices (and those
/// of goods made from them) are capped to curb runaway spikes.
/// </param>
/// <param name="IsStorable">
/// Whether this good can sit in a warehouse and be looted/traded (spec <c>storable</c>, default true).
/// Bells, crosses and hammers are <c>storable="false"</c> — they accrue toward liberty/immigration/construction
/// and are never warehoused goods (FreeCol <c>AbstractGoods.isStorable</c>; e.g. excluded from pillage loot).
/// </param>
/// <param name="Market">European market seed for this good, or null if it doesn't trade.</param>
public sealed record GoodsType(
    string Id,
    bool IsFood,
    string StoredAs,
    string? MadeFrom,
    bool IsFarmed,
    int? BreedingNumber,
    bool IsNewWorldGoods,
    bool IsStorable,
    GoodsMarket? Market)
{
    /// <summary>Short name derived from the id: <c>model.goods.food</c> → <c>food</c>.</summary>
    public string ShortName => Id[(Id.LastIndexOf('.') + 1)..];

    /// <summary>Whether this good can be bought and sold in the European market.</summary>
    public bool IsTradeable => Market is not null;
}

/// <summary>
/// A good's European market seed (the spec's <c>&lt;market&gt;</c> element): the
/// starting inventory and prices the player's market begins with.
/// </summary>
/// <param name="InitialAmount">Goods already in the European market — the supply that sets the price.</param>
/// <param name="InitialPrice">Starting sell (bid) price per unit — what the player receives.</param>
/// <param name="PriceDifference">Spread added to the bid to get the buy (ask) price.</param>
public sealed record GoodsMarket(int InitialAmount, int InitialPrice, int PriceDifference)
{
    /// <summary>Starting buy (ask) price per unit — what the player pays.</summary>
    public int InitialAskPrice => InitialPrice + PriceDifference;
}
