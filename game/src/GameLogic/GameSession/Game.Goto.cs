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
    /// <summary>
    /// The least-cost route a unit would take to <paramref name="goal"/> (the tiles to enter; empty = no route).
    /// A land unit's edge cost folds in the river/road "follow it" bonus (so a goto prefers a road/river corridor,
    /// matching <see cref="CheckMove"/>); a ship pays plain terrain cost (rivers/roads are land features).
    /// </summary>
    internal IReadOnlyList<Position> FindPath(Unit unit, Position goal) =>
        Pathfinder.FindPath(
            Map, unit.Position, goal, p => CanPathEnter(unit, p), applyImprovementBonus: !unit.Type.IsNaval);

    /// <summary>
    /// The kind of standing destination a <paramref name="goal"/> tile represents <i>for this unit</i> — derived
    /// purely from what occupies the tile, so no extra state is persisted (the save still stores only the
    /// destination's X/Y). FreeCol's <c>setDestination</c> takes a <c>Location</c> that can be a tile, a
    /// <c>Settlement</c>, the high seas, or <c>Europe</c>; we recover that kind from the tile:
    /// <list type="bullet">
    /// <item><see cref="DestinationKind.Europe"/> — a naval unit aimed at a <b>high-seas</b> tile: the ship routes
    /// to that tile and then sails to Europe (the existing <see cref="SailToEurope"/>).</item>
    /// <item><see cref="DestinationKind.Settlement"/> — a settlement the unit <b>cannot step onto</b> (a native
    /// settlement, or a colony it does not own): it routes to a tile <i>adjacent</i> to the settlement and stops
    /// there (FreeCol "arrive adjacent"). An <b>own</b> colony is enterable, so it is a plain <see cref="DestinationKind.Tile"/>.</item>
    /// <item><see cref="DestinationKind.Tile"/> — a tile the unit can step onto: open land for a land unit, or
    /// <b>open water</b> for a ship (naval/open-sea goto), or an own colony.</item>
    /// </list>
    /// </summary>
    internal DestinationKind DestinationKindOf(Unit unit, Position goal)
    {
        if (unit.Type.IsNaval && Map.TerrainAt(goal).Id == HighSeasId)
        {
            return DestinationKind.Europe; // a ship aimed at the high seas → sail to Europe on arrival
        }
        bool blockingSettlement =
            (!unit.IsNative && NativeSettlementAt(goal) is not null)
            || (ColonyAt(goal) is { } colony && (unit.IsNative || colony.OwnerId != unit.OwnerId));
        return blockingSettlement ? DestinationKind.Settlement : DestinationKind.Tile;
    }

    /// <summary>
    /// The route this turn for <paramref name="unit"/> heading to <paramref name="goal"/>, honouring the
    /// destination kind: a settlement the unit can't enter is routed <i>adjacent to</i> (<see cref="Pathfinder.FindPathAdjacent"/>),
    /// every other destination (a plain tile, open sea, or the Europe high-seas tile) is routed <i>onto</i>
    /// (<see cref="FindPath"/>). Empty = no route / already arrived.
    /// </summary>
    private IReadOnlyList<Position> RouteFor(Unit unit, Position goal) =>
        DestinationKindOf(unit, goal) == DestinationKind.Settlement
            ? Pathfinder.FindPathAdjacent(
                Map, unit.Position, goal, p => CanPathEnter(unit, p), applyImprovementBonus: !unit.Type.IsNaval)
            : FindPath(unit, goal);

    /// <summary>Whether <paramref name="unit"/> has reached <paramref name="goal"/> for its destination kind: standing on a tile/sea/high-seas goal, or <i>beside</i> a settlement goal it cannot enter.</summary>
    private bool HasArrivedAt(Unit unit, Position goal) =>
        DestinationKindOf(unit, goal) == DestinationKind.Settlement
            ? unit.Position.IsAdjacentTo(goal)
            : unit.Position == goal;

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

    /// <summary>
    /// Whether <paramref name="unit"/> may be given a goto order to <paramref name="goal"/>, and why not if not.
    /// Accepts the three faithful destination kinds (FreeCol <c>setDestination</c>): a plain <b>tile</b> (open land for
    /// a land unit, or <b>open water</b> for a ship), a <b>settlement</b> the unit arrives <i>beside</i> (a native
    /// settlement or a colony it can't enter), and <b>Europe</b> (a naval unit aimed at a high-seas tile, which it
    /// then sails from). For a plain tile the goal's terrain must match the unit's element (a land unit can't target
    /// water; a ship can't target open land); a settlement target skips that gate (the unit arrives on an adjacent
    /// tile of its own element). The target must be explored, and a route to it (or beside it) must exist.
    /// </summary>
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

        DestinationKind kind = DestinationKindOf(unit, goal);
        // Terrain legality: a land/open-sea/Europe target must match the unit's element (land for land, water for a
        // ship). A SETTLEMENT target is the one tile-terrain exception — the unit arrives on an ADJACENT tile of its
        // own element, so the settlement's own terrain need not match (a land unit may target a coastal settlement).
        if (kind != DestinationKind.Settlement && Map.TerrainAt(goal).IsWater != unit.Type.IsNaval)
        {
            return MoveCheck.No(unit.Type.IsNaval ? "A ship can only sail to water." : "A land unit can only march to land.");
        }
        if (PlayerById(unit.OwnerId) is not { } owner || !owner.Explored.Contains(goal))
        {
            return MoveCheck.No("You cannot set a course into unexplored territory.");
        }
        if (RouteFor(unit, goal).Count == 0 && !HasArrivedAt(unit, goal))
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
    /// kept so it resumes next turn). Honours the destination kind: a <b>settlement</b> the unit can't enter is
    /// reached by arriving on an <i>adjacent</i> tile; a <b>Europe</b> destination (a ship aimed at the high seas) is
    /// reached by stepping onto the high-seas tile, at which point the unit <see cref="SailToEurope"/> and the goto
    /// clears (<see cref="GotoOutcome.SailedToEurope"/>). RNG-free except for any event a step itself triggers
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
            if (HasArrivedAt(unit, goal))
            {
                return Arrive(unit, goal, steps);
            }
            if (Map.HasRumour(unit.Position))
            {
                // The unit is on an unresolved Lost City Rumour — a strange-mounds prompt left on the tile by the
                // step that landed here (or a pending one it started on). Stop so the player decides with the unit
                // standing on the rumour, as in FreeCol (exploring a rumour ends the move). The goto is kept and
                // resumes once the rumour is cleared.
                return new GotoAdvance(GotoOutcome.Interrupted, steps);
            }
            IReadOnlyList<Position> path = RouteFor(unit, goal);
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

        if (HasArrivedAt(unit, goal))
        {
            return Arrive(unit, goal, steps);
        }
        return new GotoAdvance(GotoOutcome.OutOfMoves, steps);
    }

    /// <summary>
    /// Completes a goto: clears the destination and, for a <b>Europe</b> destination (a ship now standing on the
    /// high-seas tile), hands off to the existing <see cref="SailToEurope"/> command and reports
    /// <see cref="GotoOutcome.SailedToEurope"/>; otherwise reports <see cref="GotoOutcome.Reached"/>. The sailing
    /// itself is not reimplemented here — this only invokes the shipped command at the right moment.
    /// </summary>
    private GotoAdvance Arrive(Unit unit, Position goal, int steps)
    {
        unit.Destination = null;
        if (DestinationKindOf(unit, goal) == DestinationKind.Europe && CheckSailToEurope(unit).Allowed)
        {
            SailToEurope(unit); // the ship reached its high-seas entry tile → cross to Europe (existing command)
            return new GotoAdvance(GotoOutcome.SailedToEurope, steps);
        }
        return new GotoAdvance(GotoOutcome.Reached, steps);
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

    /// <summary>The unit reached its destination (on a tile/open-sea goal, or beside a settlement goal); the goto was cleared.</summary>
    Reached,

    /// <summary>A naval unit reached its Europe destination's high-seas tile and set sail for Europe (via <see cref="Game.SailToEurope"/>); the goto was cleared.</summary>
    SailedToEurope,

    /// <summary>The unit ran out of movement; the goto is kept and resumes next turn.</summary>
    OutOfMoves,

    /// <summary>No route to the destination this turn (terrain/fog/blockers); the goto is kept.</summary>
    NoPath,

    /// <summary>A step landed on (or the unit started on) an unresolved Lost City Rumour; it stops there, goto kept.</summary>
    Interrupted,
}

/// <summary>
/// The kind of standing goto destination a tile represents for a unit, derived at runtime from what occupies the
/// tile (no extra state is persisted). Mirrors the destination types of FreeCol's <c>Unit.setDestination(Location)</c>.
/// </summary>
public enum DestinationKind
{
    /// <summary>A tile the unit steps onto: open land for a land unit, <b>open water</b> for a ship (naval/open-sea goto), or an own colony.</summary>
    Tile = 0,

    /// <summary>A settlement the unit cannot enter (a native settlement, or a colony it does not own) — it arrives on an <i>adjacent</i> tile.</summary>
    Settlement,

    /// <summary>A naval unit's high-seas tile — the ship routes to it and then sails to Europe (<see cref="Game.SailToEurope"/>).</summary>
    Europe,
}

/// <summary>The result of advancing a unit along its goto for one turn.</summary>
/// <param name="Outcome">Why the advance stopped.</param>
/// <param name="StepsTaken">How many tiles the unit moved this advance.</param>
public readonly record struct GotoAdvance(GotoOutcome Outcome, int StepsTaken);
