using CrownAndColony.GameLogic.World.Improvements;

namespace CrownAndColony.GameLogic.World;

/// <summary>
/// Deterministic, RNG-free A* over the tile grid (FreeCol <c>Map.searchMap</c>): finds the least-cost route from
/// one tile to another, entering each tile at its terrain move cost, with diagonals free (as in the original game).
///
/// <para>For land units the per-edge cost folds in the river/road "follow it" bonus (see
/// <see cref="ImprovementMovement.MoveCost"/>): stepping between two tiles that both carry a movement-granting
/// improvement (a river or a pioneer-built road) costs the reduced enter-cost (1), so a goto route prefers a
/// road/river corridor. This matches <c>Game.CheckMove</c>'s per-step charge exactly, so the summed path cost the
/// A* minimises is the movement a unit actually spends. Ships get no such bonus (rivers/roads are land features),
/// so they path on plain terrain cost.</para>
///
/// <para>The open set is ordered by a total key <c>(f, g, Y, X)</c> — f = g + heuristic, then tie-broken by lower
/// path cost, then lower row, then lower column — so the expansion order, and hence the returned path, is byte-stable
/// regardless of hash-set iteration order. No randomness is drawn (ADR-009). The caller supplies a
/// <c>passable</c> predicate (terrain, fog, enemy/settlement/colony blocking) so this stays a pure
/// graph search with no game-rules knowledge.</para>
/// </summary>
public static class Pathfinder
{
    /// <summary>
    /// The cheapest possible enter cost of any step the A* can take. Plain terrain (plains/grassland etc.) costs 3,
    /// but a land unit following a road or river pays the reduced enter-cost (1) — see
    /// <see cref="ImprovementMovement.MoveCost"/> / the classic river/road <c>MovementCost</c> of 1. The heuristic
    /// must never overestimate the true remaining cost (A* admissibility), so it multiplies the remaining Chebyshev
    /// distance by this <i>minimum</i> possible per-step cost. Were it left at 3 (the old plain-terrain minimum) the
    /// heuristic could exceed the true cost of an all-road route and A* could return a suboptimal path. Using 1
    /// keeps the heuristic admissible (and consistent) for both improvement-aware land paths and ship paths.
    /// </summary>
    private const int MinEnterCost = 1;

    /// <summary>
    /// The ordered tiles to ENTER to walk from <paramref name="start"/> to <paramref name="goal"/> (excludes the start
    /// tile; the last element is the goal), or an empty list when there is no route. <paramref name="passable"/> decides
    /// which tiles may be entered (the goal included).
    /// </summary>
    /// <param name="map">The world grid (terrain + the sparse improvement layer the road/river bonus reads).</param>
    /// <param name="start">The tile the unit stands on (not included in the returned route).</param>
    /// <param name="goal">The destination tile.</param>
    /// <param name="passable">Which tiles may be entered (terrain/fog/blocker rules from the caller).</param>
    /// <param name="applyImprovementBonus">
    /// True for a land unit — fold the river/road "follow it" bonus into each edge cost (a route along a
    /// road/river is cheaper and is preferred), mirroring <c>Game.CheckMove</c>. False for a ship — rivers and
    /// roads are land features, so it pays plain terrain cost. Defaults to false (terrain-only) so existing
    /// terrain-only callers and tests are unchanged.
    /// </param>
    public static IReadOnlyList<Position> FindPath(
        GameMap map, Position start, Position goal, Func<Position, bool> passable, bool applyImprovementBonus = false)
    {
        if (start == goal || !map.InBounds(goal) || !passable(goal))
        {
            return [];
        }

        var gScore = new Dictionary<Position, int> { [start] = 0 };
        var cameFrom = new Dictionary<Position, Position>();
        var closed = new HashSet<Position>();
        var open = new PriorityQueue<Position, (int F, int G, int Y, int X)>();
        open.Enqueue(start, (Heuristic(start, goal), 0, start.Y, start.X));

        while (open.TryDequeue(out Position current, out _))
        {
            if (current == goal)
            {
                return Reconstruct(cameFrom, goal);
            }
            if (!closed.Add(current))
            {
                continue; // a stale duplicate queued before this node was expanded
            }

            int currentG = gScore[current];
            foreach (Position n in current.Neighbours())
            {
                if (!map.InBounds(n) || closed.Contains(n) || !passable(n))
                {
                    continue;
                }
                int tentativeG = currentG + EnterCost(map, current, n, applyImprovementBonus);
                if (!gScore.TryGetValue(n, out int known) || tentativeG < known)
                {
                    gScore[n] = tentativeG;
                    cameFrom[n] = current;
                    open.Enqueue(n, (tentativeG + Heuristic(n, goal), tentativeG, n.Y, n.X));
                }
            }
        }
        return []; // no route
    }

    /// <summary>
    /// The cost to step from <paramref name="from"/> into the adjacent tile <paramref name="to"/>, matching
    /// <c>Game.CheckMove</c>: the terrain's enter cost, reduced by the river/road "follow it" bonus when
    /// <paramref name="applyImprovementBonus"/> (a land unit) and both tiles carry a movement-granting improvement.
    /// This keeps the A*'s summed path cost identical to the movement a unit actually spends, so the route the
    /// pathfinder minimises is the route the unit takes. (The partial-move rounding in CheckMove only ever
    /// <i>reduces</i> a single charged step to the unit's remaining movement at a turn boundary; it never changes
    /// which route is cheapest, so the planner uses the un-clamped per-edge cost.)
    /// </summary>
    private static int EnterCost(GameMap map, Position from, Position to, bool applyImprovementBonus)
    {
        int baseCost = map.TerrainAt(to).MoveCost;
        return applyImprovementBonus
            ? ImprovementMovement.MoveCost(map.ImprovementsAt(from), map.ImprovementsAt(to), baseCost)
            : baseCost;
    }

    /// <summary>
    /// Chebyshev distance × the cheapest possible enter cost (<see cref="MinEnterCost"/>) — admissible and
    /// consistent (diagonals are free, and no single step can cost less than <see cref="MinEnterCost"/>, so this
    /// never overestimates the true remaining cost).
    /// </summary>
    private static int Heuristic(Position p, Position goal) =>
        Math.Max(Math.Abs(p.X - goal.X), Math.Abs(p.Y - goal.Y)) * MinEnterCost;

    private static IReadOnlyList<Position> Reconstruct(IReadOnlyDictionary<Position, Position> cameFrom, Position goal)
    {
        var path = new List<Position> { goal };
        Position cur = goal;
        while (cameFrom.TryGetValue(cur, out Position prev))
        {
            path.Add(prev);
            cur = prev;
        }
        path.Reverse();
        path.RemoveAt(0); // drop the start tile — Steps are the tiles to ENTER
        return path;
    }
}
