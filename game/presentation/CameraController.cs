using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// Map camera (86d3f0vqf / 86d3fq22p / 86d3fq1qe): middle/right-mouse drag to pan, the cursor near a screen edge
/// edge-scrolls, arrow keys pan continuously, and the wheel (or +/- keys, or the on-screen
/// <see cref="MapControlsOverlay"/> buttons) steps between a few discrete, clamped zoom levels — the Col1/FreeCol
/// map-navigation staples.
/// </summary>
/// <remarks>
/// Presentation-only (ADR-006): it moves the view, never the game. The <b>right</b>-button drag still pans, but a
/// right-click that did <em>not</em> drag is left unhandled so <see cref="GameController"/> can open its tile context
/// menu on the release (86d3f0vrz) — middle-mouse pan is the drag-free fallback. Arrow keys pan in <see cref="_Process"/>
/// (continuous while held), the step scaled by the current zoom so a screen-space pan feels the same at any zoom level.
/// <para>
/// <b>Zoom is discrete</b> (<see cref="ZoomLevels"/>): the wheel/keys/buttons step to the next preset level rather than
/// scaling continuously, and the zoom <b>re-centres on the cursor</b> (the world point under the mouse stays put) so
/// zooming in homes on what you are pointing at — FreeCol's wheel-zoom behaviour. With no live cursor (the wheel arrives
/// with a position, the keys/buttons do not) it re-centres on the view centre, leaving the camera position unchanged.
/// </para>
/// <para>
/// <b>Bounds:</b> once <see cref="SetMapBounds"/> has been given the map's pixel extent, panning and edge-scroll are
/// clamped so the camera centre never leaves the map rectangle (a soft clamp — the view can still show some margin past
/// the edge, but the camera will not run off into empty space). Before bounds are set (or in a pure-math test that never
/// calls it) the camera pans freely, preserving the old behaviour.
/// </para>
/// </remarks>
public partial class CameraController : Camera2D
{
    /// <summary>The discrete zoom levels (camera <c>Zoom</c> scalar), most-zoomed-out first. The wheel/keys/buttons step between adjacent entries; 1.0 is the default. The two far-out levels (0.125/0.25) let a tall map — e.g. the 40×180 America strip — zoom right out to show the whole continent.</summary>
    private static readonly float[] ZoomLevels = [0.125f, 0.25f, 0.5f, 0.75f, 1.0f, 1.5f, 2.0f, 3.0f];

    /// <summary>The index into <see cref="ZoomLevels"/> of the default (1.0×) level a fresh camera starts at.</summary>
    private const int DefaultZoomIndex = 4;

    /// <summary>Keyboard-pan speed in world pixels per second (at zoom 1); divided by the zoom so the on-screen speed is constant.</summary>
    private const float KeyboardPanSpeed = 600f;

    /// <summary>Edge-scroll speed in world pixels per second (at zoom 1); divided by the zoom so the on-screen speed is constant.</summary>
    private const float EdgeScrollSpeed = 700f;

    /// <summary>How close (in screen pixels) the cursor must get to a viewport edge before edge-scroll kicks in.</summary>
    private const float EdgeScrollMargin = 24f;

    /// <summary>The mouse-drag distance (px) past which a right-drag counts as a pan, not a click — so a small wobble still opens the menu.</summary>
    private const float DragThreshold = 4f;

    private int _zoomIndex = DefaultZoomIndex;
    private bool _dragging;

    /// <summary>True while the right button is held; tracks whether the drag passed <see cref="DragThreshold"/> so the release can tell a pan from a click.</summary>
    private bool _rightHeld;
    private bool _rightDragged;

    /// <summary>The map's pixel extent (the rectangle the camera centre is clamped to). Null until <see cref="SetMapBounds"/> is called — then the camera pans freely.</summary>
    private Rect2? _mapBounds;

    /// <summary>The latest real mouse-motion position (the edge-scroll probe point) and whether the pointer has actually moved this session — edge-scroll acts only on a moving cursor, never a parked/warped one.</summary>
    private Vector2 _lastMotionPos;
    private bool _mouseHasMoved;

    /// <summary>
    /// Master toggle for cursor edge-scroll (default on). Independent of the focus gate below — set false to suppress
    /// edge-scroll entirely (e.g. an L3 case that pans by other means and parks a test cursor in a corner).
    /// </summary>
    public bool EdgeScrollEnabled { get; set; } = true;

    /// <summary>The current discrete zoom level's scalar (1.0 = default). Exposed for the overlay/tests.</summary>
    public float ZoomLevel => ZoomLevels[_zoomIndex];

