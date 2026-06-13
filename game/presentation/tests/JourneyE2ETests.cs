using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.World;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace CrownAndColony.Presentation.Tests;

/// <summary>
/// The single representative scene-level end-to-end journey (docs/TEST-PLAN.md
/// Journey 5): one connected flow through the real Godot scene — new game →
/// select → move → found → open the colony panel → staff a building → end turn →
/// see production. Proves the UI→logic seam holds across multiple steps, which the
/// logic-level journeys (run on the Game API directly) cannot see.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class JourneyE2ETests
{
    private const ulong Seed = 424242;

    [TestCase(Timeout = 90000)]
    public async Task FullFlow_NewGame_Select_Move_Found_Staff_Tick()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);

        // ── select the starting unit by clicking its tile ──
        var unit = game.Units[0];
        await ClickTile(runner, controller, unit.Position);
        var marker = controller.GetNode<UnitMarker>("MapView/UnitMarker");
        AssertThat(marker.Selected).IsTrue();

        // ── move it to a legal neighbour (guarded) ──
        Position? step = unit.Position.Neighbours()
            .Where(n => game.CheckMove(unit, n).Allowed)
            .Cast<Position?>()
            .FirstOrDefault();
        if (step is not null)
        {
            await ClickTile(runner, controller, step.Value);
            AssertThat(unit.Position).IsEqual(step.Value);
        }

        // ── found a colony with the B key ──
        runner.SimulateKeyPressed(Key.B);
        await runner.SimulateFrames(2);
        AssertThat(game.Colonies.Count).IsEqual(1);
        AssertThat(game.Units.Count).IsEqual(0);
        var colony = game.Colonies[0];

        // ── open the colony panel by clicking the colony's tile ──
        await ClickTile(runner, controller, colony.Position);
        var panel = controller.GetNode<PanelContainer>("UI/ColonyPanel");
        AssertThat(panel.Visible).IsTrue();

        // ── free the auto-assigned field worker (if any), then staff the town hall ──
        Button? release = FindButton(panel, "Release_");
        if (release is not null)
        {
            release.EmitSignal(BaseButton.SignalName.Pressed);
            await runner.SimulateFrames(1);
        }
        Button? staff = FindButton(panel, "Staff_townHall");
        AssertThat(staff).IsNotNull();
        staff!.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        AssertThat(colony.BuildingWorkers.GetValueOrDefault("model.building.townHall")).IsEqual(1);

        // ── end the turn and see the staffed town hall's production reach liberty ──
        int libertyBefore = game.Liberty;
        controller.GetNode<Button>("UI/EndTurnButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        // Town hall: 1 unattended + 3 from the one worker = 4 bells → +4 liberty.
        AssertThat(game.Liberty - libertyBefore).IsEqual(4);
    }

    private static async Task ClickTile(ISceneRunner runner, GameController controller, Position tile)
    {
        var camera = controller.GetNode<Camera2D>("Camera");
        Vector2 viewport = controller.GetViewport().GetVisibleRect().Size;
        Vector2 screen = MapView.TileCentre(tile) - camera.Position + viewport / 2f;
        runner.SetMousePos(screen);
        await runner.SimulateFrames(1);
        runner.SimulateMouseButtonPressed(MouseButton.Left);
        await runner.SimulateFrames(2);
    }

    private static Button? FindButton(Node root, string namePrefix) =>
        root.FindChildren("*", recursive: true, owned: false)
            .OfType<Button>()
            .FirstOrDefault(b => b.Name.ToString().StartsWith(namePrefix));

    private static Game GameOf(GameController controller) =>
        (Game)controller.GetType()
            .GetField("_game", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(controller)!;
}
