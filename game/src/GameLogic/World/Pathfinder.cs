namespace CrownAndColony.GameLogic.World;

/// <summary>
/// Deterministic, RNG-free A* over the tile grid (FreeCol <c>Map.searchMap</c>): finds the least-cost route from
/// one tile to another, entering each tile at its terrain move cost, with diagonals free (as in the original game).
///
/// <para>The open set is ordered by a total key <c>(f, g, Y, X)</c> — f = g + heuristic, then tie-broken by lower
/// path cost, then lower row, then lower column — so the expansion order, and hence the returned path, is byte-stable
/// regardless of hash-set iteration order. No randomness is drawn (ADR-009). The caller supplies a
/// <c>passable</c> predicate (terrain, fog, enemy/settlement/colony blocking) so this stays a pure
/// graph search with no game-rules knowledge.</para>
/// </summary>
public static class Pathfinder
{
    /// <summary>The cheapest terrain enter cost (plains/grassland etc. cost 3). The A* heuristic assumes no step is
    /// cheaper than this; revisit if roads/rivers (which could cost less) are added so the heuristic stays admissible.</summary>
    private const int MinEnterCost = 3;

    /// <summary>
    /// The ordered tiles to ENTER to walk from <paramref name="start"/> to <paramref name="goal"/> (excludes the start
    /// tile; the last element is the goal), or an empty list when there is no route. <paramref name="passable"/> decides
    /// which tiles may be entered (the goal included).
    /// </summary>
    public static IReadOnlyList<Position> FindPath(
        GameMap map, Position start, Position goal, Func<Position, bool> passable)
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
                int tentativeG = currentG + map.TerrainAt(n).MoveCost;
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

    /// <summary>Chebyshev distance × the cheapest enter cost — admissible and consistent (diagonals are free).</summary>
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
