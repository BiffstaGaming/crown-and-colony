using System;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// On-screen map-navigation controls (86d3fq0ch): a small vertical cluster of buttons in the top-right of the HUD —
/// <b>zoom in (+)</b>, <b>zoom out (−)</b>, and a <b>recentre ("N")</b> affordance — the faithful subset of FreeCol's
/// <c>CornerMapControls</c>. Pure presentation (ADR-006): each button drives the <see cref="CameraController"/>; it holds
/// no game state. Built entirely in code (no scene edit) and added to the HUD <c>CanvasLayer</c> by
/// <see cref="GameController"/>.
/// </summary>
/// <remarks>
/// <b>Why no rotate.</b> FreeCol's map-controls cluster includes a compass/rotate ring because its map can be viewed at
/// several rotations. Our view is a fixed top-down isometric projection (<see cref="MapView"/>) that never rotates, so a
/// rotate control would do nothing. The faithful equivalent is a <b>recentre</b> button (the "N"/home affordance) that
/// snaps the camera back to a sensible focus — the same role FreeCol's centre-on-active-unit shortcut serves — so that is
/// what this cluster provides instead of a rotate ring.
/// </remarks>
public partial class MapControlsOverlay : Control
{
    /// <summary>Side length (px) of each square control button.</summary>
    private const int ButtonSize = 32;

    private Button _zoomInButton = null!;
    private Button _zoomOutButton = null!;
    private Button _recentreButton = null!;

    /// <summary>Invoked when the recentre ("N") button is pressed — the host snaps the camera to its current focus (ADR-006).</summary>
    public event Action? RecentreRequested;

    /// <summary>The zoom-in (+) button — exposed so tests can assert it is a real, reachable descendant (the dead-click guard).</summary>
    public Button ZoomInButton => _zoomInButton;

    /// <summary>The zoom-out (−) button — exposed for the dead-click guard.</summary>
    public Button ZoomOutButton => _zoomOutButton;

    /// <summary>The recentre ("N") button — exposed for the dead-click guard.</summary>
    public Button RecentreButton => _recentreButton;

    /// <summary>
    /// Builds the button cluster and wires the zoom buttons to <paramref name="camera"/>. Anchored to the HUD's
    /// top-right corner, clear of the left-edge status/unit panels and the bottom-left minimap. Call once, after the
    /// camera node exists.
    /// </summary>
    public void Build(CameraController camera)
    {
        // Anchor the whole cluster to the top-right of the parent CanvasLayer/Control, growing down-left so it never
        // overlaps the top-left status readout or the bottom-left minimap.
        AnchorLeft = 1f;
        AnchorRight = 1f;
        OffsetLeft = -(ButtonSize + 16);
        OffsetTop = 96f; // below the top-right HUD buttons (Europe/Reports/...)
        OffsetRight = -8f;
        GrowHorizontal = GrowDirection.Begin;
        MouseFilter = MouseFilterEnum.Ignore; // the container itself is click-through; only the buttons take clicks
        Theme = ColonyTheme.Get(); // wood-styled buttons matching the rest of the HUD, not Godot's default gray

        // A parchment backing so the zoom/recentre cluster reads as a framed HUD corner, not floating buttons.
        var back = new PanelContainer { Name = "Back" };
        back.AddThemeStyleboxOverride("panel", ColonyArt.ParchmentSkin(6));
        AddChild(back);

        var column = new VBoxContainer { Name = "ControlsColumn" };
        column.AddThemeConstantOverride("separation", 4);
        back.AddChild(column);

        _zoomInButton = MakeButton("ZoomInButton", "+", "Zoom in");
        _zoomOutButton = MakeButton("ZoomOutButton", "−", "Zoom out"); // a real minus sign, not a hyphen
        _recentreButton = MakeButton("RecentreButton", "N", "Recentre the map");
        column.AddChild(_zoomInButton);
        column.AddChild(_zoomOutButton);
        column.AddChild(_recentreButton);

        _zoomInButton.Pressed += camera.ZoomInStep;
        _zoomOutButton.Pressed += camera.ZoomOutStep;
        _recentreButton.Pressed += () => RecentreRequested?.Invoke();
    }

    private static Button MakeButton(string name, string text, string tooltip) => new()
    {
        Name = name,
        Text = text,
        TooltipText = tooltip,
        CustomMinimumSize = new Vector2(ButtonSize, ButtonSize),
        FocusMode = FocusModeEnum.None, // a HUD control: clicking it must not steal keyboard focus from the map
    };
}
