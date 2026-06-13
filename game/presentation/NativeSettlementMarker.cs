using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// A native settlement on the map: FreeCol indian-settlement art (camp / village /
/// Inca or Aztec city) with a nation plate (capitals are starred). Pure presentation
/// — the marker only draws what <see cref="GameController"/> tells it.
/// </summary>
public partial class NativeSettlementMarker : Node2D
{
    private static readonly Texture2D Camp =
        GD.Load<Texture2D>("res://assets/freecol/settlements/indian_camp.png");
    private static readonly Texture2D Village =
        GD.Load<Texture2D>("res://assets/freecol/settlements/indian_village.png");
    private static readonly Texture2D IncaCity =
        GD.Load<Texture2D>("res://assets/freecol/settlements/inca_city.png");
    private static readonly Texture2D AztecCity =
        GD.Load<Texture2D>("res://assets/freecol/settlements/aztec_city.png");

    private string _settlementTypeId = "";
    private string _caption = "";

    /// <summary>Settlement template id (e.g. <c>model.settlement.camp</c>); selects the art.</summary>
    public string SettlementTypeId
    {
        get => _settlementTypeId;
        set
        {
            _settlementTypeId = value;
            QueueRedraw();
        }
    }

    /// <summary>Caption drawn under the marker (the nation's name; capitals are starred).</summary>
    public string Caption
    {
        get => _caption;
        set
        {
            _caption = value;
            QueueRedraw();
        }
    }

    private Texture2D Art => _settlementTypeId switch
    {
        var s when s.Contains("inca") => IncaCity,
        var s when s.Contains("aztec") => AztecCity,
        var s when s.Contains("village") => Village,
        _ => Camp,
    };

    public override void _Draw()
    {
        Texture2D art = Art;
        Vector2 size = art.GetSize();
        // Bottom-aligned to the diamond, like the colony marker.
        DrawTexture(art, new Vector2(-size.X / 2f, -size.Y + MapView.TileH * 0.3f));

        Font font = ThemeDB.FallbackFont;
        const int fontSize = 12;
        Vector2 text = font.GetStringSize(_caption, HorizontalAlignment.Left, -1, fontSize);
        Vector2 origin = new(-text.X / 2f, MapView.TileH * 0.3f + fontSize + 2);
        // Soft shadow for readability on any terrain.
        DrawString(font, origin + Vector2.One, _caption, HorizontalAlignment.Left, -1, fontSize, Colors.Black);
        DrawString(font, origin, _caption, HorizontalAlignment.Left, -1, fontSize, Colors.White);
    }
}
