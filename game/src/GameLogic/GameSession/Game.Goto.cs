using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;

namespace CrownAndColony.GameLogic.GameSession;

/// <summary>
/// Goto / multi-turn move orders + unit cycling (86d3c9pfy). A unit is given a standing map
/// <see cref="Unit.Destination"/> via the ADR-006 oracle/mutator pair (<see cref="CheckSetDestination"/> +
/// <see cref="SetDestination"/>); each turn <see cref="ProcessGotos"/> walks it toward the goal with a
/// deterministic RNG-free A* (<see cref="Pathfinder"/>), and <see cref="NextUnitToMove"/> lets the presentation
/// layer cycle to the next unit that still needs orders. No randomness is drawn (ADR-009); the goto UI is P7.
/// </summary>
public sealed partial class Game
{
    /// <summary>The least-cost route a unit would take to <paramref name="goal"/> (the tiles to enter; empty = no route).</summary>
    internal IReadOnlyList<Position> FindPath(Unit unit, Position goal) =>
        Pathfinder.FindPath(Map, unit.Position, goal, p => CanPathEnter(unit, p));

    /// <summary>
    /// Whether <paramref name="unit"/> may path through <paramref name="p"/>: the same blocking rules as
    /// <see cref="CheckMove"/> (terrain match, no enemy, no native settlement, only an own colony) minus the
    /// single-step/movement checks, plus a fog gate so a goto only routes through tiles its owner has seen
    /// (FreeCol's <c>BaseCostDecider</c> rejects unexplored tiles).
    /// </summary>
    private bool CanPathEnter(Unit unit, Position p)
    {
        if (!Map.InBounds(p))
        {
            return false;
        }
        if (Map.TerrainAt(p).IsWater != unit.Type.IsNaval)
        {
            return false; // land units keep to land, ships to water
        }
        if (PlayerById(unit.OwnerId) is not { } owner || !owner.Explored.Contains(p))
        {
            return false; // route only through tiles the owner has explored
        }
        if (DefenderAt(unit, p) is not null)
        {
            return false; // an enemy holds it
        }
        if (!unit.IsNative && NativeSettlementAt(p) is not null)
        {
            return false; // a native settlement holds it
        }
        if (ColonyAt(p) is { } colony && (unit.IsNative || colony.OwnerId != unit.OwnerId))
        {
            return false; // only an own colony may be entered
        }
        return true;
    }

    /// <summary>Whether <paramref name="unit"/> may be given a goto order to <paramref name="goal"/>, and why not if not.</summary>
    public MoveCheck CheckSetDestination(Unit unit, Position goal)
    {
        if (!unit.IsOnMap)
        {
            return MoveCheck.No("The unit is at sea or in Europe.");
        }
        if (!Map.InBounds(goal))
        {
            return MoveCheck.No("Destination is off the map.");
        }
        if (goal == unit.Position)
        {
            return MoveCheck.No("The unit is already there.");
        }
        if (Map.TerrainAt(goal).IsWater != unit.Type.IsNaval)
        {
            return MoveCheck.No(unit.Type.IsNaval ? "A ship can only sail to water." : "A land unit can only march to land.");
        }
        if (PlayerById(unit.OwnerId) is not { } owner || !owner.Explored.Contains(goal))
        {
            return MoveCheck.No("You cannot set a course into unexplored territory.");
        }
        if (FindPath(unit, goal).Count == 0)
        {
            return MoveCheck.No("There is no route to that destination.");
        }
        return MoveCheck.Yes(0);
    }

