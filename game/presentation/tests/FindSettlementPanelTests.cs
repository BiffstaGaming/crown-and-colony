using System.Reflection;
using System.Threading.Tasks;
using CrownAndColony.Presentation;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace CrownAndColony.Presentation.Tests;

/// <summary>
/// L3 interaction tests (docs/TESTING.md) for the <see cref="FindSettlementPanel"/>: it lists the human's colonies
/// and recenters the main camera on the chosen one. Presentation-only (ADR-006).
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class FindSettlementPanelTests
{
    [TestCase]
    public async Task FindSettlement_ListsColonies_AndRecentersOnPick()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();
        var camera = controller.GetNode<Camera2D>("Camera");

        // Found a colony, move the camera away, then locate it via the dialog.
        var game = GetGame(controller);
        var colony = game.FoundColony(game.Units[0]);
        camera.Position = new Vector2(99999, 99999);

        controller.OpenFindSettlementPanel();
        await runner.SimulateFrames(1);

        var panel = controller.GetNode<PanelContainer>("UI/FindSettlementPanel");
        AssertThat(panel.Visible).IsTrue();

        var button = controller.GetNode<Button>($"UI/FindSettlementPanel/VBox/Dynamic/Find_{colony.Id}");
        AssertThat(button.Text).Contains(colony.Name);

        button.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(camera.Position).IsEqual(MapView.TileCentre(colony.Position)); // recentered on the colony
        AssertThat(panel.Visible).IsFalse();                                      // and the dialog closed
    }

    private static CrownAndColony.GameLogic.GameSession.Game GetGame(GameController controller) =>
        (CrownAndColony.GameLogic.GameSession.Game)controller
            .GetType()
            .GetField("_game", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(controller)!;
}
