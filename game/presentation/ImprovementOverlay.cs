using System.Collections.Generic;
using CrownAndColony.GameLogic.World;
using CrownAndColony.GameLogic.World.Improvements;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// Draws the map's <b>pioneer-built</b> tile improvements — roads and plowed fields — over the terrain (and over
/// the river course, below settlements/units/markers). The sibling of <see cref="RiverOverlay"/> for the man-made
/// improvement layer (FreeCol road/plow tile graphics): no imported art, just drawn geometry over
/// <see cref="GameMap"/>'s improvement oracles (ADR-006 — pure presentation, no rules here).
/// </summary>
/// <remarks>
/// <para>
/// <b>Roads</b> draw like rivers do: each road tile draws a short spoke from its diamond centre toward every
/// adjacent road tile's centre, so neighbouring road tiles link into a continuous track; an isolated road tile
/// draws a small hub disc so a lone segment still reads. Roads connect <i>only</i> to roads (not to rivers) in the
/// drawing — the movement-bonus interconnect between roads and rivers is a rules concern (<c>ImprovementMovement</c>),
/// not a visual one; a track that visibly merged into a river would mislead. Connectivity is derived at render time
/// from the improvement layer.
/// </para>
/// <para>
/// <b>Plowed fields</b> draw as a few parallel furrows (short diagonal lines) inset within the tile diamond — a
/// subtle hatch that reads as ploughed ground without imported art. A plow lays flat on the tile (no connectivity).
/// </para>
/// <para>
/// Fog-gated exactly like the terrain and <see cref="RiverOverlay"/>: an improvement is drawn only on explored
/// tiles, dimmed on explored-but-not-currently-visible ("remembered") tiles, mirroring <see cref="MapView"/>'s
/// terrain tinting. A fresh game has no pioneer-built improvements, so this overlay draws nothing on an untouched
/// map — leaving the map/menu goldens byte-identical (the same reason <see cref="RiverOverlay"/> left them
/// unchanged where no river was in view).
/// </para>
/// </remarks>
public partial class ImprovementOverlay : Node2D
{
    /// <summary>Road surface colour (a warm earthen brown that reads over every terrain base).</summary>
    private static readonly Color Road = new(0.55f, 0.40f, 0.22f);

    /// <summary>A darker rim drawn under the road track so it stays legible over light desert / dark forest alike.</summary>
    private static readonly Color RoadRim = new(0.30f, 0.20f, 0.10f);

    /// <summary>Furrow colour for a plowed field (a deeper tilled-soil brown, distinct from the road's lighter track).</summary>
    private static readonly Color Furrow = new(0.42f, 0.28f, 0.16f);

    /// <summary>Road track half-width, in pixels (matches a small river's width so road and river read as the same calibre of line).</summary>
    private const float RoadWidth = 4f;

    /// <summary>Radius of the hub disc drawn for an isolated road tile (a lone segment with no road neighbour).</summary>
    private const float HubRadius = 5f;

    /// <summary>Number of furrows drawn across a plowed tile.</summary>
    private const int FurrowCount = 3;

    /// <summary>Furrow line width, in pixels.</summary>
    private const float FurrowWidth = 2f;

    /// <summary>How far across the diamond's half-extents the furrows span (a fraction inset from the tile edges).</summary>
    private const float FurrowExtent = 0.5f;

    private GameMap? _map;
    private IReadOnlySet<Position>? _explored;
    private IReadOnlySet<Position>? _visible;

    /// <summary>
    /// Assigns the map and the fog sets to draw, then triggers a redraw. Mirrors <see cref="RiverOverlay.ShowState"/>
    /// and <see cref="MapView.ShowState"/> so the improvement layer fogs identically to the terrain it overlays.
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

