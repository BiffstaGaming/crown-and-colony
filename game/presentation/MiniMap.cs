using System;
using System.Collections.Generic;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Natives;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// A corner overview map (FreeCol <c>MiniMap</c>): draws the explored world as scaled flat-colour cells with
/// colony / native-settlement / unit dots, recenters the main camera when clicked, and zooms in/out. Pure
/// presentation (ADR-006) — it reads <see cref="Game"/> oracles (the explored/visible fog sets, terrain and
/// entity positions) and never mutates game state. Fog-respecting: unexplored tiles stay dark, remembered
/// (explored-but-not-visible) tiles render dimmed, and an entity only shows once its tile is in the fog.
/// </summary>
public partial class MiniMap : Control
{
    /// <summary>Pixels per map tile at each zoom step (index into this array via <see cref="_zoom"/>).</summary>
    private static readonly int[] ZoomSteps = [2, 3, 4, 6];

    /// <summary>Unexplored fill — the dark "nothing seen yet" backdrop (also the opaque panel background).</summary>
    private static readonly Color Unexplored = new(0.05f, 0.06f, 0.09f);

    /// <summary>Flat minimap palette keyed on a terrain's short name (small cells, so no art — just a colour).</summary>
    private static readonly Dictionary<string, Color> TerrainColor = new()
    {
        ["ocean"] = new(0.16f, 0.34f, 0.55f),
        ["highSeas"] = new(0.10f, 0.22f, 0.42f),
        ["lake"] = new(0.27f, 0.47f, 0.66f),
        ["greatRiver"] = new(0.27f, 0.47f, 0.66f),
        ["arctic"] = new(0.90f, 0.93f, 0.97f),
        ["tundra"] = new(0.55f, 0.60f, 0.50f),
        ["desert"] = new(0.85f, 0.78f, 0.50f),
        ["grassland"] = new(0.42f, 0.62f, 0.30f),
        ["plains"] = new(0.58f, 0.68f, 0.34f),
        ["prairie"] = new(0.74f, 0.71f, 0.36f),
        ["savannah"] = new(0.52f, 0.63f, 0.30f),
        ["marsh"] = new(0.40f, 0.50f, 0.36f),
        ["swamp"] = new(0.30f, 0.40f, 0.32f),
        ["hills"] = new(0.56f, 0.46f, 0.30f),
        ["mountains"] = new(0.46f, 0.43f, 0.41f),
        ["mixedForest"] = new(0.25f, 0.46f, 0.24f),
        ["coniferForest"] = new(0.20f, 0.42f, 0.26f),
        ["broadleafForest"] = new(0.30f, 0.48f, 0.22f),
        ["tropicalForest"] = new(0.22f, 0.44f, 0.20f),
        ["wetlandForest"] = new(0.26f, 0.44f, 0.30f),
        ["rainForest"] = new(0.18f, 0.40f, 0.22f),
        ["scrubForest"] = new(0.45f, 0.50f, 0.26f),
        ["borealForest"] = new(0.28f, 0.45f, 0.32f),
    };

    private static readonly Color FallbackTerrain = new(0.5f, 0.5f, 0.5f);
    private static readonly Color Border = new(0.75f, 0.66f, 0.45f); // parchment-ish frame
    private static readonly Color OwnColonyDot = Colors.White;
    private static readonly Color RivalColonyDot = new(0.95f, 0.45f, 0.20f);
    private static readonly Color NativeDot = new(0.85f, 0.65f, 0.25f);
    private static readonly Color OwnUnitDot = new(0.85f, 0.90f, 1.0f);
    private static readonly Color RivalUnitDot = new(0.90f, 0.30f, 0.30f);

    private int _zoom = 2; // default ~4 px/tile
    private Game? _game;

    /// <summary>Raised with the map tile a click maps to, so the host can recenter the main camera there.</summary>
    public event Action<Position>? TileSelected;

    /// <summary>The current zoom level's pixels-per-tile (exposed for tests / the host).</summary>
    public int PixelsPerTile => ZoomSteps[_zoom];

