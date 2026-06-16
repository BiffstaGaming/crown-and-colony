using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.World;

namespace CrownAndColony.GameLogic.Units;

/// <summary>Where a unit is: on the map, in transit across the high seas, or docked in Europe.</summary>
public enum UnitLocation
{
    /// <summary>On the game map at <see cref="Unit.Position"/>.</summary>
    OnMap,

    /// <summary>Sailing the high seas toward Europe.</summary>
    SailingToEurope,

    /// <summary>Docked in Europe.</summary>
    InEurope,

    /// <summary>Sailing the high seas back to the New World.</summary>
    SailingToNewWorld,
}

/// <summary>
/// A unit's standing order (FreeCol <c>Unit.UnitState</c>, the subset that affects play on the map).
/// <see cref="Active"/> is the default: free to act. <see cref="Fortifying"/> is the one-turn dig-in that
/// becomes <see cref="Fortified"/> at the next turn reset (FreeCol ages FORTIFYING → FORTIFIED), granting a
/// +50% defence bonus. <see cref="Sentry"/> rests the unit (skipped when cycling, no orders prompt) until
/// something happens. Moving or attacking clears the order back to <see cref="Active"/>.
/// </summary>
public enum UnitOrders
{
    /// <summary>Free to act (the default).</summary>
    Active,

    /// <summary>Digging in this turn; becomes <see cref="Fortified"/> at the next turn reset.</summary>
    Fortifying,

    /// <summary>Dug in: +50% defence (FreeCol <c>model.modifier.fortified</c>).</summary>
    Fortified,

    /// <summary>Resting until something happens (skipped when cycling units).</summary>
    Sentry,
}

/// <summary>A unit on the map. Its capabilities come from its ruleset <see cref="UnitType"/>.</summary>
public sealed class Unit
{
    private readonly Dictionary<string, int> _cargo = [];

    /// <summary>Creates a unit of a type at a position with full movement.</summary>
    public Unit(int id, UnitType type, Position position)
    {
        Id = id;
        Type = type;
        Position = position;
        MovementLeft = type.Movement;
    }

    /// <summary>Stable per-game identifier.</summary>
    public int Id { get; }

    /// <summary>The ruleset type (movement, sight, naval, …).</summary>
    public UnitType Type { get; }

    /// <summary>
    /// Last map position. Meaningful while <see cref="Location"/> is on the map or
    /// when returning from Europe (the tile it departed from / will re-enter at).
    /// </summary>
    public Position Position { get; internal set; }

    /// <summary>Movement points remaining this turn (spec scale: terrain costs 3/6/9).</summary>
    public int MovementLeft { get; internal set; }

    /// <summary>Where the unit currently is (map / sailing / Europe).</summary>
    public UnitLocation Location { get; internal set; } = UnitLocation.OnMap;

    /// <summary>Turns left before a sailing unit arrives (0 when not sailing).</summary>
    public int SailTurnsRemaining { get; internal set; }

    /// <summary>
    /// Turns left before a damaged ship is repaired (0 when healthy). A ship beaten in combat (but not
    /// sunk) limps to a repair location — Europe in our model — at this many turns and recovers one per
    /// turn (FreeCol's hit-point repair: <c>MaxHitPoints − 1</c> turns, 5 for every classic ship). While
    /// it is &gt; 0 the ship is under forced repair and cannot act (see <see cref="IsUnderRepair"/>).
    /// </summary>
    public int RepairTurnsRemaining { get; internal set; }

    /// <summary>True while a damaged ship is repairing (and so cannot sail or act).</summary>
    public bool IsUnderRepair => RepairTurnsRemaining > 0;

    /// <summary>
    /// The unit's standing order (active / fortifying / fortified / sentry). Only <see cref="GameSession.Game"/>
    /// mutates it: <c>Fortify</c>/<c>Sentry</c>/<c>ClearOrders</c> set it, the per-turn reset ages
    /// <see cref="UnitOrders.Fortifying"/> → <see cref="UnitOrders.Fortified"/>, and moving clears it.
    /// </summary>
    public UnitOrders Orders { get; internal set; } = UnitOrders.Active;

    /// <summary>True when dug in (FreeCol FORTIFIED): the unit defends at +50% (see <see cref="Combat.CombatModel"/>).</summary>
    public bool IsFortified => Orders == UnitOrders.Fortified;

    /// <summary>
    /// The id of the ship carrying this unit, or null when not aboard. A carried
    /// unit's <see cref="Location"/>/<see cref="Position"/> mirror its carrier's.
    /// </summary>
    public int? CarrierId { get; internal set; }

    /// <summary>
    /// The owning native nation's type id (e.g. <c>model.nationType.apache</c>) when this is a
    /// native brave, or null when owned by a colonial player. Native ownership is carried here until
    /// natives become players (FP-3b); colonial ownership is <see cref="OwnerId"/>.
    /// </summary>
    public string? OwnerNationId { get; internal set; }

    /// <summary>
    /// The owning colonial player's id (FP-2; ADR-019). The human is 0; foreign colonial powers get
    /// their own ids in FP-3b. Authoritative for colonial ownership; unused (0) for a native-owned unit,
    /// whose owner is <see cref="OwnerNationId"/>. The enemy/fog/ability rules resolve the owner via <see cref="GameSession.Game"/>.
    /// </summary>
    public int OwnerId { get; internal set; }

    /// <summary>True when owned by a native nation (a brave), not a colonial player.</summary>
    public bool IsNative => OwnerNationId is not null;

    /// <summary>
    /// The unit's military status / equipment (FreeCol <see cref="Specification.RoleType"/> id);
    /// the unarmed default role unless armed (soldier/dragoon) or a brave equipped from stock.
    /// </summary>
    public string RoleId { get; internal set; } = Specification.RoleType.DefaultRoleId;

    /// <summary>The multiple of the role's required goods held (FreeCol <c>roleCount</c>); 0 for the default role.</summary>
    public int RoleCount { get; internal set; }

    /// <summary>True when the unit is in the unarmed default role.</summary>
    public bool HasDefaultRole => RoleId == Specification.RoleType.DefaultRoleId;

    /// <summary>True when this unit is a passenger aboard a ship.</summary>
    public bool IsAboard => CarrierId is not null;

    /// <summary>True when the unit is on the game map (not sailing, in Europe, or aboard a ship).</summary>
    public bool IsOnMap => Location == UnitLocation.OnMap && !IsAboard;

    /// <summary>Goods carried in the unit's hold (naval cargo).</summary>
    public IReadOnlyDictionary<string, int> Cargo => _cargo;

    /// <summary>Amount of one good in the hold.</summary>
    public int CargoOf(string goodsId) => _cargo.GetValueOrDefault(goodsId);

    /// <summary>Adds goods to the hold (negative removes; floor at 0).</summary>
    internal void AddCargo(string goodsId, int amount)
    {
        int next = Math.Max(0, CargoOf(goodsId) + amount);
        if (next == 0)
        {
            _cargo.Remove(goodsId);
        }
        else
        {
            _cargo[goodsId] = next;
        }
    }

    /// <summary>Empties the hold (a sunk or damaged ship loses its goods — FreeCol jettisons cargo on either).</summary>
    internal void ClearCargo() => _cargo.Clear();

}
