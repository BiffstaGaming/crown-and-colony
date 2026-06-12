using CrownAndColony.GameLogic.World;

namespace CrownAndColony.GameLogic.Units;

/// <summary>
/// A unit on the map. Phase 1 skeleton: every unit is a generic land unit with
/// 3 movement points (one normal move per turn, matching the free colonist).
/// Unit types parsed from the ruleset arrive with the units system proper.
/// </summary>
public sealed class Unit
{
    /// <summary>Movement points per turn for the skeleton's generic unit (spec scale: 3 = one normal move).</summary>
    public const int BaseMovementPoints = 3;

    /// <summary>Creates a unit at a position with full movement.</summary>
    public Unit(int id, Position position)
    {
        Id = id;
        Position = position;
        MovementLeft = BaseMovementPoints;
    }

    /// <summary>Stable per-game identifier.</summary>
    public int Id { get; }

    /// <summary>Current map position.</summary>
    public Position Position { get; internal set; }

    /// <summary>Movement points remaining this turn (spec scale: terrain costs 3/6/9).</summary>
    public int MovementLeft { get; internal set; }

    /// <summary>Restores full movement at the start of a turn.</summary>
    internal void ResetMovement() => MovementLeft = BaseMovementPoints;
}
