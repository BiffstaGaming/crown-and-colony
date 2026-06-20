using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// A small drawn glyph marking an explored Lost City Rumour tile (FreeCol's rumour icon) — an earthy mound with a
/// glint above it, hinting "something to investigate here". A drawn glyph (no imported art) keeps it purely
/// presentation (ADR-006) with no asset-register churn; <see cref="GameController"/> places one per explored
/// rumour tile (fog-gated) into the map's rumour layer. It draws no game state and consumes no input.
/// </summary>
public partial class RumourMarker : Node2D
{
    private static readonly Color Mound = new(0.82f, 0.72f, 0.45f); // earthy tan
    private static readonly Color Edge = new(0.30f, 0.22f, 0.10f);  // dark outline
    private static readonly Color Glint = new(0.96f, 0.86f, 0.20f); // gold "point of interest" spark

    private const float Width = 30f;
    private const float Height = 15f;

    public override void _Draw()
    {
        // A dome (half-ellipse) sitting on the tile centre: arc points from left base, over the top, to right base.
        const int segments = 14;
        var dome = new Vector2[segments + 1];
        for (int i = 0; i <= segments; i++)
        {
            float t = Mathf.Pi * i / segments; // 0..pi
            dome[i] = new Vector2(-Mathf.Cos(t) * (Width / 2f), -Mathf.Sin(t) * Height);
        }
        DrawColoredPolygon(dome, Mound);             // filled mound (auto-closed along the base)
        DrawPolyline(dome, Edge, 2f);                // outline the curved top
        DrawLine(dome[0], dome[segments], Edge, 2f); // and the base

        DrawCircle(new Vector2(0, -Height - 7f), 3.5f, Glint); // the glint above the mound
    }
}