        // Plows first (flat fill), then roads on top — a road over a plowed-then-roaded tile reads as the track
        // crossing the field. Roads draw a wider darker rim before the surface so every rim sits behind every
        // track (no rim showing through a junction where two tiles' spokes overlap), exactly like RiverOverlay.
        DrawPlows();
        DrawRoads(rim: true);
        DrawRoads(rim: false);
    }

    /// <summary>
    /// Draws every drawn road tile's spokes: a spoke from the tile centre to the midpoint toward each adjacent drawn
    /// road tile (the neighbour draws the matching half, so they meet on the shared edge into a continuous track),
    /// plus a hub disc for a lone road tile. <paramref name="rim"/> selects the wider dark underlay vs. the surface.
    /// </summary>
    private void DrawRoads(bool rim)
    {
        foreach (Position p in _map!.AllPositions())
        {
            // Fog-gated (skip unexplored) via IsDrawn.
            if (!IsDrawn(p, TileImprovementType.RoadId))
            {
                continue;
            }
            bool remembered = _visible is not null && !_visible.Contains(p);

            float thickness = rim ? RoadWidth * 2f + 2f : RoadWidth * 2f;
            Color colour = (rim, remembered) switch
            {
                (true, true) => Dim(RoadRim),
                (true, false) => RoadRim,
                (false, true) => Dim(Road),
                (false, false) => Road,
            };

            Vector2 centre = MapView.TileCentre(p);
            int spokes = 0;
            foreach (Position n in p.Neighbours())
            {
                // Connect only to road neighbours that are themselves drawn (explored), so a track never reaches
                // out to an unexplored tile.
                if (!IsDrawn(n, TileImprovementType.RoadId))
                {
                    continue;
                }
                spokes++;
                Vector2 mid = centre.Lerp(MapView.TileCentre(n), 0.5f);
                DrawLine(centre, mid, colour, thickness);
            }
            // A lone road tile (no drawn road neighbour) reads as a small hub rather than a stray dot.
            if (spokes == 0)
            {
                DrawCircle(centre, rim ? HubRadius + 1f : HubRadius, colour);
            }
        }
    }

    /// <summary>
    /// Draws a few parallel furrows across each drawn plowed tile — short diagonal lines inset within the diamond,
    /// a subtle hatch that reads as tilled ground. A plow lays flat on the tile (no connectivity to neighbours).
    /// </summary>
    private void DrawPlows()
    {
        foreach (Position p in _map!.AllPositions())
        {
            if (!IsDrawn(p, TileImprovementType.PlowId))
            {
                continue;
            }
            bool remembered = _visible is not null && !_visible.Contains(p);
            Color colour = remembered ? Dim(Furrow) : Furrow;

            Vector2 centre = MapView.TileCentre(p);
            // Furrows run parallel to the diamond's lower-right edge (down-left → up-right within the tile), spread
            // along the perpendicular. Each furrow is a short segment scaled to the tile's half-extents so it stays
            // inside the diamond. Geometry is purely a function of the tile centre — deterministic, no RNG.
            float halfW = MapView.TileW / 2f * FurrowExtent;
            float halfH = MapView.TileH / 2f * FurrowExtent;
            for (int i = 0; i < FurrowCount; i++)
            {
                // Offset each furrow across the tile (centre furrow at 0, the others above/below it).
                float t = FurrowCount == 1 ? 0f : (i / (float)(FurrowCount - 1) - 0.5f);
                var offset = new Vector2(t * halfW, t * halfH);
                Vector2 from = centre + offset + new Vector2(-halfW * 0.5f, halfH * 0.5f);
                Vector2 to = centre + offset + new Vector2(halfW * 0.5f, -halfH * 0.5f);
                DrawLine(from, to, colour, FurrowWidth);
            }
        }
    }

    /// <summary>True when a tile carries the given improvement and is explored (so the overlay draws it).</summary>
    private bool IsDrawn(Position p, string improvementId) =>
        _map!.InBounds(p) && _map.HasImprovement(p, improvementId) && (_explored is null || _explored.Contains(p));

    /// <summary>Darkens a colour to the remembered-fog tint (matches <see cref="MapView"/>'s DimTint factor, as <see cref="RiverOverlay"/> does).</summary>
    private static Color Dim(Color c) => new(c.R * 0.5f, c.G * 0.5f, c.B * 0.58f);
}
