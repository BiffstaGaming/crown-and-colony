using Godot;

namespace CrownAndColony.Presentation;

/// <summary>Placeholder colony graphic: a walled square with a name plate.</summary>
public partial class ColonyMarker : Node2D
{
    private string _colonyName = "";

    /// <summary>Name shown under the marker.</summary>
    public string ColonyName
    {
        get => _colonyName;
        set
        {
            _colonyName = value;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        const float half = MapView.TileSize * 0.38f;
        var body = new Rect2(-half, -half, half * 2, half * 2);
        DrawRect(body, new Color(0.85f, 0.80f, 0.65f));
        DrawRect(body, Colors.Black, filled: false, width: 2f);
        // Simple roof line to read as a settlement.
        DrawLine(new Vector2(-half, -half), new Vector2(0, -half * 1.6f), Colors.Black, 2f);
        DrawLine(new Vector2(0, -half * 1.6f), new Vector2(half, -half), Colors.Black, 2f);

        Font font = ThemeDB.FallbackFont;
        const int fontSize = 11;
        Vector2 size = font.GetStringSize(_colonyName, HorizontalAlignment.Left, -1, fontSize);
        DrawString(font, new Vector2(-size.X / 2, half + fontSize + 2), _colonyName,
            HorizontalAlignment.Left, -1, fontSize, Colors.White);
    }
}
