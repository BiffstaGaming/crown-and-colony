using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// A small overlay that outlines the selected unit's standing "go to" destination tile (FreeCol's goto marker).
/// Pure presentation: <see cref="GameController"/> positions it at the destination's diamond centre and toggles
/// its visibility based on the selected unit's <c>Destination</c>; it draws no game state itself.
/// </summary>
public partial class GotoMarker : Node2D
{
    private static readonly Color MarkerColor = new(0.96f, 0.86f, 0.20f); // goto gold

    public override void _Draw()
    {
        // A hollow diamond matching a map tile, drawn around this node's origin.
        Vector2[] points =
        [
            new(0, -MapView.TileH / 2f),
            new(MapView.TileW / 2f, 0),
            new(0, MapView.TileH / 2f),
            new(-MapView.TileW / 2f, 0),
        ];
        for (int i = 0; i < points.Length; i++)
        {
            DrawLine(points[i], points[(i + 1) % points.Length], MarkerColor, 3f);
        }
    }
}