    /// <summary>
    /// Master gate for all direct camera input — wheel zoom, drag/arrow/edge panning, +/- keys. Set false by
    /// <c>GameController.RefreshHudButtonVisibility</c> while a full-screen panel (colony, Europe, reports…) is open so
    /// the map behind the panel doesn't zoom/pan under stray input (Chris 2026-07-08); true again when it closes.
    /// Programmatic moves (<see cref="CenterOn"/>, <see cref="ZoomInStep"/>…) are unaffected.
    /// </summary>
    public bool InputEnabled { get; set; } = true;

    public override void _Ready() => Zoom = new Vector2(ZoomLevels[_zoomIndex], ZoomLevels[_zoomIndex]);

    /// <summary>
    /// Gives the camera the map's pixel extent so panning/edge-scroll clamp the camera centre inside it (ADR-006 — a
    /// view constraint, not a rule). <paramref name="topLeft"/>/<paramref name="bottomRight"/> are the diamond centres of
    /// the map's corner tiles; the clamp keeps the camera within that rectangle. Re-clamps the current position at once.
    /// </summary>
    public void SetMapBounds(Vector2 topLeft, Vector2 bottomRight)
    {
        Vector2 min = new(Mathf.Min(topLeft.X, bottomRight.X), Mathf.Min(topLeft.Y, bottomRight.Y));
        Vector2 max = new(Mathf.Max(topLeft.X, bottomRight.X), Mathf.Max(topLeft.Y, bottomRight.Y));
        _mapBounds = new Rect2(min, max - min);
        _mouseHasMoved = false; // a new game: edge-scroll stays inert until the player actually moves the pointer
        Position = ClampToBounds(Position);
    }

    /// <summary>Steps in one discrete zoom level (re-centred on the view centre), for the on-screen zoom-in button / + key.</summary>
    public void ZoomInStep() => StepZoom(+1, null);

    /// <summary>Steps out one discrete zoom level (re-centred on the view centre), for the on-screen zoom-out button / - key.</summary>
    public void ZoomOutStep() => StepZoom(-1, null);

