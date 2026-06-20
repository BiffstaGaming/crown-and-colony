using System.Reflection;
using System.Threading.Tasks;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.Presentation;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace CrownAndColony.Presentation.Tests;

/// <summary>
/// L3 interaction tests (docs/TESTING.md) for the <see cref="MoundsDecisionPanel"/>: a pending strange-mounds
/// decision opens the modal, and Investigate/Decline resolve it via <c>Game.ResolvePendingMounds</c>.
/// Presentation-only (ADR-006). The pending state has no public setter (a human reaches it only by rolling
/// MOUNDS on its own stream), so the test injects it by reflection and drives the real panel + buttons.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class MoundsDecisionPanelTests
{
    [TestCase]
    public async Task PendingMounds_OpensTheModal_AndDeclineResolvesIt()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();
        Game game = GameOf(controller);

        InjectPendingMounds(game, game.Units[0]);
        Refresh(controller); // the next view refresh surfaces the prompt
        await runner.SimulateFrames(1);

        var panel = controller.GetNode<PanelContainer>("UI/MoundsDecisionPanel");
        AssertThat(panel.Visible).IsTrue();

        controller.GetNode<Button>("UI/MoundsDecisionPanel/VBox/Buttons/DeclineButton")
            .EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(panel.Visible).IsFalse();          // resolved → modal hidden
        AssertThat(game.PendingMounds == null).IsTrue(); // and the pending state cleared
    }

    [TestCase]
    public async Task PendingMounds_Investigate_ResolvesTheDecision()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();
        Game game = GameOf(controller);

        InjectPendingMounds(game, game.Units[0]);
        Refresh(controller);
        await runner.SimulateFrames(1);

        controller.GetNode<Button>("UI/MoundsDecisionPanel/VBox/Buttons/InvestigateButton")
            .EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(controller.GetNode<PanelContainer>("UI/MoundsDecisionPanel").Visible).IsFalse();
        AssertThat(game.PendingMounds == null).IsTrue();
    }

    private static void InjectPendingMounds(Game game, Unit unit) =>
        typeof(Game).GetField("_pendingMounds", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(game, new Game.PendingMoundsDecision(unit.Id, unit.Position));

    private static void Refresh(GameController controller) =>
        typeof(GameController).GetMethod("RefreshView", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(controller, null);

    private static Game GameOf(GameController controller) =>
        (Game)controller.GetType()
            .GetField("_game", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(controller)!;
}