    /// <summary>Assigns the game to read and triggers a redraw. Reads only — never mutates (ADR-006).</summary>
    public void ShowState(Game game)
    {
        _game = game;
        QueueRedraw();
    }

    /// <summary>Zooms in one step (larger tiles), capped at the most-zoomed level.</summary>
    public void ZoomIn()
    {
        _zoom = Math.Min(_zoom + 1, ZoomSteps.Length - 1);
        QueueRedraw();
    }

    /// <summary>Zooms out one step (smaller tiles, more map visible), capped at the least-zoomed level.</summary>
    public void ZoomOut()
    {
        _zoom = Math.Max(_zoom - 1, 0);
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Unexplored); // opaque backdrop, so the cropped golden is clean
        if (_game is null)
        {
            return;
        }

        int pp = PixelsPerTile;
        (float offX, float offY) = Offset(pp);
        GameMap map = _game.Map;

        // Terrain cells (explored only; remembered tiles dimmed) — fog-respecting.
        foreach (Position p in map.AllPositions())
        {
            if (!_game.IsExplored(p))
            {
                continue; // unexplored → the dark backdrop shows through
            }

            var cell = new Rect2(p.X * pp - offX, p.Y * pp - offY, pp, pp);
            if (!OnPanel(cell))
            {
                continue;
            }

            Color colour = TerrainColor.GetValueOrDefault(map.TerrainAt(p).ShortName, FallbackTerrain);
            if (!_game.IsVisible(p))
            {
                colour = colour.Darkened(0.4f); // remembered, out of current sight
            }
            DrawRect(cell, colour);
        }

        // Entity dots on top: colonies + native settlements once their tile is explored; units while in sight.
        long human = _game.HumanPlayer.PlayerId;
        float dot = Math.Max(2f, pp);
        foreach (Colony c in _game.Colonies)
        {
            if (_game.IsExplored(c.Position))
            {
                DrawDot(c.Position, pp, offX, offY, dot, c.OwnerId == human ? OwnColonyDot : RivalColonyDot);
            }
        }
        foreach (NativeSettlement s in _game.NativeSettlements)
        {
            if (_game.IsExplored(s.Position))
            {
                DrawDot(s.Position, pp, offX, offY, dot, NativeDot);
            }
        }
        foreach (Unit u in _game.Units)
        {
            if (u.IsOnMap && _game.IsVisible(u.Position))
            {
                DrawDot(u.Position, pp, offX, offY, dot, u.OwnerId == human ? OwnUnitDot : RivalUnitDot);
            }
        }

        // Frame.
        DrawRect(new Rect2(Vector2.Zero, Size), Border, filled: false, width: 2f);
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (_game is null || @event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb)
        {
            return;
        }

        int pp = PixelsPerTile;
        (float offX, float offY) = Offset(pp);
        var tile = new Position(
            Mathf.FloorToInt((mb.Position.X + offX) / pp),
            Mathf.FloorToInt((mb.Position.Y + offY) / pp));
        if (_game.Map.InBounds(tile))
        {
            TileSelected?.Invoke(tile);
            AcceptEvent();
        }
    }

    /// <summary>The top-left map-pixel offset that centres the scaled map within the panel (clips when it overflows).</summary>
    private (float, float) Offset(int pp)
    {
        if (_game is null)
        {
            return (0f, 0f);
        }
        return ((_game.Map.Width * pp - Size.X) / 2f, (_game.Map.Height * pp - Size.Y) / 2f);
    }

    private bool OnPanel(Rect2 cell) =>
        cell.Position.X + cell.Size.X > 0 && cell.Position.X < Size.X
        && cell.Position.Y + cell.Size.Y > 0 && cell.Position.Y < Size.Y;

    private void DrawDot(Position p, int pp, float offX, float offY, float dot, Color colour)
    {
        var cell = new Rect2(p.X * pp - offX, p.Y * pp - offY, dot, dot);
        if (OnPanel(cell))
        {
            DrawRect(cell, colour);
        }
    }
}