    /// <summary>Centres the camera on a world point and clamps it to the map bounds — the overlay's recentre/"north" affordance and the minimap/selection recentre target.</summary>
    public void CenterOn(Vector2 worldPoint) => Position = ClampToBounds(worldPoint);

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!InputEnabled)
        {
            return; // a full-screen panel is open — the map must not react underneath it
        }
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true } wheelUp:
                StepZoom(+1, wheelUp.Position); // zoom IN toward the cursor
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true } wheelDown:
                StepZoom(-1, wheelDown.Position); // zoom OUT from the cursor
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Middle } middle:
                _dragging = middle.Pressed;
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true }:
                // Right press: start a (possible) drag-pan; reset the drag flag so a release with no movement is a click.
                _dragging = true;
                _rightHeld = true;
                _rightDragged = false;
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: false }:
                _dragging = false;
                _rightHeld = false;
                // A genuine drag-pan consumes the release so GameController doesn't also open the tile menu; a click
                // (no drag) is left unhandled so the menu opens on the release (86d3f0vrz).
                if (_rightDragged)
                {
                    GetViewport().SetInputAsHandled();
                }
                break;
            case InputEventMouseMotion motion:
                // Track real pointer motion so edge-scroll only acts on a genuinely-moving cursor (a parked/warped cursor
                // generates no motion and so never edge-scrolls — this is also what keeps the polling inert in the L3/L4
                // test runner, whose cursor is warped, not moved). The latest motion position is the edge-scroll probe point.
                _lastMotionPos = motion.Position;
                _mouseHasMoved = true;
                if (_dragging)
                {
                    if (_rightHeld && motion.Relative.Length() > DragThreshold)
                    {
                        _rightDragged = true;
                    }
                    Position = ClampToBounds(Position - motion.Relative / Zoom);
                }
                break;
        }
    }

    public override void _Process(double delta)
    {
        if (!InputEnabled)
        {
            return; // a full-screen panel is open — no keyboard/edge panning underneath it
        }
        // Arrow-key pan (continuous while held). WASD is deliberately NOT used for panning: W = wait/next-unit,
        // S/D/C etc. are GameController hotkeys (skip/disband/colopedia) — binding them to the camera too would
        // double-fire. Arrow keys are the unambiguous, Col1/FreeCol-standard map-pan keys.
        Vector2 dir = new(
            (Input.IsKeyPressed(Key.Right) ? 1 : 0) - (Input.IsKeyPressed(Key.Left) ? 1 : 0),
            (Input.IsKeyPressed(Key.Down) ? 1 : 0) - (Input.IsKeyPressed(Key.Up) ? 1 : 0));

        // +/- (and the keypad pair) step the discrete zoom from the keyboard. Edge-triggered via _UnhandledInput would
        // also work, but polling here keeps all camera-nav input in one place; a one-shot guard stops key-repeat spam.
        HandleZoomKeys();

        // Edge-scroll: when the cursor nears a viewport edge, pan in that direction (added to any arrow input).
        dir += EdgeScrollDirection();

        if (dir != Vector2.Zero)
        {
            // Divide by zoom so the screen-space pan speed is constant regardless of zoom level.
            float speed = KeyboardPanSpeed;
            Position = ClampToBounds(Position + dir.Normalized() * (speed * (float)delta / Zoom.X));
        }
    }

    private bool _zoomKeyDown;

    private void HandleZoomKeys()
    {
        bool inPressed = Input.IsKeyPressed(Key.Equal) || Input.IsKeyPressed(Key.Plus) || Input.IsKeyPressed(Key.KpAdd);
        bool outPressed = Input.IsKeyPressed(Key.Minus) || Input.IsKeyPressed(Key.KpSubtract);
        if (inPressed && !_zoomKeyDown)
        {
            StepZoom(+1, null);
        }
        else if (outPressed && !_zoomKeyDown)
        {
            StepZoom(-1, null);
        }
        _zoomKeyDown = inPressed || outPressed; // one zoom step per key press (no key-repeat spam)
    }

    /// <summary>The unit edge-scroll direction from the latest mouse-motion position, or <see cref="Vector2.Zero"/> when disabled, the pointer has not moved this session, the window is unfocused, or the cursor is not near an edge / outside the view.</summary>
    private Vector2 EdgeScrollDirection()
    {
        // Edge-scroll acts only on a genuinely moving cursor in the focused window: a parked/warped pointer never moves
        // (so _mouseHasMoved stays false) and the map must not scroll when the game is backgrounded. Together these keep
        // edge-scroll inert in the L3/L4 test runner (cursor warped, not moved) so camera-position assertions don't drift.
        if (!EdgeScrollEnabled || !_mouseHasMoved || !GetWindow().HasFocus())
        {
            return Vector2.Zero;
        }
        Rect2 view = GetViewport().GetVisibleRect();
        Vector2 mouse = _lastMotionPos;
        if (!view.HasPoint(mouse))
        {
            return Vector2.Zero; // cursor left the window — don't keep scrolling
        }
        // Scale by EdgeScrollSpeed/KeyboardPanSpeed so the normalised vector in _Process still moves at edge-scroll speed.
        float ratio = EdgeScrollSpeed / KeyboardPanSpeed;
        return new Vector2(
            mouse.X < view.Position.X + EdgeScrollMargin ? -ratio : mouse.X > view.End.X - EdgeScrollMargin ? ratio : 0f,
            mouse.Y < view.Position.Y + EdgeScrollMargin ? -ratio : mouse.Y > view.End.Y - EdgeScrollMargin ? ratio : 0f);
    }

    /// <summary>
    /// Steps the discrete zoom by <paramref name="direction"/> (+1 in / −1 out), clamped to <see cref="ZoomLevels"/>. When a
    /// <paramref name="screenAnchor"/> is given (the wheel's cursor position) the world point under it is kept fixed — the
    /// view zooms toward the cursor; with none (keys/buttons) it zooms about the view centre (Position unchanged).
    /// </summary>
    private void StepZoom(int direction, Vector2? screenAnchor)
    {
        int next = Mathf.Clamp(_zoomIndex + direction, 0, ZoomLevels.Length - 1);
        if (next == _zoomIndex)
        {
            return; // already at the clamp — no change, no re-centre
        }

        // The world point under the anchor before the zoom; after the zoom we move the camera so that same world point
        // projects back to the same screen point (cursor-anchored zoom). With no anchor this is the view centre, so the
        // camera position does not move.
        Vector2 anchor = screenAnchor ?? GetViewport().GetVisibleRect().Size / 2f;
        Vector2 worldBefore = ScreenToWorld(anchor);

        _zoomIndex = next;
        float z = ZoomLevels[_zoomIndex];
        Zoom = new Vector2(z, z);

        Vector2 worldAfter = ScreenToWorld(anchor);
        Position = ClampToBounds(Position + (worldBefore - worldAfter));
    }

    /// <summary>The world point a viewport (screen) point projects to under the current camera (an offset from the camera centre, scaled by the inverse zoom).</summary>
    private Vector2 ScreenToWorld(Vector2 screen) =>
        Position + (screen - GetViewport().GetVisibleRect().Size / 2f) / Zoom;

    /// <summary>Clamps a candidate camera centre to the map bounds when set; a no-op (identity) before <see cref="SetMapBounds"/>.</summary>
    private Vector2 ClampToBounds(Vector2 candidate)
    {
        if (_mapBounds is not { } b)
        {
            return candidate;
        }
        return new Vector2(
            Mathf.Clamp(candidate.X, b.Position.X, b.End.X),
            Mathf.Clamp(candidate.Y, b.Position.Y, b.End.Y));
    }
}
