namespace CrownAndColony.GameLogic.Specification;

/// <summary>
/// A building type from the ruleset (town hall, carpenter's house, lumber mill…)
/// — immutable rule data with inherited attributes resolved.
/// </summary>
/// <param name="Id">Ruleset id, e.g. <c>model.building.carpenterHouse</c>.</param>
/// <param name="UpgradesFrom">Building this one replaces (null for base buildings).</param>
/// <param name="Workplaces">Colonists who can work here (FreeCol default 3).</param>
/// <param name="RequiredPopulation">Minimum colony population to build it.</param>
/// <param name="Productions">Per-worker (or unattended) conversions, e.g. lumber 3 → hammers 3.</param>
/// <param name="BuildCost">Goods required to construct it (hammers, tools…).</param>
/// <param name="DefenceBonus">
/// Percentage defence bonus this building grants a unit defending in the colony (spec
/// <c>model.modifier.defence</c>; default 0). Stockade +100, fort +150, fortress +200 — the colony's
/// fortification tier (FreeCol applies it to the colony's defender, garrison or last colonist).
/// </param>
/// <param name="WarehouseStorage">
/// Warehouse capacity this building grants, in goods per type (spec <c>model.modifier.warehouseStorage</c>,
/// additive, summed up the <c>extends</c> chain; default 0). Depot 100, warehouse 200, expansion 300 — a colony
/// holds one of these tiers, capping each storable good (FreeCol <c>Settlement.getWarehouseCapacity</c>).
/// </param>
/// <param name="BellBonus">
/// Percentage boost this building gives the colony's bell output (spec <c>model.goods.bells</c> percentage;
/// default 0). Printing press +50, newspaper +100 (the newspaper deletes + redefines the inherited modifier —
/// own-valued one taken, like <see cref="DefenceBonus"/>). Speeds Sons-of-Liberty + founding-father progress.
/// </param>
public sealed record BuildingType(
    string Id,
    string? UpgradesFrom,
    int Workplaces,
    int RequiredPopulation,
    IReadOnlyList<ProductionEntry> Productions,
    IReadOnlyList<GoodsOutput> BuildCost,
    int DefenceBonus = 0,
    int WarehouseStorage = 0,
    int BellBonus = 0)
{
    /// <summary>Short name derived from the id: <c>model.building.townHall</c> → <c>townHall</c>.</summary>
    public string ShortName => Id[(Id.LastIndexOf('.') + 1)..];
}
