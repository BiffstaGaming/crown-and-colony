using CrownAndColony.GameLogic.World;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// A short, purely-cosmetic slide played when a unit marker changes tile (`86d3fq26m`). The on-map
/// <see cref="UnitMarker"/> layer is rebuilt wholesale every refresh (free-all-then-rebuild in
/// <c>GameController.SyncUnitMarkers</c>), so a moved unit's marker simply reappears at its new tile — it would
/// otherwise <em>teleport</em>. This node closes that gap the way FreeCol's <c>UnitAnimator</c>/<c>Animations.unitMove</c>
/// does: it lifts a transient copy of the unit's sprite and <em>slides</em> it from the old tile-pixel to the new one,
/// then frees itself. The real marker already sits at the destination, so the final on-screen state is identical whether
/// or not this animation ran — it is decoration over an already-resolved move (ADR-006: it reads nothing and mutates
/// nothing in <c>Game</c>, and draws no RNG).
/// </summary>
/// <remarks>
/// One-shot and non-blocking (same shape as <see cref="CombatAnimation"/>): <see cref="Play"/> instantiates a node,
/// parents it to the map's unit layer (sharing the isometric tile coordinate space), runs a <see cref="Tween"/>, and
/// frees itself on completion. Nothing awaits it; the turn/refresh proceeds immediately. It is slide-to-final
/// (old tile → new tile in one tween) rather than per-step, because the marker-refresh path sees only the start and end
/// tiles, not the path between them — a faithful-enough subset of FreeCol's per-step <c>animateMove</c>.
/// <para>
/// <b>Headless / test safety.</b> The animation is gated on the static <see cref="Enabled"/> flag (default true): GdUnit
/// scene tests force it <c>false</c> so a move resolves instantly and deterministically (the marker is already at the
/// destination — no transient, no wall-clock tween to wait on). It is also skipped automatically when there is no real
/// display (a truly headless run). The duration is short so it never gates input even when on.
/// </para>
/// </remarks>
public partial class UnitMoveAnimation : Node2D
{
    /// <summary>Slide duration per tile of distance, in seconds (kept short so it never gates input). One tween covers the whole hop.</summary>
    public const float SecondsPerTile = 0.15f;

    /// <summary>Upper bound on the slide so a long single hop (e.g. a recomputed marker after several steps) can't drag on.</summary>
    public const float MaxSeconds = 0.5f;

    /// <summary>
    /// When false, <see cref="Play"/> spawns nothing and returns <c>null</c> — the move is instant (the marker already
    /// sits at the destination). GdUnit scene tests force this off for deterministic, wall-clock-free assertions; it is
    /// the test escape hatch that mirrors how the combat lunge is kept out of the way under test.
    /// </summary>
    public static bool Enabled { get; set; } = true;

    private Sprite2D _sprite = null!;
    private Vector2 _from;
    private Vector2 _to;
    private Texture2D? _texture;
    private float _duration;

    /// <summary>
    /// Spawns and runs a one-shot slide for a unit that has just changed tile. Non-blocking: returns the node
    /// immediately (or <c>null</c> when animations are off / headless / the hop is degenerate); the slide plays itself
    /// out and frees the node when done.
    /// </summary>
    /// <param name="unitLayer">
    /// The map's unit layer (the moving marker's parent), used to locate the isometric coordinate space. The slide is
    /// actually parented to that layer's <em>parent</em> (the <see cref="MapView"/>, the same coordinate space) so the
    /// per-refresh unit-layer wipe (<c>SyncUnitMarkers</c> frees every child of the layer) can't reap the transient
    /// mid-slide — only the never-wiped <see cref="MapView"/> is touched. Mirrors <see cref="CombatAnimation.Play"/>.
    /// </param>
    /// <param name="from">The unit's previous tile (where the slide starts).</param>
    /// <param name="to">The unit's new tile (where the slide — and the real marker — ends).</param>
    /// <param name="texture">The unit's sprite to slide, or <c>null</c> to slide the red-disc stand-in.</param>
    /// <returns>The spawned node (already added to the map layer), or <c>null</c> when nothing was spawned.</returns>
    public static UnitMoveAnimation? Play(Node unitLayer, Position from, Position to, Texture2D? texture)
    {
        if (!Enabled || IsHeadless() || from == to)
        {
            return null; // disabled, no display, or not actually a move → instant (marker already at the destination)
        }
        Vector2 fromPx = MapView.TileCentre(from);
        Vector2 toPx = MapView.TileCentre(to);
        var anim = new UnitMoveAnimation
        {
            _from = fromPx,
            _to = toPx,
            _texture = texture,
            _duration = Mathf.Min(MaxSeconds, SecondsPerTile * (fromPx.DistanceTo(toPx) / MapView.TileW + 1f)),
        };
        // Attach to the durable map layer (the unit layer's parent), not the unit layer itself — see the param note.
        (unitLayer.GetParent() ?? unitLayer).AddChild(anim);
        return anim;
    }

    public override void _Ready()
    {
        Position = _from;
        _sprite = new Sprite2D { Centered = true };
        if (_texture is not null)
        {
            _sprite.Texture = _texture;
            // Feet-on-tile sprites are drawn with their base at the tile centre; nudge so the sprite sits over it
            // (mirrors CombatAnimation's offset for the lunging sprite).
            _sprite.Offset = new Vector2(0f, -_texture.GetSize().Y / 2f + 6f);
        }
        else
        {
            _sprite.Texture = CombatAnimation.DiscTexture(); // the same red-disc stand-in the marker uses for art-less units
        }
        AddChild(_sprite);

        Tween tween = CreateTween();
        tween.TweenProperty(this, "position", _to, _duration)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        tween.TweenCallback(Callable.From(QueueFree));
    }

    /// <summary>True when Godot is running without a real windowing display (a fully headless run) — slide is pointless then.</summary>
    private static bool IsHeadless() => DisplayServer.GetName() == "headless";
}
