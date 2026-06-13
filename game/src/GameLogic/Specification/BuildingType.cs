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
public sealed record BuildingType(
    string Id,
    string? UpgradesFrom,
    int Workplaces,
    int RequiredPopulation,
    IReadOnlyList<ProductionEntry> Productions,
    IReadOnlyList<GoodsOutput> BuildCost)
{
    /// <summary>Short name derived from the id: <c>model.building.townHall</c> → <c>townHall</c>.</summary>
    public string ShortName => Id[(Id.LastIndexOf('.') + 1)..];
}
