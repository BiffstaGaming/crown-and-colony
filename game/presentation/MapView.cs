using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.World;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// Draws the game map as flat-coloured tiles (Phase 1 placeholder art; real
/// tile graphics arrive with the art pass). Pure presentation — reads the map,
/// never mutates it.
/// </summary>
public partial class MapView : Node2D
{
    /// <summary>Tile edge length in pixels; shared by all map-space/screen-space conversions.</summary>
    public const int TileSize = 32;

    private GameMap? _map;

    // Placeholder palette per terrain short name; unknown terrain renders magenta
    // so a missing entry is impossible to miss.
    private static readonly Godot.Collections.Dictionary<string, Color> Palette = new()
    {
        ["plains"] = new Color(0.85f, 0.78f, 0.55f),
        ["grassland"] = new Color(0.45f, 0.65f, 0.30f),
        ["prairie"] = new Color(0.70f, 0.72f, 0.40f),
        ["savannah"] = new Color(0.80f, 0.72f, 0.35f),
        ["marsh"] = new Color(0.45f, 0.55f, 0.45f),
        ["swamp"] = new Color(0.35f, 0.45f, 0.35f),
        ["desert"] = new Color(0.90f, 0.83f, 0.60f),
        ["tundra"] = new Color(0.75f, 0.78f, 0.70f),
        ["mixedForest"] = new Color(0.25f, 0.45f, 0.20f),
        ["coniferForest"] = new Color(0.18f, 0.38f, 0.22f),
        ["broadleafForest"] = new Color(0.28f, 0.50f, 0.22f),
        ["tropicalForest"] = new Color(0.15f, 0.42f, 0.18f),
        ["wetlandForest"] = new Color(0.22f, 0.40f, 0.30f),
        ["rainForest"] = new Color(0.10f, 0.35f, 0.15f),
        ["scrubForest"] = new Color(0.50f, 0.55f, 0.30f),
        ["borealForest"] = new Color(0.20f, 0.35f, 0.28f),
        ["hills"] = new Color(0.60f, 0.50f, 0.35f),
        ["mountains"] = new Color(0.55f, 0.55f, 0.58f),
        ["arctic"] = new Color(0.92f, 0.94f, 0.96f),
        ["ocean"] = new Color(0.18f, 0.32f, 0.55f),
        ["lake"] = new Color(0.25f, 0.45f, 0.65f),
        ["highSeas"] = new Color(0.12f, 0.22f, 0.45f),
        ["greatRiver"] = new Color(0.30f, 0.50f, 0.68f),
    };

    /// <summary>Assigns the map to draw and triggers a redraw.</summary>
    public void ShowMap(GameMap map)
    {
        _map = map;
        QueueRedraw();
    }

    /// <summary>Converts a map position to the pixel centre of its tile.</summary>
    public static Vector2 TileCentre(Position p) =>
        new(p.X * TileSize + TileSize / 2f, p.Y * TileSize + TileSize / 2f);

    /// <summary>Converts a point in this node's local space to a map position (may be off-map).</summary>
    public static Position TileAt(Vector2 local) =>
        new(Mathf.FloorToInt(local.X / TileSize), Mathf.FloorToInt(local.Y / TileSize));

    public override void _Draw()
    {
        if (_map is null)
        {
            return;
        }

        foreach (Position p in _map.AllPositions())
        {
            TerrainType terrain = _map.TerrainAt(p);
            Color colour = Palette.TryGetValue(terrain.ShortName, out Color c)
                ? c
                : new Color(1f, 0f, 1f);
            var rect = new Rect2(p.X * TileSize, p.Y * TileSize, TileSize, TileSize);
            DrawRect(rect, colour);
            DrawRect(rect, colour.Darkened(0.15f), filled: false, width: 1f);
        }
    }
}