    /// <summary>Gives <paramref name="unit"/> a standing goto order to <paramref name="goal"/>.</summary>
    /// <exception cref="InvalidMoveException">The order is not allowed; see <see cref="CheckSetDestination"/>.</exception>
    public void SetDestination(Unit unit, Position goal)
    {
        MoveCheck check = CheckSetDestination(unit, goal);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }
        unit.Destination = goal;
    }

    /// <summary>Cancels a unit's standing goto order (a no-op when it has none).</summary>
    public void ClearDestination(Unit unit) => unit.Destination = null;

    /// <summary>
    /// Walks <paramref name="unit"/> toward its <see cref="Unit.Destination"/> as far as this turn's movement allows,
    /// recomputing the route each step. Stops on: reached (the goto clears), out of moves, or no route (the goto is
    /// kept so it resumes next turn). Returns what happened. RNG-free except for any event a step itself triggers
    /// (e.g. the owning player's own Lost City Rumour). A no-op for a unit with no destination.
    /// </summary>
    public GotoAdvance AdvanceDestination(Unit unit)
    {
        if (unit.Destination is not { } goal || !unit.IsOnMap)
        {
            return new GotoAdvance(GotoOutcome.NotGoing, 0);
        }

        int steps = 0;
        while (unit.MovementLeft > 0)
        {
            if (unit.Position == goal)
            {
                unit.Destination = null;
                return new GotoAdvance(GotoOutcome.Reached, steps);
            }
            if (Map.HasRumour(unit.Position))
            {
                // The unit is on an unresolved Lost City Rumour — a strange-mounds prompt left on the tile by the
                // step that landed here (or a pending one it started on). Stop so the player decides with the unit
                // standing on the rumour, as in FreeCol (exploring a rumour ends the move). The goto is kept and
                // resumes once the rumour is cleared.
                return new GotoAdvance(GotoOutcome.Interrupted, steps);
            }
            IReadOnlyList<Position> path = FindPath(unit, goal);
            if (path.Count == 0)
            {
                return new GotoAdvance(GotoOutcome.NoPath, steps); // walled off this turn — keep the goto, retry next turn
            }
            if (!CheckMove(unit, path[0]).Allowed)
            {
                return new GotoAdvance(GotoOutcome.OutOfMoves, steps); // can't afford the next step this turn (keep goto)
            }
            MoveUnit(unit, path[0]); // clears Destination (manual-move semantics) — restored below if still travelling
            steps++;
            if (!unit.IsOnMap)
            {
                return new GotoAdvance(GotoOutcome.NotGoing, steps); // consumed/transported mid-step (e.g. a rumour) — goto stays cleared
            }
            unit.Destination = goal;
        }

        if (unit.Position == goal)
        {
            unit.Destination = null;
            return new GotoAdvance(GotoOutcome.Reached, steps);
        }
        return new GotoAdvance(GotoOutcome.OutOfMoves, steps);
    }

    /// <summary>
    /// Advances every one of <paramref name="player"/>'s on-map units that holds a standing goto (id order, for
    /// determinism). Called in the player's turn before the world's movement reset; a no-op for a player with no
    /// goto units, so it draws no randomness and leaves the RNG streams untouched (ADR-009).
    /// </summary>
    internal void ProcessGotos(Player player)
    {
        foreach (Unit unit in _units
            .Where(u => u.OwnerId == player.PlayerId && !u.IsNative && u.IsOnMap && u.IsGoingTo)
            .OrderBy(u => u.Id)
            .ToList()) // materialise: a step can mutate _units (fog/rumour)
        {
            AdvanceDestination(unit);
        }
    }

    /// <summary>
    /// The next of <paramref name="player"/>'s units that still needs orders this turn (lowest id first), or null
    /// when none remain — the cycling oracle the presentation layer drives (ADR-006; the input/selection is P7).
    /// Skips units with no moves left, those resting (fortifying/fortified/sentry), those building a tile
    /// improvement (busy), those on a goto (they auto-advance), and any not on the map (sailing / in Europe).
    /// </summary>
    public Unit? NextUnitToMove(Player player) =>
        _units
            .Where(u => u.OwnerId == player.PlayerId && !u.IsNative && u.IsOnMap
                && u.MovementLeft > 0
                && u.Orders is not (UnitOrders.Fortifying or UnitOrders.Fortified or UnitOrders.Sentry)
                && !u.IsImproving
                && !u.IsGoingTo)
            .OrderBy(u => u.Id)
            .FirstOrDefault();

    /// <summary>True while <paramref name="player"/> has at least one unit still awaiting orders (see <see cref="NextUnitToMove"/>).</summary>
    public bool HasUnitsToMove(Player player) => NextUnitToMove(player) is not null;
}

/// <summary>Why a goto advance stopped (see <see cref="Game.AdvanceDestination"/>).</summary>
public enum GotoOutcome
{
    /// <summary>The unit had no destination (or was not on the map).</summary>
    NotGoing = 0,

    /// <summary>The unit reached its destination; the goto was cleared.</summary>
    Reached,

    /// <summary>The unit ran out of movement; the goto is kept and resumes next turn.</summary>
    OutOfMoves,

    /// <summary>No route to the destination this turn (terrain/fog/blockers); the goto is kept.</summary>
    NoPath,

    /// <summary>A step landed on (or the unit started on) an unresolved Lost City Rumour; it stops there, goto kept.</summary>
    Interrupted,
}

/// <summary>The result of advancing a unit along its goto for one turn.</summary>
/// <param name="Outcome">Why the advance stopped.</param>
/// <param name="StepsTaken">How many tiles the unit moved this advance.</param>
public readonly record struct GotoAdvance(GotoOutcome Outcome, int StepsTaken);
