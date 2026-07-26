using System;
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

    /// <summary>
    /// <b>Prototype top-down toggle (P8 look-and-feel).</b> When true the map projects straight down onto axis-aligned
    /// <b>square</b> tiles (closer to the original 1994 Colonization) instead of isometric diamonds; when false (the
    /// default, ADR-014) it renders the classic iso diamonds. Reversible: the iso path is untouched, so flipping this
    /// back restores today's look exactly. The square path <b>reuses the existing FreeCol ground art</b> by de-skewing
    /// each diamond into its square via a 4-corner texture warp (<see cref="DrawTopDown"/>) — not a rotation.
    /// </summary>
    public static bool TopDown;

    /// <summary>Square tile edge in pixels for the top-down prototype — sized near the iso tile's on-screen footprint so the map shows a comparable extent (zoom out for the whole continent).</summary>
    public const int SquareTile = 64;

    /// <summary>Modulate for explored-but-not-currently-visible tiles ("remembered" — darkened).</summary>
    private static readonly Color DimTint = new(0.5f, 0.5f, 0.58f);

    private GameMap? _map;
    private IReadOnlySet<Position>? _explored;
    private IReadOnlySet<Position>? _visible;

    /// <summary>The in-bounds map tile the cursor last hovered (so <see cref="HoveredTileChanged"/> only fires on a real change); null when off the map.</summary>
    private Position? _hoveredTile;

    /// <summary>
    /// Raised when the cursor moves to a different in-bounds tile (or off the map → <c>null</c>). The host
    /// (<see cref="GameController"/>) reads the public tile-yield oracle for the new tile and shows the production
    /// preview — so the rules stay in GameLogic and this stays a pure pointer-to-tile mapping (ADR-006).
    /// </summary>
    public event Action<Position?>? HoveredTileChanged;

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

    /// <summary>
    /// <b>Native square</b> top-down tiles, by terrain short name (WS2.5b). Top-down otherwise warps the inscribed
    /// diamond of a 128×64 isometric tile onto a square — but art drawn as diamonds cannot tile as squares, so that
    /// de-skew shows seams and repeating diagonal artefacts, and only part of each source image is ever sampled. A
    /// variant that ships <c>terrain/&lt;name&gt;/top0.png</c> gets a real seamless square instead. Terrains without one
    /// (and the whole classic ruleset, which ships none) fall back to the de-skew exactly as before.
    /// </summary>
    private readonly Dictionary<string, Texture2D[]> _topDownBases = [];
    private readonly Dictionary<string, Texture2D[]> _overlays = [];
    private readonly Dictionary<string, Texture2D> _bonusIcons = [];
    private Texture2D[] _unexplored = [];

    /// <summary>
    /// The goto route-preview overlay (a dedicated child so the projected path redraws independently of the expensive
    /// terrain <see cref="_Draw"/>). Created lazily in <see cref="ShowRoutePreview"/> so a host that never previews a
    /// route pays nothing. Pure presentation — it draws only the tile sequence handed to it (ADR-006).
    /// </summary>
    private RoutePreviewOverlay? _routeOverlay;

    /// <summary>
    /// Re-pulls every terrain / overlay texture through the variant art seam. <see cref="_Ready"/> runs <b>before</b> the
    /// host knows which variant is being played, so the first load is always the FreeCol fallback; the host calls this
    /// once it has set <see cref="ColonyArt.VariantArtRoot"/> (WS2.5). A no-op-shaped call: safe to call repeatedly.
    /// </summary>
    public void ReloadTerrainArt()
    {
        _bases.Clear();
        _topDownBases.Clear();
        _overlays.Clear();
        LoadTerrainArt();
        QueueRedraw();
    }

    /// <summary>Pulls every terrain base + overlay texture through the variant art seam. Split out of <see cref="_Ready"/> so <see cref="ReloadTerrainArt"/> can re-run it once the variant is known.</summary>
    private void LoadTerrainArt()
    {
        foreach (string name in new[]
        {
            "arctic", "desert", "grassland", "highSeas", "marsh", "ocean",
            "plains", "prairie", "savannah", "swamp", "tundra",
        })
        {
            _bases[name] = LoadVariants($"terrain/{name}/center");
            Texture2D[] square = LoadVariants($"terrain/{name}/top");
            if (square.Length > 0)
            {
                _topDownBases[name] = square; // WS2.5b: a native square tile for this terrain
            }
        }
        // The overlay terrains (forest/hills/mountains) carry their ground under these names too.
        foreach (string name in new[] { "hills", "mountains" })
        {
            Texture2D[] square = LoadVariants($"terrain/{name}/top");
            if (square.Length > 0)
            {
                _topDownBases[name] = square;
            }
        }
        _unexplored = LoadVariants("terrain/unexplored/center");

        foreach ((string name, string[] files) in OverlayFiles)
        {
            var textures = new List<Texture2D>();
            foreach (string file in files)
            {
                if (ColonyArt.LoadTexture(file) is { } tex) // variant-first, FreeCol fallback (WS1.3)
                {
                    textures.Add(tex);
                }
            }
            _overlays[name] = [.. textures];
        }
    }

    public override void _Ready()
    {
        LoadTerrainArt();

        foreach (string resource in new[]
        {
            "cotton", "fish", "furs", "game", "grain", "lumber",
            "minerals", "oasis", "ore", "silver", "sugar", "tobacco",
        })
        {
            _bonusIcons[resource] = GD.Load<Texture2D>($"res://assets/freecol/bonus/{resource}.png");
        }
    }

    /// <summary>
    /// Loads a terrain's numbered variants (<c>center0.png</c>, <c>center1.png</c>) <b>through the variant art seam</b>
    /// (<see cref="ColonyArt.Load"/>), so an Australia game gets <c>res://assets/australia/…</c> where it exists and
    /// falls back to the FreeCol original per file. This used to hard-code <c>res://assets/freecol/</c>, which meant the
    /// map — by far the largest art surface in the game — silently bypassed the WS1.3 seam and could never show variant
    /// terrain however much art was supplied.
    /// </summary>
    private static Texture2D[] LoadVariants(string prefix)
    {
        var variants = new List<Texture2D>();
        for (int i = 0; i < 2; i++)
        {
            if (ColonyArt.LoadTexture($"{prefix}{i}.png") is { } tex)
            {
                variants.Add(tex);
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

    /// <summary>The number of tiles currently drawn as explored — the test seam for the Admin "Show all map" reveal (it jumps to the full map when the cheat is on). 0 before any state is shown.</summary>
    internal int ExploredTileCount => _explored?.Count ?? 0;

    /// <summary>Projects a map position to the pixel centre of its tile — an iso diamond, or a top-down square when <see cref="TopDown"/>.</summary>
    public static Vector2 TileCentre(Position p) =>
        TopDown
            ? new(p.X * SquareTile, p.Y * SquareTile)
            : new((p.X - p.Y) * (TileW / 2f), (p.X + p.Y) * (TileH / 2f));

    /// <summary>
    /// Shows a projected <b>goto route preview</b>: a waypoint line + dots from the unit's current tile through
    /// <paramref name="route"/> to its destination (FreeCol's <c>displayPath</c>). The host passes the tiles the unit
    /// would <b>enter</b> (the ordered sequence from <see cref="Game.PreviewRoute"/>) plus, optionally, the unit's
    /// current tile as <paramref name="from"/> so the line starts at the unit (FreeCol's path includes the origin node).
    /// An <b>empty</b> route clears the preview — so the same call both shows and hides it as the aimed tile changes.
    /// Pure presentation: it draws only what it is given and reads/mutates no game state (ADR-006).
    /// </summary>
    /// <param name="route">The ordered tiles the unit would enter (empty = clear the preview).</param>
    /// <param name="from">The unit's current tile, prepended so the line starts at the unit; null = start at the first waypoint.</param>
    public void ShowRoutePreview(IReadOnlyList<Position> route, Position? from = null)
    {
        if (route.Count == 0)
        {
            ClearRoutePreview();
            return;
        }
        _routeOverlay ??= AddRouteOverlay();
        var tiles = new List<Position>(route.Count + 1);
        if (from is { } start)
        {
            tiles.Add(start);
        }
        tiles.AddRange(route);
        _routeOverlay.SetRoute(tiles);
    }

    /// <summary>Clears any shown goto route preview (a no-op when none is shown).</summary>
    public void ClearRoutePreview() => _routeOverlay?.SetRoute([]);

    /// <summary>The tiles the route preview is currently drawing (origin + waypoints), in order — for tests/hosts. Empty when no preview is shown.</summary>
    public IReadOnlyList<Position> PreviewedRouteTiles => _routeOverlay?.Tiles ?? [];

    private RoutePreviewOverlay AddRouteOverlay()
    {
        var overlay = new RoutePreviewOverlay { Name = "RoutePreview" };
        AddChild(overlay); // a sibling of the terrain draw, in the same (map) coordinate space
        return overlay;
    }

    /// <summary>
    /// Converts a point in this node's local space to a map position (may be off-map).
    /// </summary>
    /// <remarks>
    /// This is the exact inverse of <see cref="TileCentre"/> and is a true point-in-diamond
    /// pick — not a rectangular bounding box. The forward projection rotates the square logic
    /// grid 45°: <c>local = ((x − y)·64, (x + y)·32)</c>. Inverting it gives fractional grid
    /// coordinates <c>x = (u + v)/2</c>, <c>y = (v − u)/2</c> where <c>u = localX/64</c>,
    /// <c>v = localY/32</c>; rounding each to the nearest integer selects the tile whose diamond
    /// contains the point. Independent rounding in this rotated basis is provably equivalent to a
    /// point-in-diamond test: the region rounding to tile <c>(X,Y)</c> is exactly
    /// <c>|x−X| ≤ ½ ∧ |y−Y| ≤ ½</c>, which maps to the drawn diamond with corners at local offsets
    /// <c>(±64, 0)</c> and <c>(0, ±32)</c> — so any click inside a tile's diamond resolves to that tile.
    /// </remarks>
    public static Position TileAt(Vector2 local)
    {
        if (TopDown)
        {
            // Top-down: a plain rectilinear pick — nearest square-cell centre.
            return new Position(Mathf.RoundToInt(local.X / SquareTile), Mathf.RoundToInt(local.Y / SquareTile));
        }
        float gx = local.X / (TileW / 2f);
        float gy = local.Y / (TileH / 2f);
        return new Position(
            Mathf.RoundToInt((gx + gy) / 2f),
            Mathf.RoundToInt((gy - gx) / 2f));
    }

    /// <summary>
    /// Converts a viewport-space point (an input event's <c>Position</c>) to the map tile under it.
    /// </summary>
    /// <remarks>
    /// Picking must convert the <em>event's own</em> coordinate, not the live cursor: handlers run
    /// from the buffered input queue, so <c>GetLocalMousePosition()</c> (which polls the current OS
    /// cursor) can read a position the cursor drifted to after the button went down — while the camera
    /// pans, the wheel zooms, or input is buffered — making clicks land on the wrong tile or miss. The
    /// event's <c>Position</c> is the screen point the click was generated at; mapping it through
    /// <see cref="CanvasItem.GetGlobalTransformWithCanvas"/> (which folds in the <see cref="Camera2D"/>
    /// zoom/pan) yields the correct node-local point for <see cref="TileAt(Vector2)"/>, so picking is
    /// accurate at any zoom or pan.
    /// </remarks>
    /// <param name="viewportPosition">The event position in viewport (screen) space.</param>
    public Position TileAtScreen(Vector2 viewportPosition) =>
        TileAt(GetGlobalTransformWithCanvas().AffineInverse() * viewportPosition);

    /// <summary>
    /// Polls the cursor each frame and raises <see cref="HoveredTileChanged"/> when it crosses into a different
    /// in-bounds tile (or leaves the map). Polling (rather than consuming motion events) keeps hover tracking working
    /// while the camera drag-pan consumes those events, and is cheap (one transform + a bounds check per frame). The
    /// hovered tile is reported via the same event-position transform the click picker uses, so it is zoom/pan-correct.
    /// </summary>
    public override void _Process(double delta)
    {
        if (_map is null)
        {
            return;
        }
        Position tile = TileAt(GetLocalMousePosition());
        Position? current = _map.InBounds(tile) ? tile : null;
        if (current != _hoveredTile)
        {
            _hoveredTile = current;
            HoveredTileChanged?.Invoke(current);
        }
    }

    /// <summary>Reports the tile the cursor is currently over (for the host's hover preview / tests); null when off the map.</summary>
    public Position? HoveredTile => _hoveredTile;

    public override void _Draw()
    {
        if (_map is null)
        {
            return;
        }

        if (TopDown)
        {
            DrawTopDown();
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

    // ── Top-down square prototype (P8 look-and-feel) ────────────────────────────────────────────────────────────────
    // Renders the map straight-down onto square tiles instead of iso diamonds. It REUSES the existing FreeCol ground
    // art: each diamond ground texture is de-skewed into its square by a 4-corner texture warp (DrawPolygon with the
    // diamond's corner UVs mapped to the square's corners) — the correct way to reuse iso art top-down (a plain rotate
    // would leave it squished). Tall "standing-up" overlays (forests/hills/mountains) can't be de-skewed, so they're
    // drawn as a shrunk centred symbol — a prototype stand-in until proper top-down tiles are sourced. Iso-only extras
    // (the magenta unmapped-tile diamond) collapse to a square here.

    /// <summary>The UV coordinates of a diamond's corners (top, right, bottom, left) in a tile texture whose diamond fills its bounds — used to de-skew the diamond onto a square via <see cref="CanvasItem.DrawPolygon"/>.</summary>
    private static readonly Vector2[] DiamondUVs = [new(0.5f, 0f), new(1f, 0.5f), new(0.5f, 1f), new(0f, 0.5f)];

    private void DrawTopDown()
    {
        float h = SquareTile / 2f;
        foreach (Position p in _map!.AllPositions())
        {
            Vector2 c = TileCentre(p);
            int variantSeed = p.X * 7919 + p.Y * 104729;
            // Square-cell corners, clockwise from top-left (aligned to the DiamondUVs order top/right/bottom/left).
            Vector2[] square = [new(c.X - h, c.Y - h), new(c.X + h, c.Y - h), new(c.X + h, c.Y + h), new(c.X - h, c.Y + h)];

            if (_explored is not null && !_explored.Contains(p))
            {
                DrawDeskewed(_unexplored, variantSeed, square, Colors.White);
                continue;
            }

            Color tint = _visible is not null && !_visible.Contains(p) ? DimTint : Colors.White;
            TerrainType terrain = _map.TerrainAt(p);
            string baseName = BaseFor.GetValueOrDefault(terrain.ShortName, terrain.ShortName);
            // WS2.5b: a native square tile draws 1:1 (no warp, no seams); otherwise fall back to de-skewing the diamond.
            if (_topDownBases.TryGetValue(baseName, out Texture2D[]? squareVariants))
            {
                DrawSquare(squareVariants, variantSeed, square, tint);
            }
            else if (_bases.TryGetValue(baseName, out Texture2D[]? baseVariants))
            {
                DrawDeskewed(baseVariants, variantSeed, square, tint);
            }
            else
            {
                DrawColoredPolygon(square, new Color(1f, 0f, 1f)); // unmapped terrain → magenta square
            }

            // Forest / hills / mountains: a shrunk centred symbol (the tall iso art can't be de-skewed) so the type reads.
            if (_overlays.TryGetValue(terrain.ShortName, out Texture2D[]? overlay) && overlay.Length > 0)
            {
                DrawCentredSymbol(overlay[(variantSeed & int.MaxValue) % overlay.Length], c, tint, 0.85f);
            }

            if (_map.ResourceAt(p) is { } resourceId)
            {
                string shortName = resourceId[(resourceId.LastIndexOf('.') + 1)..];
                if (_bonusIcons.TryGetValue(shortName, out Texture2D? icon))
                {
                    DrawTexture(icon, c - icon.GetSize() / 2f, tint);
                }
            }
        }
    }

    /// <summary>De-skews a diamond ground texture onto the given square (4-corner texture warp), tinted by <paramref name="modulate"/>.</summary>
    /// <summary>Draws a <b>native square</b> tile onto the cell 1:1 — full-texture UVs, no warp, so a seamless source stays seamless (WS2.5b).</summary>
    private void DrawSquare(Texture2D[] variants, int variantSeed, Vector2[] square, Color modulate)
    {
        if (variants.Length == 0)
        {
            return;
        }
        Texture2D texture = variants[(variantSeed & int.MaxValue) % variants.Length];
        Color[] colors = [modulate, modulate, modulate, modulate];
        Vector2[] uvs = [new(0f, 0f), new(1f, 0f), new(1f, 1f), new(0f, 1f)]; // matches the square's corner order
        DrawPolygon(square, colors, uvs, texture);
    }

    private void DrawDeskewed(Texture2D[] variants, int variantSeed, Vector2[] square, Color modulate)
    {
        if (variants.Length == 0)
        {
            return;
        }
        Texture2D texture = variants[(variantSeed & int.MaxValue) % variants.Length];
        Color[] colors = [modulate, modulate, modulate, modulate];
        DrawPolygon(square, colors, DiamondUVs, texture);
    }

    /// <summary>Draws a texture shrunk to <paramref name="fraction"/> of a square tile, centred on <paramref name="centre"/> — the top-down stand-in for a standing-up overlay.</summary>
    private void DrawCentredSymbol(Texture2D texture, Vector2 centre, Color modulate, float fraction)
    {
        Vector2 texSize = texture.GetSize();
        float scale = SquareTile * fraction / Math.Max(texSize.X, texSize.Y);
        Vector2 drawn = texSize * scale;
        DrawTextureRect(texture, new Rect2(centre - (drawn / 2f), drawn), tile: false, modulate);
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

    /// <summary>
    /// Draws the projected goto route (FreeCol <c>MapViewer.displayPath</c>): a gold poly-line through the tile
    /// centres of the previewed path plus a filled dot at each waypoint, so the player can see exactly where a
    /// multi-turn move will take a unit before committing it. A dedicated overlay so the path can be redrawn (as the
    /// aimed tile changes) without re-running the terrain draw. Pure presentation — it holds only the tile list it is
    /// handed and reads no game state (ADR-006).
    /// </summary>
    private sealed partial class RoutePreviewOverlay : Node2D
    {
        private static readonly Color RouteColor = new(0.96f, 0.86f, 0.20f, 0.85f); // goto gold (matches GotoMarker)
        private const float LineWidth = 4f;
        private const float DotRadius = 6f;

        private readonly List<Position> _tiles = [];

        /// <summary>The route tiles currently drawn (origin + waypoints), in order.</summary>
        public IReadOnlyList<Position> Tiles => _tiles;

        /// <summary>Replaces the drawn route with <paramref name="tiles"/> (origin + waypoints) and redraws.</summary>
        public void SetRoute(IReadOnlyList<Position> tiles)
        {
            _tiles.Clear();
            _tiles.AddRange(tiles);
            QueueRedraw();
        }

        public override void _Draw()
        {
            if (_tiles.Count == 0)
            {
                return;
            }

            // The connecting line: tile-centre to tile-centre along the path (skipped when only the origin is present).
            for (int i = 1; i < _tiles.Count; i++)
            {
                DrawLine(TileCentre(_tiles[i - 1]), TileCentre(_tiles[i]), RouteColor, LineWidth);
            }

            // A waypoint dot on every tile the route passes through (the destination included).
            foreach (Position tile in _tiles)
            {
                DrawCircle(TileCentre(tile), DotRadius, RouteColor);
            }
        }
    }
}
