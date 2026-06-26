using CrownAndColony.GameLogic.Combat;
using CrownAndColony.GameLogic.World;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// A short, non-blocking attack animation played over the map when a combat resolves (`86d3drn72`). It is the
/// presentation-only visual layer over the already-resolved combat (ADR-006: <c>Game.Attack*</c> decides the outcome;
/// this only shows it). The motion is <em>procedural</em> — a transient sprite lunges from the attacker's tile toward
/// the defender's and snaps back, with a flash — so it needs <b>no new art</b>. This mirrors the shape of FreeCol's
/// <c>animation.Animations.unitAttack</c>, which moves the attacker's sprite toward the defender; FreeCol's per-type
/// <c>.sza</c> sprite-sequence frames cover only the soldier/dragoon roles, so the lunge tween is the faithful common
/// denominator (see <see cref="CombatAnimationKind"/>).
/// </summary>
/// <remarks>
/// One-shot: <see cref="Play"/> instantiates a node, parents it to the map's unit layer (so it shares the isometric
/// tile coordinate space), runs a <see cref="Tween"/>, and frees itself on completion. The turn proceeds immediately —
/// nothing awaits it. If the attacker has a sprite it lunges that texture; otherwise a coloured disc stands in (so the
/// red-disc-fallback units still animate). It never touches the shared <see cref="UnitMarker"/> renderer.
/// </remarks>
public partial class CombatAnimation : Node2D
{
    /// <summary>Total duration of the lunge-out + return, in seconds (kept short so it never gates input).</summary>
    public const float DurationSeconds = 0.36f;

    /// <summary>Fraction of the inter-tile distance the attacker lunges (a jab toward the defender, not a full move).</summary>
    private const float LungeFraction = 0.45f;

    private Sprite2D _sprite = null!;
    private Vector2 _home;          // resting position (attacker's tile centre, in unit-layer space)
    private Vector2 _lungeTarget;   // peak of the lunge toward the defender
    private CombatAnimationKind _kind;

    /// <summary>
    /// Spawns and runs a one-shot attack animation for a resolved combat. Non-blocking: returns the node immediately;
    /// the animation plays itself out and frees the node when done.
    /// </summary>
    /// <param name="unitLayer">
    /// The map's unit layer (the caller's <c>_unitLayer</c>), used to locate the isometric coordinate space. The
    /// animation is actually parented to that layer's <em>parent</em> (the <see cref="MapView"/>, the same coordinate
    /// space) so a unit-layer refresh — which frees every child of the layer each <c>SyncUnitMarkers</c> — can't reap
    /// this transient mid-lunge; only the never-wiped <see cref="MapView"/> is touched.
    /// </param>
    /// <param name="attacker">The attacker's tile (where the lunge starts).</param>
    /// <param name="defender">The defender's tile (the lunge aims here).</param>
    /// <param name="kind">The visual flavour, from <see cref="CombatAnimationMap.KindFor"/>.</param>
    /// <param name="texture">The attacker's sprite to lunge, or <c>null</c> to use the coloured-disc stand-in.</param>
    /// <returns>The spawned node (already added to the map layer).</returns>
    public static CombatAnimation Play(Node unitLayer, Position attacker, Position defender,
        CombatAnimationKind kind, Texture2D? texture)
    {
        var anim = new CombatAnimation
        {
            _kind = kind,
            _home = MapView.TileCentre(attacker),
        };
        Vector2 toward = MapView.TileCentre(defender) - anim._home;
        // A loss recoils away from the defender; a hit/evade jabs toward it.
        float dir = kind == CombatAnimationKind.AttackerRepelled ? -0.5f : 1f;
        anim._lungeTarget = anim._home + (toward * (LungeFraction * dir));
        anim._texture = texture;
        // Attach to the durable map layer (the unit layer's parent), not the unit layer itself — see the param note.
        (unitLayer.GetParent() ?? unitLayer).AddChild(anim);
        return anim;
    }

    private Texture2D? _texture;

    public override void _Ready()
    {
        Position = _home;
        _sprite = new Sprite2D { Centered = true };
        if (_texture is not null)
        {
            _sprite.Texture = _texture;
            // Feet-on-tile sprites are drawn with their base at the tile centre; nudge so the disc/sprite sits over it.
            _sprite.Offset = new Vector2(0f, -_texture.GetSize().Y / 2f + 6f);
        }
        AddChild(_sprite);

        // The disc stand-in for units without a sprite (mirrors UnitMarker's red-disc fallback emphasis).
        if (_texture is null)
        {
            _sprite.Texture = DiscTexture();
        }

        RunTween();
    }

    private void RunTween()
    {
        Color flash = _kind switch
        {
            CombatAnimationKind.AttackerHit => new Color(1f, 0.95f, 0.6f),     // warm strike flash
            CombatAnimationKind.AttackerRepelled => new Color(1f, 0.5f, 0.45f), // red recoil flash
            _ => new Color(0.8f, 0.9f, 1f),                                     // cool feint (evade)
        };
        float half = DurationSeconds / 2f;

        Tween tween = CreateTween();
        tween.SetParallel(true);
        // Out: lunge toward (or recoil from) the defender while flashing — both run together.
        tween.TweenProperty(this, "position", _lungeTarget, half)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(_sprite, "modulate", flash, half * 0.5f);
        // Back: snap home and fade the flash out — Chain() starts this group only after the lunge group finishes.
        tween.Chain().TweenProperty(this, "position", _home, half)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        tween.TweenProperty(_sprite, "modulate", Colors.White, half);
        tween.Chain().TweenCallback(Callable.From(QueueFree));
    }

    /// <summary>
    /// A small opaque disc texture for units that have no sprite (matches the on-map red-disc fallback). Shared with the
    /// sibling <see cref="UnitMoveAnimation"/> so an art-less unit slides the same stand-in it lunges with.
    /// </summary>
    internal static Texture2D DiscTexture()
    {
        const int d = (int)(MapView.TileH * 0.60f);
        var img = Image.CreateEmpty(d, d, false, Image.Format.Rgba8);
        float r = d / 2f;
        var col = new Color(0.85f, 0.25f, 0.25f);
        for (int y = 0; y < d; y++)
        {
            for (int x = 0; x < d; x++)
            {
                float dx = x - r + 0.5f, dy = y - r + 0.5f;
                img.SetPixel(x, y, dx * dx + dy * dy <= r * r ? col : new Color(0, 0, 0, 0));
            }
        }
        return ImageTexture.CreateFromImage(img);
    }
}
