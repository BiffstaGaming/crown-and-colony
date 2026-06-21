using CrownAndColony.GameLogic.Specification;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// A unit on the map: FreeCol sprite when one exists for the unit type,
/// red-disc fallback otherwise; gold ground-ellipse when selected.
/// </summary>
public partial class UnitMarker : Node2D
{
    private bool _selected;
    private Color _ownerColor; // default (0,0,0,0) = transparent → no ring (the human's own units)
    private Texture2D? _texture;

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

    /// <summary>
    /// Ground-ring colour identifying the unit's owner — used to mark a unit as "not yours" (a foreign power's
    /// nation colour, or a native constant). Left transparent (alpha 0) for the human's own units, which then
    /// render exactly as before. Presentation-only.
    /// </summary>
    public Color OwnerColor
    {
        get => _ownerColor;
        set
        {
            _ownerColor = value;
            QueueRedraw();
        }
    }

    /// <summary>
    /// Picks the sprite for a unit by its ruleset type + role short names (FreeCol resolves a unit's image by type
    /// <i>and</i> role — a colonist in the soldier role looks different from a plain one; a native brave in the
    /// mounted role draws mounted). The priority order lives in the engine-free
    /// <see cref="UnitSpriteCatalog.CandidatePaths"/> (role-specific → generic-role → bare-type) so it can be unit-tested;
    /// this loads the first candidate that exists and falls back to the red disc when no art does.
    /// </summary>
    /// <param name="typeShortName">The unit type's short name (e.g. <c>freeColonist</c>, <c>caravel</c>, <c>brave</c>).</param>
    /// <param name="roleShortName">The unit's role short name (e.g. <c>soldier</c>, <c>mountedBrave</c>, or <c>default</c> for unarmed).</param>
    public void SetUnit(string typeShortName, string roleShortName)
    {
        _texture = null;
        foreach (string path in UnitSpriteCatalog.CandidatePaths(typeShortName, roleShortName))
        {
            if (ResourceLoader.Exists(path))
            {
                _texture = GD.Load<Texture2D>(path);
                break;
            }
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        // Owner ring: a coloured ground ellipse marking a non-yours unit (foreign power / native), drawn just
        // inside the selection ring so both read at once. Skipped (transparent) for the human's own units.
        if (_ownerColor.A > 0f)
        {
            DrawSetTransform(Vector2.Zero, 0f, new Vector2(1f, 0.5f));
            DrawArc(Vector2.Zero, MapView.TileH * 0.48f, 0, Mathf.Tau, 40, _ownerColor, 4f);
            DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        }

        // Selection: gold ellipse on the ground plane (isometric circle).
        if (_selected)
        {
            DrawSetTransform(Vector2.Zero, 0f, new Vector2(1f, 0.5f));
            DrawArc(Vector2.Zero, MapView.TileH * 0.55f, 0, Mathf.Tau, 40, new Color(1f, 0.85f, 0.2f), 3f);
            DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        }

        if (_texture is not null)
        {
            // Feet on the tile centre.
            Vector2 size = _texture.GetSize();
            DrawTexture(_texture, new Vector2(-size.X / 2f, -size.Y + 6f));
        }
        else
        {
            const float radius = MapView.TileH * 0.30f;
            DrawCircle(Vector2.Zero, radius, new Color(0.75f, 0.15f, 0.15f));
            DrawArc(Vector2.Zero, radius, 0, Mathf.Tau, 32, Colors.Black, 2f);
        }
    }
}
