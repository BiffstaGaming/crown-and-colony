using System.Collections.Generic;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.World;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// Draws the game map as isometric diamonds using FreeCol terrain art (ADR-014).
/// The logic grid is unchanged — this view projects the square grid 45°:
/// <c>screen = ((x − y)·64, (x + y)·32)</c>. Pure presentation — reads the map,
/// never mutates it.
/// </summary>
public partial class MapView : Node2D
{
    /// <summary>Diamond tile width in pixels (FreeCol art standard).</summary>
    public const int TileW = 128;

    /// <summary>Diamond tile height in pixels.</summary>
    public const int TileH = 64;

    /// <summary>Modulate for explored-but-not-currently-visible tiles ("remembered" — darkened).</summary>
    private static readonly Color DimTint = new(0.5f, 0.5f, 0.58f);

    private GameMap? _map;
    private IReadOnlySet<Position>? _explored;
    private IReadOnlySet<Position>? _visible;

    // Forested/elevated terrain renders as a base tile + an overlay drawn on top.
    // Base mapping mirrors the classic ruleset's climate pairs (forest variants
    // share their clear counterpart's envelope); hills/mountains have no base
    // art of their own in FreeCol, so they sit on grassland/tundra.
    private static readonly Dictionary<string, string> BaseFor = new()
    {
        ["mixedForest"] = "plains",
        ["coniferForest"] = "grassland",
        ["broadleafForest"] = "prairie",
        ["tropicalForest"] = "savannah",
        ["wetlandForest"] = "marsh",
        ["rainForest"] = "swamp",
        ["scrubForest"] = "desert",
        ["borealForest"] = "tundra",
        ["hills"] = "grassland",
        ["mountains"] = "tundra",
        // Water types without their own art reuse ocean (lake/greatRiver,
        // not generated yet but saves may contain them).
        ["lake"] = "ocean",
        ["greatRiver"] = "ocean",
    };

    private static readonly Dictionary<string, string[]> OverlayFiles = new()
    {
        ["mixedForest"] = ["forest/mixed/mixed.png"],
        ["coniferForest"] = ["forest/conifer/conifer.png"],
        ["broadleafForest"] = ["forest/broadleaf/broadleaf.png"],
        ["tropicalForest"] = ["forest/tropical/tropical.png"],
        ["wetlandForest"] = ["forest/wetland/wetland.png"],
        ["rainForest"] = ["forest/rain/rain.png"],
        ["scrubForest"] = ["forest/scrub/scrub.png"],
        ["borealForest"] = ["forest/boreal/boreal.png"],
        ["hills"] = ["terrain/hills/hills0.png", "terrain/hills/hills1.png"],
        ["mountains"] = ["terrain/mountains/mountains0.png", "terrain/mountains/mountains1.png"],
    };

    private readonly Dictionary<string, Texture2D[]> _bases = [];
    private readonly Dictionary<string, Texture2D[]> _overlays = [];
    private readonly Dictionary<string, Texture2D> _bonusIcons = [];
    private Texture2D[] _unexplored = [];

    public override void _Ready()
    {
        foreach (string name in new[]
        {
            "arctic", "desert", "grassland", "highSeas", "marsh", "ocean",
            "plains", "prairie", "savannah", "swamp", "tundra",
        })
        {
            _bases[name] = LoadVariants($"terrain/{name}/center");
        }
        _unexplored = LoadVariants("terrain/unexplored/center");

        foreach ((string name, string[] files) in OverlayFiles)
        {
            var textures = new List<Texture2D>();
            foreach (string file in files)
            {
                textures.Add(GD.Load<Texture2D>($"res://assets/freecol/{file}"));
            }
            _overlays[name] = [.. textures];
        }

        foreach (string resource in new[]
        {
            "cotton", "fish", "furs", "game", "grain", "lumber",
            "minerals", "oasis", "ore", "silver", "sugar", "tobacco",
        })
        {
            _bonusIcons[resource] = GD.Load<Texture2D>($"res://assets/freecol/bonus/{resource}.png");
        }
    }

