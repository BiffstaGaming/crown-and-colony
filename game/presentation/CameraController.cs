using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// Map camera: middle/right-mouse drag to pan, wheel to zoom, arrow keys to pan continuously (86d3f0vqf).
/// </summary>
/// <remarks>
/// Presentation-only (ADR-006): it moves the view, never the game. The <b>right</b>-button drag still pans, but a
/// right-click that did <em>not</em> drag is left unhandled so <see cref="GameController"/> can open its tile context
/// menu on the release (86d3f0vrz) — middle-mouse pan is the drag-free fallback. Arrow keys pan in <see cref="_Process"/>
/// (continuous while held), the step scaled by the current zoom so a screen-space pan feels the same at any zoom level.
/// </remarks>
public partial class CameraController : Camera2D
{
    private const float ZoomStep = 1.1f;
    private const float MinZoom = 0.5f;
    private const float MaxZoom = 3.0f;

    /// <summary>Keyboard-pan speed in world pixels per second (at zoom 1); divided by the zoom so the on-screen speed is constant.</summary>
    private const float KeyboardPanSpeed = 600f;

    /// <summary>The mouse-drag distance (px) past which a right-drag counts as a pan, not a click — so a small wobble still opens the menu.</summary>
    private const float DragThreshold = 4f;

    private bool _dragging;

    /// <summary>True while the right button is held; tracks whether the drag passed <see cref="DragThreshold"/> so the release can tell a pan from a click.</summary>
    private bool _rightHeld;
    private bool _rightDragged;

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
                ApplyZoom(ZoomStep);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
                ApplyZoom(1f / ZoomStep);
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
            case InputEventMouseMotion motion when _dragging:
                if (_rightHeld && motion.Relative.Length() > DragThreshold)
                {
                    _rightDragged = true;
                }
                Position -= motion.Relative / Zoom;
                break;
        }
    }

    public override void _Process(double delta)
    {
        // Arrow-key pan (continuous while held). WASD is deliberately NOT used (W=wait, C=colopedia — GameController hotkeys).
        Vector2 dir = new(
            (Input.IsKeyPressed(Key.Right) ? 1 : 0) - (Input.IsKeyPressed(Key.Left) ? 1 : 0),
            (Input.IsKeyPressed(Key.Down) ? 1 : 0) - (Input.IsKeyPressed(Key.Up) ? 1 : 0));
        if (dir != Vector2.Zero)
        {
            // Divide by zoom so the screen-space pan speed is constant regardless of zoom level.
            Position += dir.Normalized() * (KeyboardPanSpeed * (float)delta / Zoom.X);
        }
    }

    private void ApplyZoom(float factor)
    {
        float target = Mathf.Clamp(Zoom.X * factor, MinZoom, MaxZoom);
        Zoom = new Vector2(target, target);
    }
}
