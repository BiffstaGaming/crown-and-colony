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
/// Per-tile style (small vs. large river) follows the river's stored <c>Magnitude</c> (1 = small, 2 = large draws
/// thicker). This is presentation-only; the stored improvement layer records magnitude but no drawn-style field
/// (ADR-006, no save bump).
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

    private GameMap? _map;
    private IReadOnlySet<Position>? _explored;
    private IReadOnlySet<Position>? _visible;

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

        // Two passes over the same courses: a wider darker rim first, then the water fill on top, so every rim sits
        // fully behind every course (no rim showing through a junction where two tiles' spokes overlap).
        DrawCourses(rim: true);
        DrawCourses(rim: false);
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

    /// <summary>Course half-width for a river tile: large (thick) when the river was stamped magnitude 2, else small.</summary>
    private float LineWidth(Position p) =>
        (_map!.ImprovementAt(p)?.Magnitude ?? 1) >= 2 ? LargeWidth : SmallWidth;

    /// <summary>Darkens a colour to the remembered-fog tint (matches <see cref="MapView"/>'s DimTint factor).</summary>
    private static Color Dim(Color c) => new(c.R * 0.5f, c.G * 0.5f, c.B * 0.58f);
}
