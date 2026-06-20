using System.Reflection;
using System.Threading.Tasks;
using CrownAndColony.GameLogic.World;
using CrownAndColony.Presentation;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace CrownAndColony.Presentation.Tests;

/// <summary>
/// L3 interaction tests (docs/TESTING.md) for the corner <see cref="MiniMap"/>: it loads inside the real main
/// scene, recenters the main camera on a click, and zooms via the +/- buttons. Presentation-only (ADR-006).
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class MiniMapTests
{
    [TestCase]
    public async Task MiniMap_Loads_InTheMainScene_WithAPositiveSize()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);

        var mini = runner.Scene().GetNode<MiniMap>("UI/MiniMap");
        AssertThat(mini).IsNotNull();
        AssertThat(mini.Size.X > 0 && mini.Size.Y > 0).IsTrue();
    }

    [TestCase]
    public async Task MiniMap_ClickingCentre_RecentersTheCameraOnTheMapCentre()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();
        var mini = controller.GetNode<MiniMap>("UI/MiniMap");
        var camera = controller.GetNode<Camera2D>("Camera");

        // Clicking the minimap's centre maps to the map's centre tile regardless of zoom/size: the centred
        // offset cancels, leaving tile = (Width/2, Height/2). The camera must jump there.
        var game = GetGame(controller);
        Vector2 expected = MapView.TileCentre(new Position(game.Map.Width / 2, game.Map.Height / 2));

        var click = new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = true,
            Position = mini.Size / 2f,
        };
        mini._GuiInput(click);
        await runner.SimulateFrames(1);

        AssertThat(camera.Position).IsEqual(expected);
    }

    [TestCase]
    public async Task MiniMap_ZoomButtons_ChangeThePixelsPerTile()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();
        var mini = controller.GetNode<MiniMap>("UI/MiniMap");

        int start = mini.PixelsPerTile;
        controller.GetNode<Button>("UI/MiniMap/ZoomInButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        AssertThat(mini.PixelsPerTile > start).IsTrue(); // zoomed in → larger tiles

        int zoomed = mini.PixelsPerTile;
        controller.GetNode<Button>("UI/MiniMap/ZoomOutButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        AssertThat(mini.PixelsPerTile < zoomed).IsTrue(); // zoomed back out → smaller tiles
    }

    private static CrownAndColony.GameLogic.GameSession.Game GetGame(GameController controller) =>
        (CrownAndColony.GameLogic.GameSession.Game)controller
            .GetType()
            .GetField("_game", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(controller)!;
}
