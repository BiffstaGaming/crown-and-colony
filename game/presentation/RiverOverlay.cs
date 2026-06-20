using System.Collections.Generic;
using CrownAndColony.GameLogic.World;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// Draws the map's rivers as connected blue courses over the terrain (FreeCol <c>Tile.hasRiver</c> — the river
/// improvement layer). No imported art: each river tile draws a short spoke from its diamond centre toward every
/// adjacent river tile's centre, so neighbouring river tiles link into a continuous course; an isolated river tile
/// draws a small pool so a lone source still reads as water. Connectivity is derived at render time from the
/// improvement layer (ADR-006 — pure presentation). Fog-gated like the terrain: a river is drawn only on explored
/// tiles, dimmed on explored-but-not-currently-visible ("remembered") tiles, mirroring <see cref="MapView"/>'s
/// terrain tinting.
/// </summary>
/// <remarks>
/// A single full-network drawable (like <see cref="MapView"/> itself), not a per-tile marker: connectivity needs
/// each tile's neighbours, and one <c>_Draw</c> over the whole layer keeps the spoke logic in one place.
/// <para>
/// Per-tile style (small vs. large river) is derived <b>at render time</b> from the river's connectivity, never from
/// a stored field (ADR-006, no save bump): each explored river tile belongs to a connected component (flood-filled
/// over adjacent explored river tiles), and a tile in a component of at least <see cref="LargeRiverTiles"/> tiles
/// draws as a large (thick) river — a long, well-connected course reads as a major river, a short tributary as a
/// minor one. A river type stamped at magnitude 2 by the ruleset/generator still forces the large style, so an
/// explicitly-large river is honoured; with today's generator (all rivers magnitude 1) the style is purely the
/// derived component length.
/// </para>
/// </remarks>
public partial class RiverOverlay : Node2D
{
    /// <summary>River water colour (a muted blue that reads over every terrain base).</summary>
    private static readonly Color Water = new(0.20f, 0.42f, 0.72f);

    /// <summary>A darker rim drawn under the course so it stays legible over blue ocean / light desert alike.</summary>
    private static readonly Color Rim = new(0.10f, 0.24f, 0.46f);

    /// <summary>Course half-width for a small (magnitude 1) river, in pixels.</summary>
    private const float SmallWidth = 4f;

    /// <summary>Course half-width for a large (magnitude 2) river, in pixels.</summary>
    private const float LargeWidth = 7f;

    /// <summary>Radius of the pool drawn for an isolated river tile (a lone source with no river neighbour).</summary>
    private const float PoolRadius = 6f;

    /// <summary>A connected river component of at least this many explored tiles draws as a large (major) river.</summary>
    private const int LargeRiverTiles = 4;

    private GameMap? _map;
    private IReadOnlySet<Position>? _explored;
    private IReadOnlySet<Position>? _visible;

    /// <summary>The explored river tiles whose connected component is large enough to draw as a major river (rebuilt each draw).</summary>
    private readonly HashSet<Position> _largeRivers = [];

    /// <summary>
    /// Assigns the map and the fog sets to draw, then triggers a redraw. Mirrors <see cref="MapView.ShowState"/> so
    /// the river layer fogs identically to the terrain it overlays.
    /// </summary>
    public void ShowState(GameMap map, IReadOnlySet<Position> explored, IReadOnlySet<Position> visible)
    {
        _map = map;
        _explored = explored;
        _visible = visible;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_map is null)
        {
            return;
        }

        // Derive per-tile style (small vs. large) from connectivity, then draw. Two passes over the same courses: a
        // wider darker rim first, then the water fill on top, so every rim sits fully behind every course (no rim
        // showing through a junction where two tiles' spokes overlap).
        ComputeLargeRivers();
        DrawCourses(rim: true);
        DrawCourses(rim: false);
    }

    /// <summary>
    /// Recomputes <see cref="_largeRivers"/>: flood-fills each connected component of explored river tiles (over the
    /// 8-neighbourhood, the same adjacency the courses connect along) and marks every tile of a component with at
    /// least <see cref="LargeRiverTiles"/> tiles. Pure read of the improvement layer + fog; no stored state.
    /// </summary>
    private void ComputeLargeRivers()
    {
        _largeRivers.Clear();
        var seen = new HashSet<Position>();
        var component = new List<Position>();
        var stack = new Stack<Position>();

        foreach (Position start in _map!.AllPositions())
        {
            if (!IsDrawnRiver(start) || !seen.Add(start))
            {
                continue;
            }

            component.Clear();
            stack.Push(start);
            while (stack.Count > 0)
            {
                Position p = stack.Pop();
                component.Add(p);
                foreach (Position n in p.Neighbours())
                {
                    if (IsDrawnRiver(n) && seen.Add(n))
                    {
                        stack.Push(n);
                    }
                }
            }

            if (component.Count >= LargeRiverTiles)
            {
                foreach (Position p in component)
                {
                    _largeRivers.Add(p);
                }
            }
        }
    }

    /// <summary>
    /// Draws every drawn river tile's spokes: a spoke from the tile centre to the midpoint toward each adjacent drawn
    /// river tile (the neighbour draws the matching half, so they meet on the shared edge into a continuous course),
    /// plus a pool for a lone river tile. <paramref name="rim"/> selects the wider dark underlay vs. the water fill.
    /// </summary>
    private void DrawCourses(bool rim)
    {
        foreach (Position p in _map!.AllPositions())
        {
            // Fog-gated (skip unexplored) via IsDrawnRiver.
            if (!IsDrawnRiver(p))
            {
                continue;
            }
            bool remembered = _visible is not null && !_visible.Contains(p);

            float width = LineWidth(p);
            float thickness = rim ? width * 2f + 2f : width * 2f;
            Color colour = (rim, remembered) switch
            {
                (true, true) => Dim(Rim),
                (true, false) => Rim,
                (false, true) => Dim(Water),
                (false, false) => Water,
            };

            Vector2 centre = MapView.TileCentre(p);
            int spokes = 0;
            foreach (Position n in p.Neighbours())
            {
                // Connect only to river neighbours that are themselves drawn (explored), so a course never
                // reaches out to an unexplored tile.
                if (!IsDrawnRiver(n))
                {
                    continue;
                }
                spokes++;
                Vector2 mid = centre.Lerp(MapView.TileCentre(n), 0.5f);
                DrawLine(centre, mid, colour, thickness);
            }
            // A lone river tile (no drawn river neighbour) reads as a small pool rather than a stray dot.
            if (spokes == 0)
            {
                DrawCircle(centre, rim ? PoolRadius + 1f : PoolRadius, colour);
            }
        }
    }

    /// <summary>True when a tile carries a river and is explored (so the overlay draws it).</summary>
    private bool IsDrawnRiver(Position p) =>
        _map!.InBounds(p) && _map.HasRiver(p) && (_explored is null || _explored.Contains(p));

    /// <summary>
    /// Course half-width for a river tile: large (thick) when the ruleset/generator stamped it magnitude 2 <i>or</i>
    /// its connected component is long enough (<see cref="_largeRivers"/>), else small. Render-time only — no stored
    /// drawn-style field (ADR-006).
    /// </summary>
    private float LineWidth(Position p) =>
        (_map!.RiverAt(p)?.Magnitude ?? 1) >= 2 || _largeRivers.Contains(p) ? LargeWidth : SmallWidth;

    /// <summary>Darkens a colour to the remembered-fog tint (matches <see cref="MapView"/>'s DimTint factor).</summary>
    private static Color Dim(Color c) => new(c.R * 0.5f, c.G * 0.5f, c.B * 0.58f);
}
