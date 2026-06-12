using Godot;

namespace CrownAndColony.Presentation;

/// <summary>Placeholder unit graphic: a disc with an outline; gold ring when selected.</summary>
public partial class UnitMarker : Node2D
{
    private bool _selected;

    /// <summary>Whether the selection ring is shown.</summary>
    public bool Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        const float radius = MapView.TileSize * 0.35f;
        DrawCircle(Vector2.Zero, radius, new Color(0.75f, 0.15f, 0.15f));
        DrawArc(Vector2.Zero, radius, 0, Mathf.Tau, 32, Colors.Black, 2f);
        if (_selected)
        {
            DrawArc(Vector2.Zero, radius + 4f, 0, Mathf.Tau, 32, new Color(1f, 0.85f, 0.2f), 3f);
        }
    }
}
