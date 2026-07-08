using System.Collections.Generic;
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

    /// <summary>
    /// Per-unit-id memory of the tile-pixel a marker was last placed at, surviving the free-all-then-rebuild of the
    /// unit layer each refresh. This is how a marker detects that <em>its</em> unit moved (the rebuilt markers carry no
    /// other identity), so the cosmetic slide can run from the old tile-pixel to the new one (`86d3fq26m`). Static
    /// because the marker instances themselves don't persist across a refresh. Presentation-only scratch — never touches
    /// <c>Game</c> state or the RNG.
    /// </summary>
    private static readonly Dictionary<int, Vector2> LastTileCentre = [];

    private int? _unitId;
    private Vector2 _pendingSlideFrom;
    private bool _hasPendingSlide;

    /// <summary>
    /// Largest tile gap (king-steps) still animated as a move; a larger jump is treated as a reappearance/teleport and
    /// snaps instead. Comfortably covers the fastest legitimate single-refresh move (a ship's per-turn sail) while
    /// excluding a unit re-emerging from fog, a ship, or Europe many tiles from where its marker last sat.
    /// </summary>
    private const int MaxSlideTiles = 10;

    /// <summary>
    /// Clears the per-unit slide memory. Called when the whole map is rebuilt for a different game (a new game / a load),
    /// so a marker at a reused position can't be mistaken for the same unit having moved there; GdUnit scene tests also
    /// call it for isolation. No effect on game state.
    /// </summary>
    public static void ResetMoveMemory() => LastTileCentre.Clear();

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

    private int _treasureAmount;

    /// <summary>
    /// The gold value carried by this unit, drawn as a small "Ng" badge under the marker so a treasure train's worth
    /// is visible on the map — not only at the Europe/cash-in moment (Chris 2026-07-08). 0 (every non-treasure unit)
    /// draws nothing, so all other markers render exactly as before. Read from <c>Unit.TreasureAmount</c> by
    /// <c>GameController.SyncUnitMarkers</c>; presentation-only.
    /// </summary>
    public int TreasureAmount
    {
        get => _treasureAmount;
        set
        {
            _treasureAmount = value;
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
    /// <param name="unitId">
    /// The unit's stable id, threaded so this marker can detect that <em>its</em> unit changed tile since the last
    /// refresh and play the cosmetic move-slide (`86d3fq26m`). Optional (defaults to <c>null</c>): when omitted the
    /// marker still renders correctly at its final position, just without the slide — so callers that don't have the id
    /// are unaffected. The slide itself is gated by <see cref="UnitMoveAnimation.Enabled"/> (forced off under test).
    /// </param>
    public void SetUnit(string typeShortName, string roleShortName, int? unitId = null)
    {
        _texture = ResolveTexture(typeShortName, roleShortName);
        _unitId = unitId;
        // Compare this marker's final tile-pixel (set by the host before SetUnit) against where this unit's marker last
        // sat: a change means the unit moved, so queue a slide from the old pixel. The marker isn't in the scene tree
        // yet (the host AddChilds it after SetUnit), so defer the actual spawn to _EnterTree.
        if (unitId is { } id && LastTileCentre.TryGetValue(id, out Vector2 previous) && previous != Position
            && IsSlideDistance(previous, Position))
        {
            _pendingSlideFrom = previous;
            _hasPendingSlide = true;
        }
        if (unitId is { } recordId)
        {
            LastTileCentre[recordId] = Position; // remember where this unit's marker is now, for the next refresh
        }
        QueueRedraw();
    }

    /// <summary>
    /// Fires any queued move-slide once the marker is in the scene tree (so the transient slide node has the unit layer
    /// as a parent and shares its isometric coordinate space). The marker itself is already at the destination tile;
    /// the slide is decoration that frees itself — final state is identical whether or not it ran (ADR-006). A no-op
    /// when nothing was queued or animations are off/headless.
    /// </summary>
    public override void _Ready()
    {
        if (_hasPendingSlide)
        {
            _hasPendingSlide = false;
            // Slide a copy of this marker's own sprite from the old tile to here; the real marker stays put at the end.
            UnitMoveAnimation.Play(GetParent(), TileOf(_pendingSlideFrom), TileOf(Position), _texture);
        }
    }

    /// <summary>Inverse of <see cref="MapView.TileCentre"/> for a known on-grid pixel — recovers the tile a cached/marker pixel represents so the slide can be expressed in tiles.</summary>
    private static GameLogic.World.Position TileOf(Vector2 pixel) => MapView.TileAt(pixel);

    /// <summary>
    /// True when the gap between a unit's previous and current tile is small enough to be a real move worth animating.
    /// A unit re-emerging from fog, a ship, or Europe can reappear far from where its marker last sat (its
    /// <see cref="LastTileCentre"/> entry is stale because off-map/out-of-sight units are skipped, not pruned); sliding
    /// across that gap would streak the sprite over untraversed terrain, so beyond <see cref="MaxSlideTiles"/> king-steps
    /// we snap. Cosmetic only — never affects <c>Game</c> state.
    /// </summary>
    private static bool IsSlideDistance(Vector2 fromPixel, Vector2 toPixel)
    {
        GameLogic.World.Position a = TileOf(fromPixel);
        GameLogic.World.Position b = TileOf(toPixel);
        int kingSteps = System.Math.Max(System.Math.Abs(a.X - b.X), System.Math.Abs(a.Y - b.Y));
        return kingSteps <= MaxSlideTiles;
    }

    /// <summary>
    /// Resolves the FreeCol sprite for a unit by type + role short names, using the same candidate-path search as
    /// <see cref="SetUnit"/>, or <c>null</c> when no art exists (the caller then uses its own fallback). Exposed so the
    /// procedural <see cref="CombatAnimation"/> can lunge the attacker's own sprite without duplicating the lookup or
    /// touching this marker's draw code.
    /// </summary>
    /// <param name="typeShortName">The unit type's short name (e.g. <c>freeColonist</c>, <c>caravel</c>).</param>
    /// <param name="roleShortName">The unit's role short name (e.g. <c>soldier</c>, <c>pioneer</c>, or <c>default</c>).</param>
    public static Texture2D? ResolveTexture(string typeShortName, string roleShortName)
    {
        foreach (string path in UnitSpriteCatalog.CandidatePaths(typeShortName, roleShortName))
        {
            if (ResourceLoader.Exists(path))
            {
                return GD.Load<Texture2D>(path);
            }
        }
        return null;
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

        // Treasure badge: the carried gold ("850g") under the marker, ColonyMarker's shadow-text pattern in treasure
        // gold. Only a unit actually carrying treasure draws it (Chris 2026-07-08 — the value must be visible on the
        // map, not only in Europe).
        if (_treasureAmount > 0)
        {
            Font font = ThemeDB.FallbackFont;
            const int fontSize = 12;
            string badge = $"{_treasureAmount}g";
            Vector2 text = font.GetStringSize(badge, HorizontalAlignment.Left, -1, fontSize);
            Vector2 origin = new(-text.X / 2f, MapView.TileH * 0.32f + fontSize);
            DrawString(font, origin + Vector2.One, badge, HorizontalAlignment.Left, -1, fontSize, Colors.Black);
            DrawString(font, origin, badge, HorizontalAlignment.Left, -1, fontSize, new Color(1f, 0.85f, 0.25f));
        }
    }
}
