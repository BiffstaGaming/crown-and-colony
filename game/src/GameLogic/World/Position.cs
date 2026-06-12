namespace CrownAndColony.GameLogic.World;

/// <summary>A tile coordinate on the map grid. (0,0) is the top-left corner.</summary>
/// <param name="X">Column, increasing eastward.</param>
/// <param name="Y">Row, increasing southward.</param>
public readonly record struct Position(int X, int Y)
{
    /// <summary>
    /// True when <paramref name="other"/> is one of the up-to-8 surrounding tiles
    /// (diagonal movement is allowed, as in the original game).
    /// </summary>
    public bool IsAdjacentTo(Position other)
    {
        int dx = Math.Abs(X - other.X);
        int dy = Math.Abs(Y - other.Y);
        return dx <= 1 && dy <= 1 && (dx | dy) != 0;
    }

    /// <summary>The up-to-8 neighbouring positions (may include off-map coordinates; callers bounds-check).</summary>
    public IEnumerable<Position> Neighbours()
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if ((dx | dy) != 0)
                {
                    yield return new Position(X + dx, Y + dy);
                }
            }
        }
    }
}
