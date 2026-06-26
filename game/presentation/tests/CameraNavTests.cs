using System.Linq;
using System.Threading.Tasks;
using CrownAndColony.GameLogic.GameSession;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace CrownAndColony.Presentation.Tests;

/// <summary>
/// L3 interaction tests (docs/TESTING.md) for map-navigation QoL (86d3fq22p / 86d3fq1qe / 86d3fq0ch / 86d3fq1nk):
/// keyboard + drag + edge pan move the <see cref="CameraController"/> (clamped to the map), the wheel/keys step the
/// discrete zoom and re-centre on the cursor, the on-screen <see cref="MapControlsOverlay"/> zoom/recentre buttons are
/// reachable real descendants that drive the camera, and hovering a tile shows its yield preview. Presentation-only (ADR-006).
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class CameraNavTests
{
    private const ulong Seed = 424242;

    /// <summary>Releases the arrow keys these cases hold-poll and re-parks the cursor, so neither a held key nor a warped cursor bleeds into the next case.</summary>
    [AfterTest]
    public void ResetInput()
    {
        foreach (Key key in new[] { Key.Left, Key.Right, Key.Up, Key.Down })
        {
            Input.ParseInputEvent(new InputEventKey { Keycode = key, Pressed = false });
        }
        Input.WarpMouse(Vector2.Zero);
        Input.FlushBufferedEvents();
    }

    [TestCase(Timeout = 60000)]
    public async Task ArrowKeyPan_MovesTheCamera_AndClampsWithinTheMapBounds()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);
        var camera = controller.GetNode<CameraController>("Camera");
        camera.EdgeScrollEnabled = false; // isolate the keyboard pan from any edge-scroll contribution

        // Park the camera at the map centre (well inside bounds) so the pan is observable and not pre-clamped.
        camera.CenterOn(MapView.TileCentre(new GameLogic.World.Position(game.Map.Width / 2, game.Map.Height / 2)));
        await runner.SimulateFrames(1);
        Vector2 before = camera.Position;

        // Hold Right for several frames (the pan is polled in _Process), then release.
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Right, Pressed = true });
        await runner.SimulateFrames(10);
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Right, Pressed = false });
        Input.FlushBufferedEvents();

        AssertThat(camera.Position.X > before.X).IsTrue();      // panned east
        AssertThat(Mathf.IsEqualApprox(camera.Position.Y, before.Y)).IsTrue(); // and only east

        // The camera never leaves the map rectangle (the corner-tile extent SetMapBounds was given).
        Vector2 min = MapView.TileCentre(new GameLogic.World.Position(0, 0));
        Vector2 max = MapView.TileCentre(new GameLogic.World.Position(game.Map.Width - 1, game.Map.Height - 1));
        AssertThat(camera.Position.X <= Mathf.Max(min.X, max.X) + 0.01f).IsTrue();
    }

    [TestCase(Timeout = 60000)]
    public async Task MiddleDrag_PansTheCamera_OppositeTheDragDirection()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);
        var camera = controller.GetNode<CameraController>("Camera");
        camera.EdgeScrollEnabled = false; // isolate the drag pan
        camera.CenterOn(MapView.TileCentre(new GameLogic.World.Position(game.Map.Width / 2, game.Map.Height / 2)));
        Vector2 before = camera.Position;

        // Middle-button down, then a motion: dragging the view right (+x relative) pulls the camera left (−x), like
        // grabbing the map and pulling it. Driven directly (deterministic) the same way MapPickingTests drives clicks.
        camera._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Middle, Pressed = true });
        camera._UnhandledInput(new InputEventMouseMotion { Relative = new Vector2(40, 0), Position = camera.GetViewport().GetVisibleRect().Size / 2f });
        camera._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Middle, Pressed = false });
        await runner.SimulateFrames(1);

        AssertThat(camera.Position.X < before.X).IsTrue(); // pulled left as the drag moved right
    }

    [TestCase(Timeout = 60000)]
    public async Task EdgeScroll_WhenTheCursorMovesToTheRightEdge_PansTheCameraEast()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);
        var camera = controller.GetNode<CameraController>("Camera");
        camera.CenterOn(MapView.TileCentre(new GameLogic.World.Position(game.Map.Width / 2, game.Map.Height / 2)));
        await runner.SimulateFrames(1);
        Vector2 before = camera.Position;

        // A real mouse-motion event right at the viewport's right edge arms edge-scroll (a moving cursor, not a parked
        // one), then a few frames of _Process pan the view east. The runner window is focused, so the focus gate passes.
        Rect2 view = camera.GetViewport().GetVisibleRect();
        camera._UnhandledInput(new InputEventMouseMotion { Position = new Vector2(view.End.X - 2f, view.Size.Y / 2f) });
        await runner.SimulateFrames(6);

        AssertThat(camera.Position.X > before.X).IsTrue(); // the right-edge cursor edge-scrolled east
    }

    [TestCase(Timeout = 60000)]
    public async Task WheelZoom_StepsTheDiscreteLevel_ClampedAtTheExtremes()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        var camera = controller.GetNode<CameraController>("Camera");

        float start = camera.ZoomLevel;
        Vector2 anchor = camera.GetViewport().GetVisibleRect().Size / 2f;

        // Wheel up zooms IN one discrete level; wheel down zooms back OUT — the level changes, not by an arbitrary factor.
        camera._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true, Position = anchor });
        await runner.SimulateFrames(1);
        AssertThat(camera.ZoomLevel > start).IsTrue();
        AssertThat(camera.Zoom.X).IsEqualApprox(camera.ZoomLevel, 0.0001f); // the camera Zoom follows the discrete level

        float zoomedIn = camera.ZoomLevel;
        camera._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelDown, Pressed = true, Position = anchor });
        await runner.SimulateFrames(1);
        AssertThat(camera.ZoomLevel < zoomedIn).IsTrue();

        // Zoom all the way out — the level clamps (further wheel-down is a no-op, never below the minimum).
        for (int i = 0; i < 12; i++)
        {
            camera._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelDown, Pressed = true, Position = anchor });
            await runner.SimulateFrames(1);
        }
        float floor = camera.ZoomLevel;
        camera._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelDown, Pressed = true, Position = anchor });
        await runner.SimulateFrames(1);
        AssertThat(camera.ZoomLevel).IsEqualApprox(floor, 0.0001f); // clamped — no change past the minimum level
    }

    [TestCase(Timeout = 60000)]
    public async Task WheelZoom_OffCentreCursor_KeepsTheWorldPointUnderTheCursorFixed()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        var camera = controller.GetNode<CameraController>("Camera");
        camera.EdgeScrollEnabled = false; // this case asserts exact camera positions; edge-scroll is exercised separately

        // An anchor off the view centre: the world point under it must stay put across the zoom (cursor-anchored zoom).
        Vector2 viewSize = camera.GetViewport().GetVisibleRect().Size;
        Vector2 anchor = new(viewSize.X * 0.75f, viewSize.Y * 0.25f);
        Vector2 worldUnderCursorBefore = WorldUnder(camera, anchor);

        camera._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true, Position = anchor });
        await runner.SimulateFrames(1);

        Vector2 worldUnderCursorAfter = WorldUnder(camera, anchor);
        AssertThat(worldUnderCursorBefore.DistanceTo(worldUnderCursorAfter) < 1.0f).IsTrue(); // the cursor stayed over the same world point
        AssertThat(camera.Position).IsNotEqual(Vector2.Zero); // and the camera actually moved to keep it fixed (off-centre zoom)
    }

    [TestCase(Timeout = 60000)]
    public async Task MapControlsOverlay_ButtonsAreRealReachableDescendants_AndDriveTheCamera()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        var camera = controller.GetNode<CameraController>("Camera");
        camera.EdgeScrollEnabled = false; // this case asserts an exact recentre position; edge-scroll is exercised separately

        // The overlay is a real child of the HUD CanvasLayer with three reachable button descendants (the dead-click
        // guard: each button is an actual node with a positive size, not a painted-on label).
        var overlay = controller.GetNode<CanvasLayer>("UI").GetChildren().OfType<MapControlsOverlay>().First();
        foreach (Button b in new[] { overlay.ZoomInButton, overlay.ZoomOutButton, overlay.RecentreButton })
        {
            AssertThat(b).IsNotNull();
            AssertThat(b.IsInsideTree()).IsTrue();
            AssertThat(b.Size.X > 0 && b.Size.Y > 0).IsTrue();
        }

        // The zoom-in button steps the camera zoom up; the zoom-out button steps it back down.
        float start = camera.ZoomLevel;
        overlay.ZoomInButton.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        AssertThat(camera.ZoomLevel > start).IsTrue();

        overlay.ZoomOutButton.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        AssertThat(camera.ZoomLevel).IsEqualApprox(start, 0.0001f);

        // Recentre snaps the camera to the player's focus (the first on-map unit at game start). Move it away first.
        var firstUnit = GameOf(controller).PlayerUnits.First(u => u.IsOnMap);
        camera.CenterOn(Vector2.Zero);
        overlay.RecentreButton.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        AssertThat(camera.Position).IsEqual(MapView.TileCentre(firstUnit.Position));
    }

    [TestCase(Timeout = 60000)]
    public async Task HoveringATile_ShowsItsYieldPreview_InTheTileInfoPanel()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game g = GameOf(controller);
        var camera = controller.GetNode<CameraController>("Camera");
        var mapView = controller.GetNode<MapView>("MapView");

        // Pick an explored land tile that actually offers a colonist yield (so the preview has a "Yield:" line).
        var tile = g.Map.AllPositions().First(p =>
            g.IsExplored(p) && !g.Map.TerrainAt(p).IsWater && g.TileWorkOptions(p).Count > 0);
        string topGood = g.TileWorkOptions(tile)[0].GoodsId;
        string shortGood = topGood[(topGood.LastIndexOf('.') + 1)..];

        // Centre the camera on the tile so its diamond sits at screen-centre, then warp the live cursor there — the real
        // path: MapView._Process polls the cursor, detects the new hovered tile, and raises HoveredTileChanged, which
        // the controller renders as the yield preview.
        camera.CenterOn(MapView.TileCentre(tile));
        await runner.SimulateFrames(1);
        runner.SetMousePos(camera.GetViewport().GetVisibleRect().Size / 2f);
        Input.FlushBufferedEvents();
        await runner.SimulateFrames(2);
        AssertThat(mapView.HoveredTile).IsEqual(tile); // the hover seam picked up the tile under the cursor

        var panel = controller.GetNode<PanelContainer>("UI/TileInfoPanel");
        var label = controller.GetNode<Label>("UI/TileInfoPanel/VBox/Label");
        AssertThat(panel.Visible).IsTrue();
        AssertThat(label.Text).Contains(g.Map.TerrainAt(tile).ShortName); // terrain
        AssertThat(label.Text.ToLower()).Contains("yield");                // the preview line
        AssertThat(label.Text).Contains(shortGood);                        // the best good it can make
    }

    private static Vector2 WorldUnder(CameraController camera, Vector2 screen) =>
        camera.Position + (screen - camera.GetViewport().GetVisibleRect().Size / 2f) / camera.Zoom;

    private static Game GameOf(GameController controller) =>
        (Game)controller.GetType()
            .GetField("_game", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(controller)!;
}
