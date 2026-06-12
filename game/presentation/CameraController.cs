using Godot;

namespace CrownAndColony.Presentation;

/// <summary>Map camera: middle/right-mouse drag to pan, wheel to zoom.</summary>
public partial class CameraController : Camera2D
{
    private const float ZoomStep = 1.1f;
    private const float MinZoom = 0.5f;
    private const float MaxZoom = 3.0f;

    private bool _dragging;

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
            case InputEventMouseButton { ButtonIndex: MouseButton.Middle or MouseButton.Right } drag:
                _dragging = drag.Pressed;
                break;
            case InputEventMouseMotion motion when _dragging:
                Position -= motion.Relative / Zoom;
                break;
        }
    }

    private void ApplyZoom(float factor)
    {
        float target = Mathf.Clamp(Zoom.X * factor, MinZoom, MaxZoom);
        Zoom = new Vector2(target, target);
    }
}
