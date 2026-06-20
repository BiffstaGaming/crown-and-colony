using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.Presentation;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace CrownAndColony.Presentation.Tests;

/// <summary>
/// L3 interaction tests (docs/TESTING.md) for the <see cref="EmigrationChoicePanel"/> (FreeCol's <c>selectRecruit</c>):
/// a pending emigration choice opens the modal with one button per dock recruit, and choosing one resolves it via
/// <c>Game.ChooseEmigrant</c>. Presentation-only (ADR-006). The pending state has no public setter (a human reaches
/// it only via William Brewster through a turn), so the test injects it by reflection and drives the real panel.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class EmigrationChoicePanelTests
{
    [TestCase]
    public async Task PendingEmigration_OpensTheModal_AndChoosingARecruitResolvesIt()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();
        Game game = GameOf(controller);

        // Inject a pending choice over the live recruit dock (what the engine would set under Brewster).
        InjectPendingEmigration(game, new PendingEmigrationChoice(game.HumanPlayer.PlayerId, new List<string>(game.RecruitDock)));
        Refresh(controller); // the next view refresh surfaces the prompt
        await runner.SimulateFrames(1);

        var panel = controller.GetNode<PanelContainer>("UI/EmigrationChoicePanel");
        AssertThat(panel.Visible).IsTrue();

        // One button per recruit slot; the first is named Choose_0.
        var firstChoice = controller.GetNodeOrNull<Button>("UI/EmigrationChoicePanel/VBox/Dynamic/Choose_0");
        AssertThat(firstChoice).IsNotNull();

        firstChoice!.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(panel.Visible).IsFalse();                 // one emigrant due → resolved → modal hidden
        AssertThat(game.PendingEmigration == null).IsTrue(); // and the pending state cleared
    }

    private static void InjectPendingEmigration(Game game, PendingEmigrationChoice choice) =>
        typeof(Game).GetField("_pendingEmigration", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(game, choice);

    private static void Refresh(GameController controller) =>
        typeof(GameController).GetMethod("RefreshView", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(controller, null);

    private static Game GameOf(GameController controller) =>
        (Game)controller.GetType()
            .GetField("_game", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(controller)!;
}
