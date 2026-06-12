using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.World;

namespace CrownAndColony.GameLogic.Units;

/// <summary>A unit on the map. Its capabilities come from its ruleset <see cref="UnitType"/>.</summary>
public sealed class Unit
{
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

    /// <summary>Current map position.</summary>
    public Position Position { get; internal set; }

    /// <summary>Movement points remaining this turn (spec scale: terrain costs 3/6/9).</summary>
    public int MovementLeft { get; internal set; }

    /// <summary>Restores full movement at the start of a turn.</summary>
    internal void ResetMovement() => MovementLeft = Type.Movement;
}
