using CrownAndColony.GameLogic.World;

namespace CrownAndColony.GameLogic.Colonies;

/// <summary>
/// An immutable, read-only glimpse of a rival colony's full interior — the oracle a successful
/// <see cref="GameSession.Game.SpyOnColony(Units.Unit, World.Position)">spy-on-colony</see> action hands the
/// presentation to render the same panel the owner sees (FreeCol <c>InGameController.spySettlement</c> /
/// <c>ChangeSet.addSpy</c>, which sends the colony's full <c>Tile</c> to the spying client for a one-shot view).
///
/// It is a <b>snapshot</b>, not a live link: the values are copied out of the colony at the instant of spying, so the
/// presentation can show buildings, garrison-free workforce layout, warehouse stockpile, and Sons-of-Liberty standing
/// without the player gaining any ongoing visibility (FreeCol reveals the interior for that one look only). Nothing here
/// mutates game state.
/// </summary>
/// <param name="ColonyId">The spied colony's stable id.</param>
/// <param name="Name">The colony's display name.</param>
/// <param name="OwnerId">The rival owner's player id.</param>
/// <param name="Position">The colony's map tile.</param>
/// <param name="Population">Number of colonists living in the colony.</param>
/// <param name="SonsOfLiberty">The colony's Sons-of-Liberty membership percentage (0–100).</param>
/// <param name="ProductionBonus">The per-worker production bonus (−2..+2) the colony's morale yields.</param>
/// <param name="Buildings">Building type ids present, in construction order.</param>
/// <param name="CurrentBuild">The buildable currently under construction, or null when the queue is idle.</param>
/// <param name="BuildingWorkers">Building type id → worker count.</param>
/// <param name="TileWorkers">Worked surrounding tile → the goods produced there.</param>
/// <param name="Stores">Warehouse contents: goods id → amount.</param>
public sealed record ColonyInteriorSnapshot(
    int ColonyId,
    string Name,
    int OwnerId,
    Position Position,
    int Population,
    int SonsOfLiberty,
    int ProductionBonus,
    IReadOnlyList<string> Buildings,
    string? CurrentBuild,
    IReadOnlyDictionary<string, int> BuildingWorkers,
    IReadOnlyDictionary<Position, string> TileWorkers,
    IReadOnlyDictionary<string, int> Stores)
{
    /// <summary>Copies a colony's current interior into an immutable snapshot (a defensive copy — the snapshot never tracks later changes).</summary>
    public static ColonyInteriorSnapshot Of(Colony colony) => new(
        colony.Id,
        colony.Name,
        colony.OwnerId,
        colony.Position,
        colony.Population,
        colony.SonsOfLiberty,
        colony.ProductionBonus,
        [.. colony.Buildings],
        colony.CurrentBuild,
        new Dictionary<string, int>(colony.BuildingWorkers),
        new Dictionary<Position, string>(colony.TileWorkers),
        new Dictionary<string, int>(colony.Stores));
}

/// <summary>
/// The outcome of a <see cref="GameSession.Game.SpyOnColony(Units.Unit, World.Position)">spy-on-colony</see> attempt: the
/// scout either gets its glimpse (<see cref="Snapshot"/> populated, <see cref="Rebuffed"/> false) or is turned away at the
/// gate by the colony's watch (<see cref="Rebuffed"/> true, <see cref="Snapshot"/> null). Either way the scout's turn ends.
/// </summary>
/// <param name="Rebuffed">Whether the colony's watch rebuffed the scout (no interior was revealed).</param>
/// <param name="Snapshot">The colony interior the scout glimpsed, or null when rebuffed.</param>
public readonly record struct SpyResult(bool Rebuffed, ColonyInteriorSnapshot? Snapshot)
{
    /// <summary>A successful spy revealing <paramref name="snapshot"/>.</summary>
    public static SpyResult Revealed(ColonyInteriorSnapshot snapshot) => new(false, snapshot);

    /// <summary>A rebuffed spy — the scout saw nothing.</summary>
    public static SpyResult Rebuff() => new(true, null);
}
