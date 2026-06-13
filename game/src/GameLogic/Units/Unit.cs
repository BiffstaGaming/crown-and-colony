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
    /// The id of the ship carrying this unit, or null when not aboard. A carried
    /// unit's <see cref="Location"/>/<see cref="Position"/> mirror its carrier's.
    /// </summary>
    public int? CarrierId { get; internal set; }

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

    /// <summary>Restores full movement at the start of a turn.</summary>
    internal void ResetMovement() => MovementLeft = Type.Movement;
}
