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
public sealed record GoodsType(
    string Id,
    bool IsFood,
    string StoredAs,
    string? MadeFrom,
    bool IsFarmed)
{
    /// <summary>Short name derived from the id: <c>model.goods.food</c> → <c>food</c>.</summary>
    public string ShortName => Id[(Id.LastIndexOf('.') + 1)..];
}
