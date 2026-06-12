using Godot;

namespace CrownAndColony.Presentation;

/// <summary>A colony on the map: FreeCol settlement art with a name plate.</summary>
public partial class ColonyMarker : Node2D
{
    private static readonly Texture2D Settlement =
        GD.Load<Texture2D>("res://assets/freecol/settlements/small.png");

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
        Vector2 size = Settlement.GetSize();
        DrawTexture(Settlement, new Vector2(-size.X / 2f, -size.Y + MapView.TileH * 0.3f));

        Font font = ThemeDB.FallbackFont;
        const int fontSize = 12;
        Vector2 text = font.GetStringSize(_colonyName, HorizontalAlignment.Left, -1, fontSize);
        Vector2 origin = new(-text.X / 2f, MapView.TileH * 0.3f + fontSize + 2);
        // Soft shadow for readability on any terrain.
        DrawString(font, origin + Vector2.One, _colonyName, HorizontalAlignment.Left, -1, fontSize, Colors.Black);
        DrawString(font, origin, _colonyName, HorizontalAlignment.Left, -1, fontSize, Colors.White);
    }
}