    private static Texture2D[] LoadVariants(string prefix)
    {
        var variants = new List<Texture2D>();
        for (int i = 0; i < 2; i++)
        {
            string path = $"res://assets/freecol/{prefix}{i}.png";
            if (ResourceLoader.Exists(path))
            {
                variants.Add(GD.Load<Texture2D>(path));
            }
        }
        return [.. variants];
    }

    /// <summary>
    /// Assigns the map, the explored set (ever-seen) and the currently-visible set to
    /// draw, then triggers a redraw. Explored-but-not-visible tiles render dimmed.
    /// </summary>
    public void ShowState(GameMap map, IReadOnlySet<Position> explored, IReadOnlySet<Position> visible)
    {
        _map = map;
        _explored = explored;
        _visible = visible;
        QueueRedraw();
    }

    /// <summary>Projects a map position to the pixel centre of its diamond.</summary>
    public static Vector2 TileCentre(Position p) =>
        new((p.X - p.Y) * (TileW / 2f), (p.X + p.Y) * (TileH / 2f));

    /// <summary>Converts a point in this node's local space to a map position (may be off-map).</summary>
    public static Position TileAt(Vector2 local)
    {
        float gx = local.X / (TileW / 2f);
        float gy = local.Y / (TileH / 2f);
        return new Position(
            Mathf.RoundToInt((gx + gy) / 2f),
            Mathf.RoundToInt((gy - gx) / 2f));
    }

    public override void _Draw()
    {
        if (_map is null)
        {
            return;
        }

        // Row-major order is back-to-front for upward-extending overlays.
        foreach (Position p in _map.AllPositions())
        {
            Vector2 centre = TileCentre(p);
            int variantSeed = p.X * 7919 + p.Y * 104729;

            if (_explored is not null && !_explored.Contains(p))
            {
                DrawTile(_unexplored, variantSeed, centre, Colors.White);
                continue;
            }

            // Explored but out of current sight → "remembered": draw dimmed.
            Color tint = _visible is not null && !_visible.Contains(p) ? DimTint : Colors.White;

            TerrainType terrain = _map.TerrainAt(p);
            string baseName = BaseFor.GetValueOrDefault(terrain.ShortName, terrain.ShortName);
            if (_bases.TryGetValue(baseName, out Texture2D[]? baseVariants))
            {
                DrawTile(baseVariants, variantSeed, centre, tint);
            }
            else
            {
                // Unmissable magenta diamond for unmapped terrain.
                DrawColoredPolygon(DiamondPoints(centre), new Color(1f, 0f, 1f));
            }

            if (_overlays.TryGetValue(terrain.ShortName, out Texture2D[]? overlay))
            {
                DrawTile(overlay, variantSeed, centre, tint);
            }

            // Bonus resource icon, centred on the diamond.
            if (_map.ResourceAt(p) is { } resourceId)
            {
                string shortName = resourceId[(resourceId.LastIndexOf('.') + 1)..];
                if (_bonusIcons.TryGetValue(shortName, out Texture2D? icon))
                {
                    Vector2 size = icon.GetSize();
                    DrawTexture(icon, centre - size / 2f, tint);
                }
            }
        }
    }

    /// <summary>Draws a tile texture bottom-aligned to the diamond (overlays are taller than 64px), tinted by <paramref name="modulate"/>.</summary>
    private void DrawTile(Texture2D[] variants, int variantSeed, Vector2 centre, Color modulate)
    {
        if (variants.Length == 0)
        {
            return;
        }
        Texture2D texture = variants[(variantSeed & int.MaxValue) % variants.Length];
        Vector2 size = texture.GetSize();
        DrawTexture(texture, new Vector2(centre.X - size.X / 2f, centre.Y + TileH / 2f - size.Y), modulate);
    }

    private static Vector2[] DiamondPoints(Vector2 c) =>
    [
        new(c.X, c.Y - TileH / 2f),
        new(c.X + TileW / 2f, c.Y),
        new(c.X, c.Y + TileH / 2f),
        new(c.X - TileW / 2f, c.Y),
    ];
}
