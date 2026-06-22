using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using CrownAndColony.Presentation;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace CrownAndColony.Presentation.Tests;

/// <summary>
/// L3 interaction tests (docs/TESTING.md) for the in-session <see cref="MessageLogPanel"/> (`86d3e4buh`): the
/// Messages button opens the log, an empty history shows the empty-state line, and an accumulated history renders one
/// "Turn N" section per logged turn with its notices. Presentation-only (ADR-006); the history is the
/// <see cref="GameController"/>'s session-only <c>_messageLog</c> (never serialized), seeded here by reflection —
/// exactly the seam the other panel L3 tests use — so the test does not depend on a particular turn's RNG events.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class MessageLogPanelTests
{
    [TestCase]
    public async Task MessagesButton_OpensTheLog_EmptyByDefault()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        controller.GetNode<Button>("UI/MessageLogButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        var panel = controller.GetNode<PanelContainer>("UI/MessageLogPanel");
        AssertThat(panel.Visible).IsTrue();
        // A fresh game has logged nothing yet → the empty-state line renders.
        var dynamic = controller.GetNode<VBoxContainer>("UI/MessageLogPanel/VBox/Scroll/Dynamic");
        AssertThat(dynamic.GetNodeOrNull("LogEmpty")).IsNotNull();

        controller.GetNode<Button>("UI/MessageLogPanel/VBox/CloseButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        AssertThat(panel.Visible).IsFalse();
    }

    [TestCase]
    public async Task Log_WithHistory_RendersATurnSectionPerLoggedTurn()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        // Seed the session log with two turns of notices (the seam the controller fills after each End Turn).
        var log = MessageLogOf(controller);
        log.Add(new MessageLogPanel.Entry(3, new List<string> { "A privateer sank your Caravel at (4,5)!" }));
        log.Add(new MessageLogPanel.Entry(7, new List<string> { "The Crown lowered your tax rate to 18%." }));

        controller.OpenMessageLogPanel();
        await runner.SimulateFrames(1);

        var dynamic = controller.GetNode<VBoxContainer>("UI/MessageLogPanel/VBox/Scroll/Dynamic");
        AssertThat(dynamic.GetNodeOrNull("LogTurn_3")).IsNotNull();
        AssertThat(dynamic.GetNodeOrNull("LogTurn_7")).IsNotNull();
        AssertThat(dynamic.GetNodeOrNull("LogEmpty")).IsNull(); // not the empty state once there is history
    }

    private static List<MessageLogPanel.Entry> MessageLogOf(GameController controller) =>
        (List<MessageLogPanel.Entry>)controller.GetType()
            .GetField("_messageLog", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(controller)!;
}
